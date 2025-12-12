using System;
using System.IO;
using Microsoft.Win32;

namespace QuackDuck;

internal static class AutostartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static bool IsEnabled(string appName)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(appName) is string;
        }
        catch
        {
            return false;
        }
    }

    internal static void SetEnabled(string appName, string exePath, bool enable)
    {
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true) ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                return;
            }

            if (enable)
            {
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch
        {
            // Swallow registry errors to avoid crashing the pet.
        }
    }
}
