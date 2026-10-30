using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.A350;

using Item = Models.ChecklistItem<A350ActionExecutor, A350StateEvaluator>;
using Group = Models.ChecklistGroup<A350ActionExecutor, A350StateEvaluator>;
using CheckFn = System.Func<A350ActionExecutor, A350StateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The iniBuilds A350's First Officer checklists, two kinds of group in flying order:
/// - the state and action groups mirror <see cref="A350FlowDefinitions"/>: a drivable step is an <c>Auto</c> line whose tick fires the
///   same write and whose auto-detect mirrors the step's Skip; a Captain step is a <see cref="Reminder(string, string, string)"/>;
/// - the read-back (*_CL) groups are the aircraft's own checklists (the MFD's C/L pages, A35K.CHECKLISTS, 1.2.7), in its order and its
///   words spelled out for the screen reader, spoken to one pilot, every blank answered SET with the value the First Officer reads
///   where it reads one. Action-free (FO-3).
/// </summary>
public static class A350ChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        BuildCockpitPrep(),
        BuildBeforeStart(),
        BuildBeforeStartCL(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildAfterStartCL(),
        BuildTaxi(),
        BuildLineup(),
        BuildBeforeTakeoffCL(),
        BuildAfterTakeoff(),
        BuildAfterTakeoffCL(),
        BuildApproach(),
        BuildApproachCL(),
        BuildLanding(),
        BuildLandingCL(),
        BuildAfterLanding(),
        BuildAfterLandingCL(),
        BuildParking(),
        BuildParkingCL(),
        BuildSecure(),
        BuildSecuringCL(),
    };

    private static Group BuildCockpitPrep() => new()
    {
        Id = "COCKPIT_PREP", Name = "Cockpit Preparation",
        Items = new()
        {
            Multi("CP_WIPERS", "COCKPIT_PREP", "Wipers: OFF", "A350_WIPER_LEFT", v => Math.Abs(v - 3) < 0.5, new[] { "A350_WIPER_RIGHT" },
                async (e, _) => { await e.Set("A350_WIPER_LEFT", 3); await e.Set("A350_WIPER_RIGHT", 3); }),
            Auto("CP_GEAR", "COCKPIT_PREP", "Gear lever: DOWN", "A350_LDG_LEVER", v => v > 0.5, (e, _) => e.Set("A350_LDG_LEVER", 1)),
            Reminder("CP_FLAPS", "COCKPIT_PREP", "Flaps: confirm up"),
            Auto("CP_SPOILERS", "COCKPIT_PREP", "Spoilers: disarmed", "A350_SPOILERS_ARM", v => v < 0.5, (e, _) => e.Set("A350_SPOILERS_ARM", 0)),
            Auto("CP_PARKBRK", "COCKPIT_PREP", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", v => v > 0.5, (e, _) => e.Set("A350_PARK_BRK_PARKING_BRAKE", 1)),
            Reminder("CP_THROTTLES", "COCKPIT_PREP", "Confirm thrust levers idle"),
            Auto("CP_ENGMODE", "COCKPIT_PREP", "Engine mode: NORM", "A350_ENG_MASTER_MODE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_ENG_MASTER_MODE", 1)),
            // A cold-and-dark check: a tick switches a master off only with both engines read stopped, on the ground (FO-25).
            LeaveAlone(Multi("CP_MASTERS_OFF", "COCKPIT_PREP", "Engine masters 1 and 2: OFF", "A350_ENG_MASTER_1", v => v < 0.5, new[] { "A350_ENG_MASTER_2" },
                async (e, _) => { await e.Set("A350_ENG_MASTER_1", 0); await e.Set("A350_ENG_MASTER_2", 0); }),
                NotParked, EnginesTurningText),
            Multi("CP_BAT", "COCKPIT_PREP", "Batteries: ON", "A350_ELEC_BAT_1", v => v > 0.5, new[] { "A350_ELEC_BAT_2", "A350_ELEC_BAT_EMER_1", "A350_ELEC_BAT_EMER_2" },
                async (e, _) =>
                {
                    await e.Set("A350_ELEC_BAT_1", 1);
                    await e.Set("A350_ELEC_BAT_2", 1);
                    await e.Set("A350_ELEC_BAT_EMER_1", 1);
                    await e.Set("A350_ELEC_BAT_EMER_2", 1);
                }),
            Auto("CP_COCKPITLT", "COCKPIT_PREP", "Cockpit lights: set", "A350_LIGHTS_INT_ANN_LT", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_LIGHTS_INT_ANN_LT", 1)),
            Reminder("CP_GPU_OIS", "COCKPIT_PREP", "Ground power: connect it on the OIS GROUND page if you want it"),
            Auto("CP_GPU", "COCKPIT_PREP", "Ground power: ON", A350StateEvaluator.ExtOnField, v => v > 0.5, (e, _) => e.Set(A350ActionExecutor.ExternalPowerKey, 1)),
            Reminder("CP_INSTRBRT", "COCKPIT_PREP", "Display brightness: set"),
            Multi("CP_ADIRS", "COCKPIT_PREP", "ADIRS: NAV", "A350_IRS_MODE_1", v => Math.Abs(v - 1) < 0.5, new[] { "A350_IRS_MODE_2", "A350_IRS_MODE_3" },
                async (e, _) => { await e.Set("A350_IRS_MODE_1", 1); await e.Set("A350_IRS_MODE_2", 1); await e.Set("A350_IRS_MODE_3", 1); }),
            Auto("CP_OXY", "COCKPIT_PREP", "Crew oxygen: ON", "A350_OXYGEN_CREW_SUPPLY", v => v > 0.5, (e, _) => e.Set("A350_OXYGEN_CREW_SUPPLY", 1)),
            ActionManual("CP_FIRETEST", "COCKPIT_PREP", "Fire test", (e, _) => e.FireTestAsync()),
            Auto("CP_GNDCTL", "COCKPIT_PREP", "Recorder ground control: ON", "A350_RCD_RCDR_GND_CTRL", v => v > 0.5, (e, _) => e.Set("A350_RCD_RCDR_GND_CTRL", 1)),
            Auto("CP_NAVLOGO", "COCKPIT_PREP", "Nav and logo lights: ON", A350StateEvaluator.NavLogoOnField, v => v > 0.5,
                async (e, _) => { await e.Set("A350_LIGHTS_EXT_NAV", 0); await e.Set("A350_LIGHTS_EXT_LOGO", 1); }),
            Auto("CP_EXTLT", "COCKPIT_PREP", "Strobe lights: AUTO. Beacon, landing and nose lights: OFF", A350StateEvaluator.PrepLightsField, v => v > 0.5,
                async (e, _) =>
                {
                    await e.Set("A350_LIGHTS_EXT_STROBE", 1);
                    await e.Set("A350_LIGHTS_EXT_BEACON", 0);
                    await e.Set("A350_LIGHTS_EXT_LANDING", 0);
                    await e.Set("A350_LIGHTS_EXT_NOSE", 2);
                }),
            Auto("CP_SEATBELT", "COCKPIT_PREP", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, (e, _) => e.Set("A350_SIGNS_SEAT_BELTS", 0)),
            Multi("CP_NOSMOKE", "COCKPIT_PREP", "No smoking and no mobile signs: AUTO", "A350_SIGNS_NO_SMOKING", v => Math.Abs(v - 1) < 0.5, new[] { "A350_SIGNS_NO_MOBILE" },
                async (e, _) => { await e.Set("A350_SIGNS_NO_SMOKING", 1); await e.Set("A350_SIGNS_NO_MOBILE", 1); }),
            Auto("CP_EMEREXIT", "COCKPIT_PREP", "Emergency exit lighting: ARM", "A350_SIGNS_EMER_EXIT", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_SIGNS_EMER_EXIT", 1)),
            Reminder("CP_LTSTEST", "COCKPIT_PREP", "Lights test"),
            Auto("CP_WINGAI", "COCKPIT_PREP", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", v => v < 0.5, (e, _) => e.Set("A350_ANTI_ICE_WING", 0)),
            Auto("CP_WINGLT", "COCKPIT_PREP", "Wing lights: OFF", "A350_LIGHTS_EXT_WING", v => v < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_WING", 0)),
            Multi("CP_PACKS", "COCKPIT_PREP", "Packs: ON", "A350_AIR_PACK_1", v => v > 0.5, new[] { "A350_AIR_PACK_2" },
                async (e, _) => { await e.Set("A350_AIR_PACK_1", 1); await e.Set("A350_AIR_PACK_2", 1); }),
            Auto("CP_XBLEED", "COCKPIT_PREP", "Crossbleed: AUTO", "A350_AIR_X_BLEED", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_AIR_X_BLEED", 1)),
            Auto("CP_PACKFLOW", "COCKPIT_PREP", "Air flow: NORMAL", "A350_AIR_FLOW", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_AIR_FLOW", 2)),
            Multi("CP_HOTAIR", "COCKPIT_PREP", "Hot air: ON", "A350_AIR_HOT_AIR_1", v => v > 0.5, new[] { "A350_AIR_HOT_AIR_2" },
                async (e, _) => { await e.Set("A350_AIR_HOT_AIR_1", 1); await e.Set("A350_AIR_HOT_AIR_2", 1); }),
            Reminder("CP_AIRTEMP", "COCKPIT_PREP", "Cabin temperature: set as required"),
            Auto("CP_BARO", "COCKPIT_PREP", "Baro reference: hectopascals", A350StateEvaluator.BaroUnitHectopascalsField, v => v > 0.5, (e, _) => e.Set(A350ActionExecutor.BaroUnitHectopascalsKey, 1)),
            Reminder("CP_ALTIMETERS", "COCKPIT_PREP", "Altimeters and standby instrument: set QNH"),
            Auto("CP_ANTISKID", "COCKPIT_PREP", "Anti-skid: ON", "A350_LDG_ANTI_SKID", v => v > 0.5, (e, _) => e.Set("A350_LDG_ANTI_SKID", 1)),
            Reminder("CP_ALTRPT", "COCKPIT_PREP", "Altitude reporting: on (MFD SURV page)"),
            Reminder("CP_TCASMODE", "COCKPIT_PREP", "TCAS mode: standby (MFD SURV page)"),
            Reminder("CP_XPDR", "COCKPIT_PREP", "Transponder: standby (MFD SURV page)"),
            Multi("CP_EFISMODE", "COCKPIT_PREP", "EFIS mode: ARC", "A350_EFIS_MODE_LEFT", v => Math.Abs(v - 3) < 0.5, new[] { "A350_EFIS_MODE_RIGHT" },
                async (e, _) => { await e.Set("A350_EFIS_MODE_LEFT", 3); await e.Set("A350_EFIS_MODE_RIGHT", 3); }),
            Multi("CP_EFISRANGE", "COCKPIT_PREP", "EFIS range: 40", "A350_EFIS_RANGE_LEFT", v => Math.Abs(v - 7) < 0.5, new[] { "A350_EFIS_RANGE_RIGHT" },
                async (e, _) => { await e.Set("A350_EFIS_RANGE_LEFT", 7); await e.Set("A350_EFIS_RANGE_RIGHT", 7); }),
            Auto("CP_FD", "COCKPIT_PREP", "Flight directors: ON", "A350_FCU_FD", v => v > 0.5, (e, _) => e.Set("A350_FCU_FD", 1)),
            Reminder("CP_CLOCK", "COCKPIT_PREP", "Clock: reset"),
            Auto("CP_ECAMPAGE", "COCKPIT_PREP", "ECAM page: door", "A350_SD_PAGE", v => Math.Abs(v - 11) < 0.5, (e, _) => e.Set("A350_SD_PAGE", 11)),
            Reminder("CP_IFR", "COCKPIT_PREP", "Obtain IFR clearance"),
            Reminder("CP_PAYLOAD", "COCKPIT_PREP", "Load fuel and payload on the OIS"),
            Reminder("CP_MFD", "COCKPIT_PREP", "Program the MFD"),
        }
    };

    private static Group BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Items = new()
        {
            Auto("BS_COCKPITDOOR", "BEFORE_START", "Cockpit door: CLOSED and LOCKED", A350StateEvaluator.CockpitDoorSecureField, v => v > 0.5,
                async (e, _) => { await e.Set("A350_COCKPIT_DOOR_HANDLE_BASE_CS", 0); await e.Set("A350_COCKPIT_DOOR_LOCK", 1); }),
            ActionManual("BS_FCUSPD", "BEFORE_START", "FCU speed: managed", (e, _) => e.Set("A350_FCU_SPD_KNOB_PUSH", 1)),
            ActionManual("BS_FCUHDG", "BEFORE_START", "FCU heading: managed", (e, _) => e.Set("A350_FCU_HDG_KNOB_PUSH", 1)),
            Reminder("BS_ALT", "BEFORE_START", "Set cleared altitude on the FCU"),
            ActionManual("BS_FCUALT", "BEFORE_START", "FCU altitude: pushed", (e, _) => e.Set("A350_FCU_ALT_KNOB_PUSH", 1)),
            Auto("BS_ECAMPAGE", "BEFORE_START", "ECAM page: APU", "A350_SD_PAGE", v => Math.Abs(v - 9) < 0.5, (e, _) => e.Set("A350_SD_PAGE", 9)),
            // The master alone does not start it: the tick also presses START unless AVAIL is lit. Pilot-paced, so no wait for AVAIL.
            Auto("BS_APU", "BEFORE_START", "APU: ON", "A350_APU_MASTER_SWITCH", v => v > 0.5, StartApuAsync),
            Auto("BS_APUBLEED", "BEFORE_START", "APU bleed: ON", "A350_AIR_APU_BLEED", v => v > 0.5, (e, _) => e.Set("A350_AIR_APU_BLEED", 1)),
            Auto("BS_GPU_OFF", "BEFORE_START", "Ground power: OFF", A350StateEvaluator.ExtOffField, v => v > 0.5, (e, _) => e.Set(A350ActionExecutor.ExternalPowerKey, 0)),
            Reminder("BS_GPU_DISC", "BEFORE_START", "Ground power and chocks: remove on the OIS GROUND page"),
            Multi("BS_FUELPUMPS", "BEFORE_START", "Fuel pumps: ON", A350FlowDefinitions.FuelPumps[0], v => v > 0.5, A350FlowDefinitions.FuelPumps.Skip(1).ToArray(),
                async (e, _) =>
                {
                    await e.Set("A350_FUEL_PUMP_LEFT_TANK_MAIN", 1);
                    await e.Set("A350_FUEL_PUMP_LEFT_TANK_STBY", 1);
                    await e.Set("A350_FUEL_PUMP_RIGHT_TANK_MAIN", 1);
                    await e.Set("A350_FUEL_PUMP_RIGHT_TANK_STBY", 1);
                    await e.Set("A350_FUEL_PUMP_CTR_TANK_LEFT", 1);
                    await e.Set("A350_FUEL_PUMP_CTR_TANK_RIGHT", 1);
                }),
            Auto("BS_BEACON", "BEFORE_START", "Beacon: ON", "A350_LIGHTS_EXT_BEACON", v => v > 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_BEACON", 1)),
            Auto("BS_PARKBRK", "BEFORE_START", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", v => v > 0.5, (e, _) => e.Set("A350_PARK_BRK_PARKING_BRAKE", 1)),
            Reminder("BS_SURV", "BEFORE_START", "Surveillance: default settings (MFD SURV page)"),
            Reminder("BS_PERF", "BEFORE_START", "Check the MFD PERF page"),
            Reminder("BS_TRIM", "BEFORE_START", "Set trim"),
            Reminder("BS_DOORS", "BEFORE_START", "Doors: close all on the OIS GROUND page"),
            Reminder("BS_TAXICLR", "BEFORE_START", "Obtain pushback and start clearance"),
        }
    };

    private static Group BuildBeforeStartCL() => new()
    {
        Id = "BEFORE_START_CL", Name = "Before Start Checklist",
        Items = new()
        {
            Reminder("BSC_PREP", "BEFORE_START_CL", "Cockpit preparation: COMPLETED"),
            Reminder("BSC_PINS", "BEFORE_START_CL", "Gear pins and covers: REMOVED"),
            Reminder("BSC_FUEL", "BEFORE_START_CL", "Fuel quantity: SET", s => s.FuelText()),
            Auto("BSC_SEATBELT", "BEFORE_START_CL", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, action: null),
            Auto("BSC_ADIRS", "BEFORE_START_CL", "ADIRS: NAV", A350StateEvaluator.AdirsNavField, v => v > 0.5, action: null),
            Reminder("BSC_BARO", "BEFORE_START_CL", "Baro reference: SET", s => s.BaroText()),
            Reminder("BSC_TODATA", "BEFORE_START_CL", "T.O data: SET", s => s.TakeoffDataText()),
            Reminder("BSC_DOORS", "BEFORE_START_CL", "Doors: CLOSED and ARMED"),
            Auto("BSC_THRUST", "BEFORE_START_CL", "Thrust levers: IDLE", A350StateEvaluator.LeversIdleField, v => v > 0.5, action: null),
            Reminder("BSC_PARKBRK", "BEFORE_START_CL", "Parking brake: AS REQUIRED", s => s.ParkingBrakeText()),
            Auto("BSC_BEACON", "BEFORE_START_CL", "Beacon: ON", "A350_LIGHTS_EXT_BEACON", v => v > 0.5, action: null),
        }
    };

    private static Group BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Items = new()
        {
            Auto("ES_ECAMPAGE", "ENGINE_START", "ECAM page: engine", "A350_SD_PAGE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_SD_PAGE", 1)),
            Auto("ES_APUBLEED", "ENGINE_START", "APU bleed: ON", "A350_AIR_APU_BLEED", v => v > 0.5, (e, _) => e.Set("A350_AIR_APU_BLEED", 1)),
            Auto("ES_ENGMODE", "ENGINE_START", "Engine mode: IGN", "A350_ENG_MASTER_MODE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_ENG_MASTER_MODE", 2)),
            Auto("ES_ENG1", "ENGINE_START", "Engine 1 master: ON", "A350_ENG_MASTER_1", v => v > 0.5, (e, _) => e.Set("A350_ENG_MASTER_1", 1)),
            Auto("ES_ENG2", "ENGINE_START", "Engine 2 master: ON", "A350_ENG_MASTER_2", v => v > 0.5, (e, _) => e.Set("A350_ENG_MASTER_2", 1)),
        }
    };

    private static Group BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Items = new()
        {
            Auto("AS_ENGMODE", "AFTER_START", "Engine mode: NORM", "A350_ENG_MASTER_MODE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_ENG_MASTER_MODE", 1)),
            Reminder("AS_ANTIICE", "AFTER_START", "Anti-ice: set as required"),
            Auto("AS_ECAMPAGE", "AFTER_START", "ECAM page: APU", "A350_SD_PAGE", v => Math.Abs(v - 9) < 0.5, (e, _) => e.Set("A350_SD_PAGE", 9)),
            Auto("AS_APU_OFF", "AFTER_START", "APU: OFF", "A350_APU_MASTER_SWITCH", v => v < 0.5, (e, _) => e.Set("A350_APU_MASTER_SWITCH", 0)),
            Auto("AS_APUBLEED_OFF", "AFTER_START", "APU bleed: OFF", "A350_AIR_APU_BLEED", v => v < 0.5, (e, _) => e.Set("A350_AIR_APU_BLEED", 0)),
            Auto("AS_NOSE_TAXI", "AFTER_START", "Nose light: TAXI", "A350_LIGHTS_EXT_NOSE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_NOSE", 1)),
            Auto("AS_COCKPITLT", "AFTER_START", "Cockpit lights: annunciator dim", "A350_LIGHTS_INT_ANN_LT", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_LIGHTS_INT_ANN_LT", 2)),
            LeaveAlone(Auto("AS_SPOILERS_ARM", "AFTER_START", "Spoilers: ARMED", "A350_SPOILERS_ARM", v => v > 0.5, (e, _) => e.Set("A350_SPOILERS_ARM", 1)),
                A350StateEvaluator.SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
            ActionManual("AS_RUDDERTRIM", "AFTER_START", "Rudder trim: RESET", (e, _) => e.Set("A350_RUDDER_TRIM_RESET", 1)),
            Reminder("AS_FLAPS", "AFTER_START", "Flaps: set for takeoff"),
        }
    };

    private static Group BuildAfterStartCL() => new()
    {
        Id = "AFTER_START_CL", Name = "After Start Checklist",
        Items = new()
        {
            Reminder("ASC_ANTIICE", "AFTER_START_CL", "Anti-ice: AS REQUIRED", s => s.AntiIceText()),
            Reminder("ASC_STATUS", "AFTER_START_CL", "ECAM status: NORMAL"),
            Reminder("ASC_PITCHTRIM", "AFTER_START_CL", "Pitch trim: T.O"),
            Auto("ASC_RUDDERTRIM", "AFTER_START_CL", "Rudder trim: NEUTRAL", "A350_STAT_RUDDER_TRIM", A350FlowDefinitions.RudderTrimNeutral, action: null),
        }
    };

    private static Group BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Items = new()
        {
            Reminder("TX_FCTL", "TAXI", "Check the flight controls"),
            // A/BRK flips: the executor presses only with the armed state read off, so a re-tick never disarms.
            Auto("TX_AUTOBRAKE", "TAXI", "Autobrake: RTO", A350StateEvaluator.AutoBrakeArmedField, v => v > 0.5, (e, _) => e.Set("A350_LDG_AUTO_BRK", 1)),
            Auto("TX_ENGMODE", "TAXI", "Engine mode: NORM", "A350_ENG_MASTER_MODE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_ENG_MASTER_MODE", 1)),
            Auto("TX_WXR", "TAXI", "Weather radar: ON", A350StateEvaluator.WeatherRadarOnField, v => v > 0.5,
                async (e, _) => { await e.Set("A350_EFIS_BSK_2_LEFT", 1); await e.Set("A350_EFIS_BSK_2_RIGHT", 1); }),
            Reminder("TX_PWS", "TAXI", "Predictive windshear: on (MFD SURV page)"),
            Reminder("TX_FLAPS", "TAXI", "Flaps: set for takeoff"),
            Auto("TX_ECAMPAGE", "TAXI", "ECAM page: flight controls", "A350_SD_PAGE", v => Math.Abs(v - 10) < 0.5, (e, _) => e.Set("A350_SD_PAGE", 10)),
            ActionManual("TX_CONFIG", "TAXI", "Takeoff config test", (e, _) => e.TakeoffConfigTestAsync()),
        }
    };

    private static Group BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Lineup",
        Items = new()
        {
            Reminder("LU_TCAS", "LINEUP", "TCAS mode: TA/RA (MFD SURV page)"),
            Auto("LU_STROBE", "LINEUP", "Strobe lights: ON", "A350_LIGHTS_EXT_STROBE", v => v < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_STROBE", 0)),
            Auto("LU_LANDING", "LINEUP", "Landing and nose lights: ON", A350StateEvaluator.LineupLightsField, v => v > 0.5,
                async (e, _) => { await e.Set("A350_LIGHTS_EXT_LANDING", 1); await e.Set("A350_LIGHTS_EXT_NOSE", 0); }),
            ActionManual("LU_CABIN", "LINEUP", "Advise the cabin crew for takeoff (call all)", (e, _) => e.Set("A350_CALLS_ALL", 1)),
        }
    };

    private static Group BuildBeforeTakeoffCL() => new()
    {
        Id = "BEFORE_TAKEOFF_CL", Name = "Before Takeoff Checklist",
        Items = new()
        {
            Reminder("BTC_FCTL", "BEFORE_TAKEOFF_CL", "Flight controls: CHECKED"),
            Reminder("BTC_INSTR", "BEFORE_TAKEOFF_CL", "Flight instruments: CHECKED"),
            Reminder("BTC_BRIEF", "BEFORE_TAKEOFF_CL", "Briefing: CONFIRMED"),
            Reminder("BTC_FLAPSET", "BEFORE_TAKEOFF_CL", "Flaps setting: SET", s => s.FlapsText()),
            Reminder("BTC_SPEEDS", "BEFORE_TAKEOFF_CL", "V1, VR, V2 and thrust rating: SET", s => s.TakeoffDataText()),
            Reminder("BTC_SQUAWK", "BEFORE_TAKEOFF_CL", "Squawk: SET"),
            Reminder("BTC_RWY", "BEFORE_TAKEOFF_CL", "T.O runway: CONFIRMED"),
            Reminder("BTC_CREW", "BEFORE_TAKEOFF_CL", "Cabin crew: ADVISED"),
            Auto("BTC_AUTOBRK", "BEFORE_TAKEOFF_CL", "Autobrake: RTO", A350StateEvaluator.AutoBrakeArmedField, v => v > 0.5, action: null),
            Auto("BTC_SEATBELT", "BEFORE_TAKEOFF_CL", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, action: null),
            Auto("BTC_SPLRS", "BEFORE_TAKEOFF_CL", "Ground spoilers: ARM", "A350_SPOILERS_ARM", v => v > 0.5, action: null),
            Auto("BTC_FLAPS", "BEFORE_TAKEOFF_CL", "Flaps: T.O", A350StateEvaluator.TakeoffFlapsField, v => v > 0.5, action: null),
            Reminder("BTC_CONFIG", "BEFORE_TAKEOFF_CL", "T.O config: NORMAL"),
            Reminder("BTC_CABIN", "BEFORE_TAKEOFF_CL", "Cabin: READY"),
            Reminder("BTC_PACKS", "BEFORE_TAKEOFF_CL", "Packs: OFF if required", s => s.PacksText()),
        }
    };

    private static Group BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Items = new()
        {
            Auto("AT_SPOILERS_DISARM", "AFTER_TAKEOFF", "Spoilers: DISARM", "A350_SPOILERS_ARM", v => v < 0.5, (e, _) => e.Set("A350_SPOILERS_ARM", 0)),
            Auto("AT_AUTOBRAKE", "AFTER_TAKEOFF", "Autobrake: OFF", A350StateEvaluator.AutoBrakeArmedField, v => v < 0.5, (e, _) => e.Set("A350_LDG_AUTO_BRK", 0)),
            Auto("AT_NOSE_OFF", "AFTER_TAKEOFF", "Nose light: OFF", "A350_LIGHTS_EXT_NOSE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_NOSE", 2)),
            Multi("AT_PACKS", "AFTER_TAKEOFF", "Packs: ON", "A350_AIR_PACK_1", v => v > 0.5, new[] { "A350_AIR_PACK_2" },
                async (e, _) => { await e.Set("A350_AIR_PACK_1", 1); await e.Set("A350_AIR_PACK_2", 1); }),
        }
    };

    private static Group BuildAfterTakeoffCL() => new()
    {
        Id = "AFTER_TAKEOFF_CL", Name = "After Takeoff Checklist",
        Items = new()
        {
            Auto("ATC_GEAR", "AFTER_TAKEOFF_CL", "Landing gear: UP", A350StateEvaluator.GearUpField, v => v > 0.5, action: null),
            Auto("ATC_FLAPS", "AFTER_TAKEOFF_CL", "Flaps: 0", "A350_FLAPS", v => v < 0.5, action: null),
            Auto("ATC_PACKS", "AFTER_TAKEOFF_CL", "Packs: ON", A350StateEvaluator.PacksOnField, v => v > 0.5, action: null),
            Reminder("ATC_BARO", "AFTER_TAKEOFF_CL", "Baro reference: SET", s => s.BaroText()),
        }
    };

    private static Group BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Items = new()
        {
            Reminder("AP_AUTOBRAKE", "APPROACH", "Set the landing autobrake: Instrument section, Autobrake panel"),
            Auto("AP_SEATBELTS", "APPROACH", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, (e, _) => e.Set("A350_SIGNS_SEAT_BELTS", 0)),
            Multi("AP_LS", "APPROACH", "LS buttons: ON", "A350_EFIS_LS_LEFT", v => v > 0.5, new[] { "A350_EFIS_LS_RIGHT" },
                async (e, _) => { await e.Set("A350_EFIS_LS_LEFT", 1); await e.Set("A350_EFIS_LS_RIGHT", 1); }),
            Multi("AP_EFISMODE", "APPROACH", "EFIS mode: LS", "A350_EFIS_MODE_LEFT", v => v < 0.5, new[] { "A350_EFIS_MODE_RIGHT" },
                async (e, _) => { await e.Set("A350_EFIS_MODE_LEFT", 0); await e.Set("A350_EFIS_MODE_RIGHT", 0); }),
            ActionManual("AP_CABIN", "APPROACH", "Notify the cabin crew for landing (call all)", (e, _) => e.Set("A350_CALLS_ALL", 1)),
            Reminder("AP_ACTIVATE", "APPROACH", "Activate the approach phase (MFD PERF page) when you are ready"),
        }
    };

    private static Group BuildApproachCL() => new()
    {
        Id = "APPROACH_CL", Name = "Approach Checklist",
        Items = new()
        {
            Reminder("APC_BRIEF", "APPROACH_CL", "Briefing: CONFIRMED"),
            Reminder("APC_STATUS", "APPROACH_CL", "ECAM status: NORMAL"),
            Auto("APC_SEATBELT", "APPROACH_CL", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, action: null),
            Reminder("APC_BARO", "APPROACH_CL", "Baro reference: SET", s => s.BaroText()),
            Reminder("APC_MINIMUM", "APPROACH_CL", "Minimum: SET", s => s.MinimumText()),
        }
    };

    private static Group BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Items = new()
        {
            Reminder("LD_MISSEDALT", "LANDING", "Set missed approach altitude"),
            LeaveAlone(Auto("LD_SPOILERS_ARM", "LANDING", "Spoilers: ARMED", "A350_SPOILERS_ARM", v => v > 0.5, (e, _) => e.Set("A350_SPOILERS_ARM", 1)),
                A350StateEvaluator.SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
        }
    };

    private static Group BuildLandingCL() => new()
    {
        Id = "LANDING_CL", Name = "Landing Checklist",
        Items = new()
        {
            Reminder("LDC_CREW", "LANDING_CL", "Cabin crew: ADVISED"),
            Reminder("LDC_ATHR", "LANDING_CL", "Autothrust: SPEED or OFF"),
            Reminder("LDC_AUTOBRK", "LANDING_CL", "Autobrake: AS REQUIRED"),
            Reminder("LDC_CABIN", "LANDING_CL", "Cabin: READY"),
            Reminder("LDC_RWYCOND", "LANDING_CL", "Runway condition: SET"),
            Auto("LDC_SEATBELT", "LANDING_CL", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, action: null),
            Auto("LDC_SPOILERS", "LANDING_CL", "Ground spoilers: ARM", "A350_SPOILERS_ARM", v => v > 0.5, action: null),
            Auto("LDC_GEAR", "LANDING_CL", "Landing gear: DOWN", A350StateEvaluator.GearDownField, v => v > 0.5, action: null),
            Auto("LDC_FLAPS", "LANDING_CL", "Flaps: LANDING", A350StateEvaluator.LandingFlapsField, v => v > 0.5, action: null),
        }
    };

    private static Group BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Items = new()
        {
            Auto("AL_SPOILERS_OFF", "AFTER_LANDING", "Spoilers: DISARM", "A350_SPOILERS_ARM", v => v < 0.5, (e, _) => e.Set("A350_SPOILERS_ARM", 0)),
            Auto("AL_AUTOBRAKE", "AFTER_LANDING", "Autobrake: OFF", A350StateEvaluator.AutoBrakeArmedField, v => v < 0.5, (e, _) => e.Set("A350_LDG_AUTO_BRK", 0)),
            Auto("AL_ENGMODE", "AFTER_LANDING", "Engine mode: NORM", "A350_ENG_MASTER_MODE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_ENG_MASTER_MODE", 1)),
            Reminder("AL_FLAPS_UP", "AFTER_LANDING", "Flaps: up"),
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "A350_APU_MASTER_SWITCH", v => v > 0.5, StartApuAsync),
            Multi("AL_ENGAI_OFF", "AFTER_LANDING", "Engine anti-ice: OFF", "A350_ANTI_ICE_ENG_LEFT", v => v < 0.5, new[] { "A350_ANTI_ICE_ENG_RIGHT" },
                async (e, _) => { await e.Set("A350_ANTI_ICE_ENG_LEFT", 0); await e.Set("A350_ANTI_ICE_ENG_RIGHT", 0); }),
            Auto("AL_WINGAI_OFF", "AFTER_LANDING", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", v => v < 0.5, (e, _) => e.Set("A350_ANTI_ICE_WING", 0)),
            Auto("AL_LANDING_OFF", "AFTER_LANDING", "Landing lights: OFF", "A350_LIGHTS_EXT_LANDING", v => v < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_LANDING", 0)),
            Auto("AL_STROBE_AUTO", "AFTER_LANDING", "Strobe lights: AUTO", "A350_LIGHTS_EXT_STROBE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_STROBE", 1)),
            Auto("AL_NOSE_TAXI", "AFTER_LANDING", "Nose light: TAXI", "A350_LIGHTS_EXT_NOSE", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_NOSE", 1)),
        }
    };

    private static Group BuildAfterLandingCL() => new()
    {
        Id = "AFTER_LANDING_CL", Name = "After Landing Checklist",
        Items = new()
        {
            Auto("ALC_SPOILERS", "AFTER_LANDING_CL", "Ground spoilers: DISARM", "A350_SPOILERS_ARM", v => v < 0.5, action: null),
            Auto("ALC_FLAPS", "AFTER_LANDING_CL", "Flaps: 0", "A350_FLAPS", v => v < 0.5, action: null),
            Auto("ALC_APU", "AFTER_LANDING_CL", "APU: START", A350StateEvaluator.ApuAvailField, v => v > 0.5, action: null),
        }
    };

    private static Group BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Items = new()
        {
            Auto("PK_PARKBRK", "PARKING", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", v => v > 0.5, (e, _) => e.Set("A350_PARK_BRK_PARKING_BRAKE", 1)),
            // A tick shuts the engines down only on the ground with the APU or external power able to carry the buses.
            LeaveAlone(Multi("PK_MASTERS_OFF", "PARKING", "Engine masters 1 and 2: OFF", "A350_ENG_MASTER_1", v => v < 0.5, new[] { "A350_ENG_MASTER_2" },
                async (e, _) => { await e.Set("A350_ENG_MASTER_1", 0); await e.Set("A350_ENG_MASTER_2", 0); }),
                s => !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn(A350StateEvaluator.ApuOrExtPowerField), EnginesStayText),
            Auto("PK_COCKPITLT", "PARKING", "Cockpit lights: set", "A350_LIGHTS_INT_ANN_LT", v => Math.Abs(v - 1) < 0.5, (e, _) => e.Set("A350_LIGHTS_INT_ANN_LT", 1)),
            Auto("PK_BEACON_OFF", "PARKING", "Beacon: OFF", "A350_LIGHTS_EXT_BEACON", v => v < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_BEACON", 0)),
            Auto("PK_WINGLT_OFF", "PARKING", "Wing lights: OFF", "A350_LIGHTS_EXT_WING", v => v < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_WING", 0)),
            Auto("PK_NOSE_OFF", "PARKING", "Nose light: OFF", "A350_LIGHTS_EXT_NOSE", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_LIGHTS_EXT_NOSE", 2)),
            Multi("PK_ENGAI_OFF", "PARKING", "Engine anti-ice: OFF", "A350_ANTI_ICE_ENG_LEFT", v => v < 0.5, new[] { "A350_ANTI_ICE_ENG_RIGHT" },
                async (e, _) => { await e.Set("A350_ANTI_ICE_ENG_LEFT", 0); await e.Set("A350_ANTI_ICE_ENG_RIGHT", 0); }),
            Auto("PK_WINGAI_OFF", "PARKING", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", v => v < 0.5, (e, _) => e.Set("A350_ANTI_ICE_WING", 0)),
            Auto("PK_APUBLEED", "PARKING", "APU bleed: ON", "A350_AIR_APU_BLEED", v => v > 0.5, (e, _) => e.Set("A350_AIR_APU_BLEED", 1)),
            // Never airborne or with an engine turning (FO-25).
            LeaveAlone(Multi("PK_FUELPUMPS_OFF", "PARKING", "Fuel pumps: OFF", A350FlowDefinitions.FuelPumps[0], v => v < 0.5, A350FlowDefinitions.FuelPumps.Skip(1).ToArray(),
                async (e, _) =>
                {
                    await e.Set("A350_FUEL_PUMP_LEFT_TANK_MAIN", 0);
                    await e.Set("A350_FUEL_PUMP_LEFT_TANK_STBY", 0);
                    await e.Set("A350_FUEL_PUMP_RIGHT_TANK_MAIN", 0);
                    await e.Set("A350_FUEL_PUMP_RIGHT_TANK_STBY", 0);
                    await e.Set("A350_FUEL_PUMP_CTR_TANK_LEFT", 0);
                    await e.Set("A350_FUEL_PUMP_CTR_TANK_RIGHT", 0);
                }),
                NotParked, FuelPumpsStayText),
            Auto("PK_SEATBELTS_OFF", "PARKING", "Seat belts: OFF", "A350_SIGNS_SEAT_BELTS", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_SIGNS_SEAT_BELTS", 2)),
            Reminder("PK_XPDR", "PARKING", "Transponder: standby (MFD SURV page)"),
            Reminder("PK_TCAS", "PARKING", "TCAS mode: standby (MFD SURV page)"),
            Auto("PK_COCKPITDOOR", "PARKING", "Cockpit door: UNLOCKED", "A350_COCKPIT_DOOR_LOCK", v => v < 0.5, (e, _) => e.Set("A350_COCKPIT_DOOR_LOCK", 0)),
        }
    };

    private static Group BuildParkingCL() => new()
    {
        Id = "PARKING_CL", Name = "Parking Checklist",
        Items = new()
        {
            Reminder("PKC_PARKBRK", "PARKING_CL", "Parking brake or chocks: AS REQUIRED"),
            Auto("PKC_ENGINES", "PARKING_CL", "All engines: OFF", A350StateEvaluator.EnginesOffField, v => v > 0.5, action: null),
            Auto("PKC_SEATBELT", "PARKING_CL", "Seat belts: OFF", "A350_SIGNS_SEAT_BELTS", v => Math.Abs(v - 2) < 0.5, action: null),
            Reminder("PKC_EXTLT", "PARKING_CL", "Exterior lights: AS REQUIRED"),
            Auto("PKC_FUELPUMPS", "PARKING_CL", "Fuel pumps: OFF", A350FlowDefinitions.FuelPumps[0], v => v < 0.5, A350FlowDefinitions.FuelPumps.Skip(1).ToArray(), action: null),
            Auto("PKC_APUBLEED", "PARKING_CL", "APU bleed: ON", "A350_AIR_APU_BLEED", v => v > 0.5, action: null),
            Reminder("PKC_XPDR", "PARKING_CL", "Transponder: STANDBY"),
        }
    };

    private static Group BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Items = new()
        {
            Auto("SC_OXY", "SECURE", "Crew oxygen supply: OFF", "A350_OXYGEN_CREW_SUPPLY", v => v < 0.5, (e, _) => e.Set("A350_OXYGEN_CREW_SUPPLY", 0)),
            Reminder("SC_LAPTOPS", "SECURE", "Laptops: off"),
            // Never airborne or with an engine turning (FO-25).
            LeaveAlone(Multi("SC_ADIRS", "SECURE", "IRS 1, 2 and 3: OFF", "A350_IRS_MODE_1", v => v < 0.5, new[] { "A350_IRS_MODE_2", "A350_IRS_MODE_3" },
                async (e, _) => { await e.Set("A350_IRS_MODE_1", 0); await e.Set("A350_IRS_MODE_2", 0); await e.Set("A350_IRS_MODE_3", 0); }),
                NotParked, IrsStayText),
            Auto("SC_EMEREXIT", "SECURE", "Emergency exit lighting: OFF", "A350_SIGNS_EMER_EXIT", v => Math.Abs(v - 2) < 0.5, (e, _) => e.Set("A350_SIGNS_EMER_EXIT", 2)),
            Multi("SC_SIGNS", "SECURE", "Signs: OFF", "A350_SIGNS_NO_SMOKING", v => Math.Abs(v - 2) < 0.5, new[] { "A350_SIGNS_NO_MOBILE", "A350_SIGNS_SEAT_BELTS" },
                async (e, _) => { await e.Set("A350_SIGNS_NO_SMOKING", 2); await e.Set("A350_SIGNS_NO_MOBILE", 2); await e.Set("A350_SIGNS_SEAT_BELTS", 2); }),
            Multi("SC_EXTLT_OFF", "SECURE", "Exterior lights: OFF", "A350_LIGHTS_EXT_NAV", v => Math.Abs(v - 2) < 0.5, new[] { "A350_LIGHTS_EXT_LOGO", "A350_LIGHTS_EXT_STROBE" },
                async (e, _) => { await e.Set("A350_LIGHTS_EXT_NAV", 2); await e.Set("A350_LIGHTS_EXT_LOGO", 2); await e.Set("A350_LIGHTS_EXT_STROBE", 2); }),
            Auto("SC_APUBLEED_OFF", "SECURE", "APU bleed: OFF", "A350_AIR_APU_BLEED", v => v < 0.5, (e, _) => e.Set("A350_AIR_APU_BLEED", 0)),
            Auto("SC_APU_OFF", "SECURE", "APU master: OFF", "A350_APU_MASTER_SWITCH", v => v < 0.5, (e, _) => e.Set("A350_APU_MASTER_SWITCH", 0)),
            Auto("SC_EXTPWR_OFF", "SECURE", "External power: OFF", A350StateEvaluator.ExtOffField, v => v > 0.5, (e, _) => e.Set(A350ActionExecutor.ExternalPowerKey, 0)),
            LeaveAlone(Multi("SC_BAT_OFF", "SECURE", "Batteries: OFF", "A350_ELEC_BAT_1", v => v < 0.5, new[] { "A350_ELEC_BAT_2", "A350_ELEC_BAT_EMER_1", "A350_ELEC_BAT_EMER_2" },
                async (e, _) =>
                {
                    await e.Set("A350_ELEC_BAT_1", 0);
                    await e.Set("A350_ELEC_BAT_2", 0);
                    await e.Set("A350_ELEC_BAT_EMER_1", 0);
                    await e.Set("A350_ELEC_BAT_EMER_2", 0);
                }),
                NotParked, BatteriesStayText),
        }
    };

    private static Group BuildSecuringCL() => new()
    {
        Id = "SECURING_CL", Name = "Securing the Aircraft Checklist",
        Items = new()
        {
            Auto("SCC_OXY", "SECURING_CL", "Crew oxygen supply: OFF", "A350_OXYGEN_CREW_SUPPLY", v => v < 0.5, action: null),
            Auto("SCC_ADIRS", "SECURING_CL", "ADIRS: OFF", "A350_IRS_MODE_1", v => v < 0.5, new[] { "A350_IRS_MODE_2", "A350_IRS_MODE_3" }, action: null),
            Auto("SCC_EMEREXIT", "SECURING_CL", "Emergency exit light: OFF", "A350_SIGNS_EMER_EXIT", v => Math.Abs(v - 2) < 0.5, action: null),
            Auto("SCC_SIGNS", "SECURING_CL", "Signs: OFF", "A350_SIGNS_NO_SMOKING", v => Math.Abs(v - 2) < 0.5, new[] { "A350_SIGNS_NO_MOBILE", "A350_SIGNS_SEAT_BELTS" }, action: null),
            Auto("SCC_APUBLEED", "SECURING_CL", "APU bleed: OFF", "A350_AIR_APU_BLEED", v => v < 0.5, action: null),
            Reminder("SCC_APU", "SECURING_CL", "APU: AS REQUIRED"),
            Reminder("SCC_LAPTOPS", "SECURING_CL", "All laptops: OFF"),
            Reminder("SCC_LOGBOOKS", "SECURING_CL", "Logbooks: COMPLETED"),
            Reminder("SCC_BATTERIES", "SECURING_CL", "All 4 batteries: AS REQUIRED"),
        }
    };

    /// <summary>The APU: the master, a dwell, and START unless AVAIL is lit (FO-11's slow condition is the master's own state,
    /// so the tick holds nothing open). A press flips, and the executor presses only with the button read off.</summary>
    private static async Task StartApuAsync(A350ActionExecutor e, A350StateEvaluator s)
    {
        await e.Set("A350_APU_MASTER_SWITCH", 1);
        if (!s.IsOn(A350StateEvaluator.ApuAvailField))
        {
            await Task.Delay(3000);
            await e.Set("A350_APU_START", 1);
        }
    }

    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition,
        string[]? additionalFields, CheckFn? action, Func<A350StateEvaluator, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        AdditionalStateFields = additionalFields ?? Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
        LiveValue = live,
    };

    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition, CheckFn? action,
        Func<A350StateEvaluator, string?>? live = null) =>
        Auto(id, groupId, label, field, condition, null, action, live);

    private static Item Multi(string id, string groupId, string label, string primaryField, Func<double, bool> condition,
        string[] additionalFields, CheckFn action) =>
        Auto(id, groupId, label, primaryField, condition, additionalFields, action);

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };

    // A pilot-confirm line whose answer is a blank: it speaks the value the First Officer reads when ticked.
    private static Item Reminder(string id, string groupId, string text, Func<A350StateEvaluator, string?> live) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
        LiveValue = live,
    };

    // A tick ON the aircraft's state says the First Officer must not act on: refused at once with the text, nothing run.
    private static Item LeaveAlone(Item item, Func<A350StateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }

    internal const string EnginesTurningText = "Engine masters left as they are: not read on the ground, or an engine is not read stopped.";
    internal const string EnginesStayText = "Engines left running: not on the ground, or no APU or external power to take the buses.";
    internal const string IrsStayText = "IRS left on: not read on the ground, or an engine is not read stopped.";
    internal const string FuelPumpsStayText = "Fuel pumps left on: not read on the ground, or an engine is not read stopped.";
    internal const string BatteriesStayText = "Batteries left on: not read on the ground, or an engine is not read stopped.";

    // The fuel pumps, the IRS and the batteries go OFF on a tick only on the ground with both engines read stopped (FO-25).
    private static bool NotParked(A350StateEvaluator s) =>
        !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn(A350StateEvaluator.EnginesStoppedField);
}
