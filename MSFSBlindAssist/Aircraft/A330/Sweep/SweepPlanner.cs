using System.Globalization;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>
/// Plans the live sweep of the A330's cockpit controls. Pure: the map's controls, each control's
/// starting read, the deny list and the ECAM page keys go in; a plan comes out. It touches no simulator,
/// clock or file. The runner executes the plan and measures; nothing here is measured.
///
/// A selector visits every other position in the map's order and returns to where it started; a button
/// that reports a state is pressed once; a continuous control goes to its minimum, middle and maximum
/// and back. Each position (and each continuous point) is written twice in a row, to find a toggle. A guard row is never swept on its own: its covered switch opens it first and closes it
/// last (unless it found it open). A control whose start was not read is skipped, never guessed.
/// </summary>
internal static class SweepPlanner
{
    /// <param name="controls">The map's controls, in the map's order.</param>
    /// <param name="starts">Each control's read value at the start, by key (missing or null: not read).</param>
    /// <param name="denied">The deny list: key to reason.</param>
    /// <param name="ecamPageKeys">An ECAM page's word ("FUEL") to the key of the button that shows it.</param>
    public static SweepPlan Plan(IReadOnlyList<Control> controls, IReadOnlyDictionary<string, double?> starts,
        IReadOnlyDictionary<string, string> denied, IReadOnlyDictionary<string, string> ecamPageKeys)
    {
        var byKey = controls.ToDictionary(c => c.Key);
        var guardKeys = new HashSet<string>(controls.Where(c => c.Guard != null).Select(c => c.Guard!));
        var plans = new List<ControlPlan>();
        foreach (var c in controls)
        {
            string? skip = null;
            var steps = new List<SweepStep>();
            if (denied.TryGetValue(c.Key, out var why)) skip = "denied: " + why;
            else if (guardKeys.Contains(c.Key)) skip = "guard row: opened only with its switch";
            else skip = Build(c, byKey, starts, denied, ecamPageKeys, steps);
            plans.Add(new ControlPlan(c.Key, c.Label, skip == null ? steps : Array.Empty<SweepStep>(), skip));
        }
        return new SweepPlan(plans);
    }

    private static string? Build(Control c, Dictionary<string, Control> byKey, IReadOnlyDictionary<string, double?> starts,
        IReadOnlyDictionary<string, string> denied, IReadOnlyDictionary<string, string> ecamPageKeys, List<SweepStep> steps)
    {
        var body = new List<SweepStep>();
        string? skip = c.Kind switch
        {
            "select" => SelectorSteps(c, starts, body),
            "button" => ButtonSteps(c, body),
            "continuous" => ContinuousSteps(c, starts, body),
            "status" => "read-only row",
            _ => $"kind '{c.Kind}' is not swept",
        };
        if (skip != null) return skip;

        if (c.Proof is { Kind: "sd_page" } proof)
        {
            string page = proof.Page ?? "";
            if (!ecamPageKeys.TryGetValue(page, out var pageKey)) return $"no ECAM key for page '{page}'";
            steps.Add(new SweepStep(SweepStepKind.PressEcamPage, pageKey, null, null, $"press ECAM page key {pageKey} ({page})"));
        }

        if (c.Guard != null)
        {
            if (denied.TryGetValue(c.Guard, out var gwhy)) return $"its guard {c.Guard} is denied: {gwhy}";
            if (!byKey.TryGetValue(c.Guard, out var guard)) return $"guard {c.Guard} is not in the map";
            var open = guard.Positions?.FirstOrDefault(p => p.Label == "Open");
            var closed = guard.Positions?.FirstOrDefault(p => p.Label == "Closed");
            if (open == null || closed == null) return $"guard {c.Guard} has no Open and Closed positions";
            if (!starts.TryGetValue(c.Guard, out var gs) || gs == null) return "guard start not read";
            if (!Same(gs.Value, open.Value))
            {
                steps.Add(new SweepStep(SweepStepKind.OpenGuard, c.Guard, open.Input, open.Value, $"open guard {c.Guard}"));
                steps.AddRange(body);
                steps.Add(new SweepStep(SweepStepKind.CloseGuard, c.Guard, closed.Input, closed.Value, $"close guard {c.Guard}"));
                return null;
            }
        }
        steps.AddRange(body);
        return null;
    }

    private static string? SelectorSteps(Control c, IReadOnlyDictionary<string, double?> starts, List<SweepStep> steps)
    {
        if (c.Positions is not { Count: >= 2 } ps) return "fewer than two positions";
        if (!starts.TryGetValue(c.Key, out var start) || start == null) return "start not read";
        var home = ps.FirstOrDefault(p => Same(p.Value, start.Value));
        if (home == null) return $"start {Fmt(start.Value)} matches no position";
        foreach (var p in ps.Where(p => p != home))
            AddTwice(steps, c.Key, p.Input, p.Value, $"set {c.Key} to {p.Label} (write {Fmt(p.Input)})");
        steps.Add(new SweepStep(SweepStepKind.ReturnToStart, c.Key, home.Input, home.Value, $"return {c.Key} to {home.Label} (its input event's own starting value)"));
        return null;
    }

    /// <summary>A position and its second write: the same input again, so a toggling input event shows itself.</summary>
    private static void AddTwice(List<SweepStep> steps, string key, double input, double? expected, string text)
    {
        steps.Add(new SweepStep(SweepStepKind.SetPosition, key, input, expected, text));
        steps.Add(new SweepStep(SweepStepKind.RepeatPosition, key, input, expected, text + ", again"));
    }

    private static string? ButtonSteps(Control c, List<SweepStep> steps)
    {
        if (string.IsNullOrEmpty(c.State)) return "no readable state";
        steps.Add(new SweepStep(SweepStepKind.Press, c.Key, null, null, $"press {c.Key}"));
        for (int i = 1; i < (c.PressesToCycle ?? 1); i++)
            steps.Add(new SweepStep(SweepStepKind.RestorePress, c.Key, null, null, $"press {c.Key} again to cycle back ({i} of {c.PressesToCycle - 1})"));
        return null;
    }

    private static string? ContinuousSteps(Control c, IReadOnlyDictionary<string, double?> starts, List<SweepStep> steps)
    {
        if (c.Min == null || c.Max == null) return "no range";
        if (!starts.TryGetValue(c.Key, out var start) || start == null) return "start not read";
        double mid = (c.Min.Value + c.Max.Value) / 2;
        foreach (var (name, v) in new[] { ("minimum", c.Min.Value), ("middle", mid), ("maximum", c.Max.Value) })
            AddTwice(steps, c.Key, v, null, $"set {c.Key} to its {name} (write {Fmt(v)})");
        steps.Add(new SweepStep(SweepStepKind.ReturnToStart, c.Key, start.Value, null, $"return {c.Key} to {Fmt(start.Value)} (its input event's own starting value)"));
        return null;
    }

    private static bool Same(double a, double b) => Math.Abs(a - b) < 1e-6;

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
