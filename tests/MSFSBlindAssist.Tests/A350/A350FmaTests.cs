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
            Assert.Empty(a.Observe(other ? Pfd("FMA engagement: AP1; A/THR, white") : Pfd("FMA engagement: AP1, white")));
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
