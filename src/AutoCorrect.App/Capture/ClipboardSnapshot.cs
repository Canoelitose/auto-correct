using System.Runtime.InteropServices;
using System.Windows;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Capture;

/// <summary>
/// Saves and restores the clipboard around the simulated Ctrl+C / Ctrl+V.
///
/// Clipboard access fails with an <see cref="ExternalException"/> whenever another process holds
/// the clipboard open, so every access is retried. All methods must be called on the STA UI thread.
/// </summary>
public sealed class ClipboardSnapshot
{
    private const int MaxAttempts = 5;
    private const int RetryDelayMs = 50;

    /// <summary>
    /// Formats that survive a round trip through a new DataObject. Anything else (COM backed or
    /// non serialisable payloads) is skipped instead of risking an exception during restore.
    /// </summary>
    private static readonly string[] PreservedFormats =
    [
        DataFormats.UnicodeText,
        DataFormats.Text,
        DataFormats.Rtf,
        DataFormats.Html,
        DataFormats.FileDrop,
        DataFormats.Bitmap,
        DataFormats.CommaSeparatedValue,
    ];

    private readonly Dictionary<string, object> _data = new(StringComparer.Ordinal);

    private ClipboardSnapshot()
    {
    }

    public bool IsEmpty => _data.Count == 0;

    public static async Task<ClipboardSnapshot> CaptureAsync()
    {
        var snapshot = new ClipboardSnapshot();

        var source = await RetryAsync(Clipboard.GetDataObject, (IDataObject?)null);

        if (source is null)
        {
            return snapshot;
        }

        foreach (var format in PreservedFormats)
        {
            try
            {
                if (!source.GetDataPresent(format, autoConvert: false))
                {
                    continue;
                }

                var data = source.GetData(format, autoConvert: false);
                if (data is not null)
                {
                    snapshot._data[format] = data;
                }
            }
            catch (Exception ex) when (ex is ExternalException or OutOfMemoryException or NotSupportedException)
            {
                Log.Debug($"Clipboard format '{format}' could not be preserved: {ex.GetType().Name}");
            }
        }

        return snapshot;
    }

    /// <summary>Puts the saved content back. Never throws.</summary>
    public async Task RestoreAsync()
    {
        if (_data.Count == 0)
        {
            await RetryAsync(
                () =>
                {
                    Clipboard.Clear();
                    return true;
                },
                false);
            return;
        }

        var restored = new DataObject();
        foreach (var (format, value) in _data)
        {
            try
            {
                restored.SetData(format, value);
            }
            catch (Exception ex) when (ex is ExternalException or NotSupportedException or ArgumentException)
            {
                Log.Debug($"Clipboard format '{format}' could not be restored: {ex.GetType().Name}");
            }
        }

        await RetryAsync(
            () =>
            {
                // copy: true leaves the data on the clipboard after the process exits.
                Clipboard.SetDataObject(restored, copy: true);
                return true;
            },
            false);
    }

    public static Task<bool> ClearAsync() => RetryAsync(
        () =>
        {
            Clipboard.Clear();
            return true;
        },
        false);

    public static Task<string?> ReadTextAsync() => RetryAsync(
        () => Clipboard.ContainsText() ? Clipboard.GetText() : null,
        (string?)null);

    public static Task<bool> SetTextAsync(string text) => RetryAsync(
        () =>
        {
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, text);
            Clipboard.SetDataObject(data, copy: true);
            return true;
        },
        false);

    private static async Task<T> RetryAsync<T>(Func<T> action, T fallback)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
            {
                if (attempt == MaxAttempts)
                {
                    Log.Warn($"Clipboard access failed after {MaxAttempts} attempts.", ex);
                    return fallback;
                }

                // Await instead of Thread.Sleep: this runs on the UI thread and must not block it.
                await Task.Delay(RetryDelayMs);
            }
        }

        return fallback;
    }
}
