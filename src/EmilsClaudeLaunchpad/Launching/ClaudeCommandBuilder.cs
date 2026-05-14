using EmilsClaudeLaunchpad.Config;

namespace EmilsClaudeLaunchpad.Launching;

public static class ClaudeCommandBuilder
{
    public static string BuildPwshPayload(TabPreset tab)
    {
        var parts = new List<string>();

        if (tab.PreCommands.Count > 0)
            parts.Add(string.Join("; ", tab.PreCommands));

        var claudeParts = new List<string> { "claude" };
        var hasSessionId = !string.IsNullOrEmpty(tab.SessionId);
        if (hasSessionId)
        {
            claudeParts.Add("--resume");
            claudeParts.Add(MaybeQuote(tab.SessionId));
        }
        foreach (var arg in tab.ExtraClaudeArgs)
        {
            // --resume and --continue are mutually exclusive with --resume <SessionId>; drop them
            // so the user doesn't accidentally pass both and have claude refuse to start.
            if (hasSessionId && (arg == "--resume" || arg == "--continue")) continue;
            claudeParts.Add(MaybeQuote(arg));
        }
        if (!string.IsNullOrEmpty(tab.InitialPrompt))
            claudeParts.Add(PwshSingleQuote(tab.InitialPrompt));

        parts.Add(string.Join(' ', claudeParts));

        return string.Join("; ", parts);
    }

    internal static string MaybeQuote(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "''";
        if (arg == "--%") return PwshSingleQuote(arg);
        if (arg.StartsWith('@')) return PwshSingleQuote(arg);
        if (NeedsQuoting(arg)) return PwshSingleQuote(arg);
        return arg;
    }

    internal static string PwshSingleQuote(string value) =>
        "'" + value.Replace("'", "''") + "'";

    private static bool NeedsQuoting(string s)
    {
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) return true;
            if (c is '\'' or '"' or '`' or '$' or ';' or '|' or '&' or '(' or ')' or '<' or '>' or '{' or '}' or '#' or ',') return true;
        }
        return false;
    }
}
