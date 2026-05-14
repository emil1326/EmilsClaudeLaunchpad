using System.Text.RegularExpressions;
using EmilsClaudeLaunchpad.Config;

namespace EmilsClaudeLaunchpad.Launching;

public static class WtCommandBuilder
{
    private static readonly Regex HexColorRegex = new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    public static IReadOnlyList<string> BuildArgumentList(SessionPreset preset, string defaultShell)
    {
        var args = new List<string>();
        var tabs = preset.EnumerateTabs().ToList();
        if (tabs.Count == 0) return args;

        if (!string.IsNullOrEmpty(preset.Window))
        {
            args.Add("--window");
            args.Add(preset.Window);
        }

        for (int i = 0; i < tabs.Count; i++)
        {
            if (i > 0) args.Add(";");
            AppendTabBlock(args, tabs[i], defaultShell);
        }

        return args;
    }

    private static void AppendTabBlock(List<string> args, TabSpec tab, string defaultShell)
    {
        args.Add("new-tab");

        args.Add("--title");
        args.Add(tab.Title);

        if (!string.IsNullOrEmpty(tab.TabColor) && HexColorRegex.IsMatch(tab.TabColor))
        {
            args.Add("--tabColor");
            args.Add(tab.TabColor);
        }

        if (!string.IsNullOrEmpty(tab.WtProfile))
        {
            args.Add("--profile");
            args.Add(tab.WtProfile);
        }

        args.Add("-d");
        args.Add(tab.WorkingDir);

        var shell = string.IsNullOrEmpty(tab.Shell) ? defaultShell : tab.Shell;
        args.Add(shell);
        args.Add("-NoExit");
        args.Add("-Command");
        args.Add(ClaudeCommandBuilder.BuildPwshPayload(tab));
    }
}
