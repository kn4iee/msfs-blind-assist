namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One of an entry's targets, in the order it is run.</summary>
internal sealed record A350BaroTarget(A350TypedField Field, A350TypedPlan Plan);

/// <summary>A typed QNH planned for its targets together.</summary>
/// <param name="Refusal">Said once instead of anything being sent; then there are no targets.</param>
/// <param name="Targets">What to run, one after the other.</param>
/// <param name="Linked">LINKED INSTR copies between the sides, so the rows a target does not name can move too.</param>
/// <param name="SidesOnStd">The sides read on STD (1, 2), to check after the entry that they still are.</param>
/// <param name="Notice">Said once after the targets, when one was left out for a reason the pilot needs.</param>
internal sealed record A350BaroEntryPlan(string? Refusal, IReadOnlyList<A350BaroTarget> Targets, bool Linked,
    IReadOnlyList<int> SidesOnStd, string? Notice = null);

/// <summary>
/// The Altimeter window's QNH, planned as one entry from one read: every target or none, following the OIS's LINKED INSTR as one
/// knob turned in the cockpit does. Measured in flight on 1.2.8 (2026-11-02), the option's three settings: 0 NONE, each of the three
/// apart; 1 CPT &amp; FO, a side's click, pull, push or unit copies the other side, the ISIS apart; 2 CPT/FO &amp; ISIS, a click on any of the
/// three copies all, a side's pull brings the ISIS and its push puts the ISIS on STD, and the ISIS's click takes both sides off STD.
/// While the ISIS is on its own STD nothing moves its setting. Pure.
/// </summary>
internal static class A350BaroEntry
{
    public const string LinkedVar = "INI_LINKED_INSTRUMENTS";

    /// <summary>LINKED INSTR's CPT/FO &amp; ISIS: the captain's side carries the ISIS, and the ISIS's own click would carry the sides.</summary>
    private const double LinkedWithIsis = 1.5;

    public const string IsisOnStdNotice = "ISIS not set: it is on STD.";

    private static readonly A350TypedField[] Fields = { A350TypedEntry.CaptainQnh, A350TypedEntry.FirstOfficerQnh, A350TypedEntry.IsisQnh };

    /// <param name="typed">The value as the pilot typed it, in the sides' unit.</param>
    /// <param name="read">The cached reads, by the fields' <see cref="A350TypedEntry.Inputs"/>.</param>
    /// <param name="isisOnStd">The ISIS's own STD as its display shows it (it has no variable); null when its display cannot say.</param>
    public static A350BaroEntryPlan Plan(double typed, Func<string, double?> read, bool? isisOnStd = null)
    {
        if (Fields.SelectMany(A350TypedEntry.Inputs).Any(input => read(input) is null))
            return Refused(A350WritePlan.UnknownPositionRefusal);

        double captainUnit = read("XMLVAR_Baro_Selector_HPA_1")!.Value, firstOfficerUnit = read("XMLVAR_Baro_Selector_HPA_2")!.Value;
        if (captainUnit >= 0.5 != firstOfficerUnit >= 0.5)
            return Refused($"QNH not set: the captain's baro shows {UnitName(captainUnit)}, the first officer's {UnitName(firstOfficerUnit)}. Set both sides to one unit first.");

        var plans = Fields.Select(field => new A350BaroTarget(field, A350TypedEntry.Plan(field, typed, read))).ToList();
        if (plans.FirstOrDefault(p => p.Plan.Refusal != null) is { } refused)
            return Refused(refused.Plan.Refusal!);

        double link = read(LinkedVar)!.Value;
        bool linked = link >= 0.5, withIsis = link >= LinkedWithIsis;
        var targets = new List<A350BaroTarget> { plans[0] };
        if (!linked)
            targets.Add(plans[1]);
        if (!withIsis && isisOnStd != true)
            targets.Add(plans[2]);
        return new A350BaroEntryPlan(null, targets, linked,
            new[] { 1, 2 }.Where(side => Math.Abs(read($"XMLVAR_Baro{side}_Mode")!.Value - 3) < 0.5).ToList(),
            isisOnStd == true ? IsisOnStdNotice : null);
    }

    /// <summary>The ISIS's own STD as its display draws it: an STD line or a baro line; null when it shows neither (dark, not yet read).
    /// It has no variable, and while it is on STD its setting holds 1013.25 whatever is written.</summary>
    public static bool? IsisOnStd(IReadOnlyList<string>? lines)
    {
        if (lines == null)
            return null;
        if (lines.Any(l => l.StartsWith("STD,", StringComparison.Ordinal)))
            return true;
        return lines.Any(l => l.StartsWith("Baro:", StringComparison.Ordinal)) ? false : null;
    }

    /// <summary>What a side that left STD without a pull is said as: the setting as the cockpit reads it in the side's unit.</summary>
    public static string LeftStd(int side, double mb, double unit) =>
        $"{(side == 1 ? "Captain" : "First officer")} altimeter left STD, now " + (unit >= 0.5
            ? A350FcuWindows.Hectopascals(mb) + " hectopascals"
            : A350FcuWindows.Inches(mb / AltimeterPhrase.HpaPerInHg));

    private static string UnitName(double unit) => unit >= 0.5 ? "hectopascals" : "inches";

    private static A350BaroEntryPlan Refused(string why) =>
        new(why, Array.Empty<A350BaroTarget>(), false, Array.Empty<int>());
}
