using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The Ctrl+M rows the display call-outs list by area (E/WD, PFD, ND, datalink) in <see cref="A350CalloutRows"/>, one file per
/// area so two call-outs never edit one line: the dialog lists its own rows first, then the areas' rows in that order, and every
/// row is a setting of its own, under a key and a label no other row has.
/// </summary>
public class A350CalloutRowsTests
{
    /// <summary>The areas' lists, in the order the dialog lists them.</summary>
    private static IEnumerable<IReadOnlyList<(string Key, string Label)>> Areas()
    {
        yield return A350CalloutRows.Ecam;
        yield return A350CalloutRows.Pfd;
        yield return A350CalloutRows.Nd;
        yield return A350CalloutRows.Datalink;
    }

    [Fact]
    public void Added_rows_follow_the_existing_display_rows()
    {
        var rows = A350MonitorManagerForm.BuildRows(new IniBuildsA350Definition().GetVariables());
        var added = Areas().SelectMany(area => area).Select(row => new MonitorRow(row.Key, row.Label));

        // The memory warning's row is the last of the dialog's own: after it come the areas' rows, in area order, and nothing else.
        int lastExisting = rows.FindIndex(r => r.Key == A350MemoryWarning.MuteKey);
        Assert.NotEqual(-1, lastExisting);
        Assert.Equal(added, rows.Skip(lastExisting + 1));
    }

    // Final review (Important 3): over every row the dialog lists, the variables' and the display call-outs' as well as the areas'. One key
    // is one setting: a second row under it lists that setting twice, and ticking one ticks the other. Two rows under one label are two check
    // boxes a screen reader cannot tell apart. And an area's row under a variable's key, listed or not, mutes that variable's speech with its own.
    [Fact]
    public void No_two_rows_share_a_key_or_a_label_and_no_areas_row_takes_a_variables_key()
    {
        var def = new IniBuildsA350Definition();
        var rows = A350MonitorManagerForm.BuildRows(def.GetVariables());

        Assert.Empty(rows.GroupBy(r => r.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(rows.GroupBy(r => r.Label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(A350CalloutRows.All.Select(row => row.Key).Intersect(def.GetVariables().Keys));
    }
}
