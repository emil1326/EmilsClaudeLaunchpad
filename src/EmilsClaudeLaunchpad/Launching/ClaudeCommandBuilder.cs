using EmilsClaudeLaunchpad.Config;

namespace EmilsClaudeLaunchpad.Launching;

public static class ClaudeCommandBuilder
{
    public static string BuildPwshPayload(TabSpec tab)
    {
        var parts = new List<string>();

        if (tab.PreCommands.Count > 0)
            parts.Add(string.Join("; ", tab.PreCommands));

        var claudeParts = new List<string> { "claude" };
        foreach (var arg in tab.ClaudeArgs)
            claudeParts.Add(MaybeQuote(arg));
        if (!string.IsNullOrEmpty(tab.InitialPrompt))
            claudeParts.Add(PwshSingleQuote(tab.InitialPrompt));

        parts.Add(string.Join(' ', claudeParts));

        return string.Join("; ", parts);
    }

    internal static string MaybeQuote(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "''";
        if (arg == "--%") return PwshSingleQuote(arg); // stop-parsing token — must not reach the parser raw
        if (arg.StartsWith('@')) return PwshSingleQuote(arg); // splatting / array-literal trigger
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
