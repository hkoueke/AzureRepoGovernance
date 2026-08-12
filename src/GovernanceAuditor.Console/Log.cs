using Microsoft.Extensions.Logging;

namespace GovernanceAuditor.Console;

/// <summary>
/// Messages de journal générés à la compilation (source generator) : évite le
/// coût de la journalisation par interpolation et satisfait CA1848.
/// </summary>
/// <remarks>
/// Ces messages partent sur la sortie d'erreur ; la sortie standard est réservée
/// au rendu destiné à l'utilisateur.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Dépôts découverts : {Count}")]
    public static partial void RepositoriesDiscovered(ILogger logger, int count);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Analyse terminée : {Analyzed} dépôt(s) analysé(s), {Failed} en échec")]
    public static partial void AnalysisCompleted(ILogger logger, int analyzed, int failed);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Dépôt « {Repository} » ignoré : la collecte a échoué")]
    public static partial void RepositoryFailed(ILogger logger, string repository, Exception exception);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Rapport écrit : {Path}")]
    public static partial void ReportWritten(ILogger logger, string path);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Transport non chiffré autorisé explicitement : la négociation Windows circule en clair")]
    public static partial void InsecureTransportAllowed(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "Erreur fatale pendant l'exécution")]
    public static partial void FatalError(ILogger logger, Exception exception);
}
