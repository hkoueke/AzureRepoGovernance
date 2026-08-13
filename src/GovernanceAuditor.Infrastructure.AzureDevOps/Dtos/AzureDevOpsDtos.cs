using System.Text.Json.Serialization;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;

/// <summary>Enveloppe générique des listes Azure DevOps : { count, value: [...] }.</summary>
internal sealed record ListResponse<T>
{
    [JsonPropertyName("value")]
    public IReadOnlyList<T> Value { get; init; } = [];
}

internal sealed record ProjectDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

internal sealed record RepositoryDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Url { get; init; }
    public string? WebUrl { get; init; }

    // Azure DevOps sérialise « defaultBranch » avec EmitDefaultValue=false : le champ
    // est absent — et non vide — pour un dépôt jamais initialisé.
    public string? DefaultBranch { get; init; }
    public bool IsDisabled { get; init; }
    public long? Size { get; init; }
    public ProjectDto? Project { get; init; }
}

internal sealed record GitUserDateDto
{
    public string? Name { get; init; }
    public string? Email { get; init; }
    public DateTimeOffset? Date { get; init; }
}

internal sealed record CommitDto
{
    public string? CommitId { get; init; }
    public GitUserDateDto? Author { get; init; }
    public GitUserDateDto? Committer { get; init; }
}

internal sealed record BranchStatDto
{
    public string Name { get; init; } = string.Empty;
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
    public bool IsBaseVersion { get; init; }
    public CommitDto? Commit { get; init; }
}

internal sealed record IdentityRefDto
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public string? UniqueName { get; init; }
}

internal sealed record RefDto
{
    public string Name { get; init; } = string.Empty;
    public string? ObjectId { get; init; }
    public bool IsLocked { get; init; }
    public IdentityRefDto? Creator { get; init; }
}

internal sealed record ReviewerDto
{
    public string? Id { get; init; }
    public string? DisplayName { get; init; }
    public int Vote { get; init; }
}

internal sealed record PullRequestDto
{
    public int PullRequestId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public IdentityRefDto? CreatedBy { get; init; }
    public DateTimeOffset CreationDate { get; init; }
    public string SourceRefName { get; init; } = string.Empty;
    public string TargetRefName { get; init; } = string.Empty;
    public IReadOnlyList<ReviewerDto> Reviewers { get; init; } = [];
}

internal sealed record BuildDefinitionDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

internal sealed record BuildDto
{
    public long Id { get; init; }
    public string? Status { get; init; }
    public string? Result { get; init; }
    public DateTimeOffset? StartTime { get; init; }
    public DateTimeOffset? FinishTime { get; init; }
    public DateTimeOffset? QueueTime { get; init; }
    public string? SourceBranch { get; init; }
}

internal sealed record PolicyTypeRefDto
{
    public string Id { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
}

internal sealed record PolicyScopeDto
{
    public string? RepositoryId { get; init; }
    public string? RefName { get; init; }
    public string? MatchKind { get; init; }
}

internal sealed record PolicySettingsDto
{
    public IReadOnlyList<PolicyScopeDto> Scope { get; init; } = [];
    public int? MinimumApproverCount { get; init; }
}

internal sealed record PolicyConfigurationDto
{
    public int Id { get; init; }
    public bool IsEnabled { get; init; }
    public bool IsBlocking { get; init; }
    public PolicyTypeRefDto? Type { get; init; }
    public PolicySettingsDto? Settings { get; init; }
}
