using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Analyse les pull requests actives : absence de relecteur, inactivité,
/// votes de rejet et auto-validation.
/// </summary>
public sealed class PullRequestAnalyzer : IRepositoryAnalyzer
{
    private const int RejectedVote = -10;
    private const int MinimumApprovalVote = 5;

    private readonly TimeProvider _timeProvider;

    /// <summary>Crée l'analyseur avec la source de temps injectée.</summary>
    public PullRequestAnalyzer(TimeProvider timeProvider)
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
        var staleThreshold = now.AddDays(-context.Rules.PullRequestStaleDays);
        var findings = new List<AuditFinding>();

        foreach (var pr in context.PullRequests)
        {
            if (!string.Equals(pr.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            findings.AddRange(EvaluatePullRequest(context, pr, staleThreshold));
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static IEnumerable<AuditFinding> EvaluatePullRequest(RepositoryContext context, PullRequestInfo pr, DateTimeOffset staleThreshold)
    {
        var repository = context.Repository.Name;
        var reference = $"!{pr.Id}";

        if (pr.Reviewers.Count == 0)
        {
            yield return Make(Severity.Critical, "PullRequestNoReviewer", repository, reference, pr,
                "Pull request active sans aucun relecteur.",
                "Ajouter au moins un relecteur à la pull request.");
        }

        if (pr.LastActivityDate is { } activity && activity < staleThreshold)
        {
            yield return Make(Severity.Warning, "StalePullRequest", repository, reference, pr,
                "Pull request active mais inactive depuis le seuil configuré.",
                "Relancer la revue, mettre à jour la branche ou abandonner la pull request.");
        }

        if (pr.Reviewers.Any(r => r.Vote == RejectedVote))
        {
            yield return Make(Severity.Warning, "PullRequestRejected", repository, reference, pr,
                "Au moins un relecteur a voté « rejet » sur la pull request.",
                "Traiter les objections avant de poursuivre la complétion.");
        }

        var selfApproved = pr.AuthorId is not null &&
            pr.Reviewers.Any(r => r.Vote >= MinimumApprovalVote &&
                                  string.Equals(r.ReviewerId, pr.AuthorId, StringComparison.OrdinalIgnoreCase));
        if (selfApproved)
        {
            yield return Make(Severity.Warning, "SelfApproval", repository, reference, pr,
                "L'auteur de la pull request a approuvé ses propres modifications.",
                "Exiger l'approbation d'un relecteur distinct de l'auteur (séparation des responsabilités).");
        }
    }

    private static AuditFinding Make(Severity severity, string category, string repository, string reference, PullRequestInfo pr, string message, string recommendation) =>
        new()
        {
            Severity = severity,
            Category = category,
            Repository = repository,
            PullRequest = reference,
            Actor = pr.AuthorDisplayName,
            Timestamp = pr.LastActivityDate ?? pr.CreationDate,
            Message = message,
            Recommendation = recommendation,
        };
}
