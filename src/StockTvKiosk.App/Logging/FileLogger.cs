using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using StockTvKiosk.Core.Configuration;

namespace StockTvKiosk.App.Logging;

/// <summary>
/// Schlanker Datei-Logger: der Kiosk laeuft unbeaufsichtigt, deshalb muessen erkannte
/// Bildschirme, geladene URLs und Ladefehler auch ohne angeschlossene Konsole nachlesbar sein.
/// Bewusst ohne zusaetzliche Abhaengigkeit (Serilog o. Ae.).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly LoggingFileOptions _options;
    private readonly string _path;
    private readonly BlockingCollection<string> _queue = new(4096);
    private readonly Thread _worker;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private volatile bool _disposed;

    public FileLoggerProvider(LoggingFileOptions options, string baseDirectory)
    {
        _options = options;

        string directory = Path.IsPathRooted(options.Directory)
            ? options.Directory
            : Path.Combine(baseDirectory, options.Directory);

        System.IO.Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, options.FileName);

        _worker = new Thread(ProcessQueue)
        {
            IsBackground = true,
            Name = "kiosk-file-log"
        };
        _worker.Start();
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    internal void Enqueue(string line)
    {
        if (_disposed)
            return;

        // Lieber eine Logzeile verwerfen als den Kiosk blockieren.
        _queue.TryAdd(line);
    }

    private void ProcessQueue()
    {
        foreach (string line in _queue.GetConsumingEnumerable())
        {
            try
            {
                RollIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                // Datei gesperrt oder Datentraeger voll - der Konsolen-Logger bleibt davon unberuehrt.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void RollIfNeeded()
    {
        if (_options.MaxFileSizeBytes <= 0)
            return;

        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < _options.MaxFileSizeBytes)
            return;

        string directory = file.DirectoryName!;
        string stem = Path.GetFileNameWithoutExtension(_options.FileName);
        string extension = Path.GetExtension(_options.FileName);

        string Archive(int index) => Path.Combine(directory, $"{stem}.{index}{extension}");

        File.Delete(Archive(_options.RetainedFileCount));
        for (int i = _options.RetainedFileCount - 1; i >= 1; i--)
        {
            if (File.Exists(Archive(i)))
                File.Move(Archive(i), Archive(i + 1), overwrite: true);
        }

        File.Move(_path, Archive(1), overwrite: true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var builder = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ')
                .Append(Level(logLevel))
                .Append(" [").Append(category).Append("] ")
                .Append(formatter(state, exception));

            if (exception is not null)
                builder.AppendLine().Append(exception);

            provider.Enqueue(builder.ToString());
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???"
        };
    }
}
