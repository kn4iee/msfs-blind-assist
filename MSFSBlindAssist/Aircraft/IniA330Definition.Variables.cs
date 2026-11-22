using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>Continuous keys never spoken by the generic monitor (knob positions, caches).</summary>
    private readonly HashSet<string> _silentKeys = new();

    /// <summary>FCU buttons: key -> what a press does (an L:var press flag, or an input event).</summary>
    internal static readonly (string key, string label, string? pulse, string? inputEvent)[] AutopilotButtons =
    {
        ("A330_AP1", "AP 1", "INI_AP1_BUTTON", null),
        ("A330_AP2", "AP 2", "INI_AP2_BUTTON", null),
        ("A330_ATHR", "A/THR", null, "AIRLINER_ATHR_PUSH"),
        ("A330_SPD_PULL", "Speed knob pull (selected speed)", "INI_FCU_SELECTED_SPEED_BUTTON", null),
        ("A330_SPD_PUSH", "Speed knob push (managed speed)", "INI_FCU_MANAGED_SPEED_BUTTON", null),
        ("A330_HDG_PULL", "Heading knob pull (selected heading)", "INI_FCU_SELECTED_HEADING_BUTTON", null),
        ("A330_HDG_PUSH", "Heading knob push (managed, NAV)", "INI_FCU_MANAGED_HEADING_BUTTON", null),
        ("A330_ALT_PULL", "Altitude knob pull (open climb or descent)", "INI_FCU_ALTITUDE_PULL_COMMAND", null),
        ("A330_ALT_PUSH", "Altitude knob push (managed climb or descent)", "INI_FCU_ALTITUDE_PUSH_COMMAND", null),
        ("A330_VS_PULL", "V/S knob pull (selected vertical speed)", null, "AIRLINER_MCU_VS_PULL"),
        ("A330_VS_PUSH", "V/S knob push (level off)", null, "AIRLINER_MCU_VS_PUSH"),
        ("A330_LOC", "LOC", "INI_LOCALIZER_BUTTON", null),
        ("A330_APPR", "APPR", "INI_APPROACH_BUTTON", null),
        ("A330_ALT_BTN", "ALT (hold the current altitude)", null, "AIRLINER_ALT_PUSH"),
    };

    internal static readonly string[] AutopilotSelectKeys = { "A330_SPD_MACH", "A330_TRK_FPA", "A330_ALT_STEP", "A330_METRIC" };

    internal static IEnumerable<string> AutopilotControlKeys =>
        AutopilotButtons.Select(b => b.key).Concat(AutopilotSelectKeys);

    internal static readonly string[] AutopilotDisplayKeys =
        { "A330_AP1_ON", "A330_AP2_ON", "A330_ATHR_ON", "A330_FCU_SPD", "A330_FCU_HDG", "A330_FCU_ALT", "A330_FCU_VS" };

    internal static readonly string[] FmaDisplayKeys =
        { "A330_FMA_THRUST", "A330_FMA_VERT", "A330_FMA_LAT", "A330_FMA_ARMED", "A330_FMA_CAP", "A330_FMA_AP" };

    internal const string FmaMonitorKey = "A330_M_PITCH";

    // FMA inputs: silent caches composed in ProcessSimVarUpdate
    internal static readonly (string key, string lvar)[] FmaInputVars =
    {
        ("A330_M_AT", "INI_AT_MODE"), ("A330_M_PITCH", "INI_PITCH_MODE"), ("A330_M_PITCH_ARM", "INI_PITCH_MODE_ARM"),
        ("A330_M_PITCH_ARM2", "INI_PITCH_MODE_ARM2"), ("A330_M_ROLL", "INI_ROLL_MODE"), ("A330_M_ROLL_ARM", "INI_ROLL_MODE_ARM"),
        ("A330_M_APPR", "INI_PITCH_ROLL_MODE"), ("A330_M_MACH", "INI_AIRSPEED_IS_MACH"), ("A330_M_TRKFPA", "INI_TRACK_FPA_STATE"),
        ("A330_M_TOGA_LOCK", "INI_TOGA_LOCK_ACTIVE"), ("A330_M_THR_LATCH", "INI_FORCE_THRUST_LATCH"),
        ("A330_M_CAP", "INI_AUTOPILOT_CAPABILITY"), ("A330_M_SPD_MANAGED", "INI_SPEED_MANAGED"),
    };

    /// <summary>The fuel tanks the module publishes in kilograms (centre comes from the sim, see FuelSummary).</summary>
    internal static readonly (string key, string lvar, string name)[] FuelVars =
    {
        ("A330_FUEL_LO", "INI_FUEL_WEIGHT_LEFT_OUTER", "left outer"), ("A330_FUEL_LI", "INI_FUEL_WEIGHT_LEFT_INNER", "left inner"),
        ("A330_FUEL_RI", "INI_FUEL_WEIGHT_RIGHT_INNER", "right inner"), ("A330_FUEL_RO", "INI_FUEL_WEIGHT_RIGHT_OUTER", "right outer"),
        ("A330_FUEL_TRIM", "INI_FUEL_WEIGHT_TRIM", "trim tank"),
    };

    /// <summary>The First Officer's own read-only keys (FirstOfficer/IniAirbus).</summary>
    internal const string FoEng1AvailKey = "A330_FO_ENG1_AVAIL", FoEng2AvailKey = "A330_FO_ENG2_AVAIL",
        FoApuFlapKey = "A330_FO_APU_FLAP", FoGearLeftKey = "A330_FO_GEAR_LEFT", FoGearRightKey = "A330_FO_GEAR_RIGHT";

    internal static readonly (string key, string name, string display, string units, SimVarType type)[] FoReadVars =
    {
        (FoEng1AvailKey, "ENGINE_1_AVAIL", "Engine 1 available", "number", SimVarType.LVar),
        (FoEng2AvailKey, "ENGINE_2_AVAIL", "Engine 2 available", "number", SimVarType.LVar),
        (FoApuFlapKey, "INI_APU_FLAP_PERCENT", "APU air intake flap", "number", SimVarType.LVar),
        (FoGearLeftKey, "GEAR LEFT POSITION", "Left main gear", "percent", SimVarType.SimVar),
        (FoGearRightKey, "GEAR RIGHT POSITION", "Right main gear", "percent", SimVarType.SimVar),
    };

    /// <summary>
    /// How a First Officer write of <paramref name="key"/> reaches the aircraft through
    /// <see cref="HandleUIVariableSet"/>, so the First Officer can refuse a write that cannot land
    /// instead of reporting success (HandleUIVariableSet itself only speaks the failure, and the First
    /// Officer suppresses that). <c>Calc</c>: the calculator path (MobiFlight) — a push, rpn or toggle
    /// control. <c>InputEvent</c>: SetInputEvent / a value press on this input
    /// event. <c>Toggle</c>: the control presses only when its read differs, so an unread state must not
    /// be written. <c>HoldMs</c>: a held momentary key (the tests). Null: not a control the First Officer
    /// may write.
    /// </summary>
    internal (bool Calc, string? InputEvent, bool Toggle, int HoldMs)? FoWriteRoute(string key)
    {
        GetVariables();
        if (key == "A330_AUTOBRAKE")
            return (false, AutobrakeEvent(3), false, 0);
        if (!_controlsByKey.TryGetValue(key, out var c) || c.Kind == "status") return null;
        return c.Mode switch
        {
            "push" when !string.IsNullOrEmpty(c.Event) => (true, null, false, c.HoldMs ?? 0),
            "rpn" when !string.IsNullOrEmpty(c.Rpn) => (true, null, false, c.HoldMs ?? 0),
            "toggle" when !string.IsNullOrEmpty(c.Rpn) || !string.IsNullOrEmpty(c.Event) => (true, null, true, 0),
            "set" when !string.IsNullOrEmpty(c.Event) => (false, c.Event, false, 0),
            _ => null,
        };
    }

    /// <summary>The read-back value of the generated control <paramref name="key"/> for the position
    /// labelled <paramref name="label"/>, or null when the control or label does not exist (tests pin the
    /// First Officer's values against the measured map).</summary>
    internal double? FoPositionValue(string key, string label)
    {
        GetVariables();
        return _controlsByKey.TryGetValue(key, out var c)
            ? c.Positions?.FirstOrDefault(p => p.Label == label)?.Value : null;
    }

    internal const string TakeoffIasKey = "A330_TO_IAS", TakeoffV1Key = "A330_TO_V1", TakeoffVrKey = "A330_TO_VR", TakeoffV2Key = "A330_TO_V2";

    /// <summary>The Continuous Type:Name pairs added so far in the current BuildVariables, so a generated
    /// status row or lamp reading a name another row already watches is skipped ([VAR-7]).</summary>
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    private void Add(Dictionary<string, SimVarDefinition> vars, string key, SimVarDefinition d)
    {
        vars[key] = d;
        if (d.UpdateFrequency == UpdateFrequency.Continuous) _usedNames.Add(d.Type + ":" + d.Name);
    }

    private void Button(Dictionary<string, SimVarDefinition> vars, string key, string display) => Add(vars, key, new SimVarDefinition
    {
        Name = "MSFSBA_" + key, DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        RenderAsButton = true, SuppressRestingButtonState = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Press" },
    });

    private void Watched(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, Dictionary<double, string>? values = null, bool silent = false,
                 string units = "number", SimVarType type = SimVarType.LVar) => Add(vars, key, new SimVarDefinition
    {
        Name = name, DisplayName = display, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        ValueDescriptions = values ?? new Dictionary<double, string>(),
        ExcludeFromMonitorManager = silent,
    });

    private void Silent(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
    {
        Watched(vars, key, name, display, null, true, units, type);
        _silentKeys.Add(key);
    }

    private void Synthetic(Dictionary<string, SimVarDefinition> vars, string key, string display) => Add(vars, key, new SimVarDefinition
    {
        Name = $"MSFSBA_{key}", DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
    });

    private void OnRequestVar(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
        => Add(vars, key, new SimVarDefinition { Name = name, DisplayName = display, Type = type, Units = units, UpdateFrequency = UpdateFrequency.OnRequest });

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = new Dictionary<string, SimVarDefinition>();
        _usedNames.Clear();

        var engaged = new Dictionary<double, string> { [0] = "Off", [1] = "Engaged" };

        // ---------------- Autopilot (FCU) ----------------
        foreach (var (key, label, _, _) in AutopilotButtons) Button(vars, key, label);
        Watched(vars, "A330_SPD_MACH", "INI_AIRSPEED_IS_MACH", "Speed window units", new() { [0] = "Knots", [1] = "Mach" });
        Watched(vars, "A330_TRK_FPA", "INI_TRACK_FPA_STATE", "HDG-V/S or TRK-FPA", new() { [0] = "HDG V/S", [1] = "TRK FPA" });
        // ALT knob step (measured: input 0 reads 1 and the knob steps 100 feet)
        Watched(vars, "A330_ALT_STEP", "INI_ALTITUDE_STATE", "Altitude knob steps", new() { [1] = "100 feet", [0] = "1000 feet" });
        Watched(vars, "A330_METRIC", "INI_FCU_METRIC_STATE", "Metric altitude", new() { [0] = "Off", [1] = "On" });
        Watched(vars, "A330_AP1_ON", "INI_AP1_ON", "Autopilot 1", engaged);
        Watched(vars, "A330_AP2_ON", "INI_AP2_ON", "Autopilot 2", engaged);
        Watched(vars, "A330_ATHR_ON", "INI_AT_ON", "Autothrust", engaged);
        Silent(vars, "A330_FCU_SPD", "INI_AIRSPEED_DIAL", "FCU speed");
        Silent(vars, "A330_FCU_HDG", "INI_HEADING_DIAL", "FCU heading");
        Silent(vars, "A330_FCU_TRK", "INI_TRK_DIAL", "FCU track");
        Silent(vars, "A330_FCU_ALT", "INI_ALTITUDE_DIAL", "FCU altitude");
        Silent(vars, "A330_FCU_VS", "INI_VVI_DIAL", "FCU vertical speed");
        Silent(vars, "A330_FCU_FPA", "INI_FPA_DIAL", "FCU flight path angle");
        Silent(vars, "A330_ALTITUDE", "INDICATED ALTITUDE", "Altitude", "feet", SimVarType.SimVar);

        // ---------------- Flight modes ----------------
        foreach (var (key, lvar) in FmaInputVars) Silent(vars, key, lvar, key);
        vars[FmaMonitorKey].ExcludeFromMonitorManager = false;   // one Ctrl+M row mutes every flight-mode change
        vars[FmaMonitorKey].DisplayName = "Flight mode changes";
        Synthetic(vars, "A330_FMA_THRUST", "Thrust mode");
        Synthetic(vars, "A330_FMA_VERT", "Vertical mode");
        Synthetic(vars, "A330_FMA_LAT", "Lateral mode");
        Synthetic(vars, "A330_FMA_ARMED", "Armed modes");
        Synthetic(vars, "A330_FMA_CAP", "Landing capability");
        Synthetic(vars, "A330_FMA_AP", "Autopilot and autothrust");

        // ---------------- Fuel caches ----------------
        foreach (var (key, lvar, name) in FuelVars) Silent(vars, key, lvar, name);
        Silent(vars, "A330_FUEL_CENTER", "FUEL TANK CENTER QUANTITY", "Centre tank", "gallons", SimVarType.SimVar);
        Silent(vars, "A330_FUEL_TOTAL_LB", "FUEL TOTAL QUANTITY WEIGHT", "Total fuel", "pounds", SimVarType.SimVar);
        Silent(vars, "A330_FUEL_WPG", "FUEL WEIGHT PER GALLON", "Fuel weight per gallon", "pounds", SimVarType.SimVar);
        Silent(vars, "A330_IS_METRIC", "INI_IS_METRIC", "Weight units");

        // ---------------- First Officer reads (OnRequest, silent; polled by the FO window) ----------------
        // Each name is in the aircraft's own panel-state files (data/panelstates/*.ini in the streamed
        // package) with values that follow the state they name: ENGINE_n_AVAIL 0 cold and
        // dark, 1 ready for take-off (the E/WD's green AVAIL); INI_APU_FLAP_PERCENT 0 cold and dark, 1 with
        // the APU running or still spinning down. Read only — the First Officer never writes them.
        foreach (var (key, name, display, units, type) in FoReadVars)
            if (!vars.ContainsKey(key)) OnRequestVar(vars, key, name, display, units, type);

        // ---------------- Read-outs (fresh on the key press) ----------------
        foreach (var (key, lvar, display) in new[]
                 {
                     ("A330_DIST_DEST", "INI_FMC_DIST_TO_DEST", "Distance to destination"),
                     ("A330_APPR_DIST", "INI_TOTAL_APPR_DIST", "Distance along the route to the destination"),
                     ("A330_TD_DIST_TOTAL", "INI_TD_DIST_TOTAL", "Distance to top of descent"),
                     ("A330_GREEN_DOT", "INI_GREENDOT_SPEED", "Green dot speed"), ("A330_S_SPEED", "INI_S_SPEED", "S speed"),
                     ("A330_F_SPEED", "INI_F_SPEED", "F speed"), ("A330_VLS", "INI_VLS_SPEED", "VLS"),
                     ("A330_VMAX", "INI_MAX_SPEED", "Maximum speed"), ("A330_VAPP", "INI_VAPP_SPEED", "Approach speed"),
                     ("A330_MANAGED_SPEED", "INI_MANAGED_SPEED", "Managed speed target"),
                     ("A330_MINIMUMS", "INI_MINIMUMS_PILOT", "Captain minimums"), ("A330_MDA", "INI_FMS_MDA", "FMS MDA"),
                     ("A330_ILS_FREQ", "INI_ILS_FREQUENCY", "ILS frequency"),
                 })
            OnRequestVar(vars, key, lvar, display);
        OnRequestVar(vars, "A330_GS_FRESH", "GROUND VELOCITY", "Ground speed", "knots", SimVarType.SimVar);
        OnRequestVar(vars, "A330_BARO_FRESH", "KOHLSMAN SETTING MB:1", "Captain altimeter setting", "millibars", SimVarType.SimVar);
        OnRequestVar(vars, "A330_BARO_STD_FRESH", "KOHLSMAN SETTING STD:1", "Captain altimeter standard", "Bool", SimVarType.SimVar);
        // The cockpit's own baro mode (2/3 = STD). Measured live 2026-10-17 on the
        // A330 at FL220 in STD: this read 3 while KOHLSMAN SETTING STD:1 read 0, so STD comes from here.
        OnRequestVar(vars, "A330_BARO_MODE_FRESH", "XMLVAR_Baro1_Mode", "Captain altimeter mode");
        foreach (var (key, simvar, units) in new[]
                 {
                     ("A330_NAV1_FREQ", "NAV ACTIVE FREQUENCY:1", "MHz"), ("A330_NAV2_FREQ", "NAV ACTIVE FREQUENCY:2", "MHz"),
                     ("A330_NAV3_FREQ", "NAV ACTIVE FREQUENCY:3", "MHz"), ("A330_NAV3_LOC", "NAV HAS LOCALIZER:3", "Bool"),
                     ("A330_NAV3_GS", "NAV HAS GLIDE SLOPE:3", "Bool"), ("A330_NAV3_DME", "NAV DME:3", "nautical miles"),
                     ("A330_COM1_ACT", "COM ACTIVE FREQUENCY:1", "MHz"), ("A330_COM1_STBY", "COM STANDBY FREQUENCY:1", "MHz"),
                 })
            OnRequestVar(vars, key, simvar, key, units, SimVarType.SimVar);

        // ---------------- Take-off call-outs ----------------
        Watched(vars, TakeoffV1Key, "INI_V1", "V1 call-out");
        Watched(vars, TakeoffVrKey, "INI_VR", "Rotate call-out");
        Watched(vars, TakeoffV2Key, "INI_V2", "V2 call-out");
        Add(vars, TakeoffIasKey, new SimVarDefinition
        {
            Name = "AIRSPEED INDICATED", DisplayName = "Take-off call-out airspeed", Type = SimVarType.SimVar, Units = "knots",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ExcludeFromBatch = true, HighFrequency = true, ExcludeFromMonitorManager = true,
        });

        // End-to-end MobiFlight probe target: the momentary keys, the FCU press flags and the MCDU keys
        // are calculator writes, so they gate on the probe's verdict; registering this opts in.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        };

        // each owner adds its variables, in this order; the base variables below fill only what is absent
        AddPanelVariables(vars);   // plan 04
        AddFcuVariables(vars);   // plan 05
        AddFlightDisplayVariables(vars);   // plan 09
        AddEcamVariables(vars);   // plan 08
        AddMcduVariables(vars);   // plan 03
        AddReadoutVariables(vars);   // plan 10
        AddFirstOfficerVariables(vars);   // plan 06

        // Four names are read by a second key (the Speed window units and HDG-V/S or TRK-FPA controls, the
        // base class's altitude and trim). Two Continuous batched keys with one Name shift every later
        // struct slot in the batch ([VAR-7]), so each silent cache copy takes its own once-a-second
        // subscription instead: the same sample rate, so nothing a pilot hears or sees changes.
        foreach (var key in new[] { "A330_M_MACH", "A330_M_TRKFPA", "A330_ALTITUDE", "A330_TRIM" })
            vars[key].ExcludeFromBatch = true;

        foreach (var kv in GetBaseVariables())
            if (!vars.ContainsKey(kv.Key)) vars[kv.Key] = kv.Value;
        return vars;
    }
}
