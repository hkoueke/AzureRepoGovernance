using System.Globalization;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernanceAuditor.Console;

/// <summary>Écrit le rapport dans le dossier de sortie configuré.</summary>
internal sealed class ReportWriter
{
    private readonly IReportGenerator _generator;
    private readonly ReportingOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReportWriter> _logger;

    public ReportWriter(
        IReportGenerator generator,
        IOptions<ReportingOptions> options,
        TimeProvider timeProvider,
        ILogger<ReportWriter> logger)
    {
        ArgumentNullException.ThrowIfNull(generator);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _generator = generator;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Écrit le rapport et renvoie son chemin complet.</summary>
    public async Task<string> WriteAsync(AuditRunResult result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Chemin résolu en absolu : le dossier de sortie provient de la configuration
        // et ne doit pas dépendre du répertoire courant au moment de l'exécution.
        var directory = Path.GetFullPath(_options.OutputDirectory);
        Directory.CreateDirectory(directory);

        var path = BuildPath(directory);

        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            await _generator.WriteAsync(result, stream, cancellationToken).ConfigureAwait(false);
        }

        Log.ReportWritten(_logger, path);
        return path;
    }

    /// <summary>
    /// Nom conforme à la spécification (« governance-report-yyyyMMdd.md »).
    /// Si le fichier du jour existe déjà, l'heure est ajoutée : une seconde
    /// exécution ne doit jamais détruire silencieusement le rapport précédent.
    /// </summary>
    private string BuildPath(string directory)
    {
        var now = _timeProvider.GetLocalNow();
        var candidate = Path.Combine(
            directory,
            $"governance-report-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.md");

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        return Path.Combine(
            directory,
            $"governance-report-{now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.md");
    }
}
