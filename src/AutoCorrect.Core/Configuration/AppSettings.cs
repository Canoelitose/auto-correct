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

    /// <summary>
    /// Bearer token for endpoints that require one. Ollama on the same machine needs none;
    /// llama.cpp started with --api-key, LM Studio behind a password and every hosted endpoint
    /// do. Empty means no Authorization header is sent at all.
    ///
    /// Stored in plain text in settings.json, like every other setting - it is protected by the
    /// file permissions of the user profile and nothing more.
    /// </summary>
    public string LlmApiKey { get; set; } = string.Empty;

    /// <summary>
    /// When names and contact details are replaced before the text is sent:
    /// "auto" only for an endpoint outside this machine and the local network, "always", "never".
    ///
    /// Auto is the default because a model on your own machine sees the text anyway, and masking
    /// costs a little accuracy - the model works on a sentence about someone else.
    /// </summary>
    public string LlmMaskNames { get; set; } = MaskNamesAuto;

    /// <summary>Words that are always replaced, whatever they are: own name, company, project.</summary>
    public List<string> LlmProtectedTerms { get; set; } = [];

    public const string MaskNamesAuto = "auto";
    public const string MaskNamesAlways = "always";
    public const string MaskNamesNever = "never";

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

        LlmMaskNames = LlmMaskNames?.Trim().ToLowerInvariant() switch
        {
            MaskNamesAlways => MaskNamesAlways,
            MaskNamesNever => MaskNamesNever,
            _ => MaskNamesAuto,
        };

        LlmProtectedTerms = LlmProtectedTerms is null
            ? []
            : LlmProtectedTerms
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

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
        LlmApiKey = LlmApiKey,
        LlmMaskNames = LlmMaskNames,
        LlmProtectedTerms = [.. LlmProtectedTerms],
        LogLevel = LogLevel,
        MaxInputLength = MaxInputLength,
        ClipboardWaitMilliseconds = ClipboardWaitMilliseconds,
        PreferUiAutomation = PreferUiAutomation,
        RequestTimeoutSeconds = RequestTimeoutSeconds,
    };
}
