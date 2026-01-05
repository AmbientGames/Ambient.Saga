using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Ambient.Infrastructure.Logging;

/// <summary>
/// A simple file logger provider that writes to a single log file.
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly object _writeLock = new();
    private readonly StreamWriter _writer;

    public FileLoggerProvider(string logFilePath, LogLevel minimumLevel = LogLevel.Information, bool clearOnStart = true)
    {
        _logFilePath = logFilePath;
        _minimumLevel = minimumLevel;

        // Ensure directory exists
        var directory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Clear log file on start if requested
        if (clearOnStart && File.Exists(logFilePath))
        {
            File.Delete(logFilePath);
        }

        // Open file for writing
        _writer = new StreamWriter(new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, this));
    }

    public void Dispose()
    {
        _loggers.Clear();
        _writer.Dispose();
    }

    internal void WriteLog(string categoryName, LogLevel logLevel, string message, Exception? exception)
    {
        if (logLevel < _minimumLevel)
            return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var levelStr = logLevel switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };

        var logLine = $"[{timestamp}] [{levelStr}] [{categoryName}] {message}";

        lock (_writeLock)
        {
            _writer.WriteLine(logLine);
            if (exception != null)
            {
                _writer.WriteLine($"  Exception: {exception}");
            }
        }
    }
}

/// <summary>
/// Individual logger instance for a specific category.
/// </summary>
internal class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string categoryName, FileLoggerProvider provider)
    {
        _categoryName = categoryName;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        _provider.WriteLog(_categoryName, logLevel, message, exception);
    }
}

/// <summary>
/// Extension methods for adding file logging.
/// </summary>
public static class FileLoggerExtensions
{
    /// <summary>
    /// Adds a file logger that writes to the specified path.
    /// </summary>
    public static ILoggingBuilder AddFile(this ILoggingBuilder builder, string logFilePath, LogLevel minimumLevel = LogLevel.Information, bool clearOnStart = true)
    {
        builder.AddProvider(new FileLoggerProvider(logFilePath, minimumLevel, clearOnStart));
        return builder;
    }
}
