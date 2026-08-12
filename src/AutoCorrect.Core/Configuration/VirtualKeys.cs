using System.Globalization;

namespace AutoCorrect.Core.Configuration;

/// <summary>
/// Mapping between Windows virtual key codes and the names used in settings.json.
/// Lives in Core so hotkey parsing stays testable without a Windows dependency.
/// </summary>
public static class VirtualKeys
{
    public const uint Back = 0x08;
    public const uint Tab = 0x09;
    public const uint Enter = 0x0D;
    public const uint Escape = 0x1B;
    public const uint Space = 0x20;
    public const uint PageUp = 0x21;
    public const uint PageDown = 0x22;
    public const uint End = 0x23;
    public const uint Home = 0x24;
    public const uint Left = 0x25;
    public const uint Up = 0x26;
    public const uint Right = 0x27;
    public const uint Down = 0x28;
    public const uint Insert = 0x2D;
    public const uint Delete = 0x2E;
    public const uint R = 0x52;

    private static readonly Dictionary<string, uint> NameToCode =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<uint, string> CodeToName = new();

    static VirtualKeys()
    {
        Register("Back", Back);
        Register("Tab", Tab);
        Register("Enter", Enter);
        Register("Escape", Escape);
        Register("Space", Space);
        Register("PageUp", PageUp);
        Register("PageDown", PageDown);
        Register("End", End);
        Register("Home", Home);
        Register("Left", Left);
        Register("Up", Up);
        Register("Right", Right);
        Register("Down", Down);
        Register("Insert", Insert);
        Register("Delete", Delete);

        for (var i = 0; i < 26; i++)
        {
            Register(((char)('A' + i)).ToString(), (uint)(0x41 + i));
        }

        for (var i = 0; i < 10; i++)
        {
            Register(i.ToString(CultureInfo.InvariantCulture), (uint)(0x30 + i));
        }

        for (var i = 1; i <= 24; i++)
        {
            Register("F" + i.ToString(CultureInfo.InvariantCulture), (uint)(0x70 + i - 1));
        }

        for (var i = 0; i < 10; i++)
        {
            Register("NumPad" + i.ToString(CultureInfo.InvariantCulture), (uint)(0x60 + i));
        }

        Register("Multiply", 0x6A);
        Register("Add", 0x6B);
        Register("Subtract", 0x6D);
        Register("Decimal", 0x6E);
        Register("Divide", 0x6F);
        Register("Plus", 0xBB);
        Register("Comma", 0xBC);
        Register("Minus", 0xBD);
        Register("Period", 0xBE);

        // Aliases: accepted on input, never produced by GetName.
        NameToCode["Return"] = Enter;
        NameToCode["Esc"] = Escape;
        NameToCode["Leertaste"] = Space;
        NameToCode["Entf"] = Delete;
        NameToCode["Einfg"] = Insert;
        NameToCode["Pos1"] = Home;
        NameToCode["Ende"] = End;
    }

    private static void Register(string name, uint code)
    {
        NameToCode[name] = code;
        CodeToName[code] = name;
    }

    public static bool TryGetCode(string name, out uint code) =>
        NameToCode.TryGetValue(name, out code);

    /// <summary>Human readable name, or a hex fallback for keys not in the table.</summary>
    public static string GetName(uint code) =>
        CodeToName.TryGetValue(code, out var name)
            ? name
            : "0x" + code.ToString("X2", CultureInfo.InvariantCulture);

    public static bool IsKnown(uint code) => CodeToName.ContainsKey(code);
}
