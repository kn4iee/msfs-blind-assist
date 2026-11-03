using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 09 Task 2: the MFD window ends each line that does something with the A380 window's role word (spec 2.7: the window
/// said "required" but never that a field takes typed input). The reader's own words stay; the window's shown line adds the role.</summary>
public class A350MfdRolesTests
{
    private static IReadOnlyList<string> Shown(string fixture) =>
        A350MfdWindow.PageLines(A350Fixtures.Capture(fixture + ".json"), 0, dark: false).Shown;

    [Fact]
    public void A_field_reads_edit() => Assert.Contains("FROM: VCBI, cyan, edit", Shown("mfd_notepad_open"));

    [Fact]
    public void A_required_field_reads_required_edit() => Assert.Contains("FLT NBR: required, amber, edit", Shown("mfd_init_empty"));

    [Fact]
    public void The_scratchpad_reads_edit() => Assert.Contains(Shown("mfd_init_empty"), l => l.StartsWith("Scratchpad: ", StringComparison.Ordinal) && l.EndsWith(", edit", StringComparison.Ordinal));

    [Fact]
    public void A_key_reads_button() => Assert.Contains("RETURN button", Shown("mfd_departure_rwy_list"));

    [Fact]
    public void A_list_opener_reads_combo_box_collapsed_or_expanded()
    {
        var rwy = Shown("mfd_departure_rwy_list");
        Assert.Contains("RWY, combo box, expanded", rwy);
        Assert.Contains("TRANS, grey, combo box, collapsed, dimmed", rwy);
        Assert.Contains("ACTIVE, combo box, collapsed", rwy);
        Assert.Contains("MODE: ECON, cyan, combo box, collapsed", Shown("mfd_fuel_load_altn_none"));
        Assert.Contains("ACTIVE, combo box, expanded", Shown("mfd_active_menu_open"));
    }

    [Fact]
    public void A_greyed_key_reads_dimmed() => Assert.Contains("DELETE ALL * button, grey, dimmed", Shown("mfd_active_menu_open"));

    [Fact]
    public void A_static_text_has_no_role_word()
    {
        var lines = A350MfdPage.Read(A350Fixtures.Capture("mfd_init_empty.json"), 0);
        var shown = Shown("mfd_init_empty");
        foreach (var (line, i) in lines.Select((l, i) => (l, i)).Where(p => p.l.Kind is "title" or "text"))
            Assert.Equal(line.Spoken, shown[i]);
    }

    // The reader's own lines (the probe, the status display, the tests) keep their words: only the window's shown line changes.
    [Fact]
    public void The_readers_lines_keep_their_words()
    {
        var lines = A350MfdPage.Read(A350Fixtures.Capture("mfd_departure_rwy_list.json"), 0);
        Assert.Contains(lines, l => l.Spoken == "RWY button, menu open");
        Assert.Contains(lines, l => l.Spoken == "TRANS button, grey");
    }
}
