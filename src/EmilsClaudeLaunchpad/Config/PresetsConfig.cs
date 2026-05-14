namespace EmilsClaudeLaunchpad.Config;

public sealed record AppSettings
{
    public string DefaultShell { get; init; } = "powershell";
    public bool Autostart { get; init; } = false;
}

public sealed record PresetsConfig
{
    public int SchemaVersion { get; init; } = 2;
    public AppSettings Settings { get; init; } = new();
    public IReadOnlyList<TabPreset> Tabs { get; init; } = Array.Empty<TabPreset>();
    public IReadOnlyList<GroupPreset> Groups { get; init; } = Array.Empty<GroupPreset>();

    public TabPreset? FindTab(string id) => Tabs.FirstOrDefault(t => t.Id == id);
}
