using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Review 3 (M13): what the A350 speaks and sends in a comma-decimal culture: CI runs under en-US, so nothing else catches it.</summary>
public sealed class A350CommaCultureTests : IDisposable
{
    private readonly CultureInfo _was = CultureInfo.CurrentCulture;

    public A350CommaCultureTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _was;

    private static readonly A350NdMap.NdMapFrame Frame = new(400, 400, false, 2);

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_map_places_keep_their_point(string culture)
    {
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        Assert.DoesNotContain(",5", A350NdMap.PlaceNm(Frame, 400, 395));
        Assert.Contains("2.5 NM", A350NdMap.PlaceNm(Frame, 400, 395));
    }

    [Fact]
    public void A_map_place_in_metres_has_no_decimal_comma()
    {
        var frame = new A350NdMap.NdMapFrame(400, 400, false, 0.5f);
        Assert.Matches(@"^12 o'clock, \d+ m$", A350NdMap.Place(frame, 400, 300)!);
    }

    [Fact]
    public void The_status_figures_keep_their_point()
    {
        Assert.Equal("12.5 quarts", A350StatusLines.Quarts(12.5));
        Assert.DoesNotContain(",", A350StatusLines.WholePercent(62.4));
    }

    [Fact]
    public void A_screen_frequency_reads_in_hertz()
    {
        var lines = new[] { new RmpLine("VHF1: active 121.500, green; standby 118.700, cyan") };
        Assert.Equal(118_700_000, A350RmpPage.Vhf1Hz(lines, false));
        Assert.Equal(121_500_000, A350RmpPage.Vhf1Hz(lines, true));
    }

    [Fact]
    public void A_tuned_frequency_becomes_its_digits()
    {
        var digitKeys = "0123456789".Select(c => A350Rmp.KeyFor(c)!).ToHashSet();
        Assert.Equal("118700".Select(c => A350Rmp.KeyFor(c)!), A350Rmp.ComTuningKeys(118_700_000, false).Where(digitKeys.Contains));
        Assert.Equal("11390".Select(c => A350Rmp.KeyFor(c)!), A350Rmp.TuningKeys("113.90", true)!.Where(digitKeys.Contains));
    }

    [Fact]
    public void The_mach_keeps_its_point()
    {
        var lines = A350Pfd.ReadParts(A350Fixtures.Capture("efis0_cruise_wxt.json"), 0).Lines;
        Assert.DoesNotContain(lines, l => l.StartsWith("Mach: ", StringComparison.Ordinal) && l.Contains("0,", StringComparison.Ordinal));
    }
}
