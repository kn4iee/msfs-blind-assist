using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The OIS's pause overlay, heard with its window closed (X6): the aircraft pauses the sim itself at the top of descent (OPTIONS
/// > SIMULATION > PAUSE AT T.O.D) and when the autopilot drops under time compression (AP DISCONNECT PROTECTION), and the OIS shows
/// why, over a RESUME FLIGHT button (its decoded bundle, 2026-10-30: <c>L:INI_IS_PAUSED</c> with <c>L:FMGS_PAUSE_TYPE</c> 1 and 2;
/// 0 is the toolbar's own pause, "please un-pause via the toolbar"). A pilot who cannot see the OIS must hear why the sim stopped.
/// Read from the copy, which needs no running sim.
/// </summary>
public class A350PauseAlertsTests
{
    private static readonly A350PauseFacts Facts = new(2000, 2008);

    private static A350MemoryCopy Copy(double paused, double type)
    {
        var mem = new byte[4096];
        BitConverter.GetBytes(paused).CopyTo(mem, 2000);
        BitConverter.GetBytes(type).CopyTo(mem, 2008);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);
        return copy;
    }

    [Fact]
    public void The_pause_is_read_from_the_variable_table_in_a_copy()
    {
        Assert.Equal(new A350SimPause(true, 1), A350SimPause.Read(Copy(1, 1), Facts));
        Assert.Equal(new A350SimPause(false, 0), A350SimPause.Read(Copy(0, 0), Facts));
        Assert.Null(A350SimPause.Read(Copy(1, 1), Facts with { IsPaused = 8000 }));
        Assert.Null(A350SimPause.Read(Copy(double.NaN, 1), Facts));
        Assert.Null(A350SimPause.Read(Copy(1, double.PositiveInfinity), Facts));
    }

    [Fact]
    public void The_top_of_descent_pause_is_said_once_with_how_to_resume()
    {
        var alerts = new A350PauseAlerts();
        Assert.Null(alerts.Observe(new A350SimPause(false, 0)));
        Assert.Equal("Top of descent pause. The simulation has been paused. Resume with RESUME FLIGHT on the OIS.", alerts.Observe(new A350SimPause(true, 1)));
        Assert.Null(alerts.Observe(new A350SimPause(true, 1)));
    }

    [Fact]
    public void The_autopilot_pause_says_why() =>
        Assert.Equal("Autopilot disconnected during time compression. The simulation has been paused. Resume with RESUME FLIGHT on the OIS.",
            new A350PauseAlerts().Observe(new A350SimPause(true, 2)));

    // The toolbar's pause has no button on the OIS, and SimStateTracker speaks it.
    [Fact]
    public void A_pause_of_the_pilots_own_says_nothing() =>
        Assert.Null(new A350PauseAlerts().Observe(new A350SimPause(true, 0)));

    // FMGS_PAUSE_TYPE stays set after a resume (the bundle reads it only as the sim pauses): not paused is the end of it.
    [Fact]
    public void A_type_left_set_while_running_says_nothing_and_a_new_pause_is_said_again()
    {
        var alerts = new A350PauseAlerts();
        Assert.NotNull(alerts.Observe(new A350SimPause(true, 1)));
        Assert.Null(alerts.Observe(new A350SimPause(false, 1)));
        Assert.Null(alerts.Observe(new A350SimPause(false, 1)));
        Assert.NotNull(alerts.Observe(new A350SimPause(true, 1)));
    }

    // Either variable can land first.
    [Fact]
    public void The_type_coming_after_the_pause_is_still_said()
    {
        var alerts = new A350PauseAlerts();
        Assert.Null(alerts.Observe(new A350SimPause(true, 0)));
        Assert.NotNull(alerts.Observe(new A350SimPause(true, 1)));
    }

    [Fact]
    public void The_definition_says_it_behind_its_row()
    {
        var speech = new SpeechCapture();
        var def = new IniBuildsA350Definition();
        def.ObservePause(new A350SimPause(true, 1), speech);
        Assert.Contains(speech.All, s => s.StartsWith("Top of descent pause", StringComparison.Ordinal));

        var muted = new SpeechCapture();
        new IniBuildsA350Definition { IsMuted = key => key == A350PauseAlerts.MuteKey }.ObservePause(new A350SimPause(true, 1), muted);
        Assert.Empty(muted.All);
    }

    [Fact]
    public void Ctrl_m_lists_its_row()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        Assert.Equal(A350PauseAlerts.MuteLabel, Assert.Single(rows, r => r.Key == A350PauseAlerts.MuteKey).Label);
    }
}
