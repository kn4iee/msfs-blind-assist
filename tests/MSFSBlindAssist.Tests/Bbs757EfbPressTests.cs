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

    private static readonly Bbs757EfbNotice GpuConnected = new("GPU connected.", "14:17Z");

    private static Bbs757EfbItem ItemNamed(Bbs757EfbScreenModel m, string text) =>
        m.Menu.Concat(m.Items).Concat(m.TopBar).FirstOrDefault(i => i.Text == text)
        ?? throw new Xunit.Sdk.XunitException($"No control '{text}' on {m.PageName}: " + string.Join(" | ", m.Items.Select(i => i.Text)));

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
    public void The_fingerprint_covers_the_top_bar_the_notices_and_the_question()
    {
        var page = Ground(ReqFuelTruck());
        var bar = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:46", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var countdown = page with { TopBar = new[] { Item(Bbs757EfbKind.Text, "DEP -00:45", Bbs757EfbTopBar.DepartureKey, new Rectangle(1029, 0, 106, 28)) } };
        var toast = page with { Notices = new[] { GpuConnected } };
        var later = page with { Notices = new[] { new Bbs757EfbNotice("GPU connected.", "14:19Z") } };
        var question = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "Yes", "q:YES", new Rectangle(336, 591, 177, 57)) }) };
        var other = page with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Item(Bbs757EfbKind.Button, "OK", "q:OK", new Rectangle(336, 591, 177, 57)) }) };

        var prints = new[] { page, bar, countdown, toast, later, question, other }.Select(Bbs757EfbPress.Fingerprint).ToList();

        Assert.Equal(prints.Count, prints.Distinct().Count());
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
            Bbs757EfbPress.CouldNotSetText("IRS alignment time", "Full (7 min+)"),
            Bbs757EfbPress.CouldNotSetText("Ceiling", "30,5 percent"),
            Bbs757EfbPress.UnreachableText,
        });

        Assert.Equal(new[]
        {
            "Req fuel truck is no longer on the tablet.",
            "Req fuel truck: no change on the tablet.",
            "Sync barometers did not change.",
            "IRS alignment time could not be set to Full (7 min+).",
            "Ceiling could not be set to 30,5 percent.",
            "The tablet cannot be reached, so nothing was tapped.",
        }, texts);
    }
}
