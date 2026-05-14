// V1 schema types — kept ONLY to read pre-v0.1.1 configs during migration.
// Do not consume these from app code; use the v2 types in the parent namespace.

namespace EmilsClaudeLaunchpad.Config.Legacy;

internal enum SessionKindV1 { Single, Group }

internal sealed record TabSpecV1
{
    public string? Title { get; init; }
    public string? WorkingDir { get; init; }
    public string? TabColor { get; init; }
    public string? WtProfile { get; init; }
    public string? Shell { get; init; }
    public IReadOnlyList<string>? PreCommands { get; init; }
    public IReadOnlyList<string>? ClaudeArgs { get; init; }
    public string? InitialPrompt { get; init; }
}

internal sealed record SessionPresetV1
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public SessionKindV1 Kind { get; init; }
    public TabSpecV1? Tab { get; init; }
    public IReadOnlyList<TabSpecV1>? Tabs { get; init; }
    public string? Window { get; init; }
}

internal sealed record PresetsConfigV1
{
    public int SchemaVersion { get; init; } = 1;
    public AppSettings Settings { get; init; } = new();
    public IReadOnlyList<SessionPresetV1>? Sessions { get; init; }
}
