using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Abstractions;

/// <summary>
/// Accès en lecture seule aux données d'Azure DevOps Server.
/// Toutes les méthodes n'effectuent que des lectures (GET).
/// </summary>
public interface IAzureDevOpsClient
{
    /// <summary>Liste les dépôts accessibles dans le périmètre configuré.</summary>
    public Task<IReadOnlyList<RepositoryInfo>> GetRepositoriesAsync(CancellationToken cancellationToken);

    /// <summary>Liste les branches d'un dépôt.</summary>
    public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(RepositoryInfo repository, CancellationToken cancellationToken);

    /// <summary>Liste les pull requests actives d'un dépôt.</summary>
    public Task<IReadOnlyList<PullRequestInfo>> GetPullRequestsAsync(RepositoryInfo repository, CancellationToken cancellationToken);

    /// <summary>Liste les pipelines associés à un dépôt, avec leurs exécutions récentes.</summary>
    public Task<IReadOnlyList<PipelineInfo>> GetPipelinesAsync(RepositoryInfo repository, CancellationToken cancellationToken);

    /// <summary>Liste les policies applicables à un dépôt.</summary>
    public Task<IReadOnlyList<PolicyInfo>> GetPoliciesAsync(RepositoryInfo repository, CancellationToken cancellationToken);
}

/// <summary>
/// Analyseur d'une règle de gouvernance. Une implémentation = une règle.
/// Ne réalise aucun appel réseau : travaille uniquement sur le <see cref="RepositoryContext"/>.
/// </summary>
public interface IRepositoryAnalyzer
{
    /// <summary>Analyse le contexte et produit les anomalies détectées.</summary>
    public Task<IReadOnlyCollection<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken);
}

/// <summary>Générateur de rapport à partir d'un résultat d'exécution.</summary>
public interface IReportGenerator
{
    /// <summary>Format produit (ex. « Markdown »).</summary>
    public string Format { get; }

    /// <summary>Écrit le rapport sur le flux fourni.</summary>
    public Task WriteAsync(AuditRunResult result, Stream output, CancellationToken cancellationToken);
}

/// <summary>Erreur de collecte isolée à un dépôt (n'interrompt pas les autres).</summary>
public sealed record CollectionError
{
    /// <summary>Nom du dépôt concerné.</summary>
    public required string Repository { get; init; }

    /// <summary>Message décrivant l'erreur.</summary>
    public required string Message { get; init; }
}

/// <summary>
/// Dépôt volontairement écarté de l'analyse, avec le motif. Un dépôt écarté n'est
/// ni un succès ni un échec : l'analyser n'aurait produit aucune information.
/// L'exclusion intervient soit d'emblée (dépôt désactivé, le serveur l'affirme),
/// soit après lecture des branches (dépôt sans aucune branche).
/// </summary>
public sealed record SkippedRepository
{
    /// <summary>Nom du dépôt écarté.</summary>
    public required string Repository { get; init; }

    /// <summary>Motif lisible de l'exclusion.</summary>
    public required string Reason { get; init; }
}

/// <summary>Résultat consolidé d'une exécution de l'auditeur.</summary>
public sealed record AuditRunResult
{
    /// <summary>Anomalies détectées sur l'ensemble des dépôts.</summary>
    public required IReadOnlyList<AuditFinding> Findings { get; init; }

    /// <summary>Erreurs de collecte par dépôt.</summary>
    public required IReadOnlyList<CollectionError> Errors { get; init; }

    /// <summary>Nombre de dépôts analysés avec succès.</summary>
    public required int RepositoriesAnalyzed { get; init; }

    /// <summary>Nombre de dépôts en échec de collecte.</summary>
    public required int RepositoriesFailed { get; init; }

    /// <summary>
    /// Dépôts écartés de l'analyse (désactivés, ou dépourvus de branche).
    /// Vide par défaut : un dépôt écarté ne compte ni comme analysé, ni comme en échec.
    /// </summary>
    public IReadOnlyList<SkippedRepository> Skipped { get; init; } = [];

    /// <summary>Nombre de dépôts écartés de l'analyse.</summary>
    public int RepositoriesSkipped => Skipped.Count;

    /// <summary>Durée totale de l'exécution.</summary>
    public required TimeSpan Duration { get; init; }
}
