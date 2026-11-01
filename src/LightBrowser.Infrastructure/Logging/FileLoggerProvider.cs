using System.Text;
using Microsoft.Extensions.Logging;

namespace LightBrowser.Infrastructure.Logging;

/// <summary>Minimal append-only file logger (one file per day). Logging never throws into the app.</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _gate = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, $"lightbrowser-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging must not crash the application (disk full, locked file, ...).
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = $"{DateTime.Now:HH:mm:ss.fff} [{logLevel}] {_category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                message += Environment.NewLine + exception;
            }

            _provider.Write(message);
        }
    }
}
