using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// A copy can span two A350 frames and draw a half-updated screen [A350-2]: a value read from a draw is
// taken only once two reads in a row agree, so a torn frame is never announced.
public class A350DisplayServiceTests
{
    [Fact]
    public void A_value_is_taken_after_two_agreeing_reads()
    {
        var s = new A350StableValue<string>();
        Assert.False(s.Offer("PARK BRK", out _));          // first sight
        Assert.True(s.Offer("PARK BRK", out var v));       // confirmed
        Assert.Equal("PARK BRK", v);
        Assert.False(s.Offer("PARK BRK", out _));          // unchanged: nothing new
        Assert.Equal("PARK BRK", s.Current);
    }

    [Fact]
    public void A_one_read_flicker_is_never_taken()
    {
        var s = new A350StableValue<string>();
        s.Offer("A", out _); s.Offer("A", out _);
        Assert.False(s.Offer("garbage", out _));           // a torn frame
        Assert.False(s.Offer("A", out _));                 // back to what was taken: nothing new
        Assert.Equal("A", s.Current);
        Assert.False(s.Offer("B", out _));
        Assert.True(s.Offer("B", out var v));
        Assert.Equal("B", v);
    }

    [Fact]
    public void Reset_forgets_what_was_taken()
    {
        var s = new A350StableValue<int>();
        s.Offer(1, out _); s.Offer(1, out _);
        s.Reset();
        Assert.False(s.HasValue);
        Assert.False(s.Offer(1, out _));
        Assert.True(s.Offer(1, out _));
    }

    [Fact]
    public void The_status_is_spoken_in_plain_words()
    {
        Assert.Equal("A350 displays getting ready", A350DisplayStatus.GettingReady);
        Assert.Equal("A350 displays ready", A350DisplayStatus.Ready);
        Assert.Equal("A350 displays not available: the A350 module has no function CTR::Draw()",
            A350DisplayStatus.NotAvailable("the A350 module has no function CTR::Draw()"));
    }
}
