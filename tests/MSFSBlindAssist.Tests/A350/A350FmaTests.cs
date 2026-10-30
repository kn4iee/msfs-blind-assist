using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

// FMA call-outs: each column spoken when it changes, with its colours, once two reads agree; the first agreed
// read is a silent baseline; a column that empties says so. One Ctrl+M row per column (A380 parity).
public class A350FmaTests
{
    private static IReadOnlyList<string> Pfd(params string[] fma) => fma.Concat(new[] { "Altitude: 13 feet" }).ToList();

    [Fact]
    public void The_first_agreed_read_is_silent_then_changes_speak()
    {
        var a = new A350FmaAnnouncer();
        Assert.Empty(a.Observe(Pfd("FMA engagement: 1 FD 2, white")));
        Assert.Empty(a.Observe(Pfd("FMA engagement: 1 FD 2, white")));
        Assert.Empty(a.Observe(Pfd("FMA engagement: AP1 1 FD 2 A/THR, white", "FMA vertical: SRS, green")));
        var said = a.Observe(Pfd("FMA engagement: AP1 1 FD 2 A/THR, white", "FMA vertical: SRS, green"));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("vertical"), "FMA vertical: SRS, green"),
            (A350FmaAnnouncer.KeyFor("engagement"), "FMA engagement: AP1 1 FD 2 A/THR, white") }, said);
    }

    [Fact]
    public void A_column_that_empties_is_spoken_once()
    {
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA vertical: SRS, green")); a.Observe(Pfd("FMA vertical: SRS, green"));
        for (int read = 1; read < A350FmaAnnouncer.ClearAfterReads; read++)
            Assert.Empty(a.Observe(Pfd()));   // it may be flashing
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("vertical"), "FMA vertical clear") }, a.Observe(Pfd()));
        Assert.Empty(a.Observe(Pfd()));
    }

    // Review 3 (I2): an FMA mode flashes by leaving every other draw (LVR CLB at the thrust reduction altitude). Two reads in a row never
    // agreed, so it went unsaid; with the beat of 4x sim rate it chattered, with a "clear" that was false.
    [Fact]
    public void A_mode_that_flashes_is_said_once_and_never_clear()
    {
        const string lever = "FMA autothrust: LVR CLB, white";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA autothrust: MAN FLX, white")); a.Observe(Pfd("FMA autothrust: MAN FLX, white"));
        var said = new List<(string, string)>();
        foreach (var flash in new[] { true, false, true, false, true, false, true, false, true })
            said.AddRange(a.Observe(flash ? Pfd(lever) : Pfd()));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), lever) }, said);
    }

    [Fact]
    public void A_flash_in_runs_of_reads_says_it_once()
    {
        const string lever = "FMA autothrust: LVR CLB, white";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd()); a.Observe(Pfd());
        var said = new List<(string, string)>();
        foreach (var flash in new[] { true, true, false, false, false, true, true, true, false, false, false, true })
            said.AddRange(a.Observe(flash ? Pfd(lever) : Pfd()));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), lever) }, said);
    }

    // Two texts taking turns are no mode to say: as before, it takes two reads in a row.
    [Fact]
    public void Two_texts_taking_turns_say_nothing()
    {
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA engagement: AP1, white")); a.Observe(Pfd("FMA engagement: AP1, white"));
        foreach (var other in new[] { true, false, true, false, true, false })
            Assert.Empty(a.Observe(other ? Pfd("FMA engagement: AP2, white") : Pfd("FMA engagement: AP1, white")));
    }

    // Live 2026-10-30, a go-around at 1000 ft: LVR CLB flashes under MAN TOGA, which stays, so the column is never blank. The reads as
    // they came. It said LVR CLB and then MAN TOGA, as though the lever call had gone, for as long as it flashed.
    [Fact]
    public void A_part_of_a_column_that_flashes_is_said_once()
    {
        const string man = "FMA autothrust: MAN; TOGA, white", lever = "FMA autothrust: MAN; TOGA; LVR CLB, white";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd(man)); a.Observe(Pfd(man));
        var said = new List<(string, string)>();
        foreach (var flash in new[] { true, false, false, true, true, false, false, true, false, true, true, false, false, false, true })
            said.AddRange(a.Observe(flash ? Pfd(lever) : Pfd(man)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), lever) }, said);
    }

    [Fact]
    public void A_flash_seen_one_read_at_a_time_is_said_too()
    {
        const string man = "FMA autothrust: MAN; TOGA, white", lever = "FMA autothrust: MAN; TOGA; LVR CLB, white";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd(man)); a.Observe(Pfd(man));
        var said = new List<(string, string)>();
        foreach (var flash in new[] { true, false, true, false, true, false })
            said.AddRange(a.Observe(flash ? Pfd(lever) : Pfd(man)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), lever) }, said);
    }

    // The levers come back to the detent: the next mode is news at once, whatever was flashing.
    [Fact]
    public void The_next_mode_after_a_flash_speaks_at_once()
    {
        const string man = "FMA autothrust: MAN; TOGA, white", lever = "FMA autothrust: MAN; TOGA; LVR CLB, white", speed = "FMA autothrust: SPEED, green";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd(man)); a.Observe(Pfd(man));
        a.Observe(Pfd(lever)); a.Observe(Pfd(lever));
        a.Observe(Pfd(man));
        Assert.Empty(a.Observe(Pfd(speed)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), speed) }, a.Observe(Pfd(speed)));
    }

    // A part really gone from the column that flashes is said, as a column clearing is, once it has stayed gone past any flash.
    [Fact]
    public void A_part_gone_for_good_is_said_past_any_flash()
    {
        const string both = "FMA autothrust: SPEED, green; BTV, cyan", one = "FMA autothrust: SPEED, green";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd(both)); a.Observe(Pfd(both));
        for (int read = 1; read < A350FmaAnnouncer.ClearAfterReads; read++)
            Assert.Empty(a.Observe(Pfd(one)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("autothrust"), one) }, a.Observe(Pfd(one)));
    }

    // Final review: an armed mode engaging kept its name and lost the other, so it read as a part left by a flash and waited 4 s or more.
    [Theory]
    [InlineData("lateral", "HDG, green; NAV, cyan", "NAV, green")]
    [InlineData("lateral", "RWY TRK, green; NAV, cyan", "NAV, green")]
    [InlineData("vertical", "ALT, green; G/S, cyan", "G/S, green")]
    public void An_armed_mode_engaging_speaks_at_once(string column, string armed, string engaged)
    {
        string line = $"FMA {column}: {engaged}";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd($"FMA {column}: {armed}")); a.Observe(Pfd($"FMA {column}: {armed}"));
        Assert.Empty(a.Observe(Pfd(line)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor(column), line) }, a.Observe(Pfd(line)));
    }

    // Only the autothrust column is known to flash a part. Elsewhere a part going is news: the autopilot off, an armed mode dropped.
    [Theory]
    [InlineData("engagement", "AP1; 1 FD 2; A/THR, white", "1 FD 2; A/THR, white")]
    [InlineData("vertical", "SRS, green; ALT, cyan", "SRS, green")]
    public void A_part_going_from_another_column_speaks_at_once(string column, string both, string one)
    {
        string line = $"FMA {column}: {one}";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd($"FMA {column}: {both}")); a.Observe(Pfd($"FMA {column}: {both}"));
        Assert.Empty(a.Observe(Pfd(line)));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor(column), line) }, a.Observe(Pfd(line)));
    }

    // A colour change is a change: the same mode in another colour loses nothing.
    [Fact]
    public void The_same_mode_in_another_colour_speaks_at_once()
    {
        const string armed = "FMA lateral: LOC, cyan", on = "FMA lateral: LOC, green";
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd(armed)); a.Observe(Pfd(armed));
        a.Observe(Pfd(on));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("lateral"), on) }, a.Observe(Pfd(on)));
    }

    [Fact]
    public void A_flicker_between_reads_says_nothing()
    {
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA lateral: RWY, green")); a.Observe(Pfd("FMA lateral: RWY, green"));
        Assert.Empty(a.Observe(Pfd("FMA lateral: NAV, green")));
        Assert.Empty(a.Observe(Pfd("FMA lateral: RWY, green")));
    }

    // Live 2026-10-28, the climb out of VCBI: NAV went from armed (cyan) to engaged (green) after a DIR TO, and CLB
    // engaged after the ALT knob push, and neither was said: the selected altitude drawn at the tape's top flickered in
    // and out of the engagement column, and a call-out waited for every column to agree at once. Each column agrees on
    // its own.
    [Fact]
    public void A_flickering_column_does_not_silence_another_columns_change()
    {
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA lateral: NAV, cyan", "FMA engagement: AP1, white"));
        a.Observe(Pfd("FMA lateral: NAV, cyan", "FMA engagement: AP1, white"));
        Assert.Empty(a.Observe(Pfd("FMA lateral: NAV, green", "FMA engagement: 150, cyan; AP1, white")));
        var said = a.Observe(Pfd("FMA lateral: NAV, green", "FMA engagement: AP1, white"));
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("lateral"), "FMA lateral: NAV, green") }, said);
    }

    [Fact]
    public void Each_column_has_a_ctrl_m_row()
    {
        var rows = A350MonitorManagerForm.BuildRows(new Dictionary<string, SimVarDefinition>());
        foreach (var col in A350FmaAnnouncer.Columns)
            Assert.Single(rows, r => r.Key == A350FmaAnnouncer.KeyFor(col));
        Assert.Equal("FMA vertical mode", Assert.Single(rows, r => r.Key == A350FmaAnnouncer.KeyFor("vertical")).Label);
    }
}
