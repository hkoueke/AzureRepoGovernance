using FluentAssertions;
using GovernanceAuditor.Core.Analyzers;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Tests.TestData;
using Xunit;

namespace GovernanceAuditor.Tests.Analyzers;

public sealed class PipelineAnalyzerTests
{
    private readonly PipelineAnalyzer _sut = new();

    [Fact]
    public async Task No_pipeline_is_critical()
    {
        var ctx = Build.Context();

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("MissingPipeline");
    }

    [Fact]
    public async Task Pipeline_never_run_is_critical()
    {
        var ctx = Build.Context(pipelines: [Build.NeverRunPipeline()]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("PipelineNeverRun");
    }

    [Fact]
    public async Task Last_run_failed_below_threshold_is_a_warning()
    {
        var ctx = Build.Context(pipelines: [Build.Pipeline("CI", "failed", "succeeded")]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle()
            .Which.Should().Match<AuditFinding>(f => f.Severity == Severity.Warning && f.Category == "LastRunFailed");
    }

    [Fact]
    public async Task Consecutive_failures_at_threshold_is_critical()
    {
        var ctx = Build.Context(pipelines: [Build.Pipeline("CI", "failed", "failed", "failed")]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle()
            .Which.Should().Match<AuditFinding>(f => f.Severity == Severity.Critical && f.Category == "ConsecutivePipelineFailures");
    }

    [Fact]
    public async Task Two_failures_below_threshold_is_only_a_warning()
    {
        var ctx = Build.Context(pipelines: [Build.Pipeline("CI", "failed", "failed", "succeeded")]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("LastRunFailed");
    }

    [Fact]
    public async Task Last_run_succeeded_produces_no_finding()
    {
        var ctx = Build.Context(pipelines: [Build.Pipeline("CI", "succeeded", "failed")]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}
