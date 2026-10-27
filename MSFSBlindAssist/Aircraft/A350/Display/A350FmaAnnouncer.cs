namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the FMA call-outs say (pure): from each PFD read (A350Pfd lines), the FMA columns; a column is
/// spoken when its text changes and two reads in a row agree, with its colours ("FMA vertical: SRS, green");
/// a column that empties says so once ("FMA vertical clear"). The first agreed read is a silent baseline.
/// Each line carries its column's Ctrl+M key.
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

    private Dictionary<string, string>? _spoken;     // what each column last said (the baseline once agreed)
    private Dictionary<string, string>? _pending;    // the last read, waiting for a second agreeing read

    public IReadOnlyList<(string Key, string Line)> Observe(IReadOnlyList<string> pfdLines)
    {
        var read = Columns.ToDictionary(c => c, c => pfdLines.FirstOrDefault(l => l.StartsWith($"FMA {c}: ", StringComparison.Ordinal)) ?? "");
        var said = new List<(string, string)>();
        bool agreed = _pending != null && Columns.All(c => _pending[c] == read[c]);
        _pending = read;
        if (!agreed) return said;
        if (_spoken == null) { _spoken = new Dictionary<string, string>(read); return said; }
        foreach (var c in Columns)
        {
            if (read[c] == _spoken[c]) continue;
            said.Add((KeyFor(c), read[c].Length > 0 ? read[c] : $"FMA {c} clear"));
            _spoken[c] = read[c];
        }
        return said;
    }

    public void Reset() { _spoken = null; _pending = null; }
}
