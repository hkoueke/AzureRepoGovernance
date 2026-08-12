using System.Globalization;
using System.Text;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Model;
using SysConsole = System.Console;

namespace GovernanceAuditor.Console;

/// <summary>
/// Rendu console destiné à l'humain (sortie standard). Les diagnostics techniques
/// partent, eux, sur la sortie d'erreur via le journal : la sortie standard reste
/// ainsi lisible et exploitable dans un tube.
/// </summary>
/// <remarks>
/// Se dégrade proprement : pas de couleur si la sortie est redirigée ou si
/// <c>NO_COLOR</c> est défini ; pas de caractères Unicode si la console ne les
/// accepte pas ; pas de réécriture de ligne hors terminal interactif.
/// </remarks>
internal sealed class ConsoleUi
{
    private readonly bool _color;
    private readonly bool _unicode;
    private readonly bool _canRewriteLine;
    private int _lastProgressLength;
    private int _lastMilestone = -1;
    private int _lastDone = -1;

    public ConsoleUi()
    {
        var redirected = SysConsole.IsOutputRedirected;
        var noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        _color = !redirected && !noColor;
        _canRewriteLine = !redirected;
        _unicode = TryEnableUtf8();
    }

    private static bool TryEnableUtf8()
    {
        try
        {
            SysConsole.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            return true;
        }
        catch (IOException)
        {
            return false;   // Console non disponible (service, tube fermé).
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    private string Glyph(string unicode, string ascii) => _unicode ? unicode : ascii;

    private void Write(string text, ConsoleColor? color)
    {
        if (_color && color is { } value)
        {
            var previous = SysConsole.ForegroundColor;
            SysConsole.ForegroundColor = value;
            try
            {
                SysConsole.WriteLine(text);
            }
            finally
            {
                SysConsole.ForegroundColor = previous;
            }

            return;
        }

        SysConsole.WriteLine(text);
    }

    /// <summary>Affiche l'en-tête de l'exécution et le contexte de connexion.</summary>
    public void Banner(string server, string collection, string scope, int parallelism)
    {
        var rule = new string(_unicode ? '─' : '-', 62);

        SysConsole.WriteLine();
        Write($"  {Glyph("▸", ">")} Auditeur de gouvernance des dépôts Azure DevOps", ConsoleColor.Cyan);
        Write($"  {Glyph("▸", ">")} Hervé KOUEKE (herve.kouekekouemeni@cgi.com", ConsoleColor.Cyan);
        Write($"  {rule}", ConsoleColor.DarkGray);
        Write($"  Serveur      : {server}", ConsoleColor.DarkGray);
        Write($"  Collection   : {collection}", ConsoleColor.DarkGray);
        Write($"  Périmètre    : {scope}", ConsoleColor.DarkGray);
        Write($"  Parallélisme : {parallelism.ToString(CultureInfo.CurrentCulture)} dépôts simultanés", ConsoleColor.DarkGray);
        Write($"  Mode         : lecture seule (aucune modification du serveur)", ConsoleColor.DarkGray);
        SysConsole.WriteLine();
    }

    /// <summary>Annonce une étape du traitement.</summary>
    public void Step(string message) => Write($"  {Glyph("•", "*")} {message}", color: null);

    /// <summary>Affiche une information de succès.</summary>
    public void Ok(string message) => Write($"  {Glyph("✓", "OK")} {message}", ConsoleColor.Green);

    /// <summary>Affiche un avertissement.</summary>
    public void Warn(string message) => Write($"  {Glyph("!", "!")} {message}", ConsoleColor.Yellow);

    /// <summary>Met à jour la progression de l'analyse.</summary>
    public void Progress(int done, int total, int failed)
    {
        if (total <= 0)
        {
            return;
        }

        // Les rapports d'avancement proviennent du pool de threads : leur ordre
        // n'est pas garanti. On ignore tout retour en arrière pour éviter que la
        // barre ne recule.
        if (done <= _lastDone)
        {
            return;
        }

        _lastDone = done;

        var percent = done * 100 / total;

        if (!_canRewriteLine)
        {
            // Hors terminal (fichier, pipeline CI) : jalons tous les 10 %, sans réécriture.
            var milestone = percent / 10;
            if (milestone == _lastMilestone)
            {
                return;
            }

            _lastMilestone = milestone;
            SysConsole.WriteLine(
                CultureInfo.CurrentCulture.DisplayName,
                string.Create(CultureInfo.InvariantCulture, $"  ... {done}/{total} dépôts analysés ({percent} %), {failed} en échec"));
            return;
        }

        const int width = 24;
        var filled = width * percent / 100;
        var bar = _unicode
            ? new string('█', filled) + new string('░', width - filled)
            : new string('#', filled) + new string('.', width - filled);

        var failedText = failed > 0
            ? $" {Glyph("·", "-")} {failed.ToString(CultureInfo.CurrentCulture)} en échec"
            : string.Empty;

        var line = string.Create(CultureInfo.InvariantCulture, $"  [{bar}] {percent,3} %  {done}/{total} dépôts{failedText}");
        SysConsole.Write("\r" + line.PadRight(_lastProgressLength));
        _lastProgressLength = line.Length;
    }

    /// <summary>Termine l'affichage de la progression.</summary>
    public void EndProgress()
    {
        if (_canRewriteLine && _lastProgressLength > 0)
        {
            SysConsole.Write("\r" + new string(' ', _lastProgressLength) + "\r");
            _lastProgressLength = 0;
        }
    }

    /// <summary>Affiche le bilan de l'exécution.</summary>
    public void Summary(AuditRunResult result, string reportPath, bool actorsRedacted)
    {
        var critical = result.Findings.Count(f => f.Severity == Severity.Critical);
        var warning = result.Findings.Count(f => f.Severity == Severity.Warning);
        var rule = new string(_unicode ? '─' : '-', 62);

        SysConsole.WriteLine();
        Write($"  {rule}", ConsoleColor.DarkGray);
        Write("  Bilan", ConsoleColor.Cyan);
        SysConsole.WriteLine();

        Write($"    Dépôts analysés     : {result.RepositoriesAnalyzed.ToString(CultureInfo.CurrentCulture)}", color: null);

        if (result.RepositoriesFailed > 0)
        {
            Write($"    Dépôts en échec     : {result.RepositoriesFailed.ToString(CultureInfo.CurrentCulture)}", ConsoleColor.Yellow);
        }

        Write(
            $"    Findings critiques  : {critical.ToString(CultureInfo.CurrentCulture)}",
            critical > 0 ? ConsoleColor.Red : ConsoleColor.Green);

        Write(
            $"    Avertissements      : {warning.ToString(CultureInfo.CurrentCulture)}",
            warning > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);

        Write($"    Durée               : {result.Duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture)}", color: null);

        if (actorsRedacted)
        {
            Write("    Acteurs             : pseudonymisés", ConsoleColor.DarkGray);
        }

        SysConsole.WriteLine();
        Write($"  {Glyph("→", "->")} Rapport : {reportPath}", ConsoleColor.Cyan);
        SysConsole.WriteLine();
    }

    /// <summary>Signale une configuration invalide (code de sortie 2).</summary>
    public void ConfigurationError(IEnumerable<string> reasons)
    {
        SysConsole.WriteLine();
        Write("  Configuration invalide — l'exécution est interrompue.", ConsoleColor.Red);
        SysConsole.WriteLine();

        foreach (var reason in reasons)
        {
            Write($"    {Glyph("•", "-")} {reason}", ConsoleColor.Red);
        }

        SysConsole.WriteLine();
        Write("  Corrigez appsettings.json (à côté de l'exécutable) puis relancez.", ConsoleColor.DarkGray);
        Write("  Aide : GovernanceAuditor --aide", ConsoleColor.DarkGray);
        SysConsole.WriteLine();
    }

    /// <summary>Signale une erreur fatale.</summary>
    public void FatalError(string message)
    {
        SysConsole.WriteLine();
        Write($"  Échec de l'exécution : {message}", ConsoleColor.Red);
        SysConsole.WriteLine();
    }

    /// <summary>Signale une interruption (Ctrl+C ou délai global dépassé).</summary>
    public void Cancelled(string reason)
    {
        EndProgress();
        SysConsole.WriteLine();
        Write($"  Exécution interrompue : {reason}. Les résultats sont incomplets.", ConsoleColor.Yellow);
        SysConsole.WriteLine();
    }

    /// <summary>Affiche l'aide en ligne de commande.</summary>
    public void Help()
    {
        SysConsole.WriteLine();
        Write("  Auditeur de gouvernance des dépôts Azure DevOps", ConsoleColor.Cyan);
        Write($" Hervé KOUEKE (herve.kouekekouemeni@cgi.com", ConsoleColor.Cyan);
        SysConsole.WriteLine();
        SysConsole.WriteLine("  Analyse en LECTURE SEULE les dépôts Git d'un serveur Azure DevOps Server");
        SysConsole.WriteLine("  et produit un rapport Markdown consolidé.");
        SysConsole.WriteLine();
        Write("  Utilisation", ConsoleColor.Cyan);
        SysConsole.WriteLine("    GovernanceAuditor [options]");
        SysConsole.WriteLine();
        Write("  Options", ConsoleColor.Cyan);
        SysConsole.WriteLine("    --serveur <url>        URL du serveur (ex. https://devops.entreprise.local)");
        SysConsole.WriteLine("    --collection <nom>     Collection (défaut : DefaultCollection)");
        SysConsole.WriteLine("    --projets <a,b,c>      Restreint l'analyse à ces projets (défaut : tous)");
        SysConsole.WriteLine("    --sortie <dossier>     Dossier du rapport (défaut : ./reports)");
        SysConsole.WriteLine("    --parallelisme <n>     Dépôts analysés simultanément (défaut : 5)");
        SysConsole.WriteLine("    --anonymiser           Pseudonymise les acteurs dans le rapport");
        SysConsole.WriteLine("    --aide                 Affiche cette aide");
        SysConsole.WriteLine();
        SysConsole.WriteLine("    Toute clé d'appsettings.json est surchargeable : --Rules:RequiredReviewers 3");
        SysConsole.WriteLine();
        Write("  Codes de sortie", ConsoleColor.Cyan);
        SysConsole.WriteLine("    0  Aucune anomalie critique");
        SysConsole.WriteLine("    1  Au moins une anomalie critique (ou erreur fatale)");
        SysConsole.WriteLine("    2  Configuration invalide");
        SysConsole.WriteLine("    3  Analyse partielle : trop de dépôts en échec, ou interruption");
        SysConsole.WriteLine();
        Write("  Authentification : Windows intégrée (session AD courante). Aucun secret stocké.", ConsoleColor.DarkGray);
        SysConsole.WriteLine();
    }
}
