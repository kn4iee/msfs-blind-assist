using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One of the 757's spring-loaded cockpit switches the panels hold for a second (<see cref="Bbs757SpringControls"/>):
/// its L:var, the value it rests on and the values it is held at to either side, where its buttons go, the
/// name the pilot hears for it and the registered key of the readout said after a press.
/// </summary>
/// <param name="Key">The control's own key (the switch itself is registered under <paramref name="LVar"/>).</param>
/// <param name="LVar">The switch's L:var, written to hold and release it and read fresh to verify the release.</param>
/// <param name="Rest">The value the cockpit's own mouse release writes.</param>
/// <param name="Negative">Held to the left, nose down, or descend.</param>
/// <param name="Positive">Held to the right, nose up, or climb.</param>
/// <param name="Section">The section of <paramref name="Panel"/>, used when the map lacks the panel.</param>
/// <param name="Panel">The panel its buttons are on.</param>
/// <param name="Name">The switch as spoken: "Rudder trim" in "Rudder trim switch did not return, check it".</param>
/// <param name="ReadoutKey">The registered variable read fresh after the release and spoken.</param>
/// <param name="NeedsManualMode">True for the manual cabin altitude switch, which works only with Mode Select at MAN.</param>
public sealed record Bbs757SpringControl(
    string Key, string LVar, double Rest, double Negative, double Positive,
    string Section, string Panel, string Name, string ReadoutKey, bool NeedsManualMode);

/// <summary>One panel button holding <paramref name="Control"/> to one side for a second.</summary>
/// <param name="Key">The button's variable key (an Event registration MainForm renders as a button; never "_SET").</param>
/// <param name="Control">The switch it holds.</param>
/// <param name="Positive">True for the right, nose up or climb side.</param>
/// <param name="Label">The button as the screen reader says it, the side first ([VAR-1]): "Left Rudder Trim 1 s".</param>
public sealed record Bbs757SpringButton(string Key, Bbs757SpringControl Control, bool Positive, string Label)
{
    /// <summary>
    /// The registration's event name: dotted and private, so it changes nothing in the sim even if it were ever
    /// sent (the definition claims the key, so it never is).
    /// </summary>
    public string EventName => "BBS757." + Key["BBS757_".Length..];
}

/// <summary>One read-only L:var a press reads fresh (a registered key is all a fresh read can deliver).</summary>
public sealed record Bbs757SpringRead(string Key, string Label);

/// <summary>
/// The 757's spring-loaded switches the panels hold for one second: the rudder trim, the aileron trim and the
/// alternate stab trim (Pedestal, Stabilizer trim) and the manual cabin altitude switch (Overhead,
/// Pressurization). Measured 2026-10-28 (docs/b757/evidence/flight-12.md) and read from BlueBird's code
/// (research package sections 6 and 7):
/// <list type="bullet">
/// <item>Each trim switch rests on 1, 0 is left or nose down, 2 right or nose up. BlueBird trims every frame
/// while one is off 1, and nothing in the aircraft returns it to 1 but the cockpit mouse's own release, so a
/// release that is lost runs the trim to its stop (measured twice: the rudder to −17 and to +16.7 units). The
/// press therefore always releases, reads the switch back <see cref="ReleaseReadBackMs"/> later and writes the
/// release again while it is off rest (<see cref="MaxReleaseWrites"/> in all).</item>
/// <item>The cabin switch rests on 4; its code uses only the side of 4, and the cockpit's click writes 0 (descend)
/// or 7 (climb). Its XML writes 4 back within about a second of an outside write, so the hold is written again
/// at <see cref="HoldRewriteMs"/>. It works only with Mode Select at MAN.</item>
/// </list>
/// Every write is <see cref="Bbs757Rpn.SetLVar"/>'s, invariant whatever the PC's culture; every phrase is
/// invariant too. Pure.
/// </summary>
public static class Bbs757SpringControls
{
    /// <summary>How long a press holds its switch off rest, in milliseconds.</summary>
    public const int HoldMs = 1000;

    /// <summary>When the hold is written again within <see cref="HoldMs"/> (the cabin switch's XML writes 4 back within about a second).</summary>
    public const int HoldRewriteMs = 500;

    /// <summary>How long after the release the readout is read: the readouts lag it by a few tenths (measured).</summary>
    public const int ReadoutDelayMs = 500;

    /// <summary>The most release writes a press makes while the switch reads off rest.</summary>
    public const int MaxReleaseWrites = 3;

    /// <summary>How long after each release write the switch is read back, so a read that has not yet seen the write cannot count as a lost release.</summary>
    public const int ReleaseReadBackMs = 250;

    /// <summary>A stab trim press that moved the trim less than this many indicator units did not move it.</summary>
    public const double StabTrimMovedUnder = 0.05;

    /// <summary>
    /// Said when a stab trim press did not move the trim: the alternate trim also needs hydraulic pressure and
    /// its cutouts NORMAL.
    /// </summary>
    public const string StabTrimDidNotMovePhrase = "Stab trim did not move";

    /// <summary>The pressurization Mode Select knob (the map's control, keyed by its node): 0 AUTO 1, 1 AUTO 2, 2 MAN.</summary>
    public const string ModeSelectNode = "B757_OVHD_MODE_SELECT_SWITCH";

    /// <summary>Mode Select's MAN, the only mode the manual cabin altitude switch works in.</summary>
    public const double ModeSelectMan = 2;

    /// <summary>The refusal of a cabin button outside MAN.</summary>
    public const string NotInManPhrase = "Set the pressurization mode to MAN first";

    /// <summary>BlueBird's rudder trim units, −17 to +17, positive right (the stock RUDDER TRIM PCT does not move).</summary>
    public const string RudderTrimIndicatorVar = "B757_PED_RUDDER_TRIM_INDICATOR";

    /// <summary>The control wheel's rotation, 0 to 3 with 1.5 neutral, larger right: the aileron trim's only readable result.</summary>
    public const string YokeVar = "B757_FC_YOKE";

    /// <summary>The cabin outflow valve, 0 closed to 1 fully open: what MAN drives (the cabin altitude does not move on the ground).</summary>
    public const string CabinValveVar = "BBS_CABIN_VALVE";

    /// <summary>
    /// The trim call-out's own row (BaseAircraftDefinition's <c>MON_ElevatorTrim</c>, the stock
    /// <c>ELEVATOR TRIM POSITION</c> in degrees, which BlueBird's WASM drives), read fresh for the stab trim.
    /// </summary>
    public const string StabTrimReadoutKey = "MON_ElevatorTrim";

    /// <summary>The yoke reads centered within this of its 1.5 neutral.</summary>
    public const double YokeCenteredWithin = 0.01;

    /// <summary>The rudder trim reads centered under this many units.</summary>
    public const double RudderCenteredUnder = 0.05;

    /// <summary>Sim values read back as doubles are compared with this slack, never with ==.</summary>
    private const double ValueTolerance = 0.001;

    private const string Pedestal = "Pedestal";
    private const string Overhead = "Overhead";
    private const string StabilizerTrimPanel = "Stabilizer trim";
    private const string PressurizationPanel = "Pressurization";

    public static readonly Bbs757SpringControl RudderTrim = new(
        "BBS757_RUDDER_TRIM", "B757_PEDESTAL_RUDDER_TRIM_KNOB", 1, 0, 2, Pedestal, StabilizerTrimPanel,
        "Rudder trim", RudderTrimIndicatorVar, NeedsManualMode: false);

    public static readonly Bbs757SpringControl AileronTrim = new(
        "BBS757_AILERON_TRIM", "BBS_AILERON_TRIM_SWITCH", 1, 0, 2, Pedestal, StabilizerTrimPanel,
        "Aileron trim", YokeVar, NeedsManualMode: false);

    /// <summary>The alternate stab trim: it also needs hydraulic pressure and its cutouts NORMAL, or it moves nothing (the readout then says the unchanged trim).</summary>
    public static readonly Bbs757SpringControl AltnStabTrim = new(
        "BBS757_ALTN_STAB_TRIM", "BBS_ELEV_TRIM_ALTERNATE", 1, 0, 2, Pedestal, StabilizerTrimPanel,
        "Stab trim", StabTrimReadoutKey, NeedsManualMode: false);

    public static readonly Bbs757SpringControl CabinAltManual = new(
        "BBS757_CABIN_ALT_MANUAL", "B757_OVHD_CABINALT_SWITCH", 4, 0, 7, Overhead, PressurizationPanel,
        "Cabin altitude", CabinValveVar, NeedsManualMode: true);

    /// <summary>The four switches.</summary>
    public static readonly IReadOnlyList<Bbs757SpringControl> All = new[] { RudderTrim, AileronTrim, AltnStabTrim, CabinAltManual };

    /// <summary>
    /// The eight buttons in the order their panels list them: the six trims go first on Stabilizer trim, left
    /// before right and nose down before nose up; the two cabin buttons go last on Pressurization, climb first.
    /// Each label says its side first ([VAR-1]: where rows share a phrase the discriminator comes first).
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringButton> Buttons = new[]
    {
        new Bbs757SpringButton("BBS757_RUDDER_TRIM_LEFT", RudderTrim, false, "Left Rudder Trim 1 s"),
        new Bbs757SpringButton("BBS757_RUDDER_TRIM_RIGHT", RudderTrim, true, "Right Rudder Trim 1 s"),
        new Bbs757SpringButton("BBS757_AILERON_TRIM_LEFT", AileronTrim, false, "Left Aileron Trim 1 s"),
        new Bbs757SpringButton("BBS757_AILERON_TRIM_RIGHT", AileronTrim, true, "Right Aileron Trim 1 s"),
        new Bbs757SpringButton("BBS757_ALTN_STAB_TRIM_NOSE_DOWN", AltnStabTrim, false, "Nose Down Stab Trim 1 s"),
        new Bbs757SpringButton("BBS757_ALTN_STAB_TRIM_NOSE_UP", AltnStabTrim, true, "Nose Up Stab Trim 1 s"),
        new Bbs757SpringButton("BBS757_CABIN_ALT_MANUAL_CLIMB", CabinAltManual, true, "Climb Cabin 1 s"),
        new Bbs757SpringButton("BBS757_CABIN_ALT_MANUAL_DESCEND", CabinAltManual, false, "Descend Cabin 1 s"),
    };

    /// <summary>
    /// The seven L:vars a press reads fresh, each registered under its own name: the four switches (the release's
    /// read-back) and three readouts. The stab trim's readout and Mode Select are rows the profile registers already.
    /// </summary>
    public static readonly IReadOnlyList<Bbs757SpringRead> Reads = new[]
    {
        new Bbs757SpringRead(RudderTrim.LVar, "Rudder Trim Switch"),
        new Bbs757SpringRead(AileronTrim.LVar, "Aileron Trim Switch"),
        new Bbs757SpringRead(AltnStabTrim.LVar, "Alternate Stab Trim Switch"),
        new Bbs757SpringRead(CabinAltManual.LVar, "Manual Cabin Altitude Switch"),
        new Bbs757SpringRead(RudderTrimIndicatorVar, "Rudder Trim Indicator"),
        new Bbs757SpringRead(YokeVar, "Control Wheel"),
        new Bbs757SpringRead(CabinValveVar, "Outflow Valve"),
    };

    private static readonly Dictionary<string, Bbs757SpringButton> ByButtonKey =
        Buttons.ToDictionary(b => b.Key, StringComparer.Ordinal);

    /// <summary>The button registered under <paramref name="key"/>, or null for any other key.</summary>
    public static Bbs757SpringButton? ForButtonKey(string key) => ByButtonKey.TryGetValue(key, out var b) ? b : null;

    /// <summary>
    /// The two writes of a press: <c>Hold</c> sets the switch to the side asked for (<c>"2
    /// (&gt;L:B757_PEDESTAL_RUDDER_TRIM_KNOB)"</c>), <c>Release</c> back to rest (<c>"1 (&gt;L:…)"</c>). Invariant
    /// (<see cref="Bbs757Rpn.SetLVar"/>): a comma decimal makes the RPN parser reject the string.
    /// </summary>
    public static (string Hold, string Release) Codes(Bbs757SpringControl c, bool positive)
        => (Bbs757Rpn.SetLVar(c.LVar, positive ? c.Positive : c.Negative), Bbs757Rpn.SetLVar(c.LVar, c.Rest));

    /// <summary>True when the switch, read fresh, holds its rest value; an unread or unfinite reading is not known to rest.</summary>
    public static bool AtRest(Bbs757SpringControl c, double? reading)
        => reading is double v && double.IsFinite(v) && Math.Abs(v - c.Rest) < ValueTolerance;

    /// <summary>
    /// Null when a cabin button may hold (Mode Select reads MAN); "Set the pressurization mode to MAN first" for
    /// another mode; "{label} unavailable" when Mode Select could not be read.
    /// </summary>
    public static string? ManualModeRefusal(double? modeSelect, string label)
    {
        if (modeSelect is not double v || !double.IsFinite(v)) return UnavailablePhrase(label);
        return Math.Abs(v - ModeSelectMan) < ValueTolerance ? null : NotInManPhrase;
    }

    /// <summary>A refusal with nothing sent: "Left Rudder Trim 1 s unavailable", as the app's other refusals.</summary>
    public static string UnavailablePhrase(string label) => $"{label} unavailable";

    /// <summary>Said when the switch still reads off rest after the last release: "Rudder trim switch did not return, check it".</summary>
    public static string DidNotReturnPhrase(Bbs757SpringControl c) => $"{c.Name} switch did not return, check it";

    /// <summary>The readout said after a press of <paramref name="c"/>, from its readout read fresh.</summary>
    public static string ReadoutPhrase(Bbs757SpringControl c, double? value)
    {
        if (ReferenceEquals(c, RudderTrim)) return RudderTrimPhrase(value);
        if (ReferenceEquals(c, AileronTrim)) return AileronTrimPhrase(value);
        if (ReferenceEquals(c, AltnStabTrim)) return StabTrimPhrase(value);
        return OutflowValvePhrase(value);
    }

    /// <summary>
    /// "Rudder trim 0.9 units right", "Rudder trim 3.4 units left" (one decimal, a half away from zero,
    /// invariant), "Rudder trim centered" under <see cref="RudderCenteredUnder"/> units, "Rudder trim unavailable"
    /// when nothing finite was read.
    /// </summary>
    public static string RudderTrimPhrase(double? units)
    {
        if (units is not double v || !double.IsFinite(v)) return $"{RudderTrim.Name} unavailable";
        if (Math.Abs(v) < RudderCenteredUnder) return "Rudder trim centered";
        double tenths = Math.Round(Math.Abs(v), 1, MidpointRounding.AwayFromZero);
        return $"Rudder trim {tenths.ToString("0.0", CultureInfo.InvariantCulture)} units {(v > 0 ? "right" : "left")}";
    }

    /// <summary>
    /// The direction only (BlueBird publishes no aileron trim units): "Aileron trim right", "Aileron trim left",
    /// "Aileron trim centered" within <see cref="YokeCenteredWithin"/> of the yoke's 1.5 neutral, "Aileron trim
    /// unavailable" when nothing finite was read.
    /// </summary>
    public static string AileronTrimPhrase(double? yoke)
    {
        if (yoke is not double v || !double.IsFinite(v)) return $"{AileronTrim.Name} unavailable";
        double offset = v - 1.5;
        if (Math.Abs(offset) <= YokeCenteredWithin) return "Aileron trim centered";
        return offset > 0 ? "Aileron trim right" : "Aileron trim left";
    }

    /// <summary>
    /// The trim call-out's own words from the stock degrees: BlueBird's indicator units
    /// (<see cref="Bbs757Readouts.StabTrimUnits"/>) to the tenth, "Trim 5.3" (<see cref="StabTrimUnitsCallout.Phrase"/>),
    /// as the definition's <c>DescribeElevatorTrim</c> says them; "Stab trim unavailable" when nothing finite was read.
    /// </summary>
    public static string StabTrimPhrase(double? degrees)
    {
        if (degrees is not double d || !double.IsFinite(d)) return $"{AltnStabTrim.Name} unavailable";
        return StabTrimUnitsCallout.Phrase(Math.Round(Bbs757Readouts.StabTrimUnits(d), 1));
    }

    /// <summary>
    /// What a stab trim press says from the trim (stock degrees) read fresh before the hold and after it
    /// (CORE-7: the app's own change is not said twice). Null when the trim moved and the trim call-out is
    /// speaking it (it speaks while the stabilizer moves); "Trim 5.3" when it moved and that call-out is silent
    /// (<paramref name="calloutSilent"/>: its Ctrl+M row muted, or trim announcements off); "Stab trim did not
    /// move" when it moved less than <see cref="StabTrimMovedUnder"/> indicator units; "Trim 5.3" when the trim
    /// before is unknown (whether it moved cannot be told, so the read-back is said); "Stab trim unavailable" when
    /// nothing finite was read after.
    /// </summary>
    public static string? StabTrimSpeech(double? beforeDegrees, double? afterDegrees, bool calloutSilent)
    {
        if (afterDegrees is not double after || !double.IsFinite(after)) return StabTrimPhrase(null);
        if (beforeDegrees is not double before || !double.IsFinite(before)) return StabTrimPhrase(after);
        double moved = Math.Abs(Bbs757Readouts.StabTrimUnits(after) - Bbs757Readouts.StabTrimUnits(before));
        if (moved < StabTrimMovedUnder) return StabTrimDidNotMovePhrase;
        return calloutSilent ? StabTrimPhrase(after) : null;
    }

    /// <summary>
    /// "Outflow valve 87 percent open" from the valve's 0 to 1, whole percent (a half away from zero, never past
    /// 0 or 100), invariant; "Outflow valve unavailable" when nothing finite was read.
    /// </summary>
    public static string OutflowValvePhrase(double? open)
    {
        if (open is not double v || !double.IsFinite(v)) return "Outflow valve unavailable";
        double percent = Math.Round(Math.Clamp(v, 0, 1) * 100, MidpointRounding.AwayFromZero);
        return $"Outflow valve {percent.ToString("0", CultureInfo.InvariantCulture)} percent open";
    }
}
