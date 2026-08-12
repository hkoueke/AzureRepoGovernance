using GovernanceAuditor.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GovernanceAuditor.Reporting;

/// <summary>Enregistrement du générateur de rapport dans le conteneur DI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Enregistre le générateur de rapport Markdown.</summary>
    public static IServiceCollection AddMarkdownReporting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IReportGenerator, MarkdownReportGenerator>();
        return services;
    }
}
