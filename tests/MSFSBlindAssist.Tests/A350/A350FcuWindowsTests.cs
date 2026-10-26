using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The FCU's four windows as the FCU shows them, from the A350's own variables (live 2026-10-26).</summary>
public sealed class A350FcuWindowsTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public A350FcuWindowsTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    private static string? Read(A350FcuWindow window, params (string Var, double Value)[] values) =>
        window.Compose(v => values.Where(p => p.Var == v).Select(p => (double?)p.Value).FirstOrDefault());

    private static A350FcuWindow W(string name) => A350FcuWindows.All.Single(w => w.Name == name);

    [Fact]
    public void The_four_windows_are_on_the_fcu_panel_in_cockpit_order() =>
        Assert.Equal(new[] { "Speed window", "Heading window", "Altitude window", "V/S window" }, A350FcuWindows.All.Select(w => w.Name));

    [Fact]
    public void Speed_reads_knots_or_mach_from_one_variable()
    {
        Assert.Equal("250 knots", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 250), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0)));
        Assert.Equal("Mach 0.78", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 0.78), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0)));
    }

    [Fact]
    public void A_dashed_window_reads_dashes_and_its_managed_dot()
    {
        Assert.Equal("Dashes, managed", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 250), ("INI_FCU_SPD_DASHED", 1), ("INI_FCU_SPD_DOT", 1)));
        Assert.Equal("Dashes", Read(W("V/S window"), ("INI_VVI_DIAL", 0), ("INI_FPA_DIAL", 0), ("INI_FCU_VS_DASHED", 1), ("INI_TRACK_FPA_STATE", 0)));
    }

    [Fact]
    public void Heading_reads_heading_or_track_by_the_fcu_mode()
    {
        Assert.Equal("Heading 218", Read(W("Heading window"), ("INI_HEADING_DIAL", 218.9), ("INI_TRK_DIAL", 5), ("INI_TRACK_FPA_STATE", 0),
            ("INI_FCU_HDG_DASHED", 0), ("INI_FCU_HDG_DOT", 0)));
        Assert.Equal("Track 005", Read(W("Heading window"), ("INI_HEADING_DIAL", 218.9), ("INI_TRK_DIAL", 5), ("INI_TRACK_FPA_STATE", 1),
            ("INI_FCU_HDG_DASHED", 0), ("INI_FCU_HDG_DOT", 0)));
    }

    [Fact]
    public void Altitude_reads_feet_and_its_managed_dot()
    {
        Assert.Equal("10000 feet, managed", Read(W("Altitude window"), ("INI_ALTITUDE_DIAL", 10000), ("INI_FCU_ALT_DOT", 1)));
    }

    [Fact]
    public void Vertical_reads_vs_or_fpa_by_the_fcu_mode()
    {
        Assert.Equal("V/S -1200 feet per minute", Read(W("V/S window"), ("INI_VVI_DIAL", -1200), ("INI_FPA_DIAL", -3), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 0)));
        Assert.Equal("V/S +500 feet per minute", Read(W("V/S window"), ("INI_VVI_DIAL", 500), ("INI_FPA_DIAL", 0), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 0)));
        Assert.Equal("FPA -3.0 degrees", Read(W("V/S window"), ("INI_VVI_DIAL", -1200), ("INI_FPA_DIAL", -3), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 1)));
    }

    [Fact]
    public void An_unread_input_composes_nothing() =>
        Assert.Null(Read(W("Altitude window"), ("INI_ALTITUDE_DIAL", 10000)));
}
