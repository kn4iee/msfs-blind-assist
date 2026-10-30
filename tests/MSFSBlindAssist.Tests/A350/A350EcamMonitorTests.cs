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

    private static (A350EcamMonitor Monitor, List<string> Said, bool[] Muted) Make()
    {
        var said = new List<string>();
        var muted = new bool[1];
        return (new A350EcamMonitor(() => muted[0], said.Add), said, muted);
    }

    /// <summary>The CTR draws handed over, as the display tick hands each one (<see cref="A350PfdMonitor.CtrDrawn"/>).</summary>
    private static void Ticks(A350EcamMonitor m, params A350DrawCapture?[] caps)
    {
        foreach (var c in caps) m.Observe(c);
    }

    // Review 2026-10-29 (engine M2): a CTR draw handed over after the monitor was disposed (an aircraft switch mid-tick) spoke
    // its new lines. Nothing is said once disposed.
    [Fact]
    public void Nothing_is_said_once_disposed()
    {
        var (m, said, _) = Make();
        Ticks(m, Dark, Dark);
        m.Dispose();
        m.Observe(Fixture("ctr_powerup_fws_fault"));
        m.Observe(Fixture("ctr_powerup_fws_fault"));
        Assert.Empty(said);
    }

    [Fact]
    public void A_power_up_is_spoken_line_by_line_with_colours()
    {
        var (m, said, _) = Make();
        var fws = Fixture("ctr_powerup_fws_fault");
        Ticks(m, Dark, Dark, fws, fws);
        Assert.Equal(12, said.Count);
        Assert.Equal("FWS FWS 1+2 & CPIOM FAULT, amber", said[0]);
        Assert.Equal("ALT ALERT & AUTO CALLOUT / NORM C/L & MEMOS, amber", said[^1]);
        Assert.Same(fws, m.LatestCapture);
    }

    [Fact]
    public void Muted_it_says_nothing_but_keeps_up_with_the_display()
    {
        var (m, said, muted) = Make();
        muted[0] = true;
        var fws = Fixture("ctr_powerup_fws_fault");
        var apu = Fixture("ctr_apu_emer_shutdown");
        Ticks(m, Dark, Dark, fws, fws);
        Assert.Empty(said);
        muted[0] = false;
        Ticks(m, apu, apu);
        Assert.Contains("APU EMER SHUTDOWN, amber", said);
        Assert.DoesNotContain("FWS FWS 1+2 & CPIOM FAULT, amber", said);   // never replayed after unmuting
    }

    // Review 2026-10-29 (speech 1): the call-outs were never re-baselined after a flight load, so each WD line flight B shows
    // that flight A lacked was spoken as new. After Rebaseline the new flight's E/WD is followed silently for
    // SettleAfterLoad from its first draw (it comes up as the flight loads), then speaks what changes from there.
    [Fact]
    public void After_a_flight_load_the_new_flights_lines_are_a_silent_baseline()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var said = new List<string>();
        var m = new A350EcamMonitor(() => false, said.Add, () => now);
        var fws = Fixture("ctr_powerup_fws_fault");
        var apu = Fixture("ctr_apu_emer_shutdown");
        Ticks(m, Dark, Dark, apu, apu);   // flight A
        said.Clear();
        m.Rebaseline();
        Assert.Null(m.LatestCapture);                 // flight A's E/WD is not this flight's
        Ticks(m, Dark, Dark);             // flight B loading
        now += TimeSpan.FromSeconds(5);
        Ticks(m, fws, fws);               // flight B's E/WD comes up while it loads
        Assert.Empty(said);
        now += A350EcamMonitor.SettleAfterLoad;
        Ticks(m, apu, apu);
        Assert.Contains("APU EMER SHUTDOWN, amber", said);
    }

    // Review 2026-10-29 (readers I7): Alt+E and the SD line read the last capture whatever its age.
    [Fact]
    public void A_capture_older_than_its_age_is_no_capture()
    {
        var now = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        var fws = Fixture("ctr_powerup_fws_fault");
        var m = new A350EcamMonitor(() => true, _ => { }, () => now);
        m.Observe(fws);
        now += A350PfdMonitor.MaxAge;
        Assert.Same(fws, m.LatestCapture);
        now += TimeSpan.FromSeconds(1);
        Assert.Null(m.LatestCapture);
    }

    [Fact]
    public void No_capture_or_a_trapped_one_changes_nothing()
    {
        var (m, said, _) = Make();
        var fws = Fixture("ctr_powerup_fws_fault");
        var trapped = fws with { Error = "trap: out of bounds" };
        Ticks(m, Dark, Dark, null, trapped, fws, trapped, fws);
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

    // Review 3 (M12): Alt+E said a bare "Mailbox:" with nothing in it.
    [Fact]
    public void An_empty_mailbox_has_no_heading()
    {
        var text = A350EwdText.Build(A350Fixtures.Capture("ctr_cruise.json"));
        Assert.DoesNotContain("Mailbox:", text);
        Assert.Contains("Engine display:", text);
    }

    [Fact]
    public void The_monitor_manager_lists_the_ecam_row()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        var row = Assert.Single(rows, r => r.Key == A350EcamMonitor.MuteKey);
        Assert.Equal("ECAM E/WD call-outs", row.Label);
    }
}
