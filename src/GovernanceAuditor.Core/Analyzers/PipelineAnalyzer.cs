using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Core.Analyzers;

/// <summary>
/// Analyse les pipelines d'un dépôt : absence de pipeline, pipeline jamais exécuté,
/// dernier run en échec et échecs consécutifs.
/// </summary>
public sealed class PipelineAnalyzer : IRepositoryAnalyzer
{
    private const string FailedResult = "failed";

    /// <inheritdoc />
    public Task<IReadOnlyCollection<AuditFinding>> AnalyzeAsync(RepositoryContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var repository = context.Repository.Name;
        var findings = new List<AuditFinding>();

        if (context.Pipelines.Count == 0)
        {
            findings.Add(new AuditFinding
            {
                Severity = Severity.Critical,
                Category = "MissingPipeline",
                Repository = repository,
                Message = "Aucun pipeline n'est associé au dépôt.",
                Recommendation = "Créer un pipeline d'intégration continue pour ce dépôt.",
            });
            return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
        }

        foreach (var pipeline in context.Pipelines)
        {
            findings.AddRange(EvaluatePipeline(context, pipeline));
        }

        return Task.FromResult<IReadOnlyCollection<AuditFinding>>(findings);
    }

    private static IEnumerable<AuditFinding> EvaluatePipeline(RepositoryContext context, PipelineInfo pipeline)
    {
        var repository = context.Repository.Name;

        if (!pipeline.HasEverRun || pipeline.RecentRuns.Count == 0)
        {
            yield return new AuditFinding
            {
                Severity = Severity.Critical,
                Category = "PipelineNeverRun",
                Repository = repository,
                Pipeline = pipeline.PipelineName,
                Message = "Le pipeline n'a jamais été exécuté.",
                Recommendation = "Exécuter le pipeline et corriger sa configuration si nécessaire.",
            };
            yield break;
        }

        var consecutiveFailures = CountLeadingFailures(pipeline.RecentRuns);

        if (consecutiveFailures >= context.Rules.PipelineFailureThreshold)
        {
            yield return new AuditFinding
            {
                Severity = Severity.Critical,
                Category = "ConsecutivePipelineFailures",
                Repository = repository,
                Pipeline = pipeline.PipelineName,
                Timestamp = pipeline.RecentRuns[0].FinishTime,
                Message = $"{consecutiveFailures} échecs consécutifs du pipeline (seuil : {context.Rules.PipelineFailureThreshold}).",
                Recommendation = "Investiguer la cause des échecs et rétablir le pipeline.",
            };
        }
        else if (IsFailed(pipeline.RecentRuns[0]))
        {
            yield return new AuditFinding
            {
                Severity = Severity.Warning,
                Category = "LastRunFailed",
                Repository = repository,
                Pipeline = pipeline.PipelineName,
                Timestamp = pipeline.RecentRuns[0].FinishTime,
                Message = "La dernière exécution du pipeline a échoué.",
                Recommendation = "Analyser et corriger la dernière exécution en échec.",
            };
        }
    }

    private static int CountLeadingFailures(IReadOnlyList<PipelineRun> runs)
    {
        var count = 0;
        foreach (var run in runs)
        {
            if (!IsFailed(run))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool IsFailed(PipelineRun run) =>
        string.Equals(run.Result, FailedResult, StringComparison.OrdinalIgnoreCase);
}
