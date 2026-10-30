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

    /// <summary>How long a nudge holds a seat's switch: about 34 percent of the seat's travel.</summary>
    public const int SeatNudgeMs = 1000;
    public const int KeyEventFull = 16384;

    /// <summary>The SD pages the ECP has a key for: the page value, the A380's word, the key's input event; declared before
    /// <see cref="Extras"/>, which reads it as it is built.</summary>
    private static readonly (double Page, string Word, string Key)[] SdPages =
    {
        (1, "Engine", "ENG"), (2, "Hyd", "HYD"), (3, "Elec DC", "ELDC"), (4, "Elec AC", "ELAC"), (5, "Bleed", "BLEED"),
        (6, "Cond", "COND"), (7, "Press", "PRESS"), (8, "Fuel", "FUEL"), (9, "APU", "APU"), (10, "F/Ctl", "FCTL"),
        (11, "Door", "DOOR"), (12, "Wheel", "WHEEL"), (14, "Status", "STS"), (16, "Video", "VIDEO"),
    };

    /// <summary>The take-off's first step, no detent on the quadrant but no other way to set it from a combo: the levers reaching FLX or
    /// TOGA with N2 below 70 arm nothing (<c>update_EWD_flight_phase</c>; live 2026-10-30, lever angle 12 at sea level: N1 53, N2 82).</summary>
    private const double TakeoffSpoolTla = 12;

    private static readonly (double Tla, string Word)[] Detents =
    {
        (-20, "Reverse"), (-6, "Reverse idle"), (0, "Idle"), (TakeoffSpoolTla, A350LeverSpeech.TakeoffSpool), (25, "Climb"), (35, "Flex/MCT"), (45, "TOGA"),
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
        ("AIRLINER_RMP_NAV_1", false, StbyRadNav(1)),
        ("AIRLINER_RMP_NAV_2", false, StbyRadNav(2)),
        ("AIRLINER_RMP_NAV_3", false, StbyRadNav(3)),
        ("AIRLINER_RUDDER_TRIM", false, Nudge("RUDDER_TRIM_LEFT", "Rudder trim left (nudge)", 2, NudgeMs, "AIRLINER_RUDDER_TRIM_Set")),
        ("AIRLINER_RUDDER_TRIM", false, Nudge("RUDDER_TRIM_RIGHT", "Rudder trim right (nudge)", 0, NudgeMs, "AIRLINER_RUDDER_TRIM_Set")),
        ("AIRLINER_PITCH_TRIM_RIGHT", false, Nudge("PITCH_TRIM_UP", "Pitch trim nose up (nudge)", 0, PitchNudgeMs,
            "AIRLINER_PITCH_TRIM_LEFT_Set", "AIRLINER_PITCH_TRIM_RIGHT_Set")),
        ("AIRLINER_PITCH_TRIM_RIGHT", false, Nudge("PITCH_TRIM_DOWN", "Pitch trim nose down (nudge)", 2, PitchNudgeMs,
            "AIRLINER_PITCH_TRIM_LEFT_Set", "AIRLINER_PITCH_TRIM_RIGHT_Set")),
    };

    /// <summary>The walkaround's service panels, hatches and cowlings: the exterior models' clickspots, not the cockpit's, so not in the
    /// map. Each input event only flips an L:var ("(L:INI_ASU_AVAIL) ! (>L:INI_ASU_AVAIL)"), so a row writes the state asked for. Live
    /// 2026-10-30: HP air makes the SD's Bleed page say ASU flow, the refuel door gives the memo REFUEL PNL DOOR OPEN, a main gear door
    /// stays open only with its hatch open; the rest move their door.</summary>
    private static readonly A350Lever[] Exterior =
    {
        Walkaround("EXT_SERVICES", "HP_AIR", "HP air (air start unit)", "INI_ASU_AVAIL", "Not connected", "Connected"),
        Walkaround("EXT_SERVICES", "LP_AIR", "LP air (air conditioning unit)", "INI_ACU_AVAIL", "Not connected", "Connected"),
        Walkaround("EXT_SERVICES", "EXT_POWER", "External power", "INI_GPU_AVAIL", "Not connected", "Connected"),
        Walkaround("EXT_SERVICES", "POTABLE_WATER", "Potable water panel", "INI_Potable_Water_Door"),
        Walkaround("EXT_SERVICES", "WASTE_WATER", "Waste water panel", "INI_Waste_Water_Door"),
        Walkaround("EXT_SERVICES", "REFUEL_DOOR", "Refuel panel door", "INI_Refuel_Door"),
        Walkaround("EXT_SERVICES", "REFUEL_MODE", "Refuel mode selector", "INI_Refuel_Mode_Selector", "Off", "On"),
        Walkaround("EXT_HATCHES", "HYD_GREEN", "Green hydraulic panel", "INI_HYD_Green_Hatch"),
        Walkaround("EXT_HATCHES", "HYD_YELLOW", "Yellow hydraulic panel", "INI_HYD_Yellow_Hatch"),
        Walkaround("EXT_HATCHES", "APU_SERVICE", "APU service door", "INI_APU_Service"),
        Walkaround("EXT_HATCHES", "AVIONICS_DOOR", "Avionics bay door", "INI_AVIONICS_DOOR_STATE"),
        Walkaround("EXT_HATCHES", "CARGO_FWD_UP", "Forward cargo upper panel", "INI_CARGO_FWD_UP_Panel_Door"),
        Walkaround("EXT_HATCHES", "CARGO_FWD_DOWN", "Forward cargo lower panel", "INI_CARGO_FWD_DOWN_Panel_Door"),
        Walkaround("EXT_HATCHES", "CARGO_AFT_UP", "Aft cargo upper panel", "INI_CARGO_AFT_UP_Panel_Door"),
        Walkaround("EXT_HATCHES", "CARGO_AFT_DOWN", "Aft cargo lower panel", "INI_CARGO_AFT_DOWN_Panel_Door"),
        Walkaround("EXT_GEAR", "MLG_HATCH", "Main gear door hatch", "INI_MLG_Door_Opening_Hatch"),
        Walkaround("EXT_GEAR", "MLG_DOOR_LEFT", "Left main gear door", "INI_Gear_GND_Opening_Left"),
        Walkaround("EXT_GEAR", "MLG_DOOR_RIGHT", "Right main gear door", "INI_Gear_GND_Opening_Right"),
        Walkaround("EXT_GEAR", "NLG_HATCH", "Nose gear door hatch", "INI_NLG_Door_Opening_Hatch"),
        Walkaround("EXT_GEAR", "NLG_DOOR", "Nose gear door", "INI_NLG_Door_Opening"),
        Walkaround("EXT_GEAR", "NWS_PANEL", "Nose wheel steering panel", "INI_NWS_NG"),
        Walkaround("EXT_GEAR", "INTERPHONE_PANEL", "Nose gear interphone panel", "INI_INTPH_PANEL"),
        Walkaround("EXT_ENGINES", "COWL_LEFT", "Left engine cowling", "INI_Engine_Cowl_Left"),
        Walkaround("EXT_ENGINES", "COWL_RIGHT", "Right engine cowling", "INI_Engine_Cowl_Right"),
        Walkaround("EXT_ENGINES", "COVERS", "Engine and pitot covers", "INI_COVERS_ENABLED", "Removed", "Fitted"),
    };

    /// <summary>The seats, tables and footrests: A350_Interior_SST's clickspots, not the cockpit file's, so not in the map. Live 2026-10-30: a
    /// seat switch rests at 1; BKFWD 2 moves the seat forward, 0 back; UPDOWN 0 up, 2 down, about 34 percent of its travel a second; the rest
    /// flip one L:var. Stowing a sliding table closes its keyboard cover, as its own click does.</summary>
    private static readonly A350Lever[] Comfort = new[] { ("CPT", "Captain"), ("FO", "First officer") }.SelectMany(s => new[]
    {
        SeatNudge(s.Item1, s.Item2, "forward", "BKFWD", 2),
        SeatNudge(s.Item1, s.Item2, "back", "BKFWD", 0),
        SeatNudge(s.Item1, s.Item2, "up", "UPDOWN", 0),
        SeatNudge(s.Item1, s.Item2, "down", "UPDOWN", 2),
        Comfortable($"SEAT_{s.Item1}", $"SEAT_{s.Item1}_HARNESS", $"{s.Item2} shoulder harness", $"INI_SEAT_{s.Item1}_BKREST_SWITCH", "Free", "Locked"),
        Comfortable($"SEAT_{s.Item1}", $"SEAT_{s.Item1}_ARMREST_LEFT", $"{s.Item2} left armrest", $"INI_A350_INT_SEAT_{s.Item1}_ARMREST_L_BASE", "Stowed", "Deployed"),
        Comfortable($"SEAT_{s.Item1}", $"SEAT_{s.Item1}_ARMREST_RIGHT", $"{s.Item2} right armrest", $"INI_A350_INT_SEAT_{s.Item1}_ARMREST_R_BASE", "Stowed", "Deployed"),
        SlidingTable(s.Item1, s.Item2),
        Comfortable($"TABLE_{s.Item1}", $"TABLE_{s.Item1}_COVER", $"{s.Item2} keyboard cover", $"INI_{s.Item1}_TRAY", "Closed", "Open"),
        Comfortable($"TABLE_{s.Item1}", $"TABLE_{s.Item1}_FOOTREST", $"{s.Item2} footrest", $"INI_{s.Item1}_TRAY_PEDAL", "Stowed", "Deployed"),
    }).Concat(new[]
    {
        Comfortable("SEAT_OBSERVER", "SEAT_JUMPSEAT", "Jumpseat", "INI_A350_INT_SEAT_JS_SEAT", "Stowed", "Deployed"),
        Comfortable("SEAT_OBSERVER", "SEAT_OBSERVER_ARMREST", "Observer left armrest", "INI_A350_INT_SEAT_FE_ARMREST_L_BASE", "Stowed", "Deployed"),
    }).ToArray();

    private static readonly Dictionary<string, A350Lever> ById =
        Replacements.Values.Concat(Extras.Select(e => e.Lever)).Concat(Exterior).Concat(Comfort).ToDictionary(l => l.Control.Id, StringComparer.OrdinalIgnoreCase);

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
        foreach (var lever in Exterior.Concat(Comfort))
            yield return lever.Control;
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

    private static A350Lever Walkaround(string panel, string name, string title, string lvar, string off = "Closed", string on = "Open")
    {
        var lever = Lever($"MSFSBA_EXT_{name}", title, A350Kinds.Selector, "L:" + lvar, new[] { (0.0, off), (1.0, on) },
            (position, _) => $"{Format(position)} (>L:{lvar})", key: $"A350_EXT_{name}");
        lever.Control.Area = "EXTERIOR";
        lever.Control.Panel = panel;
        return lever;
    }

    private static A350Lever Comfortable(string panel, string name, string title, string lvar, string off, string on)
    {
        var lever = Lever($"MSFSBA_{name}", title, A350Kinds.Selector, "L:" + lvar, new[] { (0.0, off), (1.0, on) },
            (position, _) => $"{Format(position)} (>L:{lvar})", key: $"A350_{name}");
        lever.Control.Area = "MISC";
        lever.Control.Panel = panel;
        return lever;
    }

    private static A350Lever SlidingTable(string side, string title)
    {
        var lever = Lever($"MSFSBA_TABLE_{side}", $"{title} sliding table", A350Kinds.Selector, $"L:INI_{side}_TRAY_COVER_TGT",
            new[] { (0.0, "Stowed"), (1.0, "Deployed") },
            (position, _) => position >= 0.5 ? $"1 (>L:INI_{side}_TRAY_COVER_TGT)" : $"0 (>L:INI_{side}_TRAY_COVER_TGT) 0 (>L:INI_{side}_TRAY)",
            key: $"A350_TABLE_{side}");
        lever.Control.Area = "MISC";
        lever.Control.Panel = $"TABLE_{side}";
        return lever;
    }

    /// <summary>A second's hold of a seat's spring switch.</summary>
    private static A350Lever SeatNudge(string side, string title, string way, string axis, double position)
    {
        string lvar = $"INI_SEAT_{side}_{axis}_SWITCH";
        var lever = Lever($"MSFSBA_SEAT_{side}_{way.ToUpperInvariant()}", $"{title} seat {way} (nudge)", A350Kinds.Button, null,
            Array.Empty<(double, string)>(), null, key: $"A350_SEAT_{side}_{way.ToUpperInvariant()}");
        lever.Control.Area = "MISC";
        lever.Control.Panel = $"SEAT_{side}";
        return lever with
        {
            Press = () => new A350Step[] { new A350CalcStep($"{Format(position)} (>L:{lvar})"), new A350DelayStep(SeatNudgeMs), new A350CalcStep($"1 (>L:{lvar})") },
        };
    }

    /// <summary>An RMP's STBY RAD NAV: no input event writes its variable in 1.2.7, so the row writes it (<see cref="A350Rmp.StbyRadNavKey"/>).</summary>
    private static A350Lever StbyRadNav(int rmp) => Lever($"MSFSBA_RMP_STBY_RAD_NAV_{rmp}", "STBY RAD NAV", A350Kinds.Selector, "L:" + A350Rmp.RadioModeVar(rmp),
        new[] { (0.0, "Off"), (1, "On") }, (position, _) => $"{Format(position)} (>L:{A350Rmp.RadioModeVar(rmp)})", key: $"A350_RMP_STBY_RAD_NAV_{rmp}");

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
