using MSFSBlindAssist.Aircraft.A330.Display;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330ReaderNoticeTests
{
    private const string Unavailable = "A330 displays unavailable: the display reader is not installed. The MCDU window cannot read its screen.";

    [Fact]
    public void A_lasting_state_is_spoken_once_thirty_seconds_after_the_aircraft_became_active()
    {
        var n = new A330ReaderNotice(1000);
        Assert.Null(n.Next(A330ReaderState.NotInstalled, 1000 + 29_999));
        Assert.Equal(Unavailable, n.Next(A330ReaderState.NotInstalled, 1000 + 30_000));
        Assert.Null(n.Next(A330ReaderState.NotInstalled, 1000 + 31_000));
    }

    [Fact]
    public void A_passing_state_is_never_spoken()
    {
        var n = new A330ReaderNotice(0);
        foreach (var s in new[] { A330ReaderState.Starting, A330ReaderState.SimulatorNotFound, A330ReaderState.AircraftNotFound, A330ReaderState.Stopped })
            Assert.Null(n.Next(s, 120_000));
    }

    [Fact]
    public void Running_after_a_spoken_notice_says_A330_displays_ready_once()
    {
        var n = new A330ReaderNotice(0);
        Assert.Null(n.Next(A330ReaderState.Running, 40_000));
        Assert.NotNull(n.Next(A330ReaderState.Failed, 40_000));
        Assert.Equal("A330 displays ready", n.Next(A330ReaderState.Running, 41_000));
        Assert.Null(n.Next(A330ReaderState.Running, 42_000));
        // after ready, a later lasting state is a new reason, spoken at once
        Assert.NotNull(n.Next(A330ReaderState.NoAccess, 43_000));
    }

    [Fact]
    public void A_new_lasting_reason_is_spoken_once()
    {
        var n = new A330ReaderNotice(0);
        Assert.NotNull(n.Next(A330ReaderState.NotInstalled, 30_000));
        var next = n.Next(A330ReaderState.OutOfDate, 30_500);
        Assert.Equal("A330 displays unavailable: the display reader is out of date. The MCDU window cannot read its screen.", next);
        Assert.Null(n.Next(A330ReaderState.OutOfDate, 31_000));
    }

    [Fact]
    public void A_lasting_state_still_lasting_after_the_quiet_period_is_spoken_at_the_first_call()
    {
        var n = new A330ReaderNotice(0);
        Assert.Null(n.Next(A330ReaderState.NoAccess, 5_000));
        Assert.NotNull(n.Next(A330ReaderState.NoAccess, 75_000));
    }

    [Fact]
    public void The_line_does_not_change_under_a_comma_decimal_culture()
    {
        var line = TestCulture.Run("de-DE", () => new A330ReaderNotice(0).Next(A330ReaderState.NotInstalled, 30_000));
        Assert.Equal(Unavailable, line);
    }
}
