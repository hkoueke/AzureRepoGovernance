using Microsoft.Extensions.Logging;

namespace GovernanceAuditor.Infrastructure.AzureDevOps.Internal;

/// <summary>
/// Messages de journal de la couche d'accès, générés à la compilation
/// (source generator) : évite l'interpolation systématique et satisfait CA1848.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2000,
        Level = LogLevel.Information,
        Message = "Dépôts renvoyés par la collection : {Total}")]
    public static partial void RepositoriesReturned(ILogger logger, int total);

    // Les projets demandés ne sont volontairement pas repris ici : la bannière les
    // affiche déjà, et chaque projet sans correspondance fait l'objet d'un avertissement
    // dédié. Les concaténer à l'appel coûterait à chaque exécution, journal actif ou non.
    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Filtrage de périmètre : {Retained} dépôt(s) retenu(s) sur {Total}")]
    public static partial void ScopeFilterApplied(ILogger logger, int retained, int total);

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Warning,
        Message = "Projet « {Project} » demandé dans Scope:Projects mais aucun dépôt ne lui correspond — nom inexact, projet vide, ou droits de lecture insuffisants")]
    public static partial void ScopeProjectNotMatched(ILogger logger, string project);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Aucun dépôt retenu : les {Count} projet(s) demandé(s) ne correspondent à aucun dépôt accessible")]
    public static partial void ScopeMatchedNothing(ILogger logger, int count);
}
