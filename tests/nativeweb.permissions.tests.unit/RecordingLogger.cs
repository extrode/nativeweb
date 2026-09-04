using Microsoft.Extensions.Logging;

namespace NativeWeb.Permissions.Tests.Unit;

/// <summary>
/// Keeps the formatted text of every warning, so "recorded once, and without the
/// subject in it" is an assertion rather than a claim.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _warnings = [];

    internal IReadOnlyList<string> Warnings => _warnings;

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Warning)
        {
            _warnings.Add(formatter(state, exception));
        }
    }

    private sealed class NullScope : IDisposable
    {
        internal static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
