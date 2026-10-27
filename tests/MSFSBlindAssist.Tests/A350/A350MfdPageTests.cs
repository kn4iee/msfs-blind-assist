using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The MFD read as lines a pilot can move through and act on, from its drawn elements and texts by geometry.
// Fixture: both MFDs on the DATA/STATUS page, on the stand at VCBI (2026-10-27).
public class A350MfdPageTests
{
    private static A350DrawCapture Fixture() =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ground_MFD.json")))!;

    [Fact]
    public void The_data_status_page_reads_as_lines()
    {
        var lines = A350MfdPage.Read(Fixture(), 0).Select(l => l.Spoken).ToList();
        Assert.Equal("FMS 1 menu, cyan", lines[0]);
        Assert.Equal("-----------, white", lines[1]);   // the header: no flight number yet
        Assert.Equal(new[] { "ACTIVE button", "POSITION button", "SEC INDEX button", "DATA button" }, lines.Skip(2).Take(4));
        Assert.Contains("ACFT STATUS tab", lines);
        Assert.Contains("FMS P/N tab", lines);
        Assert.Contains("IDLE: +0.0, green", lines);
        Assert.Contains("PERF: +0.0, green", lines);
        Assert.Contains("FUEL PENALTY: +000.0 %, cyan", lines);
        Assert.Contains("MODIFY button", lines);
        Assert.Contains("SWAP * button", lines);
        Assert.Contains("DELETE ALL * button, grey", lines);
        Assert.Contains("CLEAR INFO button", lines);
        Assert.Contains("A350-900P / TRENT XWB-84, green", lines);
        Assert.Contains("NAV DATABASE, white; SJ6192002, green", lines);
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, black", lines[^1]);   // an FMS message shows in the scratchpad
    }

    // The INIT page empty (live, KCCU INIT): amber boxes are a required entry, dashes nothing yet.
    [Fact]
    public void Boxes_dashes_and_units_read_as_words()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_init_empty.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("FLT NBR: required, amber", lines);
        Assert.Contains("FROM: required, amber", lines);
        Assert.Contains("ALTN: none, grey", lines);
        Assert.Contains("CRZ TEMP: none °C, grey", lines);
        Assert.Contains("TROPO: 36090 FT, cyan", lines);
    }

    // RAD NAV (live, KCCU RADNAV): IDENT, FREQ and CRS sit between the VOR 1 and VOR 2 columns and serve both, so
    // each field takes its column's header too; the LS fields have their own labels.
    [Fact]
    public void A_label_shared_by_two_columns_takes_each_columns_header()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_radnav.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("VOR 1 IDENT: KAT, cyan", lines);
        Assert.Contains("VOR 2 IDENT: KAT, cyan", lines);
        Assert.Contains("VOR 1 FREQ: 114.10, cyan", lines);
        Assert.Contains("VOR 2 CRS: empty", lines);
        Assert.Contains("IDENT: empty", lines);
        Assert.Contains("FREQ/CHAN: empty", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Field:"));
    }

    // A checklist (live, C/L MENU > COCKPIT PREPARATION): an item's dot leader reads as a colon, as on the E/WD,
    // and the line to fill ("____") as blank; cyan is still to do.
    [Fact]
    public void A_checklist_item_reads_without_its_dot_leader()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_cl_cockpit_prep.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("GEAR PINS & COVERS: REMOVED button, cyan", lines);
        Assert.Contains("FUEL QUANTITY: blank KG button, cyan", lines);
        Assert.Contains("BARO REF: blank (BOTH) button, cyan", lines);
        Assert.Contains("C/L COMPLETE button", lines);
    }

    // ILS 04 at VCBI tuned on RAD NAV (live): the font draws a degree sign as "d" ("dC" is °C), here after the
    // front course, "F040" and "d".
    [Fact]
    public void A_course_reads_its_degree_sign()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_radnav_ls.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("CRS: F040°, cyan", lines);
        Assert.Contains("FREQ/CHAN: 109.90, cyan", lines);
        Assert.Contains("IDENT: IKW, cyan", lines);
    }

    // SURV CONTROLS (live, transponder in standby): a selector box's options each read with the header above
    // the box and whether selected (an unselected option is grey); a check box reads checked (a fill in its
    // square) or not, grey while unavailable; the squawk field takes the label above it.
    [Fact]
    public void Surv_controls_read_their_options_and_check_boxes()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_surv_controls.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("TERR SYS: ON, selected, green", lines);
        Assert.Contains("TERR SYS: OFF", lines);
        Assert.Contains("G/S MODE: ON, selected, green", lines);
        Assert.Contains("WXR/PWS: AUTO", lines);
        Assert.Contains("WXR/PWS: OFF, selected, white", lines);
        Assert.Contains("MODE: AUTO", lines);
        Assert.Contains("MODE: STBY, selected, white", lines);
        Assert.Contains("ALT RPTG: OFF, selected, white", lines);
        Assert.Contains("SQWK: 2000, cyan", lines);
        Assert.Contains("STBY check box, checked, unavailable", lines);
        Assert.Contains("TA/RA check box, not checked, unavailable", lines);
        Assert.Contains("NORM check box, checked, unavailable", lines);
        Assert.DoesNotContain(lines, l => l.Contains("ON OFF"));
        Assert.Equal(lines.IndexOf("TERR SYS: ON, selected, green") + 1, lines.IndexOf("TERR SYS: OFF"));   // a box reads together
    }

    // F-PLN with no flight plan (live, in flight): the hidden revisions menu's rectangle covers the list and the
    // header; it is a container, so each F-PLN row and the POSITION key read on their own.
    [Fact]
    public void The_flight_plan_reads_row_by_row()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_fpln_empty.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("POSITION button", lines);
        Assert.Contains("P.POS --:-- --- -----, green", lines);
        Assert.Contains("- - - - - - DISCONTINUITY - - - - - -, white", lines);
        Assert.Contains("- - - - - - END OF F-PLN - - - - - -, white", lines);
        Assert.DoesNotContain(lines, l => l.Contains("DISCONTINUITY") && l.Contains("END OF F-PLN"));
    }

    [Fact]
    public void A_line_knows_where_to_click()
    {
        var modify = A350MfdPage.Read(Fixture(), 0).Single(l => l.Spoken == "MODIFY button");
        Assert.Equal((583.5f, 442f), (modify.ClickX, modify.ClickY));
        var fo = A350MfdPage.Read(Fixture(), 1).Single(l => l.Spoken == "MODIFY button");
        Assert.Equal((583.5f, 442f), (fo.ClickX, fo.ClickY));   // in the first officer's own coordinates
        Assert.Equal("FMS 2 menu, cyan", A350MfdPage.Read(Fixture(), 1)[0].Spoken);
    }
}
