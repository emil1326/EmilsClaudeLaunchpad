using System.Text.Json;

namespace EmilsClaudeLaunchpad.Discovery;

public sealed record ChatRecord
{
    public required string SessionId { get; init; }      // .jsonl filename without extension
    public required string WorkingDir { get; init; }     // cwd extracted from first user entry
    public required string Preview { get; init; }        // truncated first user message text
    public required DateTime LastModified { get; init; } // file modification time
    public required string ProjectFolder { get; init; }  // raw folder name under ~/.claude/projects/

    public string ShortId => SessionId.Length >= 8 ? SessionId[..8] : SessionId;
}

public static class ChatScanner
{
    private const int MaxLinesPerFile = 60;
    private const int PreviewMaxLength = 120;

    public static string GetProjectsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public static IReadOnlyList<ChatRecord> DiscoverAll()
    {
        var root = GetProjectsRoot();
        if (!Directory.Exists(root)) return Array.Empty<ChatRecord>();

        var results = new List<ChatRecord>();
        foreach (var projDir in Directory.EnumerateDirectories(root))
        {
            foreach (var jsonl in Directory.EnumerateFiles(projDir, "*.jsonl"))
            {
                var record = TryParse(jsonl);
                if (record is not null) results.Add(record);
            }
        }
        return results.OrderByDescending(r => r.LastModified).ToList();
    }

    private static ChatRecord? TryParse(string path)
    {
        try
        {
            var sessionId = Path.GetFileNameWithoutExtension(path);
            var mtime = File.GetLastWriteTime(path);
            var projectFolder = Path.GetFileName(Path.GetDirectoryName(path)!);

            // Read all lines once. Chat files are typically a few hundred KB; reading
            // them whole is fine and lets us scan forward for cwd + backward for last message.
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch { return null; }

            string? cwd = ScanForward_Cwd(lines);
            string? preview = ScanBackward_LastMessage(lines);

            cwd ??= DecodeProjectFolder(projectFolder);
            if (string.IsNullOrEmpty(cwd)) return null;

            return new ChatRecord
            {
                SessionId = sessionId,
                WorkingDir = cwd,
                Preview = Truncate(preview ?? "(no preview)", PreviewMaxLength),
                LastModified = mtime,
                ProjectFolder = projectFolder,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ScanForward_Cwd(string[] lines)
    {
        for (int i = 0; i < Math.Min(lines.Length, MaxLinesPerFile); i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("cwd", out var cwdEl))
                {
                    var v = cwdEl.GetString();
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static string? ScanBackward_LastMessage(string[] lines)
    {
        // Walk from the end, find the most recent entry where message.content has actual text.
        // Accepts both 'user' and 'assistant' types — the most recent is what helps identify the chat.
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeEl)) continue;
                var type = typeEl.GetString();
                if (type != "user" && type != "assistant") continue;

                if (!root.TryGetProperty("message", out var msg)) continue;
                if (!msg.TryGetProperty("content", out var content)) continue;

                var text = ExtractText(content);
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
            catch (JsonException) { }
        }
        return null;
    }

    private static string? ExtractText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();
        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                if (block.TryGetProperty("type", out var t) && t.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                    return text.GetString();
            }
        }
        return null;
    }

    // Claude Code project folders encode the cwd as: drive separator ':\' → '--',
    // path separators '\' → '-'. E.g. C:\Users\emili → C--Users-emili. Lossy if a
    // path component contains a literal '-', but good enough as a fallback.
    private static string DecodeProjectFolder(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return string.Empty;
        return folderName.Replace("--", ":\\").Replace("-", "\\");
    }

    private static string Truncate(string s, int max)
    {
        s = s.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return s.Length <= max ? s : string.Concat(s.AsSpan(0, max - 1), "…");
    }
}
