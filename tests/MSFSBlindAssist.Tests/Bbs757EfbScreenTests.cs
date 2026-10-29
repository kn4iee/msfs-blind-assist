using System.Drawing;
using System.Text.Json;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet reader's page model (plan 11) against real captures of BlueBird's tablet (2026-10-26,
/// 0.1.0, PW, dim night brightness and 100 %): each fixture is the calibrated capture (reduced to 256
/// colours) and the reader's lines for it (one PaddleOCR read, re-recorded for plan 17 and again for plan 18's
/// brightening by tools/Bbs757EfbProbe's reread). Parse is pure, so these pin what the pilot is given for each page.
/// Fixtures are read from the source tree (tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb).
/// </summary>
public class Bbs757EfbScreenTests
{
    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures", "bbs757-efb");
    }

    internal static Bbs757EfbScreenModel Load(string set, string name)
    {
        string stem = Path.Combine(FixtureDir(), set, name);
        var lines = new List<Bbs757EfbTextLine>();
        using (var doc = JsonDocument.Parse(File.ReadAllText(stem + ".lines.json")))
            foreach (var l in doc.RootElement.EnumerateArray())
            {
                var words = l.GetProperty("words").EnumerateArray().Select(w => new Bbs757EfbWord(w.GetProperty("text").GetString() ?? "",
                    new Rectangle(w.GetProperty("x").GetInt32(), w.GetProperty("y").GetInt32(), w.GetProperty("w").GetInt32(), w.GetProperty("h").GetInt32()))).ToList();
                lines.Add(new Bbs757EfbTextLine(l.GetProperty("text").GetString() ?? "",
                    new Rectangle(l.GetProperty("x").GetInt32(), l.GetProperty("y").GetInt32(), l.GetProperty("w").GetInt32(), l.GetProperty("h").GetInt32()),
                    words, (l.GetProperty("pass").GetString() ?? "A")[0]));
            }
        using var bmp = new Bitmap(stem + ".png");
        return Bbs757EfbScreen.Parse(lines, Bbs757EfbPixels.FromBitmap(bmp));
    }

    private static Bbs757EfbItem Item(Bbs757EfbScreenModel m, string text) =>
        m.Items.Concat(m.Menu).FirstOrDefault(i => i.Text == text)
        ?? throw new Xunit.Sdk.XunitException($"No item '{text}' on {m.PageName}: " + string.Join(" | ", m.Items.Select(i => i.Text)));

    [Fact]
    public void Home_lists_the_five_apps_tapped_on_their_icons()
    {
        var m = Load("dim", "01-home");
        Assert.Equal("HOME", m.App);
        foreach (var app in new[] { "Aircraft", "Navigation", "Systems", "Checklist", "Settings" })
        {
            var it = Item(m, app);
            Assert.Equal(Bbs757EfbKind.Menu, it.Kind);
            // The label is not clickable on BlueBird's home screen (measured): the icon ~70 px above is.
            Assert.True(it.Hit.Y < it.Box.Top - 50);
        }
    }

    [Fact]
    public void Home_lists_exactly_the_five_apps()
    {
        // An app icon read as text is no menu item (PaddleOCR reads the Systems icon as two CJK characters).
        var m = Load("dim", "01-home");
        Assert.Equal(new[] { "Aircraft", "Navigation", "Systems", "Checklist", "Settings" },
            m.Menu.Concat(m.Items).Where(i => i.Kind == Bbs757EfbKind.Menu).Select(i => i.Text));
    }

    [Fact]
    public void A_side_menu_lists_its_entries_and_not_their_icons()
    {
        // PaddleOCR reads the COCKPIT PRESETS icon, left of its label, as "tlt" on this page (the 2026-10-27
        // benchmark's stray menu item).
        var m = Load("bright", "11-aircraft-fuel");
        Assert.Equal(new[] { "Cockpit presets", "Fuel", "Weight & balance", "Ground services", "Cabin & doors", "Maintenance", "Failures" },
            m.Menu.Select(i => i.Text));
    }

    [Fact]
    public void Ground_services_tiles_carry_their_state_dim_and_bright()
    {
        foreach (var set in new[] { "dim", "bright" })
        {
            var m = Load(set, "13-aircraft-ground-services");
            Assert.Equal("Aircraft, Ground services", m.PageName);
            Assert.Contains(m.Menu, x => x.Selected && x.Text == "Ground services");
            Assert.Equal(Bbs757EfbTint.Red, Item(m, "Fuel truck off").Tint);
            Assert.Equal(Bbs757EfbTint.Red, Item(m, "Jetway removed").Tint);
            Assert.Equal(Bbs757EfbTint.Green, Item(m, "Chocks, on").Tint);
            Assert.Equal(Bbs757EfbTint.Grey, Item(m, "Ground A/C").Tint);
            Assert.Equal("GND SRVC Mode: BBS", Item(m, "GND SRVC Mode: BBS").Text);
            // Positions are the tablet's own (calibrated): the CHOCKS tile is at (410, 535).
            var chocks = Item(m, "Chocks, on");
            Assert.InRange(chocks.Hit.X, 395, 425);
            Assert.InRange(chocks.Hit.Y, 520, 550);
        }
    }

    [Fact]
    public void Presets_are_buttons_and_the_note_comes_from_the_template()
    {
        var m = Load("bright", "10-aircraft-presets");
        foreach (var b in new[] { "Cold & dark", "Turnaround", "Before start", "Ready for taxi", "Ready for takeoff",
                                  "After takeoff", "Cruise", "Approach", "Final approach", "APU quick start", "IRS rapid align" })
            Assert.Equal(Bbs757EfbKind.Button, Item(m, b).Kind);
        Assert.Equal(Bbs757EfbKind.Text, Item(m, "Choose SAVE or LOAD, then select a panel state.").Kind);
        Assert.Equal(Bbs757EfbTint.Grey, Item(m, "External power").Tint);
    }

    [Fact]
    public void Doors_are_named_from_the_layout_and_cabin_lights_are_sliders()
    {
        var m = Load("bright", "14-aircraft-cabin-doors");
        foreach (var d in new[] { "Left FWD: Closed", "Right FWD: Closed", "FWD cargo: Closed", "Left mid: Closed",
                                  "Right mid: Closed", "AFT cargo: Closed", "Left AFT: Closed", "Right AFT: Closed" })
            Assert.Equal(Bbs757EfbKind.Button, Item(m, d).Kind);
        foreach (var s in new[] { "Ceiling", "Window", "Entry" })
        {
            var it = Item(m, s);
            Assert.Equal(Bbs757EfbKind.Slider, it.Kind);
            Assert.Equal("Off", it.Value);
        }
    }

    [Fact]
    public void Fuel_values_are_labelled_by_the_template_with_units()
    {
        var m = Load("bright", "11-aircraft-fuel");
        Assert.Equal("45%", Item(m, "Left tank").Value);
        Assert.Equal("14%", Item(m, "Center tank").Value);
        Assert.Equal("1000 gal", Item(m, "Left tank volume").Value);
        Assert.Equal("6700 lbs", Item(m, "Right tank weight").Value);
        Assert.Equal("20100 lbs", Item(m, "Total fuel weight").Value);
    }

    [Fact]
    public void The_fuel_page_offers_import_from_simbrief_beside_its_figures()
    {
        // Plan 18: the template used to ignore the whole content area, which dropped IMPORT FROM SIMBRIEF.
        var m = Load("bright", "20-fuel-start-boarding");

        var import = Item(m, "Import from SIMBRIEF");
        Assert.Equal(Bbs757EfbKind.Button, import.Kind);
        Assert.True(import.Clickable);
        Assert.InRange(import.Hit.X, 502, 532);
        Assert.InRange(import.Hit.Y, 936, 966);
        Assert.Equal("45%", Item(m, "Left tank").Value);
        Assert.Equal("1000 gal", Item(m, "Left tank volume").Value);
        Assert.Equal("20100 lbs", Item(m, "Total fuel weight").Value);
        // The diagram's labels and the panel's own (LEFT, CENTER, RIGHT, TOTAL FUEL) stay out: the note is the only text.
        Assert.Equal(new[] { "Tank figures as the tablet shows them." },
            m.Items.Where(i => i.Kind is Bbs757EfbKind.Text or Bbs757EfbKind.Heading).Select(i => i.Text));
    }

    [Fact]
    public void The_fuel_page_keeps_the_box_BlueBird_shows_over_it()
    {
        var m = Load("bright", "22-fuel-adjustment-complete");

        Assert.Equal("Aircraft, Fuel", m.PageName);
        var q = Assert.IsType<Bbs757EfbQuestion>(m.Question);
        Assert.Equal("Fuel adjustment is complete!", q.Text);
        var ok = Assert.Single(q.Buttons);
        Assert.Equal("OK", ok.Text);
        Assert.InRange(ok.Hit.X, 808, 838);
        Assert.InRange(ok.Hit.Y, 647, 677);
        Assert.Equal("53%", Item(m, "Left tank").Value);
        Assert.Equal("15543 lbs", Item(m, "Total fuel weight").Value);
        // IMPORT FROM SIMBRIEF is gone from the tablet, and the box's OK is the question's.
        Assert.DoesNotContain(m.Items, i => i.Kind == Bbs757EfbKind.Button);
    }

    [Fact]
    public void Failure_buttons_say_which_failure_they_belong_to()
    {
        var m = Load("bright", "16-aircraft-failures");
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Fail now, L engine fire").Kind);
        // The button says SET UP (Windows OCR read "SET w", snapped to SET).
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Set UP, L engine fire").Kind);
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Fail now, R engine fire").Kind);
        Assert.DoesNotContain(m.Items, i => i.Text.StartsWith("Actions", StringComparison.Ordinal));
    }

    [Fact]
    public void Failure_counts_stay_figures_and_the_page_title_names_no_tab()
    {
        var m = Load("bright", "16-aircraft-failures");
        // PaddleOCR reads ACTIVE 0 ARMED 0 as one line: its figures make it no tab label (ACTIVE / ARMED).
        Assert.Contains(m.Items, i => i.Kind == Bbs757EfbKind.Text
            && i.Text.Contains("active 0", StringComparison.OrdinalIgnoreCase) && i.Text.Contains("armed 0", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(m.Items, i => i.Kind == Bbs757EfbKind.Heading && i.Text == "Active / armed");
        // FAILURES, the page's title, sits over CATALOG with FIRES under it: a column's heading, not the tab's name.
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Catalog").Kind);
    }

    [Fact]
    public void Failure_switches_keep_their_state()
    {
        // PaddleOCR reads each switch whole, RANDOM FAILURES: OFF: snapped whole to its label, the state was lost.
        var m = Load("bright", "16-aircraft-failures");
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Random failures: off").Kind);
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Notifications: off").Kind);
    }

    [Fact]
    public void Checklist_lines_are_boxes_with_their_values()
    {
        var m = Load("bright", "40-checklist");
        Assert.Equal("Checklist, Before start", m.PageName);
        var pb = Item(m, "Parking brake: SET");
        Assert.Equal(Bbs757EfbKind.Checkbox, pb.Kind);
        Assert.False(pb.On);
        Assert.Equal(Bbs757EfbKind.Checkbox, Item(m, "Oxygen: CHECKED").Kind);
        Assert.Equal(Bbs757EfbKind.Checkbox, Item(m, "Beacon: ON").Kind);
        Assert.Equal(Bbs757EfbKind.Checkbox, Item(m, "Before start, whole checklist").Kind);
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Reset").Kind);
    }

    [Theory]
    [InlineData("40-checklist", false)]
    [InlineData("41-checklist-header-ticked", true)]
    [InlineData("42-checklist-header-unticked", false)]
    public void The_checklist_header_box_is_judged_inside_its_own_border(string name, bool ticked)
    {
        // The header's words sit higher than its box: judged round the words' row, the box's bright top border read as
        // a tick (42, and live after unticking).
        var m = Load("bright", name);

        var header = Item(m, "Before start, whole checklist");
        Assert.Equal(Bbs757EfbKind.Checkbox, header.Kind);
        Assert.Equal(ticked, header.On);
        // Every line follows its own box: all ticked with the header on 41, none on 40 and 42.
        var lines = m.Items.Where(i => i.Kind == Bbs757EfbKind.Checkbox).ToList();
        Assert.Equal(14, lines.Count);   // the header and the 13 lines of BEFORE START
        Assert.All(lines, i => Assert.Equal(ticked, i.On));
    }

    [Fact]
    public void Settings_rows_are_switches_and_sliders_with_their_values()
    {
        var m = Load("bright", "51-settings-aircraft");
        var steering = Item(m, "Nosewheel steering");
        Assert.Equal(Bbs757EfbKind.Toggle, steering.Kind);
        Assert.True(steering.On);
        Assert.Equal("Realistic", steering.Value);
        Assert.Equal(Bbs757EfbKind.Toggle, Item(m, "Sync barometers").Kind);
        Assert.False(Item(m, "Sync barometers").On);
        var irs = Item(m, "IRS alignment time");
        Assert.Equal(Bbs757EfbKind.Slider, irs.Kind);
        Assert.Equal("FULL (7 MIN+)", irs.Value);
        var boarding = Item(m, "Boarding & load rate");
        Assert.Equal(Bbs757EfbKind.Slider, boarding.Kind);
        Assert.Equal("REALISTIC", boarding.Value);
    }

    [Fact]
    public void Callout_switches_are_found_where_that_page_draws_them()
    {
        // Callouts draws its switches at x 1220-1275, not where the other settings pages do.
        var m = Load("bright", "53-settings-callouts");
        var t = Item(m, "1000");
        Assert.Equal(Bbs757EfbKind.Toggle, t.Kind);
        Assert.True(t.On);
        Assert.InRange(t.Hit.X, 1225, 1270);
        Assert.False(Item(m, "Radio altimeter (2500)").On);
    }

    [Fact]
    public void Dim_settings_snap_the_menu_and_read_the_switch_values()
    {
        var m = Load("dim", "50-settings-general");
        Assert.Equal("Settings, General", m.PageName);
        Assert.Equal("On", Item(m, "Lock keyboard to EFB").Value);
        Assert.Equal("10 SEC", Item(m, "Notifications fade-out timer").Value);
    }

    [Fact]
    public void The_faint_gear_doors_are_offered_when_dim()
    {
        // At 0 percent brightness the gear doors' OPEN is pink on pink: the small model missed it on the raw picture,
        // so the nose and right doors had no button. Read brightened (plan 18), all three have one, with its state.
        var m = Load("dim", "15-aircraft-maintenance");
        foreach (var (door, x) in new[] { ("Nose door", 844), ("Left door", 985), ("Right door", 1124) })
        {
            // What trails the state is the percentage under the button, small text that wobbles ("0%" read as "05").
            var button = Assert.Single(m.Items, i => i.Text.StartsWith(door + ": Open", StringComparison.Ordinal));
            Assert.Equal(Bbs757EfbKind.Button, button.Kind);
            Assert.True(button.Clickable);
            Assert.InRange(button.Hit.X, x - 15, x + 15);
            Assert.InRange(button.Hit.Y, 645, 675);
        }
    }

    [Fact]
    public void A_page_that_scrolls_is_known()
    {
        Assert.True(Load("bright", "53-settings-callouts").Scrollable);
        Assert.False(Load("bright", "51-settings-aircraft").Scrollable);
    }

    [Fact]
    public void Nothing_in_the_update_guard_is_tappable()
    {
        foreach (var (set, name) in new[] { ("bright", "53-settings-callouts"), ("bright", "51-settings-aircraft"), ("bright", "13-aircraft-ground-services") })
        {
            var m = Load(set, name);
            Assert.DoesNotContain(m.Items, i => i.Clickable && Bbs757EfbScreen.UpdateGuard.Contains(i.Hit));
        }
    }

    // ---------------------------------------------------------------- pure helpers

    [Theory]
    [InlineData("E) FUEL", "FUEL")]
    [InlineData("e CABIN & DOORS", "CABIN & DOORS")]
    [InlineData("IO SEC", "10 SEC")]
    [InlineData("to SEC", "10 SEC")]
    [InlineData("RIOHT an START", "RIGHT AIR START")]
    [InlineData("REQ PB", "REQ PB TUG")]
    [InlineData("SET w", "SET")]
    [InlineData("Parking brake", "Parking brake")]   // mixed case never snaps to a capitals label
    [InlineData("Windows", "Windows")]
    [InlineData("CJGSW2VZ", "CJGSW2VZ")]             // a code stays as read
    [InlineData("ACTIVE 0 ARMED 0", "ACTIVE 0 ARMED 0")]   // a figure is never snapped away (ACTIVE / ARMED)
    [InlineData("RANDOM FAILURE: OFF", "RANDOM FAILURES: OFF")]   // a label snaps, the state after it stays
    [InlineData("REMOVE FP", "REMOVE FP")]           // the ACARS page's button: three edits from REMOVE ALL, which it must not become
    [InlineData("REMOVE ALL", "REMOVE ALL")]
    public void Snap_fixes_ocr_habits_without_guessing(string read, string expected) =>
        Assert.Equal(expected, Bbs757EfbVocabulary.Snap(read));

    [Theory]
    [InlineData("COCKPIT PRESETS", "Cockpit presets")]
    [InlineData("GROUND A/C", "Ground A/C")]
    [InlineData("APU QUICK START", "APU quick start")]
    [InlineData("CJGSW2VZ", "CJGSW2VZ")]
    [InlineData("REMOVE FP", "Remove FP")]
    public void Pretty_keeps_acronyms_and_codes(string s, string expected) =>
        Assert.Equal(expected, Bbs757EfbVocabulary.Pretty(s));

    [Fact]
    public void The_acars_page_reads_its_remove_fp_button_as_remove_fp()
    {
        // Navigation, ACARS dashboard (bright/23, under a question): REFRESH SIMBRIEF OFP and REMOVE FP. REMOVE FP snapped to REMOVE ALL,
        // so a pilot pressing it was told it removes everything.
        var page = Load("bright", "23-units-question");
        var texts = page.Items.Select(i => i.Text).ToList();

        Assert.Contains("Remove FP", texts);
        Assert.DoesNotContain("Remove all", texts);
    }

    [Fact]
    public void The_ehsi_up_reference_words_are_hdg_and_trk()
    {
        // The catalog's words and the tablet's, measured; "TRACK UP" and "HEADING UP" were guesses.
        Assert.True(Bbs757EfbVocabulary.IsKnown("HDG"));
        Assert.True(Bbs757EfbVocabulary.IsKnown("TRK"));
        Assert.False(Bbs757EfbVocabulary.IsKnown("TRACK UP"));
        Assert.False(Bbs757EfbVocabulary.IsKnown("HEADING UP"));

        var ehsi = Bbs757EfbSettings.ByPath("options.ehsi.up_reference");
        Assert.NotNull(ehsi);
        Assert.All(Bbs757EfbSettingsMatch.ChoicesFor(ehsi!)!, word => Assert.True(Bbs757EfbVocabulary.IsKnown(word), word));
    }

    [Theory]
    [InlineData(74, 0, 0, Bbs757EfbTint.Red)]
    [InlineData(235, 0, 0, Bbs757EfbTint.Red)]
    [InlineData(91, 223, 61, Bbs757EfbTint.Green)]
    [InlineData(136, 136, 136, Bbs757EfbTint.Grey)]
    [InlineData(44, 86, 124, Bbs757EfbTint.Blue)]
    public void Tints_hold_at_any_brightness(int r, int g, int b, Bbs757EfbTint tint) =>
        Assert.Equal(tint, Bbs757EfbScreen.TintOf((r, g, b)));

    [Fact]
    public void The_capture_maps_any_window_shape_to_the_tablet()
    {
        // Parked at the ND's aspect: no bars, the tablet is the top 1048/1397 of the width.
        var parked = Bbs757EfbPopout.ParkedClientSize;
        var r = Bbs757EfbPopout.TabletSourceRect(parked.Width, parked.Height);
        Assert.Equal(new Rectangle(0, 0, 1397, 1048), r);
        // A wide window letterboxes the ND between bars (measured 1350 x 1138): the tablet starts after the bar.
        var wide = Bbs757EfbPopout.TabletSourceRect(1334, 1099);
        Assert.Equal(0, wide.Y);
        Assert.InRange(wide.X, (1334 - wide.Width) / 2 - 1, (1334 - wide.Width) / 2 + 1);
        Assert.InRange(wide.Width, (int)(1099 * 704.0 / 859) - 1, (int)(1099 * 704.0 / 859) + 1);
    }

    [Fact]
    public void FromEngine_carries_each_line_with_its_box_and_words_as_pass_P()
    {
        var engine = new[]
        {
            new OcrLine("TOD PAUSE", new Rectangle(300, 400, 120, 20), new[]
            {
                new OcrWord("TOD", new Rectangle(300, 401, 40, 18), 0.98f),
                new OcrWord("PAUSE", new Rectangle(350, 400, 70, 20), 0.95f),
            }, 0.96f),
        };

        var line = Assert.Single(Bbs757EfbOcr.FromEngine(engine));

        Assert.Equal("TOD PAUSE", line.Text);
        Assert.Equal(new Rectangle(300, 400, 120, 20), line.Box);
        Assert.Equal('P', line.Pass);
        Assert.Equal(new[] { "TOD", "PAUSE" }, line.Words.Select(w => w.Text));
        Assert.Equal(new[] { new Rectangle(300, 401, 40, 18), new Rectangle(350, 400, 70, 20) }, line.Words.Select(w => w.Box));
    }

    [Fact]
    public void FromEngine_reads_two_lines_on_one_row_left_to_right()
    {
        static OcrLine L(string text, int x, int y) =>
            new(text, new Rectangle(x, y, 60, 20), new[] { new OcrWord(text, new Rectangle(x, y, 60, 20), 1f) }, 1f);

        // The engine's own order: the next row first, then the right-hand line, a few pixels higher than its left
        // neighbor (boxes wobble between reads), so neither its order nor a sort by top is the reading order.
        var lines = Bbs757EfbOcr.FromEngine(new[] { L("BELOW", 300, 200), L("RIGHT", 900, 100), L("LEFT", 300, 103) });

        Assert.Equal(new[] { "LEFT", "RIGHT", "BELOW" }, lines.Select(l => l.Text));
    }
}
