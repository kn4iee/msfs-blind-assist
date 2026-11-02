using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 9 (2026-10-29): the FCU altitude window and both baro windows speak on their own, as the A300 and
/// the A380 do, in the A380's words. Plan 02 (2026-11-02, in flight): the speed, heading and V/S knobs turned from outside MSFSBA
/// said nothing, where the A380 and the A320 say all four windows; now they do.</summary>
public class A350FcuCalloutsTests
{
    private static Func<string, double?> Read(params (string Name, double Value)[] values)
    {
        var map = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return name => map.TryGetValue(name, out var v) ? v : null;
    }

    private static Func<string, double?> Baro(int side, double mode, double hpaUnit, double mb, double preset = 0, double shown = 0) => Read(
        ($"XMLVAR_Baro{side}_Mode", mode), ($"XMLVAR_Baro_Selector_HPA_{side}", hpaUnit), ($"KOHLSMAN SETTING MB:{side}", mb),
        (A350TypedEntry.PresetVar(side), preset), (A350TypedEntry.PresetVar(side) + "_SHOW", shown));

    [Fact]
    public void The_four_fcu_windows_and_both_baros_speak_on_their_own() =>
        Assert.Equal(new[] { "Speed window", "Heading window", "Altitude window", "V/S window", "Captain baro", "First officer baro" },
            A350FcuCallouts.Speaking);

    private static Func<string, double?> SpeedWindow(double dial, double dashed = 0, double managed = 0, double preset = 0, double presetIsMach = 0) => Read(
        ("INI_AIRSPEED_DIAL", dial), ("INI_FCU_SPD_DASHED", dashed), ("INI_FCU_SPD_DOT", managed), ("INI_MANAGED_SPEED", managed),
        ("INI_PRESET_DIAL", preset), ("INI_PRESET_DIAL_IS_MACH", presetIsMach));

    [Fact]
    public void A_speed_knob_turned_from_hardware_is_said_in_the_a380s_words()
    {
        Assert.Equal("Speed 251 knots", A350FcuCallouts.Phrase("Speed window", SpeedWindow(251)));
        Assert.Equal("Mach 0.78", A350FcuCallouts.Phrase("Speed window", SpeedWindow(0.78)));
        Assert.Equal("Speed 260 knots, preset", A350FcuCallouts.Phrase("Speed window", SpeedWindow(229, managed: 1, preset: 260)));
        Assert.Equal("Mach 0.80, preset", A350FcuCallouts.Phrase("Speed window", SpeedWindow(229, managed: 1, preset: 0.8, presetIsMach: 1)));
        Assert.Null(A350FcuCallouts.Phrase("Speed window", SpeedWindow(229, dashed: 1, managed: 1)));
        Assert.Null(A350FcuCallouts.Phrase("Speed window", Read(("INI_AIRSPEED_DIAL", 250))));
    }

    private static Func<string, double?> HeadingWindow(double heading, double track = 0, double trkFpa = 0, double dashed = 0, double roll = 2) => Read(
        ("INI_HEADING_DIAL", heading), ("INI_TRK_DIAL", track), ("INI_TRACK_FPA_STATE", trkFpa), ("INI_FCU_HDG_DASHED", dashed),
        ("INI_FCU_HDG_DOT", 0), ("INI_ROLL_MODE", roll));

    [Fact]
    public void A_heading_knob_turned_from_hardware_is_said_in_the_a380s_words()
    {
        Assert.Equal("Heading 271 degrees", A350FcuCallouts.Phrase("Heading window", HeadingWindow(271)));
        Assert.Equal("Heading 005 degrees", A350FcuCallouts.Phrase("Heading window", HeadingWindow(5.6)));
        Assert.Equal("Track 090 degrees", A350FcuCallouts.Phrase("Heading window", HeadingWindow(271, track: 90, trkFpa: 1)));
        Assert.Equal("Heading 300 degrees, preset", A350FcuCallouts.Phrase("Heading window", HeadingWindow(300, roll: 1)));
        Assert.Null(A350FcuCallouts.Phrase("Heading window", HeadingWindow(271, dashed: 1)));
    }

    private static Func<string, double?> VsWindow(double vs, double fpa = 0, double trkFpa = 0, double dashed = 0) => Read(
        ("INI_VVI_DIAL", vs), ("INI_FPA_DIAL", fpa), ("INI_TRACK_FPA_STATE", trkFpa), ("INI_FCU_VS_DASHED", dashed));

    [Fact]
    public void A_vs_knob_turned_from_hardware_is_said_in_the_a380s_words()
    {
        Assert.Equal("Vertical speed -100 feet per minute", A350FcuCallouts.Phrase("V/S window", VsWindow(-100)));
        Assert.Equal("Vertical speed 1500 feet per minute", A350FcuCallouts.Phrase("V/S window", VsWindow(1500)));
        Assert.Equal("Vertical speed 0 feet per minute", A350FcuCallouts.Phrase("V/S window", VsWindow(-0.0)));
        Assert.Equal("FPA -3.0 degrees", A350FcuCallouts.Phrase("V/S window", VsWindow(0, fpa: -3, trkFpa: 1)));
        Assert.Null(A350FcuCallouts.Phrase("V/S window", VsWindow(-100, dashed: 1)));
    }

    [Fact]
    public void The_altitude_window_says_its_feet()
    {
        Assert.Equal("Altitude 12000 feet", A350FcuCallouts.Phrase("Altitude window", Read(("INI_ALTITUDE_DIAL", 12000))));
        Assert.Null(A350FcuCallouts.Phrase("Altitude window", Read()));
    }

    // The A380's words (BaroPhrase): the side, "altimeter", the setting in the side's unit; STD as "standard".
    [Theory]
    [InlineData(1, 1, 1013.25, "Captain altimeter 1013 hectopascals")]
    [InlineData(2, 1, 1015.81, "First officer altimeter 1015 hectopascals")]   // floored, as the knob's own tooltip
    [InlineData(1, 0, 29.92 * 33.8639, "Captain altimeter 29.92 inches")]
    public void A_baro_on_qnh_says_its_setting_in_its_unit(int side, double unit, double mb, string expected) =>
        Assert.Equal(expected, A350FcuCallouts.Phrase(side == 1 ? "Captain baro" : "First officer baro", Baro(side, 0, unit, mb)));

    [Fact]
    public void A_baro_on_std_says_standard_or_the_preset_it_shows()
    {
        Assert.Equal("Captain altimeter standard", A350FcuCallouts.Phrase("Captain baro", Baro(1, 3, 1, 1013.25)));
        Assert.Equal("Captain altimeter preset 1015 hectopascals", A350FcuCallouts.Phrase("Captain baro", Baro(1, 3, 1, 1013.25, 1015, 1)));
        Assert.Equal("First officer altimeter preset 29.92 inches", A350FcuCallouts.Phrase("First officer baro", Baro(2, 3, 0, 1013.25, 2992, 1)));
        Assert.Null(A350FcuCallouts.Phrase("Captain baro", Read(("XMLVAR_Baro1_Mode", 0))));   // unread: nothing to say
    }

    // The preset's display times out while the baro stays on STD: nobody changed anything.
    [Fact]
    public void A_preset_going_out_of_view_is_quiet()
    {
        Assert.True(A350FcuCallouts.IsQuiet("Captain altimeter preset 1015 hectopascals", "Captain altimeter standard"));
        Assert.False(A350FcuCallouts.IsQuiet("Captain altimeter standard", "Captain altimeter preset 1015 hectopascals"));
        Assert.False(A350FcuCallouts.IsQuiet("Captain altimeter 1013 hectopascals", "Captain altimeter standard"));
        Assert.False(A350FcuCallouts.IsQuiet(null, "Captain altimeter standard"));
    }

    [Fact]
    public void Numbers_read_the_same_in_every_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Altitude 12000 feet", A350FcuCallouts.Phrase("Altitude window", Read(("INI_ALTITUDE_DIAL", 12000))));
            Assert.Equal("Captain altimeter 29.92 inches", A350FcuCallouts.Phrase("Captain baro", Baro(1, 0, 0, 29.92 * 33.8639)));
            Assert.Equal("Mach 0.78", A350FcuCallouts.Phrase("Speed window", SpeedWindow(0.78)));
            Assert.Equal("FPA -3.0 degrees", A350FcuCallouts.Phrase("V/S window", VsWindow(0, fpa: -3, trkFpa: 1)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
