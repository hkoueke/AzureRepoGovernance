using FluentAssertions;
using GovernanceAuditor.Console;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GovernanceAuditor.Tests.Orchestration;

/// <summary>Client de test : renvoie des données fixes et échoue sur les dépôts désignés.</summary>
internal sealed class FakeAzureDevOpsClient : IAzureDevOpsClient
{
    private readonly IReadOnlyList<RepositoryInfo> _repositories;
    private readonly HashSet<string> _failing;

    public FakeAzureDevOpsClient(IReadOnlyList<RepositoryInfo> repositories, params string[] failing)
    {
        _repositories = repositories;
        _failing = new HashSet<string>(failing, StringComparer.Ordinal);
    }

    public Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_repositories);

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        Guard(repository);
        return Task.FromResult<IReadOnlyList<BranchInfo>>([]);
    }

    public Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(RepositoryInfo repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PullRequestInfo>>([]);

    public Task<IReadOnlyList<PipelineInfo>> GetPipelinesAsync(RepositoryInfo repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PipelineInfo>>([]);

    public Task<IReadOnlyList<PolicyInfo>> GetPoliciesAsync(RepositoryInfo repository, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PolicyInfo>>([]);

    private void Guard(RepositoryInfo repository)
    {
        if (_failing.Contains(repository.Name))
        {
            throw new InvalidOperationException($"Accès refusé sur {repository.Name}");
        }
    }
}

/// <summary>Analyseur de test : produit une anomalie par dépôt.</summary>
internal sealed class StubAnalyzer : IRepositoryAnalyzer
{
    private readonly Severity _severity;

    public StubAnalyzer(Severity severity) => _severity = severity;

    public Task<IReadOnlyCollection<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<AuditFinding>>(
        [
            new AuditFinding
            {
                Severity = _severity,
                Category = "Test",
                Repository = context.Repository.Name,
                Message = "Anomalie de test",
                Recommendation = "Aucune",
            },
        ]);
}

public sealed class AuditOrchestratorTests
{
    private static RepositoryInfo Repo(string name) => new()
    {
        Id = name,
        Name = name,
        ProjectName = "Proj",
        Url = "https://x",
    };

    private static AuditOrchestrator NewOrchestrator(
        IAzureDevOpsClient client,
        IEnumerable<IRepositoryAnalyzer> analyzers,
        ExecutionOptions? execution = null) =>
        new(
            client,
            analyzers,
            Options.Create(new RulesOptions()),
            Options.Create(execution ?? new ExecutionOptions()),
            NullLogger<AuditOrchestrator>.Instance);

    [Fact]
    public async Task A_failing_repository_does_not_stop_the_others()
    {
        var client = new FakeAzureDevOpsClient([Repo("Alpha"), Repo("Beta"), Repo("Gamma")], "Beta");
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Warning)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        result.RepositoriesAnalyzed.Should().Be(2);
        result.RepositoriesFailed.Should().Be(1);
        result.Findings.Should().HaveCount(2);
        result.Errors.Should().ContainSingle();
        result.Errors[0].Repository.Should().Be("Beta");
        result.Errors[0].Message.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task Findings_are_sorted_deterministically()
    {
        var client = new FakeAzureDevOpsClient([Repo("Zeta"), Repo("Alpha")]);
        var orchestrator = NewOrchestrator(
            client,
            [new StubAnalyzer(Severity.Warning), new StubAnalyzer(Severity.Critical)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        result.Findings.Should().HaveCount(4);
        result.Findings[0].Severity.Should().Be(Severity.Critical);
        result.Findings[0].Repository.Should().Be("Alpha");
        result.Findings[^1].Severity.Should().Be(Severity.Warning);
    }

    [Fact]
    public async Task Progress_is_reported_for_every_repository()
    {
        var client = new FakeAzureDevOpsClient([Repo("A"), Repo("B"), Repo("C")]);
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Info)]);

        var reports = new List<AuditProgress>();
        var progress = new SynchronousProgress(reports.Add);

        await orchestrator.RunAsync(progress, CancellationToken.None);

        reports.Should().HaveCount(3);
        reports.Should().OnlyContain(r => r.Total == 3);
        reports.Select(r => r.Completed).Max().Should().Be(3);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_being_swallowed()
    {
        var client = new FakeAzureDevOpsClient([Repo("A"), Repo("B")]);
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Info)]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = async () => await orchestrator.RunAsync(progress: null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Collecte synchrone des avancements (Progress&lt;T&gt; passerait par le pool de threads).</summary>
    private sealed class SynchronousProgress : IProgress<AuditProgress>
    {
        private readonly Action<AuditProgress> _handler;
        private readonly Lock _gate = new();

        public SynchronousProgress(Action<AuditProgress> handler) => _handler = handler;

        public void Report(AuditProgress value)
        {
            lock (_gate)
            {
                _handler(value);
            }
        }
    }
}
