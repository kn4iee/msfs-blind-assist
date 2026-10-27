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
