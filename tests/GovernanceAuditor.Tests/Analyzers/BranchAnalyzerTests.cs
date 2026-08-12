using FluentAssertions;
using GovernanceAuditor.Core.Analyzers;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Tests.TestData;
using Xunit;

namespace GovernanceAuditor.Tests.Analyzers;

public sealed class StaleBranchAnalyzerTests
{
    private readonly StaleBranchAnalyzer _sut = new(Build.Clock());

    [Fact]
    public async Task Branch_at_exactly_the_threshold_is_not_stale()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/x", ageDays: 90)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Branch_older_than_threshold_is_flagged_as_warning()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/x", ageDays: 91)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle()
            .Which.Should().Match<AuditFinding>(f => f.Severity == Severity.Warning && f.Category == "StaleBranch");
    }

    [Fact]
    public async Task Recent_branch_produces_no_finding()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/x", ageDays: 1)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}

public sealed class AbandonedBranchAnalyzerTests
{
    private readonly AbandonedBranchAnalyzer _sut = new(Build.Clock());

    [Fact]
    public async Task Inactive_unmerged_branch_without_active_pr_is_abandoned()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/old", ageDays: 121, ahead: 3)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("AbandonedBranch");
    }

    [Fact]
    public async Task Branch_with_active_pull_request_is_not_abandoned()
    {
        var ctx = Build.Context(
            branches: [Build.Branch("feature/old", ageDays: 121, ahead: 3)],
            pullRequests: [Build.Pr(1, source: "refs/heads/feature/old")]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Merged_branch_is_not_abandoned()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/old", ageDays: 121, ahead: 0)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Protected_branch_is_never_abandoned()
    {
        var ctx = Build.Context(branches: [Build.Branch("main", ageDays: 400, ahead: 3)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}

public sealed class AbandonedUnlockedBranchAnalyzerTests
{
    private readonly AbandonedUnlockedBranchAnalyzer _sut = new(Build.Clock());

    [Fact]
    public async Task Abandoned_and_unlocked_branch_is_critical()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/old", ageDays: 121, locked: false, ahead: 3)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle()
            .Which.Should().Match<AuditFinding>(f => f.Severity == Severity.Critical && f.Category == "AbandonedUnlockedBranch");
    }

    [Fact]
    public async Task Abandoned_but_locked_branch_produces_no_finding()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/old", ageDays: 121, locked: true, ahead: 3)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}
