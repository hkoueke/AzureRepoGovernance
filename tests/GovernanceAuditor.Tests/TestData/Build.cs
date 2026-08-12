using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;

namespace GovernanceAuditor.Tests.TestData;

/// <summary>Source de temps figée pour rendre les analyseurs déterministes en test.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>GUID des types de policy utilisés par les tests.</summary>
internal static class PolicyTypes
{
    public const string BuildValidation = "0609b952-1397-4640-95ec-e00a01b2c241";
    public const string MinimumReviewers = "fa4e907d-c16b-4a4c-9dfa-4906e5d171dd";
    public const string CommentRequirements = "c6a1889d-b943-4856-b76f-9e46bb6b0df2";
}

/// <summary>Fabriques d'objets de domaine pour construire des contextes de test concis.</summary>
internal static class Build
{
    public const string RepositoryId = "repo-1";

    /// <summary>Date « maintenant » de référence utilisée dans les tests.</summary>
    public static readonly DateTimeOffset Now = new(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

    public static FixedTimeProvider Clock() => new(Now);

    public static RepositoryContext Context(
        IEnumerable<BranchInfo>? branches = null,
        IEnumerable<PullRequestInfo>? pullRequests = null,
        IEnumerable<PipelineInfo>? pipelines = null,
        IEnumerable<PolicyInfo>? policies = null,
        RulesOptions? rules = null) => new()
        {
            Repository = new RepositoryInfo
            {
                Id = RepositoryId,
                Name = "SampleRepo",
                ProjectName = "SampleProject",
                ProjectId = "project-1",
                Url = "https://devops.local/sample",
                DefaultBranch = "refs/heads/main",
            },
            Branches = (branches ?? []).ToList(),
            PullRequests = (pullRequests ?? []).ToList(),
            Pipelines = (pipelines ?? []).ToList(),
            Policies = (policies ?? []).ToList(),
            Rules = rules ?? new RulesOptions(),
        };

    public static BranchInfo Branch(
        string name,
        int ageDays = 0,
        bool locked = false,
        int ahead = 1) => new()
        {
            Name = name,
            IsLocked = locked,
            AheadCount = ahead,
            BehindCount = 0,
            LastCommitAuthor = "Jane Doe",
            LastCommitAuthorEmail = "jane.doe@entreprise.local",
            LastCommitDate = Now.AddDays(-ageDays),
        };

    public static PullRequestInfo Pr(
        int id,
        string status = "active",
        string source = "refs/heads/feature/x",
        int activityAgeDays = 0,
        string? authorId = "author-1",
        IReadOnlyList<ReviewerVote>? reviewers = null) => new()
        {
            Id = id,
            Title = $"PR {id}",
            Status = status,
            AuthorId = authorId,
            AuthorDisplayName = "Jane Doe",
            CreationDate = Now.AddDays(-activityAgeDays - 1),
            LastActivityDate = Now.AddDays(-activityAgeDays),
            SourceBranch = source,
            TargetBranch = "refs/heads/main",
            Reviewers = reviewers ?? [],
        };

    public static ReviewerVote Vote(string reviewerId, int vote) => new()
    {
        ReviewerId = reviewerId,
        Vote = vote,
    };

    public static PipelineInfo Pipeline(string name = "CI", params string[] resultsNewestFirst)
    {
        var runs = resultsNewestFirst
            .Select((r, i) => new PipelineRun
            {
                RunId = i + 1,
                Status = "completed",
                Result = r,
                FinishTime = Now.AddDays(-i),
            })
            .ToList();

        return new PipelineInfo
        {
            PipelineId = 42,
            PipelineName = name,
            HasEverRun = runs.Count > 0,
            RecentRuns = runs,
        };
    }

    public static PipelineInfo NeverRunPipeline(string name = "CI") => new()
    {
        PipelineId = 42,
        PipelineName = name,
        HasEverRun = false,
        RecentRuns = [],
    };

    public static PolicyInfo Policy(
        string typeId,
        bool enabled = true,
        bool blocking = true,
        string? refName = "refs/heads/main",
        int? minApprovers = null) => new()
        {
            PolicyTypeId = typeId,
            Enabled = enabled,
            Blocking = blocking,
            ScopeRepositoryId = RepositoryId,
            ScopeRefName = refName,
            MinimumApproverCount = minApprovers,
        };

    /// <summary>Jeu complet de policies « saines » sur main (build + reviewers + comments, toutes bloquantes).</summary>
    public static IReadOnlyList<PolicyInfo> HealthyMainPolicies(int minApprovers = 2) =>
    [
        Policy(PolicyTypes.BuildValidation),
        Policy(PolicyTypes.MinimumReviewers, minApprovers: minApprovers),
        Policy(PolicyTypes.CommentRequirements),
    ];
}
