using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The Ctrl+M rows the display call-outs add after the ones that existed, listed by area (E/WD, PFD, ND, datalink) in
/// <see cref="A350CalloutRows"/>, one file per area so two call-outs never edit one line: the dialog lists the rows that
/// existed first, then the areas' rows in that order, and one key is one setting.
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

        // The memory warning's row is the last that existed before the areas' lists: after it come the added rows, in area order, and nothing else.
        int lastExisting = rows.FindIndex(r => r.Key == A350MemoryWarning.MuteKey);
        Assert.NotEqual(-1, lastExisting);
        Assert.Equal(added, rows.Skip(lastExisting + 1));
    }

    [Fact]
    public void No_added_row_shares_a_key_with_a_variable_row_or_another_added_row()
    {
        var variableKeys = MonitorRowBuilder.Build(new IniBuildsA350Definition().GetVariables()).Select(r => r.Key).ToHashSet();
        var addedKeys = Areas().SelectMany(area => area).Select(row => row.Key).ToList();

        // A second row under one key lists that setting twice, and ticking one ticks the other.
        Assert.Empty(addedKeys.Intersect(variableKeys));
        Assert.Empty(addedKeys.GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key));
    }
}
