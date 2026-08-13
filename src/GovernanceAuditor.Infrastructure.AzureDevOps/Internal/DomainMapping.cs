using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Internal;

/// <summary>Conversion des DTOs Azure DevOps vers le modèle de domaine.</summary>
internal static class DomainMapping
{
    private const string HeadsPrefix = "refs/heads/";

    public static RepositoryInfo Repository(RepositoryDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new RepositoryInfo
        {
            Id = dto.Id,
            Name = dto.Name,
            ProjectName = dto.Project?.Name ?? string.Empty,
            ProjectId = dto.Project?.Id,
            Url = dto.WebUrl ?? dto.Url ?? string.Empty,
            DefaultBranch = dto.DefaultBranch,
            IsDisabled = dto.IsDisabled,
            SizeInBytes = dto.Size,
        };
    }

    public static IReadOnlyList<BranchInfo> Branches(IReadOnlyList<BranchStatDto> stats, IReadOnlyList<RefDto> refs)
    {
        ArgumentNullException.ThrowIfNull(stats);
        ArgumentNullException.ThrowIfNull(refs);

        var lockByName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in refs)
        {
            lockByName[StripHeads(reference.Name)] = reference.IsLocked;
        }

        var branches = new List<BranchInfo>(stats.Count);
        foreach (var stat in stats)
        {
            var commit = stat.Commit;
            var author = commit?.Committer ?? commit?.Author;

            branches.Add(new BranchInfo
            {
                Name = stat.Name,
                IsLocked = lockByName.TryGetValue(stat.Name, out var locked) && locked,
                LastCommitId = commit?.CommitId,
                LastCommitAuthor = author?.Name,
                LastCommitAuthorEmail = author?.Email,
                LastCommitDate = author?.Date,
                AheadCount = stat.AheadCount,
                BehindCount = stat.BehindCount,
            });
        }

        return branches;
    }

    public static PullRequestInfo PullRequest(PullRequestDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        return new PullRequestInfo
        {
            Id = dto.PullRequestId,
            Title = dto.Title,
            Status = dto.Status,
            AuthorId = dto.CreatedBy?.Id,
            AuthorDisplayName = dto.CreatedBy?.DisplayName,
            CreationDate = dto.CreationDate,
            // Azure DevOps n'expose pas de champ « dernière activité » sur la PR :
            // on approxime par la date de création (affinable via l'API threads si besoin).
            LastActivityDate = dto.CreationDate,
            SourceBranch = dto.SourceRefName,
            TargetBranch = dto.TargetRefName,
            Reviewers = dto.Reviewers
                .Select(r => new ReviewerVote
                {
                    ReviewerId = r.Id ?? string.Empty,
                    DisplayName = r.DisplayName,
                    Vote = r.Vote,
                })
                .ToList(),
        };
    }

    public static PipelineInfo Pipeline(BuildDefinitionDto definition, IReadOnlyList<BuildDto> builds)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(builds);

        var runs = builds
            .OrderByDescending(BestTimestamp)
            .Select(b => new PipelineRun
            {
                RunId = b.Id,
                Branch = b.SourceBranch,
                Status = b.Status ?? "unknown",
                Result = b.Result,
                StartTime = b.StartTime,
                FinishTime = b.FinishTime,
            })
            .ToList();

        return new PipelineInfo
        {
            PipelineId = definition.Id,
            PipelineName = definition.Name,
            HasEverRun = runs.Count > 0,
            RecentRuns = runs,
        };
    }

    public static IEnumerable<PolicyInfo> PoliciesForRepository(PolicyConfigurationDto config, string repositoryId)
    {
        ArgumentNullException.ThrowIfNull(config);

        return PoliciesForRepository(config, repositoryId);

        static IEnumerable<PolicyInfo> PoliciesForRepository(PolicyConfigurationDto config, string repositoryId)
        {
            if (config.Settings is not { } settings)
            {
                yield break;
            }

            var typeId = config.Type?.Id ?? string.Empty;

            foreach (var scope in settings.Scope)
            {
                var matchesRepository = scope.RepositoryId is null ||
                    string.Equals(scope.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase);
                if (!matchesRepository)
                {
                    continue;
                }

                yield return new PolicyInfo
                {
                    PolicyTypeId = typeId,
                    PolicyTypeDisplayName = config.Type?.DisplayName,
                    Enabled = config.IsEnabled,
                    Blocking = config.IsBlocking,
                    ScopeRepositoryId = scope.RepositoryId,
                    ScopeRefName = scope.RefName,
                    MinimumApproverCount = settings.MinimumApproverCount,
                };
            }
        }
    }

    private static DateTimeOffset BestTimestamp(BuildDto build) =>
        build.FinishTime ?? build.StartTime ?? build.QueueTime ?? DateTimeOffset.MinValue;

    private static string StripHeads(string name) =>
        name.StartsWith(HeadsPrefix, StringComparison.OrdinalIgnoreCase) ? name[HeadsPrefix.Length..] : name;
}
