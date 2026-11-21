using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Regression tests for the A330 FCU value dialogs and the Ctrl+P autopilot window.</summary>
public class A330ReviewTests
{
    [Fact]
    public void Autopilot_window_rows_have_distinct_access_keys()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var keys = def.AutopilotRows(sim, null!)
            .Select(r => r.Label)
            .Where(l => l.Contains('&'))
            .Select(l => char.ToUpperInvariant(l[l.IndexOf('&') + 1]))
            .ToList();
        var dups = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, "shared access keys: " + string.Join(", ", dups));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Typed_numbers_read_the_same_on_every_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            // FPA: "-0,5" is half a degree, never -5.0
            Assert.True(IniA330Definition.TryParseVs("-0,5", true, out double fpa));
            Assert.Equal(-0.5, fpa);
            Assert.True(IniA330Definition.TryParseVs("-2.5", true, out fpa));
            Assert.Equal(-2.5, fpa);
            // Mach with a comma
            Assert.True(IniA330Definition.TryParseSpeed("0,82", out double mach));
            Assert.Equal(0.82, mach);
            Assert.True(IniA330Definition.TryParseSpeed("0.82", out mach));
            Assert.Equal(0.82, mach);
            // inHg with a comma is 29.92 inHg, not 2992
            Assert.True(IniA330Definition.TryParseBaro("29,92", out double hpa));
            Assert.Equal(29.92 * 33.8639, hpa, 6);
            Assert.True(IniA330Definition.TryParseBaro("1013", out hpa));
            Assert.Equal(1013, hpa);
            // whole numbers
            Assert.True(IniA330Definition.TryParseHeading("270", out int h));
            Assert.Equal(270, h);
            Assert.True(IniA330Definition.TryParseAltitude("35000", out int a));
            Assert.Equal(35000, a);
            return 0;
        });
    }

    [Fact]
    public void Standard_button_leaves_its_verdict_to_ToggleStandard()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var std = Assert.Single(def.BaroToggles(sim, null!));
        Assert.True(std.SuppressStateAnnounce?.Invoke() == true);
    }

    [Fact]
    public void Ambiguous_or_out_of_range_input_is_refused()
    {
        Assert.False(IniA330Definition.TryParseVs("-10", true, out _));
        Assert.False(IniA330Definition.TryParseVs("6,001", false, out _));   // a thousands group, refused
        Assert.False(IniA330Definition.TryParseSpeed("NaN", out _));
        Assert.False(IniA330Definition.TryParseHeading("abc", out _));
        Assert.False(IniA330Definition.TryParseHeading("361", out _));
        Assert.False(IniA330Definition.TryParseAltitude("99", out _));
    }
}
