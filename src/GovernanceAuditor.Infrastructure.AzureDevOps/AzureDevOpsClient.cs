using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;
using GovernanceAuditor.Infrastructure.AzureDevOps.Internal;
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
    private readonly ScopeOptions _scope;
    private readonly ExecutionOptions _execution;

    public AzureDevOpsClient(
        AzureDevOpsApiReader reader,
        ApiRoutes routes,
        IOptions<ScopeOptions> scope,
        IOptions<ExecutionOptions> execution)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(execution);

        _reader = reader;
        _routes = routes;
        _scope = scope.Value;
        _execution = execution.Value;
    }

    public async Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        var dtos = await _reader.GetListAsync<RepositoryDto>(_routes.Repositories(), cancellationToken).ConfigureAwait(false);

        IEnumerable<RepositoryDto> selected = dtos;
        if (_scope.Projects.Count > 0)
        {
            var allowed = new HashSet<string>(_scope.Projects, StringComparer.OrdinalIgnoreCase);
            selected = dtos.Where(d => d.Project is not null &&
                (allowed.Contains(d.Project.Name) || (d.Project.Id is not null && allowed.Contains(d.Project.Id))));
        }

        return selected.Select(DomainMapping.Repository).ToList();
    }

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var stats = await _reader.GetListAsync<BranchStatDto>(_routes.BranchStats(repository.Id), cancellationToken).ConfigureAwait(false);
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

        var configs = await _reader
            .GetListAsync<PolicyConfigurationDto>(_routes.PolicyConfigurations(repository.ProjectName), cancellationToken)
            .ConfigureAwait(false);

        return configs.SelectMany(c => DomainMapping.PoliciesForRepository(c, repository.Id)).ToList();
    }
}
