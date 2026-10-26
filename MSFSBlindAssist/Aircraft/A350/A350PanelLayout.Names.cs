namespace MSFSBlindAssist.Aircraft.A350;

public static partial class A350PanelLayout
{
    /// <summary>iniBuilds' group name → the panel the pilot sees; two groups naming one panel merge.</summary>
    private static readonly Dictionary<string, string> PanelNames = new(StringComparer.Ordinal)
    {
        // Overhead (manual p. 24)
        ["WIPERS"] = "Wipers",
        ["CALLS"] = "Calls and Evacuation",
        ["EVAC"] = "Calls and Evacuation",
        ["OXYGEN"] = "Oxygen",
        ["RCD"] = "Recorder",
        ["CVR"] = "Recorder",
        ["ELT"] = "ELT",
        ["EMER_ELEC"] = "Emergency Electrical Power",
        ["FCTL"] = "Flight Controls",
        ["IRS"] = "ADIRS",
        ["LIGHTS_EXT"] = "Exterior Lighting",
        ["APU"] = "APU",
        ["LIGHT_INT"] = "Interior Lighting and Signs",
        ["SIGNS"] = "Interior Lighting and Signs",
        ["ANTI_ICE"] = "Anti-Ice",
        ["AIR"] = "Air Conditioning",
        ["CABIN_PRESS"] = "Cabin Pressure",
        ["ELEC"] = "Electrical",
        ["FUEL"] = "Fuel",
        ["HYD"] = "Hydraulics",
        ["FIRE"] = "Fire",
        ["ENG_MAN_START"] = "Engine Manual Start",
        ["VENT"] = "Ventilation",
        ["CARGO_AIR_COND"] = "Cargo Air Conditioning",
        ["CARGO_SMOKE"] = "Cargo Smoke",
        ["CAB_SMOKE"] = "Cabin Smoke",
        ["GND_HYD"] = "Ground Hydraulics",
        ["CABIN"] = "Cabin",
        ["OIS"] = "OIS",
        ["SURV"] = "Surveillance",
        ["MAINTENANCE"] = "Maintenance",
        // Glareshield
        ["FCU"] = "FCU",
        ["EFIS_CPT"] = "EFIS Captain",
        ["EFIS_FO"] = "EFIS First Officer",
    };

    /// <summary>A control iniBuilds files under another group, sent to the panel it sits on in the cockpit.</summary>
    private static readonly Dictionary<string, string> ControlPanels = new(StringComparer.Ordinal)
    {
        ["JETTISON_ARM"] = "Fuel Jettison",
        ["JETTISON_ACTIVE"] = "Fuel Jettison",
        ["EMER_ELEC_RAT"] = "Emergency Electrical Power",
    };

    /// <summary>The order of each section's panels; a panel not named here follows them in cockpit file order.</summary>
    private static readonly Dictionary<string, List<string>> PanelOrder = new(StringComparer.Ordinal)
    {
        ["Overhead"] = new()
        {
            "Wipers", "Calls and Evacuation", "Oxygen", "Recorder", "ELT", "Emergency Electrical Power", "Fuel Jettison",
            "Flight Controls", "ADIRS", "Exterior Lighting", "APU", "Interior Lighting and Signs", "Anti-Ice",
            "Air Conditioning", "Cabin Pressure", "Electrical", "Fuel", "Hydraulics", "Fire", "Engine Manual Start",
            "Ventilation", "Cargo Air Conditioning", "Cargo Smoke", "Cabin Smoke", "Ground Hydraulics", "Cabin", "OIS",
            "Surveillance", "Maintenance",
        },
        ["Glareshield"] = new() { "FCU", "EFIS Captain", "EFIS First Officer" },
    };

    /// <summary>Names for controls whose tooltip title is wrong or shared: iniBuilds titles the engine bleeds
    /// "HOT AIR 1/2" (the hot-air buttons' own titles), engine 2's FADEC ground power "ENG 1", and gives both
    /// wipers, both rain repellents and the four engine fire agents one title per pair.</summary>
    private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
    {
        ["AIR_ENG_BLEED_1"] = "ENG 1 bleed",
        ["AIR_ENG_BLEED_2"] = "ENG 2 bleed",
        ["MAINT_ENG_FADEC_GND_POWER_2"] = "ENG 2 FADEC GND PWR",
        ["WIPER_LEFT"] = "Captain wiper",
        ["WIPER_RIGHT"] = "First officer wiper",
        ["WIPER_RAIN_RPLNT_LEFT"] = "Captain rain repellent",
        ["WIPER_RAIN_RPLNT_RIGHT"] = "First officer rain repellent",
        ["FIRE_ENG_1_AGENT_1"] = "ENG 1 agent 1",
        ["FIRE_ENG_1_AGENT_2"] = "ENG 1 agent 2",
        ["FIRE_ENG_2_AGENT_1"] = "ENG 2 agent 1",
        ["FIRE_ENG_2_AGENT_2"] = "ENG 2 agent 2",
        ["FIRE_APU_AGENT"] = "APU agent",
        ["3RD_OCCPT_CONSOLE_BRT"] = "Third occupant console light",
        ["3RD_OCCPT_READING_BRT"] = "Third occupant reading light",
        ["4RD_OCCPT_READING_BRT"] = "Fourth occupant reading light",
        ["LIGHTS_INT_COAT_STWG"] = "Coat stowage light",
        // Calls and Evacuation: the rest-area calls named for whom they call (CCRC, FCRC), the EVAC rows by their panel.
        ["CALLS_CAB_REST"] = "Cabin crew rest",
        ["CALLS_FLT_REST"] = "Flight crew rest",
        ["CALLS_PURS"] = "Purser",
        ["EVAC_COMMAND"] = "EVAC command",
        ["EVAC_HORN_OFF"] = "EVAC horn off",
        ["EVAC_CAPT_PURS"] = "EVAC CAPT and PURS",
        // Recorder: the CVR's two buttons carry only "TEST" and "ERASE"; placard abbreviations kept as printed.
        ["RCD_RCDR_GND_CTRL"] = "RCDR GND CTL",
        ["CVR_TEST"] = "CVR test",
        ["CVR_ERASE"] = "CVR erase",
        // Emergency electrical power: iniBuilds titles the EMER GEN button after its FAULT light.
        ["EMER_ELEC_RAT"] = "RAT MAN ON",
        ["EMER_ELEC_GEN_FAULT"] = "EMER GEN",
        // Fuel jettison: the panel's two buttons are ARM and ACTIVE (iniBuilds: "ACTIVATE").
        ["JETTISON_ACTIVE"] = "Fuel jettison active",
        // Flight controls: the computers by their own names ("F/CTL PRIM 1" reads as a fraction).
        ["FCTL_PRIM_1"] = "PRIM 1", ["FCTL_PRIM_2"] = "PRIM 2", ["FCTL_PRIM_3"] = "PRIM 3",
        ["FCTL_SEC_1"] = "SEC 1", ["FCTL_SEC_2"] = "SEC 2", ["FCTL_SEC_3"] = "SEC 3",
        // ADIRS: iniBuilds titles the three mode selectors "ADR n MODE SELECTOR".
        ["IRS_MODE_1"] = "ADIRS 1", ["IRS_MODE_2"] = "ADIRS 2", ["IRS_MODE_3"] = "ADIRS 3",
        // Lights and signs: placard abbreviations spoken whole.
        ["LIGHTS_EXT_LANDING"] = "Landing",
        ["APU_MASTER_SWITCH"] = "Master switch",
        ["LIGHTS_INT_ANN_LT"] = "Annunciator light",
        ["LIGHTS_INT_STBY_COMPASS"] = "Standby compass light",
        ["SIGNS_EMER_EXIT"] = "EMER exit light",
        ["SIGNS_NO_SMOKING"] = "No smoking and no PED",
        ["ANTI_ICE_PROBE_WINDOW_HEAT"] = "Probe and window heat",
        // Cabin pressure: the manual targets, without "TRGT SEL".
        ["CABIN_PRESS_ALTITUDE_TARGET"] = "Cabin altitude target",
        ["CABIN_PRESS_VS_TARGET"] = "Cabin V/S target",
        ["CABIN_PRESS_ALTITUDE_MODE"] = "Cabin altitude mode",
        // Air conditioning: "XBLEED", "COCKPIT SEL", "CABIN SEL", "AIR FLOW SEL" spoken whole.
        ["AIR_X_BLEED"] = "Crossbleed",
        ["AIR_TEMP_COCKPIT"] = "Cockpit temperature",
        ["AIR_TEMP_CABIN"] = "Cabin temperature",
        ["AIR_FLOW"] = "Air flow",
        // Electrical: placard abbreviations kept in capitals so a reader spells them as printed.
        ["ELEC_EXT_1"] = "EXT 1", ["ELEC_EXT_2"] = "EXT 2",
        ["ELEC_GEN_1A"] = "GEN 1A", ["ELEC_GEN_1B"] = "GEN 1B", ["ELEC_GEN_2A"] = "GEN 2A", ["ELEC_GEN_2B"] = "GEN 2B",
        ["ELEC_GEN_APU"] = "APU GEN",
        ["ELEC_IDG_1A"] = "DRIVE 1A", ["ELEC_IDG_1B"] = "DRIVE 1B", ["ELEC_IDG_2A"] = "DRIVE 2A", ["ELEC_IDG_2B"] = "DRIVE 2B",
        ["ELEC_PAX_SYS"] = "PAX SYS",
        ["ELEC_SIDE_ISOL"] = "Side 1 and 2 isolation",
        // Fuel: each wing tank's main and standby pump, side first ([VAR-1]).
        ["FUEL_PUMP_LEFT_TANK_MAIN"] = "Left main pump", ["FUEL_PUMP_LEFT_TANK_STBY"] = "Left standby pump",
        ["FUEL_PUMP_RIGHT_TANK_MAIN"] = "Right main pump", ["FUEL_PUMP_RIGHT_TANK_STBY"] = "Right standby pump",
        ["FUEL_PUMP_CTR_TANK_LEFT"] = "Center left pump", ["FUEL_PUMP_CTR_TANK_RIGHT"] = "Center right pump",
        ["FUEL_TANK_TRANSFER_LEFT"] = "Left transfer", ["FUEL_TANK_TRANSFER_RIGHT"] = "Right transfer",
        ["FUEL_CTR_TANK_FEED"] = "Center tank feed",
        ["VENT_AVNCS_EXTRACT"] = "Avionics extract", ["VENT_CABIN_FANS"] = "Cabin fans", ["VENT_COOLING"] = "Cooling",
        // FCU: placards as printed; each knob by what it sets, its push and pull apart; the two sides' twins by side.
        ["FCU_AP_1"] = "AP 1", ["FCU_AP_2"] = "AP 2", ["FCU_ATHR"] = "A/THR", ["FCU_FD"] = "FD", ["FCU_LOC"] = "LOC",
        ["FCU_APPR"] = "APPR", ["FCU_ALT"] = "ALT", ["FCU_SPD_MACH"] = "SPD/MACH", ["FCU_TRK_FPA"] = "HDG-V/S / TRK-FPA",
        ["FCU_METER"] = "Metric altitude", ["FCU_TRUE_MAG"] = "TRUE/MAG",
        ["FCU_SPD_KNOB_PUSH"] = "Speed knob push", ["FCU_SPD_KNOB_PULL"] = "Speed knob pull", ["FCU_SPD_KNOB"] = "Speed knob",
        ["FCU_HDG_KNOB_PUSH"] = "Heading knob push", ["FCU_HDG_KNOB_PULL"] = "Heading knob pull", ["FCU_HDG_KNOB"] = "Heading knob",
        ["FCU_ALT_KNOB_PUSH"] = "Altitude knob push", ["FCU_ALT_KNOB_PULL"] = "Altitude knob pull", ["FCU_ALT_KNOB"] = "Altitude knob",
        ["FCU_VS_KNOB_PUSH"] = "V/S knob push", ["FCU_VS_KNOB_PULL"] = "V/S knob pull", ["FCU_VS_KNOB"] = "V/S knob",
        ["FCU_ALT_SELECTOR"] = "Altitude 100/1000 selector",
        ["FCU_CHRONO_LEFT"] = "Captain chrono", ["FCU_CHRONO_RIGHT"] = "First officer chrono",
        ["FCU_LOUD_SPEAKER_LEFT"] = "Captain loudspeaker", ["FCU_LOUD_SPEAKER_RIGHT"] = "First officer loudspeaker",
        ["FCU_HUD_BRIGHTNESS_LEFT"] = "Captain HUD brightness", ["FCU_HUD_BRIGHTNESS_RIGHT"] = "First officer HUD brightness",
        ["FCU_HUD_XWIND_LEFT"] = "Captain HUD crosswind mode", ["FCU_HUD_XWIND_RIGHT"] = "First officer HUD crosswind mode",
        ["INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID"] = "Captain HUD declutter",
        ["INSTRUMENT_FCU_HUD_DECLUTTER_RIGHT_IE_ID"] = "First officer HUD declutter",
        ["FCU_MASTER_CAUTION_LEFT"] = "Captain master caution", ["FCU_MASTER_CAUTION_RIGHT"] = "First officer master caution",
        ["FCU_MASTER_WARNING_LEFT"] = "Captain master warning", ["FCU_MASTER_WARNING_RIGHT"] = "First officer master warning",
        ["FCU_AUTO_LAND_LEFT"] = "Captain autoland", ["FCU_AUTO_LAND_RIGHT"] = "First officer autoland",
        ["FCU_ATC_MSG_LEFT"] = "Captain ATC message", ["FCU_ATC_MSG_RIGHT"] = "First officer ATC message",
        ["FCU_SLIDING_TABLE_BRT_LEFT"] = "Captain sliding table light", ["FCU_SLIDING_TABLE_BRT_RIGHT"] = "First officer sliding table light",
        ["FCU_DISPLAY_BRT"] = "FCU display brightness", ["FCU_INTEG_BRT"] = "FCU integral lighting",
        // EFIS control panels: each side's twin by side; the filter buttons by their placard.
        ["EFIS_MODE_LEFT"] = "Captain ND mode", ["EFIS_RANGE_LEFT"] = "Captain ND range",
        ["EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH"] = "Captain baro knob push", ["EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL"] = "Captain baro knob pull",
        ["EFIS_QNH_PRESSURE_SELECTOR_LEFT"] = "Captain baro knob", ["EFIS_QNH_UNIT_SELECTOR_LEFT"] = "Captain baro unit",
        ["EFIS_TSK_1_LEFT"] = "Captain CSTR", ["EFIS_TSK_2_LEFT"] = "Captain WPT", ["EFIS_TSK_3_LEFT"] = "Captain VORD",
        ["EFIS_TSK_4_LEFT"] = "Captain NDB", ["EFIS_TSK_5_LEFT"] = "Captain ARPT",
        ["EFIS_BSK_1_LEFT"] = "Captain VOR 1", ["EFIS_BSK_2_LEFT"] = "Captain WX", ["EFIS_BSK_3_LEFT"] = "Captain TERR",
        ["EFIS_BSK_4_LEFT"] = "Captain TRAF", ["EFIS_BSK_5_LEFT"] = "Captain VOR 2",
        ["EFIS_LS_LEFT"] = "Captain LS", ["EFIS_TAXI_LEFT"] = "Captain taxi", ["EFIS_VV_LEFT"] = "Captain VV",
        // Cargo, ground hydraulics, cabin, OIS, surveillance and maintenance: abbreviations spoken whole.
        ["CARGO_AIR_COND_AFT_ISOL_VALVE"] = "Aft isolation valves",
        ["CARGO_AIR_COND_FWD_ISOL_VALVE"] = "Forward isolation valves",
        ["CARGO_AIR_COND_BULK_ISOL_VALVE"] = "Bulk isolation valves",
        ["CARGO_AIR_COND_BULK_TEMP"] = "Bulk temperature", ["CARGO_AIR_COND_FWD_TEMP"] = "Forward temperature",
        ["CARGO_SMOKE_AGENT_TO_FWD"] = "Agent to forward", ["CARGO_SMOKE_AGENT_TO_AFT"] = "Agent to aft",
        ["GND_HYD_GREEN_ELEC_PUMP"] = "Green electric pump", ["GND_HYD_YELLOW_ELEC_PUMP"] = "Yellow electric pump",
        ["GND_HYD_GREEN_ELEC_PUMP_AUTO"] = "Green electric pump override",
        ["GND_HYD_YELLOW_ELEC_PUMP_AUTO"] = "Yellow electric pump override",
        ["CABIN_MOBILE_COM"] = "Mobile communication",
        ["OIS_CAB_DATA"] = "Cabin data to OIS", ["OIS_TO_ANVCS"] = "OIS data to avionics",
        ["SURV_TO_SURV"] = "Takeoff surveillance",
        ["MAINT_APU_ECON_MODE"] = "APU economy mode",
        ["MAINT_CKPT_DOOR_LOCKG_SYS"] = "Cockpit door locking system",
        ["MAINT_COM_GND_CONNECTION"] = "COM ground connection",
        ["MAINT_ELEC_REMOTE_CB_CTRL"] = "ELEC remote circuit breaker control",
        ["MAINT_GROUND_HF_DATALINK"] = "Ground HF datalink",
        ["MAINT_SVCE_INT_OVRD"] = "Service interphone override",
        ["MAINT_TOW_ON_BAT_POWER"] = "Towing on battery",
    };

    private static readonly Dictionary<double, string> NdModes = new()
    {
        [0] = "Rose LS", [1] = "Rose VOR", [2] = "Rose NAV", [3] = "Arc", [4] = "Plan",
    };

    /// <summary>The ND range: five ZOOM positions (the manual: "from a maximum of 5nm down to a minimum of 0.2nm"; the
    /// middle three are inferred), then 10 to 640 NM.</summary>
    private static readonly Dictionary<double, string> NdRanges = new()
    {
        [0] = "Zoom 0.2 NM", [1] = "Zoom 0.5 NM", [2] = "Zoom 1 NM", [3] = "Zoom 2 NM", [4] = "Zoom 5 NM", [5] = "10 NM",
        [6] = "20 NM", [7] = "40 NM", [8] = "80 NM", [9] = "160 NM", [10] = "320 NM", [11] = "640 NM",
    };

    /// <summary>Toggles the cockpit returns by itself within a second (light tests): a row presses them, once.
    /// Live 2026-10-26: AUTOLAND's INI_AUTOLAND_LIGHT_TEST goes back to 0 on its own.</summary>
    private static readonly HashSet<string> MomentaryToggles = new(StringComparer.Ordinal)
    {
        "FCU_AUTO_LAND_LEFT", "FCU_AUTO_LAND_RIGHT",
    };

    /// <summary>Whether a toggle is pressed rather than set (<see cref="MomentaryToggles"/>).</summary>
    public static bool IsMomentary(A350Control control) => MomentaryToggles.Contains(Short(control.Id));

    /// <summary>Position words where iniBuilds' tooltip is a sentence: the switch's own markings instead.</summary>
    private static readonly Dictionary<string, Dictionary<double, string>> PositionOverrides = new(StringComparer.Ordinal)
    {
        ["LIGHTS_EXT_NAV"] = new() { [0] = "1", [1] = "2", [2] = "Off" },
        ["LIGHTS_EXT_NOSE"] = new() { [0] = "T.O", [1] = "Taxi", [2] = "Off" },
        ["LIGHTS_INT_STBY_COMPASS"] = new() { [0] = "Compass and eye reference", [1] = "Ice indicator", [2] = "Off" },
        ["SIGNS_EMER_EXIT"] = new() { [0] = "On", [1] = "Arm", [2] = "Off" },
        ["EVAC_CAPT_PURS"] = new() { [0] = "Captain only", [1] = "Captain and purser" },
        ["ELT_GUARD"] = new() { [0] = "Closed", [1] = "Open" },   // its tooltip words are the click's action
        ["FCU_FD"] = new() { [0] = "Off", [1] = "On" },
        ["FCU_METER"] = new() { [0] = "Off", [1] = "On" },
        ["FCU_TRUE_MAG"] = new() { [0] = "Magnetic", [1] = "True" },
        ["EFIS_MODE_LEFT"] = NdModes, ["EFIS_MODE_RIGHT"] = NdModes,
        ["EFIS_RANGE_LEFT"] = NdRanges, ["EFIS_RANGE_RIGHT"] = NdRanges,
        ["CABIN_PRESS_ALTITUDE_MODE"] = new() { [0] = "Auto", [1] = "Man" },   // the button's MAN legend
        ["CABIN_PRESS_VS_MODE"] = new() { [0] = "Auto", [1] = "Man" },
    };
}
