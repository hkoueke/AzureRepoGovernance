using FluentAssertions;
using GovernanceAuditor.Console;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GovernanceAuditor.Tests.Orchestration;

/// <summary>
/// « --projets » doit REMPLACER le périmètre configuré. Les fournisseurs de
/// configuration fusionnent clé par clé : sans blanchiment des index excédentaires,
/// l'option ne pouvait qu'écraser les N premières entrées d'appsettings.json et
/// laissait subsister les suivantes.
/// </summary>
public sealed class ProjectScopeOverrideTests
{
    private const string Prefix = "Scope:Projects";

    private static ConfigurationManager ConfiguredWith(params string[] projects)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            projects
                .Select((name, index) => new KeyValuePair<string, string?>($"{Prefix}:{index}", name))
                .ToList());

        return configuration;
    }

    private static List<string?> ProjectsIn(ConfigurationManager configuration) =>
        configuration.GetSection(Prefix).GetChildren().Select(c => c.Value).ToList();

    [Fact]
    public void Fewer_projects_on_the_command_line_blank_out_the_surplus()
    {
        using var configuration = ConfiguredWith("Fwfc - Loc", "Fwfc-Spie");

        Program.ApplyProjectScope(configuration, ["--projets", "Paie"]);

        // Sans blanchiment, on obtiendrait « Paie » + « Fwfc-Spie ».
        ProjectsIn(configuration).Should().Equal("Paie", string.Empty);
    }

    [Fact]
    public void An_empty_value_clears_the_whole_scope()
    {
        using var configuration = ConfiguredWith("Fwfc - Loc", "Fwfc-Spie");

        Program.ApplyProjectScope(configuration, ["--projets", ""]);

        ProjectsIn(configuration).Should().AllSatisfy(v => v.Should().BeEmpty());
    }

    [Fact]
    public void A_following_option_is_not_mistaken_for_a_project_name()
    {
        using var configuration = ConfiguredWith("Fwfc - Loc");

        Program.ApplyProjectScope(configuration, ["--projets", "--anonymiser"]);

        ProjectsIn(configuration).Should().AllSatisfy(v => v.Should().BeEmpty());
    }

    [Fact]
    public void More_projects_than_configured_are_all_kept()
    {
        using var configuration = ConfiguredWith("Fwfc - Loc");

        Program.ApplyProjectScope(configuration, ["--projets", "Paie, Facturation ,RH"]);

        ProjectsIn(configuration).Should().Equal("Paie", "Facturation", "RH");
    }

    [Fact]
    public void Absent_option_leaves_the_configured_scope_untouched()
    {
        using var configuration = ConfiguredWith("Fwfc - Loc", "Fwfc-Spie");

        Program.ApplyProjectScope(configuration, ["--anonymiser"]);

        ProjectsIn(configuration).Should().Equal("Fwfc - Loc", "Fwfc-Spie");
    }
}
