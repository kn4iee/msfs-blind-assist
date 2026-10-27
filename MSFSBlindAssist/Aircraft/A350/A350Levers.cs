using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>A pedestal lever: its row's control, its positions' words, and the calculator string that moves it to a
/// position (null where its own input event does).</summary>
/// <param name="Words">Position → the word its row reads, the other aircraft's words (A380, A320).</param>
/// <param name="Write">(position, state value) → the string the cockpit's own lever code would send.</param>
/// <param name="Nudge">A press that holds a spring switch at <c>Side</c> for <see cref="A350Levers.NudgeMs"/>, then
/// returns it to <c>Back</c>.</param>
/// <param name="Steps">A pick that takes more than one string (a key held, then let go), instead of
/// <paramref name="Write"/>.</param>
internal sealed record A350Lever(A350Control Control, IReadOnlyDictionary<double, string> Words, Func<double, double, string>? Write,
    (double Side, double Back)? Nudge = null, Func<double, IReadOnlyList<A350Step>>? Steps = null);

/// <summary>
/// The pedestal's levers, hand-written because they bypass their input events (the map lists them as "none"). Each is
/// written the way the cockpit's own lever code writes it, measured live on the A350-900 (package 1.2.7):
/// <list type="bullet">
/// <item>Flaps: the input event takes the lever position in 16k steps of 4096 (`INI.FLAPS_SET`); the position is
/// <c>INI_FLAPS_HANDLE_INDEX</c>, 0 to 4 (CONF 0, 1, 2, 3, FULL; the aircraft picks 1+F itself).</item>
/// <item>Thrust levers: the drag code writes the lever angle into <c>INI_TLAn_REQUEST</c>, which the aircraft takes and
/// resets to -1; the angle is <c>INI_AUTOTHRUST_TLA:n</c>. Detents from the drag code: reverse -20 to 0, CLB 25,
/// FLX/MCT 35, TOGA 45; reverse idle -6, where the commanded N1 changes slope.</item>
/// <item>Speed brake: the drag code fires <c>'INI.SPOILERS_SET'</c> (0 to 16384) and <c>'INI.SPOILERS_ARM_ON'</c> /
/// <c>_OFF</c>; leaving ARM disarms, arming retracts. The handle is <c>INI_SPOILERS_HANDLE_POSITION</c>, 0 to 1.</item>
/// <item>Parking brake: its input event fires the toggle <c>K:PARKING_BRAKES</c>, so it is a command on the stock
/// <c>BRAKE PARKING POSITION</c>, sent only when that differs.</item>
/// <item>Rudder trim: the switch trims while held off center (about 1.6 degrees a second); as on the A380 and the
/// Fenix it is two nudge buttons (left 2, right 0, rest 1), never a combo left trimming. Reset fires
/// <c>'INI.RUDDER_TRIM_RESET'</c>, a button.</item>
/// </list>
/// "All thrust levers", "Ground spoilers" and "System display page" have no input event of their own: they are the
/// A380's rows. The page is <c>INI_ECAM_ACTIVE_PAGE</c>; a pick presses that page's ECP key through its input event
/// (a hold, as the key row presses it), live 2026-10-27. Pure.
/// </summary>
internal static class A350Levers
{
    public const int FlapStep = 4096;

    /// <summary>How long a nudge holds the rudder trim switch: about 0.4 degrees.</summary>
    public const int NudgeMs = 300;
    public const int KeyEventFull = 16384;

    /// <summary>The SD pages the ECP has a key for: the page value, the A380's word, the key's input event; declared before
    /// <see cref="Extras"/>, which reads it as it is built.</summary>
    private static readonly (double Page, string Word, string Key)[] SdPages =
    {
        (1, "Engine", "ENG"), (2, "Hyd", "HYD"), (3, "Elec DC", "ELDC"), (4, "Elec AC", "ELAC"), (5, "Bleed", "BLEED"),
        (6, "Cond", "COND"), (7, "Press", "PRESS"), (8, "Fuel", "FUEL"), (9, "APU", "APU"), (10, "F/Ctl", "FCTL"),
        (11, "Door", "DOOR"), (12, "Wheel", "WHEEL"), (14, "Status", "STS"), (16, "Video", "VIDEO"),
    };

    private static readonly (double Tla, string Word)[] Detents =
    {
        (-20, "Reverse"), (-6, "Reverse idle"), (0, "Idle"), (25, "Climb"), (35, "Flex/MCT"), (45, "TOGA"),
    };

    private static readonly (double Handle, string Word)[] SpeedBrakeSteps =
    {
        (0, "Retracted"), (0.25, "Quarter"), (0.5, "Half"), (0.75, "Three quarters"), (1, "Full"),
    };

    private static readonly Dictionary<string, A350Lever> Replacements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AIRLINER_FLAPS"] = Lever("AIRLINER_FLAPS", "Flaps", A350Kinds.Selector, "L:INI_FLAPS_HANDLE_INDEX",
            new[] { (0.0, "Up"), (1, "1"), (2, "2"), (3, "3"), (4, "Full") },
            (position, _) => $"{Format(position * FlapStep)} (>B:AIRLINER_Flaps_Set)"),
        ["ENGINE_THROTTLE_1"] = ThrustLever(1),
        ["ENGINE_THROTTLE_2"] = ThrustLever(2),
        ["AIRLINER_FCTL_SPD_BRK_LEVER"] = Lever("AIRLINER_FCTL_SPD_BRK_LEVER", "Speed brake", A350Kinds.Selector,
            "L:INI_SPOILERS_HANDLE_POSITION", SpeedBrakeSteps,
            (_, handle) => $"'INI.SPOILERS_ARM_OFF' (>F:KeyEvent) {Format(handle * KeyEventFull)} 'INI.SPOILERS_SET' (>F:KeyEvent)"),
        ["AIRLINER_PARK_BRK_PARKING_BRAKE"] = Lever("AIRLINER_PARK_BRK_PARKING_BRAKE", "Parking brake", A350Kinds.Command,
            "A:BRAKE PARKING POSITION", new[] { (0.0, "Off"), (1, "On") }, null, "Bool"),
        // The running switch is not a row: its nudges are (Extras).
        ["AIRLINER_RUDDER_TRIM"] = Lever("AIRLINER_RUDDER_TRIM", "Rudder trim", A350Kinds.None, null,
            Array.Empty<(double, string)>(), null),
        ["AIRLINER_RUDDER_TRIM_RESET"] = Lever("AIRLINER_RUDDER_TRIM_RESET", "Rudder trim reset", A350Kinds.Button, null,
            Array.Empty<(double, string)>(), null),
    };

    /// <summary>Rows with no input event of their own, placed beside the lever they belong to.</summary>
    private static readonly (string Anchor, bool Before, A350Lever Lever)[] Extras =
    {
        ("ENGINE_THROTTLE_1", true, Lever("MSFSBA_THRUST_LEVERS_ALL", "All thrust levers", A350Kinds.Selector,
            "L:INI_AUTOTHRUST_TLA:1", Detents, (_, tla) => $"{Format(tla)} (>L:INI_TLA1_REQUEST) {Format(tla)} (>L:INI_TLA2_REQUEST)",
            key: "A350_THRUST_LEVERS_ALL")),
        ("AIRLINER_FCTL_SPD_BRK_LEVER", false, Lever("MSFSBA_SPOILERS_ARM", "Ground spoilers", A350Kinds.Selector,
            "L:INI_SPOILERS_ARMED", new[] { (0.0, "Disarmed"), (1, "Armed") },
            (position, _) => position >= 0.5
                ? $"0 'INI.SPOILERS_SET' (>F:KeyEvent) 'INI.SPOILERS_ARM_ON' (>F:KeyEvent)"
                : "'INI.SPOILERS_ARM_OFF' (>F:KeyEvent)",
            key: "A350_SPOILERS_ARM")),
        ("AIRLINER_ECAM_SD_KNOB_PUSH", true, SystemDisplayPage()),
        ("AIRLINER_RUDDER_TRIM", false, Nudge("LEFT", "left", 2)),
        ("AIRLINER_RUDDER_TRIM", false, Nudge("RIGHT", "right", 0)),
    };

    private static readonly Dictionary<string, A350Lever> ById =
        Replacements.Values.Concat(Extras.Select(e => e.Lever)).ToDictionary(l => l.Control.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>The map's controls with each lever in its place: a lever replaces its map entry (keeping the map's key,
    /// area, panel and event), and a lever with no input event sits beside its anchor.</summary>
    public static IEnumerable<A350Control> Merge(IEnumerable<A350Control> controls)
    {
        foreach (var control in controls)
        {
            foreach (var extra in Extras.Where(e => e.Before && Same(e.Anchor, control.Id)))
                yield return Placed(extra.Lever.Control, control);
            if (Replacements.TryGetValue(control.Id, out var lever))
            {
                var placed = lever.Control;
                placed.Key = control.Key;
                placed.Area = control.Area;
                placed.Panel = control.Panel;
                placed.Event = control.Event;
                yield return placed;
            }
            else
            {
                yield return control;
            }
            foreach (var extra in Extras.Where(e => !e.Before && Same(e.Anchor, control.Id)))
                yield return Placed(extra.Lever.Control, control);
        }
    }

    /// <summary>A lever's position words, or null for any other control.</summary>
    public static IReadOnlyDictionary<double, string>? WordsFor(string controlId) =>
        ById.TryGetValue(controlId, out var lever) && lever.Words.Count > 0 ? lever.Words : null;

    /// <summary>The steps a pick takes where one string does not do it, or null.</summary>
    public static IReadOnlyList<A350Step>? StepsFor(A350Control control, double position) =>
        ById.TryGetValue(control.Id, out var lever) && lever.Steps is { } steps ? steps(position) : null;

    /// <summary>The nudge a press makes, or null for any other control.</summary>
    public static (double Side, double Back)? NudgeFor(A350Control control) =>
        ById.TryGetValue(control.Id, out var lever) ? lever.Nudge : null;

    /// <summary>The string that moves a lever to <paramref name="position"/>, or null where its own Set does.</summary>
    public static string? WriteFor(A350Control control, double position) =>
        ById.TryGetValue(control.Id, out var lever) && lever.Write is { } write
            ? write(position, control.StateForPosition(position))
            : null;

    private static A350Control Placed(A350Control control, A350Control anchor)
    {
        control.Area = anchor.Area;
        control.Panel = anchor.Panel;
        return control;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static A350Lever SystemDisplayPage()
    {
        var lever = Lever("MSFSBA_SD_PAGE", "System display page", A350Kinds.Selector, "L:INI_ECAM_ACTIVE_PAGE",
            SdPages.Select(p => (p.Page, p.Word)).ToList(), null, key: "A350_SD_PAGE");
        return lever with
        {
            Steps = position =>
            {
                string key = $"AIRLINER_ECAM_{SdPages[(int)position].Key}_Set";
                return new A350Step[]
                {
                    new A350CalcStep($"2 (>B:{key})"), new A350DelayStep(A350WritePlan.HoldMs), new A350CalcStep($"0 (>B:{key})"),
                };
            },
        };
    }

    private static A350Lever Nudge(string side, string word, double position)
    {
        var lever = Lever($"MSFSBA_RUDDER_TRIM_{side}", $"Rudder trim {word} (nudge)", A350Kinds.Button, null,
            Array.Empty<(double, string)>(), null, key: $"A350_RUDDER_TRIM_{side}");
        lever.Control.Event = "AIRLINER_RUDDER_TRIM_Set";
        return lever with { Nudge = (position, 1) };
    }

    private static A350Lever ThrustLever(int n) =>
        Lever($"ENGINE_THROTTLE_{n}", $"Thrust lever {n}", A350Kinds.Selector, $"L:INI_AUTOTHRUST_TLA:{n}", Detents,
            (_, tla) => $"{Format(tla)} (>L:INI_TLA{n}_REQUEST)");

    /// <summary>Positions are numbered 0, 1, 2…; each reads <paramref name="positions"/>' state value.</summary>
    private static A350Lever Lever(string id, string title, string kind, string? stateVar,
        IReadOnlyList<(double State, string Word)> positions, Func<double, double, string>? write,
        string? unit = null, string? key = null)
    {
        var control = new A350Control
        {
            Id = id,
            Key = key ?? string.Empty,
            Title = title,
            Kind = kind,
            StateVar = stateVar,
            StateUnit = unit,
            Press = kind == A350Kinds.Button ? 1 : null,
        };
        var words = new Dictionary<double, string>();
        var values = new Dictionary<string, string>();
        bool numbered = true;
        for (int i = 0; i < positions.Count; i++)
        {
            control.Positions[Format(i)] = positions[i].Word;
            values[Format(i)] = Format(positions[i].State);
            words[i] = positions[i].Word;
            numbered &= positions[i].State == i;
        }
        if (!numbered)
            control.Values = values;
        return new A350Lever(control, words, write);
    }

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
