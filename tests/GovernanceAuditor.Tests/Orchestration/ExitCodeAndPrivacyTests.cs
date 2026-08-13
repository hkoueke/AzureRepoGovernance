using FluentAssertions;
using GovernanceAuditor.Console;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;
using Xunit;

namespace GovernanceAuditor.Tests.Orchestration;

public sealed class ExitCodePolicyTests
{
    private static AuditRunResult Result(int analyzed, int failed, params Severity[] severities) => new()
    {
        Findings = severities
            .Select(s => new AuditFinding
            {
                Severity = s,
                Category = "Test",
                Repository = "R",
                Message = "m",
                Recommendation = "r",
            })
            .ToList(),
        Errors = [],
        RepositoriesAnalyzed = analyzed,
        RepositoriesFailed = failed,
        Duration = TimeSpan.Zero,
    };

    [Fact]
    public void No_repository_analysed_is_reported_as_partial_not_success()
    {
        // Périmètre qui ne correspond à rien, droits insuffisants, collection vide :
        // renvoyer 0 affirmerait « aucune anomalie » sans avoir rien examiné.
        var code = ExitCodePolicy.Resolve(Result(0, 0), new ExecutionOptions());

        code.Should().Be(ExitCodes.PartialFailure);
    }

    [Fact]
    public void Only_skipped_repositories_is_reported_as_partial()
    {
        var result = Result(0, 0) with
        {
            Skipped = [new SkippedRepository { Repository = "Vide", Reason = "dépôt vide" }],
        };

        ExitCodePolicy.Resolve(result, new ExecutionOptions()).Should().Be(ExitCodes.PartialFailure);
    }

    [Fact]
    public void Clean_run_returns_success()
    {
        var code = ExitCodePolicy.Resolve(Result(10, 0, Severity.Warning), new ExecutionOptions());

        code.Should().Be(ExitCodes.Success);
    }

    [Fact]
    public void Critical_finding_returns_one()
    {
        var code = ExitCodePolicy.Resolve(Result(10, 0, Severity.Critical), new ExecutionOptions());

        code.Should().Be(ExitCodes.Critical);
    }

    [Fact]
    public void Failure_ratio_above_threshold_returns_partial_failure()
    {
        // 3 échecs sur 10 dépôts, seuil à 10 % : l'analyse n'est pas exploitable.
        var code = ExitCodePolicy.Resolve(Result(7, 3, Severity.Critical), new ExecutionOptions());

        code.Should().Be(ExitCodes.PartialFailure);
    }

    [Fact]
    public void Failures_within_tolerance_do_not_mask_critical_findings()
    {
        var code = ExitCodePolicy.Resolve(Result(99, 1, Severity.Critical), new ExecutionOptions());

        code.Should().Be(ExitCodes.Critical);
    }

    [Fact]
    public void FailOnCritical_disabled_returns_success()
    {
        var execution = new ExecutionOptions { FailOnCritical = false };

        var code = ExitCodePolicy.Resolve(Result(10, 0, Severity.Critical), execution);

        code.Should().Be(ExitCodes.Success);
    }
}

public sealed class ActorRedactorTests
{
    private static AuditRunResult WithActors(params (string Actor, string Email)[] actors) => new()
    {
        Findings = actors
            .Select(a => new AuditFinding
            {
                Severity = Severity.Warning,
                Category = "Test",
                Repository = "R",
                Actor = a.Actor,
                ActorEmail = a.Email,
                Message = "m",
                Recommendation = "r",
            })
            .ToList(),
        Errors = [],
        RepositoriesAnalyzed = 1,
        RepositoriesFailed = 0,
        Duration = TimeSpan.Zero,
    };

    [Fact]
    public void Redaction_removes_names_and_emails()
    {
        var result = new ActorRedactor().Redact(WithActors(("Jane Doe", "jane@x.local")));

        result.Findings[0].Actor.Should().NotBe("Jane Doe");
        result.Findings[0].Actor.Should().StartWith("Acteur-");
        result.Findings[0].ActorEmail.Should().NotContain("jane");
    }

    [Fact]
    public void Same_actor_keeps_the_same_pseudonym_within_a_run()
    {
        var result = new ActorRedactor().Redact(
            WithActors(("Jane Doe", "jane@x.local"), ("Jane Doe", "jane@x.local"), ("John Roe", "john@x.local")));

        result.Findings[0].Actor.Should().Be(result.Findings[1].Actor);
        result.Findings[0].Actor.Should().NotBe(result.Findings[2].Actor);
    }

    [Fact]
    public void Pseudonyms_differ_between_runs()
    {
        // Le sel est tiré par exécution : un rapport ne doit pas permettre de
        // recouper les acteurs d'un autre rapport.
        var first = new ActorRedactor().Redact(WithActors(("Jane Doe", "jane@x.local")));
        var second = new ActorRedactor().Redact(WithActors(("Jane Doe", "jane@x.local")));

        first.Findings[0].Actor.Should().NotBe(second.Findings[0].Actor);
    }

    [Fact]
    public void Missing_actor_is_left_untouched()
    {
        var result = new AuditRunResult
        {
            Findings =
            [
                new AuditFinding
                {
                    Severity = Severity.Info,
                    Category = "Test",
                    Repository = "R",
                    Message = "m",
                    Recommendation = "r",
                },
            ],
            Errors = [],
            RepositoriesAnalyzed = 1,
            RepositoriesFailed = 0,
            Duration = TimeSpan.Zero,
        };

        var redacted = new ActorRedactor().Redact(result);

        redacted.Findings[0].Actor.Should().BeNull();
        redacted.Findings[0].ActorEmail.Should().BeNull();
    }
}

public sealed class TransportValidationTests
{
    [Fact]
    public void Https_is_accepted()
    {
        var options = new AzureDevOpsServerOptions { BaseUrl = "https://devops.local" };

        options.ValidateTransport().Should().BeNull();
    }

    [Fact]
    public void Plain_http_is_rejected_by_default()
    {
        var options = new AzureDevOpsServerOptions { BaseUrl = "http://devops.local:8080" };

        options.ValidateTransport().Should().NotBeNull();
    }

    [Fact]
    public void Plain_http_requires_explicit_opt_in()
    {
        var options = new AzureDevOpsServerOptions
        {
            BaseUrl = "http://devops.local:8080",
            AllowInsecureHttp = true,
        };

        options.ValidateTransport().Should().BeNull();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("pas-une-url")]
    public void Unsupported_schemes_are_rejected(string url)
    {
        var options = new AzureDevOpsServerOptions { BaseUrl = url, AllowInsecureHttp = true };

        options.ValidateTransport().Should().NotBeNull();
    }
}
