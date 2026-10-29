using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

// The E/WD call-outs run in the background whether or not a window is open: the CTR drawn twice a second,
// each new line spoken with its colour behind one Ctrl+M row, which Ctrl+E flips (A380 parity).
public class A350EcamMonitorTests
{
    private static A350DrawCapture Fixture(string name) =>
        A350Fixtures.Capture(name + ".json");

    private static readonly A350DrawCapture Dark =
        new("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);

    private static (A350EcamMonitor Monitor, List<string> Said, Queue<A350DrawCapture?> Feed, bool[] Muted) Make()
    {
        var feed = new Queue<A350DrawCapture?>();
        var said = new List<string>();
        var muted = new bool[1];
        var m = new A350EcamMonitor(() => Task.FromResult(feed.Count > 0 ? feed.Dequeue() : null), () => muted[0], said.Add);
        return (m, said, feed, muted);
    }

    private static async Task Ticks(A350EcamMonitor m, Queue<A350DrawCapture?> feed, params A350DrawCapture?[] caps)
    {
        foreach (var c in caps) { feed.Enqueue(c); await m.TickAsync(); }
    }

    // Review 2026-10-29 (engine M2): a CTR draw handed over after the monitor was disposed (an aircraft switch mid-tick) spoke
    // its new lines. Nothing is said once disposed.
    [Fact]
    public async Task Nothing_is_said_once_disposed()
    {
        var (m, said, feed, _) = Make();
        await Ticks(m, feed, Dark, Dark);
        m.Dispose();
        m.Observe(Fixture("ctr_powerup_fws_fault"));
        m.Observe(Fixture("ctr_powerup_fws_fault"));
        Assert.Empty(said);
    }

    [Fact]
    public async Task A_power_up_is_spoken_line_by_line_with_colours()
    {
        var (m, said, feed, _) = Make();
        var fws = Fixture("ctr_powerup_fws_fault");
        await Ticks(m, feed, Dark, Dark, fws, fws);
        Assert.Equal(12, said.Count);
        Assert.Equal("FWS FWS 1+2 & CPIOM FAULT, amber", said[0]);
        Assert.Equal("ALT ALERT & AUTO CALLOUT / NORM C/L & MEMOS, amber", said[^1]);
        Assert.Same(fws, m.LatestCapture);
    }

    [Fact]
    public async Task Muted_it_says_nothing_but_keeps_up_with_the_display()
    {
        var (m, said, feed, muted) = Make();
        muted[0] = true;
        var fws = Fixture("ctr_powerup_fws_fault");
        var apu = Fixture("ctr_apu_emer_shutdown");
        await Ticks(m, feed, Dark, Dark, fws, fws);
        Assert.Empty(said);
        muted[0] = false;
        await Ticks(m, feed, apu, apu);
        Assert.Contains("APU EMER SHUTDOWN, amber", said);
        Assert.DoesNotContain("FWS FWS 1+2 & CPIOM FAULT, amber", said);   // never replayed after unmuting
    }

    // Review 2026-10-29 (speech 1): the call-outs were never re-baselined after a flight load, so each WD line flight B shows
    // that flight A lacked was spoken as new. After Rebaseline the new flight's E/WD is followed silently for
    // SettleAfterLoad from its first draw (it comes up as the flight loads), then speaks what changes from there.
    [Fact]
    public async Task After_a_flight_load_the_new_flights_lines_are_a_silent_baseline()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var feed = new Queue<A350DrawCapture?>();
        var said = new List<string>();
        var m = new A350EcamMonitor(() => Task.FromResult(feed.Count > 0 ? feed.Dequeue() : null), () => false, said.Add, () => now);
        var fws = Fixture("ctr_powerup_fws_fault");
        var apu = Fixture("ctr_apu_emer_shutdown");
        await Ticks(m, feed, Dark, Dark, apu, apu);   // flight A
        said.Clear();
        m.Rebaseline();
        Assert.Null(m.LatestCapture);                 // flight A's E/WD is not this flight's
        await Ticks(m, feed, Dark, Dark);             // flight B loading
        now += TimeSpan.FromSeconds(5);
        await Ticks(m, feed, fws, fws);               // flight B's E/WD comes up while it loads
        Assert.Empty(said);
        now += A350EcamMonitor.SettleAfterLoad;
        await Ticks(m, feed, apu, apu);
        Assert.Contains("APU EMER SHUTDOWN, amber", said);
    }

    // Review 2026-10-29 (readers I7): Alt+E and the SD line read the last capture whatever its age.
    [Fact]
    public async Task A_capture_older_than_its_age_is_no_capture()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var fws = Fixture("ctr_powerup_fws_fault");
        var feed = new Queue<A350DrawCapture?>(new[] { fws });
        var m = new A350EcamMonitor(() => Task.FromResult(feed.Count > 0 ? feed.Dequeue() : null), () => true, _ => { }, () => now);
        await m.TickAsync();
        now += A350PfdMonitor.MaxAge;
        Assert.Same(fws, m.LatestCapture);
        now += TimeSpan.FromSeconds(1);
        Assert.Null(m.LatestCapture);
    }

    [Fact]
    public async Task No_capture_or_a_trapped_one_changes_nothing()
    {
        var (m, said, feed, _) = Make();
        var fws = Fixture("ctr_powerup_fws_fault");
        var trapped = fws with { Error = "trap: out of bounds" };
        await Ticks(m, feed, Dark, Dark, null, trapped, fws, trapped, fws);
        Assert.Equal(12, said.Count);
    }

    [Fact]
    public void Ctrl_e_flips_the_ecam_row()
    {
        var disabled = new List<string>();
        Assert.False(A350EcamMonitor.Toggle(disabled));
        Assert.Contains(A350EcamMonitor.MuteKey, disabled);
        Assert.True(A350EcamMonitor.Toggle(disabled));
        Assert.DoesNotContain(A350EcamMonitor.MuteKey, disabled);
        Assert.Equal("E W D monitoring disabled", A350EcamMonitor.ToggleSpoken(false));
        Assert.Equal("E W D monitoring enabled", A350EcamMonitor.ToggleSpoken(true));
    }

    [Fact]
    public void The_monitor_manager_lists_the_ecam_row()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        var row = Assert.Single(rows, r => r.Key == A350EcamMonitor.MuteKey);
        Assert.Equal("ECAM E/WD call-outs", row.Label);
    }
}
