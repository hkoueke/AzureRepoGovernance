using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using GovernanceAuditor.Core.Options;

namespace GovernanceAuditor.Console;

/// <summary>Codes de sortie du processus, exploitables par un ordonnanceur.</summary>
internal static class ExitCodes
{
    /// <summary>Aucune anomalie critique.</summary>
    public const int Success = 0;

    /// <summary>Au moins une anomalie critique, ou erreur fatale.</summary>
    public const int Critical = 1;

    /// <summary>Configuration invalide : rien n'a été analysé.</summary>
    public const int ConfigurationError = 2;

    /// <summary>Analyse partielle : trop de dépôts en échec, ou interruption.</summary>
    public const int PartialFailure = 3;
}

/// <summary>Détermine le code de sortie à partir du résultat d'exécution.</summary>
internal static class ExitCodePolicy
{
    /// <summary>
    /// Précédence : une analyse incomplète prime sur les anomalies détectées.
    /// Un résultat partiel ne permet en effet pas d'affirmer l'absence de problème.
    /// </summary>
    public static int Resolve(AuditRunResult result, ExecutionOptions execution)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(execution);

        var total = result.RepositoriesAnalyzed + result.RepositoriesFailed;

        if (total == 0)
        {
            return ExitCodes.Success;
        }

        var failureRatio = (double)result.RepositoriesFailed / total;
        if (failureRatio > execution.MaxRepositoryFailureRatio)
        {
            return ExitCodes.PartialFailure;
        }

        var hasCritical = result.Findings.Any(f => f.Severity == Severity.Critical);
        return execution.FailOnCritical && hasCritical ? ExitCodes.Critical : ExitCodes.Success;
    }
}
