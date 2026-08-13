using System.Globalization;
using GovernanceAuditor.Core.Abstractions;
using GovernanceAuditor.Core.Analyzers;
using GovernanceAuditor.Core.Options;
using GovernanceAuditor.Infrastructure.AzureDevOps;
using GovernanceAuditor.Reporting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using SysConsole = System.Console;

namespace GovernanceAuditor.Console;

/// <summary>Point d'entrée de l'auditeur de gouvernance.</summary>
internal static class Program
{
    /// <summary>Alias conviviaux vers les clés de configuration.</summary>
    private static readonly Dictionary<string, string> SwitchMappings = new(StringComparer.Ordinal)
    {
        ["--serveur"] = "AzureDevOpsServer:BaseUrl",
        ["--collection"] = "AzureDevOpsServer:Collection",
        ["--sortie"] = "Reporting:OutputDirectory",
        ["--parallelisme"] = "Execution:MaxDegreeOfParallelism",
        ["--anonymiser"] = "Privacy:RedactActors",
    };

    private static async Task<int> Main(string[] args)
    {
        var ui = new ConsoleUi();

        if (args.Any(a => a is "--aide" or "--help" or "-h" or "/?"))
        {
            ui.Help();
            return ExitCodes.Success;
        }

        try
        {
            return await RunAsync(args, ui).ConfigureAwait(false);
        }
        catch (OptionsValidationException exception)
        {
            ui.ConfigurationError(exception.Failures);
            return ExitCodes.ConfigurationError;
        }
        catch (ConfigurationException exception)
        {
            ui.ConfigurationError([exception.Message]);
            return ExitCodes.ConfigurationError;
        }
        finally
        {
            PauseIfInteractive();
        }
    }

    /// <summary>
    /// Laisse la fenêtre ouverte après un double-clic, mais uniquement en session
    /// réellement interactive. Sur une entrée ou une sortie redirigée — tâche
    /// planifiée, pipeline, « GovernanceAuditor &gt; rapport.log » — l'attente
    /// bloquerait l'appelant, et <see cref="System.Console.ReadKey()"/> lèverait
    /// <see cref="InvalidOperationException"/> faute de console à lire.
    /// </summary>
    private static void PauseIfInteractive()
    {
        if (SysConsole.IsInputRedirected || SysConsole.IsOutputRedirected)
        {
            return;
        }

        SysConsole.ReadKey(intercept: true);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0051:Method is too long", Justification = "<En attente>")]
    private static async Task<int> RunAsync(string[] args, ConsoleUi ui)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            // appsettings.json est lu à côté de l'exécutable, pas dans le répertoire courant.
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Réglages locaux, à côté de l'exécutable et hors dépôt : permet de garder
        // serveur et projets réels sans les versionner. Facultatif.
        builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);

        builder.Configuration.AddCommandLine(NormaliseFlags(args), SwitchMappings);
        ApplyProjectScope(builder.Configuration, args);

        ConfigureLogging(builder.Logging);
        ConfigureServices(builder.Services);

        using var host = builder.Build();
        var services = host.Services;

        var server = Validate<AzureDevOpsServerOptions>(services);
        var execution = Validate<ExecutionOptions>(services);
        var reporting = Validate<ReportingOptions>(services);
        var scope = services.GetRequiredService<IOptions<ScopeOptions>>().Value;
        var privacy = services.GetRequiredService<IOptions<PrivacyOptions>>().Value;

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GovernanceAuditor");
        ValidateTransport(server, logger);
        ValidateReportFormat(reporting, services);

        ui.Banner(
            server.BaseUrl,
            server.Collection,
            scope.Projects.Count == 0 ? "tous les projets accessibles" : string.Join(", ", scope.Projects),
            execution.MaxDegreeOfParallelism,
            scope.Projects.Count > 0);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(execution.GlobalTimeoutMinutes));
        var interrupted = false;
        SysConsole.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;   // Arrêt maîtrisé plutôt que brutal.
            interrupted = true;
            cancellation.Cancel();
        };

        var orchestrator = services.GetRequiredService<AuditOrchestrator>();
        var progress = new Progress<AuditProgress>(p => ui.Progress(p.Completed, p.Total, p.Failed));

        ui.Step("Découverte des dépôts et collecte…");

        try
        {
            var result = await orchestrator.RunAsync(progress, cancellation.Token).ConfigureAwait(false);
            ui.EndProgress();

            if (privacy.RedactActors)
            {
                result = services.GetRequiredService<ActorRedactor>().Redact(result);
            }

            var path = await services.GetRequiredService<ReportWriter>()
                .WriteAsync(result, CancellationToken.None)
                .ConfigureAwait(false);

            ui.Summary(result, path, privacy.RedactActors);

            if (result.RepositoriesFailed > 0)
            {
                ui.Warn(string.Create(CultureInfo.InvariantCulture, $"{result.RepositoriesFailed} dépôt(s) n'ont pas pu être analysés — détail dans le rapport."));
            }

            return ExitCodePolicy.Resolve(result, execution);
        }
        catch (OperationCanceledException)
        {
            ui.Cancelled(interrupted ? "interruption demandée (Ctrl+C)" : "délai global dépassé");
            return ExitCodes.PartialFailure;
        }
#pragma warning disable CA1031, S2221
        // Dernier filet : toute erreur non anticipée doit produire un message lisible
        // et un code de sortie exploitable, jamais une trace brute.
        catch (Exception exception)
#pragma warning restore CA1031, S2221
        {
            ui.EndProgress();
            Log.FatalError(logger, exception);
            ui.FatalError(exception.Message);
            return ExitCodes.Critical;
        }
    }

    private static void ConfigureLogging(ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        // Les diagnostics partent sur la sortie d'erreur : la sortie standard
        // reste réservée au rendu utilisateur et peut être redirigée proprement.
        logging.Services.Configure<ConsoleLoggerOptions>(options =>
            options.LogToStandardErrorThreshold = LogLevel.Trace);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<RulesOptions>()
            .BindConfiguration(RulesOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddOptions<PrivacyOptions>()
            .BindConfiguration(PrivacyOptions.SectionName);

        services.AddOptions<ReportingOptions>()
            .BindConfiguration(ReportingOptions.SectionName)
            .ValidateDataAnnotations();

        services.AddAzureDevOpsInfrastructure();
        services.AddMarkdownReporting();

        // Les six analyseurs, injectés en IEnumerable<IRepositoryAnalyzer>.
        services.AddSingleton<IRepositoryAnalyzer, StaleBranchAnalyzer>();
        services.AddSingleton<IRepositoryAnalyzer, AbandonedBranchAnalyzer>();
        services.AddSingleton<IRepositoryAnalyzer, AbandonedUnlockedBranchAnalyzer>();
        services.AddSingleton<IRepositoryAnalyzer, BranchPolicyAnalyzer>();
        services.AddSingleton<IRepositoryAnalyzer, PullRequestAnalyzer>();
        services.AddSingleton<IRepositoryAnalyzer, PipelineAnalyzer>();

        services.AddSingleton<AuditOrchestrator>();
        services.AddSingleton<ActorRedactor>();
        services.AddSingleton<ReportWriter>();
    }

    /// <summary>Force la validation des options et renvoie la valeur validée.</summary>
    private static T Validate<T>(IServiceProvider services)
        where T : class => services.GetRequiredService<IOptions<T>>().Value;

    private static void ValidateTransport(AzureDevOpsServerOptions server, ILogger logger)
    {
        if (server.ValidateTransport() is { } reason)
        {
            throw new ConfigurationException(reason);
        }

        if (!server.BaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Log.InsecureTransportAllowed(logger);
        }
    }

    private static void ValidateReportFormat(ReportingOptions reporting, IServiceProvider services)
    {
        var generator = services.GetRequiredService<IReportGenerator>();
        if (!string.Equals(reporting.Format, generator.Format, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConfigurationException(
                $"Reporting:Format « {reporting.Format} » n'est pas supporté. Format disponible : {generator.Format}.");
        }
    }

    /// <summary>
    /// Traduit « --projets a,b,c » en clés indexées <c>Scope:Projects:0..n</c> (le liant
    /// de configuration n'accepte pas de liste sur une seule clé).
    /// </summary>
    /// <remarks>
    /// L'option REMPLACE le périmètre configuré, elle ne s'y ajoute pas. Les fournisseurs
    /// de configuration fusionnent clé par clé : sans blanchiment des index excédentaires,
    /// « --projets Paie » sur un appsettings.json qui en déclare deux donnerait
    /// « Paie » + la seconde entrée héritée. « --projets » sans valeur vide le périmètre,
    /// c'est-à-dire « tous les projets accessibles ».
    /// </remarks>
    internal static void ApplyProjectScope(ConfigurationManager configuration, string[] args)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);

        if (!TryReadProjects(args, out var names))
        {
            return;
        }

        const string prefix = $"{ScopeOptions.SectionName}:Projects";
        var configured = configuration.GetSection(prefix).GetChildren().Count();
        var entries = new List<KeyValuePair<string, string?>>(Math.Max(names.Count, configured));

        for (var index = 0; index < names.Count; index++)
        {
            entries.Add(new KeyValuePair<string, string?>(Key(index), names[index]));
        }

        // Les index restants proviennent d'une source de moindre priorité : on les
        // neutralise (les entrées vides sont écartées à la lecture du périmètre).
        for (var index = names.Count; index < configured; index++)
        {
            entries.Add(new KeyValuePair<string, string?>(Key(index), string.Empty));
        }

        if (entries.Count > 0)
        {
            configuration.AddInMemoryCollection(entries);
        }

        static string Key(int index) =>
            $"{prefix}:{index.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Indique si « --projets » figure dans la ligne de commande, et renvoie les noms lus.
    /// La distinction « absent » / « fourni mais vide » porte tout le sens : la seconde
    /// forme est une demande explicite d'ouvrir le périmètre.
    /// </summary>
    private static bool TryReadProjects(string[] args, out List<string> names)
    {
        names = [];
        var found = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--projets", StringComparison.Ordinal))
            {
                continue;
            }

            found = true;

            // Une valeur absente, ou immédiatement suivie d'une autre option, vaut
            // « périmètre vide » : « --projets --anonymiser » ne doit pas créer un
            // projet nommé « --anonymiser ».
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            names.AddRange(args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return found;
    }

    /// <summary>
    /// Permet d'écrire « --anonymiser » sans valeur : le liant de configuration
    /// exige sinon la forme « --anonymiser true ».
    /// </summary>
    private static string[] NormaliseFlags(string[] args)
    {
        var normalised = new List<string>(args.Length);

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--anonymiser", StringComparison.Ordinal))
            {
                normalised.Add(args[i]);
                continue;
            }

            var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            normalised.Add(hasValue ? args[i] : "--anonymiser=true");
        }

        return [.. normalised];
    }
}
