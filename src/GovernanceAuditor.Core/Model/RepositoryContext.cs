using GovernanceAuditor.Core.Options;

namespace GovernanceAuditor.Core.Model;

/// <summary>
/// Ensemble complet des données d'un dépôt, collectées une seule fois.
/// Les analyseurs travaillent exclusivement sur ce contexte et n'effectuent
/// jamais d'appel réseau.
/// </summary>
public sealed record RepositoryContext
{
    /// <summary>Dépôt analysé.</summary>
    public required RepositoryInfo Repository { get; init; }

    /// <summary>Branches du dépôt.</summary>
    public required IReadOnlyList<BranchInfo> Branches { get; init; }

    /// <summary>Pull requests du dépôt.</summary>
    public required IReadOnlyList<PullRequestInfo> PullRequests { get; init; }

    /// <summary>Pipelines associés au dépôt.</summary>
    public required IReadOnlyList<PipelineInfo> Pipelines { get; init; }

    /// <summary>Policies applicables au dépôt.</summary>
    public required IReadOnlyList<PolicyInfo> Policies { get; init; }

    /// <summary>Seuils des règles de gouvernance à appliquer.</summary>
    public required RulesOptions Rules { get; init; }
}
