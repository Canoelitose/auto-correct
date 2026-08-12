using System.IO;
using Microsoft.Win32;
using AutoCorrect.Core.Diagnostics;

namespace AutoCorrect.App.Startup;

/// <summary>
/// "Start with Windows" through the per user Run key. No administrator rights needed,
/// no scheduled task, no service.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AutoCorrect";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("Autostart state could not be read.", ex);
            return false;
        }
    }

    /// <summary>Returns true when the change was applied.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
            {
                return false;
            }

            if (enabled)
            {
                var path = Environment.ProcessPath;
                if (string.IsNullOrEmpty(path))
                {
                    Log.Warn("Autostart could not be enabled: the executable path is unknown.");
                    return false;
                }

                key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn($"Autostart could not be set to {enabled}.", ex);
            return false;
        }
    }
}
