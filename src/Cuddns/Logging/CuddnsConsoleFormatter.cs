using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Cuddns.Logging;

/// <summary>
/// A compact console format: "yyyy-MM-dd HH:mm:ss [LEVEL] ShortCategory: message", one line
/// per log entry (exceptions included) rather than the built-in "simple" formatter's
/// full-namespace category and multi-line layout.
/// </summary>
public sealed class CuddnsConsoleFormatter() : ConsoleFormatter("cuddns")
{
    public override void Write<TState>(
        in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
        {
            return;
        }

        textWriter.Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        textWriter.Write(" [");
        textWriter.Write(LevelLabel(logEntry.LogLevel));
        textWriter.Write("] ");
        textWriter.Write(ShortCategory(logEntry.Category));
        textWriter.Write(": ");
        textWriter.Write(message);

        if (logEntry.Exception is not null)
        {
            textWriter.Write(' ');
            textWriter.Write(logEntry.Exception.ToString().Replace(Environment.NewLine, " "));
        }

        textWriter.Write(Environment.NewLine);
    }

    private static string LevelLabel(LogLevel logLevel) => (logLevel switch
    {
        LogLevel.Trace => "TRACE",
        LogLevel.Debug => "DEBUG",
        LogLevel.Information => "INFO",
        LogLevel.Warning => "WARN",
        LogLevel.Error => "ERROR",
        LogLevel.Critical => "CRIT",
        _ => logLevel.ToString().ToUpperInvariant(),
    }).PadLeft(5);

    private static string ShortCategory(string category)
    {
        var lastDot = category.LastIndexOf('.');
        return lastDot < 0 ? category : category[(lastDot + 1)..];
    }
}
