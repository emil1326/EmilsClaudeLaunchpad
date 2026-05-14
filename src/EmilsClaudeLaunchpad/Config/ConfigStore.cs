using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmilsClaudeLaunchpad.Config;

public static class ConfigStore
{
    private const string AppFolderName = "EmilsClaudeLaunchpad";
    private const string FileName = "presets.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string GetConfigDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);

    public static string GetConfigPath() => Path.Combine(GetConfigDir(), FileName);

    public static PresetsConfig Load()
    {
        var path = GetConfigPath();
        if (!File.Exists(path))
        {
            var seed = BuildSeed();
            Save(seed);
            return seed;
        }

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<PresetsConfig>(json, JsonOptions);
        return config ?? new PresetsConfig();
    }

    public static void Save(PresetsConfig config)
    {
        Directory.CreateDirectory(GetConfigDir());
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(GetConfigPath(), json);
    }

    private static PresetsConfig BuildSeed() => new()
    {
        SchemaVersion = 1,
        Settings = new AppSettings { DefaultShell = "powershell", Autostart = false },
        Sessions = new[]
        {
            new SessionPreset
            {
                Id = "example-single",
                Title = "Example (resume + remote_control)",
                Kind = SessionKind.Single,
                Tab = new TabSpec
                {
                    Title = "Example",
                    TabColor = "#FF8800",
                    WorkingDir = @"C:\path\to\your\project",
                    ClaudeArgs = new[] { "--resume", "REPLACE-WITH-YOUR-SESSION-UUID" },
                    InitialPrompt = "/remote_control",
                },
            },
            new SessionPreset
            {
                Id = "example-group",
                Title = "Example group (multi-tab)",
                Kind = SessionKind.Group,
                Tabs = new[]
                {
                    new TabSpec
                    {
                        Title = "Backend",
                        TabColor = "#FF8800",
                        WorkingDir = @"C:\path\to\your\backend",
                        ClaudeArgs = new[] { "--resume", "REPLACE-WITH-BACKEND-UUID" },
                        InitialPrompt = "/remote_control",
                    },
                    new TabSpec
                    {
                        Title = "Frontend",
                        TabColor = "#0088FF",
                        WorkingDir = @"C:\path\to\your\frontend",
                        ClaudeArgs = new[] { "--resume", "REPLACE-WITH-FRONTEND-UUID" },
                        InitialPrompt = "/remote_control",
                    },
                },
            },
        },
    };
}
