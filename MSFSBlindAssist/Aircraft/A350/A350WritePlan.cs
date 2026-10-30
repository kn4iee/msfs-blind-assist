using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One thing the definition does to carry out a plan.</summary>
public abstract record A350Step;

/// <summary>Send this RPN through the calculator path (unique-ified by the sender).</summary>
public sealed record A350CalcStep(string Rpn) : A350Step;

/// <summary>Wait before the next step (a hold button's hold, a spring switch's hold). <paramref name="OwesRest"/>: the wait
/// holds a control, so the steps after it (its release) are owed and sent at once if the definition goes away mid-wait; a
/// wait between a menu's clicks owes nothing (review 2026-10-29: the runway condition's remaining clicks and PUSH, sent in
/// one burst, landed as one click).</summary>
public sealed record A350DelayStep(int Milliseconds, bool OwesRest = true) : A350Step;

/// <summary>What to do, or why nothing will be done. An empty plan with no refusal means the
/// control is already where the pilot asked; a refusal is an error the pilot must hear.</summary>
public sealed record A350Plan(IReadOnlyList<A350Step> Steps, string? Refusal)
{
    public static readonly A350Plan AlreadyInPosition = new(Array.Empty<A350Step>(), null);
    public static A350Plan Refused(string why) => new(Array.Empty<A350Step>(), why);
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>
/// Turns "set this control to that position" (or press / step it) into the A350's own B: Set event, the way
/// the cockpit's own click code calls it. Pure: no SimConnect, no clock. Ported from the iniBuilds A300's,
/// whose cockpit file has the same shape; measured on the A350 2026-10-26.
/// <list type="bullet">
/// <item>A position already held is not written again.</item>
/// <item>A TOGGLE or COMMAND flips whatever value is passed (GALLEY 0 → 1 for a Set of 1 and of 0 alike), so it
/// is sent only when the current position is KNOWN and differs; an unknown position (or a reading that is none
/// of the control's positions) is refused, because a flip sent the wrong way round inverts the switch.</item>
/// <item>Selectors and knobs are absolute: Set n is position n (NO SMOKING went to 2 for a Set of 2).</item>
/// <item>A hold button is pressed with Set 2 and released with Set 0 after <see cref="HoldMs"/>; a spring
/// switch is held at the picked side for <see cref="SpringHoldMs"/>, then returned to rest.</item>
/// </list>
/// </summary>
public static class A350WritePlan
{
    public const double SameValueTolerance = 0.01;

    /// <summary>A hold button's press, the A300's measured 250 ms until an A350 key needs longer.</summary>
    public const int HoldMs = 250;

    /// <summary>How long a spring switch is held off center.</summary>
    public const int SpringHoldMs = 1000;

    /// <summary>
    /// Buttons a pilot presses and holds, held for as long as the pilot chooses: the first press holds, the next
    /// releases; MSFSBA never times them (the owner's rule, 2026-10-28). Measured live: FIRE TEST raises its fire
    /// warnings and CRC only after 2.5 s held; a 250 ms T.O CONFIG left the memo's TO CONFIG TEST undone; a mask's flow
    /// blinker shows only while PRESS TO TEST is held; ACCU REINFLATE refills only while held. CVR TEST (its tone), CVR
    /// ERASE (2 s on the ground), the RMP PA keys (push to talk) and MECH call (the nose horn while held) by their own
    /// procedures. The other holds the aircraft models (the ECP and ISIS keys, the chimes) are momentary taps.
    /// Each one's Set writes its own held L:var while the press value is 2 (<c>p0 2 == if{ 1 (&gt;L:X) } els{ 0 (&gt;L:X) }</c>,
    /// A350_Interior.behavior.xml, 1.2.7): the cockpit's record of the button held down, by button.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Latched = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["AIRLINER_FIRE_TEST"] = "INI_FIRE_TEST",
        ["AIRLINER_ECAM_TO_CONFIG"] = "PUSH_ECAM_TOCONFIG",
        ["AIRLINER_PARK_BRK_ACCU_REINFLATE"] = "INI_ACCU_REINFLATE",
        ["AIRLINER_OXYTEST_CPT_PANEL"] = "INI_OXYTEST_BLINKER_CPT", ["AIRLINER_OXYTEST_FO_PANEL"] = "INI_OXYTEST_BLINKER_FO",
        ["AIRLINER_OXYTEST_TO_PANEL"] = "INI_OXYTEST_BLINKER_TO", ["AIRLINER_OXYTEST_4TH_PANEL"] = "INI_OXYTEST_BLINKER_4TH",
        ["AIRLINER_OXYTEST_CPT_MASK"] = "INI_OXYTEST_EMER_CPT", ["AIRLINER_OXYTEST_FO_MASK"] = "INI_OXYTEST_EMER_FO",
        ["AIRLINER_OXYTEST_TO_MASK"] = "INI_OXYTEST_EMER_TO", ["AIRLINER_OXYTEST_4TH_MASK"] = "INI_OXYTEST_EMER_4TH",
        ["AIRLINER_CVR_TEST"] = "INI_CVR_TEST",
        ["AIRLINER_CVR_ERASE"] = "INI_CVR_ERASE",
        ["AIRLINER_CALLS_MECH"] = "INI_CALLS_MECH",
        ["AIRLINER_RMP_PA_CALL_1"] = "INI_RMP1_PA_CHANNEL", ["AIRLINER_RMP_PA_CALL_2"] = "INI_RMP2_PA_CHANNEL",
        ["AIRLINER_RMP_PA_CALL_3"] = "INI_RMP3_PA_CHANNEL",
    };

    /// <summary>Whether a hold button is held by the pilot until pressed again (<see cref="Latched"/>).</summary>
    public static bool IsLatched(A350Control control) => control.Kind == A350Kinds.Hold && Latched.ContainsKey(control.Id);

    /// <summary>The L:var a latched button's Set holds at 1 while it is held down (its label reads it, and a press lets
    /// go when it reads held); null for any other control.</summary>
    public static string? HeldVarFor(A350Control control) => IsLatched(control) ? Latched[control.Id] : null;

    /// <summary>A latched button pressed down, and kept down.</summary>
    public static A350Plan ForHoldDown(A350Control control) => Steps(Set(control, control.Press ?? 2));

    /// <summary>A latched button let go.</summary>
    public static A350Plan ForHoldUp(A350Control control) => Steps(Set(control, 0));

    /// <summary>
    /// Switches whose input event only animates the key while the click code flips the state: the app writes what the
    /// click code writes. The HUD declutter keys (1.2.7, LeftSingle): the key's ANIM to 0 and a 2-frame TIMER, after
    /// which the model's update puts the ANIM back to 1, and the declutter itself.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<double, string>> ClickWrites =
        new Dictionary<string, Func<double, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID"] = p => HudDeclutter("LEFT", p),
            ["INSTRUMENT_FCU_HUD_DECLUTTER_RIGHT_IE_ID"] = p => HudDeclutter("RIGHT", p),
        };

    private static string HudDeclutter(string side, double position) =>
        $"0 (>L:INI_FCU_HUD_DECLUTTER_{side}_ANIM) 2 (>L:INI_FCU_HUD_DECLUTTER_{side}_TIMER) {Format(position)} (>L:INI_FCU_HUD_DECLUTTER_{side})";

    /// <summary>A switch whose position is not read yet: the PMDGs' sentence, spoken alone.</summary>
    public const string UnknownPositionRefusal = "Switch not ready, please try again in a moment.";
    public const string NotAPositionRefusal = "not a position of this control";
    public const string NotSettableRefusal = "cannot be set from this panel";

    /// <summary>What a refused row says: <see cref="UnknownPositionRefusal"/> alone, any other refusal after
    /// the row's name ("BAT 1: not a position of this control").</summary>
    public static string SpokenRefusal(string rowName, string refusal) =>
        refusal == UnknownPositionRefusal ? refusal : $"{rowName}: {refusal}";

    /// <summary><paramref name="target"/> and <paramref name="current"/> are STATE values (what the state
    /// variable reads; for a knob, its scaled value).</summary>
    public static A350Plan ForSet(A350Control control, double target, double? current)
    {
        switch (control.Kind)
        {
            case A350Kinds.Toggle:
            case A350Kinds.Command:
            {
                if (control.PositionForState(target) is not double position)
                    return A350Plan.Refused(NotAPositionRefusal);
                if (current is not double now || control.PositionForState(now) is null)
                    return A350Plan.Refused(UnknownPositionRefusal);
                if (Math.Abs(now - target) < SameValueTolerance)
                    return A350Plan.AlreadyInPosition;
                return Steps(Set(control, position));
            }
            case A350Kinds.Selector:
            {
                if (control.PositionForState(target) is not double position)
                    return A350Plan.Refused(NotAPositionRefusal);
                if (current is double now && Math.Abs(now - target) < SameValueTolerance)
                    return A350Plan.AlreadyInPosition;
                if (ClickWrites.TryGetValue(control.Id, out var click))
                    return Steps(new A350CalcStep(click(position)));
                if (A350Levers.StepsFor(control, position) is { } steps)
                    return new A350Plan(steps, null);   // a pick that presses a key (the SD page)
                if (A350Levers.WriteFor(control, position) is { } lever)
                    return Steps(new A350CalcStep(lever));   // a lever, as its own lever code writes it
                return Steps(Set(control, position));
            }
            case A350Kinds.Spring:
            {
                if (control.PositionForState(target) is not double position)
                    return A350Plan.Refused(NotAPositionRefusal);
                double rest = control.Rest ?? 1;
                if (Math.Abs(position - rest) < SameValueTolerance)
                    return Steps(Set(control, rest));
                return Steps(Set(control, position), new A350DelayStep(SpringHoldMs), Set(control, rest));
            }
            case A350Kinds.Knob:
            {
                double scale = control.Scale is > 0 ? control.Scale.Value : 1.0;
                return Steps(Set(control, Math.Clamp(target / scale, 0, 100)));
            }
            default:
                return A350Plan.Refused(NotSettableRefusal);
        }
    }

    public static A350Plan ForPress(A350Control control) => A350Levers.PressStepsFor(control) is { } nudge
        ? new A350Plan(nudge, null)   // a nudge: switches held off center, then returned (A350Levers)
        : control.Kind switch
    {
        A350Kinds.Button => Steps(Set(control, control.Press ?? 1)),
        A350Kinds.Hold => Steps(Set(control, control.Press ?? 2), new A350DelayStep(HoldMs), Set(control, 0)),
        A350Kinds.Toggle or A350Kinds.Selector when A350PanelLayout.IsMomentary(control) => Steps(Set(control, 1)),   // the cockpit returns it
        _ => A350Plan.Refused(NotSettableRefusal),
    };

    public static A350Plan ForStep(A350Control control, bool increase) => control.Kind == A350Kinds.Encoder
        ? Steps(Set(control, increase ? 1 : -1))
        : A350Plan.Refused(NotSettableRefusal);

    /// <summary><c>value (&gt;B:EVENT)</c>, the number in invariant culture.</summary>
    public static A350CalcStep Set(A350Control control, double value) =>
        new($"{Format(value)} (>B:{control.Event})");

    /// <summary>Invariant, up to four decimals, never exponent notation: the MSFS RPN parser
    /// rejects both comma decimals and "1E-05".</summary>
    public static string Format(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static A350Plan Steps(params A350Step[] steps) => new(steps, null);
}
