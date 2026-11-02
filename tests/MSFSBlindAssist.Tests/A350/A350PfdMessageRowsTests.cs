using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Plan 07 Task 2, the owner's answer to question 6 (2026-10-31): one Ctrl+M row per PFD message instead of the one "PFD flags and
// warnings" row; a flag the list does not name stays on that row. A pilot who had unticked the old row keeps every message muted.
public class A350PfdMessageRowsTests
{
    [Fact]
    public void Each_pfd_message_has_its_own_row()
    {
        Assert.Contains("RWY TOO SHORT", A350PfdFlagAnnouncer.Messages);
        Assert.Contains("MAX REVERSE", A350PfdFlagAnnouncer.Messages);
        foreach (var message in A350PfdFlagAnnouncer.Messages)
        {
            Assert.Contains((A350PfdFlagAnnouncer.KeyFor(message), message + " call-out"), A350CalloutRows.Pfd);
            Assert.StartsWith("A350_PFD_MSG_", A350PfdFlagAnnouncer.KeyFor(message), StringComparison.Ordinal);
        }
        Assert.Equal("A350_PFD_MSG_RWY_TOO_SHORT", A350PfdFlagAnnouncer.KeyFor("RWY TOO SHORT"));
        Assert.Equal(A350PfdFlagAnnouncer.Messages.Count, A350CalloutRows.Pfd.Select(r => r.Key).Distinct().Count());
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(new Dictionary<string, MSFSBlindAssist.SimConnect.SimVarDefinition>());
        Assert.Contains(rows, r => r.Key == A350PfdFlagAnnouncer.KeyFor("PULL UP"));
        Assert.Contains(rows, r => r.Key == A350PfdFlagAnnouncer.MuteKey);   // what the list does not name
    }

    [Fact]
    public void A_muted_message_says_nothing()
    {
        Func<string, bool> muted = key => key == A350PfdFlagAnnouncer.KeyFor("RWY TOO SHORT") || key == A350PfdFlagAnnouncer.SplitDone;
        Assert.True(A350PfdFlagAnnouncer.IsMuted("RWY TOO SHORT, amber", muted));
        Assert.False(A350PfdFlagAnnouncer.IsMuted("MAX REVERSE, green", muted));
        Assert.False(A350PfdFlagAnnouncer.IsMuted("SOMETHING NEW, amber", muted));
        Assert.Equal(A350PfdFlagAnnouncer.KeyFor("DH"), A350PfdFlagAnnouncer.MuteKeyFor("DH 200, amber"));   // the same flag while its figures move
        Assert.Equal(A350PfdFlagAnnouncer.MuteKey, A350PfdFlagAnnouncer.MuteKeyFor("SOMETHING NEW, amber"));
    }

    [Fact]
    public void The_old_rows_mute_carries_to_every_message()
    {
        // Before the split has run (the old row unticked, nothing else): every message stays quiet.
        var disabled = new List<string> { A350PfdFlagAnnouncer.MuteKey };
        foreach (var message in A350PfdFlagAnnouncer.Messages)
            Assert.True(A350PfdFlagAnnouncer.IsMuted(message + ", red", disabled.Contains));

        // The split: every message's own row starts unticked, once.
        A350PfdFlagAnnouncer.SplitOldMute(disabled);
        foreach (var message in A350PfdFlagAnnouncer.Messages)
            Assert.Contains(A350PfdFlagAnnouncer.KeyFor(message), disabled);
        Assert.True(A350PfdFlagAnnouncer.IsMuted("PULL UP, red", disabled.Contains));

        // Ticked back by the pilot: it speaks, and a later split leaves that choice alone.
        disabled.Remove(A350PfdFlagAnnouncer.KeyFor("PULL UP"));
        A350PfdFlagAnnouncer.SplitOldMute(disabled);
        Assert.False(A350PfdFlagAnnouncer.IsMuted("PULL UP, red", disabled.Contains));
        Assert.True(A350PfdFlagAnnouncer.IsMuted("ATT, red", disabled.Contains));

        // The old row left ticked: the split mutes nothing.
        var none = new List<string>();
        A350PfdFlagAnnouncer.SplitOldMute(none);
        Assert.False(A350PfdFlagAnnouncer.IsMuted("PULL UP, red", none.Contains));
    }

    [Fact]
    public void A_flashing_message_is_said_once_as_flashing()
    {
        var flags = new A350PfdFlagAnnouncer();
        var said = new List<string>();
        for (int n = 0; n < 12; n++)
            said.AddRange(flags.Observe(n % 2 == 0 ? new[] { "RWY TOO SHORT, amber" } : Array.Empty<string>()));
        Assert.Equal(new[] { "RWY TOO SHORT, amber, flashing" }, said);

        var steady = new A350PfdFlagAnnouncer();
        steady.Observe(new[] { "MAX REVERSE, green" });
        Assert.Equal(new[] { "MAX REVERSE, green" }, steady.Observe(new[] { "MAX REVERSE, green" }));
    }
}
