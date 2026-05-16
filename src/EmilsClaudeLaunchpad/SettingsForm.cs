using System.Runtime.InteropServices;
using EmilsClaudeLaunchpad.Config;
using EmilsClaudeLaunchpad.Startup;
using static EmilsClaudeLaunchpad.Ui.Theme;

namespace EmilsClaudeLaunchpad;

// Modal app-wide preferences. Lives in its own form so the EditorForm doesn't grow yet
// another card. Edits AppSettings + the registry-backed autostart flag in one place.
public sealed class SettingsForm : Form
{
    private readonly AppSettings _initial;
    private ComboBox _shellBox = null!;
    private CheckBox _autostartBox = null!;
    private CheckBox _autoUpdateBox = null!;

    public AppSettings? Result { get; private set; }
    public bool AutostartChanged { get; private set; }

    private static readonly string[] ShellOptions = { "powershell", "pwsh", "cmd", "wsl" };

    [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;

    public SettingsForm(AppSettings initial)
    {
        _initial = initial;
        Text = "Settings";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(440, 280);
        BackColor = Bg;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            // Don't close the dialog when the user is just dismissing an open dropdown — Escape
            // should pop the dropdown first, not the whole settings window.
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Bg,
            Padding = new Padding(16, 12, 16, 14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var divider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Border, Margin = new Padding(0, 0, 0, 12) };
        root.Controls.Add(divider, 0, 1);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 13));

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Bg,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(grid, "Default shell", _shellBox = MakeShellCombo(), 0, "Used when a tab doesn't override its own shell.");
        AddRow(grid, "Autostart at login", _autostartBox = MakeCheck("Run when Windows starts"), 1,
            "Toggles a HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run entry.");
        AddRow(grid, "Updates", _autoUpdateBox = MakeCheck("Check for updates on startup"), 2,
            "When off, you can still update manually from the launcher's Updates button.");

        root.Controls.Add(grid, 0, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            Height = 38,
            BackColor = Bg,
            Margin = new Padding(0, 12, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        footer.Controls.Add(new Panel { BackColor = Bg }, 0, 0);
        var cancel = MakeSecondaryButton("Cancel", bordered: true);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(0, 0, 8, 0);
        cancel.Click += (_, _) => Close();
        var save = MakePrimaryButton("Save");
        save.Dock = DockStyle.Fill;
        save.Click += (_, _) => SaveAndClose();
        footer.Controls.Add(cancel, 1, 0);
        footer.Controls.Add(save, 2, 0);
        root.Controls.Add(footer, 0, 3);
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        AcceptButton = save;
        CancelButton = cancel;

        Controls.Add(root);
    }

    private Panel BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Bg, Cursor = Cursors.SizeAll };
        var titleLabel = new Label
        {
            Text = "Settings",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextPrimary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll,
        };
        var closeBtn = MakeCloseButton();
        closeBtn.Width = 32;
        closeBtn.Dock = DockStyle.Right;
        closeBtn.Click += (_, _) => Close();
        header.Controls.Add(titleLabel);
        header.Controls.Add(closeBtn);
        header.MouseDown += DragHeader;
        titleLabel.MouseDown += DragHeader;
        return header;
    }

    private void DragHeader(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    private void AddRow(TableLayoutPanel grid, string labelText, Control control, int row, string helpText)
    {
        var lbl = new Label
        {
            Text = labelText,
            ForeColor = TextMuted,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 8, 0),
            AutoSize = false,
        };
        grid.Controls.Add(lbl, 0, row * 2);
        control.Dock = DockStyle.Top;
        control.Margin = new Padding(0, 4, 0, 0);
        grid.Controls.Add(control, 1, row * 2);

        var help = new Label
        {
            Text = helpText,
            ForeColor = TextDim,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI", 8F),
            AutoSize = false,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 0, 4),
        };
        grid.Controls.Add(new Panel { BackColor = Bg, Height = 22 }, 0, row * 2 + 1);
        grid.Controls.Add(help, 1, row * 2 + 1);

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
    }

    // DropDownList (not DropDown): the global default shell must be one of the supported names —
    // free-typing "fish" here would silently fail every time SessionLauncher tried to spawn it.
    // The per-tab editor still allows free typing; this is the safety net.
    private static ComboBox MakeShellCombo()
    {
        var cb = new ComboBox
        {
            BackColor = InputBg,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 200,
        };
        cb.Items.AddRange(ShellOptions);
        return cb;
    }

    private static CheckBox MakeCheck(string text) => new()
    {
        Text = text,
        ForeColor = TextPrimary,
        BackColor = Bg,
        FlatStyle = FlatStyle.Flat,
        AutoSize = true,
        Padding = new Padding(0),
    };

    private void LoadInitial()
    {
        // Persisted shell may be a legacy value that's no longer in our list — fall back to
        // powershell so the DropDownList isn't left with nothing selected.
        var shell = string.IsNullOrWhiteSpace(_initial.DefaultShell) ? "powershell" : _initial.DefaultShell;
        if (!ShellOptions.Contains(shell)) shell = "powershell";
        _shellBox.SelectedItem = shell;
        _autostartBox.Checked = AutoStartManager.IsEnabled;
        _autoUpdateBox.Checked = _initial.AutoCheckUpdatesOnStartup;
    }

    private void SaveAndClose()
    {
        // Autostart is a registry side-effect (no JSON home) — apply BEFORE we stash the result so
        // a failure can abort the close and let the user retry without losing the dialog state.
        var wantAutostart = _autostartBox.Checked;
        if (wantAutostart != AutoStartManager.IsEnabled)
        {
            try
            {
                if (wantAutostart) AutoStartManager.Enable();
                else AutoStartManager.Disable();
                AutostartChanged = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Autostart toggle failed: {ex.Message}", "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return; // keep dialog open so the user can retry without losing the other edits
            }
        }

        var pickedShell = _shellBox.SelectedItem as string ?? "powershell";
        Result = _initial with
        {
            DefaultShell = pickedShell,
            AutoCheckUpdatesOnStartup = _autoUpdateBox.Checked,
        };
        DialogResult = DialogResult.OK;
        Close();
    }
}
