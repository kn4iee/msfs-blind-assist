using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28 every draw trapped on a torn copy for many minutes and the displays simply went quiet: the FMA
// call-outs, the ECAM, the MFD window. A few failed draws in a row are normal; when every draw has failed for ten
// seconds the pilot is told once, and told again once the displays read steadily.
public class A350ReadHealthTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>A draw failing every second from <paramref name="from"/> to <paramref name="to"/> seconds.</summary>
    private static void Fail(A350ReadHealth health, int from, int to)
    {
        for (int s = from; s < to; s++)
            health.Report(false, T0.AddSeconds(s));
    }

    [Fact]
    public void A_few_failed_draws_say_nothing()
    {
        var health = new A350ReadHealth();
        Assert.Null(health.Report(false, T0));
        Assert.Null(health.Report(false, T0.AddSeconds(5)));
    }

    [Fact]
    public void Draws_failing_for_ten_seconds_are_said_once()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 10);
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(10)));
        Assert.Null(health.Report(false, T0.AddSeconds(11)));
    }

    // Review 2026-10-28: one good draw among failures said "readable again", and the next failures "cannot be read"
    // again, over and over. The displays read again once three draws in a row are good.
    [Fact]
    public void The_displays_read_again_after_three_good_draws_in_a_row()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 11);
        Assert.Null(health.Report(true, T0.AddSeconds(11)));
        Assert.Null(health.Report(false, T0.AddSeconds(12)));   // a lone good draw: still unreadable, nothing said
        Assert.Null(health.Report(true, T0.AddSeconds(13)));
        Assert.Null(health.Report(true, T0.AddSeconds(14)));
        Assert.Equal("A350 displays readable again", health.Report(true, T0.AddSeconds(15)));
        Assert.Null(health.Report(true, T0.AddSeconds(16)));
    }

    [Fact]
    public void A_good_draw_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        Fail(health, 0, 5);
        Assert.Null(health.Report(true, T0.AddSeconds(5)));
        Fail(health, 6, 16);
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(16)));
    }

    // Review 2026-10-28: a pause in the draws (no A350 found while a flight loads, no window asking) kept the first
    // failure's time, so the first torn draw after it said "cannot be read" at once. A pause starts the window again.
    [Fact]
    public void A_pause_in_the_draws_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        health.Report(false, T0);
        Assert.Null(health.Report(false, T0.AddSeconds(60)));
        Fail(health, 61, 70);
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(70)));
    }
}
