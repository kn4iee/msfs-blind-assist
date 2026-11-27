namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>Parses the sweep's deny list: one control a line, <c>key TAB reason</c>; blank lines and
/// <c>#</c> comment lines are ignored. A line with no reason, or a key twice, is refused: a reason says
/// what the control would do, and a repeated key is a mistake worth seeing. Keys match ignoring case.</summary>
public static class DenyList
{
    public static Dictionary<string, string> Parse(string text)
    {
        var denied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int lineNo = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#')) continue;
            int tab = line.IndexOf('\t');
            string key = tab < 0 ? line.Trim() : line[..tab].Trim();
            string reason = tab < 0 ? "" : line[(tab + 1)..].Trim();
            if (key.Length == 0 || reason.Length == 0)
                throw new FormatException($"deny list line {lineNo}: expected '<key><TAB><reason>', got '{line}'");
            if (!denied.TryAdd(key, reason))
                throw new FormatException($"deny list line {lineNo}: '{key}' is listed twice");
        }
        return denied;
    }
}
