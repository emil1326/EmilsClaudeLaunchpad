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

    public bool Launch(GroupPreset group, IReadOnlyDictionary<string, TabPreset> tabsById, string defaultShell)
    {
        var missingIds = group.TabIds.Where(id => !tabsById.ContainsKey(id)).ToList();
        if (missingIds.Count > 0)
            _notifyError("Missing tabs",
                $"Group '{group.Title}' references unknown tab id(s): {string.Join(", ", missingIds)}. Open the editor to clean it up.");

        var args = WtCommandBuilder.BuildArgumentList(group, tabsById, defaultShell);
        if (args.Count == 0)
        {
            _notifyError("Launch skipped", $"Group '{group.Title}' has no usable tabs.");
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
            _notifyError($"Failed to launch '{group.Title}'", ex.Message);
            return false;
        }
    }

    public async Task LaunchAllAsync(IEnumerable<GroupPreset> groups, IReadOnlyDictionary<string, TabPreset> tabsById, string defaultShell)
    {
        foreach (var g in groups)
        {
            Launch(g, tabsById, defaultShell);
            await Task.Delay(80);
        }
    }
}
