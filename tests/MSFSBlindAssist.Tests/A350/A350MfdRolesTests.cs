using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 09 Task 2, plan 25 Task 2: the MFD window shows each line as the A380's MFD window does (`FBWA380MCDUForm.RoleWord`,
/// `RefreshDisplay`): "&lt;text&gt;, &lt;role&gt;[, state][, dimmed]", the colours said in the owner's words (<see cref="A350MfdWording"/>).
/// The reader's own words stay; the window's shown line changes.</summary>
public class A350MfdRolesTests
{
    private static IReadOnlyList<string> Shown(string fixture) =>
        A350MfdWindow.PageLines(A350Fixtures.Capture(fixture + ".json"), 0, dark: false).Shown;

    [Theory]
    [InlineData("field", "CI: required, amber", false, "CI: required, edit")]
    [InlineData("field", "ETA: none, amber", false, "ETA: none, required, edit")]
    [InlineData("field", "LEVEL: FL required, grey", false, "LEVEL: FL required, edit, dimmed")]
    [InlineData("field", "Notepad: HELLO WORLD, cyan, menu open", false, "Notepad: HELLO WORLD, edit, menu open")]
    [InlineData("scratchpad", "Scratchpad: CHECK DATABASE CYCLE, amber", false, "Scratchpad: CHECK DATABASE CYCLE, edit")]
    [InlineData("scratchpad", "Scratchpad: empty", false, "Scratchpad: empty, edit")]
    [InlineData("button", "INSERT * TMPY button, amber", false, "INSERT * TMPY, button")]
    [InlineData("button", "Saved notes button", false, "Saved notes, button")]
    [InlineData("button", "DELETE ALL * button, grey", false, "DELETE ALL *, button, dimmed")]
    [InlineData("button", "ERASE button, yellow", false, "ERASE, button, temporary")]
    [InlineData("menuitem", "DEPARTURE button, grey", false, "DEPARTURE, button, dimmed")]
    [InlineData("menuitem", "MIRO3G button", false, "MIRO3G, button")]
    [InlineData("menu", "FMS 1 menu, cyan", true, "FMS 1, combo box, collapsed")]
    [InlineData("menu", "MODE: ECON menu, cyan", true, "MODE: ECON, combo box, collapsed")]
    [InlineData("button", "RWY button, menu open", true, "RWY, combo box, expanded")]
    [InlineData("button", "TRANS button, grey", true, "TRANS, combo box, collapsed, dimmed")]
    [InlineData("tab", "F-PLN tab, selected", false, "F-PLN, tab, selected")]
    [InlineData("tab", "CMS tab", false, "CMS, tab")]
    [InlineData("checkbox", "FULL check box, checked, cyan", false, "FULL, check box, checked")]
    [InlineData("checkbox", "DIRECT check box, checked, yellow", false, "DIRECT, check box, checked, temporary")]
    [InlineData("checkbox", "X check box, not checked, unavailable", false, "X, check box, not checked, dimmed")]
    [InlineData("checkbox", "check box, not checked", false, "check box, not checked")]
    [InlineData("option", "ADS-C: OFF, selected, green", false, "ADS-C: OFF, radio button, selected")]
    [InlineData("option", "ADS-C: ON", false, "ADS-C: ON, radio button")]
    [InlineData("waypoint", "VCBI22                             00:00, green", false, "VCBI22                             00:00, button")]
    [InlineData("waypoint", "IKONA         038° 15       2000, cyan, overfly, ALT magenta", false, "IKONA         038° 15       2000, overfly, ALT constraint, button")]
    [InlineData("text", "A350-900P / TRENT XWB-84, green", false, "A350-900P / TRENT XWB-84")]
    [InlineData("title", "ACTIVE/F-PLN; TMPY, yellow", false, "ACTIVE/F-PLN; TMPY")]
    [InlineData("message", "Scratchpad: CHECK DATABASE CYCLE, amber", false, "Scratchpad: CHECK DATABASE CYCLE")]
    public void A_line_reads_as_the_a380_windows(string kind, string spoken, bool opener, string shown) =>
        Assert.Equal(shown, A350MfdPage.Shown(new MfdLine(kind, spoken, 0, 0, Opener: opener)));

    [Fact]
    public void A_highlighted_row_says_so_after_its_role() =>
        Assert.Equal("MIRO3G, button, highlighted", A350MfdPage.Shown(new MfdLine("menuitem", "MIRO3G button", 0, 0) { Highlighted = true }));

    [Fact]
    public void A_field_reads_edit() => Assert.Contains("FROM: VCBI, edit", Shown("mfd_notepad_open"));

    [Fact]
    public void A_required_field_reads_required_edit() => Assert.Contains("FLT NBR: required, edit", Shown("mfd_init_empty"));

    [Fact]
    public void The_scratchpad_reads_edit() => Assert.Contains(Shown("mfd_init_empty"), l => l.StartsWith("Scratchpad: ", StringComparison.Ordinal) && l.EndsWith(", edit", StringComparison.Ordinal));

    [Fact]
    public void A_key_reads_button() => Assert.Contains("RETURN, button", Shown("mfd_departure_rwy_list"));

    [Fact]
    public void A_list_opener_reads_combo_box_collapsed_or_expanded()
    {
        var rwy = Shown("mfd_departure_rwy_list");
        Assert.Contains("RWY, combo box, expanded", rwy);
        Assert.Contains("TRANS, combo box, collapsed, dimmed", rwy);
        Assert.Contains("ACTIVE, combo box, collapsed", rwy);
        Assert.Contains("MODE: ECON, combo box, collapsed", Shown("mfd_fuel_load_altn_none"));
        Assert.Contains("ACTIVE, combo box, expanded", Shown("mfd_active_menu_open"));
    }

    [Fact]
    public void A_greyed_key_reads_dimmed() => Assert.Contains("DELETE ALL *, button, dimmed", Shown("mfd_active_menu_open"));

    [Fact]
    public void A_static_text_has_no_role_word()
    {
        var lines = A350MfdPage.Read(A350Fixtures.Capture("mfd_init_empty.json"), 0);
        foreach (var line in lines.Where(l => l.Kind is "title" or "text"))
            Assert.Equal(A350MfdWording.Speak(line), A350MfdPage.Shown(line));
    }

    // The reader's own lines (the probe, the reader's logic, the tests) keep their words: only the window's shown line changes.
    [Fact]
    public void The_readers_lines_keep_their_words()
    {
        var lines = A350MfdPage.Read(A350Fixtures.Capture("mfd_departure_rwy_list.json"), 0);
        Assert.Contains(lines, l => l.Spoken == "RWY button, menu open");
        Assert.Contains(lines, l => l.Spoken == "TRANS button, grey");
    }
}
