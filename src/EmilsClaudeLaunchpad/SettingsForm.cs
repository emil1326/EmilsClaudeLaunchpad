using System.Runtime.InteropServices;
using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Startup;
using EmilsClaudeLaunchpad.Ui;
using static EmilsClaudeLaunchpad.Ui.Theme;

namespace EmilsClaudeLaunchpad;

// App-wide preferences. Every control commits live — autostart hits the registry on change,
// the other fields update _result so the caller can pull a fresh snapshot on close. No
// Save/Cancel pair: there's nothing to roll back.
public sealed class SettingsForm : Form
{
    private readonly AppSettings _initial;
    private readonly Func<Task>? _onCheckUpdates;
    private AppSettings _result;
    private ComboBox _shellBox = null!;
    private CheckBox _autostartBox = null!;
    private CheckBox _autoUpdateBox = null!;
    private Button? _checkUpdatesNowBtn;
    private Label? _updateStatusLabel;
    private bool _loading; // suppress live-apply while LoadInitial seeds controls

    public AppSettings Result => _result;
    public bool AutostartChanged { get; private set; }

    private static readonly string[] ShellOptions = { "powershell", "pwsh", "cmd", "wsl" };

    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    public SettingsForm(AppSettings initial, Func<Task>? onCheckUpdates = null)
    {
        _initial = initial;
        _result = initial;
        _onCheckUpdates = onCheckUpdates;
        Text = "Settings";
        Icon = AppIcons.LoadApp();
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        // Width sized so help-text labels fit on one line; height tuned for 4 setting rows + footer.
        ClientSize = new Size(520, 360);
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            // Don't close when the user is just dismissing the open dropdown — Escape should pop
            // the dropdown, not the whole window.
            if (e.KeyCode == Keys.Escape && !_shellBox.DroppedDown) Close();
        };

        BuildLayout();
        LoadInitial();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Bg);
        using var pen = new Pen(Border, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void BuildLayout()
    {
        // Single-panel layout (no nested TLP for the body) — simpler and Dock=Top stacking is
        // predictable. Each setting row is built and appended in display order.
        var root = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Bg,
            Padding = new Padding(18, 14, 18, 14),
        };

        // Footer first so it docks to the bottom of root (Dock=Bottom over Fill). Close button
        // has explicit Size + Anchor; using Dock=Fill inside a TLP cell with Margin was clipping
        // the text in earlier versions.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Bg };
        var done = MakePrimaryButton("Close");
        done.Size = new Size(110, 32);
        done.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        done.Location = new Point(0, 6); // updated on Resize below
        done.Click += (_, _) => Close();
        footer.Controls.Add(done);
        footer.Layout += (_, _) => done.Left = footer.ClientSize.Width - done.Width;
        AcceptButton = done;
        root.Controls.Add(footer);

        var header = BuildHeader();
        var divider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 0, 0, 14) };

        // Body container. Add settings in REVERSE display order because Dock=Top stacks the
        // most-recently-added control at the top of the remaining space.
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Bg };

        if (_onCheckUpdates is not null)
            body.Controls.Add(BuildUpdateNowRow());

        body.Controls.Add(BuildAutoUpdateRow());
        body.Controls.Add(BuildAutostartRow());
        body.Controls.Add(BuildShellRow());

        root.Controls.Add(body);
        root.Controls.Add(divider);
        root.Controls.Add(header);

        Controls.Add(root);
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 32, BackColor = Bg, Cursor = Cursors.SizeAll, Margin = new Padding(0, 0, 0, 8) };
        var title = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll,
        };
        var close = MakeCloseButton();
        close.Width = 32;
        close.Dock = DockStyle.Right;
        close.Click += (_, _) => Close();
        header.Controls.Add(title);
        header.Controls.Add(close);
        header.MouseDown += DragHeader;
        title.MouseDown += DragHeader;
        return header;
    }

    private void DragHeader(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private Panel BuildShellRow()
    {
        _shellBox = new ComboBox
        {
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 280,
        };
        _shellBox.Items.AddRange(ShellOptions);
        _shellBox.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            _result = _result with { DefaultShell = (string)(_shellBox.SelectedItem ?? "powershell") };
        };
        return BuildSettingRow("Default shell", _shellBox, "Used when a tab doesn't override its own shell.");
    }

    private Panel BuildAutostartRow()
    {
        _autostartBox = MakeCheck("Run when Windows starts");
        _autostartBox.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            ApplyAutostart(_autostartBox.Checked);
        };
        return BuildSettingRow("Autostart at login", _autostartBox, "Writes to HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run. Applies immediately.");
    }

    private Panel BuildAutoUpdateRow()
    {
        _autoUpdateBox = MakeCheck("Check on startup");
        _autoUpdateBox.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _result = _result with { AutoCheckUpdatesOnStartup = _autoUpdateBox.Checked };
        };
        return BuildSettingRow("Auto-updates", _autoUpdateBox, "When off, you can still update on demand using the button below.");
    }

    private Panel BuildUpdateNowRow()
    {
        _checkUpdatesNowBtn = MakeSecondaryButton("Check now", bordered: true);
        _checkUpdatesNowBtn.Size = new Size(140, 26);
        _checkUpdatesNowBtn.Click += async (_, _) =>
        {
            if (_onCheckUpdates is null) return;
            _checkUpdatesNowBtn.Enabled = false;
            if (_updateStatusLabel is not null) _updateStatusLabel.Text = "Checking…";
            try { await _onCheckUpdates(); }
            finally { _checkUpdatesNowBtn.Enabled = true; }
        };
        _updateStatusLabel = new Label
        {
            Text = string.Empty,
            ForeColor = TextDim,
            Font = new Font("Segoe UI", 8F),
            AutoSize = true,
            Margin = new Padding(8, 5, 0, 0),
        };
        var inline = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            BackColor = Bg,
        };
        inline.Controls.Add(_checkUpdatesNowBtn);
        inline.Controls.Add(_updateStatusLabel);
        return BuildSettingRow("Update manually", inline, "Triggers an immediate Velopack check. Restarts the app if an update is applied.");
    }

    // Each row = label on the left (160px), control on the right (fills), help text on a second
    // line under the control. The row sizes itself based on contents.
    private static Panel BuildSettingRow(string labelText, Control control, string helpText)
    {
        var row = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = Bg,
            Margin = new Padding(0, 0, 0, 6),
        };

        var lbl = new Label
        {
            Text = labelText,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Location = new Point(0, 4),
            Size = new Size(160, 22),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        row.Controls.Add(lbl);

        control.Location = new Point(168, 2);
        control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        if (control.Width < 200) control.Width = row.ClientSize.Width - 168;
        // For ComboBox/CheckBox we want their natural rendered height — only force a min.
        if (control.Height < 22) control.Height = 22;
        row.Controls.Add(control);
        row.Resize += (_, _) =>
        {
            // Stretch the control to fill remaining width on resize. Some control types (ComboBox)
            // ignore Anchor right-stretch when AutoSize semantics interfere, so we recompute.
            if (control is not CheckBox && control is not FlowLayoutPanel)
                control.Width = row.ClientSize.Width - control.Left;
        };

        var help = new Label
        {
            Text = helpText,
            Font = new Font("Segoe UI", 8F),
            ForeColor = TextDim,
            Location = new Point(168, 32),
            AutoSize = false,
            Size = new Size(330, 20),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };
        row.Controls.Add(help);
        row.Resize += (_, _) => help.Width = row.ClientSize.Width - help.Left;

        return row;
    }

    private static CheckBox MakeCheck(string text) => new()
    {
        Text = text,
        ForeColor = TextPrimary,
        BackColor = Bg,
        FlatStyle = FlatStyle.Flat,
        AutoSize = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private void LoadInitial()
    {
        _loading = true;
        // Fallback: persisted shell may be a legacy value no longer in our list. Default to
        // powershell so the DropDownList isn't left with nothing selected.
        var shell = string.IsNullOrWhiteSpace(_initial.DefaultShell) ? "powershell" : _initial.DefaultShell;
        if (!ShellOptions.Contains(shell)) shell = "powershell";
        _shellBox.SelectedItem = shell;
        _autostartBox.Checked = AutoStartManager.IsEnabled;
        _autoUpdateBox.Checked = _initial.AutoCheckUpdatesOnStartup;
        _loading = false;
    }

    private void ApplyAutostart(bool wantOn)
    {
        if (wantOn == AutoStartManager.IsEnabled) return;
        try
        {
            if (wantOn) AutoStartManager.Enable();
            else AutoStartManager.Disable();
            AutostartChanged = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Autostart toggle failed: {ex.Message}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // Revert the checkbox to reality so the user sees the toggle didn't take.
            _loading = true;
            _autostartBox.Checked = AutoStartManager.IsEnabled;
            _loading = false;
        }
    }
}
