using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>A pedestal lever: its row's control, its positions' words, and the calculator string that moves it to a
/// position (null where its own input event does).</summary>
/// <param name="Words">Position → the word its row reads, the other aircraft's words (A380, A320).</param>
/// <param name="Write">(position, state value) → the string the cockpit's own lever code would send.</param>
/// <param name="Press">A press that takes several strings: a nudge holds spring switches off center for
/// <see cref="A350Levers.NudgeMs"/>, then returns them.</param>
/// <param name="Steps">A pick that takes more than one string (a key held, then let go), instead of
/// <paramref name="Write"/>.</param>
internal sealed record A350Lever(A350Control Control, IReadOnlyDictionary<double, string> Words, Func<double, double, string>? Write,
    Func<IReadOnlyList<A350Step>>? Press = null, Func<double, IReadOnlyList<A350Step>>? Steps = null);

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
/// <item>Pitch trim: each of the two switches trims the THS while held (0 nose up, 2 nose down, 1 off), alone or both
/// together; two nudges move both together, as a pilot moves them.</item>
/// </list>
/// "All thrust levers", "Ground spoilers" and "System display page" have no input event of their own: they are the
/// A380's rows. The page is <c>INI_ECAM_ACTIVE_PAGE</c>; a pick presses that page's ECP key through its input event
/// (a hold, as the key row presses it), live 2026-10-27. Pure.
/// </summary>
internal static class A350Levers
{
    public const int FlapStep = 4096;

    /// <summary>How long a nudge holds the rudder trim switch: about 0.4 degrees (it trims 1.6 degrees a second).</summary>
    public const int NudgeMs = 300;

    /// <summary>How long a nudge holds the pitch trim switches: about 0.2 degrees (the THS trims about 0.19 degrees a
    /// second, live 2026-10-27; the A350 sets its takeoff trim itself, so these are small corrections).</summary>
    public const int PitchNudgeMs = 1000;
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
        ["AIRLINER_PITCH_TRIM_LEFT"] = Lever("AIRLINER_PITCH_TRIM_LEFT", "Left pitch trim", A350Kinds.None, null,
            Array.Empty<(double, string)>(), null),
        ["AIRLINER_PITCH_TRIM_RIGHT"] = Lever("AIRLINER_PITCH_TRIM_RIGHT", "Right pitch trim", A350Kinds.None, null,
            Array.Empty<(double, string)>(), null),
        // The sidestick takeover buttons' Set fires 'INI.AUTOPILOT_OFF' (>F:KeyEvent), which the map cannot read as a state.
        ["AIRLINER_SIDE_STICK_DISCO_CPT"] = Lever("AIRLINER_SIDE_STICK_DISCO_CPT", "Captain sidestick takeover", A350Kinds.Button,
            null, Array.Empty<(double, string)>(), null),
        ["AIRLINER_SIDE_STICK_DISCO_FO"] = Lever("AIRLINER_SIDE_STICK_DISCO_FO", "First officer sidestick takeover", A350Kinds.Button,
            null, Array.Empty<(double, string)>(), null),
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
        ("AIRLINER_RUDDER_TRIM", false, Nudge("RUDDER_TRIM_LEFT", "Rudder trim left (nudge)", 2, NudgeMs, "AIRLINER_RUDDER_TRIM_Set")),
        ("AIRLINER_RUDDER_TRIM", false, Nudge("RUDDER_TRIM_RIGHT", "Rudder trim right (nudge)", 0, NudgeMs, "AIRLINER_RUDDER_TRIM_Set")),
        ("AIRLINER_PITCH_TRIM_RIGHT", false, Nudge("PITCH_TRIM_UP", "Pitch trim nose up (nudge)", 0, PitchNudgeMs,
            "AIRLINER_PITCH_TRIM_LEFT_Set", "AIRLINER_PITCH_TRIM_RIGHT_Set")),
        ("AIRLINER_PITCH_TRIM_RIGHT", false, Nudge("PITCH_TRIM_DOWN", "Pitch trim nose down (nudge)", 2, PitchNudgeMs,
            "AIRLINER_PITCH_TRIM_LEFT_Set", "AIRLINER_PITCH_TRIM_RIGHT_Set")),
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

    /// <summary>The SD page's name by its page number (`INI_ECAM_ACTIVE_PAGE`: 8 is "Fuel"), or null. CRUISE (13) has no
    /// ECP key: the SD shows it in cruise, and STS with nothing to show brings it up (live 2026-10-28).</summary>
    public static string? SdPageWord(double page) =>
        SdPages.Where(p => p.Page == page).Select(p => p.Word).FirstOrDefault() ?? (page == 13 ? "Cruise" : null);

    /// <summary>The words a row says for a state no pick selects (SimVarDefinition.SpokenOnlyDescriptions), or null: the
    /// SD page row's CRUISE (review 2026-10-29: said "13.0").</summary>
    public static Dictionary<double, string>? SpokenOnlyWordsFor(string controlId) =>
        Same(controlId, "MSFSBA_SD_PAGE") ? new Dictionary<double, string> { [13] = SdPageWord(13)! } : null;

    /// <summary>A lever's position words, or null for any other control.</summary>
    public static IReadOnlyDictionary<double, string>? WordsFor(string controlId) =>
        ById.TryGetValue(controlId, out var lever) && lever.Words.Count > 0 ? lever.Words : null;

    /// <summary>The steps a pick takes where one string does not do it, or null.</summary>
    public static IReadOnlyList<A350Step>? StepsFor(A350Control control, double position) =>
        ById.TryGetValue(control.Id, out var lever) && lever.Steps is { } steps ? steps(position) : null;

    /// <summary>The steps a press takes where one Set does not do it (a nudge), or null.</summary>
    public static IReadOnlyList<A350Step>? PressStepsFor(A350Control control) =>
        ById.TryGetValue(control.Id, out var lever) && lever.Press is { } press ? press() : null;

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

    /// <summary>A button that holds spring switches at <paramref name="side"/> for <paramref name="holdMs"/>, then puts them
    /// back to 1 (off), through their input events.</summary>
    private static A350Lever Nudge(string name, string title, double side, int holdMs, params string[] events)
    {
        var lever = Lever($"MSFSBA_{name}", title, A350Kinds.Button, null, Array.Empty<(double, string)>(), null,
            key: $"A350_{name}");
        lever.Control.Event = events[0];
        string Write(double position) => string.Join(" ", events.Select(e => $"{Format(position)} (>B:{e})"));
        return lever with
        {
            Press = () => new A350Step[] { new A350CalcStep(Write(side)), new A350DelayStep(holdMs), new A350CalcStep(Write(1)) },
        };
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

    private static string Format(double value) => A350WritePlan.Format(value);
}
