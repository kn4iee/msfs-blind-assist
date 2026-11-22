using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The A330's take-off call-outs follow the shared contract ([TKO-5]): one keys record, a feed
/// that pauses once nothing can be called, and a mute row per speed.</summary>
[Collection("SettingsManagerGlobalState")]
public class A330TakeoffCalloutTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A330TakeoffCalloutTests()
    {
        _savedMutes = SettingsManager.Current.A330DisabledMonitorVariables;
        Mute();
    }

    public void Dispose()
    {
        SettingsManager.Current.A330DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.A330DisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static IniA330Definition Parked(SpeechCapture speech)
    {
        var def = new IniA330Definition();
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffV1Key, 142, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffVrKey, 148, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffV2Key, 155, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffIasKey, 5, speech);
        return def;
    }

    private static void Roll(IniA330Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples) def.ProcessSimVarUpdate(IniA330Definition.TakeoffIasKey, ias, speech);
    }

    [Fact]
    public void The_airspeed_feed_is_named_and_pauses_once_nothing_can_be_called()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Assert.Equal(IniA330Definition.TakeoffIasKey, def.TakeoffCalloutFeedKey);
        Assert.True(def.TakeoffCalloutFeedNeeded);                       // on the ground: a roll can start any moment

        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Assert.True(def.TakeoffCalloutFeedNeeded);                       // airborne but V2 not yet called
        Roll(def, speech, 156);
        Assert.False(def.TakeoffCalloutFeedNeeded);                      // V2 called: the feed pauses until touchdown
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        Assert.True(def.TakeoffCalloutFeedNeeded);
    }

    [Fact]
    public void The_feed_is_kept_out_of_the_mute_wrap()
    {
        var def = new IniA330Definition();
        Assert.True(def.IsMuteWrapExempt(IniA330Definition.TakeoffIasKey));
        Assert.False(def.IsMuteWrapExempt(IniA330Definition.TakeoffV1Key));
    }

    [Fact]
    public void A_roll_calls_v1_rotate_and_v2_in_order()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 141, 143, 147, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Theory]
    [InlineData(IniA330Definition.TakeoffV1Key, "Rotate", "V2")]
    [InlineData(IniA330Definition.TakeoffVrKey, "V1", "V2")]
    [InlineData(IniA330Definition.TakeoffV2Key, "V1", "Rotate")]
    public void A_muted_row_silences_only_its_own_call(string mutedKey, string first, string second)
    {
        Mute(mutedKey);
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Equal(new[] { first, second }, speech.Interrupts);
    }

    [Fact]
    public void All_three_rows_muted_say_nothing()
    {
        Mute(IniA330Definition.TakeoffV1Key, IniA330Definition.TakeoffVrKey, IniA330Definition.TakeoffV2Key);
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void The_mute_rows_exist_in_ctrl_m_and_the_feed_does_not()
    {
        var rows = MonitorRowBuilder.Build(new IniA330Definition().GetVariables()).Select(r => r.Key).ToList();
        Assert.Contains(IniA330Definition.TakeoffV1Key, rows);
        Assert.Contains(IniA330Definition.TakeoffVrKey, rows);
        Assert.Contains(IniA330Definition.TakeoffV2Key, rows);
        Assert.DoesNotContain(IniA330Definition.TakeoffIasKey, rows);
    }
}

/// <summary>Every number the A330's read-outs speak reads the same on a comma-decimal PC.</summary>
public class A330ReadoutCultureTests
{
    public static TheoryData<string> Cultures() => new() { "sv-SE", "de-DE", "en-US" };

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Distances_to_go_read_whole_miles_and_minutes_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            Assert.Equal("Destination 123 miles, 16 minutes", IniA330Definition.DescribeFmsDistance(123.4, 450, false));
            Assert.Equal("1235 miles to top of descent, 2 hours 45 minutes", IniA330Definition.DescribeFmsDistance(1234.5, 450, true));
            Assert.Equal("Destination 99 miles", IniA330Definition.DescribeFmsDistance(99.4, 0, false));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_fuel_summary_has_no_group_or_decimal_separator_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var sim = Tests.FirstOfficer.SeededSimConnectCache.With(
                ("A330_IS_METRIC", 1), ("A330_FUEL_LO", 12345.6), ("A330_FUEL_LI", 20000.4), ("A330_FUEL_TOTAL_LB", 100000));
            var def = new IniA330Definition();
            Assert.Equal("Fuel 45359 kilograms. left outer 12346 kilograms, left inner 20000 kilograms", def.FuelSummary(sim));
            var lb = Tests.FirstOfficer.SeededSimConnectCache.With(("A330_IS_METRIC", 0), ("A330_FUEL_LO", 12345.6), ("A330_FUEL_TOTAL_LB", 100000));
            Assert.Equal("Fuel 100000 pounds. left outer 27217 pounds", def.FuelSummary(lb));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_hotkey_readouts_read_mach_and_flight_path_angle_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            string Read(Hotkeys.HotkeyAction action, params (string, double)[] cache)
            {
                var sim = Tests.FirstOfficer.SeededSimConnectCache.With(cache);
                var speech = new SpeechCapture();
                Assert.True(new IniA330Definition().HandleHotkeyAction(action, sim, speech, null!, null!));
                return Assert.Single(speech.Interrupts);
            }
            Assert.StartsWith("FCU Mach 0.78", Read(Hotkeys.HotkeyAction.ReadSpeed, ("A330_M_MACH", 1), ("A330_FCU_SPD", 0.78)));
            Assert.StartsWith("FCU flight path angle -3.0 degrees",
                Read(Hotkeys.HotkeyAction.ReadFCUVerticalSpeedFPA, ("A330_M_TRKFPA", 1), ("A330_FCU_FPA", -3)));
            Assert.StartsWith("FCU flight path angle 0.5 degrees",
                Read(Hotkeys.HotkeyAction.ReadFCUVerticalSpeedFPA, ("A330_M_TRKFPA", 1), ("A330_FCU_FPA", 0.5)));
            return 0;
        });
}
