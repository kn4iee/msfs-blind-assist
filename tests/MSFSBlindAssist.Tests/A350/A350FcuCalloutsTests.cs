using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 9 (2026-10-29): the FCU altitude window and both baro windows speak on their own, as the A300 and
/// the A380 do, in the A380's words.</summary>
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
    public void Only_the_altitude_and_baro_windows_speak_on_their_own() =>
        Assert.Equal(new[] { "Altitude window", "Captain baro", "First officer baro" }, A350FcuCallouts.Speaking);

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
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
