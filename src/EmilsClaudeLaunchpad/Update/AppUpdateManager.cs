using Velopack;
using Velopack.Sources;

namespace EmilsClaudeLaunchpad.Update;

public sealed class AppUpdateManager
{
    public const string RepoUrl = "https://github.com/emil1326/EmilsClaudeLaunchpad";

    private readonly Action<string, string> _notify;
    private readonly UpdateManager? _vmgr;

    public AppUpdateManager(Action<string, string> notify)
    {
        _notify = notify;
        try
        {
            var source = new GithubSource(RepoUrl, accessToken: string.Empty, prerelease: false);
            _vmgr = new UpdateManager(source);
        }
        catch
        {
            _vmgr = null;
        }
    }

    public bool IsAvailable => _vmgr is not null && _vmgr.IsInstalled;

    public async Task CheckOnStartupAsync()
    {
        if (!IsAvailable) return;
        try
        {
            var info = await _vmgr!.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is not null)
                _notify("Update available", "Open the launcher and click 'Check for updates' to apply.");
        }
        catch
        {
            // swallow — startup check is best-effort
        }
    }

    public async Task CheckAndApplyAsync()
    {
        if (!IsAvailable)
        {
            _notify("Update check skipped", "Running from a dev build (no Velopack install detected).");
            return;
        }
        try
        {
            var info = await _vmgr!.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                _notify("Up to date", "You're running the latest version.");
                return;
            }
            await _vmgr.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _vmgr.ApplyUpdatesAndRestart(info);
        }
        catch (Exception ex)
        {
            _notify("Update failed", ex.Message);
        }
    }
}
