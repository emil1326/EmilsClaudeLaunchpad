using EmilsClaudeLaunchpad.Launching;
using EmilsClaudeLaunchpad.Update;

namespace EmilsClaudeLaunchpad;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly SessionLauncher _launcher;
    private readonly AppUpdateManager _updater;
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
        // before our dark theme is applied.
        _form = new LauncherForm(_launcher, _updater, ShowBalloon)
        {
            Location = new Point(-32000, -32000),
            Opacity = 0,
        };
        _form.Show();
        _form.Hide();
        _form.Opacity = 1;

        _ = _updater.CheckOnStartupAsync();
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button is MouseButtons.Left or MouseButtons.Right)
            ShowLauncher();
    }

    private void ShowLauncher()
    {
        if (_form is null || _form.IsDisposed)
            _form = new LauncherForm(_launcher, _updater, ShowBalloon);

        if (_form.Visible)
        {
            _form.Hide();
            return;
        }
        _form.ShowNearCursor();
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
            _icon.Visible = false;
            _icon.Dispose();
            _form?.Dispose();
        }
        base.Dispose(disposing);
    }
}
