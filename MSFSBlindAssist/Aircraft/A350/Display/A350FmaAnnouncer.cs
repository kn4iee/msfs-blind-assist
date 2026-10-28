namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the FMA call-outs say (pure): from each PFD read (A350Pfd lines), the FMA columns; a column is
/// spoken when its text changes and two reads in a row agree on it, with its colours ("FMA vertical: SRS, green");
/// a column that empties says so once ("FMA vertical clear"). Each column's first agreed read is its silent
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

    private readonly Dictionary<string, string> _spoken = new();   // what each column last said (its baseline once agreed)
    private Dictionary<string, string>? _pending;    // the last read, waiting for a second agreeing read

    public IReadOnlyList<(string Key, string Line)> Observe(IReadOnlyList<string> pfdLines)
    {
        var read = Columns.ToDictionary(c => c, c => pfdLines.FirstOrDefault(l => l.StartsWith($"FMA {c}: ", StringComparison.Ordinal)) ?? "");
        var said = new List<(string, string)>();
        var previous = _pending;
        _pending = read;
        if (previous == null) return said;
        foreach (var c in Columns)
        {
            if (previous[c] != read[c]) continue;   // not agreed yet
            if (!_spoken.TryGetValue(c, out var last)) { _spoken[c] = read[c]; continue; }   // the silent baseline
            if (read[c] == last) continue;
            said.Add((KeyFor(c), read[c].Length > 0 ? read[c] : $"FMA {c} clear"));
            _spoken[c] = read[c];
        }
        return said;
    }

    public void Reset() { _spoken.Clear(); _pending = null; }
}
