using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Signale les branches abandonnées : non protégées, inactives au-delà du seuil
/// d'abandon, non mergées et sans pull request active. Gravité : Warning.
/// </summary>
public sealed class AbandonedBranchAnalyzer : IRepositoryAnalyzer
{
    private const string Category = "AbandonedBranch";
    private readonly TimeProvider _timeProvider;

    /// <summary>Crée l'analyseur avec la source de temps injectée.</summary>
    public AbandonedBranchAnalyzer(TimeProvider timeProvider)
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
            if (!BranchRules.IsAbandoned(branch, context, now))
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
                Message = $"Branche abandonnée : inactive depuis {BranchRules.InactiveDays(branch, now)} jours, non mergée et sans pull request active.",
                Recommendation = "Supprimer ou verrouiller la branche après vérification auprès de son auteur.",
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}

/// <summary>
/// Signale les branches abandonnées qui ne sont PAS verrouillées : risque de
/// reprise accidentelle de travail obsolète. Gravité : Critical.
/// </summary>
public sealed class AbandonedUnlockedBranchAnalyzer : IRepositoryAnalyzer
{
    private const string Category = "AbandonedUnlockedBranch";
    private readonly TimeProvider _timeProvider;

    /// <summary>Crée l'analyseur avec la source de temps injectée.</summary>
    public AbandonedUnlockedBranchAnalyzer(TimeProvider timeProvider)
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
            if (!BranchRules.IsAbandoned(branch, context, now) || branch.IsLocked)
            {
                continue;
            }

            findings.Add(new AuditFinding
            {
                Severity = Severity.Critical,
                Category = Category,
                Repository = context.Repository.Name,
                Branch = branch.Name,
                Actor = branch.LastCommitAuthor,
                ActorEmail = branch.LastCommitAuthorEmail,
                Timestamp = branch.LastCommitDate,
                Message = "Branche abandonnée et non verrouillée.",
                Recommendation = "Verrouiller ou supprimer la branche après vérification.",
            });
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }
}
