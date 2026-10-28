namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>A button with no light of its own whose state the cockpit shows elsewhere: the variables it reads (all read
/// before it says anything), and the word for their values.</summary>
internal sealed record A350ButtonState(IReadOnlyList<string> Vars, Func<IReadOnlyList<double>, string> Words)
{
    public A350ButtonState(string var, Func<double, string> word) : this(new[] { var }, v => word(v[0])) { }
}

/// <summary>
/// What a button row reads on its label (the owner's rule, 2026-10-27: a button shows its state where the cockpit shows
/// one). A button with lights reads its lit legends (<see cref="LegendState"/>); the ones below read the state they
/// switch, as the FCU and EFIS windows show it: the speed in knots or Mach, HDG-V/S or TRK-FPA, each side's baro unit
/// and STD or QNH, and each FCU knob's push and pull the managed or selected mode it changes (measured in flight
/// 2026-10-26: `INI_ROLL_MODE` NAV 1, HDG 2, TRK 11; `INI_PITCH_MODE` V/S 8, FPA 11). Momentary buttons with nothing to
/// show (calls, chronos, tests, the ISIS keys) read nothing. Pure.
/// </summary>
internal static class A350ButtonStates
{
    private static readonly A350ButtonState Speed = new("INI_MANAGED_SPEED", v => v >= 0.5 ? "Managed" : "Selected");
    // Managed while NAV is engaged (a heading preset in a NAV turn shows a number, dot 0) or armed: on the ground before
    // takeoff the FCU shows dashes and the managed dot (INI_FCU_HDG_DOT) while no lateral mode is engaged (live 2026-10-28).
    private static readonly A350ButtonState Heading = new(new[] { "INI_ROLL_MODE", "INI_FCU_HDG_DOT" },
        v => Math.Abs(v[0] - A350TypedEntry.NavRollMode) < 0.5 || v[1] >= 0.5 ? "Managed" : "Selected");
    private static readonly A350ButtonState Altitude = new("INI_FCU_ALT_DOT", v => v >= 0.5 ? "Managed" : "Selected");
    private static readonly A350ButtonState Vertical = new("INI_PITCH_MODE",
        v => Math.Abs(v - A350TypedEntry.VsPitchMode) < 0.5 || Math.Abs(v - A350TypedEntry.FpaPitchMode) < 0.5 ? "Engaged" : "Not engaged");

    private static A350ButtonState BaroMode(int side) => new($"XMLVAR_Baro{side}_Mode", v => Math.Abs(v - 3) < 0.5 ? "STD" : "QNH");
    private static A350ButtonState BaroUnit(int side) => new($"XMLVAR_Baro_Selector_HPA_{side}", v => v >= 0.5 ? "Hectopascals" : "Inches");

    public static readonly IReadOnlyDictionary<string, A350ButtonState> ByRow = new Dictionary<string, A350ButtonState>(StringComparer.Ordinal)
    {
        ["A350_FCU_SPD_MACH"] = new("INI_AIRSPEED_IS_MACH", v => v >= 0.5 ? "Mach" : "Knots"),
        ["A350_FCU_TRK_FPA"] = new("INI_TRACK_FPA_STATE", v => v >= 0.5 ? "TRK-FPA" : "HDG-V/S"),
        ["A350_FCU_SPD_KNOB_PUSH"] = Speed, ["A350_FCU_SPD_KNOB_PULL"] = Speed,
        ["A350_FCU_HDG_KNOB_PUSH"] = Heading, ["A350_FCU_HDG_KNOB_PULL"] = Heading,
        ["A350_FCU_ALT_KNOB_PUSH"] = Altitude, ["A350_FCU_ALT_KNOB_PULL"] = Altitude,
        ["A350_FCU_VS_KNOB_PUSH"] = Vertical, ["A350_FCU_VS_KNOB_PULL"] = Vertical,
        ["A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH"] = BaroMode(1), ["A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL"] = BaroMode(1),
        ["A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH"] = BaroMode(2), ["A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL"] = BaroMode(2),
        ["A350_EFIS_QNH_UNIT_SELECTOR_LEFT"] = BaroUnit(1), ["A350_EFIS_QNH_UNIT_SELECTOR_RIGHT"] = BaroUnit(2),
    };

    /// <summary>
    /// A button's lights read together: one light reads On or Off; several read their lit legends ("Available",
    /// "Squib and discharged"), a legend with no word of its own (a master light's lower half) reading On, and Off when
    /// none is lit. A legend is the light's name after the button's name, without "light".
    /// </summary>
    public static string LegendState(string rowName, IReadOnlyList<(string LampName, bool Lit)> lamps)
    {
        if (lamps.Count == 1)
            return lamps[0].Lit ? "On" : "Off";
        var words = lamps.Where(l => l.Lit).Select(l => Legend(rowName, l.LampName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (words.Count == 0)
            return "Off";
        string text = string.Join(" and ", words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Legend(string rowName, string lampName)
    {
        string legend = lampName.StartsWith(rowName, StringComparison.OrdinalIgnoreCase) ? lampName[rowName.Length..] : lampName;
        legend = legend.Trim();
        if (legend.EndsWith("light", StringComparison.OrdinalIgnoreCase))
            legend = legend[..^"light".Length].Trim();
        return legend.Length == 0 || legend.Equals("lower", StringComparison.OrdinalIgnoreCase) ? "on" : legend;
    }
}
