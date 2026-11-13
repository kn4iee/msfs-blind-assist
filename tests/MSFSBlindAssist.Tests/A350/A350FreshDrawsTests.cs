using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 26's re-review: which draw a display window shows on each side (<see cref="A350FreshDraws"/>), shared by the display
/// windows and the ND window. A press's fresh draw is shown over the monitor's draw from before it, on its own side only; a newer draw of
/// the monitor's replaces it and it is dropped; with no draw of the monitor's at all it stands while it is young.</summary>
public class A350FreshDrawsTests
{
    private static A350DrawCapture Draw() => new("EFIS", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);

    private long _now = 1_000;

    private A350FreshDraws Draws() => new(() => _now, maxAgeMs: 30_000);

    [Fact]
    public void A_fresh_draw_is_shown_over_the_draw_from_before_it_and_a_newer_one_replaces_it()
    {
        var draws = Draws();
        var before = Draw();
        var fresh = Draw();
        draws.Took(0, fresh, before);
        Assert.Same(fresh, draws.ToShow(0, before));
        var newer = Draw();
        Assert.Same(newer, draws.ToShow(0, newer));
        Assert.Null(draws.ToShow(0, null));   // dropped once a newer draw was shown: never shown again
    }

    [Fact]
    public void A_fresh_draw_is_its_own_sides()
    {
        var draws = Draws();
        var captainsLatest = Draw();
        var firstOfficersBefore = Draw();
        var firstOfficersFresh = Draw();
        draws.Took(1, firstOfficersFresh, firstOfficersBefore);
        Assert.Same(captainsLatest, draws.ToShow(0, captainsLatest));
        Assert.Same(firstOfficersFresh, draws.ToShow(1, firstOfficersBefore));
    }

    // With no draw of the monitor's (just after a flight load, or none young enough) the fresh draw stands while it is young.
    [Fact]
    public void With_no_latest_draw_a_young_fresh_draw_stands()
    {
        var draws = Draws();
        var fresh = Draw();
        draws.Took(0, fresh, null);
        Assert.Same(fresh, draws.ToShow(0, null));
        _now += 30_001;
        Assert.Null(draws.ToShow(0, null));
    }
}
