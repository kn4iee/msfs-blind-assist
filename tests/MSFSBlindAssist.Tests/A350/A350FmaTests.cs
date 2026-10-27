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
        a.Observe(Pfd());
        Assert.Equal(new[] { (A350FmaAnnouncer.KeyFor("vertical"), "FMA vertical clear") }, a.Observe(Pfd()));
        Assert.Empty(a.Observe(Pfd()));
    }

    [Fact]
    public void A_flicker_between_reads_says_nothing()
    {
        var a = new A350FmaAnnouncer();
        a.Observe(Pfd("FMA lateral: RWY, green")); a.Observe(Pfd("FMA lateral: RWY, green"));
        Assert.Empty(a.Observe(Pfd("FMA lateral: NAV, green")));
        Assert.Empty(a.Observe(Pfd("FMA lateral: RWY, green")));
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
