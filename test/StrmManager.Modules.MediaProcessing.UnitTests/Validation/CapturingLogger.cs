using System.Text;
using Microsoft.Extensions.Logging;

namespace StrmManager.Modules.MediaProcessing.UnitTests.Validation;

/// <summary>
/// Records everything a logger would emit, at every level: the formatted message, every structured
/// property (a LoggerMessage's {StandardError} lives here even if it is not in the template), and the
/// full exception text. <see cref="AllText"/> is what a log sink could ever see.
/// </summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    private readonly List<string> _entries = [];

    public IReadOnlyList<string> Entries => _entries;

    public string AllText => string.Join('\n', _entries);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new StringBuilder();
        entry.Append('[').Append(logLevel).Append("] ").Append(formatter(state, exception));

        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (KeyValuePair<string, object?> property in properties)
            {
                entry.Append(" | ").Append(property.Key).Append('=').Append(property.Value);
            }
        }

        if (exception is not null)
        {
            entry.Append(" | exception=").Append(exception);
        }

        _entries.Add(entry.ToString());
    }
}
