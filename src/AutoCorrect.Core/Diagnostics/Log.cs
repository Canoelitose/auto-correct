using System.Globalization;

namespace AutoCorrect.Core.Diagnostics;

/// <summary>
/// Static entry point used across the application. Without initialisation every call is a no-op,
/// which keeps unit tests free of file system side effects.
///
/// Rule: never pass processed user text into these methods. Use <see cref="Describe"/>.
/// </summary>
public static class Log
{
    private static FileLogger? _logger;

    public static void Initialize(FileLogger logger) => _logger = logger;

    public static FileLogger? Current => _logger;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoCorrect",
        "logs");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "autocorrect.log");

    public static void Debug(string message) => _logger?.Write(LogLevel.Debug, message);

    public static void Info(string message) => _logger?.Write(LogLevel.Info, message);

    public static void Warn(string message, Exception? exception = null) =>
        _logger?.Write(LogLevel.Warning, message, exception);

    public static void Error(string message, Exception? exception = null) =>
        _logger?.Write(LogLevel.Error, message, exception);

    /// <summary>
    /// Renders a length placeholder for user text. The content itself must never be logged.
    /// </summary>
    public static string Describe(string? text) =>
        text is null
            ? "<null>"
            : string.Format(CultureInfo.InvariantCulture, "<{0} chars>", text.Length);
}
