namespace GovernanceAuditor.Core.Model;

/// <summary>Informations sur un dépôt Git.</summary>
public sealed record RepositoryInfo
{
    /// <summary>Identifiant technique du dépôt.</summary>
    public required string Id { get; init; }

    /// <summary>Nom du dépôt.</summary>
    public required string Name { get; init; }

    /// <summary>Nom du projet d'appartenance (requis pour construire les URLs à portée projet).</summary>
    public required string ProjectName { get; init; }

    /// <summary>Identifiant du projet d'appartenance.</summary>
    public string? ProjectId { get; init; }

    /// <summary>URL web du dépôt.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// Branche par défaut, au format complet « refs/heads/… ». Absente lorsque le
    /// dépôt n'a jamais été initialisé : Azure DevOps omet alors le champ.
    /// </summary>
    public string? DefaultBranch { get; init; }

    /// <summary>Vrai si le dépôt est désactivé côté serveur.</summary>
    public bool IsDisabled { get; init; }

    /// <summary>Taille du dépôt en octets, lorsque le serveur la renvoie.</summary>
    public long? SizeInBytes { get; init; }
}

/// <summary>Informations sur une branche, relatives à la branche par défaut.</summary>
public sealed record BranchInfo
{
    /// <summary>Nom court de la branche (ex. « feature/x »).</summary>
    public required string Name { get; init; }

    /// <summary>Indique si la branche est verrouillée.</summary>
    public bool IsLocked { get; init; }

    /// <summary>Identifiant du créateur de la branche (best-effort, peut être absent).</summary>
    public string? CreatorId { get; init; }

    /// <summary>Identifiant du dernier commit.</summary>
    public string? LastCommitId { get; init; }

    /// <summary>Auteur du dernier commit.</summary>
    public string? LastCommitAuthor { get; init; }

    /// <summary>E-mail de l'auteur du dernier commit (PII).</summary>
    public string? LastCommitAuthorEmail { get; init; }

    /// <summary>Date du dernier commit.</summary>
    public DateTimeOffset? LastCommitDate { get; init; }

    /// <summary>Nombre de commits uniques à la branche par rapport à la branche par défaut.
    /// Une valeur de 0 signifie que la branche est entièrement contenue dans la branche par défaut (« mergée »).</summary>
    public int AheadCount { get; init; }

    /// <summary>Nombre de commits de retard par rapport à la branche par défaut.</summary>
    public int BehindCount { get; init; }
}

/// <summary>Vote d'un relecteur sur une pull request.</summary>
public sealed record ReviewerVote
{
    /// <summary>Identifiant du relecteur.</summary>
    public required string ReviewerId { get; init; }

    /// <summary>Nom d'affichage du relecteur.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Valeur du vote : 10 approuvé, 5 approuvé avec suggestions, 0 aucun, -5 attente, -10 rejet.</summary>
    public required int Vote { get; init; }
}

/// <summary>Informations sur une pull request.</summary>
public sealed record PullRequestInfo
{
    /// <summary>Identifiant de la pull request.</summary>
    public required int Id { get; init; }

    /// <summary>Titre de la pull request.</summary>
    public required string Title { get; init; }

    /// <summary>Statut : « active », « completed » ou « abandoned ».</summary>
    public required string Status { get; init; }

    /// <summary>Identifiant de l'auteur.</summary>
    public string? AuthorId { get; init; }

    /// <summary>Nom d'affichage de l'auteur (PII).</summary>
    public string? AuthorDisplayName { get; init; }

    /// <summary>Date de création.</summary>
    public DateTimeOffset CreationDate { get; init; }

    /// <summary>Date de dernière activité.</summary>
    public DateTimeOffset? LastActivityDate { get; init; }

    /// <summary>Branche source, au format complet « refs/heads/… ».</summary>
    public required string SourceBranch { get; init; }

    /// <summary>Branche cible, au format complet « refs/heads/… ».</summary>
    public required string TargetBranch { get; init; }

    /// <summary>Votes des relecteurs.</summary>
    public required IReadOnlyList<ReviewerVote> Reviewers { get; init; }
}

/// <summary>Exécution (run) d'un pipeline.</summary>
public sealed record PipelineRun
{
    /// <summary>Identifiant de l'exécution.</summary>
    public required long RunId { get; init; }

    /// <summary>Branche déclenchante.</summary>
    public string? Branch { get; init; }

    /// <summary>Statut d'exécution (ex. « completed », « inProgress »).</summary>
    public required string Status { get; init; }

    /// <summary>Résultat : « succeeded », « failed », « canceled », « partiallySucceeded ».</summary>
    public string? Result { get; init; }

    /// <summary>Heure de début.</summary>
    public DateTimeOffset? StartTime { get; init; }

    /// <summary>Heure de fin.</summary>
    public DateTimeOffset? FinishTime { get; init; }
}

/// <summary>Informations sur un pipeline associé à un dépôt.</summary>
public sealed record PipelineInfo
{
    /// <summary>Identifiant du pipeline (définition).</summary>
    public required int PipelineId { get; init; }

    /// <summary>Nom du pipeline.</summary>
    public required string PipelineName { get; init; }

    /// <summary>Indique si le pipeline a déjà été exécuté au moins une fois.</summary>
    public bool HasEverRun { get; init; }

    /// <summary>Exécutions récentes, triées de la plus récente à la plus ancienne.</summary>
    public required IReadOnlyList<PipelineRun> RecentRuns { get; init; }
}

/// <summary>Configuration d'une policy de branche Azure DevOps.</summary>
public sealed record PolicyInfo
{
    /// <summary>GUID du type de policy.</summary>
    public required string PolicyTypeId { get; init; }

    /// <summary>Nom d'affichage du type de policy.</summary>
    public string? PolicyTypeDisplayName { get; init; }

    /// <summary>Indique si la policy est activée.</summary>
    public bool Enabled { get; init; }

    /// <summary>Indique si la policy est bloquante.</summary>
    public bool Blocking { get; init; }

    /// <summary>Identifiant du dépôt visé par la portée de la policy.</summary>
    public string? ScopeRepositoryId { get; init; }

    /// <summary>Référence de branche visée (ex. « refs/heads/main »).</summary>
    public string? ScopeRefName { get; init; }

    /// <summary>Nombre minimal d'approbateurs (pour la policy « minimum reviewers »).</summary>
    public int? MinimumApproverCount { get; init; }
}

