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
        Assert.Equal("none, white", lines[1]);   // the header: no flight number yet
        Assert.Equal(new[] { "ACTIVE button", "POSITION button", "SEC INDEX button", "DATA button" }, lines.Skip(2).Take(4));
        Assert.Contains("ACFT STATUS tab, selected", lines);
        Assert.Contains("FMS P/N tab", lines);
        Assert.Contains("IDLE: +0.0, green", lines);
        Assert.Contains("PERF: +0.0, green", lines);
        Assert.Contains("FUEL PENALTY: +000.0 %, cyan", lines);
        Assert.Contains("MODIFY button", lines);
        Assert.Contains("SWAP * button", lines);
        Assert.Contains("DELETE ALL * button, grey", lines);
        Assert.Contains("CLEAR INFO button", lines);
        Assert.Contains("A350-900P / TRENT XWB-84, green", lines);
        Assert.Contains("NAV DATABASE: SJ6192002, green", lines);
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

    // AFTER START with every item ticked (live 2026-10-28): a ticked item is drawn "ANTI ICE : SET", its leader
    // replaced by a spaced colon; it reads as the unticked one does, "ANTI ICE: SET".
    [Fact]
    public void A_ticked_checklist_item_reads_its_colon_unspaced()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_cl_after_start_ticked.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("ANTI ICE: SET button, green", lines);
        Assert.Contains("FLIGHT CONTROLS: CHECKED button, green", lines);
        Assert.Contains("GROUND CLEARANCE: RECEIVED button, green", lines);
        Assert.DoesNotContain(lines, l => l.Contains(" : "));
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

    // SURV's DEFAULT SETTINGS asks to confirm (live 2026-10-28): an open pop-up (a PopupDialog with its own texts) hides
    // what was drawn under it, the ADS-B TRAFFIC box and the DEFAULT SETTINGS key, whose places its CANCEL and CONFIRM *
    // keys took (they read "ADS-B TRAFFIC: CANCEL OFF" and "DEFAULT SETTINGS CONFIRM * button"); what is outside it reads.
    [Fact]
    public void An_open_pop_up_hides_what_it_covers()
    {
        var lines = Page("mfd_surv_default_confirm");
        Assert.Contains("SELECT SURV DEFAULT SETTINGS?, white", lines);
        Assert.Contains("CANCEL button", lines);
        Assert.Contains("CONFIRM * button", lines);
        Assert.DoesNotContain(lines, l => l.Contains("ADS-B TRAFFIC") || l.Contains("DEFAULT SETTINGS CONFIRM"));
        Assert.Contains("ADS-B RPTG: OFF, selected, white", lines);   // the transponder's, outside it
        Assert.Contains("TA/RA check box, not checked, unavailable", lines);
        Assert.Contains("SQWK: 2000, cyan", lines);
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
        Assert.Contains("P.POS none none none, green", lines);
        Assert.Contains("DISCONTINUITY, white", lines);
        Assert.Contains("END OF F-PLN, white", lines);
        Assert.DoesNotContain(lines, l => l.Contains("DISCONTINUITY") && l.Contains("END OF F-PLN"));
    }

    // P.POS's revisions menu open over the F-PLN (live, no flight plan): a menu item holds only what was drawn after it began
    // (DrawnElement.TextStart), never the list rows under it.
    [Fact]
    public void An_open_menu_reads_its_own_items_only()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_fpln_revisions.json")))!;
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        // With no flight plan every item but HOLD is unavailable (grey).
        Assert.Contains("FROM P.POS DIR TO button, grey", lines);
        Assert.Contains("INSERT NEXT WPT button, grey", lines);
        Assert.Contains("DEPARTURE button, grey", lines);
        Assert.Contains("NEW DEST button, grey", lines);
        Assert.Contains("HOLD button", lines);
        Assert.Contains("DISCONTINUITY, white", lines);
    }

    // ACTIVE/F-PLN with the uplinked VCBI-OMDB plan (live): one line per waypoint, labelled by the page's own column
    // headers (FROM, TIME, the SPD ALT menu's words; TRK, DIST, FPA for the leg into it, drawn just above its row),
    // dashes as none, the track's "d" a degree sign; only the rows above the destination bar (a sighted pilot sees
    // no more); the destination bar as one line.
    private static IReadOnlyList<string> FlightPlan() => A350MfdPage.Read(JsonSerializer.Deserialize<A350DrawCapture>(
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350",
            "Fixtures", "mfd_fpln_route.json")))!, 0).Select(l => l.Spoken).ToList();

    [Fact]
    public void A_flight_plan_reads_one_line_per_waypoint_with_its_leg()
    {
        var lines = FlightPlan();
        Assert.Contains("FROM VCBI: TIME 00:00, SPD none, ALT none, green", lines);
        Assert.Contains("BASUR: TIME none, SPD none, ALT none, green", lines);
        Assert.Contains("TVM: via P570, TRK 295°, DIST 80; TIME none, SPD none, ALT none, green", lines);
        Assert.Contains("NIVUD: via P570, TRK 306°, DIST 105; TIME none, SPD none, ALT none, green", lines);
        Assert.Contains("DISCONTINUITY, white", lines);
    }

    [Fact]
    public void A_flight_plan_reads_only_what_shows_above_the_destination_bar()
    {
        var lines = FlightPlan();
        Assert.DoesNotContain(lines, l => l.Contains("OMAA") || l.Contains("ALTN F-PLN") || l.Contains("END OF F-PLN"));
        Assert.Single(lines, l => l.Contains("DISCONTINUITY"));
        Assert.Contains("Destination OMDB: time none, EFOB none T, distance none NM, green", lines);
        Assert.Contains("DEST button", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("FROM TIME") || l.Contains("306d"));
        Assert.Contains("CLEAR INFO button", lines);   // a hidden row's box reaches under the bottom keys
        Assert.Contains("Scratchpad: NAV ACCURACY UPGRADED, black", lines);
    }

    // POSITION/IRS (live, aligned): IRS 3's texts sit at y 430 and 432, either side of a fixed 12 px bucket's edge;
    // a row is the texts near each other, wherever they fall. A position's "d" is its degree sign.
    [Fact]
    public void A_row_is_texts_near_each_other_and_a_position_reads_degrees()
    {
        var lines = A350MfdPage.Read(JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "mfd_irs.json")))!, 0)
            .Select(l => l.Spoken).ToList();
        Assert.Contains("IRS 3: NAV, 0.1 NM, green", lines);
        Assert.Contains("IRS ALIGNED ON GNSS POS: 07°10.5N/079°53.1E, green", lines);
    }

    // A row of white headings over a row of values (live: DEPARTURE's RWY LENGTH CRS, its EOSID FREQ/CHAN SID TRANS,
    // DATA/STATUS's ACTIVE and SECOND database dates) reads each value with its heading, a grey unit after its value
    // (the font's "d" a degree sign); by order when the counts match (LENGTH's value ends nearer CRS than LENGTH).
    private static IReadOnlyList<string> Page(string fixture) => PageLines(fixture).Select(l => l.Spoken).ToList();

    private static IReadOnlyList<MfdLine> PageLines(string fixture) => A350MfdPage.Read(JsonSerializer.Deserialize<A350DrawCapture>(
        File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures",
            fixture + ".json")))!, 0);

    // Live 2026-10-28: INSERT NEXT WPT for BASUR opens a pop-up in the revision menu's column (NextWaypointPopup, no
    // rectangle of its own): its entry field read "C220°: none", the label of the F-PLN row under it. The field takes
    // the pop-up's title, and the row text it covers is gone.
    [Fact]
    public void An_insert_next_waypoint_popup_names_its_field_by_its_title()
    {
        var all = PageLines("mfd_insert_next_wpt");
        var lines = all.Select(l => l.Spoken).ToList();
        var field = Assert.Single(all, l => l.Kind == "field" && l.Spoken.StartsWith("INSERT NEXT WPT FROM BASUR", StringComparison.Ordinal));
        Assert.Equal("INSERT NEXT WPT FROM BASUR: none, cyan", field.Spoken);
        Assert.DoesNotContain(lines, l => l.StartsWith("C220°:", StringComparison.Ordinal));
        Assert.Contains("CANCEL button", lines);
    }

    // Live 2026-10-28, AIRWAYS after VIA P570 TO ITURA: the page's VIA/TO list keeps text boxes for rows off the screen,
    // one under the INSERT key, which read "Field: INSERT". The key is drawn after (over) the box: the text is its.
    [Fact]
    public void A_key_drawn_over_a_hidden_list_box_keeps_its_text()
    {
        var lines = Page("mfd_airways_p570_itura");
        Assert.Contains("INSERT button", lines);
        Assert.DoesNotContain(lines, l => l.Contains("Field: INSERT", StringComparison.Ordinal));
        Assert.Contains("VIA: P570, yellow", lines);
        Assert.Contains("TO: ITURA, yellow", lines);
    }

    // Live 2026-10-28, WIND CRZ at TVM: four rows of altitude, direction and speed boxes under the headers ALT and T.WIND
    // read "Field: none" twelve times. A box with no label of its own takes its column's header, numbered by row.
    [Fact]
    public void A_table_of_boxes_takes_its_column_headers_by_row()
    {
        var lines = Page("mfd_wind_crz_empty");
        Assert.DoesNotContain(lines, l => l.StartsWith("Field:", StringComparison.Ordinal));
        Assert.Contains("CRZ WIND AT: TVM, cyan", lines);
        int first = lines.ToList().IndexOf("ALT 1: none, cyan");
        Assert.True(first >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "ALT 1: none, cyan", "T.WIND 1: none°, cyan", "T.WIND 1: none KT, cyan", "ALT 2: none, cyan" },
            lines.Skip(first).Take(4));
        Assert.Contains("T.WIND 4: none KT, cyan", lines);
    }

    [Fact]
    public void The_window_moves_to_a_menu_as_it_opens_and_only_then()
    {
        var closed = Page("mfd_fpln_tmpy_basu1d").Select(s => new MfdLine("text", s, 0, 0)).ToList();
        var open = PageLines("mfd_fpln_revision_basur");
        int first = open.ToList().FindIndex(l => l.Kind == "menuitem");
        Assert.Equal(first, A350MfdPage.MenuJustOpened(closed, open));
        Assert.Null(A350MfdPage.MenuJustOpened(open, open));     // already open: the pilot's place is kept
        Assert.Null(A350MfdPage.MenuJustOpened(open, closed));   // closed
    }

    // Live 2026-10-28: BASUR's revision menu read woven between the F-PLN rows, and its first item, FROM P.POS DIR TO,
    // white in the column-header band, was taken for a header and lost. The menu (UIFlightplanRevisionsMenu) is drawn
    // at a fixed place and names no waypoint: its items read together after the page title, as menu items (the window
    // moves to the first when a menu opens), and the rows stay readable.
    [Fact]
    public void A_waypoints_revision_menu_reads_whole_and_together()
    {
        var all = PageLines("mfd_fpln_revision_basur");
        var lines = all.Select(l => l.Spoken).ToList();
        var items = new[]
        {
            "FROM P.POS DIR TO button", "INSERT NEXT WPT button", "DELETE * button", "DEPARTURE button, grey", "ARRIVAL button, grey",
            "OFFSET button", "OVERFLY * button", "HOLD button", "AIRWAYS button", "ENABLE ALTN * button", "NEW DEST button",
            "CONSTRAINTS button", "CMS button", "STEP ALTS button", "WIND button", "WHAT IF button, grey",
        };
        int first = lines.IndexOf(items[0]);
        Assert.True(first > 0, string.Join(" | ", lines));
        Assert.Equal(items, lines.Skip(first).Take(items.Length));
        Assert.Equal("ACTIVE/F-PLN, black", lines[first - 1]);
        Assert.All(all.Skip(first).Take(items.Length), l => Assert.Equal("menuitem", l.Kind));
        Assert.Contains(lines, l => l.StartsWith("BASUR: via BASU1D", StringComparison.Ordinal));
    }

    [Fact]
    public void Headings_over_values_read_each_value_with_its_heading()
    {
        var departure = Page("mfd_departure_empty");
        Assert.Contains("RWY none, LENGTH none M, CRS none°, green", departure);
        Assert.Contains("EOSID NONE, FREQ/CHAN none, SID none, TRANS none, green", departure);
        Assert.Contains("FROM: VCBI, green", departure);
        var status = Page("mfd_data_status_db");
        Assert.Contains("ACTIVE 01OCT-28OCT, SECOND 01OCT-28OCT, green", status);
        Assert.DoesNotContain(status, l => l.StartsWith("ACTIVE SECOND"));
    }

    // DEPARTURE in a temporary revision (live, runway 22 picked): the A350 draws temporary data yellow; a text's
    // colour survives the row reading (yellow read as white before), and the SID list reads one button per SID.
    [Fact]
    public void A_temporary_revision_reads_yellow()
    {
        var lines = Page("mfd_departure_tmpy_sids");
        Assert.Contains("RWY 22, LENGTH 3340 M, CRS 215°, yellow", lines);
        Assert.Contains("FROM: VCBI, yellow", lines);
        Assert.Contains("BASU1D button", lines);
        Assert.Contains("TMPY F-PLN button, yellow", lines);
    }

    // The temporary F-PLN with BASU1D (live): the MFD font draws symbols as lowercase letters (A350MFD-Regular.ttf:
    // a left arrow, b right arrow, d degree, f the overfly triangle; the other lowercase letters are small capitals). An
    // arrow after an ident is the procedure's turn, in words (BI611: BASU1D's right turn after 500 ft, track 221 to 279;
    // a bare arrow is a symbol a screen reader may not say). A row's colour is its values'; an ident of another colour
    // (the white TO waypoint in a yellow plan) says so once.
    [Fact]
    public void A_temporary_flight_plan_reads_its_symbols_and_colours()
    {
        var lines = Page("mfd_fpln_tmpy_sid");
        Assert.Contains("DER22 (overfly) (white): via BASU1D, TRK 218°, DIST 1; TIME none, SPD none, ALT none, yellow", lines);
        Assert.Contains("500: via C220°, TRK 221°, DIST 0; TIME none, SPD none, ALT 500 (magenta), yellow", lines);
        Assert.Contains("BI611 (right turn): via BASU1D, TRK 279°, DIST 5; TIME none, SPD none, ALT none, yellow", lines);
        Assert.Contains("END OF TMPY F-PLN, yellow", lines);
        Assert.Contains("INSERT * TMPY button, amber", lines);
    }

    // The active F-PLN with predictions (live, BASU1D): an altitude column can hold a constraint's star and the predicted
    // altitude, both read (the reader once took the star alone and dropped the 5000); a magenta star is a constraint
    // predicted to be met, an amber one missed (the Airbus FMS's). A ditto mark is the value above, said in words.
    [Fact]
    public void A_flight_plans_constraint_star_and_ditto_are_read_in_words()
    {
        var lines = Page("mfd_fpln_basu1d");
        Assert.Contains("BI613: via BASU1D, TRK 295°, DIST 4; TIME 00:05, SPD same as above, ALT 5000 (constraint met), green", lines);
        Assert.Contains("BI453: via BASU1D, TRK 309°, DIST 6; TIME 00:04, SPD 250, ALT 5000, green", lines);
        Assert.Contains(lines, l => l.StartsWith("BI611 (right turn): via BASU1D", StringComparison.Ordinal));
    }

    // After the 1.2.7 hotfix (live 2026-10-28) both MFDs open on the CRASH REPORTING consent dialog (EULADialog) over
    // DATA/STATUS: its sentences were read woven into the page's lines ("NAV DATABASE; AND SEEN ONLY BY THE DEVELOPMENT
    // TEAM. THEY: SJ6192002"). It hides what it covers, as a pop-up does, and keeps its two buttons.
    [Fact]
    public void The_crash_reporting_dialog_hides_the_page_under_it()
    {
        var lines = Page("mfd_crash_reporting");
        Assert.Contains("CRASH REPORTING, white", lines);
        Assert.Contains("IF YOU DECLINE THEN NOTHING IS SENT., white", lines);
        Assert.Contains("DECLINE button", lines);
        Assert.Contains("ACCEPT button", lines);
        Assert.DoesNotContain(lines, l => l.Contains("NAV DATABASE", StringComparison.Ordinal) || l.Contains("IDLE:", StringComparison.Ordinal));
    }

    // Live 2026-10-28: ACTIVE's menu read woven into DATA/STATUS ("F-PLN button, DATA/STATUS, PERF button, ACFT STATUS
    // tab, FUEL&LOAD button..."). A sighted pilot sees the menu hanging off ACTIVE, over the page's left column: its
    // items follow their button, and what it covers is gone; the tab beside it stays.
    [Fact]
    public void An_open_header_menu_reads_after_its_button_and_hides_what_it_covers()
    {
        var all = PageLines("mfd_active_menu_open");
        var lines = all.Select(l => l.Spoken).ToList();
        int active = lines.IndexOf("ACTIVE button, menu open");
        Assert.True(active >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "F-PLN button", "PERF button", "FUEL&LOAD button", "WIND button", "INIT button" }, lines.Skip(active + 1).Take(5));
        Assert.All(all.Skip(active + 1).Take(5), l => Assert.Equal("menuitem", l.Kind));
        Assert.DoesNotContain(lines, l => l.StartsWith("DATA/STATUS", StringComparison.Ordinal));   // under the menu
        Assert.Contains("ACFT STATUS tab, selected", lines);                                                     // beside it
        Assert.Contains("POSITION button", lines);
    }

    // The ILS 12L final at OMDB (live 2026-10-28, IMPE3B via REREK): the final leg's flight path angle "-3.0" (white) is
    // drawn beside its distance, nearer the DIST header than the FPA one; it read "DIST -3.0 (white)", a second distance.
    [Fact]
    public void A_final_legs_flight_path_angle_reads_as_its_fpa()
    {
        var lines = Page("mfd_fpln_ils12l_final");
        var final = Assert.Single(lines, l => l.StartsWith("OMDB12L:", StringComparison.Ordinal));
        Assert.Contains("DIST 5, FPA -3.0° (white)", final);
        Assert.DoesNotContain(lines, l => l.Contains("DIST -3.0"));
    }

    // FUEL&LOAD before entry (live): a value then its unit (the unit sits 4 px higher); "ooo.o" boxes are a required
    // entry; a second box on a row shares the row's label (RTE RSV weight and percent, FINAL fuel and time); a label
    // beside a field is close to it (the row label DEST is the table's; MIN FUEL AT DEST's field takes the heading
    // above it); a menu takes its label; times drawn in parts read as one; a table's rows carry their row labels.
    [Fact]
    public void Fuel_and_load_reads_every_value_with_its_label()
    {
        var lines = Page("mfd_fuel_load_empty");
        foreach (var l in new[]
        {
            "GW: none; CG: none; FOB: none, green", "ZFW: required T, amber", "ZFWCG: required %, amber", "BLOCK: required T, amber",
            "TAXI: 0.6 T, cyan", "PAX NBR: required, amber", "MODE: ECON menu, cyan", "TRIP: none T, none, green", "CI: 52, cyan",
            "RTE RSV: none T, cyan", "RTE RSV: 5.0 %, cyan", "ALTN: none T, cyan", "ALTN: none, green", "TOW: none, green",
            "FINAL: none T, cyan", "FINAL: 00:30, cyan", "LW: none, green", "MIN FUEL AT DEST: none T, cyan",
            "DEST OMDB: TIME none, EFOB none, green", "ALTN OMAA: TIME none, EFOB none, green", "EXTRA: none T, none, green",
        })
            Assert.Contains(l, lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("Field:") || l.Contains(" CI:"));
    }

    // PERF T.O after the OIS sent its speeds (live): TOGA and FLEX are check boxes labelled right of the box; the FLEX
    // temperature field takes FLEX's label; the uplinked speeds (yellow) beside V1, VR and V2 are theirs; FLAPS,
    // PACKS and ANTI-ICE label their menus from above; a label is shared only with a box right beside it.
    [Fact]
    public void Perf_takeoff_reads_its_check_boxes_and_the_uplinked_speeds()
    {
        var lines = Page("mfd_perf_to_uplinked");
        foreach (var l in new[]
        {
            "TOGA check box, not checked", "FLEX check box, checked, cyan", "FLEX: +67 °C, cyan",
            "V1: required KT, amber", "V1: 142 KT, yellow", "VR: 143 KT, yellow", "V2: 148 KT, yellow",
            "CONFIRM * TO SPDs button, yellow", "FLAPS: 2 menu, cyan", "PACKS: ON menu, cyan", "ANTI-ICE: OFF menu, cyan",
            "THS FOR: 30.1 %, cyan",
        })
            Assert.Contains(l, lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("TOGA:") || l.StartsWith("VR: +67"));
    }

    // PERF T.O after CONFIRM * TO SPDs (live): the speeds in their fields, and the F, S and green dot speeds; green dot's
    // label is no text but a drawn green circle (a 20 px square stroke) left of its value.
    [Fact]
    public void The_green_dot_speed_takes_its_drawn_symbol_as_label()
    {
        var lines = Page("mfd_perf_to_confirmed");
        foreach (var l in new[] { "V1: 142 KT, cyan", "VR: 143 KT, cyan", "V2: 148 KT, cyan", "F: 138 KT, green", "S: 174 KT, green", "Green dot: 195 KT, green" })
            Assert.Contains(l, lines);
        Assert.DoesNotContain("V2: 195 KT, green", lines);
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

    private static A350DrawCapture Load(string fixture) => JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", fixture + ".json")))!;

    public static IEnumerable<object[]> MfdFixtures() => Directory.GetFiles(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "tests",
            "MSFSBlindAssist.Tests", "A350", "Fixtures"), "*.json")
        .Select(Path.GetFileNameWithoutExtension).Where(n => n!.StartsWith("mfd_", StringComparison.Ordinal) || n == "ground_MFD")
        .OrderBy(n => n, StringComparer.Ordinal).Select(n => new object[] { n! });

    /// <summary>The captain's MFD drawn on both halves: the captain's half as it is, then the same drawn again 836 to the
    /// right as the first officer's (both MFDs draw one after the other in one frame).</summary>
    private static A350DrawCapture CaptainOnBothSides(A350DrawCapture cap)
    {
        const float half = 836;
        int k = cap.Texts.TakeWhile(t => t.X < half).Count();
        Assert.All(cap.Texts.Skip(k), t => Assert.True(t.X >= half));   // the captain's texts come first
        var texts = cap.Texts.Take(k).ToList();
        var elements = cap.Elements.Where(e => e.X < half).ToList();
        var shapes = (cap.Shapes ?? Array.Empty<DrawnShape>()).Where(s => s.X0 < half).ToList();
        return cap with
        {
            Texts = texts.Concat(texts.Select(t => t with { X = t.X + half })).ToList(),
            Elements = elements.Concat(elements.Select(e => e with { X = e.X + half, RectX = e.RectX + half, TextStart = e.TextStart + k })).ToList(),
            Shapes = shapes.Concat(shapes.Select(s => s with { X0 = s.X0 + half, X1 = s.X1 + half })).ToList(),
        };
    }

    // Review 2026-10-28: no test read the first officer's MFD through the menus and pop-ups. Every captured page, drawn
    // on the first officer's half, reads as the captain's does, each line clicked at the same point of its own MFD.
    [Theory]
    [MemberData(nameof(MfdFixtures))]
    public void The_first_officers_mfd_reads_as_the_captains(string fixture)
    {
        var cap = Load(fixture);
        var captain = A350MfdPage.Read(cap, 0);
        var firstOfficer = A350MfdPage.Read(CaptainOnBothSides(cap), 1);
        Assert.Equal(captain.Select(l => (l.Kind, l.Spoken)), firstOfficer.Select(l => (l.Kind, l.Spoken)));
        Assert.All(captain.Zip(firstOfficer), p =>   // a shape's point moved 836 and back rounds in its last digit
            Assert.True(Math.Abs(p.First.ClickX - p.Second.ClickX) < 0.01 && Math.Abs(p.First.ClickY - p.Second.ClickY) < 0.01, p.First.Spoken));
    }

    /// <summary>A drop-down opened on a capture, drawn where an open menu's is (ACTIVE's, live): its items
    /// (DropdownButtonUI) and their texts right after the element they hang from, <paramref name="after"/>, each item
    /// <paramref name="height"/> tall from <paramref name="top"/>; what is drawn later moves down the draw order.</summary>
    private static A350DrawCapture WithDropdown(A350DrawCapture cap, int after, float x, float top, float width, float height,
        params string[] items)
    {
        int at = after + 1 < cap.Elements.Count ? cap.Elements[after + 1].TextStart : cap.Texts.Count;
        var elements = cap.Elements.Select((e, i) => i > after ? e with { TextStart = e.TextStart + items.Length } : e).ToList();
        var texts = cap.Texts.ToList();
        var white = new A350Rgba(1, 1, 1, 1);
        for (int i = 0; i < items.Length; i++)
        {
            float y = top + i * height;
            elements.Insert(after + 1 + i, new DrawnElement(0, "DropdownButtonUI", x, y, width, height, x, y, at + i));
            texts.Insert(at + i, new DrawnText(items[i], x + 6, y + height * 0.7f, x + 6, y + height * 0.7f, 0, white));
        }
        return cap with { Elements = elements, Texts = texts };
    }

    /// <summary>The draw-order index of a side's element of <paramref name="cls"/> at <paramref name="x"/>, <paramref name="y"/>.</summary>
    private static int ElementAt(A350DrawCapture cap, string cls, float x, float y) =>
        cap.Elements.Select((e, i) => (e, i)).Single(p => p.e.Class == cls && p.e.X == x && p.e.Y == y).i;

    // Review 2026-10-28: the dismissed consent dialog (EULADialog, 600 x 600 from y 212) sits in every FMS frame, and
    // only the FMS system menu is drawn after it. Its open test was its band at any x, so the menu's fifth item (y 244,
    // left of the dialog) judged it open, and the whole page in its band was hidden. Open is its own text first, inside it.
    [Fact]
    public void The_fms_system_menu_beside_the_dismissed_consent_dialog_hides_nothing()
    {
        var page = Fixture();
        var cap = WithDropdown(page, ElementAt(page, "MainHeaderDropdownMenu", 21, 6), 21, 46, 190, 42,
            "ITEM 1", "ITEM 2", "ITEM 3", "ITEM 4", "ITEM 5");
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("IDLE: +0.0, green", lines);
        Assert.Contains("NAV DATABASE: SJ6192002, green", lines);
        int menu = lines.IndexOf("FMS 1 menu, cyan, menu open");
        Assert.True(menu >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "ITEM 1 button", "ITEM 2 button", "ITEM 3 button", "ITEM 4 button", "ITEM 5 button" },
            lines.Skip(menu + 1).Take(5));
    }

    // Review 2026-10-28: an open list took as its owner any key above it in its column, so a list hanging off the INSERT
    // NEXT WPT pop-up's entry field (a DropdownTextBox) went to the POSITION key, 250 px up, which then read "menu open".
    // The owner is the control whose edge the list touches.
    [Fact]
    public void A_list_hangs_from_the_control_it_touches()
    {
        var page = Load("mfd_insert_next_wpt");
        var cap = WithDropdown(page, ElementAt(page, "BaseUITextBox", 288, 314), 285, 357, 178, 32, "BI614", "BASUR");
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.Contains("POSITION button", lines);
        int field = lines.IndexOf("INSERT NEXT WPT FROM BASUR: none, cyan, menu open");
        Assert.True(field >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "BI614 button", "BASUR button" }, lines.Skip(field + 1).Take(2));
    }

    [Fact]
    public void A_list_touching_no_control_keeps_its_place_and_names_no_owner()
    {
        var page = Load("mfd_insert_next_wpt");
        var cap = WithDropdown(page, ElementAt(page, "BaseUITextBox", 288, 314), 500, 600, 150, 32, "LOOSE 1", "LOOSE 2");
        var lines = A350MfdPage.Read(cap, 0).Select(l => l.Spoken).ToList();
        Assert.DoesNotContain(lines, l => l.EndsWith("menu open", StringComparison.Ordinal));
        Assert.Contains("LOOSE 1 button", lines);
    }

    // Review 2026-10-28: the INSERT NEXT WPT pop-up removed the F-PLN cells under it, and the rows then read them as
    // absent ("500: ... ALT none" for its 500 ft constraint); the open revision menu covers the same columns and left
    // them readable. One rule for both: a column under the overlay is left out of its row, as a sighted pilot sees it.
    [Theory]
    [InlineData("mfd_insert_next_wpt")]
    [InlineData("mfd_fpln_revision_basur")]
    public void A_column_under_a_revision_overlay_is_left_out_of_its_row(string fixture)
    {
        var rows = PageLines(fixture).Where(l => l.Kind == "waypoint").Select(l => l.Spoken).ToList();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.DoesNotContain("TIME ", row));
        Assert.All(rows, row => Assert.DoesNotContain("SPD ", row));
        Assert.All(rows, row => Assert.DoesNotContain("ALT ", row));
        Assert.Contains(rows, row => row.StartsWith("500: via C220°", StringComparison.Ordinal));
    }

    // FROM is the F-PLN's first column heading: it names the first row only while that row is the FROM waypoint (the last
    // sequenced, no leg into it). Scrolled down, the top row is any waypoint with its leg (DB416 at the ILS 12L final, live)
    // and read "FROM DB416: TRK 298°, DIST 3; ...".
    [Fact]
    public void Only_the_from_waypoint_is_called_from()
    {
        Assert.StartsWith("DB416: TRK 298°, DIST 3;", Page("mfd_fpln_ils12l_final").First(l => l.Contains("DB416", StringComparison.Ordinal)));
        Assert.StartsWith("FROM VCBI22: ", Page("mfd_fpln_basu1d").First(l => l.Contains("VCBI22", StringComparison.Ordinal)));
    }

    // ACTIVE/WIND, CLB tab, empty (live 2026-10-28): under the ALT and WIND headings (y 330) each row is a grey FL, the
    // altitude, the direction with its degree glyph, a grey "/" and the speed with its grey KT, 83 px under the headings
    // and 78 px apart. The rows read "FL, grey; none none° / none KT, green", without their headings or the units' places.
    [Fact]
    public void A_wind_table_reads_each_row_under_its_headings_with_its_units()
    {
        var lines = Page("mfd_wind_empty");
        Assert.Equal(4, lines.Count(l => l == "ALT FL none, WIND none° / none KT, green"));
        Assert.DoesNotContain(lines, l => l.StartsWith("FL, grey", StringComparison.Ordinal));
    }

    // Live 2026-10-28: C/L COMPLETE sent the MFD from the checklists back to INIT, and the window changed its list without
    // a word; a sighted pilot sees the new title bar at once (the A380's MFD window says the new title once).
    [Fact]
    public void A_page_change_names_the_new_page_once()
    {
        var checklists = PageLines("mfd_cl_menu");
        var init = PageLines("mfd_init_empty");
        Assert.Equal("CHECKLISTS", A350MfdPage.PageTitle(checklists));
        Assert.Equal("ACTIVE/INIT", A350MfdPage.PageJustChanged(checklists, init));
        Assert.Null(A350MfdPage.PageJustChanged(init, init));                       // the same page: nothing
        Assert.Null(A350MfdPage.PageJustChanged(Array.Empty<MfdLine>(), init));    // the window's first read: nothing
        Assert.Equal("ACTIVE/F-PLN TMPY", A350MfdPage.PageJustChanged(init, PageLines("mfd_fpln_tmpy_basu1d")));
    }

    // DEPARTURE's RWY key opened (live 2026-10-28): its list starts 6 px under the key and reads right after it.
    [Fact]
    public void The_runway_list_hangs_from_the_rwy_key()
    {
        var lines = Page("mfd_departure_rwy_list");
        int rwy = lines.ToList().IndexOf("RWY button, menu open");
        Assert.True(rwy >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "04 3400M ILS button", "22 3400M ILS button" }, lines.Skip(rwy + 1).Take(2));
        Assert.Contains("SID button, grey", lines);
        Assert.DoesNotContain(Page("mfd_departure_vcbi"), l => l.Contains("menu open", StringComparison.Ordinal));   // closed
    }

    // The tab a page is on is drawn black, the others dark grey (every tabbed page captured, 2026-10-27/28): a
    // sighted pilot sees which, and the read said none (PERF's T.O or APPR, VERT REV's ALT or SPD).
    [Theory]
    [InlineData("mfd_perf_to_empty", "T.O tab, selected")]
    [InlineData("mfd_perf_appr_omdb", "APPR tab, selected")]
    [InlineData("mfd_wind_empty", "HISTORY tab, selected")]
    [InlineData("mfd_wind_crz_empty", "CRZ tab, selected")]
    [InlineData("mfd_radnav_ls", "TUNED FOR DISPLAY tab, selected")]
    [InlineData("mfd_data_status_db", "ACFT STATUS tab, selected")]
    [InlineData("mfd_sec1_received_cpny", "SEC 1 tab, selected")]
    [InlineData("mfd_vertrev_alt_imped", "ALT tab, selected")]
    [InlineData("mfd_vertrev_spd_imped", "SPD tab, selected")]
    public void The_tab_a_page_is_on_says_so(string fixture, string selected)
    {
        var lines = Page(fixture);
        Assert.Equal(selected, Assert.Single(lines, l => l.EndsWith(" tab, selected", StringComparison.Ordinal)));
    }

    // SEC INDEX after the company flight plan arrived in SEC 1 (live 2026-10-28): RECEIVED CPNY F-PLN's menu open.
    [Fact]
    public void The_received_company_plan_reads_its_menu()
    {
        var lines = Page("mfd_sec1_received_cpny");
        int received = lines.ToList().IndexOf("RECEIVED CPNY F-PLN button, menu open");
        Assert.True(received >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "INSERT * button", "CLEAR * button" }, lines.Skip(received + 1).Take(2));
        Assert.Contains("SEC 1 tab, selected", lines);
        Assert.Contains("PERF button, grey", lines);
    }

    // AIRWAYS from POMAN, before an airway is typed (live 2026-10-28): the two entry fields by their labels.
    [Fact]
    public void The_airways_page_reads_its_entry_fields()
    {
        var lines = Page("mfd_airways_poman");
        Assert.Contains("ACTIVE/F-PLN/AIRWAYS TMPY, black", lines);
        Assert.Contains("VIA: none, cyan", lines);
        Assert.Contains("TO: none, cyan", lines);
    }

    // The POSITION menu open over the F-PLN (live 2026-10-28): its REPORT item (text at y 176) sits in the F-PLN's column
    // header band and was taken as the TIME header ("REPORT none") while the item itself read nothing. A menu's texts are
    // its own, and the TIME and SPD values it covers are left out of the rows under it, as the revision menu's are.
    [Fact]
    public void A_drop_down_over_the_flight_plan_keeps_its_items_and_covers_its_columns()
    {
        var all = PageLines("mfd_fpln_position_menu");
        var lines = all.Select(l => l.Spoken).ToList();
        int position = lines.IndexOf("POSITION button, menu open");
        Assert.True(position >= 0, string.Join(" | ", lines));
        Assert.Equal(new[] { "MONITOR button", "REPORT button", "NAVAIDS button", "IRS button", "GNSS button", "TIME button" },
            lines.Skip(position + 1).Take(6));
        Assert.DoesNotContain(lines, l => l.Contains("REPORT none", StringComparison.Ordinal));
        string from = all.Single(l => l.Kind == "waypoint" && l.Spoken.StartsWith("FROM VCBI22", StringComparison.Ordinal)).Spoken;
        Assert.DoesNotContain("TIME ", from);   // under the menu
        Assert.DoesNotContain("SPD ", from);
        Assert.Contains("ALT ", from);          // beside it
    }

    // NEW DEST's pop-up stands in mid-page (live 2026-10-28, y 444 to its CANCEL key at 599): the rows above and below
    // it show their columns, the rows beside it do not.
    [Fact]
    public void A_pop_up_covers_only_the_rows_beside_it()
    {
        var rows = PageLines("mfd_new_dest_popup").Where(l => l.Kind == "waypoint").Select(l => l.Spoken).ToList();
        Assert.Contains(rows, row => row.StartsWith("500: ", StringComparison.Ordinal) && row.Contains("ALT 500", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.StartsWith("BI453: ", StringComparison.Ordinal) && !row.Contains("TIME ", StringComparison.Ordinal));
        Assert.Contains(rows, row => row.StartsWith("BI614: ", StringComparison.Ordinal) && row.Contains("TIME ", StringComparison.Ordinal));
        Assert.Contains("NEW DEST FROM VCBI22: none, cyan", PageLines("mfd_new_dest_popup").Select(l => l.Spoken));
    }

    [Fact]
    public void Without_an_overlay_every_column_reads()
    {
        var rows = PageLines("mfd_fpln_tmpy_basu1d").Where(l => l.Kind == "waypoint").Select(l => l.Spoken).ToList();
        Assert.Contains(rows, row => row.StartsWith("500: ", StringComparison.Ordinal) && row.Contains("ALT 500", StringComparison.Ordinal));
    }
}
