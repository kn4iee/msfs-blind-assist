using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A330;

/// <summary>A continuous control's spoken unit: its steps and the words for each. The control's input
/// <c>Min..Max</c> maps linearly onto its unit's <c>Min..Max</c> (<see cref="A330ControlMap.UnitScale"/>).
/// Pure, so it is pinned without a sim.</summary>
internal static class A330Units
{
    /// <summary>The control's input values from <c>Min</c> to <c>Max</c> by <c>Step</c> (a step of 1 when none is set).</summary>
    public static IReadOnlyList<double> Steps(A330ControlMap.Control c)
    {
        double min = c.Min ?? 0, max = c.Max ?? 100, step = c.Step is > 0 ? c.Step.Value : 1;
        int n = (int)Math.Floor((max - min) / step + 1e-9);
        // by index, so a fractional step never drifts
        return Enumerable.Range(0, n + 1).Select(i => Math.Round(min + i * step, 6)).ToList();
    }

    /// <summary>The unit value an input value stands for.</summary>
    public static double UnitValue(A330ControlMap.Control c, double input)
    {
        var u = c.Unit!;
        double min = c.Min ?? 0, max = c.Max ?? 100;
        return max <= min ? u.Min : u.Min + (input - min) / (max - min) * (u.Max - u.Min);
    }

    /// <summary>What a step is called: the number, then the unit's name, as the FlyByWire A32NX says its zone
    /// temperature ("{t:0.#} degrees") and the Fenix's tilt signs its steps ("-1°", "0°", "+1°"): a plus sign only
    /// where the unit's range has a negative side. Invariant, so no PC culture changes the decimal point (CORE-21).</summary>
    public static string Say(A330ControlMap.Control c, double input)
    {
        var u = c.Unit!;
        double v = Math.Round(UnitValue(c, input), 2);
        string sign = u.Min < 0 && v > 0 ? "+" : "";
        return FormattableString.Invariant($"{sign}{v:0.##} {u.Name}");
    }

    /// <summary>The ValueDescriptions of a control with a unit: each step's input value, in the unit's words.</summary>
    public static Dictionary<double, string> Descriptions(A330ControlMap.Control c)
    {
        var d = new Dictionary<double, string>();
        foreach (double s in Steps(c)) d[s] = Say(c, s);
        return d;
    }

    /// <summary>The step a reading is nearest to (a reading between two steps is the nearest one).</summary>
    public static double NearestStep(A330ControlMap.Control c, double reading)
    {
        double min = c.Min ?? 0, step = c.Step is > 0 ? c.Step.Value : 1;
        var steps = Steps(c);
        int i = (int)Math.Round((reading - min) / step);
        return steps[Math.Clamp(i, 0, steps.Count - 1)];
    }
}
