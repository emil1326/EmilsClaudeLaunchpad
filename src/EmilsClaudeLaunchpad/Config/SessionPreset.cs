namespace EmilsClaudeLaunchpad.Config;

public enum SessionKind
{
    Single,
    Group,
}

public sealed record SessionPreset
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    public required SessionKind Kind { get; init; }

    public TabSpec? Tab { get; init; }
    public IReadOnlyList<TabSpec>? Tabs { get; init; }

    public string? Window { get; init; }

    public IEnumerable<TabSpec> EnumerateTabs() => Kind switch
    {
        SessionKind.Single => Tab is null ? Array.Empty<TabSpec>() : new[] { Tab },
        SessionKind.Group => Tabs ?? Array.Empty<TabSpec>(),
        _ => Array.Empty<TabSpec>(),
    };
}
