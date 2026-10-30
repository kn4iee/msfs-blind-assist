using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

// Gap 134, 2026-10-30: a PULL UP on the PFD was read in its status display and never said; the ECAM and FMA call-outs have
// no part in what the PFD flags on its own (PULL UP, ATT, the T C A S letters, AUTO BRK OFF, DECEL, RWY TOO SHORT...).
public class A350PfdFlagTests
{
    /// <summary>A captain's EFIS draw of these texts, at the PFD's own x (its left half).</summary>
    private static A350DrawCapture Pfd(params (string Text, float X, float Y, string Colour)[] texts) =>
        new("EFIS", texts.Select(t => new DrawnText(t.Text, t.X, t.Y, t.X, t.Y, 0, A350Rgba.Of(t.Colour))).ToList(),
            Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);

    private static A350DrawCapture Fixture(string name) => A350Fixtures.Capture(name + ".json");

    [Fact]
    public void A_flag_drawn_a_letter_at_a_time_down_the_screen_reads_as_one_word()
    {
        var read = A350Pfd.ReadParts(Pfd(("T", 700, 480, "amber"), ("C", 700, 508, "amber"), ("A", 700, 535, "amber"), ("S", 700, 563, "amber")), 0);
        Assert.Equal(new[] { "TCAS, amber" }, read.Rest);
    }

    [Fact]
    public void Letters_of_two_colours_or_far_apart_stay_apart()
    {
        var read = A350Pfd.ReadParts(Pfd(("T", 700, 480, "amber"), ("C", 700, 508, "red"), ("X", 400, 300, "amber"), ("Y", 400, 400, "amber")), 0);
        Assert.Equal(4, read.Rest.Count());
    }

    [Fact]
    public void A_text_drawn_twice_in_one_place_reads_as_the_one_on_top()
    {
        // Live 2026-10-30: PULL UP is drawn green, then red over it, at x 359 y 400.
        var read = A350Pfd.ReadParts(Pfd(("PULL UP", 359, 400, "green"), ("PULL UP", 359, 400, "red")), 0);
        Assert.Equal(new[] { "PULL UP, red" }, read.Rest);
    }

    [Fact]
    public void Small_capitals_read_as_capitals()
    {
        // The font's small capitals are lowercase letters: the red RA flag is drawn "ra" (live 2026-10-30).
        var read = A350Pfd.ReadParts(Pfd(("ra", 360, 580, "red")), 0);
        Assert.Equal(new[] { "RA, red" }, read.Rest);
    }

    [Fact]
    public void The_rest_is_what_fits_no_area_and_the_lines_still_hold_it()
    {
        var read = A350Pfd.ReadParts(Fixture("efis0_aligning"), 0);
        Assert.Contains("ATT, red", read.Rest);
        Assert.Contains("ATT, red", read.Lines);
        Assert.DoesNotContain(read.Rest, l => l.StartsWith("FMA ", StringComparison.Ordinal) || l.StartsWith("Heading", StringComparison.Ordinal));
        Assert.Equal(A350Pfd.Read(Fixture("efis0_aligning"), 0), read.Lines);
    }

    [Theory]
    [InlineData("PULL UP, red", true)]
    [InlineData("ATT, red", true)]
    [InlineData("TCAS, amber", true)]
    [InlineData("DH, amber", true)]
    [InlineData("AUTO BRK OFF, amber", true)]
    [InlineData("T.O THS, white; OUT OF RANGE, red", true)]
    [InlineData("T.O THS FOR, white; 40.3, amber; %, cyan", true)]
    [InlineData("DECEL, green", true)]
    [InlineData("SPD BRK AUTO EXT, white", true)]
    [InlineData("ILS, magenta", false)]
    [InlineData("30.0, magenta", false)]
    [InlineData("3000, white", false)]
    [InlineData("T.O THS FOR, white; 28.5, green; %, cyan; 28.4, magenta", false)]
    public void A_flag_is_what_a_pilot_would_be_told_to_look_at(string line, bool flag)
    {
        Assert.Equal(flag, A350PfdFlagAnnouncer.IsFlag(line));
    }

    [Fact]
    public void A_flag_is_said_on_its_second_read_and_once()
    {
        var flags = new A350PfdFlagAnnouncer();
        Assert.Empty(flags.Observe(new[] { "PULL UP, red" }));
        Assert.Equal(new[] { "PULL UP, red" }, flags.Observe(new[] { "PULL UP, red" }));
        Assert.Empty(flags.Observe(new[] { "PULL UP, red" }));
    }

    [Fact]
    public void A_flashing_flag_is_said_and_said_once()
    {
        // Every other read: two reads in a row would never agree on it (MASTER WARN flashes about as fast as the reads).
        var flags = new A350PfdFlagAnnouncer();
        var said = new List<string>();
        for (int n = 0; n < 12; n++)
            said.AddRange(flags.Observe(n % 2 == 0 ? new[] { "PULL UP, red" } : Array.Empty<string>()));
        Assert.Equal(new[] { "PULL UP, red" }, said);
    }

    [Fact]
    public void One_torn_read_says_nothing()
    {
        var flags = new A350PfdFlagAnnouncer();
        var said = new List<string>();
        said.AddRange(flags.Observe(new[] { "WXYZ, red" }));
        for (int n = 0; n < 10; n++) said.AddRange(flags.Observe(Array.Empty<string>()));
        Assert.Empty(said);
    }

    [Fact]
    public void A_flag_gone_for_a_few_seconds_is_new_when_it_comes_back()
    {
        var flags = new A350PfdFlagAnnouncer();
        flags.Observe(new[] { "ATT, red" });
        flags.Observe(new[] { "ATT, red" });
        for (int n = 0; n < A350PfdFlagAnnouncer.GoneAfter; n++) flags.Observe(Array.Empty<string>());
        Assert.Empty(flags.Observe(new[] { "ATT, red" }));
        Assert.Equal(new[] { "ATT, red" }, flags.Observe(new[] { "ATT, red" }));
    }

    [Fact]
    public void A_flag_whose_figure_moves_is_the_same_flag()
    {
        var flags = new A350PfdFlagAnnouncer();
        flags.Observe(new[] { "T.O THS FOR, white; 40.3, amber; %, cyan" });
        Assert.Single(flags.Observe(new[] { "T.O THS FOR, white; 40.3, amber; %, cyan" }));
        Assert.Empty(flags.Observe(new[] { "T.O THS FOR, white; 41.0, amber; %, cyan" }));
        Assert.Empty(flags.Observe(new[] { "T.O THS FOR, white; 41.5, amber; %, cyan" }));
    }

    [Fact]
    public void A_flag_that_changes_colour_is_said_again()
    {
        var flags = new A350PfdFlagAnnouncer();
        flags.Observe(new[] { "SPD, amber" });
        flags.Observe(new[] { "SPD, amber" });
        flags.Observe(new[] { "SPD, red" });
        Assert.Equal(new[] { "SPD, red" }, flags.Observe(new[] { "SPD, red" }));
    }

    [Fact]
    public void What_is_no_flag_is_never_said()
    {
        var flags = new A350PfdFlagAnnouncer();
        for (int n = 0; n < 4; n++) Assert.Empty(flags.Observe(new[] { "ILS, magenta", "3000, white" }));
    }

    [Fact]
    public void A_reset_forgets_what_was_said()
    {
        var flags = new A350PfdFlagAnnouncer();
        flags.Observe(new[] { "ATT, red" });
        flags.Observe(new[] { "ATT, red" });
        flags.Reset();
        flags.Observe(new[] { "ATT, red" });
        Assert.Equal(new[] { "ATT, red" }, flags.Observe(new[] { "ATT, red" }));
    }

    [Fact]
    public async Task The_monitor_says_a_flag_as_it_appears()
    {
        var said = new List<string>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((g, side) => g == "EFIS" && side == 0 ? Pfd(("PULL UP", 359, 400, "red")) : null), _ => false, said.Add);
        await m.TickAsync();
        await m.TickAsync();
        await m.TickAsync();
        Assert.Equal(new[] { "PULL UP, red" }, said);
    }

    [Fact]
    public async Task Its_ctrl_m_row_mutes_it_and_unmuting_never_replays_it()
    {
        bool muted = true;
        var said = new List<string>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((g, side) => g == "EFIS" && side == 0 ? Pfd(("PULL UP", 359, 400, "red")) : null),
            key => muted && key == A350PfdFlagAnnouncer.MuteKey, said.Add);
        await m.TickAsync();
        await m.TickAsync();
        muted = false;
        await m.TickAsync();
        Assert.Empty(said);
    }

    [Fact]
    public async Task A_loaded_flights_flags_settle_silently()
    {
        var at = new DateTime(2026, 10, 30, 8, 0, 0, DateTimeKind.Utc);
        var said = new List<string>();
        var m = new A350PfdMonitor(A350PfdMonitorTests.Each((g, side) => g == "EFIS" && side == 0 ? Pfd(("ATT", 359, 400, "red")) : null), _ => false, said.Add, () => at);
        m.Rebaseline();
        await m.TickAsync();
        await m.TickAsync();
        at += A350EcamMonitor.SettleAfterLoad + TimeSpan.FromSeconds(1);
        await m.TickAsync();
        Assert.Empty(said);
    }

    [Fact]
    public void It_has_a_ctrl_m_row()
    {
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        Assert.Contains(rows, r => r.Key == A350PfdFlagAnnouncer.MuteKey && r.Label == A350PfdFlagAnnouncer.MuteLabel);
    }
}
