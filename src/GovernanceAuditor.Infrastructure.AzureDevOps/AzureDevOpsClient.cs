using System.Collections.Concurrent;
using System.Net;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;
using GovernanceAuditor.Infrastructure.AzureDevOps.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernanceAuditor.Infrastructure.AzureDevOps;

/// <summary>
/// Implémentation en lecture seule de <see cref="IAzureDevOpsClient"/> au-dessus de
/// l'API REST 7.1. Interne : exposée via l'enregistrement DI sous forme du contrat.
/// </summary>
internal sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly AzureDevOpsApiReader _reader;
    private readonly ApiRoutes _routes;
    private readonly IReadOnlyList<string> _projects;
    private readonly ExecutionOptions _execution;
    private readonly ILogger<AzureDevOpsClient> _logger;

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<PolicyConfigurationDto>>> _policiesByProject =
        new(StringComparer.OrdinalIgnoreCase);

    public AzureDevOpsClient(
        AzureDevOpsApiReader reader,
        ApiRoutes routes,
        IOptions<ScopeOptions> scope,
        IOptions<ExecutionOptions> execution,
        ILogger<AzureDevOpsClient> logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _routes = routes;

        // Les entrées vides proviennent du blanchiment des index excédentaires quand
        // « --projets » remplace le périmètre configuré : elles ne désignent aucun projet.
        _projects = [.. scope.Value.Projects.Where(p => !string.IsNullOrWhiteSpace(p))];
        _execution = execution.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        var dtos = await _reader.GetListAsync<RepositoryDto>(_routes.Repositories(), cancellationToken).ConfigureAwait(false);
        Log.RepositoriesReturned(_logger, dtos.Count);

        if (_projects.Count == 0)
        {
            return dtos.Select(DomainMapping.Repository).ToList();
        }

        var allowed = new HashSet<string>(_projects, StringComparer.OrdinalIgnoreCase);
        var selected = dtos
            .Where(d => d.Project is not null &&
                (allowed.Contains(d.Project.Name) || (d.Project.Id is not null && allowed.Contains(d.Project.Id))))
            .ToList();

        // Un périmètre qui écarte tout est le symptôme le plus coûteux à diagnostiquer :
        // sans ces messages, un nom de projet inexact est indiscernable d'un projet vide.
        Log.ScopeFilterApplied(_logger, selected.Count, dtos.Count);
        ReportUnmatchedProjects(dtos, selected.Count);

        return selected.Select(DomainMapping.Repository).ToList();
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        IReadOnlyList<BranchStatDto> stats;
        try
        {
            stats = await _reader.GetListAsync<BranchStatDto>(_routes.BranchStats(repository.Id), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // Sur un dépôt sans commit, « stats/branches » répond 404. Ce n'est pas une
            // erreur de collecte : c'est la façon dont le serveur dit « aucune branche ».
            // Sans ce filet, le dépôt apparaîtrait en échec avec un message opaque.
            Log.BranchStatsNotFound(_logger, repository.Name);
            return [];
        }

        var refs = await _reader.GetListAsync<RefDto>(_routes.Refs(repository.Id), cancellationToken).ConfigureAwait(false);

        return DomainMapping.Branches(stats, refs);
    }

    public async Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var dtos = await _reader.GetListAsync<PullRequestDto>(_routes.ActivePullRequests(repository.Id), cancellationToken).ConfigureAwait(false);
        return dtos.Select(DomainMapping.PullRequest).ToList();
    }

    public async Task<IReadOnlyList<PipelineInfo>> GetPipelinesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var definitions = await _reader
            .GetListAsync<BuildDefinitionDto>(_routes.BuildDefinitions(repository.ProjectName, repository.Id), cancellationToken)
            .ConfigureAwait(false);

        var pipelines = new List<PipelineInfo>(definitions.Count);
        foreach (var definition in definitions)
        {
            var builds = await _reader
                .GetListAsync<BuildDto>(_routes.Builds(repository.ProjectName, definition.Id, _execution.RecentRunsToInspect), cancellationToken)
                .ConfigureAwait(false);

            pipelines.Add(DomainMapping.Pipeline(definition, builds));
        }

        return pipelines;
    }

    public async Task<IReadOnlyList<PolicyInfo>> GetPoliciesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var configs = await GetProjectPoliciesAsync(repository.ProjectName, cancellationToken).ConfigureAwait(false);

        return configs.SelectMany(c => DomainMapping.PoliciesForRepository(c, repository.Id)).ToList();
    }

    /// <summary>
    /// Lit les policies d'un projet, une seule fois par exécution. L'endpoint est à
    /// portée projet : sans mémoïsation, dix dépôts d'un même projet déclenchaient dix
    /// requêtes identiques, en parallèle qui plus est.
    /// </summary>
    private Task<IReadOnlyList<PolicyConfigurationDto>> GetProjectPoliciesAsync(string project, CancellationToken cancellationToken)
    {
        // La valeur mise en cache est la tâche elle-même : les appels concurrents
        // partagent la même requête au lieu de la lancer chacun de leur côté.
        return _policiesByProject.GetOrAdd(
            project,
            static (key, state) => state.Reader.GetListAsync<PolicyConfigurationDto>(
                state.Routes.PolicyConfigurations(key),
                state.Token),
            (Reader: _reader, Routes: _routes, Token: cancellationToken));
    }

    /// <summary>Signale chaque entrée de <c>Scope:Projects</c> qui ne correspond à aucun dépôt.</summary>
    private void ReportUnmatchedProjects(IReadOnlyList<RepositoryDto> dtos, int retained)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (dto.Project is not { } project)
            {
                continue;
            }

            present.Add(project.Name);
            if (project.Id is { Length: > 0 })
            {
                present.Add(project.Id);
            }
        }

        foreach (var requested in _projects.Where(p => !present.Contains(p)))
        {
            Log.ScopeProjectNotMatched(_logger, requested);
        }

        if (retained == 0)
        {
            Log.ScopeMatchedNothing(_logger, _projects.Count);
        }
    }
}
