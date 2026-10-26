using System.Drawing;
using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet reader's page model (plan 11) against real captures of BlueBird's tablet (2026-10-26,
/// 0.1.0, PW, dim night brightness and 100 %): each fixture is the calibrated capture (reduced to 256
/// colours) and the reader's lines for it (OCR plus its closer re-reads, saved by
/// tools/Bbs757EfbProbe). Parse is pure, so these pin what the pilot is given for each page.
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
    public void Failure_buttons_say_which_failure_they_belong_to()
    {
        var m = Load("bright", "16-aircraft-failures");
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Fail now, L engine fire").Kind);
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Set, L engine fire").Kind);
        Assert.Equal(Bbs757EfbKind.Button, Item(m, "Fail now, R engine fire").Kind);
        Assert.DoesNotContain(m.Items, i => i.Text.StartsWith("Actions", StringComparison.Ordinal));
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
    public void Snap_fixes_ocr_habits_without_guessing(string read, string expected) =>
        Assert.Equal(expected, Bbs757EfbVocabulary.Snap(read));

    [Theory]
    [InlineData("COCKPIT PRESETS", "Cockpit presets")]
    [InlineData("GROUND A/C", "Ground A/C")]
    [InlineData("APU QUICK START", "APU quick start")]
    [InlineData("CJGSW2VZ", "CJGSW2VZ")]
    public void Pretty_keeps_acronyms_and_codes(string s, string expected) =>
        Assert.Equal(expected, Bbs757EfbVocabulary.Pretty(s));

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
    public void Ocr_merge_keeps_pass_a_and_adds_only_new_lines_from_b()
    {
        static Bbs757EfbTextLine L(string t, int x, int y, char p) => new(t, new Rectangle(x, y, 50, 14), Array.Empty<Bbs757EfbWord>(), p);
        var a = new[] { L("ON", 1280, 100, 'A') };
        var b = new[] { L("0N", 1282, 101, 'B'), L("OFF", 1280, 350, 'B') };
        var m = Bbs757EfbOcr.Merge(a, b);
        Assert.Equal(new[] { "ON", "OFF" }, m.Select(l => l.Text));
    }
}
