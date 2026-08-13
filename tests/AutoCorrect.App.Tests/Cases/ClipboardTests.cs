using System.Windows;
using AutoCorrect.App.Capture;
using AutoCorrect.Core.Tests;

namespace AutoCorrect.App.Tests.Cases;

public static class ClipboardTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Clipboard: text survives snapshot, clear and restore", async () =>
        {
            const string original = "Das ist der ursprüngliche Inhalt der Zwischenablage.";
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, original), copy: true);

            var snapshot = await ClipboardSnapshot.CaptureAsync();
            await ClipboardSnapshot.ClearAsync();

            Assert.False(Clipboard.ContainsText(), "the clipboard was not cleared");

            await snapshot.RestoreAsync();
            Assert.Equal(original, Clipboard.GetText());
        });

        runner.Add("Clipboard: the round trip of the capture path leaves no trace", async () =>
        {
            // This is the sequence the selection capture runs: save, clear, copy, read, restore.
            const string userContent = "Etwas, das der Benutzer vorher kopiert hat.";
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, userContent), copy: true);

            var snapshot = await ClipboardSnapshot.CaptureAsync();
            try
            {
                await ClipboardSnapshot.ClearAsync();
                await ClipboardSnapshot.SetTextAsync("die vom Programm kopierte Auswahl");
                Assert.Equal("die vom Programm kopierte Auswahl", await ClipboardSnapshot.ReadTextAsync());
            }
            finally
            {
                await snapshot.RestoreAsync();
            }

            Assert.Equal(userContent, Clipboard.GetText());
        });

        runner.Add("Clipboard: an empty clipboard stays empty after restore", async () =>
        {
            await ClipboardSnapshot.ClearAsync();

            var snapshot = await ClipboardSnapshot.CaptureAsync();
            await ClipboardSnapshot.SetTextAsync("zwischendurch");
            await snapshot.RestoreAsync();

            Assert.False(Clipboard.ContainsText(), "text was left behind on an empty clipboard");
        });

        runner.Add("Clipboard: reading returns null when there is no text", async () =>
        {
            await ClipboardSnapshot.ClearAsync();
            Assert.True(await ClipboardSnapshot.ReadTextAsync() is null, "expected no text");
        });

        runner.Add("Clipboard: several formats are preserved", async () =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, "Nur Text");
            data.SetData(DataFormats.Rtf, @"{\rtf1\ansi Nur Text}");
            Clipboard.SetDataObject(data, copy: true);

            var snapshot = await ClipboardSnapshot.CaptureAsync();
            await ClipboardSnapshot.ClearAsync();
            await snapshot.RestoreAsync();

            Assert.Equal("Nur Text", Clipboard.GetText());
            Assert.True(Clipboard.ContainsData(DataFormats.Rtf), "the RTF format was lost");
        });

        runner.Add("Clipboard: unicode text survives unchanged", async () =>
        {
            const string text = "Grüezi – die Strasse ist gross. 你好 \U0001F600";
            await ClipboardSnapshot.SetTextAsync(text);
            Assert.Equal(text, await ClipboardSnapshot.ReadTextAsync());
        });
    }
}
