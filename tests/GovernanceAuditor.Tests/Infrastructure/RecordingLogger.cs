using Microsoft.Extensions.Logging;

namespace GovernanceAuditor.Tests.Infrastructure;

/// <summary>
/// Journal factice : conserve les messages formatés afin de vérifier que les
/// diagnostics attendus sont bien émis. Un diagnostic non testé finit par disparaître.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>Entrées enregistrées, dans l'ordre d'émission.</summary>
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>Vrai si un message du niveau donné contient le fragment attendu.</summary>
    public bool Contains(LogLevel level, string fragment) =>
        Entries.Exists(e => e.Level == level && e.Message.Contains(fragment, StringComparison.Ordinal));
}
