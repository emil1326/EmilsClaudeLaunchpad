namespace EmilsClaudeLaunchpad.Config;

public sealed record AppSettings
{
    public string DefaultShell { get; init; } = "powershell";
    public bool Autostart { get; init; } = false;
}

public sealed record PresetsConfig
{
    public int SchemaVersion { get; init; } = 1;
    public AppSettings Settings { get; init; } = new();
    public IReadOnlyList<SessionPreset> Sessions { get; init; } = Array.Empty<SessionPreset>();
}
