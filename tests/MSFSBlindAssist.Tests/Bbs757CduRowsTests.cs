using System.Text;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>Plan 08 Task 1: the CDU window's rows carry an identity, so the cursor stays on the pilot's line.</summary>
public class Bbs757CduRowsTests
{
    private static Bbs757CduScreen Fixture(string name) =>
        Bbs757CduFrame.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Encoding.UTF8));

    /// <summary>The screen with some of its 14 lines replaced (each padded or cut to 24 characters).</summary>
    private static Bbs757CduScreen With(Bbs757CduScreen screen, params (int Line, string Text)[] changes)
    {
        var lines = screen.Lines.ToArray();
        foreach (var (line, text) in changes)
            lines[line] = text.PadRight(Bbs757CduFrame.Columns)[..Bbs757CduFrame.Columns];
        return new Bbs757CduScreen(lines);
    }

    private static Bbs757CduScreen Legs => Fixture("bbs757-cdu-legs.json");

    [Fact]
    public void Kinds_and_lines()
    {
        var rows = Bbs757CduRows.Build(Legs);

        Assert.Equal(Bbs757CduRowKind.Title, rows[0].Kind);
        Assert.Equal(Bbs757CduRowKind.Scratchpad, rows[^1].Kind);
        Assert.Equal("FLT NUMBER UPLINK", rows[^1].Text);
        for (int line = 1; line <= 6; line++)
            Assert.Single(rows, r => r.Kind == Bbs757CduRowKind.Value && r.Line == line);
        // LEGS shows a label (the course and distance) above lines 1-5 and the dashed line above 6.
        Assert.Equal(6, rows.Count(r => r.Kind == Bbs757CduRowKind.Label));
        Assert.All(rows.Where(r => r.Kind is Bbs757CduRowKind.Title or Bbs757CduRowKind.Scratchpad), r => Assert.Equal(0, r.Line));
        // The texts are the window's today.
        Assert.Equal(Bbs757CduFrame.ToDisplayLines(Legs), rows.Select(r => r.Text).ToList());
        Assert.StartsWith("1: SEA", rows.Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1).Text);
    }

    [Fact]
    public void Restore_follows_the_row_when_a_label_above_goes()
    {
        var before = Bbs757CduRows.Build(Legs);
        int onLine4 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 4);

        // Line 2's label goes blank: every row below it moves up one.
        var after = Bbs757CduRows.Build(With(Legs, (3, "")));
        int restored = Bbs757CduRows.Restore(before, onLine4, after);

        Assert.Equal(onLine4 - 1, restored);
        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(4, after[restored].Line);
    }

    private static Bbs757CduScreen NavRadio => Fixture("bbs757-cdu-navradio.json");

    [Fact]
    public void Nav_radio_lists_only_the_labels_it_shows()
    {
        // Captured live 2026-10-27 (first officer's CDU): labels on lines 1, 2 and 6 only.
        var rows = Bbs757CduRows.Build(NavRadio);
        Assert.Equal("        NAV RADIO", rows[0].Text);
        Assert.Equal(new[] { 1, 2, 6 }, rows.Where(r => r.Kind == Bbs757CduRowKind.Label).Select(r => r.Line));
        Assert.Equal(6, rows.Count(r => r.Kind == Bbs757CduRowKind.Value));
        Assert.Equal("NAV DATA OUT OF DATE", rows[^1].Text);
        Assert.Equal(Bbs757CduFrame.ToDisplayLines(NavRadio), rows.Select(r => r.Text).ToList());
    }

    [Fact]
    public void Restore_follows_the_row_when_a_label_above_appears()
    {
        // On NAV RADIO's PRESELECT line (6), a label appearing on line 3 pushes every row below it down one.
        var before = Bbs757CduRows.Build(NavRadio);
        int onLine6 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 6);

        var after = Bbs757CduRows.Build(With(NavRadio, (5, "ADF L              ADF R")));
        int restored = Bbs757CduRows.Restore(before, onLine6, after);

        Assert.Equal(onLine6 + 1, restored);
        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(6, after[restored].Line);
    }

    [Fact]
    public void Restore_on_a_label_that_goes_lands_on_its_value()
    {
        var before = Bbs757CduRows.Build(Legs);
        int onLabel2 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Label && r.Line == 2);

        var after = Bbs757CduRows.Build(With(Legs, (3, "")));
        int restored = Bbs757CduRows.Restore(before, onLabel2, after);

        Assert.Equal(Bbs757CduRowKind.Value, after[restored].Kind);
        Assert.Equal(2, after[restored].Line);
    }

    [Fact]
    public void Restore_keeps_the_title_and_the_scratchpad()
    {
        var before = Bbs757CduRows.Build(Legs);
        var after = Bbs757CduRows.Build(With(Legs, (3, ""), (5, "")));

        Assert.Equal(0, Bbs757CduRows.Restore(before, 0, after));
        Assert.Equal(after.Count - 1, Bbs757CduRows.Restore(before, before.Count - 1, after));
    }

    [Fact]
    public void Restore_with_nothing_to_go_on_keeps_the_index_clamped()
    {
        var after = Bbs757CduRows.Build(Legs);
        Assert.Equal(3, Bbs757CduRows.Restore(Array.Empty<Bbs757CduRow>(), 3, after));
        Assert.Equal(after.Count - 1, Bbs757CduRows.Restore(Array.Empty<Bbs757CduRow>(), 99, after));
        Assert.Equal(0, Bbs757CduRows.Restore(after, -1, after));
        Assert.Equal(-1, Bbs757CduRows.Restore(after, 2, Array.Empty<Bbs757CduRow>()));
    }

    [Fact]
    public void Act_to_mod_is_not_a_page_change()
    {
        var mod = With(Legs, (0, "  MOD RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsPageChange(Legs.Lines[0], mod.Lines[0]));

        // An entry that turns ACT into MOD (a speed on line 2) keeps the cursor on the line just edited.
        var before = Bbs757CduRows.Build(Legs);
        int onLine2 = before.ToList().FindIndex(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 2);
        var after = Bbs757CduRows.Build(With(mod, (4, "NORMY         250/------")));
        Assert.Equal(onLine2, Bbs757CduRows.Restore(before, onLine2, after));
    }

    [Fact]
    public void Next_page_is_a_page_change()
    {
        Assert.True(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     2/2"));
        Assert.True(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "       NAV RADIO"));
        Assert.True(Bbs757CduRows.IsPageChange("", "  ACT RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     1/2"));
    }

    [Fact]
    public void A_page_total_change_is_the_same_page()
    {
        // Inserting a waypoint can add a page while the pilot stays on page 1: the cursor stays on the line
        // (the MD-11's rule, [MD11-17]); only another page, or another page number, moves it to line 1.
        Assert.False(Bbs757CduRows.IsPageChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.False(Bbs757CduRows.IsPageChange("  ACT RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.True(Bbs757CduRows.IsPageChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     2/3"));
        // A route made active keeps its page.
        Assert.False(Bbs757CduRows.IsPageChange("     RTE 1           1/2", "   ACT RTE 1         1/2"));
    }

    // The first officer's VNAV pages in cruise, read live 2026-10-27 (FL380): the three pages and their counters.
    private const string VnavClb = "               CLB  1/3 ";
    private const string VnavCrz = "     ACT ECON CRZ    2/3";
    private const string VnavDes = "              DES   3/3 ";

    [Fact]
    public void A_speed_entered_on_a_vnav_page_keeps_the_page()
    {
        // A VNAV page retitles when a speed is entered (the owner, 2026-10-27; e.g. ECON CLB -> 250KT CLB on
        // Boeing FMCs, the BlueBird's exact words not captured): the same page, so the cursor stays and the
        // new title is said.
        Assert.False(Bbs757CduRows.IsPageChange("     ACT ECON CLB    1/3", "    MOD 250KT CLB    1/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavDes, "     MOD 300KT DES   3/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     MOD M.800 CRZ   2/3"));
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     ACT LRC CRZ     2/3"));
        // A new cruise altitude on the CRZ page: still page 2.
        Assert.False(Bbs757CduRows.IsPageChange(VnavCrz, "     MOD CRZ CLB     2/3"));
        Assert.True(Bbs757CduRows.IsTitleChange("     ACT ECON CLB    1/3", "    MOD 250KT CLB    1/3"));
    }

    [Fact]
    public void Another_vnav_page_is_a_page_change()
    {
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, VnavClb));   // PREV PAGE
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, VnavDes));   // NEXT PAGE twice
        Assert.True(Bbs757CduRows.IsPageChange("        NAV RADIO       ", VnavCrz));   // the VNAV key
        Assert.True(Bbs757CduRows.IsPageChange(VnavCrz, "  ACT RTE 1 LEGS     1/2"));
    }

    [Fact]
    public void Any_changed_title_is_a_title_change()
    {
        // Every other aircraft's CDU window speaks a changed title, ACT to MOD and a new page total included
        // (owner, 2026-10-27: "however the other planes have it").
        Assert.True(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/2"));
        Assert.True(Bbs757CduRows.IsTitleChange("  MOD RTE 1 LEGS     1/2", "  MOD RTE 1 LEGS     1/3"));
        Assert.True(Bbs757CduRows.IsTitleChange("", "       NAV RADIO"));
        // The same title, or the same words spaced differently, is not; nor is a blank one.
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "  ACT RTE 1 LEGS     1/2"));
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", " ACT RTE 1 LEGS      1/2"));
        Assert.False(Bbs757CduRows.IsTitleChange("  ACT RTE 1 LEGS     1/2", "   "));
    }

    [Fact]
    public void PageStart_is_line_one_value()
    {
        var rows = Bbs757CduRows.Build(Legs);
        int start = Bbs757CduRows.PageStart(rows);
        Assert.Equal(Bbs757CduRowKind.Value, rows[start].Kind);
        Assert.Equal(1, rows[start].Line);
        // LEGS has a label above line 1, so its value is the third row.
        Assert.Equal(2, start);
        Assert.Equal(0, Bbs757CduRows.PageStart(Array.Empty<Bbs757CduRow>()));
    }
}
