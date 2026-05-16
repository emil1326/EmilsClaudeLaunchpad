using System.Diagnostics;
using System.Reflection;
using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Launching;
using EmilsClaudeLaunchpad.Startup;
using EmilsClaudeLaunchpad.Ui;
using EmilsClaudeLaunchpad.Update;
using static EmilsClaudeLaunchpad.Ui.Theme;

namespace EmilsClaudeLaunchpad;

public sealed class LauncherForm : Form
{
    private readonly SessionLauncher _launcher;
    private readonly AppUpdateManager _updater;
    private readonly Action<string, string> _notify;

    private FlowLayoutPanel _sessionsPanel = null!;
    private PresetsConfig _config = new();

    public LauncherForm(SessionLauncher launcher, AppUpdateManager updater, Action<string, string> notify)
    {
        _launcher = launcher;
        _updater = updater;
        _notify = notify;

        Text = "Emil's Claude Launchpad";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(320, 440);
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        Padding = new Padding(10);
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        BuildLayout();
        ReloadConfig();
        Deactivate += (_, _) => Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Bg);
        using var borderPen = new Pen(Border, 1);
        e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Bg,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 28,
            BackColor = Bg,
            Margin = new Padding(0, 0, 0, 8),
        };
        var titleLabel = new Label
        {
            Text = "Emil's Claude Launchpad",
            ForeColor = TextPrimary,
            BackColor = Bg,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(2, 0, 0, 0),
        };
        headerPanel.Controls.Add(titleLabel);
        root.Controls.Add(headerPanel, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var headerDivider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 0, 0, 8) };
        root.Controls.Add(headerDivider, 0, 1);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 9));

        _sessionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Bg,
            Margin = new Padding(0, 0, 0, 8),
        };
        root.Controls.Add(_sessionsPanel, 0, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var divider1 = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 4, 0, 8) };
        root.Controls.Add(divider1, 0, 3);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));

        var launchAll = MakePrimaryButton("Launch all");
        launchAll.Click += async (_, _) => await OnLaunchAllAsync();
        launchAll.Height = 34;
        launchAll.Dock = DockStyle.Top;
        launchAll.Margin = new Padding(0, 0, 0, 8);
        root.Controls.Add(launchAll, 0, 4);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        // 3-column action row: Edit / Reload / Settings cogwheel. Updates moved into the Settings
        // dialog ("Check now" button) — it didn't earn a permanent spot in the launcher chrome.
        var actionRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = 30,
            BackColor = Bg,
            Margin = new Padding(0, 0, 0, 8),
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
        var editBtn = MakeSecondaryButton("Edit");
        editBtn.Click += (_, _) => OnEditConfig();
        var reloadBtn = MakeSecondaryButton("Reload");
        reloadBtn.Click += (_, _) => ReloadConfig();
        var settingsBtn = MakeSecondaryButton("⚙");
        settingsBtn.Font = new Font("Segoe UI Symbol", 11F);
        settingsBtn.Click += (_, _) => OnOpenSettings();
        editBtn.Dock = reloadBtn.Dock = settingsBtn.Dock = DockStyle.Fill;
        editBtn.Margin = new Padding(0, 0, 3, 0);
        reloadBtn.Margin = new Padding(3, 0, 3, 0);
        settingsBtn.Margin = new Padding(3, 0, 0, 0);
        actionRow.Controls.Add(editBtn, 0, 0);
        actionRow.Controls.Add(reloadBtn, 1, 0);
        actionRow.Controls.Add(settingsBtn, 2, 0);
        root.Controls.Add(actionRow, 0, 5);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 28,
            BackColor = Bg,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var versionLabel = new Label
        {
            Text = $"v{GetAssemblyVersion()}",
            ForeColor = TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(2, 0, 0, 0),
        };
        var exitBtn = MakeSecondaryButton("Exit");
        exitBtn.Click += (_, _) => Application.Exit();
        exitBtn.Dock = DockStyle.Fill;
        footer.Controls.Add(versionLabel, 0, 0);
        footer.Controls.Add(exitBtn, 1, 0);
        root.Controls.Add(footer, 0, 6);
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

    // Used when the form is woken by something other than a tray click (IPC ping from a 2nd
    // launch, where the cursor might be anywhere). Anchored to the bottom-right of the primary
    // working area — directly above where tray icons live on a default Windows layout.
    public void ShowNearTray()
    {
        ReloadConfig();
        var screen = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Right - Width - 10, screen.Bottom - Height - 10);
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
    }

    private void PopulateSessionButtons()
    {
        _sessionsPanel.SuspendLayout();
        _sessionsPanel.Controls.Clear();

        if (_config.Groups.Count == 0)
        {
            var empty = new Label
            {
                Text = "No groups configured.\nClick 'Edit' to add some.",
                ForeColor = TextMuted,
                AutoSize = false,
                Size = new Size(_sessionsPanel.ClientSize.Width - 4, 80),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _sessionsPanel.Controls.Add(empty);
        }
        else
        {
            foreach (var group in _config.Groups)
            {
                var accent = PickGroupColor(group);
                var btn = new SessionButton(group.Title, accent);
                btn.Width = _sessionsPanel.ClientSize.Width - 4;
                btn.Height = 36;
                btn.Margin = new Padding(0, 0, 0, 6);
                btn.Click += (_, _) => OnLaunchGroup(group);
                _sessionsPanel.Controls.Add(btn);
            }
        }

        _sessionsPanel.ResumeLayout();
    }

    private IReadOnlyDictionary<string, TabPreset> BuildTabsById() =>
        _config.Tabs.ToDictionary(t => t.Id, t => t);

    private void OnLaunchGroup(GroupPreset group)
    {
        var ok = _launcher.Launch(group, BuildTabsById(), _config.Settings.DefaultShell);
        if (ok) Hide();
    }

    private async Task OnLaunchAllAsync()
    {
        await _launcher.LaunchAllAsync(_config.Groups, BuildTabsById(), _config.Settings.DefaultShell);
        Hide();
    }

    private void OnEditConfig()
    {
        Hide();
        using var editor = new EditorForm(_config);
        editor.ShowDialog();
        if (editor.SavedChanges) ReloadConfig();
        // Launcher stays hidden after editor closes — user can click the tray again to reopen.
    }

    // Opens the cogwheel dialog. Every setting commits live inside (autostart → registry,
    // others → dlg.Result), so on close we just persist the non-registry bits to presets.json.
    // The updater is passed in so the dialog can host a "Check now" button without coupling
    // SettingsForm to AppUpdateManager directly.
    private void OnOpenSettings()
    {
        Hide();
        using var dlg = new SettingsForm(_config.Settings, () => _updater.CheckAndApplyAsync());
        dlg.ShowDialog();
        if (!dlg.Result.Equals(_config.Settings))
        {
            try
            {
                var updated = _config with { Settings = dlg.Result };
                ConfigStore.Save(updated);
            }
            catch (Exception ex)
            {
                _notify("Settings save failed", ex.Message);
            }
        }
        ReloadConfig();
    }

    private Color PickGroupColor(GroupPreset group)
    {
        var explicitColor = TryParseHex(group.Color);
        if (explicitColor.HasValue) return explicitColor.Value;
        var firstTabId = group.TabIds.FirstOrDefault();
        var firstTab = firstTabId is null ? null : _config.FindTab(firstTabId);
        return TryParseHex(firstTab?.TabColor) ?? NeutralAccent;
    }

    private static string GetAssemblyVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    // Custom-drawn session button: dark surface with a colored accent strip on the left.
    private sealed class SessionButton : Button
    {
        private readonly Color _accent;
        private bool _hovered;

        public SessionButton(string text, Color accent)
        {
            _accent = accent;
            Text = text;
            FlatStyle = FlatStyle.Flat;
            BackColor = Surface;
            ForeColor = TextPrimary;
            UseVisualStyleBackColor = false;
            TextAlign = ContentAlignment.MiddleLeft;
            Font = new Font("Segoe UI", 9.5F);
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = SurfaceHover;
            FlatAppearance.MouseDownBackColor = Surface;
            DoubleBuffered = true;
            MouseEnter += (_, _) => { _hovered = true; Invalidate(); };
            MouseLeave += (_, _) => { _hovered = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(_hovered ? SurfaceHover : Surface);

            // Accent stripe on the left
            using (var brush = new SolidBrush(_accent))
                g.FillRectangle(brush, 0, 0, 4, Height);

            // Text, with padding to clear the stripe
            var textRect = new Rectangle(14, 0, Width - 18, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }
}
