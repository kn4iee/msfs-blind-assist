using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// A display window shows a fresh draw after a key, and otherwise the background monitor's latest. The monitor's latest
// at the moment of a fresh draw is older than it: shown on the next tick, the list went back to the screen before the
// key and its change was spoken again (live 2026-10-28, the RMP window: "standby 122.500", then "standby 123.450").
// The draw worker runs one draw at a time, so the monitor's next draw is newer, and is shown: told apart by identity, as a
// capture is a record and two draws of an unchanged screen are equal.
public class A350ShownCaptureTests
{
    private static A350DrawCapture Capture() =>
        new("RMP", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);

    [Fact]
    public void Before_any_fresh_draw_the_latest_is_shown()
    {
        Assert.False(new A350ShownCapture().Superseded(Capture()));
    }

    [Fact]
    public void The_latest_at_a_fresh_draw_is_never_shown_after_it()
    {
        var shown = new A350ShownCapture();
        var latest = Capture();
        shown.Fresh(latest);
        Assert.True(shown.Superseded(latest));
    }

    [Fact]
    public void The_monitors_next_draw_is_shown()
    {
        var shown = new A350ShownCapture();
        shown.Fresh(Capture());
        Assert.False(shown.Superseded(Capture()));
    }

    [Fact]
    public void No_latest_is_nothing_superseded()
    {
        var shown = new A350ShownCapture();
        shown.Fresh(null);
        Assert.False(shown.Superseded(null));
    }
}
