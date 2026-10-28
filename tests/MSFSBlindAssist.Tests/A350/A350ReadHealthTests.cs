using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Live 2026-10-28 every draw trapped on a torn copy for many minutes and the displays simply went quiet:
// the FMA call-outs, the ECAM, the MFD window. A few failed draws in a row are normal; when every draw has
// failed for ten seconds the pilot is told once, and told again when the displays read.
public class A350ReadHealthTests
{
    private static readonly DateTime T0 = new(2026, 10, 28, 18, 0, 0, DateTimeKind.Utc);

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
        health.Report(false, T0);
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(10)));
        Assert.Null(health.Report(false, T0.AddSeconds(20)));
    }

    [Fact]
    public void The_first_good_draw_after_that_says_so()
    {
        var health = new A350ReadHealth();
        health.Report(false, T0);
        health.Report(false, T0.AddSeconds(10));
        Assert.Equal("A350 displays readable again", health.Report(true, T0.AddSeconds(11)));
        Assert.Null(health.Report(true, T0.AddSeconds(12)));
    }

    [Fact]
    public void A_good_draw_starts_the_ten_seconds_again()
    {
        var health = new A350ReadHealth();
        health.Report(false, T0);
        Assert.Null(health.Report(true, T0.AddSeconds(5)));
        health.Report(false, T0.AddSeconds(6));
        Assert.Null(health.Report(false, T0.AddSeconds(15)));
        Assert.Equal("A350 displays cannot be read at the moment", health.Report(false, T0.AddSeconds(16)));
    }
}
