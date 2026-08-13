using FluentAssertions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using GovernanceAuditor.Infrastructure.AzureDevOps;
using GovernanceAuditor.Infrastructure.AzureDevOps.Dtos;
using GovernanceAuditor.Infrastructure.AzureDevOps.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GovernanceAuditor.Tests.Infrastructure;

public sealed class AzureDevOpsClientTests
{
    private static RepositoryInfo Repo() => new()
    {
        Id = "repo-1",
        Name = "R",
        ProjectName = "Proj",
        Url = "https://x",
    };

    private static AzureDevOpsClient NewClient(
        HttpClient http,
        ScopeOptions? scope = null,
        ExecutionOptions? execution = null,
        ILogger<AzureDevOpsClient>? logger = null)
    {
        var routes = new ApiRoutes(new AzureDevOpsServerOptions
        {
            BaseUrl = "https://server",
            Collection = "DefaultCollection",
            ApiVersion = "7.1",
        });
        var reader = new AzureDevOpsApiReader(http);
        return new AzureDevOpsClient(
            reader,
            routes,
            Options.Create(scope ?? new ScopeOptions()),
            Options.Create(execution ?? new ExecutionOptions()),
            logger ?? NullLogger<AzureDevOpsClient>.Instance);
    }

    [Fact]
    public async Task GetListAsync_follows_continuation_token_across_pages()
    {
        using var handler = new FakeHttpMessageHandler(req =>
            req.RequestUri!.AbsoluteUri.Contains("continuationToken", StringComparison.Ordinal)
                ? FakeHttpMessageHandler.Json("""{"value":[{"id":"p3","name":"P3"}]}""")
                : FakeHttpMessageHandler.Json("""{"value":[{"id":"p1","name":"P1"},{"id":"p2","name":"P2"}]}""", "TOK"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var reader = new AzureDevOpsApiReader(http);

        var projects = await reader.GetListAsync<ProjectDto>("DefaultCollection/_apis/projects?api-version=7.1", CancellationToken.None);

        projects.Should().HaveCount(3);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[1].Should().Contain("continuationToken=TOK");
    }

    [Fact]
    public async Task GetBranchesAsync_merges_commit_stats_and_lock_state()
    {
        using var handler = new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (url.Contains("stats/branches", StringComparison.Ordinal))
            {
                return FakeHttpMessageHandler.Json("""{"value":[{"name":"main","aheadCount":2,"behindCount":0,"commit":{"commitId":"abc","committer":{"name":"Jane","email":"jane@x","date":"2026-06-01T10:00:00Z"}}}]}""");
            }

            if (url.Contains("/refs", StringComparison.Ordinal))
            {
                return FakeHttpMessageHandler.Json("""{"value":[{"name":"refs/heads/main","isLocked":true}]}""");
            }

            return FakeHttpMessageHandler.Json("""{"value":[]}""");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var client = NewClient(http);

        var branches = await client.GetBranchesAsync(Repo(), CancellationToken.None);

        branches.Should().ContainSingle();
        var main = branches[0];
        main.Name.Should().Be("main");
        main.IsLocked.Should().BeTrue();
        main.AheadCount.Should().Be(2);
        main.LastCommitAuthorEmail.Should().Be("jane@x");
        main.LastCommitDate.Should().Be(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetPoliciesAsync_keeps_only_scopes_matching_the_repository()
    {
        const string body = """{"value":[{"id":1,"isEnabled":true,"isBlocking":true,"type":{"id":"fa4e907d-c16b-4a4c-9dfa-4906e5d171dd","displayName":"Min reviewers"},"settings":{"minimumApproverCount":2,"scope":[{"repositoryId":"repo-1","refName":"refs/heads/main"},{"repositoryId":"other","refName":"refs/heads/main"}]}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var client = NewClient(http);

        var policies = await client.GetPoliciesAsync(Repo(), CancellationToken.None);

        policies.Should().ContainSingle();
        policies[0].MinimumApproverCount.Should().Be(2);
        policies[0].Blocking.Should().BeTrue();
        policies[0].ScopeRepositoryId.Should().Be("repo-1");
    }

    [Fact]
    public async Task GetPipelinesAsync_orders_runs_newest_first()
    {
        using var handler = new FakeHttpMessageHandler(req =>
        {
            var url = req.RequestUri!.AbsoluteUri;
            if (url.Contains("build/definitions", StringComparison.Ordinal))
            {
                return FakeHttpMessageHandler.Json("""{"value":[{"id":7,"name":"CI"}]}""");
            }

            if (url.Contains("build/builds", StringComparison.Ordinal))
            {
                return FakeHttpMessageHandler.Json("""{"value":[{"id":1,"status":"completed","result":"succeeded","finishTime":"2026-06-01T00:00:00Z"},{"id":2,"status":"completed","result":"failed","finishTime":"2026-06-10T00:00:00Z"}]}""");
            }

            return FakeHttpMessageHandler.Json("""{"value":[]}""");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var client = NewClient(http);

        var pipelines = await client.GetPipelinesAsync(Repo(), CancellationToken.None);

        pipelines.Should().ContainSingle();
        var ci = pipelines[0];
        ci.PipelineName.Should().Be("CI");
        ci.HasEverRun.Should().BeTrue();
        ci.RecentRuns[0].RunId.Should().Be(2);
        ci.RecentRuns[0].Result.Should().Be("failed");
    }

    [Fact]
    public async Task GetRepositoriesAsync_filters_by_configured_projects()
    {
        const string body = """{"value":[{"id":"r1","name":"RepoA","project":{"id":"pa","name":"Alpha"}},{"id":"r2","name":"RepoB","project":{"id":"pb","name":"Beta"}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var client = NewClient(http, scope: new ScopeOptions { Projects = ["Beta"] });

        var repos = await client.GetRepositoriesAsync(CancellationToken.None);

        repos.Should().ContainSingle();
        repos[0].Name.Should().Be("RepoB");
        repos[0].ProjectName.Should().Be("Beta");
    }

    [Fact]
    public async Task GetRepositoriesAsync_reports_the_count_before_and_after_filtering()
    {
        const string body = """{"value":[{"id":"r1","name":"RepoA","project":{"id":"pa","name":"Alpha"}},{"id":"r2","name":"RepoB","project":{"id":"pb","name":"Beta"}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var logger = new RecordingLogger<AzureDevOpsClient>();
        var client = NewClient(http, scope: new ScopeOptions { Projects = ["Beta"] }, logger: logger);

        await client.GetRepositoriesAsync(CancellationToken.None);

        // Le total renvoyé par le serveur doit rester visible : sans lui, « 1 dépôt »
        // ne dit pas si le serveur en a renvoyé 1 ou 200.
        logger.Contains(LogLevel.Information, "Dépôts renvoyés par la collection : 2").Should().BeTrue();
        logger.Contains(LogLevel.Information, "1 dépôt(s) retenu(s) sur 2").Should().BeTrue();
    }

    [Fact]
    public async Task GetRepositoriesAsync_warns_when_a_configured_project_matches_nothing()
    {
        const string body = """{"value":[{"id":"r1","name":"RepoA","project":{"id":"pa","name":"Alpha"}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var logger = new RecordingLogger<AzureDevOpsClient>();
        var client = NewClient(http, scope: new ScopeOptions { Projects = ["Alpha", "Typo"] }, logger: logger);

        var repos = await client.GetRepositoriesAsync(CancellationToken.None);

        repos.Should().ContainSingle();
        logger.Contains(LogLevel.Warning, "« Typo »").Should().BeTrue();
        logger.Contains(LogLevel.Warning, "« Alpha »").Should().BeFalse();
    }

    [Fact]
    public async Task GetRepositoriesAsync_warns_when_the_whole_scope_matches_nothing()
    {
        const string body = """{"value":[{"id":"r1","name":"RepoA","project":{"id":"pa","name":"Alpha"}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var logger = new RecordingLogger<AzureDevOpsClient>();
        var client = NewClient(http, scope: new ScopeOptions { Projects = ["Nope"] }, logger: logger);

        var repos = await client.GetRepositoriesAsync(CancellationToken.None);

        repos.Should().BeEmpty();
        logger.Contains(LogLevel.Warning, "Aucun dépôt retenu").Should().BeTrue();
    }

    [Fact]
    public async Task GetRepositoriesAsync_maps_disabled_flag_and_absent_default_branch()
    {
        // Azure DevOps omet « defaultBranch » tant qu'aucun commit n'a été poussé.
        const string body = """{"value":[{"id":"r1","name":"Vide","project":{"id":"pa","name":"Alpha"}},{"id":"r2","name":"Desactive","isDisabled":true,"size":0,"defaultBranch":"refs/heads/main","project":{"id":"pa","name":"Alpha"}}]}""";
        using var handler = new FakeHttpMessageHandler(_ => FakeHttpMessageHandler.Json(body));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://server/") };
        var client = NewClient(http);

        var repos = await client.GetRepositoriesAsync(CancellationToken.None);

        repos.Should().HaveCount(2);
        repos[0].DefaultBranch.Should().BeNull();
        repos[0].IsDisabled.Should().BeFalse();
        repos[1].IsDisabled.Should().BeTrue();
        repos[1].SizeInBytes.Should().Be(0);
    }
}
