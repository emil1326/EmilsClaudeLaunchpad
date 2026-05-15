namespace EmilsClaudeLaunchpad.Config;

public sealed record GroupPreset
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Color { get; init; }
    public IReadOnlyList<string> TabIds { get; init; } = Array.Empty<string>();
    public string? Window { get; init; }
}
