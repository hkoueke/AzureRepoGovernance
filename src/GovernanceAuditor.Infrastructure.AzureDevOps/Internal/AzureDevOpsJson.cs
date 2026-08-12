using System.Text.Json;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Internal;

/// <summary>Options de désérialisation JSON communes aux appels Azure DevOps.</summary>
internal static class AzureDevOpsJson
{
    /// <summary>Insensible à la casse (l'API renvoie du camelCase, nos DTOs sont en PascalCase).</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };
}
