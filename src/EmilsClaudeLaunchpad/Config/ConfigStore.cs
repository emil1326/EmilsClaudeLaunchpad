using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmilsClaudeLaunchpad.Config.Legacy;

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

        // Detect schema: v2 has a "tabs" + "groups" pair; v1 has "sessions".
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var hasV2Shape = root.TryGetProperty("tabs", out _) && root.TryGetProperty("groups", out _);

        if (hasV2Shape)
        {
            var v2 = JsonSerializer.Deserialize<PresetsConfig>(json, JsonOptions);
            return v2 ?? new PresetsConfig();
        }

        // v1: parse + migrate.
        var v1 = JsonSerializer.Deserialize<PresetsConfigV1>(json, JsonOptions);
        var migrated = Migrate(v1 ?? new PresetsConfigV1());
        Save(migrated); // persist the migrated form so we don't re-migrate every load
        return migrated;
    }

    public static void Save(PresetsConfig config)
    {
        Directory.CreateDirectory(GetConfigDir());
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(GetConfigPath(), json);
    }

    private static PresetsConfig Migrate(PresetsConfigV1 v1)
    {
        var tabs = new List<TabPreset>();
        var groups = new List<GroupPreset>();
        var sessionCounter = 1;

        foreach (var session in v1.Sessions ?? Array.Empty<SessionPresetV1>())
        {
            var sessionTabs = session.Kind switch
            {
                SessionKindV1.Single => session.Tab is null ? Array.Empty<TabSpecV1>() : new[] { session.Tab },
                SessionKindV1.Group => (session.Tabs ?? Array.Empty<TabSpecV1>()).ToArray(),
                _ => Array.Empty<TabSpecV1>(),
            };

            var tabIds = new List<string>();
            string? groupColor = null;

            foreach (var v1Tab in sessionTabs)
            {
                var newTab = MigrateTab(v1Tab, session.Id ?? $"session-{sessionCounter}", tabs.Count);
                tabs.Add(newTab);
                tabIds.Add(newTab.Id);
                groupColor ??= newTab.TabColor;
            }

            groups.Add(new GroupPreset
            {
                Id = !string.IsNullOrEmpty(session.Id) ? session.Id : $"group-{sessionCounter}",
                Title = session.Title ?? $"Group {sessionCounter}",
                Color = groupColor,
                TabIds = tabIds,
                Window = session.Window,
            });
            sessionCounter++;
        }

        return new PresetsConfig
        {
            SchemaVersion = 2,
            Settings = v1.Settings,
            Tabs = tabs,
            Groups = groups,
        };
    }

    private static TabPreset MigrateTab(TabSpecV1 v1Tab, string sessionId, int index)
    {
        // Extract --resume <uuid> from claudeArgs; remaining args go into extraClaudeArgs.
        var args = (v1Tab.ClaudeArgs ?? Array.Empty<string>()).ToList();
        var resumedSessionId = string.Empty;
        var extraArgs = new List<string>();
        for (int i = 0; i < args.Count; i++)
        {
            if (args[i] == "--resume" && i + 1 < args.Count)
            {
                resumedSessionId = args[i + 1];
                i++;
                continue;
            }
            extraArgs.Add(args[i]);
        }

        return new TabPreset
        {
            Id = $"tab-{sessionId}-{index}",
            SessionId = resumedSessionId,
            Title = string.IsNullOrEmpty(v1Tab.Title) ? "Untitled" : v1Tab.Title,
            WorkingDir = v1Tab.WorkingDir ?? string.Empty,
            TabColor = v1Tab.TabColor,
            WtProfile = v1Tab.WtProfile,
            Shell = v1Tab.Shell,
            PreCommands = v1Tab.PreCommands ?? Array.Empty<string>(),
            ExtraClaudeArgs = extraArgs,
            InitialPrompt = v1Tab.InitialPrompt,
        };
    }

    private static PresetsConfig BuildSeed()
    {
        var captureTab = new TabPreset
        {
            Id = "tab-example",
            SessionId = "REPLACE-WITH-YOUR-SESSION-UUID",
            Title = "Example",
            WorkingDir = @"C:\path\to\your\project",
            TabColor = "#FF8800",
            InitialPrompt = "/remote-control",
        };
        var workspaceBackend = new TabPreset
        {
            Id = "tab-backend",
            SessionId = "REPLACE-WITH-BACKEND-UUID",
            Title = "Backend",
            WorkingDir = @"C:\path\to\your\backend",
            TabColor = "#FF8800",
            InitialPrompt = "/remote-control",
        };
        var workspaceFrontend = new TabPreset
        {
            Id = "tab-frontend",
            SessionId = "REPLACE-WITH-FRONTEND-UUID",
            Title = "Frontend",
            WorkingDir = @"C:\path\to\your\frontend",
            TabColor = "#0088FF",
            InitialPrompt = "/remote-control",
        };

        return new PresetsConfig
        {
            SchemaVersion = 2,
            Settings = new AppSettings { DefaultShell = "powershell", Autostart = false },
            Tabs = new[] { captureTab, workspaceBackend, workspaceFrontend },
            Groups = new[]
            {
                new GroupPreset
                {
                    Id = "group-example",
                    Title = "Example project",
                    Color = "#FF8800",
                    TabIds = new[] { captureTab.Id },
                },
                new GroupPreset
                {
                    Id = "group-workspace",
                    Title = "Example workspace",
                    Color = "#FF8800",
                    TabIds = new[] { workspaceBackend.Id, workspaceFrontend.Id },
                },
            },
        };
    }
}
