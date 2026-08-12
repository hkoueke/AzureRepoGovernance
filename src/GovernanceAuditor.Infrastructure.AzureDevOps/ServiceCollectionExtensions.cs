using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Options;
using GovernanceAuditor.Infrastructure.AzureDevOps.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GovernanceAuditor.Infrastructure.AzureDevOps;

/// <summary>Méthodes d'enregistrement de l'infrastructure Azure DevOps dans le conteneur DI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le client Azure DevOps en lecture seule : options liées et validées,
    /// HttpClient en authentification Windows intégrée, et pile de résilience standard.
    /// </summary>
    public static IServiceCollection AddAzureDevOpsInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<AzureDevOpsServerOptions>()
            .BindConfiguration(AzureDevOpsServerOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<ScopeOptions>()
            .BindConfiguration(ScopeOptions.SectionName);

        services.AddOptions<ExecutionOptions>()
            .BindConfiguration(ExecutionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(sp =>
            new ApiRoutes(sp.GetRequiredService<IOptions<AzureDevOpsServerOptions>>().Value));

        services.AddHttpClient<AzureDevOpsApiReader>(static (sp, http) =>
        {
            var server = sp.GetRequiredService<IOptions<AzureDevOpsServerOptions>>().Value;
            var execution = sp.GetRequiredService<IOptions<ExecutionOptions>>().Value;
            http.BaseAddress = new Uri(EnsureTrailingSlash(server.BaseUrl));
            http.Timeout = TimeSpan.FromSeconds(execution.HttpTimeoutSeconds);
        })
        .ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler
        {
            // Authentification Windows intégrée (NTLM/Kerberos) — aucun secret, aucun PAT.
            UseDefaultCredentials = true,
            PreAuthenticate = true,
            AllowAutoRedirect = false,
        })
        .AddStandardResilienceHandler();

        services.AddSingleton<IAzureDevOpsClient, AzureDevOpsClient>();

        return services;
    }

    private static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";
}
