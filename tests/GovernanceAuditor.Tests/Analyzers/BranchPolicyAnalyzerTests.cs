using FluentAssertions;
using GovernanceAuditor.Core.Analyzers;
using GovernanceAuditor.Tests.TestData;
using Xunit;

namespace GovernanceAuditor.Tests.Analyzers;

public sealed class BranchPolicyAnalyzerTests
{
    private readonly BranchPolicyAnalyzer _sut = new();

    [Fact]
    public async Task Protected_branch_with_all_policies_produces_no_finding()
    {
        var ctx = Build.Context(
            branches: [Build.Branch("main", ageDays: 1)],
            policies: Build.HealthyMainPolicies());

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }

    [Fact]
    public async Task Protected_branch_without_any_policy_reports_all_critical_gaps()
    {
        var ctx = Build.Context(branches: [Build.Branch("main", ageDays: 1)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Select(f => f.Category).Should().BeEquivalentTo(
            "MissingBuildValidation",
            "MissingMinimumReviewers",
            "MissingDirectPushProtection",
            "MissingCommentResolution"
        );
    }

    [Fact]
    public async Task Minimum_reviewers_below_threshold_is_a_warning()
    {
        var ctx = Build.Context(
            branches: [Build.Branch("main", ageDays: 1)],
            policies:
            [
                Build.Policy(PolicyTypes.BuildValidation),
                Build.Policy(PolicyTypes.MinimumReviewers, minApprovers: 1),
                Build.Policy(PolicyTypes.CommentRequirements),
            ]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("InsufficientReviewers");
    }

    [Fact]
    public async Task Non_protected_branch_is_ignored()
    {
        var ctx = Build.Context(branches: [Build.Branch("feature/x", ageDays: 1)]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}
