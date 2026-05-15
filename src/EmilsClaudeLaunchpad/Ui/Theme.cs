namespace EmilsClaudeLaunchpad.Ui;

// Shared visual constants + control factories for both the launcher and editor forms.
// Both forms drift if these live locally — keep them here.
internal static class Theme
{
    public static readonly Color Bg = Color.FromArgb(24, 24, 28);
    public static readonly Color Surface = Color.FromArgb(36, 36, 42);
    public static readonly Color SurfaceHover = Color.FromArgb(48, 48, 56);
    public static readonly Color SurfaceSelected = Color.FromArgb(58, 86, 140);
    public static readonly Color Border = Color.FromArgb(52, 52, 60);
    public static readonly Color TextPrimary = Color.FromArgb(232, 232, 235);
    public static readonly Color TextMuted = Color.FromArgb(140, 140, 150);
    public static readonly Color TextDim = Color.FromArgb(95, 95, 105);
    public static readonly Color AccentBlue = Color.FromArgb(72, 124, 200);
    public static readonly Color AccentBlueHover = Color.FromArgb(92, 144, 220);
    public static readonly Color AccentRed = Color.FromArgb(180, 70, 70);
    public static readonly Color StatusInfo = Color.FromArgb(160, 200, 240);
    public static readonly Color StatusError = Color.FromArgb(240, 130, 130);

    // Slightly darker than Bg — used as the background of input controls (textbox, combo)
    // so they read as "type here" against the surrounding form chrome.
    public static readonly Color InputBg = Color.FromArgb(20, 20, 24);

    // Neutral fallback used when a group/tab doesn't define an explicit color.
    public static readonly Color NeutralAccent = Color.FromArgb(120, 120, 130);

    public static Color? TryParseHex(string? hex)
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
        catch { return null; }
    }

    public static Button MakePrimaryButton(string text)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = AccentBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = AccentBlueHover;
        btn.FlatAppearance.MouseDownBackColor = AccentBlue;
        return btn;
    }

    // `bordered: true` draws a 1px border in Theme.Border — useful when the button sits on a
    // Surface-colored card and needs delineation. Default (false) is borderless, fine on Bg.
    public static Button MakeSecondaryButton(string text, bool bordered = false)
    {
        var btn = new Button
        {
            Text = text,
            BackColor = Surface,
            ForeColor = TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F),
            UseVisualStyleBackColor = false,
        };
        btn.FlatAppearance.BorderSize = bordered ? 1 : 0;
        if (bordered) btn.FlatAppearance.BorderColor = Border;
        btn.FlatAppearance.MouseOverBackColor = SurfaceHover;
        btn.FlatAppearance.MouseDownBackColor = Surface;
        return btn;
    }

    public static Button MakeCloseButton()
    {
        var btn = new Button
        {
            Text = "×",
            BackColor = Bg,
            ForeColor = TextMuted,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = AccentRed;
        return btn;
    }
}
