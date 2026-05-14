namespace EmilsClaudeLaunchpad.Config;

public sealed record TabSpec
{
    public required string Title { get; init; }
    public required string WorkingDir { get; init; }

    public string? TabColor { get; init; }
    public string? WtProfile { get; init; }
    public string? Shell { get; init; }
    public IReadOnlyList<string> PreCommands { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ClaudeArgs { get; init; } = Array.Empty<string>();
    public string? InitialPrompt { get; init; }
}
