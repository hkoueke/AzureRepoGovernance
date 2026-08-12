using FluentAssertions;
using GovernanceAuditor.Core.Analyzers;
using GovernanceAuditor.Tests.TestData;
using Xunit;

namespace GovernanceAuditor.Tests.Analyzers;

public sealed class PullRequestAnalyzerTests
{
    private readonly PullRequestAnalyzer _sut = new(Build.Clock());

    [Fact]
    public async Task Active_pull_request_without_reviewer_is_critical()
    {
        var ctx = Build.Context(pullRequests: [Build.Pr(1, reviewers: [])]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("PullRequestNoReviewer");
    }

    [Fact]
    public async Task Inactive_pull_request_is_a_warning()
    {
        var ctx = Build.Context(pullRequests:
            [Build.Pr(1, activityAgeDays: 31, reviewers: [Build.Vote("r1", 10)])]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("StalePullRequest");
    }

    [Fact]
    public async Task Rejecting_vote_is_reported()
    {
        var ctx = Build.Context(pullRequests:
            [Build.Pr(1, reviewers: [Build.Vote("r1", -10)])]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("PullRequestRejected");
    }

    [Fact]
    public async Task Author_approving_own_pull_request_is_reported()
    {
        var ctx = Build.Context(pullRequests:
            [Build.Pr(1, authorId: "a1", reviewers: [Build.Vote("a1", 10)])]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().ContainSingle().Which.Category.Should().Be("SelfApproval");
    }

    [Fact]
    public async Task Non_active_pull_request_is_ignored()
    {
        var ctx = Build.Context(pullRequests:
            [Build.Pr(1, status: "completed", reviewers: [])]);

        var findings = await _sut.AnalyzeAsync(ctx, CancellationToken.None);

        findings.Should().BeEmpty();
    }
}
