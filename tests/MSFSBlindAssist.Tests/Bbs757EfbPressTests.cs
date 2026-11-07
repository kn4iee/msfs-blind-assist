using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 18, Task 5: the press pipeline's pure decisions (<see cref="Bbs757EfbPress"/>): finding a pressed control again in a fresh
/// read of the tablet, whether a switch needs a tap, where to tap a slider, and what is said after a press. Models are built in
/// memory with the keys and boxes the reader gives (dumped from the 2026-10-29 captures); a few tests read the captures themselves.
/// Everything spoken is checked under en-US, de-DE and sv-SE (CI runs under en-US).
/// </summary>
public class Bbs757EfbPressTests
{
    private const string Ellipsis = "…";

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    // ---- models --------------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Item(Bbs757EfbKind kind, string text, string key, Rectangle box, bool? on = null, string value = "", bool selected = false) =>
        new(kind, text, box, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), true, key, On: on, Selected: selected, Value: value);

    private static Bbs757EfbItem Tile(string text, string key, Rectangle box) => Item(Bbs757EfbKind.Button, text, key, box);

    private static Bbs757EfbScreenModel PageOf(string app, string section, params Bbs757EfbItem[] items) =>
        new(app, section, Array.Empty<Bbs757EfbItem>(), items);

    private static Bbs757EfbScreenModel Ground(params Bbs757EfbItem[] items) => PageOf("AIRCRAFT", "GROUND SERVICES", items);

    // The Ground services tiles as the reader gives them on the 2026-10-29 captures (bright/18, in MSFS mode).
    private static Bbs757EfbItem ReqFuelTruck() => Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42));
    private static Bbs757EfbItem ReqCatering() => Tile("Req catering", "btn:|REQ CATERING@45,36", new Rectangle(912, 723, 196, 42));
    private static Bbs757EfbItem Requesting() =>
        Tile("Requesting" + Ellipsis, "btn:|REQUESTING" + Ellipsis + "@56,28", new Rectangle(1131, 576, 197, 43));

    private static Bbs757EfbItem StartBoarding() =>
        Item(Bbs757EfbKind.Button, "Start boarding", Bbs757EfbTopBar.BoardingKey, new Rectangle(1142, 3, 180, 28));

    private static Bbs757EfbItem Boarding() =>
        new(Bbs757EfbKind.Text, "Boarding" + Ellipsis + " 10 min", new Rectangle(1142, 3, 180, 28), new Point(1232, 17), false, Bbs757EfbTopBar.BoardingKey);

    private static Bbs757EfbItem Notifications() =>
        Item(Bbs757EfbKind.Button, "Notifications", Bbs757EfbTopBar.NotificationsKey, new Rectangle(1364, 6, 19, 19));

    private static readonly Bbs757EfbNotice GpuConnected = new(1, "GPU connected.", "14:17Z");

    private static Bbs757EfbItem ItemNamed(Bbs757EfbScreenModel m, string text) =>
        m.Menu.Concat(m.Items).Concat(m.TopBar).FirstOrDefault(i => i.Text == text)
        ?? throw new Xunit.Sdk.XunitException($"No control '{text}' on {m.PageName}: " + string.Join(" | ", m.Items.Select(i => i.Text)));

    // ---- TapPoint and DragFor ------------------------------------------------------------------------------------------

    private static Bbs757EfbScreenModel InViewport(params Bbs757EfbItem[] items) =>
        PageOf("SETTINGS", "AIRCRAFT", items) with { Viewport = new Rectangle(250, 82, 1147, 966), Scrollable = true };

    [Fact]
    public void TapPoint_is_the_middle_of_the_shown_part_and_null_under_six_pixels()
    {
        var whole = Item(Bbs757EfbKind.Button, "Whole", "btn:W", new Rectangle(300, 200, 400, 60));
        var last = Item(Bbs757EfbKind.Button, "Last", "btn:L", new Rectangle(273, 1027, 600, 60));      // shows 1027-1048: 21 px
        var strip = Item(Bbs757EfbKind.Button, "Strip", "btn:S", new Rectangle(273, 1040, 600, 60));    // 8 px
        var edge = Item(Bbs757EfbKind.Button, "Edge", "btn:E", new Rectangle(273, 1043, 600, 60));      // 5 px
        var below = Item(Bbs757EfbKind.Button, "Below", "btn:B", new Rectangle(273, 1202, 600, 60));
        var cut = Item(Bbs757EfbKind.Button, "Cut", "btn:C", new Rectangle(273, 1000, 600, 60));        // 48 px, its middle (1030) shows
        var m = InViewport(whole, last, strip, edge, below, cut);

        Assert.Equal(whole.Hit, Bbs757EfbPress.TapPoint(m, whole));
        Assert.Equal(new Point(573, 1037), Bbs757EfbPress.TapPoint(m, last));
        Assert.Equal(new Point(573, 1044), Bbs757EfbPress.TapPoint(m, strip));
        Assert.Null(Bbs757EfbPress.TapPoint(m, edge));
        Assert.Null(Bbs757EfbPress.TapPoint(m, below));
        Assert.Equal(new Point(573, 1024), Bbs757EfbPress.TapPoint(m, cut));   // the middle of the strip, not the box's middle near its edge
    }

    [Fact]
    public void TapPoint_clips_a_control_to_its_own_scrollbox_not_to_the_pages()
    {
        // A control in an inner scrollbox (Weather's report box at 292-1022) is clipped to it; one outside it to the content area.
        var inner = new Rectangle(344, 292, 960, 730);
        var tab = Item(Bbs757EfbKind.Tab, "Metar", "tab:METAR", new Rectangle(324, 250, 250, 34)) with { Clip = Bbs757EfbScreen.ContentArea };
        var inside = Item(Bbs757EfbKind.Button, "Inside", "btn:I", new Rectangle(400, 270, 100, 40)) with { Clip = inner };   // 18 px show
        var hidden = Item(Bbs757EfbKind.Button, "Hidden", "btn:H", new Rectangle(400, 250, 100, 40)) with { Clip = inner };   // none shows
        var m = PageOf("NAVIGATION", "WEATHER & NOTAM", tab, inside, hidden);

        Assert.Equal(tab.Hit, Bbs757EfbPress.TapPoint(m, tab));
        Assert.Equal(new Point(450, 301), Bbs757EfbPress.TapPoint(m, inside));
        Assert.Null(Bbs757EfbPress.TapPoint(m, hidden));
        Assert.Null(Bbs757EfbPress.DragFor(m, hidden));   // the page does not scroll
    }

    [Fact]
    public void DragFor_drags_only_a_control_of_the_scrollbox_that_scrolls()
    {
        var other = new Rectangle(324, 820, 1000, 120);
        var row = Item(Bbs757EfbKind.Button, "Row", "btn:R", new Rectangle(273, 1202, 600, 60));                         // the viewport's
        var boxed = Item(Bbs757EfbKind.Button, "Boxed", "btn:X", new Rectangle(400, 960, 100, 40)) with { Clip = other }; // another box's
        var m = InViewport(row, boxed);

        Assert.NotNull(Bbs757EfbPress.DragFor(m, row));
        Assert.Null(Bbs757EfbPress.TapPoint(m, boxed));
        Assert.Null(Bbs757EfbPress.DragFor(m, boxed));
    }

    [Fact]
    public void TapPoint_leaves_the_side_menu_and_the_top_bar_where_the_reader_found_them()
    {
        var menu = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var m = new Bbs757EfbScreenModel("AIRCRAFT", "FUEL", new[] { menu }, Array.Empty<Bbs757EfbItem>());

        Assert.Equal(menu.Hit, Bbs757EfbPress.TapPoint(m, menu));
        Assert.Null(Bbs757EfbPress.DragFor(m, menu));
    }

    [Fact]
    public void DragFor_moves_the_drawn_middle_to_the_viewports_middle_in_one_drag_of_at_most_height_less_200()
    {
        var below = Item(Bbs757EfbKind.Button, "Below", "btn:B", new Rectangle(273, 1202, 600, 60));   // middle 1232; viewport middle 565
        var above = Item(Bbs757EfbKind.Button, "Above", "btn:A", new Rectangle(273, -40, 600, 60));    // middle -10
        var near = Item(Bbs757EfbKind.Button, "Near", "btn:N", new Rectangle(273, 1000, 600, 60));     // shows 48 px: in view
        var barely = Item(Bbs757EfbKind.Button, "Barely", "btn:R", new Rectangle(273, 1043, 600, 60)); // shows 5 px: 508 to go
        var far = Item(Bbs757EfbKind.Button, "Far", "btn:F", new Rectangle(273, 3000, 600, 60));       // 2465 to go: capped
        var m = InViewport(below, above, near, barely, far);

        var down = Bbs757EfbPress.DragFor(m, below)!.Value;
        Assert.Equal(500, down.X);
        Assert.Equal(667, down.Y0 - down.Y1);              // 1232 - 565, under the 766 cap: y0 below y1, the content moves up
        var up = Bbs757EfbPress.DragFor(m, above)!.Value;
        Assert.Equal(-575, up.Y0 - up.Y1);                 // the other way: y0 above y1
        Assert.Null(Bbs757EfbPress.DragFor(m, near));
        var small = Bbs757EfbPress.DragFor(m, barely)!.Value;
        Assert.Equal(508, small.Y0 - small.Y1);            // 1073 - 565
        var capped = Bbs757EfbPress.DragFor(m, far)!.Value;
        Assert.Equal(966 - 200, capped.Y0 - capped.Y1);
        foreach (var d in new[] { down, up, small, capped })
        {
            Assert.InRange(d.Y0, 82, 1048);
            Assert.InRange(d.Y1, 82, 1048);
        }
    }

    // ---- an open drop-down's list (plan 21, Task 7) -------------------------------------------------------------------

    /// <summary>The runway list as captured open (25c, 2026-11-07): its scrollbox at 519,189 150x228, six rows 38 px apart from 189.</summary>
    private static readonly Rectangle RunwayList = new(519, 189, 150, 228);

    private static Rectangle RunwayRow(int index, int scroll = 0) => new(519, 189 + 38 * index - scroll, 150, 38);

    [Fact]
    public void An_open_lists_row_is_tapped_at_its_middle_and_a_strip_of_it_only_from_six_pixels()
    {
        Assert.True(Bbs757EfbPress.RowShowsWhole(RunwayList, RunwayRow(5)));                     // 379-417: the list's bottom is 417
        Assert.Equal(new Point(594, 398), Bbs757EfbPress.RowTapPoint(RunwayList, RunwayRow(5)));
        Assert.Equal(new Point(594, 208), Bbs757EfbPress.RowTapPoint(RunwayList, RunwayRow(0)));

        var view = RunwayList with { Height = 152 };                                              // four rows show: 189-341
        Assert.False(Bbs757EfbPress.RowShowsWhole(view, RunwayRow(4)));                           // 341-379: none of it shows
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, RunwayRow(4)));
        Assert.False(Bbs757EfbPress.RowShowsWhole(view, RunwayRow(4, scroll: 20)));               // 321-359: 20 px show
        Assert.Equal(new Point(594, 331), Bbs757EfbPress.RowTapPoint(view, RunwayRow(4, scroll: 20)));
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, RunwayRow(4, scroll: 5)));                   // 336-374: 5 px show
        Assert.Equal(new Point(594, 192), Bbs757EfbPress.RowTapPoint(view, RunwayRow(0, scroll: 32)));   // 157-195: 6 px show
    }

    [Fact]
    public void The_surface_lists_rows_are_tapped_in_the_strip_that_shows_and_none_past_its_box()
    {
        // Measured live 2026-11-07 (the coordinator's "Task 7 live"): the Surface list box [977.9,357.2 150x180] shows four and a half of
        // its six 130 px rows. Snow's 28 px strip (509.2-537.2) took a tap and chose Snow; a tap on Wet's row below the box closed the list
        // without choosing (rows past the box are clipped). An open list scrolls by no input, so no row is ever dragged into view.
        var view = new Rectangle(977, 357, 150, 180);   // as the reader boxes it

        Assert.Equal(new Point(1042, 376), Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 357, 130, 38)));   // Dry, whole
        Assert.Equal(new Point(1042, 523), Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 509, 130, 38)));   // Snow, 509-537 shows
        Assert.Null(Bbs757EfbPress.RowTapPoint(view, new Rectangle(977, 547, 130, 38)));                          // Wet, none shows
    }

    [Fact]
    public void An_open_list_is_read_from_the_capture_and_its_rows_are_its_entries()
    {
        var open = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25c-navigation-perf-calc-runway-open"))!;
        var runway = Assert.Single(open.Items, i => i.Name == "planning_perf_calc_runway_dropdown");

        Assert.True(runway.Open);
        Assert.Equal(RunwayList, Bbs757EfbPress.ListViewOf(runway));
        Assert.Equal(runway.Entries, runway.List!.Rows.Select(r => r.Text));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => RunwayRow(i)), runway.List.Rows.Select(r => r.Box));
        Assert.Equal(RunwayRow(5), Bbs757EfbPress.RowOf(runway, "RW26")!.Box);
        Assert.Null(Bbs757EfbPress.RowOf(runway, "RW99"));

        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"))!;
        var shut = Assert.Single(closed.Items, i => i.Name == "planning_perf_calc_runway_dropdown");
        Assert.False(shut.Open);
        Assert.Null(shut.List);
        Assert.Null(Bbs757EfbPress.ListViewOf(shut));
        Assert.Null(Bbs757EfbPress.RowOf(shut, "RW26"));
    }

    [Fact]
    public void A_control_under_another_drop_downs_open_list_is_covered()
    {
        // 25c: the runway list (519-669, 189-417) covers the Flap Setting drop-down's face (519,240 150x34); the Thrust Setting drop-down
        // beside it (748,240) and the runway drop-down's own face (above its list) are not covered.
        var open = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25c-navigation-perf-calc-runway-open"))!;
        var runway = open.Items.Single(i => i.Name == "planning_perf_calc_runway_dropdown");
        var flap = open.Items.Single(i => i.Name == "planning_perf_calc_flap_dropdown");
        var thrust = open.Items.Single(i => i.Name == "planning_perf_calc_thrust_dropdown");

        Assert.Same(runway, Bbs757EfbPress.CoveringList(open, flap, Bbs757EfbPress.TapPoint(open, flap)!.Value));
        Assert.Null(Bbs757EfbPress.CoveringList(open, thrust, Bbs757EfbPress.TapPoint(open, thrust)!.Value));
        Assert.Null(Bbs757EfbPress.CoveringList(open, runway, Bbs757EfbPress.TapPoint(open, runway)!.Value));

        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("25b-navigation-perf-calc-calculated"))!;
        var shutFlap = closed.Items.Single(i => i.Name == "planning_perf_calc_flap_dropdown");
        Assert.Null(Bbs757EfbPress.CoveringList(closed, shutFlap, Bbs757EfbPress.TapPoint(closed, shutFlap)!.Value));
    }

    [Fact]
    public void The_systems_page_menu_in_the_title_row_is_tapped_where_it_is_drawn()
    {
        // Systems keeps its page menu in the app's title row (1097,32 300x50), above the content area (82): a control in no scrollbox
        // is clipped to the page area, so it is tapped at its own point, not refused as out of view.
        var systems = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("30-systems"))!;
        var menu = systems.Items.Single(i => i.Name == "pb_menu1");

        Assert.Equal(Bbs757EfbScreen.PageArea, menu.Clip);
        Assert.Equal(new Point(1247, 57), Bbs757EfbPress.TapPoint(systems, menu));
    }

    [Theory]
    [InlineData("51-settings-aircraft", true)]
    [InlineData("30-systems", false)]
    [InlineData("24-navigation-map", false)]
    public void Only_a_page_with_a_content_scrollbox_reads_scrollable(string fixture, bool scrollable)
    {
        Assert.Equal(scrollable, Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture))!.Scrollable);
    }

    [Fact]
    public void The_last_switch_of_settings_aircraft_mid_scroll_is_tapped_in_the_strip_that_shows()
    {
        // Geometry: Settings, Aircraft mid-scroll, at the offset where a strip of its last switch was measured to take a tap (176.7,
        // 2026-11-07). The row's control is its toggle, [1137,1214.5 60x35]: drawn from 1037.8, of which about 10 px show above the
        // viewport's bottom (1048). It is tapped in the middle of that strip.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", 176.7f))!;
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.Equal("DIRTY PANEL STATES", dirty.Text);
        Assert.Equal(1048, page.Viewport.Bottom);
        Assert.InRange(dirty.Box.Top, 1037, 1038);

        var tap = Bbs757EfbPress.TapPoint(page, dirty);

        Assert.NotNull(tap);
        Assert.Equal(dirty.Hit.X, tap!.Value.X);
        Assert.Equal(dirty.Box.Top + (1048 - dirty.Box.Top) / 2, tap.Value.Y);
        Assert.Null(Bbs757EfbPress.DragFor(page, dirty));
    }

    [Fact]
    public void The_last_switch_of_settings_aircraft_shows_whole_at_the_end_of_its_scroll()
    {
        // The end of the scroll is the stackbox's 1,180 less the scrollbox's 966: 214, read at +0x134 (B757-26). The last switch is
        // drawn at 1000.5, whole inside the viewport, and tapped at its own point.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Scrolled(Bbs757EfbTreeJson.Load("51-settings-aircraft"), "aircraft_scrollbox", 214f))!;
        var dirty = page.Items.Last(i => i.Kind == Bbs757EfbKind.Toggle);
        Assert.Equal(new Rectangle(1137, 1000, 60, 35), dirty.Box);

        Assert.Equal(dirty.Hit, Bbs757EfbPress.TapPoint(page, dirty));
    }

    /// <summary>Every page item a pilot can press, of every committed tree.</summary>
    public static IEnumerable<object[]> PressablesOfEveryFixture()
    {
        foreach (var path in Directory.GetFiles(Path.Combine(FixtureRoot(), "bbs757-efb-memory"), "*.tree.json").OrderBy(p => p, StringComparer.Ordinal))
            yield return new object[] { Path.GetFileName(path)[..^".tree.json".Length] };
    }

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "MSFSBlindAssist.Tests", "Fixtures");
    }

    [Theory]
    [MemberData(nameof(PressablesOfEveryFixture))]
    public void Every_control_of_every_page_is_tapped_where_it_shows_or_scrolled_to(string fixture)
    {
        // At offset 0, as captured: a page that does not scroll has every control tappable inside its own clip; a page that scrolls has
        // each one tappable or dragged into view. The final review's C1: the ACARS dashboard, Weather & NOTAM and the Checklist had their
        // only (inner) scrollbox as the viewport, so every control outside it could never be pressed.
        var page = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture));
        if (page == null) return;
        var pressables = page.Items.Where(i => i.Kind is Bbs757EfbKind.Button or Bbs757EfbKind.Tab or Bbs757EfbKind.Toggle
            or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Slider).ToList();
        Assert.All(pressables, i =>
        {
            var tap = Bbs757EfbPress.TapPoint(page, i);
            if (tap is { } p)
            {
                var clip = Bbs757EfbPress.ClipOf(i);
                Assert.True(p.Y >= i.Box.Top && p.Y < i.Box.Bottom && p.Y >= clip.Top && p.Y < clip.Bottom,
                    $"{fixture}: {i.Kind} '{i.Text}' tapped at {p}, outside its box {i.Box} or its clip {clip}");
                return;
            }
            Assert.True(page.Scrollable && Bbs757EfbPress.DragFor(page, i) != null,
                $"{fixture}: {i.Kind} '{i.Text}' {i.Box} can neither be tapped nor scrolled to (clip {i.Clip}, viewport {page.Viewport})");
        });
    }

    // ---- Relocate ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Relocate_finds_a_moved_control_by_its_key()
    {
        // The page scrolled between the read and the press: the switch kept its key and lost its place.
        var pressed = Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 333, 237, 33), on: false);
        var before = PageOf("SETTINGS", "AIRCRAFT", pressed);
        var scrolled = Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 203, 237, 33), on: false);
        var other = Item(Bbs757EfbKind.Toggle, "Automatic boarding", "tgl:AUTOMATIC BOARDING", new Rectangle(273, 504, 275, 33), on: false);

        Assert.Same(scrolled, Bbs757EfbPress.Relocate(before, PageOf("SETTINGS", "AIRCRAFT", other, scrolled), pressed));
    }

    [Fact]
    public void Relocate_finds_a_tile_whose_state_words_changed()
    {
        // Req fuel truck, pressed: the tile now says Requesting... in the same place (another word, so another key).
        var pressed = ReqFuelTruck();
        var requesting = Requesting();

        Assert.Same(requesting, Bbs757EfbPress.Relocate(Ground(pressed, ReqCatering()), Ground(ReqCatering(), requesting), pressed));
    }

    [Theory]
    [InlineData(60, 0)]     // IoU 0.53: a neighbor sliding over the tile's place
    [InlineData(100, 0)]    // 0.32
    [InlineData(190, 0)]    // a sliver
    [InlineData(0, 21)]     // half the height: 0.33
    [InlineData(-80, 10)]   // from the other side, a little lower
    public void Relocate_never_takes_a_control_with_other_words(int dx, int dy)
    {
        // The tile is gone (a notification covers it). A neighbor covers its place in part, with other words: tapping it would
        // press another control than the one asked for, so nothing is found.
        var pressed = ReqFuelTruck();
        var neighbor = Tile("Req catering", "btn:|REQ CATERING@62,29", new Rectangle(pressed.Box.X + dx, pressed.Box.Y + dy, pressed.Box.Width, pressed.Box.Height));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(neighbor), pressed));
    }

    [Fact]
    public void Relocate_returns_null_on_another_page()
    {
        // The same key on a different page is another control.
        var pressed = Item(Bbs757EfbKind.Button, "Close all", "btn:|CLOSE ALL@16,9", new Rectangle(326, 189, 195, 29));
        var elsewhere = Item(Bbs757EfbKind.Button, "Close all", "btn:|CLOSE ALL@16,9", new Rectangle(326, 189, 195, 29));

        Assert.Null(Bbs757EfbPress.Relocate(PageOf("AIRCRAFT", "CABIN & DOORS", pressed), PageOf("AIRCRAFT", "GROUND SERVICES", elsewhere), pressed));
    }

    [Theory]
    [InlineData("Fuel truck off", "Fuel truck")]
    [InlineData("Chocks, on", "Chocks")]
    [InlineData("GPU removed", "GPU connected")]
    [InlineData("Left FWD: Closed", "Left FWD: Open")]
    [InlineData("Cockpit open", "cockpit CLOSED")]
    public void Relocate_follows_a_tile_that_moved_and_changed_only_its_state_word(string pressedText, string nowText)
    {
        // Moved down 25 px (a heading appeared above it): another 20 px bucket, so another key, and too little overlap to be the
        // same box (IoU 0.27). Only its words, without the state word, say it is the same tile.
        var pressed = Tile(pressedText, "btn:|TILE@45,9", new Rectangle(911, 193, 197, 43));
        var moved = Tile(nowText, "btn:|TILE@45,10", new Rectangle(911, 218, 197, 43));

        Assert.Same(moved, Bbs757EfbPress.Relocate(Ground(pressed), Ground(moved), pressed));
    }

    [Theory]
    [InlineData("Fuel truck off", "Req fuel truck")]       // other words
    [InlineData("Left FWD: Closed", "Right FWD: Closed")]   // the neighbor door
    [InlineData("Open", "Closed")]                          // a state word alone is no words
    [InlineData("Common", "Comm")]                          // "on" is a state word only as a word of its own
    public void Relocate_does_not_follow_a_moved_tile_with_other_words(string pressedText, string nowText)
    {
        var pressed = Tile(pressedText, "btn:|TILE@45,9", new Rectangle(911, 193, 197, 43));
        var moved = Tile(nowText, "btn:|TILE@45,10", new Rectangle(911, 218, 197, 43));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(moved), pressed));
    }

    [Fact]
    public void Relocate_does_not_follow_the_same_words_that_do_not_overlap_the_pressed_place()
    {
        // Same words, another place altogether (the page re-laid out): not provably the same control.
        var pressed = Tile("Chocks, on", "btn:|CHOCKS@15,25", new Rectangle(312, 513, 196, 42));
        var elsewhere = Tile("Chocks", "btn:|CHOCKS@15,45", new Rectangle(312, 913, 196, 42));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(elsewhere), pressed));
    }

    [Fact]
    public void Relocate_never_takes_a_control_of_another_kind()
    {
        var pressed = ReqFuelTruck();
        var page = Ground(
            Item(Bbs757EfbKind.Toggle, "Req fuel truck", "tgl:REQ FUEL TRUCK", pressed.Box, on: true),
            Item(Bbs757EfbKind.Text, "Req fuel truck", "txt:Req fuel truck", pressed.Box),
            Item(Bbs757EfbKind.Slider, "Req fuel truck", "sld:REQ FUEL TRUCK", pressed.Box));

        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), page, pressed));
    }

    [Fact]
    public void Relocate_takes_the_closest_box_when_several_cover_the_place()
    {
        var pressed = Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(0, 0, 100, 100));
        var weaker = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 65));     // IoU 0.65
        var closer = Tile("Req catering", "btn:|B@0,0", new Rectangle(0, 0, 100, 90));               // IoU 0.90

        Assert.Same(closer, Bbs757EfbPress.Relocate(Ground(pressed), Ground(weaker, closer), pressed));
    }

    [Fact]
    public void Relocate_takes_a_box_at_three_fifths_overlap_and_not_below()
    {
        var pressed = Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(0, 0, 100, 100));
        var exactly = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 60));     // IoU exactly 0.60
        var below = Tile("Requesting" + Ellipsis, "btn:|A@0,0", new Rectangle(0, 0, 100, 59));       // 0.59

        Assert.Same(exactly, Bbs757EfbPress.Relocate(Ground(pressed), Ground(exactly), pressed));
        Assert.Null(Bbs757EfbPress.Relocate(Ground(pressed), Ground(below), pressed));
    }

    [Fact]
    public void Relocate_gives_a_control_with_no_box_only_by_its_key()
    {
        // The window's own Home button has no box. Two empty boxes cover each other in no place at all.
        var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:HOME");
        var other = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:OTHER");

        Assert.Null(Bbs757EfbPress.Relocate(Ground(home), Ground(other), home));
        Assert.Same(home, Bbs757EfbPress.Relocate(Ground(home), Ground(other, home), home));
    }

    [Fact]
    public void Relocate_looks_in_the_menu_the_top_bar_and_the_question()
    {
        var menu = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var boarding = StartBoarding();
        var before = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menu }, new[] { ReqFuelTruck() })
        {
            TopBar = new[] { boarding },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { yes }),
        };
        var menuNow = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var yesNow = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var boardingNow = Boarding();
        var now = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menuNow }, new[] { ReqFuelTruck() })
        {
            TopBar = new[] { boardingNow },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { yesNow }),
        };

        Assert.Same(menuNow, Bbs757EfbPress.Relocate(before, now, menu));
        Assert.Same(yesNow, Bbs757EfbPress.Relocate(before, now, yes));
        // The boarding control keeps its key as its words change (it is text while boarding runs).
        Assert.Same(boardingNow, Bbs757EfbPress.Relocate(before, now, boarding));
    }

    [Fact]
    public void A_control_the_notification_list_covers_is_gone_and_the_others_are_found()
    {
        // bright/18 shows a notification, bright/19 the list the top bar's arrow opens: the list covers four of the page's controls,
        // which the reader drops (it would read them cut). Nothing else may take their place.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var now = Bbs757EfbScreenTests.Load("bright", "19-notification-list");

        foreach (var covered in new[] { "Req cargo SVC", "Right air start", "GPU connected", "Remove all" })
            Assert.Null(Bbs757EfbPress.Relocate(before, now, ItemNamed(before, covered)));
        foreach (var clear in new[] { "Req fuel truck", "Chocks", "Left air start", "Req catering" })
        {
            var pressed = ItemNamed(before, clear);
            Assert.Equal(pressed.Key, Bbs757EfbPress.Relocate(before, now, pressed)?.Key);
        }
    }

    [Fact]
    public void Every_control_of_a_real_page_is_found_again_on_another_capture_of_it()
    {
        // The same page at 100 % and dimmed: the keys hold (they are what Relocate leans on), so each control comes back as itself.
        var before = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var now = Bbs757EfbScreenTests.Load("dim", "13-aircraft-ground-services");

        var controls = before.Menu.Concat(before.Items).Concat(before.TopBar).ToList();
        Assert.True(controls.Count > 15, "the page reads its menu and tiles");
        foreach (var pressed in controls)
            Assert.Equal(pressed.Key, Bbs757EfbPress.Relocate(before, now, pressed)?.Key);
    }

    [Theory]
    [InlineData("Req fuel truck", "Fuel truck off")]
    [InlineData("Req catering", "Catering off")]
    [InlineData("Req cargo SVC", "Cargo SVC off")]
    [InlineData("Req FWD stair", "FWD stair off")]
    [InlineData("Stairs disabled", "AFT stairs off")]
    [InlineData("GPU connected", "GPU removed")]
    public void Relocate_follows_a_real_tile_whose_words_changed_in_place(string pressedText, string nowText)
    {
        // GND SRVC Mode MSFS (bright/18) and BBS (bright/13) word the same tiles differently, in the same boxes.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var now = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");

        Assert.Equal(nowText, Bbs757EfbPress.Relocate(before, now, ItemNamed(before, pressedText))?.Text);
    }

    // ---- NeedsTap ------------------------------------------------------------------------------------------------------

    private static Bbs757EfbItem Switch(bool? on) =>
        Item(Bbs757EfbKind.Toggle, "Sync barometers", "tgl:SYNC BAROMETERS", new Rectangle(273, 333, 237, 33), on: on);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_switch_already_as_wished_needs_no_tap(bool on, bool wantOn)
    {
        // It changed since the read (BlueBird, or the pilot's other hand): tapping would undo the wish.
        Assert.False(Bbs757EfbPress.NeedsTap(Switch(on), wantOn));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_switch_that_differs_from_the_wish_is_tapped(bool on, bool wantOn)
    {
        Assert.True(Bbs757EfbPress.NeedsTap(Switch(on), wantOn));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_unread_switch_is_tapped(bool wantOn)
    {
        // The reader could not tell its state: it is not known to be as wished.
        Assert.True(Bbs757EfbPress.NeedsTap(Switch(null), wantOn));
    }

    // ---- ChoiceX -------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(895, 1190, 5)]     // IRS alignment time
    [InlineData(895, 1190, 4)]     // Boarding & load rate
    [InlineData(895, 1190, 16)]    // Notifications fade-out timer
    [InlineData(795, 1100, 2)]     // two choices: the two ends
    public void The_first_choice_is_at_the_start_of_the_track_and_the_last_at_its_end(int left, int right, int count)
    {
        var slider = new Bbs757EfbSlider(left, right, left);

        Assert.Equal(left, Bbs757EfbPress.ChoiceX(slider, 0, count));
        Assert.Equal(right, Bbs757EfbPress.ChoiceX(slider, count - 1, count));
    }

    [Theory]
    [InlineData(100, 500, 5, 2, 300)]       // the middle of five on a round track
    [InlineData(100, 500, 5, 1, 200)]
    [InlineData(895, 1190, 5, 1, 969)]      // IRS alignment time: 73.75 px apart
    [InlineData(895, 1190, 5, 2, 1043)]     // 147.5: to the even
    [InlineData(895, 1190, 5, 3, 1116)]
    [InlineData(895, 1190, 4, 1, 993)]      // Boarding & load rate
    [InlineData(895, 1190, 4, 2, 1092)]
    [InlineData(895, 1190, 16, 7, 1033)]    // Notifications fade-out timer
    [InlineData(895, 1190, 16, 8, 1052)]
    public void The_choices_are_spread_evenly_along_the_track(int left, int right, int count, int index, int expected)
    {
        // The places measured on the three settings sliders (2026-10-26) are unchanged by the move out of the window's elements.
        Assert.Equal(expected, Bbs757EfbPress.ChoiceX(new Bbs757EfbSlider(left, right, left), index, count));
    }

    [Fact]
    public void A_slider_with_one_choice_is_tapped_at_the_start_of_its_track()
    {
        Assert.Equal(895, Bbs757EfbPress.ChoiceX(new Bbs757EfbSlider(895, 1190, 1000), 0, 1));
    }

    // ---- NextChoiceX ---------------------------------------------------------------------------------------------------

    private static readonly Bbs757EfbSlider Track = new(895, 1190, 1000);

    [Theory]
    // lastX, target, read, count, taps, expected
    [InlineData(1116, 3, 2, 5, 1, 1141)]   // IRS: a choice is 73.75 px, a third 24.6 -> 25; the knob landed one short: right
    [InlineData(969, 1, 2, 5, 1, 944)]     // the other side: the knob landed one past: left
    [InlineData(1033, 7, 6, 16, 2, 1040)]  // fade-out timer: 19.7 px a choice, a third 6.6 -> 7
    [InlineData(1033, 7, 8, 16, 2, 1026)]
    [InlineData(993, 1, 0, 4, 3, 1026)]    // boarding rate: 98.3 px a choice, a third 32.8 -> 33
    [InlineData(1000, 30, 29, 60, 1, 1004)]  // 5 px a choice: a third is under 4 px, so 4 px
    [InlineData(1000, 30, 31, 60, 1, 996)]
    public void The_next_tap_steps_a_third_of_a_choice_toward_the_target(int lastX, int target, int read, int count, int taps, int expected)
    {
        Assert.Equal(expected, Bbs757EfbPress.NextChoiceX(Track, lastX, target, read, count, taps));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Stepping_goes_on_below_four_taps(int taps)
    {
        Assert.NotNull(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, 2, 5, taps));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Stepping_stops_after_four_taps(int taps)
    {
        Assert.Equal(4, Bbs757EfbPress.MaxSliderTaps);
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, 2, 5, taps));
    }

    [Fact]
    public void A_slider_whose_value_was_not_read_is_not_stepped()
    {
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1000, 3, -1, 5, 1));
    }

    [Fact]
    public void A_slider_already_at_the_target_has_no_next_tap()
    {
        Assert.Null(Bbs757EfbPress.NextChoiceX(Track, 1116, 3, 3, 5, 1));
    }

    [Theory]
    [InlineData(1190, 15, 14, 1190)]   // a step past the track's right end would tap beside it (another control's place): the end
    [InlineData(895, 0, 1, 895)]       // and past its left end
    public void A_step_never_leaves_the_track(int lastX, int target, int read, int expected)
    {
        Assert.Equal(expected, Bbs757EfbPress.NextChoiceX(Track, lastX, target, read, 16, 1));
    }

    // ---- Fingerprint and PageUnchanged ---------------------------------------------------------------------------------

    [Fact]
    public void Two_reads_that_say_the_same_have_the_same_fingerprint()
    {
        // A refresh of an unchanged tablet must say the same thing: a pixel of wobble in a box moves nothing the pilot would hear, a misread does.
        var a = Ground(Tile("GPU", "btn:GPU", new Rectangle(300, 300, 80, 40)));
        var b = Ground(Tile("GPU", "btn:GPU", new Rectangle(301, 299, 80, 40)));
        var c = Ground(Tile("GPV", "btn:GPV", new Rectangle(300, 300, 80, 40)));

        Assert.Equal(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(b));
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(a), Bbs757EfbPress.Fingerprint(c));
    }

    [Fact]
    public void A_notification_appearing_over_an_unchanged_page_changes_the_fingerprint()
    {
        // The bridge pushes a read only when its fingerprint moved: a notification over a page that did not change must count.
        var quiet = Ground(Tile("GPU", "btn:GPU", new Rectangle(300, 300, 80, 40)));
        var toast = quiet with { Notices = new[] { GpuConnected } };

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(quiet), Bbs757EfbPress.Fingerprint(toast));
    }

    [Fact]
    public void Wordless_dialogs_are_told_apart_by_their_answers()
    {
        // Every question's answers are keyed alike (q:OK): a question is the same question by its words, and one with none (none was
        // measured) by its text boxes' names and its answers' words, so an OK pressed on one is never tapped on another.
        static Bbs757EfbItem Answer(string text) => Item(Bbs757EfbKind.Button, text, "q:" + text.ToUpperInvariant(), new Rectangle(608, 589, 180, 60));
        var ok = new Bbs757EfbQuestion("", new[] { Answer("OK") });
        var insert = new Bbs757EfbQuestion("", new[] { Answer("Cancel"), Answer("Insert") });
        var fuel = insert with { Rows = new[] { Item(Bbs757EfbKind.Field, "FUEL", "fuel_input_textbox", new Rectangle(595, 284, 250, 52)) } };

        Assert.False(Bbs757EfbPress.SameQuestion(ok, insert));
        Assert.False(Bbs757EfbPress.SameQuestion(insert, fuel));
        Assert.True(Bbs757EfbPress.SameQuestion(ok, ok with { }));
        // Words decide whenever there are any, however the answers read.
        Assert.True(Bbs757EfbPress.SameQuestion(new Bbs757EfbQuestion("Are you sure?", new[] { Answer("Yes") }),
            new Bbs757EfbQuestion("Are you sure ?", new[] { Answer("OK") })));
        Assert.False(Bbs757EfbPress.SameQuestion(ok, new Bbs757EfbQuestion("OK", new[] { Answer("OK") })));
    }

    [Fact]
    public void The_fingerprint_covers_the_top_bar_the_notices_and_the_question()
    {
        var page = Ground(ReqFuelTruck());
        var bar = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:46", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var countdown = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:45", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var toast = page with { Notices = new[] { GpuConnected } };
        var later = page with { Notices = new[] { new Bbs757EfbNotice(1, "GPU connected.", "14:19Z") } };
        var question = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };
        var other = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "OK", "q:OK", new Rectangle(336, 591, 177, 57)) }) };

        var prints = new[] { page, bar, countdown, toast, later, question, other }.Select(Bbs757EfbPress.Fingerprint).ToList();

        Assert.Equal(prints.Count, prints.Distinct().Count());
    }

    [Fact]
    public void A_label_change_is_a_new_page_to_push()
    {
        var box = new Rectangle(300, 300, 80, 40);
        var plain = Ground(Tile("OPEN", "btn:OPEN", box));
        var labeled = Ground(Tile("OPEN", "btn:OPEN", box) with { Label = "NOSE DOOR" });
        var heading = Ground(Item(Bbs757EfbKind.Heading, "Doors", "h:Doors", box) with { Level = 2 });
        var heading3 = Ground(Item(Bbs757EfbKind.Heading, "Doors", "h:Doors", box) with { Level = 3 });

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(plain), Bbs757EfbPress.Fingerprint(labeled));
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(heading), Bbs757EfbPress.Fingerprint(heading3));
    }

    [Fact]
    public void A_drop_downs_entries_changing_is_a_new_page_to_push()
    {
        // Plan 21: the window's choice list offers a drop-down's entries, so a read in which only they changed (another airport's
        // runways, the same first one shown) is pushed; the same entries in a new list are no change.
        var box = new Rectangle(519, 156, 150, 34);
        Bbs757EfbScreenModel Runways(params string[] entries) => Ground(Item(Bbs757EfbKind.Choice, "Runway", "planning_perf_calc_runway_dropdown", box)
            with { Value = "RW07L", Entries = entries });

        Assert.NotEqual(Bbs757EfbPress.Fingerprint(Runways("RW07L", "RW25R")), Bbs757EfbPress.Fingerprint(Runways("RW07L", "RW26")));
        Assert.True(Bbs757EfbPress.PageUnchanged(Runways("RW07L", "RW26"), Runways("RW07L", "RW26")));
        Assert.False(Bbs757EfbPress.PageUnchanged(Runways("RW07L"), Runways("RW07L", "RW26")));
    }

    [Fact]
    public void The_keyboard_coming_up_is_no_page_change()
    {
        var page = Ground(ReqFuelTruck());
        var withKeys = page with
        {
            Keyboard = new Bbs757EfbKeyboard(new Rectangle(0, 648, 1397, 400),
                new[] { new Bbs757EfbKey("char_btn_q|Q|1", "q", "Q", "1", new Point(40, 700)) }),
        };

        Assert.True(Bbs757EfbPress.PageUnchanged(page, withKeys));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_fingerprint_does_not_depend_on_the_Windows_culture(string culture)
    {
        var page = Ground(ReqFuelTruck(), Switch(true)) with
        {
            TopBar = new[] { Notifications() },
            Notices = new[] { GpuConnected },
            Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }),
        };

        Assert.Equal(Bbs757EfbPress.Fingerprint(page), TestCulture.Run(culture, () => Bbs757EfbPress.Fingerprint(page)));
    }

    [Fact]
    public void The_page_is_unchanged_when_only_the_top_bar_or_the_notices_differ()
    {
        var before = Ground(ReqFuelTruck(), ReqCatering());
        var countdown = before with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:46", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var toast = before with { Notices = new[] { GpuConnected }, TopBar = new[] { Notifications() } };

        Assert.True(Bbs757EfbPress.PageUnchanged(before, before));
        Assert.True(Bbs757EfbPress.PageUnchanged(before, countdown));
        Assert.True(Bbs757EfbPress.PageUnchanged(before, toast));
        Assert.True(Bbs757EfbPress.PageUnchanged(countdown, toast));
        // ...but the bridge still sees them as a change.
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(before), Bbs757EfbPress.Fingerprint(toast));
    }

    [Fact]
    public void The_page_is_unchanged_when_things_only_wobble_by_a_pixel()
    {
        var a = Ground(Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1132, 577, 196, 42)));
        var b = Ground(Tile("Req fuel truck", "btn:|REQ FUEL TRUCK@56,28", new Rectangle(1131, 576, 197, 43)));

        Assert.True(Bbs757EfbPress.PageUnchanged(a, b));
    }

    [Fact]
    public void A_knob_bluebird_moved_is_a_change_of_the_page_in_whole_percent()
    {
        // The review's I2: the window's slider value is the knob's place, so two reads that differ only in BlueBird's knob are two pages
        // (the bridge pushes the second, a press sees its change). In whole percent, as the window shows it, so a pixel of wobble that
        // stays at the same percent is none.
        static Bbs757EfbScreenModel At(int knobX, bool read = true) => PageOf("AIRCRAFT", "WEIGHT & BALANCE",
            Item(Bbs757EfbKind.Slider, "FORWARD CARGO", "wb_baggage_fwd_slider", new Rectangle(1162, 462, 188, 18))
                with { Slider = new Bbs757EfbSlider(1168, 1344, knobX) { KnobRead = read } });

        Assert.False(Bbs757EfbPress.PageUnchanged(At(1273), At(1300)));                     // 60 % and 75 %
        Assert.NotEqual(Bbs757EfbPress.Fingerprint(At(1273)), Bbs757EfbPress.Fingerprint(At(1300)));
        Assert.True(Bbs757EfbPress.PageUnchanged(At(1273), At(1274)));                      // 59.66 % and 60.23 %: both 60
        // A knob placed from the value shown (not BlueBird's) moves only with that value, which the page already compares.
        Assert.True(Bbs757EfbPress.PageUnchanged(At(1273, read: false), At(1300, read: false)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_knobs_place_in_the_fingerprint_is_the_same_in_any_culture(string culture)
    {
        var page = PageOf("SYSTEMS", "", Item(Bbs757EfbKind.Slider, "ELEC ZOOM", "elec_zoom_slider", new Rectangle(16, 148, 42, 154))
            with { Slider = new Bbs757EfbSlider(296, 154, 281) { Vertical = true, AcrossX = 37, KnobRead = true } });
        Assert.Equal(TestCulture.Run("en-US", () => Bbs757EfbPress.Fingerprint(page)), TestCulture.Run(culture, () => Bbs757EfbPress.Fingerprint(page)));
        Assert.Contains("|11\n", Bbs757EfbPress.Fingerprint(page));
    }

    [Fact]
    public void A_question_appearing_is_a_change_of_the_page()
    {
        var before = Ground(ReqFuelTruck());
        var after = before with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };

        Assert.False(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.False(Bbs757EfbPress.PageUnchanged(after, before));
    }

    [Fact]
    public void Anything_on_the_page_itself_changing_is_a_change_of_the_page()
    {
        var tile = ReqFuelTruck();
        var before = Ground(tile);
        var changedPages = new[]
        {
            Ground(tile with { Text = "Requesting" + Ellipsis }),
            Ground(tile with { Value = "5 percent" }),
            Ground(tile with { On = true }),
            Ground(tile with { Selected = true }),
            Ground(tile with { Key = "btn:|REQ FUEL TRUCK@57,28" }),
            Ground(tile, ReqCatering()),
            PageOf("AIRCRAFT", "CABIN & DOORS", tile),
            new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29)) }, new[] { tile }),
        };

        Assert.All(changedPages, after => Assert.False(Bbs757EfbPress.PageUnchanged(before, after)));
    }

    // ---- ButtonOutcome -------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_press_that_changes_the_page_says_nothing(string culture)
    {
        var fuel = Item(Bbs757EfbKind.Menu, "Fuel", "menu:FUEL", new Rectangle(43, 216, 58, 29));
        var before = Ground(ReqFuelTruck());
        var after = PageOf("AIRCRAFT", "FUEL", Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", new Rectangle(377, 929, 280, 45)));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, fuel)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_tile_whose_words_changed_under_a_new_key_says_its_new_words(string culture)
    {
        // Req fuel truck, pressed: the tile says Requesting... in the same box, under another key. The window rebuilds a control
        // whose key changed and says nothing of it, so the press says the new words, once.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqCatering(), Requesting());

        Assert.Equal("Requesting" + Ellipsis, TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_control_whose_words_changed_says_them_whatever_its_key_did(string culture)
    {
        var box = new Rectangle(700, 780, 280, 45);
        var mode = new Rectangle(258, 864, 196, 42);
        var truck = new Rectangle(1132, 577, 196, 42);
        var cases = new (string Section, Bbs757EfbItem Pressed, Bbs757EfbItem Now)[]
        {
            // The ACARS dashboard's fetch becomes its refresh in the same box: another key.
            ("ACARS DASHBOARD", Tile("Fetch SIMBRIEF OFP", "btn:|FETCH SIMBRIEF OFP@42,40", box),
                Tile("Refresh SIMBRIEF OFP", "btn:|REFRESH SIMBRIEF OFP@42,40", box)),
            // GND SRVC Mode under its caption (bright/13 and bright/18): btn:GND SRVC Mode|BBS@16,44 becomes ...|MSFS@16,44.
            ("GROUND SERVICES", Tile("GND SRVC Mode: BBS", "btn:GND SRVC Mode|BBS@16,44", mode),
                Tile("GND SRVC Mode: MSFS", "btn:GND SRVC Mode|MSFS@16,44", mode)),
            // BBS mode's tile: its state word changed and its key, which leaves the state word out, did not.
            ("GROUND SERVICES", Tile("Fuel truck off", "btn:|FUEL TRUCK@56,28", truck), Tile("Fuel truck on", "btn:|FUEL TRUCK@56,28", truck)),
        };

        foreach (var (section, pressed, now) in cases)
        {
            var before = PageOf("AIRCRAFT", section, pressed);
            var after = PageOf("AIRCRAFT", section, now);
            Assert.Equal(now.Text, TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_question_the_press_brought_up_is_its_outcome_whatever_the_dimmed_page_reads(string culture)
    {
        // IMPORT FROM SIMBRIEF, then the defuel question over the dimmed page: under the dimming the reader names the button by the
        // TOTAL FUEL label above it (bright/20 reads btn:|IMPORT FROM SIMBRIEF@18,46, bright/21 btn:TOTAL FUEL|IMPORT FROM
        // SIMBRIEF@18,46), in the same box. The read that found the question said it; the button's other words would talk over it.
        var box = new Rectangle(377, 929, 280, 45);
        var import = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", box);
        var before = PageOf("AIRCRAFT", "FUEL", import);
        var dimmed = Tile("Total fuel: Import from SIMBRIEF", "btn:TOTAL FUEL|IMPORT FROM SIMBRIEF@18,46", box);
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var after = PageOf("AIRCRAFT", "FUEL", dimmed) with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { yes }) };
        Assert.Same(dimmed, Bbs757EfbPress.Relocate(before, after, import));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, import)));
        // Under a question that was up before the press (a top bar control pressed under it), the control's new words are said.
        var asked = before with { Question = after.Question, TopBar = new[] { StartBoarding() } };
        var boarding = asked with { TopBar = new[] { Boarding() } };
        Assert.Equal("Boarding" + Ellipsis + " 10 min", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(asked, boarding, StartBoarding())));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_page_with_only_a_new_notification_is_still_no_change(string culture)
    {
        // The press did nothing the page shows; the notification (BlueBird's, or another press's) is not the press's outcome.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = before with { Notices = new[] { GpuConnected }, TopBar = new[] { Notifications() } };

        Assert.Equal("Req fuel truck: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_unchanged_page_is_no_change(string culture)
    {
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqFuelTruck(), ReqCatering());

        Assert.Equal("Req fuel truck: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_press_that_changed_another_control_says_nothing(string culture)
    {
        // The pressed tile reads as before, but something else on the page moved: the press did something.
        var pressed = ReqFuelTruck();
        var before = Ground(pressed, ReqCatering());
        var after = Ground(ReqFuelTruck(), Tile("Requesting" + Ellipsis, "btn:|REQ CATERING@45,36", ReqCatering().Box));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_question_appearing_is_the_outcome_not_a_no_change(string culture)
    {
        var pressed = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@18,46", new Rectangle(377, 929, 280, 45));
        var before = PageOf("AIRCRAFT", "FUEL", pressed);
        var after = before with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void An_answer_whose_question_went_says_nothing_whatever_lies_under_it(string culture)
    {
        // YES pressed: the question went, and the page it covered has a button where the YES was. By its box alone it would be
        // found as the YES with other words; it is another control, and the question's going is the press's outcome.
        var yes = Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57));
        var before = PageOf("AIRCRAFT", "FUEL") with { Question = new Bbs757EfbQuestion("Are you sure you want to defuel?", new[] { yes }) };
        var under = Tile("Import from SIMBRIEF", "btn:|IMPORT FROM SIMBRIEF@21,30", new Rectangle(338, 592, 177, 57));
        var after = PageOf("AIRCRAFT", "FUEL", under);
        Assert.Same(under, Bbs757EfbPress.Relocate(before, after, yes));

        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, yes)));
        // The question still up, the answer not taken: that is no change.
        Assert.Equal("Yes: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, before with { }, yes)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_button_that_became_text_says_its_new_words_though_the_top_bar_is_not_the_page(string culture)
    {
        // Start boarding becomes Boarding... 10 min, text under the same key: the page below the bar is untouched, but the pressed
        // control changed, and its new words say the press took.
        var pressed = StartBoarding();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck()) with { TopBar = new[] { Boarding() } };

        Assert.True(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.Equal("Boarding" + Ellipsis + " 10 min", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_notification_button_says_its_new_words(string culture)
    {
        // Pressed, it opens BlueBird's list (bright/18 to bright/19): "Notifications: 55" becomes "Close notifications", its key
        // the same, while the list covers part of the page.
        var before = Bbs757EfbScreenTests.Load("bright", "18-aircraft-ground-services-notification");
        var after = Bbs757EfbScreenTests.Load("bright", "19-notification-list");
        var pressed = Assert.Single(before.TopBar, i => i.Key == Bbs757EfbTopBar.NotificationsKey);

        Assert.Equal("Close notifications", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_control_that_vanished_says_nothing(string culture)
    {
        var pressed = Notifications();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck());

        Assert.True(Bbs757EfbPress.PageUnchanged(before, after));
        Assert.Null(TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void A_top_bar_control_that_did_nothing_is_no_change(string culture)
    {
        var pressed = StartBoarding();
        var before = Ground(ReqFuelTruck()) with { TopBar = new[] { pressed } };
        var after = Ground(ReqFuelTruck()) with { TopBar = new[] { StartBoarding() } };

        Assert.Equal("Start boarding: no change on the tablet.", TestCulture.Run(culture, () => Bbs757EfbPress.ButtonOutcome(before, after, pressed)));
    }

    // ---- the texts -----------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_spoken_texts_are_the_same_in_any_Windows_culture(string culture)
    {
        // Verbatim from the plan's speech rules. The label is the element's text; the value is already the words said.
        var texts = TestCulture.Run(culture, () => new[]
        {
            Bbs757EfbPress.GoneText("Req fuel truck"),
            Bbs757EfbPress.NoChangeText("Req fuel truck"),
            Bbs757EfbPress.NotChangedText("Sync barometers"),
            Bbs757EfbPress.ChoiceText("CPDLC network", "SayIntentions"),
            Bbs757EfbPress.ChoiceText("Displays bootup speed", "Realistic"),
            Bbs757EfbPress.CouldNotSetText("IRS alignment time", "Full (7 min+)"),
            Bbs757EfbPress.CouldNotSetText("Ceiling", "30,5 percent"),
            Bbs757EfbPress.UnreachableText,
        });

        Assert.Equal(new[]
        {
            "Req fuel truck is no longer on the tablet.",
            "Req fuel truck: no change on the tablet.",
            "Sync barometers did not change.",
            "CPDLC network: SayIntentions",
            "Displays bootup speed: Realistic",
            "IRS alignment time could not be set to Full (7 min+).",
            "Ceiling could not be set to 30,5 percent.",
            "The tablet cannot be reached, so nothing was tapped.",
        }, texts);
    }

    // ---- the new word of a switch of two choices ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Hoppie", "SayIntentions", "SayIntentions")]        // the word changed: the new word, as the page shows it
    [InlineData("Hoppie", "Sayintentions", "Sayintentions")]        // the reader's spelling is kept
    [InlineData("Option 1", "Option 2", "Option 2")]                // a digit is part of the word
    [InlineData("HDG", "Trk", "Trk")]
    [InlineData("100%", "Auto", "Auto")]                            // a percent is a word to compare, whatever the tablet shows
    [InlineData("Hoppie", "Hoppie", null)]                          // the pill moved, the word did not
    [InlineData("Hoppie", "HOPPIE", null)]                          // case wobbles from one read to the next
    [InlineData("Imperial", "IMPERIAL", null)]                      // in any culture (tr-TR upper-cases an i to a dotted capital)
    [InlineData("Option 1", " option  1. ", null)]                  // so do spaces and punctuation
    [InlineData("", "SayIntentions", null)]                         // no word before: a plain switch, or one the reader gave none
    [InlineData("On", "SayIntentions", null)]                       // On and Off are the plain switch's states, not words
    [InlineData("off", "SayIntentions", null)]
    [InlineData("Hoppie", "", null)]                                // no word now
    [InlineData("Hoppie", "On", null)]
    [InlineData("Hoppie", "OFF", null)]
    [InlineData("Hoppie", "...", null)]                             // punctuation alone is no word
    [InlineData("40", "", null)]                                    // the TOD pause distance switched off
    [InlineData("Hoppie", null, null)]                              // the control was not found again
    public void The_new_word_of_a_two_choice_switch_is_the_word_it_shows_now_when_it_is_another(string before, string? now, string? expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE", "tr-TR" })
            Assert.Equal(expected, TestCulture.Run(culture, () => Bbs757EfbPress.NewChoiceWord(before, now)));
    }

    // ---- plan 20, Task 6: named controls are still found, and a settings slider has its track --------------------------------

    [Fact]
    public void A_labelled_door_button_is_relocated_by_its_key()
    {
        var closed = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("14-aircraft-cabin-doors"))!;
        var door = closed.Items.Single(i => i.Name == "btn_l1_door");
        Assert.Equal("LEFT FWD", door.Label);
        Assert.Equal(8, closed.Items.Count(i => i.Kind == Bbs757EfbKind.Button && i.Label.Length > 0));

        // The door opened: its words change, its label and its key do not, and the read finds the same control again.
        var opened = new Bbs757EfbScreenModel(closed.App, closed.Section, closed.Menu,
            closed.Items.Select(i => i.Name == "btn_l1_door" ? i with { Text = "Open" } : i).ToList());
        var found = Bbs757EfbPress.Relocate(closed, opened, door);

        Assert.Equal("btn_l1_door", found?.Name);
        Assert.Equal("Open", found?.Text);
        Assert.Equal(door.Key, found?.Key);
        var doors = closed.Items.Where(i => i.Name.EndsWith("_door", StringComparison.Ordinal)).ToList();
        Assert.Equal(doors.Count, doors.Select(i => i.Key).Distinct().Count()); // no two share a key
    }

    [Fact]
    public void A_settings_choice_slider_taps_its_choice_from_the_track()
    {
        var model = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load("51-settings-aircraft"))!;
        var boarding = Assert.Single(model.Items, i => i.Text == "BOARDING & LOAD RATE");
        var options = Bbs757EfbElements.OptionsFor(boarding, model.PageName)!;
        Assert.Equal(new[] { "Realistic", "Standard", "Quick", "Instant" }, options, StringComparer.OrdinalIgnoreCase);

        var track = Assert.IsType<Bbs757EfbSlider>(boarding.Slider);
        // The committed page has BlueBird's 15 px knob (captured 2026-11-07), and a tap centers the knob on the tap point (measured), so
        // the track is the knob center's range, 897 + 7.5 to 1197 - 7.5: the choices sit at the knob centers, the last one inside the box.
        Assert.True(track.KnobRead);
        Assert.Equal((904, 1189), (track.TrackLeft, track.TrackRight));
        // The 2026-10-27 measurement: x 1095 set QUICK and x 945 set REALISTIC; each choice is 100 px wide, so the tap lands within half of that.
        Assert.InRange(Bbs757EfbPress.ChoiceX(track, 2, options.Count), 1095 - 50, 1095 + 50);
        Assert.InRange(Bbs757EfbPress.ChoiceX(track, 0, options.Count), 945 - 50, 945 + 50);

        // With no knob read (a layout without the slider knob), the track is the slider's box, as before.
        var bare = Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.WithoutKnobs(Bbs757EfbTreeJson.Load("51-settings-aircraft")))!;
        var fallback = Assert.Single(bare.Items, i => i.Text == "BOARDING & LOAD RATE").Slider!;
        Assert.Equal((897, 1197, false), (fallback.TrackLeft, fallback.TrackRight, fallback.KnobRead));
    }
}
