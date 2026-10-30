using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.FirstOfficer.Airbus;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.A350;

/// <summary>
/// Reads the iniBuilds A350's state for the First Officer's checklists from the SimConnect cache. The switches are the panel's
/// own rows, by key, on the continuous batches; what no row reads (the lights' and displays' variables) is an input
/// (<see cref="Inputs"/>), streamed by the definition under the key <see cref="IniBuildsA350Definition.KeyForInput"/> names and read
/// here through a synthetic field. Unread is NaN, never false, the contract of every First Officer read.
/// </summary>
public sealed class A350StateEvaluator : LVarStateEvaluator
{
    // Live 2026-10-30 unless said otherwise: each of these read as named in flight or on the stand.
    internal const string ApuAvailable = "INI_APU_AVAILABLE", ApuFlap = "INI_APU_FLAP_PERCENT";
    internal const string ExtAOnline = "INI_GEN_EXT_A_ONLINE", ExtBOnline = "INI_GEN_EXT_B_ONLINE", GpuAvailable = "INI_GPU_AVAIL";
    internal const string AutoBrakeArmed = "INI_AUTOBRAKE_ARMED", ApuStartButton = "INI_APU_START_BUTTON";
    internal const string Engine1Available = "ENGINE_1_AVAIL", Engine2Available = "ENGINE_2_AVAIL";
    internal const string Baro1Mode = "XMLVAR_Baro1_Mode", Baro2Mode = "XMLVAR_Baro2_Mode";
    internal const string BaroUnit1 = "XMLVAR_Baro_Selector_HPA_1", BaroUnit2 = "XMLVAR_Baro_Selector_HPA_2";
    internal const string LandingConfig = "INI_LANDING_CONFIG";
    internal const string WeatherRadar1 = "INI_WXR_ON", WeatherRadar2 = "INI_WXR2_ON";
    internal const string GreenDot = "FMGEC_GD_SPD", SSpeed = "FCPC_S_SPEED", FSpeed = "FCPC_F_SPEED";
    private const string GearLeft = "GEAR LEFT POSITION", GearCentre = "GEAR CENTER POSITION", GearRight = "GEAR RIGHT POSITION";
    private const string FuelTotal = "FUEL TOTAL QUANTITY WEIGHT";

    /// <summary>What the First Officer reads that no panel row streams, with the unit of a stock variable. The definition
    /// streams each it does not stream already, consumed silently.</summary>
    public static readonly IReadOnlyList<(string Name, string? Unit)> Inputs = new (string, string?)[]
    {
        (ApuAvailable, null), (ApuFlap, null), (ApuStartButton, null), (ExtAOnline, null), (ExtBOnline, null), (GpuAvailable, null), (AutoBrakeArmed, null),
        (Engine1Available, null), (Engine2Available, null), (Baro1Mode, null), (Baro2Mode, null), (BaroUnit1, null), (BaroUnit2, null), (LandingConfig, null),
        (WeatherRadar1, null), (WeatherRadar2, null),
        (GreenDot, null), (SSpeed, null), (FSpeed, null), (A350Hotkeys.VfeNext, null),
        (GearLeft, "percent over 100"), (GearCentre, "percent over 100"), (GearRight, "percent over 100"), (FuelTotal, "kilograms"),
    };

    /// <summary>The state value XMLVAR_BaroN_Mode reads in STD (A350ButtonStates; 0 is QNH, live 2026-10-30).</summary>
    private const double BaroStdMode = 3;

    /// <summary>INI_LANDING_CONFIG with PERF APPR's landing configuration at CONF 3 (FULL is 2; live 2026-10-30).</summary>
    private const double LandingConfigConf3 = 1;

    /// <summary>A gear leg down at or above, stowed at or below (stock positions, percent over 100).</summary>
    private const double GearLegDown = 0.98, GearLegUp = 0.02;

    /// <summary>The N2 below which an engine counts as stopped, the 737's figure.</summary>
    private const double StoppedN2 = 10;

    internal const string EnginesStoppedField = "FO_ENGINES_STOPPED", EnginesOffField = "FO_ENGINES_OFF";
    internal const string Engine1RunningField = "FO_ENG1_RUNNING", Engine2RunningField = "FO_ENG2_RUNNING";
    internal const string ApuAvailField = "FO_APU_AVAIL", ApuFlapField = "FO_APU_FLAP";
    internal const string ExtOnField = "FO_EXT_ON", ExtOffField = "FO_EXT_OFF", GpuAvailField = "FO_GPU_AVAIL";
    internal const string ApuOrExtPowerField = "FO_APU_OR_EXT_POWER";
    internal const string AutoBrakeArmedField = "FO_AUTOBRK_ARMED";
    internal const string BaroStdField = "FO_BARO_STD", BaroUnitHectopascalsField = "FO_BARO_UNIT_HPA";
    internal const string GearDownField = "FO_GEAR_DOWN", GearUpField = "FO_GEAR_UP";
    internal const string TakeoffFlapsField = "FO_TO_FLAPS", LandingFlapsField = "FO_LDG_FLAPS_SET";
    internal const string TakeoffMemoField = "FO_TO_MEMO_OK", LandingMemoField = "FO_LDG_MEMO_OK";
    internal const string PacksOnField = "FO_PACKS_ON", NavLogoOnField = "FO_NAV_LOGO_ON", LineupLightsField = "FO_LINEUP_LIGHTS", PrepLightsField = "FO_PREP_LIGHTS";
    internal const string WeatherRadarOnField = "FO_WXR_ON";
    internal const string GreenDotField = "FO_SPEED_GD", SSpeedField = "FO_SPEED_S", FSpeedField = "FO_SPEED_F", VfeNextField = "FO_VFE_NEXT";
    internal const string LandingConf3Field = "FO_LANDING_CONF3";

    private readonly Func<string, string> _keyFor;
    private readonly IniBuildsA350Definition? _definition;

    /// <summary>An evaluator with no definition: each input is read under its own key (a test's, with nothing read).</summary>
    public A350StateEvaluator() => _keyFor = A350PanelLamps.InputKey;

    /// <param name="definition">Says which key streams an input, and holds what the display reader reads from the FMS.</param>
    public A350StateEvaluator(IniBuildsA350Definition definition)
    {
        _keyFor = definition.KeyForInput;
        _definition = definition;
    }

    private double Input(string name) => Sc?.GetCachedVariableValue(_keyFor(name)) ?? double.NaN;

    /// <summary>Whether the lever's speed brake reads extended: arming the ground spoilers moves it to retracted
    /// (<c>0 'INI.SPOILERS_SET'</c>, A350Levers), so a spoiler arm is left alone (FOA-11).</summary>
    internal static bool SpeedBrakeExtended(A350StateEvaluator s) => AirbusGroundSpoilers.FbwSpeedBrakeExtended(s.GetValue("A350_STAT_SPEED_BRAKE"));

    /// <summary>True when the flap lever reads a takeoff position, 1 to 3.</summary>
    internal static bool IsTakeoffFlapsLever(double lever) => lever > 0.5 && lever < 3.5;

    /// <summary>The fuel on board as the Before Start Checklist's blank reads it.</summary>
    public string? FuelText() => AirbusReadbackFormat.FuelQuantity(Input(FuelTotal), pounds: false);

    /// <summary>The Captain's baro: "standard", or the QNH in the unit the EFIS panel's selector shows. Null while unread.</summary>
    public string? BaroText()
    {
        double mode = Input(Baro1Mode), unit = Input(BaroUnit1), millibars = GetValue("A350_FCU_CAPTAIN_BARO");
        if (double.IsNaN(mode)) return null;
        bool inches = !double.IsNaN(unit) && unit < 0.5;
        return AirbusReadbackFormat.Baro(Math.Abs(mode - BaroStdMode) < 0.5 ? 1 : 0,
            inches ? millibars / AltimeterPhrase.HpaPerInHg : millibars, inches);
    }

    /// <summary>The FMS's V1, VR, V2 and FLEX temperature as a blank's value, "not set" until entered; null while the reader has not read them.</summary>
    public string? TakeoffDataText() => _definition?.FmsTakeoffSpeeds is { } s ? AirbusReadbackFormat.TakeoffSpeeds(s.V1, s.Vr, s.V2, s.Flex) : null;

    /// <summary>The flap lever as a blank's value.</summary>
    public string? FlapsText() => AirbusReadbackFormat.FlapsLever(GetValue("A350_FLAPS"));

    /// <summary>Both packs as a blank's value.</summary>
    public string? PacksText() => AirbusReadbackFormat.Packs(GetValue("A350_AIR_PACK_1"), GetValue("A350_AIR_PACK_2"));

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        switch (field)
        {
            case EnginesStoppedField:
                value = Both(Eng1N2, Eng2N2, (one, two) => one < StoppedN2 && two < StoppedN2);
                return true;
            case EnginesOffField:
                value = Both(GetValue("A350_ENG_MASTER_1"), GetValue("A350_ENG_MASTER_2"), (one, two) => one < 0.5 && two < 0.5);
                return true;
            case Engine1RunningField:
                value = Input(Engine1Available);
                return true;
            case Engine2RunningField:
                value = Input(Engine2Available);
                return true;
            case ApuAvailField:
                value = Input(ApuAvailable);
                return true;
            case ApuFlapField:
                value = Input(ApuFlap);
                return true;
            case GpuAvailField:
                value = Input(GpuAvailable);
                return true;
            case ExtOnField:
                value = Both(Input(ExtAOnline), Input(ExtBOnline), (a, b) => a > 0.5 && b > 0.5);
                return true;
            case ExtOffField:
                value = Both(Input(ExtAOnline), Input(ExtBOnline), (a, b) => a < 0.5 && b < 0.5);
                return true;
            // Something to carry the buses once the engine generators go (Parking's PK_POWER_SOURCE): one lit is enough.
            case ApuOrExtPowerField:
            {
                double[] sources = { Input(ApuAvailable), Input(ExtAOnline), Input(ExtBOnline) };
                value = Array.Exists(sources, v => v > 0.5) ? 1.0 : Array.Exists(sources, double.IsNaN) ? double.NaN : 0.0;
                return true;
            }
            case AutoBrakeArmedField:
                value = Input(AutoBrakeArmed);
                return true;
            case BaroStdField:
                value = Both(Input(Baro1Mode), Input(Baro2Mode),
                    (one, two) => Math.Abs(one - BaroStdMode) < 0.5 && Math.Abs(two - BaroStdMode) < 0.5);
                return true;
            case BaroUnitHectopascalsField:
                value = Both(Input(BaroUnit1), Input(BaroUnit2), (one, two) => one > 0.5 && two > 0.5);
                return true;
            case GearDownField:
                value = Gear(lever => lever > 0.5, leg => leg >= GearLegDown);
                return true;
            case GearUpField:
                value = Gear(lever => lever < 0.5, leg => leg <= GearLegUp);
                return true;
            case TakeoffFlapsField:
            {
                double lever = GetValue("A350_FLAPS");
                value = double.IsNaN(lever) ? double.NaN : IsTakeoffFlapsLever(lever) ? 1.0 : 0.0;
                return true;
            }
            case LandingConf3Field:
            {
                double config = Input(LandingConfig);
                value = double.IsNaN(config) ? double.NaN : Math.Abs(config - LandingConfigConf3) < 0.5 ? 1.0 : 0.0;
                return true;
            }
            // "Flaps: LANDING": FULL, or 3 when PERF APPR selects CONF 3.
            case LandingFlapsField:
                value = Both(GetValue("A350_FLAPS"), GetValue(LandingConf3Field),
                    (flaps, conf3) => conf3 > 0.5 ? Math.Abs(flaps - 3) < 0.5 : Math.Abs(flaps - 4) < 0.5);
                return true;
            // The Before Takeoff block's memo lines the pilot set: seat belts, ground spoilers, flaps and the autobrake.
            case TakeoffMemoField:
                value = Both(
                    Both(GetValue("A350_SIGNS_SEAT_BELTS"), GetValue("A350_SPOILERS_ARM"), (belts, spoilers) => belts < 0.5 && spoilers > 0.5),
                    Both(GetValue(TakeoffFlapsField), Input(AutoBrakeArmed), (flaps, brake) => flaps > 0.5 && brake > 0.5),
                    (one, two) => one > 0.5 && two > 0.5);
                return true;
            case LandingMemoField:
                value = Both(GetValue("A350_SIGNS_SEAT_BELTS"), GetValue(LandingFlapsField), (belts, flaps) => belts < 0.5 && flaps > 0.5);
                return true;
            case PacksOnField:
                value = Both(GetValue("A350_AIR_PACK_1"), GetValue("A350_AIR_PACK_2"), (one, two) => one > 0.5 && two > 0.5);
                return true;
            // Nav at its position 1 and logo at AUTO, the two the Cockpit Preparation step sets.
            case NavLogoOnField:
                value = Both(GetValue("A350_LIGHTS_EXT_NAV"), GetValue("A350_LIGHTS_EXT_LOGO"), (nav, logo) => nav < 1.5 && Math.Abs(logo - 1) < 0.5);
                return true;
            // The landing lights on and the nose selector at T.O, the two the Lineup step sets.
            case LineupLightsField:
                value = Both(GetValue("A350_LIGHTS_EXT_LANDING"), GetValue("A350_LIGHTS_EXT_NOSE"), (landing, nose) => landing > 0.5 && nose < 0.5);
                return true;
            // The cockpit preparation's strobe at AUTO and its beacon, landing and nose lights off.
            case PrepLightsField:
                value = Both(
                    Both(GetValue("A350_LIGHTS_EXT_STROBE"), GetValue("A350_LIGHTS_EXT_BEACON"), (strobe, beacon) => Math.Abs(strobe - 1) < 0.5 && beacon < 0.5),
                    Both(GetValue("A350_LIGHTS_EXT_LANDING"), GetValue("A350_LIGHTS_EXT_NOSE"), (landing, nose) => landing < 0.5 && Math.Abs(nose - 2) < 0.5),
                    (one, two) => one > 0.5 && two > 0.5);
                return true;
            case WeatherRadarOnField:
                value = Both(Input(WeatherRadar1), Input(WeatherRadar2), (one, two) => one > 0.5 && two > 0.5);
                return true;
            case GreenDotField:
                value = Input(GreenDot);
                return true;
            case SSpeedField:
                value = Input(SSpeed);
                return true;
            case FSpeedField:
                value = Input(FSpeed);
                return true;
            case VfeNextField:
                value = Input(A350Hotkeys.VfeNext);
                return true;
        }
        value = double.NaN;
        return false;
    }

    // The lever and all three legs, as FO-13 has it: NaN when any is unread, so an unread field never reads confirmed.
    private double Gear(Func<double, bool> leverAt, Func<double, bool> legAt)
    {
        double lever = GetValue("A350_LDG_LEVER");
        double[] legs = { Input(GearLeft), Input(GearCentre), Input(GearRight) };
        if (double.IsNaN(lever) || Array.Exists(legs, double.IsNaN)) return double.NaN;
        return leverAt(lever) && Array.TrueForAll(legs, l => legAt(l)) ? 1.0 : 0.0;
    }
}
