using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Review 2026-10-29 (engine I2, I3): while the A350 could not be found, every draw request walked the sim's whole address
// space (about 25,000 VirtualQueryEx calls, over a second each time), back to back, and nothing was said: the call-outs
// simply stopped. The reader now looks again only every 10 s, and says once why it cannot read: the A350 not found during
// an A350 flight (after 30 s, a flight load takes that long to publish), no access to the sim (at once: MSFS running as
// administrator), or an error in the reader; and once when it reads again.
public class A350ReaderStatusTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
    private static DateTime At(double s) => T0.AddSeconds(s);

    [Fact]
    public void A_failed_look_waits_ten_seconds_before_the_next()
    {
        var s = new A350ReaderStatus();
        Assert.True(s.MayLocate(At(0)));
        s.Report(A350ReaderFailure.NotFound, a350Flight: true, At(0));
        Assert.False(s.MayLocate(At(9.9)));
        Assert.True(s.MayLocate(At(10)));
    }

    [Fact]
    public void Not_found_in_an_a350_flight_is_said_once_after_thirty_seconds_and_its_end_once()
    {
        var s = new A350ReaderStatus();
        for (int t = 0; t < 30; t += 10) Assert.Null(s.Report(A350ReaderFailure.NotFound, true, At(t)));
        Assert.Equal("A350 displays cannot be read: the A350 is not found in the simulator", s.Report(A350ReaderFailure.NotFound, true, At(30)));
        Assert.Null(s.Report(A350ReaderFailure.NotFound, true, At(40)));
        Assert.Equal("A350 displays readable again", s.Report(null, true, At(50)));
        Assert.Null(s.Report(null, true, At(51)));
        Assert.True(s.MayLocate(At(51)));
    }

    [Fact]
    public void Outside_an_a350_flight_nothing_is_said_and_nothing_counts()
    {
        var s = new A350ReaderStatus();
        for (int t = 0; t <= 60; t += 10) Assert.Null(s.Report(A350ReaderFailure.NotFound, false, At(t)));
        Assert.Null(s.Report(A350ReaderFailure.NotFound, true, At(70)));   // the A350 flight starts: 30 s from here
        Assert.Null(s.Report(A350ReaderFailure.NotFound, true, At(90)));
        Assert.NotNull(s.Report(A350ReaderFailure.NotFound, true, At(100)));
    }

    [Fact]
    public void No_access_to_the_sim_is_said_at_once()
    {
        var s = new A350ReaderStatus();
        Assert.Equal("A350 displays cannot be read: MSFSBA may not read the simulator; is MSFS running as administrator?",
            s.Report(A350ReaderFailure.AccessDenied, true, At(0)));
    }

    [Fact]
    public void A_new_cause_is_said_too()
    {
        var s = new A350ReaderStatus();
        s.Report(A350ReaderFailure.NotFound, true, At(0));
        Assert.NotNull(s.Report(A350ReaderFailure.NotFound, true, At(30)));
        Assert.Equal("A350 displays cannot be read: an error in the reader, written to the log", s.Report(A350ReaderFailure.Error, true, At(40)));
    }

    // A reader error is no failed look: the A350 was found, so the next draw looks at once.
    [Fact]
    public void An_error_never_delays_the_next_look()
    {
        var s = new A350ReaderStatus();
        s.Report(A350ReaderFailure.Error, true, At(0));
        Assert.True(s.MayLocate(At(1)));
    }
}
