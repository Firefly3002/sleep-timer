using System.IO;
using Microsoft.Win32;

namespace SleepTimer.Desktop;

internal static class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SleepTimer";

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var executablePath = ResolveExecutablePath();
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                ?? throw new InvalidOperationException("Windows could not open your sign-in startup settings.");
            runKey.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            return;
        }

        using var existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ResolveExecutablePath()
    {
        var appHostPath = Path.Combine(AppContext.BaseDirectory, "SleepTimer.exe");
        if (File.Exists(appHostPath)) return appHostPath;

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath)
            && string.Equals(Path.GetExtension(processPath), ".exe", StringComparison.OrdinalIgnoreCase))
            return processPath;

        throw new InvalidOperationException("Could not find Sleep Timer’s Windows executable to add it to sign-in startup.");
    }
}
