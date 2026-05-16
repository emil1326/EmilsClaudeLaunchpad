namespace EmilsClaudeLaunchpad.Config;

public sealed record AppSettings
{
    public string DefaultShell { get; init; } = "powershell";
    public bool AutoCheckUpdatesOnStartup { get; init; } = true;
}

public sealed record PresetsConfig
{
    // Bumped whenever the on-disk shape changes. ConfigStore reads this to decide whether to migrate.
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AppSettings Settings { get; init; } = new();
    public IReadOnlyList<TabPreset> Tabs { get; init; } = Array.Empty<TabPreset>();
    public IReadOnlyList<GroupPreset> Groups { get; init; } = Array.Empty<GroupPreset>();

    public TabPreset? FindTab(string id) => Tabs.FirstOrDefault(t => t.Id == id);
}
