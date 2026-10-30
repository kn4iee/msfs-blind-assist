namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the FMA call-outs say (pure): from each PFD read (A350Pfd lines), the FMA columns; a column is
/// spoken when its text changes and two reads in a row agree on it, or two of the last few with only parts of it between (a mode
/// that flashes leaves every other draw: LVR CLB under MAN TOGA at the thrust reduction altitude), with its colours ("FMA vertical:
/// SRS, green"). A column left with only a part of what it said, or with nothing ("FMA vertical clear"), says so once it has stayed
/// that way past any flash. Each column's first agreed read is its silent
/// baseline. Columns agree each on its own: one that flickers never holds back another's change (live 2026-10-28,
/// NAV engaging went unsaid while the engagement column flickered). Each line carries its column's Ctrl+M key.
/// </summary>
public sealed class A350FmaAnnouncer
{
    /// <summary>The FMA's columns, left to right (A350Pfd's names).</summary>
    public static readonly IReadOnlyList<string> Columns = new[] { "autothrust", "vertical", "lateral", "approach", "engagement" };

    public static string KeyFor(string column) => "A350_FMA_" + column.ToUpperInvariant();

    public static string LabelFor(string column) => column switch
    {
        "autothrust" => "FMA autothrust mode", "vertical" => "FMA vertical mode", "lateral" => "FMA lateral mode",
        "approach" => "FMA approach capability", _ => "FMA engagement (AP, FD, A/THR)",
    };

    /// <summary>How many reads a column must have lost a part of what it said, or all of it, for that to be news: past a flash, about
    /// <see cref="A350FlashingLines.Window"/> at the monitor's two reads a second.</summary>
    public const int ClearAfterReads = 8;

    /// <summary>The reads looked back over for a flash: its text twice, a part of it between.</summary>
    private const int FlashReads = 4;

    private readonly Dictionary<string, string> _spoken = new();   // what each column last said (its baseline once agreed)
    private readonly Dictionary<string, List<string>> _reads = new();   // each column's latest reads, the newest last

    public IReadOnlyList<(string Key, string Line)> Observe(IReadOnlyList<string> pfdLines)
    {
        var said = new List<(string, string)>();
        foreach (var c in Columns)
        {
            string read = pfdLines.FirstOrDefault(l => l.StartsWith($"FMA {c}: ", StringComparison.Ordinal)) ?? "";
            if (!_reads.TryGetValue(c, out var reads))
                _reads[c] = reads = new List<string>();
            reads.Add(read);
            if (reads.Count > ClearAfterReads)
                reads.RemoveAt(0);
            if (!_spoken.TryGetValue(c, out var last))
            {
                if (reads.Count >= 2 && reads[^2] == read) _spoken[c] = read;   // the silent baseline
                continue;
            }
            if (read == last)
                continue;
            bool news = IsPartOf(read, last)
                ? reads.Count == ClearAfterReads && reads.All(r => r == read)
                : Agreed(reads);
            if (news)
            {
                said.Add((KeyFor(c), read.Length > 0 ? read : $"FMA {c} clear"));
                _spoken[c] = read;
            }
        }
        return said;
    }

    /// <summary>The latest read twice in a row, or twice in the last few with only parts of it between.</summary>
    private static bool Agreed(List<string> reads)
    {
        string latest = reads[^1];
        var recent = reads.TakeLast(FlashReads).SkipWhile(r => r != latest).ToList();
        return recent.Count >= 2 && recent.All(r => r == latest || IsPartOf(r, latest));
    }

    /// <summary>Holds fewer modes than the other line and none it does not: what a part that flashes leaves behind, down to nothing.</summary>
    internal static bool IsPartOf(string part, string whole)
    {
        var modes = Modes(part);
        var all = Modes(whole);
        return modes.Count < all.Count && modes.All(all.Contains);
    }

    /// <summary>A column's modes, without its name and colours: "FMA autothrust: MAN; TOGA, white" holds MAN and TOGA.</summary>
    private static IReadOnlyList<string> Modes(string line)
    {
        int name = line.IndexOf(": ", StringComparison.Ordinal);
        if (name < 0)
            return Array.Empty<string>();
        return line[(name + 2)..].Split("; ").Select(mode =>
        {
            int colour = mode.LastIndexOf(", ", StringComparison.Ordinal);
            return colour < 0 ? mode : mode[..colour];
        }).ToList();
    }

    public void Reset() { _spoken.Clear(); _reads.Clear(); }
}
