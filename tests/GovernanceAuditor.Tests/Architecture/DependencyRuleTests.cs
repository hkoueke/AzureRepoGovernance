using FluentAssertions;
using GovernanceAuditor.Core.Model;
using NetArchTest.Rules;
using Xunit;

namespace GovernanceAuditor.Tests.Architecture;

/// <summary>
/// Verrouille la règle de dépendance : le projet Core ne doit dépendre ni de
/// l'infrastructure, ni d'un client HTTP. Tout manquement fait échouer le build.
/// </summary>
public sealed class DependencyRuleTests
{
    [Fact]
    public void Core_must_not_depend_on_infrastructure_or_http()
    {
        var coreAssembly = typeof(RepositoryContext).Assembly;

        var result = Types.InAssembly(coreAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "System.Net.Http",
                "GovernanceAuditor.Infrastructure",
                "GovernanceAuditor.Infrastructure.AzureDevOps")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "le projet Core doit rester pur (aucun appel réseau, aucune infrastructure)");
    }
}
