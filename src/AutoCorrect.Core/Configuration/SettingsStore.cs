using System.Text.Json;
using System.Text.Json.Serialization;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.Core.Configuration;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. A missing or damaged file never stops the
/// application: defaults are used and the damaged file is kept for inspection.
/// </summary>
public sealed class SettingsStore
{
    private readonly string _filePath;

    public SettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public string FilePath => _filePath;

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoCorrect");

    public static string DefaultFilePath => Path.Combine(DefaultDirectory, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings);
            if (settings is null)
            {
                return new AppSettings();
            }

            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Settings could not be read from '{_filePath}', falling back to defaults.", ex);
            QuarantineDamagedFile();
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings);

        // Write to a temporary file first so a crash cannot leave a half written settings file.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private void QuarantineDamagedFile()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Move(_filePath, _filePath + ".invalid", overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Damaged settings file could not be renamed.", ex);
        }
    }
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
