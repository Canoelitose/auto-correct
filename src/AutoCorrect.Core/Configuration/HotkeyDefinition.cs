using System.Diagnostics.CodeAnalysis;
using AutoCorrect.Core.Localization;

namespace AutoCorrect.Core.Configuration;

/// <summary>
/// Modifier flags. The values match the MOD_* constants of RegisterHotKey so the
/// interop layer can pass them through unchanged.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

/// <summary>
/// A parsed hotkey such as "Ctrl+Alt+Space". Stored as text in settings.json so it can
/// be pre-seeded by a script or group policy.
/// </summary>
public readonly record struct HotkeyDefinition(HotkeyModifiers Modifiers, uint VirtualKey)
{
    /// <summary>
    /// Win+Space. Windows uses this combination for the keyboard layout switcher, so
    /// RegisterHotKey cannot claim it; the hotkey manager falls back to a low level keyboard
    /// hook, which intercepts the combination and swallows it.
    /// </summary>
    public static readonly HotkeyDefinition DefaultPrimary =
        new(HotkeyModifiers.Windows, VirtualKeys.Space);

    /// <summary>Combination used before Win+Space became the default.</summary>
    public static readonly HotkeyDefinition ClassicPrimary =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.Space);

    public static readonly HotkeyDefinition DefaultRephrase =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, VirtualKeys.R);

    public bool IsEmpty => Modifiers == HotkeyModifiers.None && VirtualKey == 0;

    /// <summary>
    /// True for combinations Windows claims for itself. RegisterHotKey fails on these, so the
    /// low level keyboard hook is used instead.
    /// </summary>
    public bool IsReservedByWindows =>
        Modifiers == HotkeyModifiers.Windows &&
        VirtualKey is VirtualKeys.Space or VirtualKeys.Tab;

    /// <summary>Returns a German error message, or null when the combination is usable.</summary>
    public string? Validate()
    {
        if (IsEmpty)
        {
            return UiText.HotkeyEmpty;
        }

        if (VirtualKey == 0)
        {
            return UiText.HotkeyNeedsKey;
        }

        if (Modifiers == HotkeyModifiers.None)
        {
            return UiText.HotkeyNeedsModifier;
        }

        return null;
    }

    public static bool TryParse(string? text, out HotkeyDefinition result, [NotNullWhen(false)] out string? error)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = UiText.HotkeyEmpty;
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        uint virtualKey = 0;

        foreach (var rawToken in text.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            switch (token.ToUpperInvariant())
            {
                case "CTRL":
                case "CONTROL":
                case "STRG":
                    modifiers |= HotkeyModifiers.Control;
                    continue;
                case "ALT":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "SHIFT":
                case "UMSCHALT":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "WIN":
                case "WINDOWS":
                    modifiers |= HotkeyModifiers.Windows;
                    continue;
            }

            if (!VirtualKeys.TryGetCode(token, out var code))
            {
                error = UiText.HotkeyUnknownKey(token);
                return false;
            }

            if (virtualKey != 0)
            {
                error = UiText.HotkeyUnknownKey(token);
                return false;
            }

            virtualKey = code;
        }

        var candidate = new HotkeyDefinition(modifiers, virtualKey);
        var validationError = candidate.Validate();
        if (validationError is not null)
        {
            error = validationError;
            return false;
        }

        result = candidate;
        error = null;
        return true;
    }

    /// <summary>Parses, falling back to <paramref name="fallback"/> on any problem.</summary>
    public static HotkeyDefinition ParseOrDefault(string? text, HotkeyDefinition fallback) =>
        TryParse(text, out var parsed, out _) ? parsed : fallback;

    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        if (VirtualKey != 0)
        {
            parts.Add(VirtualKeys.GetName(VirtualKey));
        }

        return string.Join("+", parts);
    }
}
