using AutoCorrect.Core.Configuration;

namespace AutoCorrect.Core.Tests.Cases;

public static class HotkeyDefinitionTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Hotkey: parses the default combination", () =>
        {
            Assert.True(HotkeyDefinition.TryParse("Ctrl+Alt+Space", out var hotkey, out var error), error ?? "");
            Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hotkey.Modifiers);
            Assert.Equal(VirtualKeys.Space, hotkey.VirtualKey);
        });

        runner.Add("Hotkey: round trips through text", () =>
        {
            // Canonical order produced by ToString(): Ctrl, Alt, Shift, Win, key.
            foreach (var text in new[] { "Ctrl+Alt+Space", "Ctrl+Alt+R", "Ctrl+Shift+F9", "Alt+Win+Period" })
            {
                Assert.True(HotkeyDefinition.TryParse(text, out var hotkey, out var error), error ?? "");
                Assert.Equal(text, hotkey.ToString());
            }
        });

        runner.Add("Hotkey: modifier order and casing are irrelevant", () =>
        {
            Assert.True(HotkeyDefinition.TryParse("alt + STRG + space", out var a, out _));
            Assert.True(HotkeyDefinition.TryParse("Ctrl+Alt+Space", out var b, out _));
            Assert.Equal(b, a);
        });

        runner.Add("Hotkey: rejects a combination without a modifier", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("F5", out _, out var error));
            Assert.NotNull(error);
            Assert.Contains("Zusatztaste", error!);
        });

        runner.Add("Hotkey: rejects modifiers without a key", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("Ctrl+Alt", out _, out var error));
            Assert.NotNull(error);
        });

        runner.Add("Hotkey: rejects Win+Space, which Windows uses for the layout switch", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("Win+Space", out _, out var error));
            Assert.NotNull(error);
            Assert.Contains("Win+Space", error!);
        });

        runner.Add("Hotkey: rejects unknown keys", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("Ctrl+Alt+Bratwurst", out _, out var error));
            Assert.NotNull(error);
            Assert.Contains("Bratwurst", error!);
        });

        runner.Add("Hotkey: rejects empty input", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("   ", out _, out var error));
            Assert.NotNull(error);
        });

        runner.Add("Hotkey: rejects two main keys", () =>
        {
            Assert.False(HotkeyDefinition.TryParse("Ctrl+A+B", out _, out var error));
            Assert.NotNull(error);
        });

        runner.Add("Hotkey: falls back to the default on garbage", () =>
        {
            var parsed = HotkeyDefinition.ParseOrDefault("nonsense", HotkeyDefinition.DefaultPrimary);
            Assert.Equal(HotkeyDefinition.DefaultPrimary, parsed);
        });

        runner.Add("Hotkey: German key aliases are accepted", () =>
        {
            Assert.True(HotkeyDefinition.TryParse("Strg+Umschalt+Leertaste", out var hotkey, out var error), error ?? "");
            Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, hotkey.Modifiers);
            Assert.Equal(VirtualKeys.Space, hotkey.VirtualKey);
        });

        runner.Add("Hotkey: modifier flag values match the RegisterHotKey constants", () =>
        {
            Assert.Equal(0x0001, (int)HotkeyModifiers.Alt);
            Assert.Equal(0x0002, (int)HotkeyModifiers.Control);
            Assert.Equal(0x0004, (int)HotkeyModifiers.Shift);
            Assert.Equal(0x0008, (int)HotkeyModifiers.Windows);
        });

        runner.Add("VirtualKeys: letter and function key codes", () =>
        {
            Assert.True(VirtualKeys.TryGetCode("A", out var a));
            Assert.Equal(0x41u, a);
            Assert.True(VirtualKeys.TryGetCode("f12", out var f12));
            Assert.Equal(0x7Bu, f12);
            Assert.Equal("Space", VirtualKeys.GetName(0x20));
        });
    }
}
