using System.ComponentModel;
using System.Diagnostics;
using EmilsClaudeLaunchpad.Config;

namespace EmilsClaudeLaunchpad.Launching;

public sealed class SessionLauncher
{
    private readonly Action<string, string> _notifyError;

    public SessionLauncher(Action<string, string> notifyError)
    {
        _notifyError = notifyError;
    }

    public bool Launch(SessionPreset preset, string defaultShell)
    {
        var args = WtCommandBuilder.BuildArgumentList(preset, defaultShell);
        if (args.Count == 0)
        {
            _notifyError("Launch skipped", $"Session '{preset.Title}' has no tabs configured.");
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wt.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            _notifyError("wt.exe not found", "Windows Terminal isn't on PATH. Install it from the Microsoft Store.");
            return false;
        }
        catch (Exception ex)
        {
            _notifyError($"Failed to launch '{preset.Title}'", ex.Message);
            return false;
        }
    }

    public async Task LaunchAllAsync(IEnumerable<SessionPreset> presets, string defaultShell)
    {
        foreach (var p in presets)
        {
            Launch(p, defaultShell);
            await Task.Delay(80);
        }
    }
}
