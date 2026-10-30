using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The printer's paper: A350_Printer.js writes each printout into its view's <c>#paper</c> as plain text, wrapped at 78 columns,
/// and it stays there until the next one (live 2026-10-30, 1.2.7: both views "VLivery0N - A350Printer" hold the same text).
/// </summary>
public static class A350Printout
{
    public const string ViewNeedle = "A350Printer";
    public const string Empty = "Nothing printed yet.";

    // "P" marks a read: the eval answers "" when the view cannot be reached, which is no blank paper.
    private const string PaperJs = "(function(){var p=document.getElementById('paper');return p?'P'+p.textContent:''})()";

    /// <summary>The paper's raw text, or null while the view cannot be read.</summary>
    public static async Task<string?> ReadAsync()
    {
        string answer = await CoherentEvalClient.EvalAsync(ViewNeedle, PaperJs);
        return answer.Length == 0 ? null : answer[1..];
    }

    /// <summary>A printout's title: its first line up to the wide gap before its date ("FM ATC FLIGHT PLAN INITIALIZATION DATA").</summary>
    public static string Title(string? raw)
    {
        string first = Text(raw).Split("\r\n")[0];
        int gap = first.IndexOf("  ", StringComparison.Ordinal);
        return (gap > 0 ? first[..gap] : first).Trim();
    }

    /// <summary>The paper as a text box shows it: Windows line ends, no blank tail, and <see cref="Empty"/> for no paper.</summary>
    public static string Text(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Empty;
        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        return string.Join("\r\n", lines);
    }
}
