using System.Diagnostics;
using System.Reflection;
using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Launching;
using EmilsClaudeLaunchpad.Startup;
using EmilsClaudeLaunchpad.Update;

namespace EmilsClaudeLaunchpad;

public sealed class LauncherForm : Form
{
    private readonly SessionLauncher _launcher;
    private readonly UpdateManager _updater;
    private readonly Action<string, string> _notify;

    private FlowLayoutPanel _sessionsPanel = null!;
    private CheckBox _autostartCheckbox = null!;
    private PresetsConfig _config = new();

    public LauncherForm(SessionLauncher launcher, UpdateManager updater, Action<string, string> notify)
    {
        _launcher = launcher;
        _updater = updater;
        _notify = notify;

        Text = "Emil's Claude Launchpad";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(340, 480);
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.Gainsboro;
        Font = new Font("Segoe UI", 9F);

        BuildLayout();
        Deactivate += (_, _) => Hide();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12),
            BackColor = BackColor,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _sessionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BackColor,
        };
        root.Controls.Add(_sessionsPanel, 0, 0);
        root.SetColumnSpan(_sessionsPanel, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var launchAll = MakeFlatButton("Launch all", Color.FromArgb(48, 80, 140));
        launchAll.Click += async (_, _) => await OnLaunchAllAsync();
        root.Controls.Add(launchAll, 0, 1);
        root.SetColumnSpan(launchAll, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var editBtn = MakeFlatButton("Edit config", Color.FromArgb(50, 50, 56));
        editBtn.Click += (_, _) => OnEditConfig();
        var reloadBtn = MakeFlatButton("Reload", Color.FromArgb(50, 50, 56));
        reloadBtn.Click += (_, _) => ReloadConfig();
        root.Controls.Add(editBtn, 0, 2);
        root.Controls.Add(reloadBtn, 1, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        _autostartCheckbox = new CheckBox
        {
            Text = "Autostart at login",
            Dock = DockStyle.Fill,
            ForeColor = Color.Gainsboro,
            BackColor = BackColor,
            Margin = new Padding(0, 8, 0, 0),
        };
        _autostartCheckbox.CheckedChanged += OnAutostartToggled;
        root.Controls.Add(_autostartCheckbox, 0, 3);
        root.SetColumnSpan(_autostartCheckbox, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        var updatesBtn = MakeFlatButton("Check for updates", Color.FromArgb(50, 50, 56));
        updatesBtn.Click += async (_, _) => await _updater.CheckAndApplyAsync();
        root.Controls.Add(updatesBtn, 0, 4);
        root.SetColumnSpan(updatesBtn, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var versionLabel = new Label
        {
            Text = $"v{GetAssemblyVersion()}",
            ForeColor = Color.DimGray,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var exitBtn = MakeFlatButton("Exit", Color.FromArgb(80, 40, 40));
        exitBtn.Click += (_, _) => Application.Exit();
        root.Controls.Add(versionLabel, 0, 5);
        root.Controls.Add(exitBtn, 1, 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        Controls.Add(root);
    }

    public void ShowNearCursor()
    {
        ReloadConfig();
        var p = Cursor.Position;
        var screen = Screen.FromPoint(p).WorkingArea;
        var x = Math.Min(p.X - Width / 2, screen.Right - Width - 10);
        var y = Math.Min(p.Y - Height - 10, screen.Bottom - Height - 10);
        Location = new Point(Math.Max(screen.Left + 10, x), Math.Max(screen.Top + 10, y));
        Show();
        Activate();
    }

    private void ReloadConfig()
    {
        try { _config = ConfigStore.Load(); }
        catch (Exception ex)
        {
            _notify("Config error", ex.Message);
            _config = new PresetsConfig();
        }
        PopulateSessionButtons();
        _autostartCheckbox.CheckedChanged -= OnAutostartToggled;
        _autostartCheckbox.Checked = AutoStartManager.IsEnabled;
        _autostartCheckbox.CheckedChanged += OnAutostartToggled;
    }

    private void PopulateSessionButtons()
    {
        _sessionsPanel.SuspendLayout();
        _sessionsPanel.Controls.Clear();

        if (_config.Sessions.Count == 0)
        {
            var empty = new Label
            {
                Text = "No sessions configured.\nClick 'Edit config' to add some.",
                ForeColor = Color.DimGray,
                AutoSize = false,
                Size = new Size(_sessionsPanel.ClientSize.Width - 8, 60),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _sessionsPanel.Controls.Add(empty);
        }
        else
        {
            foreach (var session in _config.Sessions)
            {
                var color = PickSessionColor(session);
                var btn = MakeFlatButton(session.Title, color);
                btn.Width = _sessionsPanel.ClientSize.Width - 8;
                btn.Height = 40;
                btn.Margin = new Padding(0, 0, 0, 6);
                btn.Click += (_, _) => OnLaunchSession(session);
                _sessionsPanel.Controls.Add(btn);
            }
        }

        _sessionsPanel.ResumeLayout();
    }

    private void OnLaunchSession(SessionPreset session)
    {
        var ok = _launcher.Launch(session, _config.Settings.DefaultShell);
        if (ok) Hide();
    }

    private async Task OnLaunchAllAsync()
    {
        await _launcher.LaunchAllAsync(_config.Sessions, _config.Settings.DefaultShell);
        Hide();
    }

    private void OnEditConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ConfigStore.GetConfigPath()) { UseShellExecute = true });
            Hide();
        }
        catch (Exception ex)
        {
            _notify("Couldn't open config", ex.Message);
        }
    }

    private void OnAutostartToggled(object? sender, EventArgs e)
    {
        try
        {
            if (_autostartCheckbox.Checked) AutoStartManager.Enable();
            else AutoStartManager.Disable();
        }
        catch (Exception ex)
        {
            _notify("Autostart toggle failed", ex.Message);
        }
    }

    private static Color PickSessionColor(SessionPreset session)
    {
        var firstColor = session.EnumerateTabs().FirstOrDefault()?.TabColor;
        return ParseHexColor(firstColor) ?? Color.FromArgb(60, 60, 70);
    }

    private static Color? ParseHexColor(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (hex.StartsWith('#')) hex = hex[1..];
        if (hex.Length != 6) return null;
        try
        {
            return Color.FromArgb(
                Convert.ToInt32(hex[..2], 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
        }
        catch
        {
            return null;
        }
    }

    private static Color ContrastForeground(Color bg)
    {
        var luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance > 0.55 ? Color.Black : Color.White;
    }

    private static Button MakeFlatButton(string text, Color bg)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = bg,
            ForeColor = ContrastForeground(bg),
            FlatStyle = FlatStyle.Flat,
            AutoSize = false,
            Margin = new Padding(2),
        };
        btn.FlatAppearance.BorderSize = 0;
        return btn;
    }

    private static string GetAssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}
