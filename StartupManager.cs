using System;
using Microsoft.Win32;

namespace ClaudeUsageWidget;

// Registers/unregisters the app in the per-user "Run" registry key, the
// standard startup mechanism for non-packaged (non-MSIX) desktop apps.
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // One entry per profile, so several widgets can start with Windows independently.
    private static string ValueName =>
        AppProfile.IsDefault ? "ClaudeUsageWidget" : $"ClaudeUsageWidget - {AppProfile.Name}";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null) return;

        if (enabled)
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            var command = AppProfile.IsDefault
                ? $"\"{exePath}\""
                : $"\"{exePath}\" --profile \"{AppProfile.Name}\"";
            key.SetValue(ValueName, command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
