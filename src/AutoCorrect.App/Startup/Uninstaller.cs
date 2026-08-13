using System.Diagnostics;
using System.IO;
using AutoCorrect.Core.Configuration;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Startup;

/// <summary>What a removal attempt did.</summary>
public sealed record UninstallResult(IReadOnlyList<string> Removed, IReadOnlyList<string> Failed);

/// <summary>
/// Removes everything the application leaves behind on a machine.
///
/// There is no installer, so Windows has nothing to offer under "Apps and features". Without
/// this the user has to know about two hidden folders and a registry value, which is not a
/// reasonable thing to expect.
///
/// The executable itself is not deleted here: a running process cannot reliably remove its own
/// file. The caller points the user at it instead.
/// </summary>
public static class Uninstaller
{
    public static string SettingsDirectory => SettingsStore.DefaultDirectory;

    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoCorrect");

    /// <summary>Deletes settings, logs and the autostart entry. Never throws.</summary>
    public static UninstallResult RemoveUserData()
    {
        var removed = new List<string>();
        var failed = new List<string>();

        if (AutoStartManager.IsEnabled())
        {
            if (AutoStartManager.SetEnabled(false))
            {
                removed.Add(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AutoCorrect");
            }
            else
            {
                failed.Add(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run\AutoCorrect");
            }
        }

        foreach (var directory in new[] { SettingsDirectory, LogDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                removed.Add(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The log file is still open while the application runs; that is expected and
                // the caller reports the path so the user can delete the rest by hand.
                Log.Warn($"'{directory}' could not be removed.", ex);
                failed.Add(directory);
            }
        }

        return new UninstallResult(removed, failed);
    }

    /// <summary>Opens Explorer with the executable selected, so it can be deleted.</summary>
    public static void ShowExecutableInExplorer()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn("Explorer could not be opened.", ex);
        }
    }
}
