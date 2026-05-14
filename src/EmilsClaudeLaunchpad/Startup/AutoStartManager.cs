using Microsoft.Win32;

namespace EmilsClaudeLaunchpad.Startup;

public static class AutoStartManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "EmilsClaudeLaunchpad";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is not null;
        }
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        key.SetValue(ValueName, $"\"{GetExePath()}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not null) key.DeleteValue(ValueName);
    }

    private static string GetExePath()
    {
        var procPath = Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(procPath)) return procPath;

        // If running inside a Velopack install (versioned subfolder), prefer the
        // stable launcher at the install root so updates don't break the registry value.
        var dir = Path.GetDirectoryName(procPath);
        if (dir is null) return procPath;

        var parent = Directory.GetParent(dir);
        if (parent is null) return procPath;

        var exeName = Path.GetFileName(procPath);
        var stable = Path.Combine(parent.FullName, exeName);
        if (File.Exists(stable) && !string.Equals(stable, procPath, StringComparison.OrdinalIgnoreCase))
            return stable;

        return procPath;
    }
}
