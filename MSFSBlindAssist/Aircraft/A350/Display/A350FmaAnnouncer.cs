namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the FMA call-outs say (pure): from each PFD read (A350Pfd lines), the FMA columns; a column is
/// spoken when its text changes and two reads in a row agree on it, or two of the last few with only blanks between (a mode
/// that flashes leaves every other draw: LVR CLB at the thrust reduction altitude), with its colours ("FMA vertical: SRS, green");
/// a column that stays empty past any flash says so once ("FMA vertical clear"). Each column's first agreed read is its silent
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

    /// <summary>How many reads a column must stay empty to be clear: past a flash, about <see cref="A350FlashingLines.Window"/> at the
    /// monitor's two reads a second.</summary>
    public const int ClearAfterReads = 8;

    /// <summary>The reads looked back over for a flash: its text twice, a blank between.</summary>
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
            bool twice = reads.Count >= 2 && reads[^2] == read;
            if (!_spoken.TryGetValue(c, out var last))
            {
                if (twice) _spoken[c] = read;   // the silent baseline
                continue;
            }
            if (Agreed(reads) is { } mode)
            {
                if (mode != last)
                {
                    said.Add((KeyFor(c), mode));
                    _spoken[c] = mode;
                }
            }
            else if (last.Length > 0 && reads.Count == ClearAfterReads && reads.All(r => r.Length == 0))
            {
                said.Add((KeyFor(c), $"FMA {c} clear"));
                _spoken[c] = "";
            }
        }
        return said;
    }

    /// <summary>The text the latest reads agree on, null with none: the same twice in a row, or twice with only blanks between.</summary>
    private static string? Agreed(List<string> reads)
    {
        string latest = reads[^1];
        if (latest.Length == 0)
        {
            // Still flashing through this blank: the text it last agreed on stands.
            var shown = reads.TakeLast(FlashReads).Where(r => r.Length > 0).ToList();
            return shown.Count >= 2 && shown.All(r => r == shown[0]) ? shown[0] : null;
        }
        if (reads.Count >= 2 && reads[^2] == latest)
            return latest;
        var recent = reads.TakeLast(FlashReads).Where(r => r.Length > 0).ToList();
        return recent.Count >= 2 && recent.All(r => r == latest) ? latest : null;
    }

    public void Reset() { _spoken.Clear(); _reads.Clear(); }
}
