using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Launching;
using EmilsClaudeLaunchpad.Startup;
using EmilsClaudeLaunchpad.Update;

namespace EmilsClaudeLaunchpad;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly SessionLauncher _launcher;
    private readonly AppUpdateManager _updater;
    private readonly IpcServer _ipc;
    private readonly SynchronizationContext _uiContext;
    private LauncherForm? _form;

    public TrayAppContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Emil's Claude Launchpad",
            Visible = true,
        };
        _icon.MouseUp += OnTrayMouseUp;

        _launcher = new SessionLauncher(ShowBalloon);
        _updater = new AppUpdateManager(ShowBalloon);

        // Pre-warm the form off-screen so the first show doesn't flash with default Windows colors
        // before our dark theme is applied. Pre-warming also forces handle creation, which is what
        // makes the IpcServer's BeginInvoke marshalling work below.
        _form = new LauncherForm(_launcher, _updater, ShowBalloon)
        {
            Location = new Point(-32000, -32000),
            Opacity = 0,
        };
        _form.Show();
        _form.Hide();
        _form.Opacity = 1;

        // Listen for "show" pings from a second launch and pop the launcher on the UI thread.
        // Use Form.BeginInvoke so we don't depend on the captured SynchronizationContext, which
        // is fragile for tray apps where Application.Run hasn't installed the WinForms context yet
        // at ctor time.
        _ipc = new IpcServer(WakeFromIpc);

        // Honor the user's "auto check on startup" preference. Reading the config here is
        // best-effort — if it's missing or corrupt, we fall through to default (check on).
        var checkOnStartup = true;
        try { checkOnStartup = ConfigStore.Load().Settings.AutoCheckUpdatesOnStartup; } catch { /* default */ }
        if (checkOnStartup) _ = _updater.CheckOnStartupAsync();
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
            ToggleLauncher();
    }

    private void WakeFromIpc()
    {
        // Always show — never toggle off — when triggered by a 2nd-launch ping. The user's intent
        // is "bring it up", not "blink it away if it happens to be visible."
        if (_form is { IsHandleCreated: true } f)
            f.BeginInvoke(new Action(ShowLauncherAtTray));
    }

    private void ToggleLauncher()
    {
        EnsureForm();
        if (_form!.Visible) { _form.Hide(); return; }
        _form.ShowNearCursor();
    }

    // IPC wake path: position the form near the tray (bottom-right of working area). The cursor
    // could be anywhere — desktop, browser, wherever the user double-clicked the .exe — and we
    // don't want the form to materialize in the middle of that.
    private void ShowLauncherAtTray()
    {
        EnsureForm();
        if (!_form!.Visible) _form.ShowNearTray();
        else { _form.BringToFront(); _form.Activate(); }
    }

    private void EnsureForm()
    {
        if (_form is null || _form.IsDisposed)
            _form = new LauncherForm(_launcher, _updater, ShowBalloon);
    }

    private void ShowBalloon(string title, string text)
    {
        _uiContext.Post(_ =>
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.ShowBalloonTip(3000);
        }, null);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ipc.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
            _form?.Dispose();
            SingleInstance.Release();
        }
        base.Dispose(disposing);
    }
}
