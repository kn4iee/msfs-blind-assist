using System.Globalization;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace A330PanelSweep;

/// <summary>
/// <c>--dry-run</c>: the plan, the counts and a rough running time, with no simulator. The live run plans from the
/// aircraft's real starting reads; with no sim, each control is assumed to start at its map's first position (a guard
/// closed, a continuous control at its minimum), so a control starting elsewhere will visit a different set of
/// positions on the day.
/// </summary>
internal static class PlanReport
{
    /// <summary>The MCDU brightness knobs: their minimum blanks the MCDU, which the MCDU and display readers need on.</summary>
    public static readonly string[] BlanksTheMcdu = { "A330_CPT_MCDU_BRT", "A330_FO_MCDU_BRT" };

    // Rough seconds per step, for the estimate only: a write that moves within half a second, its snapshots and its gap.
    private const double SetSeconds = 1.5, RepeatSeconds = 2.0, PressSeconds = 1.5, GuardSeconds = 1.2, RestoreSeconds = 1.2;

    /// <summary>Each control at its first position (a guard closed, a continuous control at its minimum).</summary>
    public static Dictionary<string, double?> AssumedStarts(IEnumerable<Control> controls) =>
        controls.ToDictionary(c => c.Key, c => c.Kind switch
        {
            "continuous" => c.Min,
            _ => c.Positions is { Count: > 0 } ps ? ps[0].Value : (double?)null,
        }, StringComparer.Ordinal);

    public static void Print(SweepPlan plan, IReadOnlyList<Control> controls, string pass, TextWriter w)
    {
        w.WriteLine("A330 panel sweep, dry run: nothing is connected to the sim and nothing is written.");
        w.WriteLine("Starts assumed: each control at its map's first position (a guard closed, a continuous control at its minimum).");
        w.WriteLine("The live run plans from the aircraft's own starting reads, and puts every control back by its input event's own starting value.");
        w.WriteLine($"Pass: {pass}");

        var byKey = controls.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string panel = "";
        foreach (var cp in plan.Controls)
        {
            var c = byKey[cp.Key];
            string here = $"{c.Section} / {c.Panel}";
            if (here != panel) { w.WriteLine(); w.WriteLine($"== {here}"); panel = here; }
            w.WriteLine($"  {cp.Key} | {cp.Label} | {c.Kind}");
            if (!cp.IsPlanned) { w.WriteLine($"    skipped: {cp.SkipReason}"); continue; }
            foreach (var s in cp.Steps) w.WriteLine($"    {s.Text}");
            if (c.Proof is { } proof)
                w.WriteLine(proof.Kind is "ewd_memo" or "sd_page"
                    ? $"    proof: {proof.Text ?? proof.Page} ({proof.Kind}): needs the display reader; the owner checks it by ear"
                    : $"    proof: {proof.Read} ({proof.Kind}), read by the runner");
            if (BlanksTheMcdu.Contains(cp.Key, StringComparer.Ordinal))
                w.WriteLine("    NOTE: its minimum blanks this MCDU; the journal puts it back.");
        }

        w.WriteLine();
        PrintCounts(plan, controls, w);
    }

    public static void PrintCounts(SweepPlan plan, IReadOnlyList<Control> controls, TextWriter w)
    {
        var planned = plan.Controls.Where(c => c.IsPlanned).ToList();
        var steps = planned.SelectMany(c => c.Steps).ToList();
        int Count(SweepStepKind k) => steps.Count(s => s.Kind == k);
        int touched = planned.Sum(c => SweepSession.Touched(c).Count);
        double seconds = Count(SweepStepKind.SetPosition) * SetSeconds + Count(SweepStepKind.RepeatPosition) * RepeatSeconds
            + (Count(SweepStepKind.Press) + Count(SweepStepKind.RestorePress) + Count(SweepStepKind.PressEcamPage)) * PressSeconds
            + Count(SweepStepKind.OpenGuard) * GuardSeconds + touched * RestoreSeconds;

        w.WriteLine($"Controls: {plan.Controls.Count}; to sweep: {planned.Count}; skipped: {plan.SkippedCount}.");
        foreach (var g in plan.Controls.Where(c => !c.IsPlanned).GroupBy(c => Reason(c.SkipReason!)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal))
            w.WriteLine($"  skipped, {g.Key}: {g.Count()}");
        w.WriteLine($"Steps: {Count(SweepStepKind.SetPosition)} positions, each written twice; {Count(SweepStepKind.Press)} presses"
            + $" (+{Count(SweepStepKind.RestorePress)} to cycle back); {Count(SweepStepKind.OpenGuard)} guards opened;"
            + $" {Count(SweepStepKind.PressEcamPage)} ECAM page keys; {touched} put-backs from the journal.");
        w.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Rough running time: about {Math.Ceiling(seconds / 60)} minutes, plus about a minute to read the starts; each write that moves nothing waits {SweepTiming.LimitMs / 1000} s more."));
        int display = planned.Count(c => controls.First(x => x.Key == c.Key).Proof?.Kind is "ewd_memo" or "sd_page");
        int variable = planned.Count(c => controls.First(x => x.Key == c.Key).Proof?.Kind is "lamp" or "simvar");
        w.WriteLine($"Proofs: {variable} read by the runner, {display} needing the display reader (checked by ear), {planned.Count - variable - display} with none (read-only at best).");
        w.WriteLine($"Snapshot: {SweepSnapshot.Names(controls, Array.Empty<string>()).Count} INI_* variables before and after each write.");
        w.WriteLine($"System state with each row: {string.Join(", ", SweepSnapshot.SystemStateKeys)}.");
        w.WriteLine($"MCDU brightness (its minimum blanks the MCDU; journaled and put back): {string.Join(", ", BlanksTheMcdu.Where(k => planned.Any(p => p.Key == k)))}.");
        w.WriteLine($"The --pass apu-bleed sweep: {string.Join(", ", SweepSnapshot.ApuBleedPassKeys)}, with {SweepSnapshot.ApuBleedKey} on.");
    }

    /// <summary>A skip reason without its particulars, for the counts ("denied: ..." groups as "denied").</summary>
    private static string Reason(string why)
    {
        if (why.StartsWith("denied", StringComparison.Ordinal)) return "denied";
        if (why.StartsWith("its guard", StringComparison.Ordinal)) return "its guard is denied";
        if (why.StartsWith("no ECAM key", StringComparison.Ordinal)) return "no ECAM key for its proof's page";
        if (why.StartsWith("start ", StringComparison.Ordinal) && why.Contains("matches no position", StringComparison.Ordinal)) return "start matches no position";
        return why;
    }
}
