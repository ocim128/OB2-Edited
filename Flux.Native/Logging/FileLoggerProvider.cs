using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Flux.Native.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly bool _overwrite;
    private readonly object _writeLock = new();

    public FileLoggerProvider(string filePath, bool overwrite = false)
    {
        _filePath = filePath;
        _overwrite = overwrite;
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(_filePath, _overwrite, _writeLock, categoryName);

    public void Dispose()
    {
    }
}

internal sealed class CategoryFilteredLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;
    private readonly Func<string, LogLevel, bool> _filter;

    public CategoryFilteredLoggerProvider(ILoggerProvider inner, Func<string, LogLevel, bool> filter)
    {
        _inner = inner;
        _filter = filter;
    }

    public ILogger CreateLogger(string categoryName)
    {
        var innerLogger = _inner.CreateLogger(categoryName);
        return new FilteredLogger(innerLogger, categoryName, _filter);
    }

    public void Dispose() => _inner.Dispose();

    private sealed class FilteredLogger : ILogger
    {
        private readonly ILogger _inner;
        private readonly string _categoryName;
        private readonly Func<string, LogLevel, bool> _filter;

        public FilteredLogger(ILogger inner, string categoryName, Func<string, LogLevel, bool> filter)
        {
            _inner = inner;
            _categoryName = categoryName;
            _filter = filter;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => _filter(_categoryName, logLevel) && _inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (_filter(_categoryName, logLevel))
            {
                _inner.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}

internal sealed class FileLogger : ILogger
{
    private readonly string _filePath;
    private readonly bool _overwrite;
    private readonly object _writeLock;
    private readonly string _categoryName;

    public FileLogger(string filePath, bool overwrite, object writeLock, string categoryName)
    {
        _filePath = filePath;
        _overwrite = overwrite;
        _writeLock = writeLock;
        _categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (formatter == null)
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        lock (_writeLock)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {_categoryName}: {message}";
                if (exception != null)
                {
                    line += Environment.NewLine + exception;
                }
                line += Environment.NewLine;

                if (_overwrite)
                {
                    File.WriteAllText(_filePath, line);
                }
                else
                {
                    File.AppendAllText(_filePath, line);
                }
            }
            catch
            {
                // Logging must never throw.
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
