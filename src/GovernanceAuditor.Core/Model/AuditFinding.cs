namespace GovernanceAuditor.Core.Model;

/// <summary>
/// Niveau de gravité d'une anomalie de gouvernance détectée.
/// </summary>
public enum Severity
{
    /// <summary>Observation informative, aucune action immédiate attendue.</summary>
    Info,

    /// <summary>Anomalie pour laquelle une action est recommandée.</summary>
    Warning,

    /// <summary>Violation de gouvernance ; une action est attendue.</summary>
    Critical,
}

/// <summary>
/// Anomalie unitaire produite par un analyseur. Immuable.
/// Les champs <see cref="Actor"/> et <see cref="ActorEmail"/> sont des données
/// personnelles (PII) : ils peuvent être pseudonymisés selon la configuration de confidentialité.
/// </summary>
public sealed record AuditFinding
{
    /// <summary>Gravité de l'anomalie.</summary>
    public required Severity Severity { get; init; }

    /// <summary>Catégorie métier de la règle (ex. « StaleBranch »).</summary>
    public required string Category { get; init; }

    /// <summary>Nom du dépôt concerné.</summary>
    public required string Repository { get; init; }

    /// <summary>Branche concernée, le cas échéant.</summary>
    public string? Branch { get; init; }

    /// <summary>Identifiant de la pull request concernée, le cas échéant.</summary>
    public string? PullRequest { get; init; }

    /// <summary>Nom du pipeline concerné, le cas échéant.</summary>
    public string? Pipeline { get; init; }

    /// <summary>Nom de l'acteur responsable (PII).</summary>
    public string? Actor { get; init; }

    /// <summary>Adresse e-mail de l'acteur responsable (PII).</summary>
    public string? ActorEmail { get; init; }

    /// <summary>Date pertinente associée à l'anomalie (dernier commit, dernière activité…).</summary>
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Message décrivant l'anomalie de façon lisible.</summary>
    public required string Message { get; init; }

    /// <summary>Recommandation d'action pour corriger l'anomalie.</summary>
    public required string Recommendation { get; init; }
}
