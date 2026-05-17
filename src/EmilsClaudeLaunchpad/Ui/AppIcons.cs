namespace EmilsClaudeLaunchpad.Ui;

// Loads the embedded .ico resources. Going through GetManifestResourceStream (instead of
// a file under bin\Resources\) is what lets the icons survive PublishSingleFile — content
// files alongside the .exe don't get extracted at runtime, embedded resources do.
internal static class AppIcons
{
    // tray.ico is the small icon shown in the Windows notification area.
    public static Icon? LoadTray() => LoadByFileName("tray.ico");

    // app.ico is the larger icon used as Form.Icon (Alt+Tab thumbnails, taskbar entries
    // for borderless windows, window list popups). Windows scales it down for those slots.
    public static Icon? LoadApp() => LoadByFileName("app.ico");

    private static Icon? LoadByFileName(string fileName)
    {
        var asm = typeof(AppIcons).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null) return null;
        using var stream = asm.GetManifestResourceStream(resourceName);
        return stream is null ? null : new Icon(stream);
    }
}
