using System.Globalization;
using System.Text;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;

namespace GovernanceAuditor.Reporting;

/// <summary>
/// Génère un rapport de gouvernance au format Markdown à partir d'un
/// <see cref="AuditRunResult"/>. La sortie est déterministe (tri stable),
/// ce qui la rend testable par capture (snapshot).
/// </summary>
public sealed class MarkdownReportGenerator : IReportGenerator
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss 'UTC'";
    private const string DayFormat = "yyyy-MM-dd";

    private readonly TimeProvider _timeProvider;

    /// <summary>Crée le générateur avec la source de temps injectée (horodatage du rapport).</summary>
    public MarkdownReportGenerator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Format => "Markdown";

    /// <inheritdoc />
    public async Task WriteAsync(AuditRunResult result, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();

        var content = BuildReport(result);

        // UTF-8 sans BOM ; on laisse le flux ouvert (sa fermeture appartient à l'appelant).
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var writer = new StreamWriter(output, encoding, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
        {
            await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private string BuildReport(AuditRunResult result)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Rapport de gouvernance");
        builder.AppendLine();
        builder.Append("_Généré le ")
            .Append(_timeProvider.GetUtcNow().ToString(DateFormat, CultureInfo.InvariantCulture))
            .AppendLine("_");
        builder.AppendLine();

        AppendSummary(builder, result);
        AppendFindings(builder, "Findings critiques", result.Findings, Severity.Critical);
        AppendFindings(builder, "Avertissements", result.Findings, Severity.Warning);
        AppendFindings(builder, "Informations", result.Findings, Severity.Info);
        AppendErrors(builder, result.Errors);
        AppendSkipped(builder, result.Skipped);
        AppendRepositoryDetails(builder, result);

        return builder.ToString();
    }

    private static void AppendSummary(StringBuilder builder, AuditRunResult result)
    {
        builder.AppendLine("## Synthèse");
        builder.AppendLine();
        AppendBullet(builder, "Dépôts analysés", Inv(result.RepositoriesAnalyzed));
        AppendBullet(builder, "Dépôts en échec de collecte", Inv(result.RepositoriesFailed));

        if (result.RepositoriesSkipped > 0)
        {
            AppendBullet(builder, "Dépôts écartés avant collecte", Inv(result.RepositoriesSkipped));
        }

        AppendBullet(builder, "Findings critiques", Inv(Count(result, Severity.Critical)));
        AppendBullet(builder, "Avertissements", Inv(Count(result, Severity.Warning)));
        AppendBullet(builder, "Durée", result.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture));
        builder.AppendLine();
    }

    private static void AppendFindings(StringBuilder builder, string title, IReadOnlyList<AuditFinding> findings, Severity severity)
    {
        var selected = findings.Where(f => f.Severity == severity).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        builder.Append("## ").AppendLine(title);
        builder.AppendLine();

        var byRepository = selected
            .GroupBy(f => f.Repository, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byRepository)
        {
            builder.Append("### ").AppendLine(MarkdownText.Inline(group.Key));
            builder.AppendLine();

            foreach (var finding in group.OrderBy(f => f.Category, StringComparer.OrdinalIgnoreCase))
            {
                AppendFinding(builder, finding);
            }

            builder.AppendLine();
        }
    }

    private static void AppendFinding(StringBuilder builder, AuditFinding finding)
    {
        builder.Append("- **").Append(MarkdownText.Inline(finding.Category)).Append("** — ").AppendLine(MarkdownText.Inline(finding.Message));
        builder.Append("  - Recommandation : ").AppendLine(MarkdownText.Inline(finding.Recommendation));

        AppendOptional(builder, "Branche", finding.Branch);
        AppendOptional(builder, "Pull request", finding.PullRequest);
        AppendOptional(builder, "Pipeline", finding.Pipeline);

        var responsible = FormatResponsible(finding.Actor, finding.ActorEmail);
        AppendOptional(builder, "Responsable", responsible);

        if (finding.Timestamp is { } timestamp)
        {
            AppendOptional(builder, "Horodatage", timestamp.ToString(DayFormat, CultureInfo.InvariantCulture));
        }
    }

    private static void AppendErrors(StringBuilder builder, IReadOnlyList<CollectionError> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Erreurs de collecte");
        builder.AppendLine();

        foreach (var error in errors.OrderBy(e => e.Repository, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- **").Append(MarkdownText.Inline(error.Repository)).Append("** : ").AppendLine(MarkdownText.Inline(error.Message));
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Liste les dépôts écartés avant toute collecte. Les nommer évite qu'une absence
    /// du rapport soit interprétée comme un oubli de l'outil.
    /// </summary>
    private static void AppendSkipped(StringBuilder builder, IReadOnlyList<SkippedRepository> skipped)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Dépôts écartés");
        builder.AppendLine();

        foreach (var entry in skipped.OrderBy(s => s.Repository, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("- **").Append(MarkdownText.Inline(entry.Repository)).Append("** : ").AppendLine(MarkdownText.Inline(entry.Reason));
        }

        builder.AppendLine();
    }

    private static void AppendRepositoryDetails(StringBuilder builder, AuditRunResult result)
    {
        var repositories = result.Findings
            .Select(f => f.Repository)
            .Concat(result.Errors.Select(e => e.Repository))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (repositories.Count == 0)
        {
            return;
        }

        builder.AppendLine("## Détail par dépôt");
        builder.AppendLine();

        foreach (var repository in repositories)
        {
            var critical = result.Findings.Count(f => f.Severity == Severity.Critical && Same(f.Repository, repository));
            var warning = result.Findings.Count(f => f.Severity == Severity.Warning && Same(f.Repository, repository));

            builder.Append("- **").Append(MarkdownText.Inline(repository)).Append("** — ")
                .Append(Inv(critical)).Append(" critique(s), ")
                .Append(Inv(warning)).AppendLine(" avertissement(s)");
        }

        builder.AppendLine();
    }

    private static void AppendBullet(StringBuilder builder, string label, string value) =>
        builder.Append("- ").Append(label).Append(" : ").AppendLine(value);

    private static void AppendOptional(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append("  - ").Append(label).Append(" : ").AppendLine(MarkdownText.Inline(value));
        }
    }

    private static string? FormatResponsible(string? actor, string? email)
    {
        if (!string.IsNullOrWhiteSpace(actor) && !string.IsNullOrWhiteSpace(email))
        {
            return $"{actor} ({email})";
        }

        return !string.IsNullOrWhiteSpace(actor) ? actor : email;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static int Count(AuditRunResult result, Severity severity) =>
        result.Findings.Count(f => f.Severity == severity);

    private static string Inv(int value) => value.ToString(CultureInfo.InvariantCulture);
}
