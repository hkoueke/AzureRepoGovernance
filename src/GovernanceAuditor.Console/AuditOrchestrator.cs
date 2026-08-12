using System.Diagnostics;
using System.Runtime.InteropServices;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernanceAuditor.Console;

/// <summary>Avancement de l'analyse, publié au fil de l'eau.</summary>
/// <param name="Completed">Nombre de dépôts traités (succès + échecs).</param>
/// <param name="Total">Nombre total de dépôts à traiter.</param>
/// <param name="Failed">Nombre de dépôts en échec de collecte.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AuditProgress(int Completed, int Total, int Failed);

/// <summary>
/// Enchaîne collecte et analyse pour l'ensemble des dépôts, avec un parallélisme
/// borné et une isolation stricte par dépôt.
/// </summary>
internal sealed class AuditOrchestrator
{
    private readonly IAzureDevOpsClient _client;
    private readonly IReadOnlyList<IRepositoryAnalyzer> _analyzers;
    private readonly RulesOptions _rules;
    private readonly ExecutionOptions _execution;
    private readonly ILogger<AuditOrchestrator> _logger;

    public AuditOrchestrator(
        IAzureDevOpsClient client,
        IEnumerable<IRepositoryAnalyzer> analyzers,
        IOptions<RulesOptions> rules,
        IOptions<ExecutionOptions> execution,
        ILogger<AuditOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(analyzers);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _analyzers = analyzers.ToList();
        _rules = rules.Value;
        _execution = execution.Value;
        _logger = logger;
    }

    /// <summary>Exécute l'audit complet et renvoie le résultat consolidé.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<En attente>")]
    public async Task<AuditRunResult> RunAsync(IProgress<AuditProgress>? progress, CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();

        var repositories = await _client.GetRepositoriesAsync(cancellationToken).ConfigureAwait(false);
        Log.RepositoriesDiscovered(_logger, repositories.Count);

        var findings = new List<AuditFinding>();
        var errors = new List<CollectionError>();
        Lock gate = new();
        var analyzed = 0;
        var failed = 0;

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, _execution.MaxDegreeOfParallelism),
            CancellationToken = cancellationToken,
        };

        await Parallel.ForEachAsync(repositories, parallelOptions, async (repository, token) =>
        {
            try
            {
                var context = await BuildContextAsync(repository, token).ConfigureAwait(false);
                var repositoryFindings = await AnalyzeAsync(context, token).ConfigureAwait(false);

                lock (gate)
                {
                    findings.AddRange(repositoryFindings);
                    analyzed++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;   // L'annulation n'est pas une erreur de dépôt : elle remonte.
            }
#pragma warning disable CA1031, S2221
            // Isolation volontaire : un dépôt en erreur (droits, API, données inattendues)
            // ne doit jamais interrompre l'analyse des autres. L'erreur est tracée et
            // reportée dans le rapport final.
            catch (Exception exception)
#pragma warning restore CA1031, S2221
            {
                Log.RepositoryFailed(_logger, repository.Name, exception);

                lock (gate)
                {
                    errors.Add(new CollectionError
                    {
                        Repository = repository.Name,
                        Message = Describe(exception),
                    });
                    failed++;
                }
            }
            finally
            {
                if (progress is not null)
                {
                    int done, currentFailed;
                    lock (gate)
                    {
                        done = analyzed + failed;
                        currentFailed = failed;
                    }

                    progress.Report(new AuditProgress(done, repositories.Count, currentFailed));
                }
            }
        }).ConfigureAwait(false);

        Log.AnalysisCompleted(_logger, analyzed, failed);

        return new AuditRunResult
        {
            Findings = Sort(findings),
            Errors = errors.OrderBy(e => e.Repository, StringComparer.OrdinalIgnoreCase).ToList(),
            RepositoriesAnalyzed = analyzed,
            RepositoriesFailed = failed,
            Duration = Stopwatch.GetElapsedTime(startedAt),
        };
    }

    private async Task<RepositoryContext> BuildContextAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        // Séquentiel volontairement : le parallélisme est déjà appliqué au niveau
        // des dépôts, inutile de multiplier la charge sur le serveur.
        var branches = await _client.GetBranchesAsync(repository, cancellationToken).ConfigureAwait(false);
        var pullRequests = await _client.GetPullRequestsAsync(repository, cancellationToken).ConfigureAwait(false);
        var pipelines = await _client.GetPipelinesAsync(repository, cancellationToken).ConfigureAwait(false);
        var policies = await _client.GetPoliciesAsync(repository, cancellationToken).ConfigureAwait(false);

        return new RepositoryContext
        {
            Repository = repository,
            Branches = branches,
            PullRequests = pullRequests,
            Pipelines = pipelines,
            Policies = policies,
            Rules = _rules,
        };
    }

    private async Task<List<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken)
    {
        var results = new List<AuditFinding>();

        foreach (var analyzer in _analyzers)
        {
            var produced = await analyzer.AnalyzeAsync(context, cancellationToken).ConfigureAwait(false);
            results.AddRange(produced);
        }

        return results;
    }

    /// <summary>Tri stable : le rapport doit être reproductible d'une exécution à l'autre.</summary>
    private static List<AuditFinding> Sort(List<AuditFinding> findings) =>
        findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Repository, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Message, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Décrit une exception de façon compacte pour le rapport, sans divulguer de
    /// trace d'exécution (la trace complète reste dans le journal).
    /// </summary>
    private static string Describe(Exception exception)
    {
        var message = exception.Message.ReplaceLineEndings(" ").Trim();
        return $"{exception.GetType().Name} : {message}";
    }
}
