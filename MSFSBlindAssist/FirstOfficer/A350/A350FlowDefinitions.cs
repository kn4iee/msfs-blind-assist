using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.FirstOfficer.A350;

using Flow = Models.FlowDefinition<A350StateEvaluator>;
using Step = Models.FlowStep<A350StateEvaluator>;

/// <summary>
/// The iniBuilds A350's First Officer flows, the A380's twelve phases with the A350's own rows (values are state values, the
/// rows' own: A350_LIGHTS_EXT_NOSE 0 T.O, 1 Taxi, 2 Off; A350_SIGNS_* 0 On, 1 Auto, 2 Off; A350_LIGHTS_EXT_NAV 0 and 1 On, 2 Off).
/// Surveillance (transponder, TCAS, weather radar, predictive windshear) is set on the MFD's SURV page, which no row reaches: the
/// First Officer reminds and never writes it (FOA-9's reasoning; FO-2). Every write completes its own action line, and the
/// read-only checks complete the aircraft's own checklist lines a later condition makes true (FO-6).
/// </summary>
public static class A350FlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildCockpitPrep(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildTaxi(),
        BuildLineup(),
        BuildAfterTakeoff(),
        BuildApproach(),
        BuildLanding(),
        BuildAfterLanding(),
        BuildParking(),
        BuildSecure(),
    };

    /// <summary>The six fuel pumps the flows switch together, the one list the checklist's pump lines read.</summary>
    internal static readonly IReadOnlyList<string> FuelPumps = Array.AsReadOnly(new[]
    {
        "A350_FUEL_PUMP_LEFT_TANK_MAIN", "A350_FUEL_PUMP_LEFT_TANK_STBY", "A350_FUEL_PUMP_RIGHT_TANK_MAIN", "A350_FUEL_PUMP_RIGHT_TANK_STBY",
        "A350_FUEL_PUMP_CTR_TANK_LEFT", "A350_FUEL_PUMP_CTR_TANK_RIGHT",
    });

    /// <summary>How long the Engine Start flow waits for an engine to run: its start measured about a minute, hot or cold
    /// (live 2026-10-30, both engines, twice), so 180 s is well clear of it.</summary>
    internal const int EngineStartTimeoutSeconds = 180;

    private static Flow BuildCockpitPrep() => new()
    {
        Id = "COCKPIT_PREP", Name = "Cockpit Preparation",
        Description = "Safety checks, batteries and ground power, overhead setup, EFIS, FCU.",
        RelatedChecklistGroupIds = Array.Empty<string>(),
        Steps = new()
        {
            // A cold-and-dark flow: never with an engine turning, nor airborne (FO-25).
            WaitForField("CP_ENGINES_STOPPED", "Engines stopped", A350StateEvaluator.EnginesStoppedField, v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop),
            OnGroundGuard("CP_ON_GROUND"),
            Done(Skip(Multi("CP_WIPERS", "Wipers: OFF", ("A350_WIPER_LEFT", 3), ("A350_WIPER_RIGHT", 3)),
                s => s.IsPosition("A350_WIPER_LEFT", 3) && s.IsPosition("A350_WIPER_RIGHT", 3)), "CP_WIPERS"),
            Done(Skip(SW("CP_GEAR", "Gear lever: DOWN", "A350_LDG_LEVER", 1), s => s.IsPosition("A350_LDG_LEVER", 1)), "CP_GEAR"),
            Captain("CP_FLAPS", "Flaps: confirm up"),
            Done(Skip(SW("CP_SPOILERS", "Spoilers: disarmed", "A350_SPOILERS_ARM", 0), s => s.IsPosition("A350_SPOILERS_ARM", 0)), "CP_SPOILERS"),
            Done(Skip(SW("CP_PARKBRK", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", 1), s => s.IsOn("A350_PARK_BRK_PARKING_BRAKE")), "CP_PARKBRK"),
            Captain("CP_THROTTLES", "Confirm thrust levers idle"),
            Done(Skip(SW("CP_ENGMODE", "Engine mode: NORM", "A350_ENG_MASTER_MODE", 1), s => s.IsPosition("A350_ENG_MASTER_MODE", 1)), "CP_ENGMODE"),
            Done(Multi("CP_MASTERS_OFF", "Engine masters 1 and 2: OFF", ("A350_ENG_MASTER_1", 0), ("A350_ENG_MASTER_2", 0)), "CP_MASTERS_OFF"),
            Done(Skip(Multi("CP_BAT", "Batteries: ON", ("A350_ELEC_BAT_1", 1), ("A350_ELEC_BAT_2", 1), ("A350_ELEC_BAT_EMER_1", 1), ("A350_ELEC_BAT_EMER_2", 1)),
                s => s.IsOn("A350_ELEC_BAT_1") && s.IsOn("A350_ELEC_BAT_2") && s.IsOn("A350_ELEC_BAT_EMER_1") && s.IsOn("A350_ELEC_BAT_EMER_2")), "CP_BAT"),
            Wait("CP_STBY1", "Standby", 5),
            Done(Skip(SW("CP_COCKPITLT", "Cockpit lights: set", "A350_LIGHTS_INT_ANN_LT", 1), s => s.IsPosition("A350_LIGHTS_INT_ANN_LT", 1)), "CP_COCKPITLT"),
            // The GPU comes from the OIS's GROUND page; with none connected the EXT buttons have nothing to switch, and the
            // reminder before them has said so (FO-20's silent skip).
            Captain("CP_GPU_OIS", "Ground power: connect it on the OIS GROUND page if you want it"),
            Done(Skip(Provider("CP_GPU", "Ground power: ON", A350ActionExecutor.ExternalPowerKey, s => s.IsOn(A350StateEvaluator.GpuAvailField) ? 1 : null),
                s => s.IsOn(A350StateEvaluator.ExtOnField)), "CP_GPU"),
            Captain("CP_INSTRBRT", "Display brightness: set"),
            Done(Skip(Multi("CP_ADIRS", "ADIRS: NAV", ("A350_IRS_MODE_1", 1), ("A350_IRS_MODE_2", 1), ("A350_IRS_MODE_3", 1)),
                s => s.IsPosition("A350_IRS_MODE_1", 1) && s.IsPosition("A350_IRS_MODE_2", 1) && s.IsPosition("A350_IRS_MODE_3", 1)), "CP_ADIRS"),
            Done(Skip(SW("CP_OXY", "Crew oxygen: ON", "A350_OXYGEN_CREW_SUPPLY", 1), s => s.IsOn("A350_OXYGEN_CREW_SUPPLY")), "CP_OXY"),
            Done(SW("CP_FIRETEST", "Fire test: listen for the fire warning", A350ActionExecutor.FireTestKey, 1), "CP_FIRETEST"),
            Done(Skip(SW("CP_GNDCTL", "Recorder ground control: ON", "A350_RCD_RCDR_GND_CTRL", 1), s => s.IsOn("A350_RCD_RCDR_GND_CTRL")), "CP_GNDCTL"),
            Done(Skip(Multi("CP_NAVLOGO", "Nav and logo lights: ON", ("A350_LIGHTS_EXT_NAV", 0), ("A350_LIGHTS_EXT_LOGO", 1)),
                s => s.IsPosition("A350_LIGHTS_EXT_NAV", 0) && s.IsPosition("A350_LIGHTS_EXT_LOGO", 1)), "CP_NAVLOGO"),
            // The rest of the exterior lighting panel, as the manual's cockpit preparation sets it.
            Done(Skip(Multi("CP_EXTLT", "Strobe lights: AUTO. Beacon, landing and nose lights: OFF",
                    ("A350_LIGHTS_EXT_STROBE", 1), ("A350_LIGHTS_EXT_BEACON", 0), ("A350_LIGHTS_EXT_LANDING", 0), ("A350_LIGHTS_EXT_NOSE", 2)),
                s => s.IsOn(A350StateEvaluator.PrepLightsField)), "CP_EXTLT"),
            Done(Skip(SW("CP_SEATBELT", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", 0), s => s.IsPosition("A350_SIGNS_SEAT_BELTS", 0)), "CP_SEATBELT"),
            Done(Skip(Multi("CP_NOSMOKE", "No smoking and no mobile signs: AUTO", ("A350_SIGNS_NO_SMOKING", 1), ("A350_SIGNS_NO_MOBILE", 1)),
                s => s.IsPosition("A350_SIGNS_NO_SMOKING", 1) && s.IsPosition("A350_SIGNS_NO_MOBILE", 1)), "CP_NOSMOKE"),
            Done(Skip(SW("CP_EMEREXIT", "Emergency exit lighting: ARM", "A350_SIGNS_EMER_EXIT", 1), s => s.IsPosition("A350_SIGNS_EMER_EXIT", 1)), "CP_EMEREXIT"),
            Captain("CP_LTSTEST", "Lights test"),
            Done(Skip(SW("CP_WINGAI", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", 0), s => s.IsPosition("A350_ANTI_ICE_WING", 0)), "CP_WINGAI"),
            Done(Skip(SW("CP_WINGLT", "Wing lights: OFF", "A350_LIGHTS_EXT_WING", 0), s => s.IsPosition("A350_LIGHTS_EXT_WING", 0)), "CP_WINGLT"),
            Done(Skip(Multi("CP_PACKS", "Packs: ON", ("A350_AIR_PACK_1", 1), ("A350_AIR_PACK_2", 1)), s => s.IsOn(A350StateEvaluator.PacksOnField)), "CP_PACKS"),
            Done(Skip(SW("CP_XBLEED", "Crossbleed: AUTO", "A350_AIR_X_BLEED", 1), s => s.IsPosition("A350_AIR_X_BLEED", 1)), "CP_XBLEED"),
            Done(Skip(SW("CP_PACKFLOW", "Air flow: NORMAL", "A350_AIR_FLOW", 2), s => s.IsPosition("A350_AIR_FLOW", 2)), "CP_PACKFLOW"),
            Done(Skip(Multi("CP_HOTAIR", "Hot air: ON", ("A350_AIR_HOT_AIR_1", 1), ("A350_AIR_HOT_AIR_2", 1)),
                s => s.IsOn("A350_AIR_HOT_AIR_1") && s.IsOn("A350_AIR_HOT_AIR_2")), "CP_HOTAIR"),
            Captain("CP_AIRTEMP", "Cabin temperature: set as required"),
            Done(Skip(SW("CP_BARO", "Baro reference: hectopascals", A350ActionExecutor.BaroUnitHectopascalsKey, 1),
                s => s.IsOn(A350StateEvaluator.BaroUnitHectopascalsField)), "CP_BARO"),
            Captain("CP_ALTIMETERS", "Altimeters and standby instrument: set QNH"),
            Done(Skip(SW("CP_ANTISKID", "Anti-skid: ON", "A350_LDG_ANTI_SKID", 1), s => s.IsOn("A350_LDG_ANTI_SKID")), "CP_ANTISKID"),
            Captain("CP_ALTRPT", "Altitude reporting: on (MFD SURV page)"),
            Captain("CP_TCASMODE", "TCAS mode: standby (MFD SURV page)"),
            Captain("CP_XPDR", "Transponder: standby (MFD SURV page)"),
            Done(Multi("CP_EFISMODE", "EFIS mode: ARC", ("A350_EFIS_MODE_LEFT", 3), ("A350_EFIS_MODE_RIGHT", 3)), "CP_EFISMODE"),
            Done(Multi("CP_EFISRANGE", "EFIS range: 40", ("A350_EFIS_RANGE_LEFT", 7), ("A350_EFIS_RANGE_RIGHT", 7)), "CP_EFISRANGE"),
            Done(Skip(SW("CP_FD", "Flight directors: ON", "A350_FCU_FD", 1), s => s.IsOn("A350_FCU_FD")), "CP_FD"),
            Captain("CP_CLOCK", "Clock: reset"),
            Done(SW("CP_ECAMPAGE", "ECAM page: door", "A350_SD_PAGE", 11), "CP_ECAMPAGE"),
            Captain("CP_IFR", "Obtain IFR clearance"),
            Captain("CP_PAYLOAD", "Load fuel and payload on the OIS"),
            Captain("CP_MFD", "Program the MFD"),
        }
    };

    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "Cockpit door, FCU managed modes, APU start, ground power off, fuel pumps, beacon.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START_CL" },
        Steps = new()
        {
            Done(Skip(Multi("BS_COCKPITDOOR", "Cockpit door: CLOSED and LOCKED", ("A350_COCKPIT_DOOR_HANDLE_BASE_CS", 0), ("A350_COCKPIT_DOOR_LOCK", 1)),
                s => s.IsPosition("A350_COCKPIT_DOOR_HANDLE_BASE_CS", 0) && s.IsPosition("A350_COCKPIT_DOOR_LOCK", 1)), "BS_COCKPITDOOR"),
            Done(SW("BS_FCUSPD", "FCU speed: managed", "A350_FCU_SPD_KNOB_PUSH", 1), "BS_FCUSPD"),
            Done(SW("BS_FCUHDG", "FCU heading: managed", "A350_FCU_HDG_KNOB_PUSH", 1), "BS_FCUHDG"),
            Captain("BS_ALT", "Set cleared altitude on the FCU"),
            Done(SW("BS_FCUALT", "FCU altitude: pushed", "A350_FCU_ALT_KNOB_PUSH", 1), "BS_FCUALT"),
            Done(SW("BS_ECAMPAGE", "ECAM page: APU", "A350_SD_PAGE", 9), "BS_ECAMPAGE"),
            // Master, a dwell, START, then AVAIL (live 2026-10-30, about 72 s). A start that fails stops the flow before the
            // ground power goes off.
            Done(Skip(SW("BS_APU", "APU: ON", "A350_APU_MASTER_SWITCH", 1), s => s.IsOn("A350_APU_MASTER_SWITCH")), "BS_APU"),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3), s => s.IsOn(A350StateEvaluator.ApuAvailField)),
            Skip(SW("BS_APU_START", "APU start", "A350_APU_START", 1), s => s.IsOn(A350StateEvaluator.ApuAvailField)),
            WaitForField("BS_APU_AVAIL", "Waiting for APU available", A350StateEvaluator.ApuAvailField, v => v > 0.5, 180, onTimeout: FlowStepFailurePolicy.Stop),
            Done(Skip(SW("BS_APUBLEED", "APU bleed: ON", "A350_AIR_APU_BLEED", 1), s => s.IsOn("A350_AIR_APU_BLEED")), "BS_APUBLEED"),
            Done(Skip(SW("BS_GPU_OFF", "Ground power: OFF", A350ActionExecutor.ExternalPowerKey, 0), s => s.IsOn(A350StateEvaluator.ExtOffField)), "BS_GPU_OFF"),
            Captain("BS_GPU_DISC", "Ground power and chocks: remove on the OIS GROUND page"),
            Done(Multi("BS_FUELPUMPS", "Fuel pumps: ON", FuelPumpWrites(1)), "BS_FUELPUMPS"),
            // The aircraft's own Before Start lines the flow does not set itself: read only (5 s, skipped aloud, never writes).
            Done(Skip(WaitForField("BS_BELTS_CHECK", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", v => v < 0.5, 5),
                s => s.IsPosition("A350_SIGNS_SEAT_BELTS", 0)), "BSC_SEATBELT"),
            Done(Skip(WaitForField("BS_ADIRS_CHECK", "ADIRS: NAV", "A350_IRS_MODE_1", v => Math.Abs(v - 1) < 0.5, 5),
                s => s.IsPosition("A350_IRS_MODE_1", 1) && s.IsPosition("A350_IRS_MODE_2", 1) && s.IsPosition("A350_IRS_MODE_3", 1)), "BSC_ADIRS"),
            Done(Skip(WaitForField("BS_LEVERS_CHECK", "Thrust levers: IDLE", "A350_THRUST_LEVERS_ALL", LeversIdle, 5),
                s => LeversIdle(s.GetValue("A350_THRUST_LEVERS_ALL"))), "BSC_THRUST"),
            Also(Done(Skip(SW("BS_BEACON", "Beacon: ON", "A350_LIGHTS_EXT_BEACON", 1), s => s.IsOn("A350_LIGHTS_EXT_BEACON")), "BS_BEACON"), "BSC_BEACON"),
            Done(Skip(SW("BS_PARKBRK", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", 1), s => s.IsOn("A350_PARK_BRK_PARKING_BRAKE")), "BS_PARKBRK"),
            // The manual's way: KCCU SURV, DEFAULT SETTINGS, CONFIRM makes the transponder and TCAS active.
            Captain("BS_SURV", "Surveillance: default settings (MFD SURV page)"),
            Captain("BS_PERF", "Check the MFD PERF page"),
            Captain("BS_TRIM", "Set trim"),
            Captain("BS_DOORS", "Doors: close all on the OIS GROUND page"),
            Captain("BS_TAXICLR", "Obtain pushback and start clearance"),
        }
    };

    private static Flow BuildEngineStart() => new()
    {
        Id = "ENGINE_START", Name = "Engine Start",
        Description = "Engine mode IGN/START, then engine 1, and engine 2 once engine 1 runs.",
        RelatedChecklistGroupIds = Array.Empty<string>(),
        Steps = new()
        {
            Done(SW("ES_ECAMPAGE", "ECAM page: engine", "A350_SD_PAGE", 1), "ES_ECAMPAGE"),
            Done(Skip(SW("ES_APUBLEED", "APU bleed: ON", "A350_AIR_APU_BLEED", 1), s => s.IsOn("A350_AIR_APU_BLEED")), "ES_APUBLEED"),
            Done(Skip(SW("ES_ENGMODE", "Engine mode: IGN", "A350_ENG_MASTER_MODE", 2), s => s.IsPosition("A350_ENG_MASTER_MODE", 2)), "ES_ENGMODE"),
            Done(Skip(SW("ES_ENG1", "Engine 1 master: ON", "A350_ENG_MASTER_1", 1), s => s.IsOn("A350_ENG_MASTER_1")), "ES_ENG1"),
            // Engine 2 only once engine 1 RUNS (the aircraft's own AVAIL), never after a fixed pause: a slow or failed start
            // must not have the next one started on top of it. Not there in time STOPS the flow, aloud.
            WaitForField("ES_ENG1_RUNNING", "Engine 1 running", A350StateEvaluator.Engine1RunningField, v => v > 0.5, EngineStartTimeoutSeconds, onTimeout: FlowStepFailurePolicy.Stop),
            Done(Skip(SW("ES_ENG2", "Engine 2 master: ON", "A350_ENG_MASTER_2", 1), s => s.IsOn("A350_ENG_MASTER_2")), "ES_ENG2"),
            WaitForField("ES_ENG2_RUNNING", "Engine 2 running", A350StateEvaluator.Engine2RunningField, v => v > 0.5, EngineStartTimeoutSeconds, onTimeout: FlowStepFailurePolicy.Stop),
        }
    };

    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Mode NORM, APU off, spoilers armed, taxi light, rudder trim reset and neutral check.",
        RelatedChecklistGroupIds = new[] { "AFTER_START_CL" },
        Steps = new()
        {
            Done(Skip(SW("AS_ENGMODE", "Engine mode: NORM", "A350_ENG_MASTER_MODE", 1), s => s.IsPosition("A350_ENG_MASTER_MODE", 1)), "AS_ENGMODE"),
            Captain("AS_ANTIICE", "Anti-ice: set as required"),
            Done(SW("AS_ECAMPAGE", "ECAM page: APU", "A350_SD_PAGE", 9), "AS_ECAMPAGE"),
            Done(Skip(SW("AS_APU_OFF", "APU: OFF", "A350_APU_MASTER_SWITCH", 0), s => s.IsPosition("A350_APU_MASTER_SWITCH", 0)), "AS_APU_OFF"),
            Done(Skip(SW("AS_APUBLEED_OFF", "APU bleed: OFF", "A350_AIR_APU_BLEED", 0), s => s.IsPosition("A350_AIR_APU_BLEED", 0)), "AS_APUBLEED_OFF"),
            Done(Skip(SW("AS_NOSE_TAXI", "Nose light: TAXI", "A350_LIGHTS_EXT_NOSE", 1), s => s.IsPosition("A350_LIGHTS_EXT_NOSE", 1)), "AS_NOSE_TAXI"),
            Done(Skip(SW("AS_COCKPITLT", "Cockpit lights: annunciator dim", "A350_LIGHTS_INT_ANN_LT", 2), s => s.IsPosition("A350_LIGHTS_INT_ANN_LT", 2)), "AS_COCKPITLT"),
            LeaveAlone(Done(Skip(SW("AS_SPOILERS_ARM", "Spoilers: ARMED", "A350_SPOILERS_ARM", 1), s => s.IsPosition("A350_SPOILERS_ARM", 1)), "AS_SPOILERS_ARM"),
                A350StateEvaluator.SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
            // The reset only commands the trim; the read-only check after it completes "Rudder trim: NEUTRAL" (FO-6).
            Done(SW("AS_RUDDERTRIM", "Rudder trim: RESET", "A350_RUDDER_TRIM_RESET", 1), "AS_RUDDERTRIM"),
            Done(Skip(WaitForField("AS_RUDDERTRIM_CHECK", "Rudder trim: NEUTRAL", "A350_STAT_RUDDER_TRIM", RudderTrimNeutral, 20),
                s => RudderTrimNeutral(s.GetValue("A350_STAT_RUDDER_TRIM"))), "ASC_RUDDERTRIM"),
            Captain("AS_FLAPS", "Flaps: set for takeoff"),
        }
    };

    private static Flow BuildTaxi() => new()
    {
        Id = "TAXI", Name = "Taxi",
        Description = "Flight controls check, autobrake RTO, engine mode NORM, weather radar, flight controls page, takeoff config test.",
        RelatedChecklistGroupIds = Array.Empty<string>(),
        Steps = new()
        {
            Captain("TX_FCTL", "Check the flight controls"),
            // A/BRK on the ground arms RTO; it flips, so it is pressed only with the armed state read off.
            Done(Skip(SW("TX_AUTOBRAKE", "Autobrake: RTO", "A350_LDG_AUTO_BRK", 1), s => s.IsOn(A350StateEvaluator.AutoBrakeArmedField)), "TX_AUTOBRAKE"),
            Done(Skip(SW("TX_ENGMODE", "Engine mode: NORM", "A350_ENG_MASTER_MODE", 1), s => s.IsPosition("A350_ENG_MASTER_MODE", 1)), "TX_ENGMODE"),
            // The EFIS panels' WX keys, the manual's way; they flip, so each is pressed only with its state read off.
            Done(Skip(Multi("TX_WXR", "Weather radar: ON", ("A350_EFIS_BSK_2_LEFT", 1), ("A350_EFIS_BSK_2_RIGHT", 1)), s => s.IsOn(A350StateEvaluator.WeatherRadarOnField)), "TX_WXR"),
            Captain("TX_PWS", "Predictive windshear: on (MFD SURV page)"),
            Captain("TX_FLAPS", "Flaps: set for takeoff"),
            Done(SW("TX_ECAMPAGE", "ECAM page: flight controls", "A350_SD_PAGE", 10), "TX_ECAMPAGE"),
            // Done after the flaps, so the warning computer verifies a complete configuration.
            Done(SW("TX_CONFIG", "Takeoff config test", A350ActionExecutor.ToConfigTestKey, 1), "TX_CONFIG"),
        }
    };

    private static Flow BuildLineup() => new()
    {
        Id = "LINEUP", Name = "Lineup",
        Description = "Before takeoff block: TCAS reminder, memo check, strobe, landing and nose lights, cabin call.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            Captain("LU_TCAS", "TCAS mode: TA/RA (MFD SURV page)"),
            // The memo lines the pilot set: read only (5 s, skipped aloud, never writes). What already reads right ticks with the rest.
            Also(Done(Skip(WaitForField("LU_MEMO_CHECK", "Autobrake, seat belts, ground spoilers and flaps for takeoff", A350StateEvaluator.TakeoffMemoField, v => v > 0.5, 5),
                    s => s.IsOn(A350StateEvaluator.TakeoffMemoField)), "BTC_AUTOBRK"),
                "BTC_SEATBELT", "BTC_SPLRS", "BTC_FLAPS"),
            Done(Skip(SW("LU_STROBE", "Strobe lights: ON", "A350_LIGHTS_EXT_STROBE", 0), s => s.IsPosition("A350_LIGHTS_EXT_STROBE", 0)), "LU_STROBE"),
            Done(Skip(Multi("LU_LANDING", "Landing and nose lights: ON", ("A350_LIGHTS_EXT_LANDING", 1), ("A350_LIGHTS_EXT_NOSE", 0)),
                s => s.IsOn("A350_LIGHTS_EXT_LANDING") && s.IsPosition("A350_LIGHTS_EXT_NOSE", 0)), "LU_LANDING"),
            Done(SW("LU_CABIN", "Advise the cabin crew for takeoff (call all)", "A350_CALLS_ALL", 1), "LU_CABIN"),
        }
    };

    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Spoilers disarm, autobrake off, nose light off, packs on, flaps and gear up checks.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF_CL" },
        Steps = new()
        {
            Done(Skip(SW("AT_SPOILERS_DISARM", "Spoilers: DISARM", "A350_SPOILERS_ARM", 0), s => s.IsPosition("A350_SPOILERS_ARM", 0)), "AT_SPOILERS_DISARM"),
            Done(Skip(SW("AT_AUTOBRAKE", "Autobrake: OFF", "A350_LDG_AUTO_BRK", 0), s => s.IsPosition(A350StateEvaluator.AutoBrakeArmedField, 0)), "AT_AUTOBRAKE"),
            Done(Skip(SW("AT_NOSE_OFF", "Nose light: OFF", "A350_LIGHTS_EXT_NOSE", 2), s => s.IsPosition("A350_LIGHTS_EXT_NOSE", 2)), "AT_NOSE_OFF"),
            Also(Done(Skip(Multi("AT_PACKS", "Packs: ON", ("A350_AIR_PACK_1", 1), ("A350_AIR_PACK_2", 1)), s => s.IsOn(A350StateEvaluator.PacksOnField)), "AT_PACKS"), "ATC_PACKS"),
            Done(Skip(WaitForField("AT_FLAPS_CHECK", "Flaps: 0", "A350_FLAPS", v => v < 0.5, 5), s => s.IsPosition("A350_FLAPS", 0)), "ATC_FLAPS"),
            // LAST (FO-13): the gear confirmed up from the legs, read only.
            Done(Skip(WaitForField("AT_GEAR_UP_CHECK", "Landing gear: UP", A350StateEvaluator.GearUpField, v => v > 0.5, 20),
                s => s.IsOn(A350StateEvaluator.GearUpField)), "ATC_GEAR"),
        }
    };

    private static Flow BuildApproach() => new()
    {
        Id = "APPROACH", Name = "Approach",
        Description = "Landing autobrake reminder, seat belts, LS buttons, EFIS LS mode, cabin call.",
        RelatedChecklistGroupIds = new[] { "APPROACH_CL" },
        Steps = new()
        {
            Captain("AP_AUTOBRAKE", "Set the landing autobrake: Instrument section, Autobrake panel"),
            Also(Done(Skip(SW("AP_SEATBELTS", "Seat belts: ON", "A350_SIGNS_SEAT_BELTS", 0), s => s.IsPosition("A350_SIGNS_SEAT_BELTS", 0)), "AP_SEATBELTS"), "APC_SEATBELT"),
            Done(Skip(Multi("AP_LS", "LS buttons: ON", ("A350_EFIS_LS_LEFT", 1), ("A350_EFIS_LS_RIGHT", 1)), s => s.IsOn("A350_EFIS_LS_LEFT") && s.IsOn("A350_EFIS_LS_RIGHT")), "AP_LS"),
            Done(Multi("AP_EFISMODE", "EFIS mode: LS", ("A350_EFIS_MODE_LEFT", 0), ("A350_EFIS_MODE_RIGHT", 0)), "AP_EFISMODE"),
            Done(SW("AP_CABIN", "Notify the cabin crew for landing (call all)", "A350_CALLS_ALL", 1), "AP_CABIN"),
            Captain("AP_ACTIVATE", "Activate the approach phase (MFD PERF page) when you are ready"),
        }
    };

    private static Flow BuildLanding() => new()
    {
        Id = "LANDING", Name = "Landing",
        Description = "Missed approach altitude reminder, spoilers armed, landing memo check, gear-down check.",
        RelatedChecklistGroupIds = new[] { "LANDING_CL" },
        Steps = new()
        {
            Captain("LD_MISSEDALT", "Set missed approach altitude"),
            // Left alone over an extended speed brake, which the arm would snap back to retracted on final (FOA-11).
            LeaveAlone(Also(Done(Skip(SW("LD_SPOILERS_ARM", "Spoilers: ARMED", "A350_SPOILERS_ARM", 1), s => s.IsPosition("A350_SPOILERS_ARM", 1)), "LD_SPOILERS_ARM"), "LDC_SPOILERS"),
                A350StateEvaluator.SpeedBrakeExtended, AirbusGroundSpoilers.LeaveAloneText),
            Also(Done(Skip(WaitForField("LD_MEMO_CHECK", "Seat belts and landing flaps", A350StateEvaluator.LandingMemoField, v => v > 0.5, 5),
                    s => s.IsOn(A350StateEvaluator.LandingMemoField)), "LDC_SEATBELT"), "LDC_FLAPS"),
            // LAST (FO-13): the gear confirmed down from the legs, read only.
            Done(Skip(WaitForField("LD_GEAR_DOWN_CHECK", "Landing gear: DOWN", A350StateEvaluator.GearDownField, v => v > 0.5, 20),
                s => s.IsOn(A350StateEvaluator.GearDownField)), "LDC_GEAR"),
        }
    };

    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Clean up after vacating: spoilers, autobrake, engine mode, flaps check, APU, anti-ice, lights.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING_CL" },
        Steps = new()
        {
            Also(Done(Skip(SW("AL_SPOILERS_OFF", "Spoilers: DISARM", "A350_SPOILERS_ARM", 0), s => s.IsPosition("A350_SPOILERS_ARM", 0)), "AL_SPOILERS_OFF"), "ALC_SPOILERS"),
            Done(Skip(SW("AL_AUTOBRAKE", "Autobrake: OFF", "A350_LDG_AUTO_BRK", 0), s => s.IsPosition(A350StateEvaluator.AutoBrakeArmedField, 0)), "AL_AUTOBRAKE"),
            Done(Skip(SW("AL_ENGMODE", "Engine mode: NORM", "A350_ENG_MASTER_MODE", 1), s => s.IsPosition("A350_ENG_MASTER_MODE", 1)), "AL_ENGMODE"),
            Captain("AL_FLAPS_UP", "Flaps: up"),
            Done(Skip(WaitForField("AL_FLAPS_CHECK", "Flaps: 0", "A350_FLAPS", v => v < 0.5, 5), s => s.IsPosition("A350_FLAPS", 0)), "ALC_FLAPS"),
            // The APU for the stand: the wait only skips, the engines still run and the clean-up below goes on.
            Done(Skip(SW("AL_APU", "APU: ON", "A350_APU_MASTER_SWITCH", 1), s => s.IsOn("A350_APU_MASTER_SWITCH")), "AL_APU"),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3), s => s.IsOn(A350StateEvaluator.ApuAvailField)),
            Skip(SW("AL_APU_START", "APU start", "A350_APU_START", 1), s => s.IsOn(A350StateEvaluator.ApuAvailField)),
            Done(WaitForField("AL_APU_AVAIL", "Waiting for APU available", A350StateEvaluator.ApuAvailField, v => v > 0.5, 180), "ALC_APU"),
            Done(Multi("AL_ENGAI_OFF", "Engine anti-ice: OFF", ("A350_ANTI_ICE_ENG_LEFT", 0), ("A350_ANTI_ICE_ENG_RIGHT", 0)), "AL_ENGAI_OFF"),
            Done(Skip(SW("AL_WINGAI_OFF", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", 0), s => s.IsPosition("A350_ANTI_ICE_WING", 0)), "AL_WINGAI_OFF"),
            Done(Skip(SW("AL_LANDING_OFF", "Landing lights: OFF", "A350_LIGHTS_EXT_LANDING", 0), s => s.IsPosition("A350_LIGHTS_EXT_LANDING", 0)), "AL_LANDING_OFF"),
            Done(Skip(SW("AL_STROBE_AUTO", "Strobe lights: AUTO", "A350_LIGHTS_EXT_STROBE", 1), s => s.IsPosition("A350_LIGHTS_EXT_STROBE", 1)), "AL_STROBE_AUTO"),
            Done(Skip(SW("AL_NOSE_TAXI", "Nose light: TAXI", "A350_LIGHTS_EXT_NOSE", 1), s => s.IsPosition("A350_LIGHTS_EXT_NOSE", 1)), "AL_NOSE_TAXI"),
        }
    };

    private static Flow BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Parking brake, engine shutdown, lights, fuel pumps, signs, cockpit door. Leaves the aircraft powered; Securing shuts it down.",
        RelatedChecklistGroupIds = new[] { "PARKING_CL" },
        Steps = new()
        {
            OnGroundGuard("PK_ON_GROUND"),
            Done(Skip(SW("PK_PARKBRK", "Parking brake: ON", "A350_PARK_BRK_PARKING_BRAKE", 1), s => s.IsOn("A350_PARK_BRK_PARKING_BRAKE")), "PK_PARKBRK"),
            // The masters take the generators off the buses, so the APU or external power must carry them first.
            Skip(WaitForField("PK_POWER_SOURCE", "APU or external power available", A350StateEvaluator.ApuOrExtPowerField, v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
                s => s.IsOn(A350StateEvaluator.EnginesOffField)),
            Done(Multi("PK_MASTERS_OFF", "Engine masters 1 and 2: OFF", ("A350_ENG_MASTER_1", 0), ("A350_ENG_MASTER_2", 0)), "PK_MASTERS_OFF"),
            // Read only: completes the checklist's line and the masters' own, on the same field (FO-6, FO-8).
            Also(Done(WaitForField("PK_WAIT_ENG", "Waiting for engines off", A350StateEvaluator.EnginesOffField, v => v > 0.5, 120), "PKC_ENGINES"), "PK_MASTERS_OFF"),
            Wait("PK_STBY1", "Standby", 5),
            Done(Skip(SW("PK_COCKPITLT", "Cockpit lights: set", "A350_LIGHTS_INT_ANN_LT", 1), s => s.IsPosition("A350_LIGHTS_INT_ANN_LT", 1)), "PK_COCKPITLT"),
            Done(Skip(SW("PK_BEACON_OFF", "Beacon: OFF", "A350_LIGHTS_EXT_BEACON", 0), s => s.IsPosition("A350_LIGHTS_EXT_BEACON", 0)), "PK_BEACON_OFF"),
            Done(Skip(SW("PK_WINGLT_OFF", "Wing lights: OFF", "A350_LIGHTS_EXT_WING", 0), s => s.IsPosition("A350_LIGHTS_EXT_WING", 0)), "PK_WINGLT_OFF"),
            Done(Skip(SW("PK_NOSE_OFF", "Nose light: OFF", "A350_LIGHTS_EXT_NOSE", 2), s => s.IsPosition("A350_LIGHTS_EXT_NOSE", 2)), "PK_NOSE_OFF"),
            Done(Multi("PK_ENGAI_OFF", "Engine anti-ice: OFF", ("A350_ANTI_ICE_ENG_LEFT", 0), ("A350_ANTI_ICE_ENG_RIGHT", 0)), "PK_ENGAI_OFF"),
            Done(Skip(SW("PK_WINGAI_OFF", "Wing anti-ice: OFF", "A350_ANTI_ICE_WING", 0), s => s.IsPosition("A350_ANTI_ICE_WING", 0)), "PK_WINGAI_OFF"),
            Also(Done(Skip(SW("PK_APUBLEED", "APU bleed: ON", "A350_AIR_APU_BLEED", 1), s => s.IsOn("A350_AIR_APU_BLEED")), "PK_APUBLEED"), "PKC_APUBLEED"),
            // The fuel goes off only once both engines have spun down (FO-25). Two minutes, then the flow stops with the pumps on.
            WaitForField("PK_ENGINES_STOPPED", "Engines stopped", A350StateEvaluator.EnginesStoppedField, v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Multi("PK_FUELPUMPS_OFF", "Fuel pumps: OFF", FuelPumpWrites(0)), "PK_FUELPUMPS_OFF"), "PKC_FUELPUMPS"),
            Also(Done(Skip(SW("PK_SEATBELTS_OFF", "Seat belts: OFF", "A350_SIGNS_SEAT_BELTS", 2), s => s.IsPosition("A350_SIGNS_SEAT_BELTS", 2)), "PK_SEATBELTS_OFF"), "PKC_SEATBELT"),
            Captain("PK_XPDR", "Transponder: standby (MFD SURV page)"),
            Captain("PK_TCAS", "TCAS mode: standby (MFD SURV page)"),
            Done(Skip(SW("PK_COCKPITDOOR", "Cockpit door: UNLOCKED", "A350_COCKPIT_DOOR_LOCK", 0), s => s.IsPosition("A350_COCKPIT_DOOR_LOCK", 0)), "PK_COCKPITDOOR"),
        }
    };

    // The power-down phase: Parking leaves the aircraft powered, this is where it goes cold and dark. The last steps in the order
    // the owner chose for every Airbus (FOA-10): APU master off, wait for the flap to close (it needs APU or external power),
    // external power off, batteries.
    private static Flow BuildSecure() => new()
    {
        Id = "SECURE", Name = "Securing",
        Description = "Oxygen, ADIRS, signs, exterior lights, APU bleed, APU master, wait for the APU flap, ground power, batteries.",
        RelatedChecklistGroupIds = new[] { "SECURING_CL" },
        Steps = new()
        {
            OnGroundGuard("SC_ON_GROUND"),
            WaitForField("SC_ENGINES_STOPPED", "Engines stopped", A350StateEvaluator.EnginesStoppedField, v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Skip(SW("SC_OXY", "Crew oxygen supply: OFF", "A350_OXYGEN_CREW_SUPPLY", 0), s => s.IsPosition("A350_OXYGEN_CREW_SUPPLY", 0)), "SC_OXY"), "SCC_OXY"),
            Captain("SC_LAPTOPS", "Laptops: off"),
            Also(Done(Skip(Multi("SC_ADIRS", "IRS 1, 2 and 3: OFF", ("A350_IRS_MODE_1", 0), ("A350_IRS_MODE_2", 0), ("A350_IRS_MODE_3", 0)),
                s => s.IsPosition("A350_IRS_MODE_1", 0) && s.IsPosition("A350_IRS_MODE_2", 0) && s.IsPosition("A350_IRS_MODE_3", 0)), "SC_ADIRS"), "SCC_ADIRS"),
            Also(Done(Skip(SW("SC_EMEREXIT", "Emergency exit lighting: OFF", "A350_SIGNS_EMER_EXIT", 2), s => s.IsPosition("A350_SIGNS_EMER_EXIT", 2)), "SC_EMEREXIT"), "SCC_EMEREXIT"),
            Also(Done(Skip(Multi("SC_SIGNS", "Signs: OFF", ("A350_SIGNS_NO_SMOKING", 2), ("A350_SIGNS_NO_MOBILE", 2), ("A350_SIGNS_SEAT_BELTS", 2)),
                s => s.IsPosition("A350_SIGNS_NO_SMOKING", 2) && s.IsPosition("A350_SIGNS_NO_MOBILE", 2) && s.IsPosition("A350_SIGNS_SEAT_BELTS", 2)), "SC_SIGNS"), "SCC_SIGNS"),
            Done(Skip(Multi("SC_EXTLT_OFF", "Exterior lights: OFF", ("A350_LIGHTS_EXT_NAV", 2), ("A350_LIGHTS_EXT_LOGO", 2), ("A350_LIGHTS_EXT_STROBE", 2)),
                s => s.IsPosition("A350_LIGHTS_EXT_NAV", 2) && s.IsPosition("A350_LIGHTS_EXT_LOGO", 2) && s.IsPosition("A350_LIGHTS_EXT_STROBE", 2)), "SC_EXTLT_OFF"),
            Also(Done(Skip(SW("SC_APUBLEED_OFF", "APU bleed: OFF", "A350_AIR_APU_BLEED", 0), s => s.IsPosition("A350_AIR_APU_BLEED", 0)), "SC_APUBLEED_OFF"), "SCC_APUBLEED"),
            Done(Skip(SW("SC_APU_OFF", "APU master: OFF", "A350_APU_MASTER_SWITCH", 0), s => s.IsPosition("A350_APU_MASTER_SWITCH", 0)), "SC_APU_OFF"),
            WaitForField("SC_APU_WAIT", "APU flap to close", A350StateEvaluator.ApuFlapField, v => v < 0.5, 150),
            Done(Skip(SW("SC_EXTPWR_OFF", "External power: OFF", A350ActionExecutor.ExternalPowerKey, 0), s => s.IsOn(A350StateEvaluator.ExtOffField)), "SC_EXTPWR_OFF"),
            // LAST: the final power source, the step that makes the aircraft dark.
            Done(Skip(Multi("SC_BAT_OFF", "Batteries: OFF", ("A350_ELEC_BAT_1", 0), ("A350_ELEC_BAT_2", 0), ("A350_ELEC_BAT_EMER_1", 0), ("A350_ELEC_BAT_EMER_2", 0)),
                s => s.IsPosition("A350_ELEC_BAT_1", 0) && s.IsPosition("A350_ELEC_BAT_2", 0) && s.IsPosition("A350_ELEC_BAT_EMER_1", 0) && s.IsPosition("A350_ELEC_BAT_EMER_2", 0)), "SC_BAT_OFF"),
        }
    };

    /// <summary>The levers at idle: their state value is 0 there (25 climb, -6 reverse idle).</summary>
    internal static bool LeversIdle(double lever) => Math.Abs(lever) < 1;

    /// <summary>Rudder trim neutral, in degrees (the A380's and FlyByWire's figure, its magnitude).</summary>
    internal static bool RudderTrimNeutral(double degrees) => Math.Abs(degrees) < 0.35;

    private static (string EventName, int? TargetValue)[] FuelPumpWrites(int target) =>
        FuelPumps.Select(pump => (EventName: pump, TargetValue: (int?)target)).ToArray();

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<A350StateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Wait(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    private static Step Captain(string id, string label) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = label,
    };

    private static Step Skip(Step step, Func<A350StateEvaluator, bool> condition)
    {
        step.SkipCondition = condition;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    // A further line the step delivers, beyond Done's (FO-6).
    private static Step Also(Step step, params string[] alsoCompletes)
    {
        step.AlsoCompletesChecklistItemIds = alsoCompletes;
        return step;
    }

    // A step the First Officer must not perform in the aircraft's current state: nothing is sent, the text is spoken, its lines stay open.
    private static Step LeaveAlone(Step step, Func<A350StateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    // Stops a flow that switches engines, IRS or batteries off unless the aircraft reads on the ground; unread fails it.
    private static Step OnGroundGuard(string id) =>
        WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, 5, onTimeout: FlowStepFailurePolicy.Stop);
}
