// Characterization tests for Navigation.PairedCrossingHoldWindow: when a second Continue counts as
// "cleared across both" for a paired-runway crossing. The window opens a delay after the offer, so a
// key bounce or a quick double press (milliseconds after the first) never clears the second runway's
// stop (PR #324 review). The window comes from Open, the same call ContinuePastHoldShort makes, so a
// changed delay or a swapped bound fails here.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class PairedCrossingHoldWindowTests
{
    private static readonly DateTime Offered = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static bool Accepts(double secondsAfterOffer)
    {
        var (from, until) = PairedCrossingHoldWindow.Open(Offered);
        return PairedCrossingHoldWindow.Accepts(Offered.AddSeconds(secondsAfterOffer), from, until);
    }

    [Fact]
    public void The_window_opens_two_seconds_after_the_offer_and_stands_for_twelve()
    {
        Assert.Equal(2.0, PairedCrossingHoldWindow.MinDelaySeconds);
        Assert.Equal(12.0, PairedCrossingHoldWindow.WindowSeconds);
        Assert.Equal((Offered.AddSeconds(2.0), Offered.AddSeconds(12.0)), PairedCrossingHoldWindow.Open(Offered));
    }

    [Theory]
    [InlineData(0.0)]   // the same frame as the first press
    [InlineData(0.05)]  // a key bounce
    [InlineData(1.9)]   // a quick "to be sure" double press
    public void A_press_before_the_window_opens_is_not_a_second_clearance(double secondsAfterOffer)
        => Assert.False(Accepts(secondsAfterOffer));

    [Theory]
    [InlineData(2.0)]
    [InlineData(6.0)]
    [InlineData(12.0)]
    public void A_press_inside_the_window_is_the_second_clearance(double secondsAfterOffer)
        => Assert.True(Accepts(secondsAfterOffer));

    [Fact]
    public void A_press_after_the_window_closes_is_not_a_second_clearance()
        => Assert.False(Accepts(12.1));

    // Owner, 2026-11-25 ("Ignore it"): a Continue before the line pressed straight after the Continue that resumed
    // taxiing (a "to be sure" double press at a hold) would clear the next runway's stop whose countdown latched within
    // a frame, under ~90 m ahead. Inside the same delay it is ignored; after it, it works as before.
    private static readonly DateTime Resumed = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0.0)]   // the same frame
    [InlineData(0.05)]  // a key bounce
    [InlineData(1.0)]   // a double press, after the next stop's first countdown call
    [InlineData(1.99)]
    public void An_early_Continue_right_after_the_resuming_one_is_ignored(double secondsAfterResume)
        => Assert.True(PairedCrossingHoldWindow.IgnoresEarlyContinue(Resumed.AddSeconds(secondsAfterResume), Resumed));

    [Theory]
    [InlineData(2.0)]
    [InlineData(6.0)]
    [InlineData(600.0)]
    public void An_early_Continue_once_the_delay_has_passed_works(double secondsAfterResume)
        => Assert.False(PairedCrossingHoldWindow.IgnoresEarlyContinue(Resumed.AddSeconds(secondsAfterResume), Resumed));

    [Fact]
    public void With_no_resuming_Continue_nothing_is_ignored()
    {
        Assert.False(PairedCrossingHoldWindow.IgnoresEarlyContinue(Resumed, DateTime.MinValue));
        Assert.False(PairedCrossingHoldWindow.IgnoresEarlyContinue(Resumed.AddSeconds(-1), Resumed));   // a clock set back
    }
}
