using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28 every draw trapped on a torn copy for many minutes and the displays simply went quiet: the FMA
// call-outs, the ECAM, the MFD window. A few failed draws in a row are normal; when every draw has failed for ten
// seconds the pilot is told once, and told again once the displays read steadily.
public class A350ReadHealthTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>A quick draw (it ends as it starts) at <paramref name="s"/> seconds.</summary>
    private static string? At(A350ReadHealth health, bool ok, int s) => health.Report(ok, T0.AddSeconds(s), T0.AddSeconds(s));

    /// <summary>A draw failing every second from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Fail(A350ReadHealth health, int from, int to)
    {
        for (int s = from; s < to; s++)
            At(health, false, s);
    }

    [Fact]
    public void A_few_failed_draws_say_nothing()
    {
        var health = new A350ReadHealth();
        Assert.Null(At(health, false, 0));
        Assert.Null(At(health, false, 5));
    }

    [Fact]
    public void Draws_failing_for_ten_seconds_are_said_once()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 10);
        Assert.Equal("A350 displays cannot be read at the moment", At(health, false, 10));
        Assert.Null(At(health, false, 11));
    }

    // Review 2026-10-28: one good draw among failures said "readable again", and the next failures "cannot be read"
    // again, over and over. The displays read again once three draws in a row are good.
    [Fact]
    public void The_displays_read_again_after_three_good_draws_in_a_row()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 11);
        Assert.Null(At(health, true, 11));
        Assert.Null(At(health, false, 12));   // a lone good draw: still unreadable, nothing said
        Assert.Null(At(health, true, 13));
        Assert.Null(At(health, true, 14));
        Assert.Equal("A350 displays readable again", At(health, true, 15));
        Assert.Null(At(health, true, 16));
    }

    [Fact]
    public void A_good_draw_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 5);
        Assert.Null(At(health, true, 5));
        Fail(health, 6, 16);
        Assert.Equal("A350 displays cannot be read at the moment", At(health, false, 16));
    }

    // Review 2026-10-28: a pause in the draws (no A350 found while a flight loads, no window asking) kept the first
    // failure's time, so the first torn draw after it said "cannot be read" at once. A pause starts the window again.
    [Fact]
    public void A_pause_in_the_draws_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        At(health, false, 0);
        Assert.Null(At(health, false, 60));
        Fail(health, 61, 70);
        Assert.Equal("A350 displays cannot be read at the moment", At(health, false, 70));
    }

    // Live 2026-10-28, the A350's memory at 495 MB: one draw took 5 to 9 seconds and every one failed for a minute,
    // yet nothing was said, because each gap between two draws' ends was longer than the pause. A pause is time with
    // no draw running: from one draw's end to the next one's start.
    [Fact]
    public void Slow_draws_back_to_back_are_not_a_pause()
    {
        var health = new A350ReadHealth();
        Assert.Null(health.Report(false, T0, T0.AddSeconds(8)));
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(8), T0.AddSeconds(16)));
    }
}
