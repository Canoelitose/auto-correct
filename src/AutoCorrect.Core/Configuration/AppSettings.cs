using System.Text.Json.Serialization;
using AutoCorrect.Core.Diagnostics;
using AutoCorrect.Core.Engines.LanguageTool;
using AutoCorrect.Core.Engines.Llm;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.Core.Configuration;

/// <summary>
/// Persisted configuration. Serialised as JSON to %APPDATA%\AutoCorrect\settings.json
/// so it can be pre-seeded per group policy or deployment script.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Hotkey that opens the popup in <c>Correct</c> mode.</summary>
    public string PrimaryHotkey { get; set; } = HotkeyDefinition.DefaultPrimary.ToString();

    /// <summary>Optional second hotkey that jumps straight into <c>Rephrase</c>. Empty disables it.</summary>
    public string RephraseHotkey { get; set; } = HotkeyDefinition.DefaultRephrase.ToString();

    public bool StartWithWindows { get; set; }

    public string LanguageToolEndpoint { get; set; } = LanguageToolEngine.DefaultEndpoint;

    /// <summary>
    /// Language of the text being corrected. "auto" lets LanguageTool detect it, which covers
    /// German and English in the same session without switching anything.
    /// </summary>
    public string Language { get; set; } = LanguageOptions.AutomaticDetection;

    /// <summary>
    /// Variants preferred while detecting. Without them Swiss German text would be treated as
    /// de-DE and "ss" would be corrected to "ß".
    /// </summary>
    public string PreferredVariants { get; set; } = LanguageOptions.DefaultPreferredVariants;

    /// <summary>Interface language: "auto" (Windows display language), "de" or "en".</summary>
    public string InterfaceLanguage { get; set; } = UiText.AutomaticSetting;

    /// <summary>Phase 2: OpenAI compatible base address (llama.cpp, Ollama, own API).</summary>
    public string LlmEndpoint { get; set; } = LlmEngine.DefaultEndpoint;

    /// <summary>
    /// Phase 2: model name passed to the OpenAI compatible endpoint. A name that is not
    /// installed is not an error - the engine then uses whatever usable model it finds.
    /// </summary>
    public string LlmModel { get; set; } = LlmEngine.DefaultModel;

    public LogLevel LogLevel { get; set; } = LogLevel.Warning;

    /// <summary>Selections longer than this are rejected with a hint instead of being processed.</summary>
    public int MaxInputLength { get; set; } = 5000;

    /// <summary>
    /// Delay between the simulated Ctrl+C and reading the clipboard. Below roughly 80 ms
    /// the previous clipboard content is read instead of the selection.
    /// </summary>
    public int ClipboardWaitMilliseconds { get; set; } = 100;

    /// <summary>Try UI Automation before falling back to the clipboard.</summary>
    public bool PreferUiAutomation { get; set; } = true;

    /// <summary>Request timeout for engine calls.</summary>
    public int RequestTimeoutSeconds { get; set; } = 15;

    [JsonIgnore]
    public HotkeyDefinition PrimaryHotkeyDefinition =>
        HotkeyDefinition.ParseOrDefault(PrimaryHotkey, HotkeyDefinition.DefaultPrimary);

    /// <summary>Null when the optional second hotkey is disabled or unparseable.</summary>
    [JsonIgnore]
    public HotkeyDefinition? RephraseHotkeyDefinition =>
        HotkeyDefinition.TryParse(RephraseHotkey, out var parsed, out _) ? parsed : null;

    /// <summary>Clamps values that would break the application if edited by hand.</summary>
    public void Normalize()
    {
        MaxInputLength = Math.Clamp(MaxInputLength, 100, 50_000);
        ClipboardWaitMilliseconds = Math.Clamp(ClipboardWaitMilliseconds, 80, 1000);
        RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 2, 300);

        if (string.IsNullOrWhiteSpace(LanguageToolEndpoint))
        {
            LanguageToolEndpoint = LanguageToolEngine.DefaultEndpoint;
        }

        if (string.IsNullOrWhiteSpace(Language))
        {
            Language = LanguageOptions.AutomaticDetection;
        }

        if (string.IsNullOrWhiteSpace(PreferredVariants))
        {
            PreferredVariants = LanguageOptions.DefaultPreferredVariants;
        }

        if (string.IsNullOrWhiteSpace(InterfaceLanguage))
        {
            InterfaceLanguage = UiText.AutomaticSetting;
        }

        if (string.IsNullOrWhiteSpace(LlmEndpoint))
        {
            LlmEndpoint = LlmEngine.DefaultEndpoint;
        }

        if (string.IsNullOrWhiteSpace(LlmModel))
        {
            LlmModel = LlmEngine.DefaultModel;
        }

        if (!HotkeyDefinition.TryParse(PrimaryHotkey, out _, out _))
        {
            PrimaryHotkey = HotkeyDefinition.DefaultPrimary.ToString();
        }
    }

    public AppSettings Clone() => new()
    {
        PrimaryHotkey = PrimaryHotkey,
        RephraseHotkey = RephraseHotkey,
        StartWithWindows = StartWithWindows,
        LanguageToolEndpoint = LanguageToolEndpoint,
        Language = Language,
        PreferredVariants = PreferredVariants,
        InterfaceLanguage = InterfaceLanguage,
        LlmEndpoint = LlmEndpoint,
        LlmModel = LlmModel,
        LogLevel = LogLevel,
        MaxInputLength = MaxInputLength,
        ClipboardWaitMilliseconds = ClipboardWaitMilliseconds,
        PreferUiAutomation = PreferUiAutomation,
        RequestTimeoutSeconds = RequestTimeoutSeconds,
    };
}
