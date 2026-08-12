using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Signale les branches obsolètes : sans commit depuis au moins
/// <c>Rules.BranchInactiveDays</c> jours. Gravité : Warning.
/// </summary>
public sealed class StaleBranchAnalyzer : IRepositoryAnalyzer
{
    private const string Category = "StaleBranch";
    private readonly TimeProvider _timeProvider;

    /// <summary>Crée l'analyseur avec la source de temps injectée (testabilité).</summary>
    public StaleBranchAnalyzer(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _timeProvider.GetUtcNow();
        var findings = new List<AuditFinding>();

        foreach (var branch in context.Branches)
        {
            if (!BranchRules.IsInactive(branch, now, context.Rules.BranchInactiveDays))
            {
                continue;
            }

            findings.Add(new AuditFinding
            {
                Severity = Severity.Warning,
                Category = Category,
                Repository = context.Repository.Name,
                Branch = branch.Name,
                Actor = branch.LastCommitAuthor,
                ActorEmail = branch.LastCommitAuthorEmail,
                Timestamp = branch.LastCommitDate,
                Message = $"Branche inactive depuis {BranchRules.InactiveDays(branch, now)} jours.",
                Recommendation = "Vérifier si la branche est encore nécessaire ; sinon, la supprimer ou la verrouiller.",
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
