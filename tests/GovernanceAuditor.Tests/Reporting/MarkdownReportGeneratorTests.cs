using System.Text;
using FluentAssertions;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Reporting;
using GovernanceAuditor.Tests.TestData;
using Xunit;

namespace GovernanceAuditor.Tests.Reporting;

public sealed class MarkdownReportGeneratorTests
{
    private readonly MarkdownReportGenerator _sut = new(Build.Clock());

    private static AuditRunResult SampleResult() => new()
    {
        Findings =
        [
            new AuditFinding
            {
                Severity = Severity.Critical,
                Category = "MissingBuildValidation",
                Repository = "Alpha",
                Branch = "main",
                Actor = "Jane Doe",
                ActorEmail = "jane.doe@entreprise.local",
                Message = "Aucune policy de validation par build sur la branche protégée.",
                Recommendation = "Créer une policy de build validation obligatoire.",
            },
            new AuditFinding
            {
                Severity = Severity.Warning,
                Category = "StaleBranch",
                Repository = "Beta",
                Branch = "feature/x",
                Message = "Branche inactive depuis 132 jours.",
                Recommendation = "Supprimer ou verrouiller la branche.",
            },
        ],
        Errors =
        [
            new CollectionError { Repository = "Gamma", Message = "401 Unauthorized" },
        ],
        RepositoriesAnalyzed = 2,
        RepositoriesFailed = 1,
        Duration = TimeSpan.FromSeconds(83),
    };

    private async Task<string> RenderAsync(AuditRunResult result)
    {
        using var stream = new MemoryStream();
        await _sut.WriteAsync(result, stream, CancellationToken.None);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Report_contains_title_and_summary_counts()
    {
        var content = await RenderAsync(SampleResult());

        content.Should().Contain("# Rapport de gouvernance");
        content.Should().Contain("Dépôts analysés : 2");
        content.Should().Contain("Dépôts en échec de collecte : 1");
        content.Should().Contain("Findings critiques : 1");
        content.Should().Contain("Avertissements : 1");
        content.Should().Contain("Durée : 00:01:23");
    }

    [Fact]
    public async Task Report_orders_critical_section_before_warnings()
    {
        var content = await RenderAsync(SampleResult());

        var criticalIndex = content.IndexOf("## Findings critiques", StringComparison.Ordinal);
        var warningIndex = content.IndexOf("## Avertissements", StringComparison.Ordinal);

        criticalIndex.Should().BeGreaterThan(0);
        warningIndex.Should().BeGreaterThan(criticalIndex);
    }

    [Fact]
    public async Task Report_includes_finding_details_and_responsible()
    {
        var content = await RenderAsync(SampleResult());

        content.Should().Contain("### Alpha");
        content.Should().Contain("**MissingBuildValidation**");
        content.Should().Contain("Responsable : Jane Doe (jane.doe@entreprise.local)");
    }

    [Fact]
    public async Task Report_neutralises_untrusted_text_from_azure_devops()
    {
        // Un nom de branche est du texte libre : il ne doit pas pouvoir injecter
        // de structure Markdown ni casser la mise en forme par un retour à la ligne.
        var result = new AuditRunResult
        {
            Findings =
            [
                new AuditFinding
                {
                    Severity = Severity.Warning,
                    Category = "StaleBranch",
                    Repository = "Alpha",
                    Branch = "feature/x\n## Faux titre",
                    Message = "Branche [piégée](http://malveillant.example) inactive.",
                    Recommendation = "Supprimer la branche.",
                },
            ],
            Errors = [],
            RepositoriesAnalyzed = 1,
            RepositoriesFailed = 0,
            Duration = TimeSpan.Zero,
        };

        var content = await RenderAsync(result);

        content.Should().NotContain("\n## Faux titre");
        content.Should().NotContain("[piégée](http://malveillant.example)");
        content.Should().Contain(@"\[piégée\]");
    }

    [Fact]
    public async Task Report_lists_collection_errors_and_repository_details()
    {
        var content = await RenderAsync(SampleResult());

        content.Should().Contain("## Erreurs de collecte");
        content.Should().Contain("**Gamma** : 401 Unauthorized");
        content.Should().Contain("## Détail par dépôt");
    }

    [Fact]
    public async Task Empty_result_still_produces_title_and_summary()
    {
        var empty = new AuditRunResult
        {
            Findings = [],
            Errors = [],
            RepositoriesAnalyzed = 0,
            RepositoriesFailed = 0,
            Duration = TimeSpan.Zero,
        };

        var content = await RenderAsync(empty);

        content.Should().Contain("# Rapport de gouvernance");
        content.Should().Contain("## Synthèse");
        content.Should().NotContain("## Findings critiques");
    }
}
