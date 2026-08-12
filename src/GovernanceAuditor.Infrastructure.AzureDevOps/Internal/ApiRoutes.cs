using System.Globalization;
using GovernanceAuditor.Core.Options;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Internal;

/// <summary>
/// Construit les URLs relatives de l'API REST, à résoudre contre la <c>BaseAddress</c>
/// du <see cref="HttpClient"/>. Les segments variables sont échappés.
/// </summary>
internal sealed class ApiRoutes
{
    private readonly string _collection;
    private readonly string _apiVersion;

    public ApiRoutes(AzureDevOpsServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _collection = Escape(options.Collection);
        _apiVersion = options.ApiVersion;
    }

    public string Repositories() =>
        $"{_collection}/_apis/git/repositories?api-version={_apiVersion}";

    public string BranchStats(string repositoryId) =>
        $"{_collection}/_apis/git/repositories/{Escape(repositoryId)}/stats/branches?api-version={_apiVersion}";

    public string Refs(string repositoryId) =>
        $"{_collection}/_apis/git/repositories/{Escape(repositoryId)}/refs?filter=heads/&api-version={_apiVersion}";

    public string ActivePullRequests(string repositoryId) =>
        $"{_collection}/_apis/git/repositories/{Escape(repositoryId)}/pullrequests?searchCriteria.status=active&api-version={_apiVersion}";

    public string BuildDefinitions(string project, string repositoryId) =>
        $"{_collection}/{Escape(project)}/_apis/build/definitions?repositoryId={Escape(repositoryId)}&repositoryType=TfsGit&api-version={_apiVersion}";

    public string Builds(string project, int definitionId, int top) =>
        $"{_collection}/{Escape(project)}/_apis/build/builds?definitions={definitionId.ToString(CultureInfo.InvariantCulture)}&$top={top.ToString(CultureInfo.InvariantCulture)}&queryOrder=finishTimeDescending&api-version={_apiVersion}";

    public string PolicyConfigurations(string project) =>
        $"{_collection}/{Escape(project)}/_apis/policy/configurations?api-version={_apiVersion}";

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
