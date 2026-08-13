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
    private readonly Lock _collectedGate = new();

    public FakeAzureDevOpsClient(IReadOnlyList<RepositoryInfo> repositories, params string[] failing)
    {
        _repositories = repositories;
        _failing = new HashSet<string>(failing, StringComparer.Ordinal);
    }

    /// <summary>
    /// Dépôts qui doivent se comporter comme vides (aucune branche renvoyée).
    /// Par défaut un dépôt renvoie une branche : sans cela l'orchestrateur les
    /// écarterait tous, et les tests passeraient à vide.
    /// </summary>
    public HashSet<string> WithoutBranches { get; } = new(StringComparer.Ordinal);

    /// <summary>Noms des dépôts pour lesquels une collecte a réellement été tentée.</summary>
    public List<string> Collected { get; } = [];

    public Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_repositories);

    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        lock (_collectedGate)
        {
            Collected.Add(repository.Name);
        }

        Guard(repository);

        if (WithoutBranches.Contains(repository.Name))
        {
            return Task.FromResult<IReadOnlyList<BranchInfo>>([]);
        }

        return Task.FromResult<IReadOnlyList<BranchInfo>>([new BranchInfo { Name = "main" }]);
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
        // Sans branche par défaut, l'orchestrateur écarterait le dépôt comme « vide ».
        DefaultBranch = "refs/heads/main",
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

    [Fact]
    public async Task Disabled_repositories_are_skipped_before_any_collection()
    {
        var disabled = Repo("Desactive") with { IsDisabled = true };
        var client = new FakeAzureDevOpsClient([Repo("Alpha"), disabled]);
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Warning)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        // « isDisabled » est affirmé par le serveur : aucune requête n'est nécessaire.
        client.Collected.Should().ContainSingle().Which.Should().Be("Alpha");

        result.RepositoriesAnalyzed.Should().Be(1);
        result.RepositoriesSkipped.Should().Be(1);
        result.Skipped[0].Reason.Should().Contain("désactivé");
    }

    [Fact]
    public async Task Repositories_without_branches_are_skipped_after_reading_branches()
    {
        var client = new FakeAzureDevOpsClient([Repo("Alpha"), Repo("Vide")]);
        client.WithoutBranches.Add("Vide");
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Warning)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        // La vacuité se constate : une lecture des branches, puis on s'arrête là.
        client.Collected.Should().BeEquivalentTo("Alpha", "Vide");

        result.RepositoriesAnalyzed.Should().Be(1);
        result.RepositoriesFailed.Should().Be(0);
        result.RepositoriesSkipped.Should().Be(1);
        result.Skipped[0].Repository.Should().Be("Vide");
        result.Skipped[0].Reason.Should().Contain("aucune branche");
    }

    [Fact]
    public async Task A_repository_without_default_branch_is_still_analysed_when_it_has_branches()
    {
        // Régression : « defaultBranch » est un réglage, pas une preuve de vacuité.
        // Une branche par défaut supprimée ne doit pas faire disparaître le dépôt
        // de l'audit — ce serait masquer ses anomalies.
        var client = new FakeAzureDevOpsClient([Repo("SansDefaut") with { DefaultBranch = null }]);
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Critical)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        result.RepositoriesAnalyzed.Should().Be(1);
        result.RepositoriesSkipped.Should().Be(0);
        result.Findings.Should().ContainSingle();
        result.Findings[0].Repository.Should().Be("SansDefaut");
    }

    [Fact]
    public async Task Skipped_repositories_still_advance_progress_to_completion()
    {
        var client = new FakeAzureDevOpsClient([Repo("A"), Repo("Vide")]);
        client.WithoutBranches.Add("Vide");
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Info)]);

        var reports = new List<AuditProgress>();
        await orchestrator.RunAsync(new SynchronousProgress(reports.Add), CancellationToken.None);

        // Un dépôt écarté n'est ni analysé ni en échec : sans le compter, la
        // progression resterait bloquée avant 100 %.
        reports.Should().HaveCount(2);
        reports.Should().OnlyContain(r => r.Total == 2);
        reports.Select(r => r.Completed).Max().Should().Be(2);
    }

    [Fact]
    public async Task Skipped_repositories_do_not_count_as_failures_for_the_exit_code()
    {
        // Deux dépôts vides sur trois : sans exclusion, le ratio d'échec ferait
        // basculer le code de sortie en « analyse partielle ».
        var client = new FakeAzureDevOpsClient([Repo("Alpha"), Repo("Vide1"), Repo("Vide2")]);
        client.WithoutBranches.Add("Vide1");
        client.WithoutBranches.Add("Vide2");
        var orchestrator = NewOrchestrator(client, [new StubAnalyzer(Severity.Info)]);

        var result = await orchestrator.RunAsync(progress: null, CancellationToken.None);

        result.RepositoriesSkipped.Should().Be(2);
        ExitCodePolicy.Resolve(result, new ExecutionOptions()).Should().Be(ExitCodes.Success);
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
