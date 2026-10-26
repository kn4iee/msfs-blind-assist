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
    };

    /// <summary>Position words where iniBuilds' tooltip is a sentence: the switch's own markings instead.</summary>
    private static readonly Dictionary<string, Dictionary<double, string>> PositionOverrides = new(StringComparer.Ordinal)
    {
        ["LIGHTS_EXT_NAV"] = new() { [0] = "1", [1] = "2", [2] = "Off" },
        ["LIGHTS_EXT_NOSE"] = new() { [0] = "T.O", [1] = "Taxi", [2] = "Off" },
        ["LIGHTS_INT_STBY_COMPASS"] = new() { [0] = "Compass and eye reference", [1] = "Ice indicator", [2] = "Off" },
        ["SIGNS_EMER_EXIT"] = new() { [0] = "On", [1] = "Arm", [2] = "Off" },
    };
}
