using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.Tests.A350;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

// What the A350 First Officer reads from the cache: unread is never false (the contract of every FO read).
public class A350StateEvaluatorTests
{
    private static readonly IniBuildsA350Definition Definition = new();

    /// <summary>An evaluator over a cache holding these rows (by key) and inputs (by variable name).</summary>
    internal static A350StateEvaluator Evaluator(params (string Name, double Value)[] values)
    {
        var state = new A350StateEvaluator(Definition);
        var variables = Definition.GetVariables();
        state.SetSimConnect(SeededSimConnectCache.ConnectedWith(values
            .Select(v => (variables.ContainsKey(v.Name) ? v.Name : Definition.KeyForInput(v.Name), v.Value)).ToArray()));
        return state;
    }

    [Fact]
    public void Engines_stopped_is_both_below_10_percent_and_unknown_until_read()
    {
        var state = new A350StateEvaluator();
        Assert.True(double.IsNaN(state.GetValue(A350StateEvaluator.EnginesStoppedField)));
        state.SetEngineN2(5, double.NaN);
        Assert.True(double.IsNaN(state.GetValue(A350StateEvaluator.EnginesStoppedField)));
        state.SetEngineN2(5, 9.9);
        Assert.Equal(1, state.GetValue(A350StateEvaluator.EnginesStoppedField));
        state.SetEngineN2(5, 10);
        Assert.Equal(0, state.GetValue(A350StateEvaluator.EnginesStoppedField));
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    public void Engines_off_reads_the_masters(double master1, double master2, double off) =>
        Assert.Equal(off, Evaluator(("A350_ENG_MASTER_1", master1), ("A350_ENG_MASTER_2", master2)).GetValue(A350StateEvaluator.EnginesOffField));

    [Fact]
    public void Engines_off_is_unknown_until_both_masters_read() =>
        Assert.True(double.IsNaN(Evaluator(("A350_ENG_MASTER_1", 0)).GetValue(A350StateEvaluator.EnginesOffField)));

    [Fact]
    public void An_engine_runs_once_the_aircraft_says_it_is_available()
    {
        var state = Evaluator(("ENGINE_1_AVAIL", 1), ("ENGINE_2_AVAIL", 0));
        Assert.Equal(1, state.GetValue(A350StateEvaluator.Engine1RunningField));
        Assert.Equal(0, state.GetValue(A350StateEvaluator.Engine2RunningField));
    }

    [Theory]
    [InlineData(1, 0, 0, 1)]   // the APU available
    [InlineData(0, 1, 0, 1)]   // external power A on
    [InlineData(0, 0, 1, 1)]   // external power B on
    [InlineData(0, 0, 0, 0)]
    public void Apu_or_external_power_carries_the_buses(double apu, double extA, double extB, double expected) =>
        Assert.Equal(expected, Evaluator(("INI_APU_AVAILABLE", apu), ("INI_GEN_EXT_A_ONLINE", extA), ("INI_GEN_EXT_B_ONLINE", extB))
            .GetValue(A350StateEvaluator.ApuOrExtPowerField));

    [Fact]
    public void One_power_source_lit_is_enough_with_the_rest_unread_and_none_lit_is_unknown()
    {
        Assert.Equal(1, Evaluator(("INI_APU_AVAILABLE", 1)).GetValue(A350StateEvaluator.ApuOrExtPowerField));
        Assert.True(double.IsNaN(Evaluator(("INI_APU_AVAILABLE", 0)).GetValue(A350StateEvaluator.ApuOrExtPowerField)));
    }

    [Theory]
    [InlineData(3, 3, 1)]
    [InlineData(3, 0, 0)]
    [InlineData(0, 0, 0)]
    public void Both_baros_standard(double one, double two, double expected) =>
        Assert.Equal(expected, Evaluator(("XMLVAR_Baro1_Mode", one), ("XMLVAR_Baro2_Mode", two)).GetValue(A350StateEvaluator.BaroStdField));

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 0)]
    public void Both_baro_units_hectopascals(double one, double two, double expected) =>
        Assert.Equal(expected, Evaluator(("XMLVAR_Baro_Selector_HPA_1", one), ("XMLVAR_Baro_Selector_HPA_2", two)).GetValue(A350StateEvaluator.BaroUnitHectopascalsField));

    private static (string, double)[] Legs(double left, double centre, double right) => new[]
        { ("GEAR LEFT POSITION", left), ("GEAR CENTER POSITION", centre), ("GEAR RIGHT POSITION", right) };

    [Fact]
    public void The_gear_is_down_only_with_the_lever_down_and_every_leg_there()
    {
        Assert.Equal(1, Evaluator(Legs(1, 1, 1).Append(("A350_LDG_LEVER", 1)).ToArray()).GetValue(A350StateEvaluator.GearDownField));
        Assert.Equal(0, Evaluator(Legs(1, 0.9, 1).Append(("A350_LDG_LEVER", 1)).ToArray()).GetValue(A350StateEvaluator.GearDownField));
        Assert.Equal(0, Evaluator(Legs(1, 1, 1).Append(("A350_LDG_LEVER", 0)).ToArray()).GetValue(A350StateEvaluator.GearDownField));
        Assert.True(double.IsNaN(Evaluator(Legs(1, 1, 1)).GetValue(A350StateEvaluator.GearDownField)));
    }

    [Fact]
    public void The_gear_is_up_only_with_the_lever_up_and_every_leg_stowed()
    {
        Assert.Equal(1, Evaluator(Legs(0, 0, 0).Append(("A350_LDG_LEVER", 0)).ToArray()).GetValue(A350StateEvaluator.GearUpField));
        Assert.Equal(0, Evaluator(Legs(0, 0.1, 0).Append(("A350_LDG_LEVER", 0)).ToArray()).GetValue(A350StateEvaluator.GearUpField));
        Assert.Equal(0, Evaluator(Legs(0, 0, 0).Append(("A350_LDG_LEVER", 1)).ToArray()).GetValue(A350StateEvaluator.GearUpField));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void Takeoff_flaps_are_1_to_3(double lever, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_FLAPS", lever)).GetValue(A350StateEvaluator.TakeoffFlapsField));

    [Fact]
    public void The_takeoff_memo_reads_belts_spoilers_flaps_and_the_autobrake()
    {
        (string, double)[] Memo(double belts, double spoilers, double flaps, double brake) => new[]
            { ("A350_SIGNS_SEAT_BELTS", belts), ("A350_SPOILERS_ARM", spoilers), ("A350_FLAPS", flaps), ("INI_AUTOBRAKE_ARMED", brake) };
        Assert.Equal(1, Evaluator(Memo(0, 1, 2, 1)).GetValue(A350StateEvaluator.TakeoffMemoField));
        Assert.Equal(0, Evaluator(Memo(2, 1, 2, 1)).GetValue(A350StateEvaluator.TakeoffMemoField));
        Assert.Equal(0, Evaluator(Memo(0, 0, 2, 1)).GetValue(A350StateEvaluator.TakeoffMemoField));
        Assert.Equal(0, Evaluator(Memo(0, 1, 0, 1)).GetValue(A350StateEvaluator.TakeoffMemoField));
        Assert.Equal(0, Evaluator(Memo(0, 1, 2, 0)).GetValue(A350StateEvaluator.TakeoffMemoField));
        Assert.True(double.IsNaN(Evaluator(("A350_SIGNS_SEAT_BELTS", 0)).GetValue(A350StateEvaluator.TakeoffMemoField)));
    }

    // INI_LANDING_CONFIG (live 2026-10-30): 2 with PERF APPR at FULL, 1 at CONF 3.
    [Theory]
    [InlineData(4, 2, 1)]
    [InlineData(3, 2, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 1, 0)]
    public void The_landing_flaps_follow_the_perf_page(double lever, double config, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_FLAPS", lever), ("INI_LANDING_CONFIG", config)).GetValue(A350StateEvaluator.LandingFlapsField));

    // The manual's cockpit preparation: strobe AUTO, beacon, landing and nose lights off.
    [Theory]
    [InlineData(1, 0, 0, 2, 1)]
    [InlineData(0, 0, 0, 2, 0)]
    [InlineData(1, 1, 0, 2, 0)]
    [InlineData(1, 0, 1, 2, 0)]
    [InlineData(1, 0, 0, 1, 0)]
    public void The_cockpit_preparation_lights(double strobe, double beacon, double landing, double nose, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_LIGHTS_EXT_STROBE", strobe), ("A350_LIGHTS_EXT_BEACON", beacon), ("A350_LIGHTS_EXT_LANDING", landing), ("A350_LIGHTS_EXT_NOSE", nose))
            .GetValue(A350StateEvaluator.PrepLightsField));

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 0, 0)]
    public void The_weather_radar_is_on_with_both_sides_on(double one, double two, double expected) =>
        Assert.Equal(expected, Evaluator(("INI_WXR_ON", one), ("INI_WXR2_ON", two)).GetValue(A350StateEvaluator.WeatherRadarOnField));

    [Fact]
    public void The_speeds_read_the_aircrafts_own()
    {
        var state = Evaluator(("FMGEC_GD_SPD", 210), ("FCPC_S_SPEED", 180), ("FCPC_F_SPEED", 150), ("INI_VFE_SPEED", 255));
        Assert.Equal(210, state.GetValue(A350StateEvaluator.GreenDotField));
        Assert.Equal(180, state.GetValue(A350StateEvaluator.SSpeedField));
        Assert.Equal(150, state.GetValue(A350StateEvaluator.FSpeedField));
        Assert.Equal(255, state.GetValue(A350StateEvaluator.VfeNextField));
    }

    [Theory]
    [InlineData(1, "standard")]
    [InlineData(0, "QNH 1013")]
    public void The_baro_reads_as_said(double std, string said)
    {
        var state = Evaluator(("XMLVAR_Baro1_Mode", std > 0.5 ? 3 : 0), ("XMLVAR_Baro_Selector_HPA_1", 1), ("A350_FCU_CAPTAIN_BARO", 1013));
        Assert.Equal(said, state.BaroText());
    }

    [Fact]
    public void An_unread_baro_says_nothing() => Assert.Null(Evaluator().BaroText());

    [Fact]
    public void The_baro_says_the_unit_its_selector_shows()
    {
        var state = Evaluator(("XMLVAR_Baro1_Mode", 0), ("XMLVAR_Baro_Selector_HPA_1", 0), ("A350_FCU_CAPTAIN_BARO", 1013.25));
        Assert.Equal("QNH 29.92", state.BaroText());
    }

    // Review 3 (2026-10-30, I5): a latched line is judged on all it claims: "ADIRS: NAV" read IRS 1 alone, "Thrust levers: IDLE" lever 1
    // alone, "Cockpit door: CLOSED and LOCKED" the lock alone.
    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(1, 1, 2, 0)]
    public void Adirs_nav_is_all_three(double one, double two, double three, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_IRS_MODE_1", one), ("A350_IRS_MODE_2", two), ("A350_IRS_MODE_3", three)).GetValue(A350StateEvaluator.AdirsNavField));

    [Fact]
    public void Adirs_nav_unread_is_unknown() =>
        Assert.True(double.IsNaN(Evaluator(("A350_IRS_MODE_1", 1), ("A350_IRS_MODE_2", 1)).GetValue(A350StateEvaluator.AdirsNavField)));

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(0.5, -0.5, 1)]
    [InlineData(0, 25, 0)]
    [InlineData(25, 0, 0)]
    public void Levers_idle_is_both(double one, double two, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_ENGINE_THROTTLE_1", one), ("A350_ENGINE_THROTTLE_2", two)).GetValue(A350StateEvaluator.LeversIdleField));

    [Fact]
    public void Levers_idle_unread_is_unknown() =>
        Assert.True(double.IsNaN(Evaluator(("A350_ENGINE_THROTTLE_1", 0)).GetValue(A350StateEvaluator.LeversIdleField)));

    [Theory]
    [InlineData(0, 1, 1)]   // closed, locked
    [InlineData(1, 1, 0)]   // open, its lock set
    [InlineData(0, 0, 0)]   // closed, unlocked
    public void The_cockpit_door_is_closed_and_locked(double open, double locked, double expected) =>
        Assert.Equal(expected, Evaluator(("A350_COCKPIT_DOOR_HANDLE_BASE_CS", open), ("A350_COCKPIT_DOOR_LOCK", locked)).GetValue(A350StateEvaluator.CockpitDoorSecureField));

    // Before Start's "T.O data" and Before Takeoff's "V1, VR, V2 and thrust rating", as the A380's read theirs: from the FMS's
    // flight plan in the reader's copy (INI_V1, INI_VR and INI_V2 are never written), flex only when set.
    [Theory]
    [InlineData(142, 143, 148, 67, "V1 142, VR 143, V2 148, flex 67")]
    [InlineData(142, 143, 148, 0, "V1 142, VR 143, V2 148")]
    [InlineData(0, 0, 0, 0, "not set")]
    public void The_takeoff_data_reads_the_fms(int v1, int vr, int v2, int flex, string said)
    {
        var def = new IniBuildsA350Definition();
        var speech = new SpeechCapture();
        for (int i = 0; i < 2; i++)
            def.ObserveTakeoffSpeeds(new A350TakeoffSpeeds(v1, vr, v2, flex), speech);
        Assert.Equal(said, new A350StateEvaluator(def).TakeoffDataText());
    }

    [Fact]
    public void The_takeoff_data_unread_says_nothing()
    {
        Assert.Null(new A350StateEvaluator(new IniBuildsA350Definition()).TakeoffDataText());
        Assert.Null(new A350StateEvaluator().TakeoffDataText());
    }

    [Fact]
    public void The_fuel_says_kilograms()
    {
        Assert.Equal("40,000 kilograms", Evaluator(("FUEL TOTAL QUANTITY WEIGHT", 40000)).FuelText());
        Assert.Null(Evaluator().FuelText());
    }
}
