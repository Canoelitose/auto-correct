using System.Globalization;
using System.Text;

namespace AutoCorrect.Core.Diagnostics;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warning = 2,
    Error = 3,
    Off = 4,
}

/// <summary>
/// Minimal rotating file logger. No external logging framework, no background thread.
///
/// IMPORTANT: the text the user processes must never reach the log. Use
/// <see cref="Log.Describe"/> to log a length instead of the content.
/// </summary>
public sealed class FileLogger : IDisposable
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly int _maxFiles;
    private bool _disposed;

    public FileLogger(
        string path,
        LogLevel minimumLevel = LogLevel.Warning,
        long maxBytes = 1024 * 1024,
        int maxFiles = 3)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _maxBytes = Math.Max(4096, maxBytes);
        _maxFiles = Math.Clamp(maxFiles, 1, 20);
        MinimumLevel = minimumLevel;

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public LogLevel MinimumLevel { get; set; }

    public string FilePath => _path;

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel || MinimumLevel == LogLevel.Off || _disposed)
        {
            return;
        }

        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append(message);

        if (exception is not null)
        {
            line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
            if (exception.StackTrace is { Length: > 0 } stack)
            {
                line.Append(Environment.NewLine).Append(stack);
            }
        }

        line.Append(Environment.NewLine);
        var payload = line.ToString();

        lock (_sync)
        {
            try
            {
                RotateIfNeeded(Encoding.UTF8.GetByteCount(payload));
                File.AppendAllText(_path, payload, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the application down.
            }
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        var info = new FileInfo(_path);
        if (!info.Exists || info.Length + incomingBytes <= _maxBytes)
        {
            return;
        }

        var oldest = _path + "." + _maxFiles.ToString(CultureInfo.InvariantCulture);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }

        for (var i = _maxFiles - 1; i >= 1; i--)
        {
            var source = _path + "." + i.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(source))
            {
                File.Move(source, _path + "." + (i + 1).ToString(CultureInfo.InvariantCulture), overwrite: true);
            }
        }

        File.Move(_path, _path + ".1", overwrite: true);
    }

    public void Dispose() => _disposed = true;
}
