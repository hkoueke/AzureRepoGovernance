using System.ComponentModel.DataAnnotations;

namespace GovernanceAuditor.Core.Options;

/// <summary>Connexion au serveur Azure DevOps Server (on-premises).</summary>
public sealed class AzureDevOpsServerOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "AzureDevOpsServer";

    /// <summary>URL de base du serveur (ex. « https://devops.entreprise.local »).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>Collection (ex. « DefaultCollection »).</summary>
    [Required]
    public string Collection { get; init; } = "DefaultCollection";

    /// <summary>Version de l'API REST. Pour Azure DevOps Server 2022.2 : « 7.1 ».</summary>
    [Required]
    public string ApiVersion { get; init; } = "7.1";

    /// <summary>
    /// Autorise explicitement une URL en HTTP simple. Faux par défaut : en
    /// authentification Windows intégrée, le canal en clair exposerait la
    /// négociation NTLM/Kerberos à un observateur du réseau.
    /// </summary>
    public bool AllowInsecureHttp { get; init; }

    /// <summary>
    /// Valide la cohérence de l'URL et du mode de transport.
    /// Renvoie le motif de rejet, ou <c>null</c> si la configuration est acceptable.
    /// </summary>
    public string? ValidateTransport()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
        {
            return "AzureDevOpsServer:BaseUrl doit être une URL absolue (ex. « https://devops.entreprise.local »).";
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return AllowInsecureHttp
                ? null
                : "AzureDevOpsServer:BaseUrl utilise HTTP en clair. En authentification Windows, "
                  + "cela expose la négociation NTLM/Kerberos. Utilisez HTTPS, ou activez "
                  + "explicitement AzureDevOpsServer:AllowInsecureHttp si le réseau est maîtrisé.";
        }

        return $"Schéma d'URL non supporté : « {uri.Scheme} ». Attendu : https (ou http en opt-in).";
    }
}

/// <summary>
/// Périmètre d'analyse. Aucun filtre par dépôt : le serveur ne renvoie déjà
/// que les ressources autorisées pour l'identité courante.
/// </summary>
public sealed class ScopeOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Scope";

    /// <summary>
    /// Projets à analyser. Liste vide = tous les projets accessibles de la collection.
    /// </summary>
    public IReadOnlyList<string> Projects { get; init; } = [];
}

/// <summary>Seuils des règles de gouvernance.</summary>
public sealed class RulesOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Rules";

    /// <summary>Nombre de jours d'inactivité au-delà duquel une branche est « obsolète ».</summary>
    [Range(1, 3650)]
    public int BranchInactiveDays { get; init; } = 90;

    /// <summary>Nombre de jours d'inactivité au-delà duquel une branche est « abandonnée ».</summary>
    [Range(1, 3650)]
    public int BranchAbandonedDays { get; init; } = 120;

    /// <summary>Noms courts des branches protégées (ex. main, master, develop).</summary>
    public IReadOnlyList<string> ProtectedBranches { get; init; } = ["main", "master", "develop"];

    /// <summary>Nombre minimal de relecteurs attendu sur une branche protégée.</summary>
    [Range(1, 100)]
    public int RequiredReviewers { get; init; } = 2;

    /// <summary>Nombre d'échecs consécutifs de pipeline jugé critique.</summary>
    [Range(1, 100)]
    public int PipelineFailureThreshold { get; init; } = 3;

    /// <summary>Nombre de jours d'inactivité au-delà duquel une pull request est « inactive ».</summary>
    [Range(1, 3650)]
    public int PullRequestStaleDays { get; init; } = 30;
}

/// <summary>Paramètres d'exécution et de résilience.</summary>
public sealed class ExecutionOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Execution";

    /// <summary>Degré de parallélisme maximal pour la collecte.</summary>
    [Range(1, 20)]
    public int MaxDegreeOfParallelism { get; init; } = 5;

    /// <summary>Délai d'expiration d'une requête HTTP, en secondes.</summary>
    [Range(1, 600)]
    public int HttpTimeoutSeconds { get; init; } = 30;

    /// <summary>Délai d'expiration global de l'exécution, en minutes.</summary>
    [Range(1, 600)]
    public int GlobalTimeoutMinutes { get; init; } = 10;

    /// <summary>Si vrai, la présence d'un finding critique fait échouer l'exécution (code 1).</summary>
    public bool FailOnCritical { get; init; } = true;

    /// <summary>Ratio maximal toléré de dépôts en échec avant d'échouer (code 3).</summary>
    [Range(0d, 1d)]
    public double MaxRepositoryFailureRatio { get; init; } = 0.10;

    /// <summary>Nombre d'exécutions récentes de pipeline à inspecter.</summary>
    [Range(1, 100)]
    public int RecentRunsToInspect { get; init; } = 10;
}

/// <summary>Paramètres de confidentialité.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Privacy";

    /// <summary>Si vrai, les acteurs (noms / e-mails) sont pseudonymisés dans le rapport.</summary>
    public bool RedactActors { get; init; }
}

/// <summary>Paramètres de génération du rapport.</summary>
public sealed class ReportingOptions
{
    /// <summary>Nom de la section de configuration.</summary>
    public const string SectionName = "Reporting";

    /// <summary>Format du rapport (« Markdown » par défaut).</summary>
    [Required]
    public string Format { get; init; } = "Markdown";

    /// <summary>Répertoire de sortie du rapport.</summary>
    [Required]
    public string OutputDirectory { get; init; } = "./reports";
}
