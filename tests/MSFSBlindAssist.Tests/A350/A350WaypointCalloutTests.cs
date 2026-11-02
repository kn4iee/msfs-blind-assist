using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 08 Task 1: the next waypoint said as the captain's ND's TO waypoint changes (the owner's answer to question 7,
/// 2026-10-31: "Next waypoint {name}", the name as the ND draws it, its row off until the pilot ticks it).</summary>
public class A350WaypointCalloutTests
{
    private static string? Read(A350WaypointCallout c, string? name, bool settling = false) => c.Observe(name, settling);

    /// <summary>Two agreeing reads take the baseline, silently.</summary>
    private static A350WaypointCallout Flying(string at)
    {
        var c = new A350WaypointCallout();
        Read(c, at);
        Read(c, at);
        return c;
    }

    [Fact]
    public void A_new_to_waypoint_is_said_once_when_two_reads_agree()
    {
        var c = Flying("BI614");
        Assert.Null(Read(c, "KAT"));
        Assert.Equal("Next waypoint KAT", Read(c, "KAT"));
        Assert.Null(Read(c, "KAT"));
        Assert.Null(Read(c, "KAT"));
    }

    // A torn copy can draw a garbage name for one read.
    [Fact]
    public void A_torn_read_says_nothing()
    {
        var c = Flying("BI614");
        Assert.Null(Read(c, "B#6?4"));
        Assert.Null(Read(c, "BI614"));
        Assert.Null(Read(c, "BI614"));
    }

    // The end of the route, a discontinuity: the TO field is blank. The next real waypoint is said when it appears; the same
    // waypoint back after a blank is not news.
    [Fact]
    public void A_blank_to_field_says_nothing()
    {
        var c = Flying("BI614");
        Assert.Null(Read(c, null));
        Assert.Null(Read(c, ""));
        Assert.Null(Read(c, "BI614"));
        Assert.Null(Read(c, "BI614"));
        Assert.Null(Read(c, null));
        Assert.Null(Read(c, "GOPRU"));
        Assert.Equal("Next waypoint GOPRU", Read(c, "GOPRU"));
    }

    // A flight load or an OIS restore: the call-outs settle silently, and the waypoint read then is where the pilot is.
    [Fact]
    public void The_first_read_after_a_load_is_silent()
    {
        var c = Flying("BI614");
        Assert.Null(Read(c, "DB403", settling: true));
        Assert.Null(Read(c, "DB403", settling: true));
        Assert.Null(Read(c, "DB403"));
        var fresh = new A350WaypointCallout();
        Assert.Null(Read(fresh, "DB403"));
        Assert.Null(Read(fresh, "DB403"));   // the first agreed read of all is a baseline too
    }

    [Fact]
    public void The_name_is_read_from_the_nd_as_drawn()
    {
        Assert.Equal("BI614", A350GroundDisplays.ToWaypointName(A350Fixtures.Capture("efis0_climb_bi614.json"), 0));
        Assert.Equal("DB403", A350GroundDisplays.ToWaypointName(A350Fixtures.Capture("efis0_arc40_omdb_label.json"), 0));
        Assert.Null(A350GroundDisplays.ToWaypointName(A350Fixtures.Capture("efis0_ls_rose_ils2.json"), 0));   // the receiver's corner
    }

    private static List<string> Fly(bool muted, params string[] fixtures)
    {
        var def = new IniBuildsA350Definition();
        var said = new List<string>();
        foreach (var f in fixtures)
            def.ObserveCaptainEfis(A350Fixtures.Capture(f), DateTime.UtcNow, settling: false,
                key => muted && key == A350WaypointCallout.Key, said.Add);
        return said;
    }

    [Fact]
    public void The_definition_says_the_captains_next_waypoint()
    {
        Assert.Equal(new[] { "Next waypoint DB403" }, Fly(false,
            "efis0_climb_bi614.json", "efis0_climb_bi614.json", "efis0_arc40_omdb_label.json", "efis0_arc40_omdb_label.json"));
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        Assert.Empty(Fly(true,
            "efis0_climb_bi614.json", "efis0_climb_bi614.json", "efis0_arc40_omdb_label.json", "efis0_arc40_omdb_label.json"));
    }

    [Fact]
    public void Its_row_is_in_ctrl_m()
    {
        Assert.Contains((A350WaypointCallout.Key, "Next waypoint call-out"), A350CalloutRows.Nd);
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(new IniBuildsA350Definition().GetVariables());
        Assert.Contains(rows, r => r.Key == A350WaypointCallout.Key && r.Label == "Next waypoint call-out");
    }

    [Fact]
    public void The_next_waypoint_row_starts_unticked()
    {
        var s = new UserSettings();
        Assert.True(SettingsManager.SeedA350MonitorDefaults(s));
        Assert.True(s.A350MonitorDefaultsSeeded);
        Assert.Contains(A350WaypointCallout.Key, s.A350DisabledMonitorVariables);
        Assert.False(SettingsManager.SeedA350MonitorDefaults(s));   // once
        Assert.Single(s.A350DisabledMonitorVariables, k => k == A350WaypointCallout.Key);
    }

    [Fact]
    public void A_row_the_pilot_ticked_stays_ticked()
    {
        var s = new UserSettings();
        SettingsManager.SeedA350MonitorDefaults(s);
        s.A350DisabledMonitorVariables.Remove(A350WaypointCallout.Key);
        Assert.False(SettingsManager.SeedA350MonitorDefaults(s));
        Assert.DoesNotContain(A350WaypointCallout.Key, s.A350DisabledMonitorVariables);
    }
}
