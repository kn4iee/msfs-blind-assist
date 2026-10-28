namespace MSFSBlindAssist.Aircraft.A350;

public static partial class A350PanelLayout
{
    /// <summary>iniBuilds' group name → the panel the pilot sees; two groups naming one panel merge. Panels take the
    /// FlyByWire A380's names (and order, <see cref="PanelOrder"/>) wherever the two cockpits share a panel, so a pilot finds
    /// the same panel in the same place on both Airbus widebodies.</summary>
    private static readonly Dictionary<string, string> PanelNames = new(StringComparer.Ordinal)
    {
        // Overhead
        ["ELEC"] = "ELEC",
        ["EMER_ELEC"] = "ELEC",
        ["APU"] = "APU",
        ["FUEL"] = "Fuel",
        ["HYD"] = "Hydraulics",
        ["GND_HYD"] = "Hydraulics",
        ["AIR"] = "Air Conditioning",
        ["CABIN_PRESS"] = "Pressurization",
        ["VENT"] = "Ventilation",
        ["CARGO_AIR_COND"] = "Cargo Air",
        ["ANTI_ICE"] = "Anti Ice",
        ["FIRE"] = "Fire",
        ["CARGO_SMOKE"] = "Cargo Smoke",
        ["CAB_SMOKE"] = "Cabin Smoke",
        ["OXYGEN"] = "Oxygen",
        ["CALLS"] = "Calls",
        ["EVAC"] = "Evacuation",
        ["SIGNS"] = "Signs",
        ["WIPERS"] = "Wipers",
        ["IRS"] = "ADIRS",
        ["FCTL"] = "Flight Control Computers",
        ["ENG_MAN_START"] = "Engine FADEC and Manual Start",
        ["RCD"] = "Recorder and Misc",
        ["CVR"] = "Recorder and Misc",
        ["ELT"] = "Recorder and Misc",
        ["SURV"] = "Surveillance",
        ["CABIN"] = "Cabin",
        ["OIS"] = "OIS",
        ["MAINTENANCE"] = "Maintenance",
        ["LIGHT_INT"] = "Interior Lighting",
        ["LIGHTS_EXT"] = "Exterior Lighting",
        // Glareshield
        ["FCU"] = "FCU",
        ["EFIS_CPT"] = "EFIS Captain",
        ["EFIS_FO"] = "EFIS First Officer",
        // Instrument panel (main instrument panel and its side consoles)
        ["LDG_PANEL"] = "Autobrake",
        ["ISIS_1"] = "ISIS",
        ["ISIS_2"] = "ISIS",
        ["ADIRS_FMS"] = "Source Switching",
        ["DISPLAY_EFB_LEFT_PWR"] = "Display Power", ["DISPLAY_EFB_RIGHT_PWR"] = "Display Power",
        ["DISPLAY_EFIS_LEFT_PWR"] = "Display Power", ["DISPLAY_EFIS_RIGHT_PWR"] = "Display Power",
        ["DISPLAY_PFDND_MFD_PWR"] = "Display Power", ["DISPLAY_SD_PWR"] = "Display Power",
        ["MIP_SIDE_PANEL_LEFT"] = "Captain Side Panel",
        ["MIP_INDIVIDUAL_PANEL_LEFT"] = "Captain Individual Panel",
        ["MIP_INDIVIDUAL_PANEL_RIGHT"] = "First Officer Individual Panel",
        ["MIP_SIDE_PANEL_RIGHT"] = "First Officer Side Panel",
        // Pedestal: the A380's panels (Engines, Thrust Levers, Flaps and Brakes, Speed Brake, ECAM Control Panel, Weather
        // Radar, Transponder, Cockpit); the A350's emergency descent and trims have their own.
        ["ENG_PANEL"] = "Engines",
        ["THROTTLE"] = "Thrust Levers",
        ["EMER_DESCENT"] = "Emergency Descent",
        ["FCTL_FLAPS_LEVER"] = "Flaps and Brakes", ["PARK_BRK_PANEL"] = "Flaps and Brakes",
        ["SPOILER_LEVER"] = "Speed Brake",
        ["PITCH_TRIM"] = "Trim", ["RUDDER_TRIM"] = "Trim",
        ["ECAM_BUTTONS"] = "ECAM Control Panel",
        ["SURV_PANEL"] = "Transponder",
        ["CKPT_DOOR"] = "Cockpit", ["CKPT_LT"] = "Cockpit", ["OIS_DISPLAY_TRANSFER"] = "Cockpit", ["PRINTER"] = "Cockpit",
        // Cockpit section (iniBuilds' MISC area)
        ["COCKPIT_DOOR"] = "Doors and Hatches", ["JUMP_SEAT_HATCH_LOCK"] = "Doors and Hatches",
        ["VISORS"] = "Sun Shades",
        ["OXYGEN_TEST"] = "Oxygen Masks",
        ["A350_INTERIOR_SWITCH"] = "HUD Units",
        ["SIDE_STICK_DISCO_CPT"] = "Tillers and Sidesticks", ["SIDE_STICK_DISCO_FO"] = "Tillers and Sidesticks",
        ["CPT_TILLER_DISCO"] = "Tillers and Sidesticks", ["FO_TILLER_DISCO"] = "Tillers and Sidesticks",
        ["LIGHTS_DOME_OVERRIDE"] = "Misc", ["PRINTER_PAPER_SIDE_SWITCH"] = "Misc", ["SPECIAL_FEATURE"] = "Misc",
    };

    /// <summary>Clickspots that repeat another row exactly: the cockpit door's cabin-side handle (the same INI_COCKPIT_DOOR
    /// as the cockpit-side one) and seven of the eight printer paper spots (each hides the same paper,
    /// INI_PRINTER_SIDE_PAPER_VIS 0).</summary>
    private static readonly HashSet<string> HiddenControls = new(StringComparer.Ordinal)
    {
        "COCKPIT_DOOR_HANDLE_OUTTER_CS",
        "PRINTER_PAPER_SIDE_2_CS", "PRINTER_PAPER_SIDE_3_CS", "PRINTER_PAPER_SIDE_4_CS", "PRINTER_PAPER_SIDE_5_CS",
        "PRINTER_PAPER_SIDE_6_CS", "PRINTER_PAPER_SIDE_7_CS", "PRINTER_PAPER_SIDE_8_CS",
    };

    /// <summary>Panels with a plan of their own still to come, not placed until then: the RMPs (192 keys and a display)
    /// and the KCCU wheels (the MFD's).</summary>
    private static readonly HashSet<string> DeferredPanels = new(StringComparer.Ordinal) { "FMGS_1", "FMGS_2" };

    /// <summary>A control iniBuilds files under another group, sent to the panel the A380 puts it on.</summary>
    private static readonly Dictionary<string, string> ControlPanels = new(StringComparer.Ordinal)
    {
        ["JETTISON_ARM"] = "Fuel",
        ["JETTISON_ACTIVE"] = "Fuel",
        ["EMER_ELEC_RAT"] = "ELEC",
        // The bleeds are their own panel on the A380; packs, hot air and temperatures stay Air Conditioning.
        ["AIR_X_BLEED"] = "Bleed Air", ["AIR_ENG_BLEED_1"] = "Bleed Air", ["AIR_ENG_BLEED_2"] = "Bleed Air",
        ["AIR_APU_BLEED"] = "Bleed Air",
        // The occupants' lights and the coat stowage light are lights, not signs.
        ["3RD_OCCPT_CONSOLE_BRT"] = "Interior Lighting", ["3RD_OCCPT_READING_BRT"] = "Interior Lighting",
        ["4RD_OCCPT_READING_BRT"] = "Interior Lighting", ["LIGHTS_INT_COAT_STWG"] = "Interior Lighting",
        ["LIGHTS_INT_ANN_LT"] = "Interior Lighting", ["LIGHTS_INT_STBY_COMPASS"] = "Interior Lighting",
        // The FADEC ground power buttons sit with the engine start, as the A380's "Engine FADEC and Manual Start".
        ["MAINT_ENG_FADEC_GND_POWER_1"] = "Engine FADEC and Manual Start",
        ["MAINT_ENG_FADEC_GND_POWER_2"] = "Engine FADEC and Manual Start",
        // Glareshield: the warning lights and chronos as the A380's Warnings; the HUD and the side controls apart.
        ["FCU_MASTER_CAUTION_LEFT"] = "Warnings", ["FCU_MASTER_CAUTION_RIGHT"] = "Warnings",
        ["FCU_MASTER_WARNING_LEFT"] = "Warnings", ["FCU_MASTER_WARNING_RIGHT"] = "Warnings",
        ["FCU_AUTO_LAND_LEFT"] = "Warnings", ["FCU_AUTO_LAND_RIGHT"] = "Warnings",
        ["FCU_ATC_MSG_LEFT"] = "Warnings", ["FCU_ATC_MSG_RIGHT"] = "Warnings",
        ["FCU_CHRONO_LEFT"] = "Warnings", ["FCU_CHRONO_RIGHT"] = "Warnings",
        ["FCU_HUD_BRIGHTNESS_LEFT"] = "HUD", ["FCU_HUD_BRIGHTNESS_RIGHT"] = "HUD",
        ["FCU_HUD_XWIND_LEFT"] = "HUD", ["FCU_HUD_XWIND_RIGHT"] = "HUD",
        ["INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID"] = "HUD", ["INSTRUMENT_FCU_HUD_DECLUTTER_RIGHT_IE_ID"] = "HUD",
        ["FCU_LOUD_SPEAKER_LEFT"] = "Loudspeakers and Table Lights", ["FCU_LOUD_SPEAKER_RIGHT"] = "Loudspeakers and Table Lights",
        ["FCU_SLIDING_TABLE_BRT_LEFT"] = "Loudspeakers and Table Lights",
        ["FCU_SLIDING_TABLE_BRT_RIGHT"] = "Loudspeakers and Table Lights",
        // Instrument: the gear lever, anti-skid and brake fans as the A380's Gear panel; the rest is Autobrake.
        ["LDG_LEVER"] = "Gear", ["LDG_ANTI_SKID"] = "Gear", ["LDG_BRK_HOT"] = "Gear",
        // Pedestal: the surveillance panel splits as the A380's Weather Radar and Transponder.
        ["WXR_ELEV_LEFT"] = "Weather Radar", ["WXR_ELEV_LEFT_PUSH"] = "Weather Radar", ["WXR_ELEV_LEFT_PULL"] = "Weather Radar",
        ["WXR_ELEV_RIGHT"] = "Weather Radar", ["WXR_ELEV_RIGHT_PUSH"] = "Weather Radar", ["WXR_ELEV_RIGHT_PULL"] = "Weather Radar",
        ["WXR_GAIN_LEFT"] = "Weather Radar", ["WXR_GAIN_LEFT_PUSH"] = "Weather Radar", ["WXR_GAIN_LEFT_PULL"] = "Weather Radar",
        ["WXR_GAIN_RIGHT"] = "Weather Radar", ["WXR_GAIN_RIGHT_PUSH"] = "Weather Radar", ["WXR_GAIN_RIGHT_PULL"] = "Weather Radar",
        ["WXR_VD_AZIM_LEFT"] = "Weather Radar", ["WXR_VD_AZIM_LEFT_PUSH"] = "Weather Radar", ["WXR_VD_AZIM_LEFT_PULL"] = "Weather Radar",
        ["WXR_VD_AZIM_RIGHT"] = "Weather Radar", ["WXR_VD_AZIM_RIGHT_PUSH"] = "Weather Radar", ["WXR_VD_AZIM_RIGHT_PULL"] = "Weather Radar",
        ["WXR_SYS_1"] = "Weather Radar", ["WXR_SYS_2"] = "Weather Radar",
    };

    /// <summary>The order of each section's panels, the A380's where the panels are shared; a panel listed here belongs to
    /// that section whatever iniBuilds' area (the ISIS, filed under the pedestal, is the Instrument section's as on the
    /// A380). A panel not named here follows them in cockpit file order.</summary>
    private static readonly Dictionary<string, List<string>> PanelOrder = new(StringComparer.Ordinal)
    {
        ["Overhead"] = new()
        {
            "ELEC", "APU", "Fuel", "Hydraulics", "Bleed Air", "Air Conditioning", "Pressurization", "Ventilation", "Cargo Air",
            "Anti Ice", "Fire", "Cargo Smoke", "Cabin Smoke", "Oxygen", "Calls", "Evacuation", "Signs", "Wipers", "ADIRS",
            "Flight Control Computers", "Engine FADEC and Manual Start", "Recorder and Misc", "Surveillance", "Cabin", "OIS",
            "Maintenance", "Interior Lighting", "Exterior Lighting",
        },
        ["Glareshield"] = new() { "FCU", "EFIS Captain", "EFIS First Officer", "Warnings", "HUD", "Loudspeakers and Table Lights" },
        ["Instrument"] = new()
        {
            "Gear", "Autobrake", "ISIS", "Source Switching", "Display Power", "Captain Side Panel", "Captain Individual Panel",
            "First Officer Individual Panel", "First Officer Side Panel",
        },
        ["Pedestal"] = new()
        {
            "Engines", "Thrust Levers", "Captain RMP", "First Officer RMP", "Emergency Descent", "Flaps and Brakes", "Speed Brake",
            "Trim", "ECAM Control Panel", "Weather Radar", "Transponder", "Cockpit", "RMP 3",
        },
        ["Cockpit"] = new() { "Doors and Hatches", "Sun Shades", "Oxygen Masks", "HUD Units", "Tillers and Sidesticks", "Misc" },
    };

    /// <summary>Names for controls whose tooltip title is wrong or shared: iniBuilds titles the engine bleeds
    /// "HOT AIR 1/2" (the hot-air buttons' own titles), engine 2's FADEC ground power "ENG 1", and gives both
    /// wipers, both rain repellents and the four engine fire agents one title per pair.</summary>
    private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
    {
        // Cockpit section: each seat's oxygen mask by seat; the sun shades by place; the HUD units as covers and units.
        ["COCKPIT_DOOR_LOCK"] = "Cockpit door lock",
        ["COCKPIT_DOOR_HANDLE_BASE_CS"] = "Cockpit door",
        ["AVBAY_HATCH_CS"] = "Avionics bay hatch",
        ["CPT_VISOR_MAIN"] = "Captain sun shade",
        ["CPT_VISOR_SIDE"] = "Captain side sun shade",
        ["CPT_VISOR_BACK"] = "Captain rear sun shade",
        ["FO_VISOR_MAIN"] = "First officer sun shade",
        ["FO_VISOR_SIDE"] = "First officer side sun shade",
        ["FO_VISOR_BACK"] = "First officer rear sun shade",
        ["CTR_VISOR_MAIN_HANDLE"] = "Center sun shade",
        ["HUD_LEFT"] = "Captain HUD cover",
        ["HUD_RIGHT"] = "First officer HUD cover",
        ["HUD_HANDLE_LEFT"] = "Captain HUD",
        ["HUD_HANDLE_RIGHT"] = "First officer HUD",
        ["CPT_TILLER_DISCO"] = "Captain tiller",
        ["FO_TILLER_DISCO"] = "First officer tiller",
        ["LIGHTS_DOME_OVERRIDE"] = "Dome light override",
        ["PRINTER_PAPER_SIDE_1_CS"] = "Printer paper tear off",
        ["A350_INT_INI_PEN"] = "Pen",
        ["OXYTEST_CPT_HATCH"] = "Captain oxygen mask stowage",
        ["OXYTEST_CPT_N"] = "Captain oxygen N/100%",
        ["OXYTEST_CPT_PANEL"] = "Captain oxygen mask test",
        ["OXYTEST_CPT_MASK"] = "Captain oxygen emergency pressure",
        ["OXYTEST_FO_HATCH"] = "First officer oxygen mask stowage",
        ["OXYTEST_FO_N"] = "First officer oxygen N/100%",
        ["OXYTEST_FO_PANEL"] = "First officer oxygen mask test",
        ["OXYTEST_FO_MASK"] = "First officer oxygen emergency pressure",
        ["OXYTEST_TO_HATCH"] = "Third occupant oxygen mask stowage",
        ["OXYTEST_TO_N"] = "Third occupant oxygen N/100%",
        ["OXYTEST_TO_PANEL"] = "Third occupant oxygen mask test",
        ["OXYTEST_TO_MASK"] = "Third occupant oxygen emergency pressure",
        ["OXYTEST_4TH_HATCH"] = "Fourth occupant oxygen mask stowage",
        ["OXYTEST_4TH_N"] = "Fourth occupant oxygen N/100%",
        ["OXYTEST_4TH_PANEL"] = "Fourth occupant oxygen mask test",
        ["OXYTEST_4TH_MASK"] = "Fourth occupant oxygen emergency pressure",
        // Pedestal: the A380's ECAM and lever words; the cockpit's own legends elsewhere; each weather radar knob's push
        // (AUTO MODE) and pull (MANUAL MODE) by what they select.
        ["ENG_MASTER_MODE"] = "Engine mode selector",
        ["ENG_MASTER_1"] = "ENG 1 master",
        ["ENG_MASTER_2"] = "ENG 2 master",
        ["ENG_THROTTLE_ATHR_DISC_LEFT"] = "Left A/THR disconnect",
        ["ENG_THROTTLE_ATHR_DISC_RIGHT"] = "Right A/THR disconnect",
        ["EMER_DESCENT"] = "Emergency descent",
        ["PARK_BRK_ACCU_REINFLATE"] = "Brake accumulator reinflate",
        ["GRAVITY_GEAR_COVER"] = "Gravity gear extension cover",
        ["GRAVITY_GEAR_GUARD_1"] = "Gravity gear extension guard 1",
        ["GRAVITY_GEAR_GUARD_2"] = "Gravity gear extension guard 2",
        ["GRAVITY_GEAR_HANDLE"] = "Gravity gear extension",
        ["PITCH_TRIM_LEFT"] = "Left pitch trim",
        ["PITCH_TRIM_RIGHT"] = "Right pitch trim",
        ["ECAM_ENG"] = "ECAM ENG",
        ["ECAM_APU"] = "ECAM APU",
        ["ECAM_BLEED"] = "ECAM BLEED",
        ["ECAM_COND"] = "ECAM COND",
        ["ECAM_PRESS"] = "ECAM PRESS",
        ["ECAM_DOOR"] = "ECAM DOOR",
        ["ECAM_ELAC"] = "ECAM EL/AC",
        ["ECAM_ELDC"] = "ECAM EL/DC",
        ["ECAM_FUEL"] = "ECAM FUEL",
        ["ECAM_WHEEL"] = "ECAM WHEEL",
        ["ECAM_HYD"] = "ECAM HYD",
        ["ECAM_FCTL"] = "ECAM F/CTL",
        ["ECAM_ALL"] = "ECAM all",
        ["ECAM_STS"] = "ECAM status",
        ["ECAM_VIDEO"] = "ECAM video",
        ["ECAM_ABN"] = "ECAM abnormal proc",
        ["ECAM_DEFRD"] = "ECAM deferred proc",
        ["ECAM_DISPCH"] = "ECAM dispatch",
        ["ECAM_MORE"] = "ECAM more",
        ["ECAM_TO_CONFIG"] = "ECAM T.O config",
        ["ECAM_EMER_CANCEL"] = "ECAM emergency cancel",
        ["ECAM_EMER_CANCEL_GUARD"] = "ECAM emergency cancel guard",
        ["ECAM_CLEAR_LEFT"] = "Captain ECAM clear",
        ["ECAM_CLEAR_RIGHT"] = "First officer ECAM clear",
        ["ECAM_CHECKLIST_LEFT"] = "Captain ECAM checklist",
        ["ECAM_CHECKLIST_RIGHT"] = "First officer ECAM checklist",
        ["ECAM_RCL_LAST"] = "ECAM recall last",
        ["ECAM_RCL_ALL"] = "ECAM recall all",
        ["ECAM_SCROLL_L"] = "Captain ECAM wheel",
        ["ECAM_SCROLL_R"] = "First officer ECAM wheel",
        ["ECAM_SD_KNOB"] = "SD knob",
        ["ECAM_SD_KNOB_PUSH"] = "SD knob push",
        ["WXR_SYS_1"] = "Weather radar system 1",
        ["WXR_SYS_2"] = "Weather radar system 2",
        ["XPDR_SYS_1"] = "Transponder system 1",
        ["XPDR_SYS_2"] = "Transponder system 2",
        ["TCAS_ABV"] = "TCAS above",
        ["TCAS_BLW"] = "TCAS below",
        ["TCAS_TA_ONLY"] = "TCAS TA only",
        ["TAWS_GS_MODE_OFF"] = "TAWS G/S mode",
        ["CKPT_DOOR"] = "Cockpit door switch",
        ["CKPT_LT_DOME"] = "Dome light",
        ["CKPT_LT_FLOOD"] = "Flood light",
        ["CKPT_LT_INTEG"] = "Integral light",
        ["CKPT_LT_DU_MASTER_BRT"] = "DU master brightness",
        ["CKPT_LT_DU_MASTER_INHIBIT"] = "DU master",
        ["OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_LEFT"] = "Captain OIS on center",
        ["OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_RIGHT"] = "First officer OIS on center",
        ["OIS_DISPLAY_TRANSFER_DISPLAY_CYCLE_LEFT"] = "Captain display cycle",
        ["OIS_DISPLAY_TRANSFER_DISPLAY_CYCLE_RIGHT"] = "First officer display cycle",
        ["PRINTER_PAPER_PED"] = "Printer",
        ["WXR_ELEV_LEFT"] = "Captain weather radar elevation",
        ["WXR_ELEV_LEFT_PUSH"] = "Captain weather radar elevation auto",
        ["WXR_ELEV_LEFT_PULL"] = "Captain weather radar elevation manual",
        ["WXR_ELEV_RIGHT"] = "First officer weather radar elevation",
        ["WXR_ELEV_RIGHT_PUSH"] = "First officer weather radar elevation auto",
        ["WXR_ELEV_RIGHT_PULL"] = "First officer weather radar elevation manual",
        ["WXR_GAIN_LEFT"] = "Captain weather radar gain",
        ["WXR_GAIN_LEFT_PUSH"] = "Captain weather radar gain auto",
        ["WXR_GAIN_LEFT_PULL"] = "Captain weather radar gain manual",
        ["WXR_GAIN_RIGHT"] = "First officer weather radar gain",
        ["WXR_GAIN_RIGHT_PUSH"] = "First officer weather radar gain auto",
        ["WXR_GAIN_RIGHT_PULL"] = "First officer weather radar gain manual",
        ["WXR_VD_AZIM_LEFT"] = "Captain weather radar VD azimuth",
        ["WXR_VD_AZIM_LEFT_PUSH"] = "Captain weather radar VD azimuth auto",
        ["WXR_VD_AZIM_LEFT_PULL"] = "Captain weather radar VD azimuth manual",
        ["WXR_VD_AZIM_RIGHT"] = "First officer weather radar VD azimuth",
        ["WXR_VD_AZIM_RIGHT_PUSH"] = "First officer weather radar VD azimuth auto",
        ["WXR_VD_AZIM_RIGHT_PULL"] = "First officer weather radar VD azimuth manual",
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
        // Ctrl+M and the announcements are one flat list: a row's name says its system without its panel (the A380's).
        ["CALLS_PURS"] = "Purser call", ["CALLS_AFT"] = "Aft call", ["CALLS_ALL"] = "All call", ["CALLS_EXIT"] = "Exit call",
        ["CALLS_FWD"] = "Fwd call", ["CALLS_MID"] = "Mid call", ["CALLS_EMER"] = "EMER call",
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
        ["LIGHTS_EXT_LANDING"] = "Landing lights", ["LIGHTS_EXT_BEACON"] = "Beacon light", ["LIGHTS_EXT_LOGO"] = "Logo lights",
        ["LIGHTS_EXT_NAV"] = "Nav lights", ["LIGHTS_EXT_NOSE"] = "Nose light", ["LIGHTS_EXT_STROBE"] = "Strobe lights",
        ["LIGHTS_EXT_WING"] = "Wing lights",
        ["APU_MASTER_SWITCH"] = "APU master switch", ["APU_START"] = "APU start",
        ["OXYGEN_CREW_SUPPLY"] = "Crew oxygen supply",
        ["CARGO_AIR_COND_HEATER"] = "Cargo heater",
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
        ["FUEL_TANK_TRANSFER_LEFT"] = "Left fuel transfer", ["FUEL_TANK_TRANSFER_RIGHT"] = "Right fuel transfer",
        ["FUEL_CTR_TANK_FEED"] = "Center tank feed",
        ["VENT_AVNCS_EXTRACT"] = "Avionics extract", ["VENT_CABIN_FANS"] = "Cabin fans", ["VENT_COOLING"] = "Avionics cooling",
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
        // The baro knob's push and pull by what they do, iniBuilds' tooltips (PUSH "STD VALUE", PULL "SET QNH"): the
        // A320's opposite, so the name must say it (live 2026-10-28, a pull for STD set nothing).
        ["EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH"] = "Captain baro STD (push)", ["EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL"] = "Captain baro QNH (pull)",
        ["EFIS_QNH_PRESSURE_SELECTOR_LEFT"] = "Captain baro knob", ["EFIS_QNH_UNIT_SELECTOR_LEFT"] = "Captain baro unit",
        ["EFIS_TSK_1_LEFT"] = "Captain CSTR", ["EFIS_TSK_2_LEFT"] = "Captain WPT", ["EFIS_TSK_3_LEFT"] = "Captain VORD",
        ["EFIS_TSK_4_LEFT"] = "Captain NDB", ["EFIS_TSK_5_LEFT"] = "Captain ARPT",
        ["EFIS_BSK_1_LEFT"] = "Captain VOR 1", ["EFIS_BSK_2_LEFT"] = "Captain WX", ["EFIS_BSK_3_LEFT"] = "Captain TERR",
        ["EFIS_BSK_4_LEFT"] = "Captain TRAF", ["EFIS_BSK_5_LEFT"] = "Captain VOR 2",
        ["EFIS_LS_LEFT"] = "Captain LS", ["EFIS_TAXI_LEFT"] = "Captain taxi", ["EFIS_VV_LEFT"] = "Captain VV",
        ["EFIS_MODE_RIGHT"] = "First officer ND mode", ["EFIS_RANGE_RIGHT"] = "First officer ND range",
        ["EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH"] = "First officer baro STD (push)",
        ["EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL"] = "First officer baro QNH (pull)",
        ["EFIS_QNH_PRESSURE_SELECTOR_RIGHT"] = "First officer baro knob", ["EFIS_QNH_UNIT_SELECTOR_RIGHT"] = "First officer baro unit",
        ["EFIS_TSK_1_RIGHT"] = "First officer CSTR", ["EFIS_TSK_2_RIGHT"] = "First officer WPT", ["EFIS_TSK_3_RIGHT"] = "First officer VORD",
        ["EFIS_TSK_4_RIGHT"] = "First officer NDB", ["EFIS_TSK_5_RIGHT"] = "First officer ARPT",
        ["EFIS_BSK_1_RIGHT"] = "First officer VOR 1", ["EFIS_BSK_2_RIGHT"] = "First officer WX", ["EFIS_BSK_3_RIGHT"] = "First officer TERR",
        ["EFIS_BSK_4_RIGHT"] = "First officer TRAF", ["EFIS_BSK_5_RIGHT"] = "First officer VOR 2",
        ["EFIS_LS_RIGHT"] = "First officer LS", ["EFIS_TAXI_RIGHT"] = "First officer taxi", ["EFIS_VV_RIGHT"] = "First officer VV",
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
        // Instrument panel: the A380's names where it has the control (Gear lever, Anti-skid), the placards elsewhere.
        ["LDG_LEVER"] = "Gear lever", ["LDG_ANTI_SKID"] = "Anti-skid",
        ["LDG_RWY_COND_BRK_ACTION_SELECTOR_PUSH"] = "Runway condition knob push",
        ["LDG_RWY_COND_BRK_ACTION_SELECTOR_PULL"] = "Runway condition knob pull",
        ["LDG_RWY_COND_BRK_ACTION_MODE_SELECTOR"] = "Runway condition or braking action",
        ["LDG_AUTO_BRK"] = "A/BRK",
        ["ADIRS_FMS_FMS_SELECTOR"] = "FMS source", ["ADIRS_FMS_AIR_DATA_SELECTOR"] = "Air data source",
        // Display power: each display by what goes dark without it (live 2026-10-26, through the display reader and the
        // OIS's INI_EFB1/EFB2_STATE and INI_MFD_STATE); every switch's tooltip is "ON/OFF".
        ["DISPLAY_EFB_LEFT_PWR"] = "Captain OIS display", ["DISPLAY_EFB_RIGHT_PWR"] = "First officer OIS display",
        ["DISPLAY_EFIS_LEFT_PWR"] = "Captain outer display", ["DISPLAY_EFIS_RIGHT_PWR"] = "First officer outer display",
        ["DISPLAY_PFDND_MFD_PWR"] = "Center lower display", ["DISPLAY_SD_PWR"] = "Center upper display",
        // Side and individual panels: each side's name carries its side, as the glareshield's twins do (Ctrl+M lists both).
        ["MIP_PEDAL_LT_LEFT"] = "Captain pedal light", ["MIP_PEDAL_LT_RIGHT"] = "First officer pedal light",
        ["OIS_AVNCS_EFB_LEFT"] = "Captain OIS switch", ["OIS_AVNCS_EFB_RIGHT"] = "First officer OIS switch",
        ["OIS_VIEW_OFFSIDE_LEFT"] = "Captain OIS view offside", ["OIS_VIEW_OFFSIDE_RIGHT"] = "First officer OIS view offside",
        ["MIP_CONSOLE_BRT_LEFT"] = "Captain console light", ["MIP_CONSOLE_BRT_RIGHT"] = "First officer console light",
        ["MIP_MAP_BRT_LEFT"] = "Captain map light", ["MIP_MAP_BRT_RIGHT"] = "First officer map light",
        ["INDIV_LAT_VENT_DEFOG_LEFT"] = "Captain lateral vent and defog", ["INDIV_LAT_VENT_DEFOG_RIGHT"] = "First officer lateral vent and defog",
        ["INDIV_FOOT_VENT_LEFT"] = "Captain foot vent", ["INDIV_FOOT_VENT_RIGHT"] = "First officer foot vent",
        ["INDIV_HEATER_LEFT"] = "Captain heater", ["INDIV_HEATER_RIGHT"] = "First officer heater",
        // ISIS: both units' keys on one panel, by side.
        ["ISIS_KNOB_BUTTON_1"] = "Captain STD/CHANGE", ["ISIS_LS_1"] = "Captain LS/DIR TO", ["ISIS_MENU_1"] = "Captain menu",
        ["ISIS_MINUS_1"] = "Captain minus", ["ISIS_MODE_1"] = "Captain mode", ["ISIS_PLUS_1"] = "Captain plus",
        ["ISIS_KNOB_BUTTON_2"] = "First officer STD/CHANGE", ["ISIS_LS_2"] = "First officer LS/DIR TO",
        ["ISIS_MENU_2"] = "First officer menu", ["ISIS_MINUS_2"] = "First officer minus", ["ISIS_MODE_2"] = "First officer mode",
        ["ISIS_PLUS_2"] = "First officer plus", ["ISIS_KNOB_2"] = "First officer set/sel knob",
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

    /// <summary>The OIS switch's markings; declared before <see cref="PositionOverrides"/>, which reads it as it is built.</summary>
    private static readonly Dictionary<double, string> OisSwitch = new() { [0] = "EFB", [1] = "Neutral", [2] = "AVNCS" };

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
        ["LDG_RWY_COND_BRK_ACTION_MODE_SELECTOR"] = new() { [0] = "Runway condition", [1] = "Braking action" },
        ["ADIRS_FMS_AIR_DATA_SELECTOR"] = new() { [0] = "Captain on backup", [1] = "Auto", [2] = "First officer on backup" },
        ["OIS_AVNCS_EFB_LEFT"] = OisSwitch, ["OIS_AVNCS_EFB_RIGHT"] = OisSwitch,
        // iniBuilds' master tooltips are swapped (the OFF key reads ON): both engines running read 1 (live 2026-10-27).
        ["ENG_MASTER_1"] = new() { [0] = "Off", [1] = "On" }, ["ENG_MASTER_2"] = new() { [0] = "Off", [1] = "On" },
        // The cover's tooltip words are the click's action. The handle's tooltip reads state n as key n+1 and iniBuilds has
        // no third key, so its words come from what it does: 2 is DOWN (taken only with the cover and both guards at 1),
        // and it rests at 1, OFF (live 2026-10-27).
        ["GRAVITY_GEAR_COVER"] = new() { [0] = "Closed", [1] = "Open" },
        ["GRAVITY_GEAR_GUARD_1"] = new() { [0] = "Closed", [1] = "Open" },
        ["GRAVITY_GEAR_GUARD_2"] = new() { [0] = "Closed", [1] = "Open" },
        ["GRAVITY_GEAR_HANDLE"] = new() { [0] = "Reset", [1] = "Off", [2] = "Down" },
        // The left switch's tooltip reads Off/On, the right's NORM/X-WIND: both read the right's.
        ["FCU_HUD_XWIND_LEFT"] = new() { [0] = "Norm", [1] = "X-wind" }, ["FCU_HUD_XWIND_RIGHT"] = new() { [0] = "Norm", [1] = "X-wind" },
        // Cockpit section: the mask stowages' tooltips name the click (they rest closed at 0); the HUD handle's too (1 drew
        // the HUD); both tiller disconnects flip one INI_TILLER_DISC; locking the door closes it (its set code).
        ["COCKPIT_DOOR_LOCK"] = new() { [0] = "Unlocked", [1] = "Locked" },
        ["COCKPIT_DOOR_HANDLE_BASE_CS"] = new() { [0] = "Closed", [1] = "Open" },
        ["AVBAY_HATCH_CS"] = new() { [0] = "Closed", [1] = "Open" },
        ["HUD_HANDLE_LEFT"] = new() { [0] = "Stowed", [1] = "Deployed" },
        ["HUD_HANDLE_RIGHT"] = new() { [0] = "Stowed", [1] = "Deployed" },
        ["CPT_TILLER_DISCO"] = new() { [0] = "Connected", [1] = "Disconnected" },
        ["FO_TILLER_DISCO"] = new() { [0] = "Connected", [1] = "Disconnected" },
        ["OXYTEST_CPT_HATCH"] = new() { [0] = "Closed", [1] = "Open" },
        ["OXYTEST_CPT_N"] = new() { [0] = "Normal", [1] = "100%" },
        ["OXYTEST_FO_HATCH"] = new() { [0] = "Closed", [1] = "Open" },
        ["OXYTEST_FO_N"] = new() { [0] = "Normal", [1] = "100%" },
        ["OXYTEST_TO_HATCH"] = new() { [0] = "Closed", [1] = "Open" },
        ["OXYTEST_TO_N"] = new() { [0] = "Normal", [1] = "100%" },
        ["OXYTEST_4TH_HATCH"] = new() { [0] = "Closed", [1] = "Open" },
        ["OXYTEST_4TH_N"] = new() { [0] = "Normal", [1] = "100%" },
    };
}
