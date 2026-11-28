using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>One row of <c>sweep-&lt;date&gt;.tsv</c>: one position written (twice), one press, one put-back, or one
/// control not swept and why. Numbers stay numbers here; <see cref="SweepTsv.Format"/> writes them.</summary>
internal sealed record SweepRow
{
    public required string Key { get; init; }
    public string Label { get; init; } = "";
    /// <summary>The position written ("Off (write 1)"), "press", "(restore) start", or empty for a control not swept.</summary>
    public string Position { get; init; } = "";
    /// <summary>What was sent (<see cref="SweepTsv.Describe"/>), empty when nothing was.</summary>
    public string Sent { get; init; } = "";
    public double? ReadBack { get; init; }
    public int? SettleMs { get; init; }
    public string ProofBefore { get; init; } = "";
    public string ProofAfter { get; init; } = "";
    /// <summary>A <see cref="SweepVerdict"/>'s word, or what happened instead ("already there", "restored", "skipped: ...").</summary>
    public string Verdict { get; init; } = "";
    public string Note { get; init; } = "";
    public double? SecondReadBack { get; init; }
    public int? ProofSettleMs { get; init; }
    public string? AircraftValueName { get; init; }
    public double? AircraftValue { get; init; }
    public IReadOnlyList<SideEffect> SideEffects { get; init; } = Array.Empty<SideEffect>();
    public IReadOnlyList<(string Name, double? Value)> SystemState { get; init; } = Array.Empty<(string, double?)>();
}

/// <summary>The sweep's tab-separated results: the brief's columns first, then the runner's own. Every number in
/// the invariant culture (CORE-21), whatever the PC's.</summary>
internal static class SweepTsv
{
    public static readonly string[] Columns =
    {
        "key", "label", "position", "sent", "read back", "settle ms", "proof before", "proof after", "verdict",
        "note", "second read back", "proof settle ms", "aircraft value", "side effects", "system state",
    };

    public static string Header => string.Join('\t', Columns);

    public static string Format(SweepRow r) => string.Join('\t', new[]
    {
        r.Key, r.Label, r.Position, r.Sent, Num(r.ReadBack), Int(r.SettleMs), r.ProofBefore, r.ProofAfter, r.Verdict,
        r.Note, Num(r.SecondReadBack), Int(r.ProofSettleMs),
        r.AircraftValueName == null ? "" : $"{r.AircraftValueName} {Num(r.AircraftValue)}".TrimEnd(),
        string.Join("; ", r.SideEffects.Select(e => $"{e.Name} {Num(e.Before)} -> {Num(e.After)}")),
        string.Join(", ", r.SystemState.Select(s => $"{s.Name} {(s.Value is null ? "?" : Num(s.Value))}")),
    }.Select(Clean));

    /// <summary>A reading as the files print it: invariant, at most six decimals; empty when there is none.</summary>
    public static string Num(double? v) => v?.ToString("0.######", CultureInfo.InvariantCulture) ?? "";

    private static string Int(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    /// <summary>One call of a write as the files print it.</summary>
    public static string Describe(A330Call call) => call switch
    {
        A330InputEventCall ie => $"set {ie.Event} = {Num(ie.Value)}",
        A330PressCall p => $"press {p.Event} ({p.HoldMs.ToString(CultureInfo.InvariantCulture)} ms)",
        A330InputEventPressCall vp => $"press {vp.Event} (set {Num(vp.Press)})",
        A330CalcCall c => $"calc {c.Rpn}",
        _ => call.ToString() ?? "",
    };

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    /// <summary>
    /// The files of one run, never an earlier run's: <c>&lt;stem&gt;-&lt;stamp&gt;.tsv</c> for each stem, or, when any of
    /// them is taken (<paramref name="exists"/>), all with the same next free suffix (<c>-2</c>, <c>-3</c>, ...), so a
    /// sweep and its journal stay a pair. The tool opens them create-new, so even a clash in between is never an overwrite.
    /// </summary>
    public static IReadOnlyList<string> FreeNames(string stamp, Func<string, bool> exists, params string[] stems)
    {
        for (int n = 1; ; n++)
        {
            string suffix = n == 1 ? "" : "-" + n.ToString(CultureInfo.InvariantCulture);
            var names = stems.Select(s => $"{s}-{stamp}{suffix}.tsv").ToList();
            if (!names.Any(exists)) return names;
        }
    }
}
