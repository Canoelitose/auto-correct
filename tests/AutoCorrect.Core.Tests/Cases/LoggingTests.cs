using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.Core.Tests.Cases;

public static class LoggingTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Logger: writes warnings and errors, skips debug by default", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "app.log");
            using var logger = new FileLogger(path);

            logger.Write(LogLevel.Debug, "debug-entry");
            logger.Write(LogLevel.Info, "info-entry");
            logger.Write(LogLevel.Warning, "warning-entry");
            logger.Write(LogLevel.Error, "error-entry");

            var content = File.ReadAllText(path);
            Assert.False(content.Contains("debug-entry", StringComparison.Ordinal), "debug must be filtered out");
            Assert.False(content.Contains("info-entry", StringComparison.Ordinal), "info must be filtered out");
            Assert.Contains("warning-entry", content);
            Assert.Contains("error-entry", content);
            Assert.Contains("[WARNING]", content);
        });

        runner.Add("Logger: exception details are recorded", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "app.log");
            using var logger = new FileLogger(path);

            logger.Write(LogLevel.Error, "request failed", new InvalidOperationException("connection refused"));

            var content = File.ReadAllText(path);
            Assert.Contains("InvalidOperationException", content);
            Assert.Contains("connection refused", content);
        });

        runner.Add("Logger: rotates and keeps a bounded number of files", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "app.log");
            using var logger = new FileLogger(path, LogLevel.Debug, maxBytes: 4096, maxFiles: 2);

            for (var i = 0; i < 400; i++)
            {
                logger.Write(LogLevel.Warning, $"entry {i} " + new string('x', 100));
            }

            Assert.True(File.Exists(path), "current log file is missing");
            Assert.True(File.Exists(path + ".1"), "rotated log file is missing");
            Assert.False(File.Exists(path + ".3"), "more files were kept than configured");
            Assert.True(new FileInfo(path).Length <= 8192, "current log file grew beyond the limit");
        });

        runner.Add("Logger: level Off silences everything", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "app.log");
            using var logger = new FileLogger(path, LogLevel.Off);

            logger.Write(LogLevel.Error, "should-not-appear");
            Assert.False(File.Exists(path), "no file should be created when logging is off");
        });

        runner.Add("Logger: creates the log directory when it does not exist yet", () =>
        {
            using var temp = new TempDirectory();
            var path = Path.Combine(temp.Path, "logs", "nested", "app.log");

            using var logger = new FileLogger(path);
            logger.Write(LogLevel.Error, "first entry");

            Assert.True(File.Exists(path), "log file was not created in the new directory");
        });

        runner.Add("Log.Describe replaces user text with its length", () =>
        {
            Assert.Equal("<11 chars>", Log.Describe("Hallo Welt!"));
            Assert.Equal("<null>", Log.Describe(null));
            Assert.False(Log.Describe("geheimer Text").Contains("geheim", StringComparison.Ordinal));
        });

        runner.Add("Log: calls without initialisation are harmless", () =>
        {
            Log.Warn("no logger configured");
            Log.Error("no logger configured", new InvalidOperationException("x"));
        });
    }
}
