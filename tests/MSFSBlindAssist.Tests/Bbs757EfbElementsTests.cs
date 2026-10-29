using System.Drawing;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 tablet window's elements (plan 18, Task 7): a read page as the shared EFB window's push
/// (<see cref="Bbs757EfbElements.Build"/>), in the order the pilot meets it, with every control the reader offers pressable and
/// nothing refused (the owner's ruling of 2026-10-29). Pages are the 2026-10-29 captures (tests/MSFSBlindAssist.Tests/Fixtures/
/// bbs757-efb); a few controls are built in memory where no capture holds them. A refresh of an unchanged tablet says the same
/// thing in the same order (the reading-order tests).
/// </summary>
public class Bbs757EfbElementsTests
{
    private static Bbs757EfbTextLine Line(string text, int x, int y, int h = 12) =>
        new(text, new Rectangle(x, y, 60, h), Array.Empty<Bbs757EfbWord>(), 'A');

    [Fact]
    public void A_pixel_of_wobble_does_not_swap_two_things_on_one_line()
    {
        // With the old 12 px bands, y 11 and y 12 fell in different bands and the right-hand item
        // came FIRST on one read and second on the next.
        foreach (int wobble in new[] { 0, 1, 2 })
        {
            var lines = new[] { Line("RIGHT", 300, 11 + wobble), Line("LEFT", 50, 12 - wobble) };
            var order = Bbs757EfbReadingOrder.Lines(lines).Select(l => l.Text).ToArray();
            Assert.Equal(new[] { "LEFT", "RIGHT" }, order);
        }
    }

    [Fact]
    public void Lines_a_row_apart_stay_in_top_to_bottom_order()
    {
        var lines = new[] { Line("SECOND", 50, 40), Line("FIRST", 300, 20), Line("THIRD", 10, 60) };
        Assert.Equal(new[] { "FIRST", "SECOND", "THIRD" }, Bbs757EfbReadingOrder.Lines(lines).Select(l => l.Text));
    }

    [Fact]
    public void A_column_of_close_lines_is_not_chained_into_one_row()
    {
        // Anchored on a row's first line: 5 px steps must not pull a whole column into one row.
        var lines = new[] { Line("C", 10, 20), Line("A", 200, 0), Line("B", 100, 10) };
        Assert.Equal(new[] { "A", "B", "C" }, Bbs757EfbReadingOrder.Lines(lines, 6).Select(l => l.Text));
    }

    // ---- helpers -------------------------------------------------------------------------------------------------------

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static readonly IReadOnlyList<Bbs757EfbExtraElement> NoExtras = Array.Empty<Bbs757EfbExtraElement>();

    private const char OptionSeparator = (char)0x1f;

    private static Bbs757EfbElements.Built BuildOf(string set, string name, string? alert = null) =>
        Bbs757EfbElements.Build(Bbs757EfbScreenTests.Load(set, name), alert, NoExtras);

    private static string Field(Bbs757EfbElements.Built b, int index, string field) =>
        b.Data.TryGetValue($"items.{index}.{field}", out var v) ? v : "";

    private static List<string> Texts(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => Field(b, i, "text")).ToList();

    private static List<string> Keys(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count).Select(i => Field(b, i, "key")).ToList();

    private static int IndexOf(Bbs757EfbElements.Built b, string text)
    {
        int index = Texts(b).IndexOf(text);
        Assert.True(index >= 0, $"No element '{text}': " + string.Join(" | ", Texts(b)));
        return index;
    }

    private static Bbs757EfbElements.Action? ActionAt(Bbs757EfbElements.Built b, int index) =>
        b.Actions.TryGetValue(int.Parse(Field(b, index, "aidx")), out var action) ? action : null;

    private static string[] OptionsAt(Bbs757EfbElements.Built b, int index) =>
        Field(b, index, "options").Split(OptionSeparator);

    // A control the pilot can press: clickable, never dimmed, and a tap on the tablet control it was read from.
    private static Bbs757EfbElements.Action AssertPressable(Bbs757EfbElements.Built b, string text)
    {
        int index = IndexOf(b, text);
        Assert.Equal("true", Field(b, index, "clickable"));
        Assert.Equal("false", Field(b, index, "disabled"));
        var action = ActionAt(b, index);
        Assert.NotNull(action);
        Assert.Equal(Bbs757EfbElements.ActionKind.Tap, action!.Kind);
        Assert.NotNull(action.Item);
        return action;
    }

    // Text on the window: no control behind it, nothing to press.
    private static void AssertText(Bbs757EfbElements.Built b, string text)
    {
        int index = IndexOf(b, text);
        Assert.Equal("false", Field(b, index, "clickable"));
        Assert.Equal("", Field(b, index, "kind"));
        Assert.Equal("", Field(b, index, "type"));
        Assert.Null(ActionAt(b, index));
    }

    private static Bbs757EfbItem Control(Bbs757EfbKind kind, string text, string key, Rectangle box, bool clickable,
        bool? on = null, string value = "", Bbs757EfbSlider? slider = null) =>
        new(kind, text, box, new Point(box.Left + box.Width / 2, box.Top + box.Height / 2), clickable, key, Bbs757EfbTint.Blue,
            on, Value: value, Slider: slider);

    private static Bbs757EfbItem Answer(string word, string key = "") =>
        Control(Bbs757EfbKind.Button, word, key.Length > 0 ? key : "q:" + word.ToUpperInvariant(), new Rectangle(336, 591, 177, 57), true);

    // ---- every control is pressable ------------------------------------------------------------------------------------

    [Fact]
    public void Every_control_is_pressable()
    {
        // Ground services, REMOVE ALL among them (it used to be refused with external power on line).
        var ground = BuildOf("bright", "13-aircraft-ground-services");
        foreach (var tile in new[] { "Remove all", "Jetway removed", "GPU removed", "Chocks, on", "Fuel truck off", "Ground A/C", "GND SRVC Mode: BBS" })
            AssertPressable(ground, tile);

        // Presets (they used to wait for the ground or the air) and the custom panel states' SAVE and LOAD.
        var presets = BuildOf("bright", "10-aircraft-presets");
        foreach (var tile in new[] { "Cold & dark", "Ready for takeoff", "After takeoff", "Save user config", "Load user config" })
            AssertPressable(presets, tile);

        // Settings: the switches, with the word of their state, and the slider.
        var settings = BuildOf("bright", "51-settings-aircraft");
        foreach (var row in new[] { "Sync barometers", "Automatic boarding", "Nosewheel steering: Realistic", "IRS alignment time" })
            AssertPressable(settings, row);

        // Callouts: Rotate and Positive rate sit where the reader still keeps its old update guard.
        var callouts = BuildOf("bright", "53-settings-callouts");
        foreach (var row in new[] { "80 KTS", "Rotate", "Positive rate" })
            AssertPressable(callouts, row);

        // The checklist: a line's box, and the side menu's own switch and button.
        var checklist = BuildOf("bright", "40-checklist");
        foreach (var row in new[] { "Reset all", "Interactive", "Parking brake: SET", "Reset" })
            AssertPressable(checklist, row);
    }

    [Fact]
    public void The_navigation_data_buttons_are_pressable_in_the_box_the_reader_keeps_clear()
    {
        // The AIRAC page's UPDATE or REMOVE and the controls round it, as the reader marks them not clickable inside the old guard box.
        var guard = Bbs757EfbScreen.UpdateGuard;
        var update = Control(Bbs757EfbKind.Button, "Update", "btn:|UPDATE@64,15", new Rectangle(guard.Left + 30, guard.Top + 10, 140, 40), false);
        var remove = Control(Bbs757EfbKind.Button, "Remove", "btn:|REMOVE@64,15", new Rectangle(guard.Left + 30, guard.Top + 10, 140, 40), false);
        var nearby = Control(Bbs757EfbKind.Toggle, "Some switch", "tgl:SOME SWITCH", new Rectangle(guard.Left + 20, guard.Top + 50, 100, 30), false, on: true);
        var model = new Bbs757EfbScreenModel("SETTINGS", "AIRAC DATA", Array.Empty<Bbs757EfbItem>(), new[] { update, remove, nearby });

        var b = Bbs757EfbElements.Build(model, null, NoExtras);

        Assert.Equal("Settings, AIRAC data", b.Data["page"]);
        Assert.Same(update, AssertPressable(b, "Update").Item);
        Assert.Same(remove, AssertPressable(b, "Remove").Item);
        Assert.Same(nearby, AssertPressable(b, "Some switch").Item);
        Assert.Equal("checkbox", Field(b, IndexOf(b, "Some switch"), "type"));
        Assert.Equal("true", Field(b, IndexOf(b, "Some switch"), "value"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_readers_clickable_flag_decides_nothing(bool flag)
    {
        // A button, a switch, a check box, a slider and a tab press whatever the flag says; a row, a text and a heading never do.
        var track = new Bbs757EfbSlider(895, 1190, 1000);
        Bbs757EfbItem Make(Bbs757EfbKind kind, string text, Bbs757EfbSlider? slider = null) =>
            Control(kind, text, "k:" + text, new Rectangle(300, 200, 120, 30), flag, slider: slider);
        var menu = Make(Bbs757EfbKind.Menu, "Side entry");
        var tab = Control(Bbs757EfbKind.Menu, "Landing gear", "tab:|LANDING GEAR@10,10", new Rectangle(500, 100, 120, 30), flag);
        var items = new[]
        {
            Make(Bbs757EfbKind.Button, "A tile"), Make(Bbs757EfbKind.Toggle, "A switch"), Make(Bbs757EfbKind.Checkbox, "A check box"),
            Make(Bbs757EfbKind.Slider, "A slider", track), tab,
            Make(Bbs757EfbKind.Row, "A row"), Make(Bbs757EfbKind.Text, "A text"), Make(Bbs757EfbKind.Heading, "A heading"),
        };
        var model = new Bbs757EfbScreenModel("AIRCRAFT", "GROUND SERVICES", new[] { menu }, items);

        var b = Bbs757EfbElements.Build(model, null, NoExtras);

        foreach (var text in new[] { "Side entry", "A tile", "A switch", "A check box", "A slider", "Landing gear" })
            AssertPressable(b, text);
        foreach (var text in new[] { "A row", "A text", "A heading" })
        {
            int index = IndexOf(b, text);
            Assert.Equal("false", Field(b, index, "clickable"));
            Assert.Null(ActionAt(b, index));
        }
    }

    [Fact]
    public void A_tile_whose_words_carry_its_state_is_announced_when_they_change()
    {
        var b = BuildOf("bright", "13-aircraft-ground-services");

        Assert.Equal("true", Field(b, IndexOf(b, "Fuel truck off"), "announceChange"));
        Assert.Equal("true", Field(b, IndexOf(b, "GND SRVC Mode: BBS"), "announceChange"));
        // Home and the side menu say their page; a tile with no state in its words has nothing to announce.
        Assert.Equal("", Field(b, IndexOf(b, "Home"), "announceChange"));
        Assert.Equal("", Field(b, IndexOf(b, "Fuel"), "announceChange"));
        Assert.Equal("", Field(b, IndexOf(b, "Req PB tug"), "announceChange"));
    }

    // ---- the order -----------------------------------------------------------------------------------------------------

    [Fact]
    public void A_question_comes_first_as_text()
    {
        // The window speaks only its first alert and remembers one text: a question kept as an alert would be said again whenever
        // another alert went. The bridge says a new question through its own alert, once (plan 18, Task 8); here it is text.
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        Assert.Equal(page.Question!.Text, Field(b, 0, "text"));
        Assert.Equal("question", Field(b, 0, "key"));
        AssertText(b, page.Question.Text);
        Assert.Equal("", Field(b, 0, "live"));
        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "kind") == "alert");
        // Its answers follow at once, left to right, each pressing its own control on the tablet.
        Assert.Equal(new[] { "Yes", "No" }, Texts(b).Skip(1).Take(2));
        for (int answer = 0; answer < 2; answer++)
        {
            Assert.Equal("button", Field(b, 1 + answer, "kind"));
            Assert.Equal("true", Field(b, 1 + answer, "clickable"));
            var action = ActionAt(b, 1 + answer)!;
            Assert.Equal(Bbs757EfbElements.ActionKind.Tap, action.Kind);
            Assert.Same(page.Question.Buttons[answer], action.Item);
        }
        Assert.Equal(new[] { "q:YES", "q:NO" }, Keys(b).Skip(1).Take(2));
    }

    [Fact]
    public void A_question_with_one_answer_has_one_button()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "22-fuel-adjustment-complete");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        Assert.Equal("Fuel adjustment is complete!", Field(b, 0, "text"));
        AssertText(b, "Fuel adjustment is complete!");
        Assert.Same(Assert.Single(page.Question!.Buttons), AssertPressable(b, "OK").Item);
    }

    [Fact]
    public void An_alert_is_ahead_of_the_question_and_is_the_only_alert()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var b = Bbs757EfbElements.Build(page, "The tablet did not show Fuel. Try again, or use Read the tablet again.", NoExtras);

        Assert.Equal("The tablet did not show Fuel. Try again, or use Read the tablet again.", Field(b, 0, "text"));
        Assert.Equal("alert", Field(b, 0, "kind"));
        Assert.Equal("assertive", Field(b, 0, "live"));
        Assert.Equal(page.Question!.Text, Field(b, 1, "text"));
        Assert.Equal("", Field(b, 1, "kind"));
        Assert.Single(Enumerable.Range(0, b.Count), i => Field(b, i, "kind") == "alert");
        Assert.Equal(new[] { "Yes", "No" }, Texts(b).Skip(2).Take(2));
    }

    [Fact]
    public void Each_element_is_numbered_by_its_identity_on_the_page()
    {
        // The bridge numbers an element by what it is (its key, else its words, and which of the page's elements with that identity
        // it is), so it can keep the number while the element stays and never give one number to two elements.
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services");
        var identities = new List<string>();
        var numbered = Bbs757EfbElements.Build(page, "Alert", NoExtras, identity =>
        {
            identities.Add(identity);
            return 1000 + identities.Count;
        });

        Assert.Equal(numbered.Count, identities.Count);
        Assert.Equal(identities.Count, identities.Distinct().Count());
        Assert.Equal(Enumerable.Range(1001, numbered.Count).Select(n => n.ToString()), Enumerable.Range(0, numbered.Count).Select(i => Field(numbered, i, "aidx")));
        Assert.Equal("alert#1", identities[0]);
        Assert.Contains("btn:HOME#1", identities);
        Assert.Equal("btn:HOME", numbered.Actions[1001 + identities.IndexOf("btn:HOME#1")].Item!.Key);

        // Without a numbering, 1, 2, 3...
        var plain = Bbs757EfbElements.Build(page, null, NoExtras);
        Assert.Equal(Enumerable.Range(1, plain.Count).Select(n => n.ToString()), Enumerable.Range(0, plain.Count).Select(i => Field(plain, i, "aidx")));
    }

    [Fact]
    public void The_page_behind_a_question_is_text_while_the_answers_and_the_top_bar_press()
    {
        // The tablet dims its page under a question and takes no tap on it: the window offers none either (this mirrors the tablet).
        var page = Bbs757EfbScreenTests.Load("bright", "21-defuel-question");
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        foreach (var behind in new[] { "Home", "Cockpit presets", "Fuel", "Ground services", "Total fuel: Import from SIMBRIEF" })
            AssertText(b, behind);
        AssertPressable(b, "Yes");
        AssertPressable(b, "No");
        AssertPressable(b, "Start boarding");
        AssertPressable(b, "Notifications");

        var taps = b.Actions.OrderBy(a => a.Key).Where(a => a.Value.Kind == Bbs757EfbElements.ActionKind.Tap).Select(a => a.Value.Item!.Key);
        Assert.Equal(new[] { "q:YES", "q:NO", "top:boarding", "top:notifications" }, taps);
        // The page's read-outs are still there to read.
        Assert.Contains("14%", Texts(b));
    }

    [Fact]
    public void A_switch_or_slider_behind_a_question_is_text_with_its_state()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft")
            with { Question = new Bbs757EfbQuestion("Are you sure?", new[] { Answer("Yes") }) };
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        var texts = Texts(b);
        Assert.Contains("Sync barometers: Off", texts);
        Assert.Contains("Nosewheel steering: Realistic", texts);
        Assert.Contains("IRS alignment time: Full (7 min+)", texts);
        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "type") is "checkbox" or "select" or "range");
        AssertPressable(b, "Yes");
    }

    [Fact]
    public void Top_bar_items_follow_the_question_and_keep_their_keys()
    {
        var b = BuildOf("bright", "21-defuel-question");

        // The question and its answers, then the top bar left to right, then the page (Home first).
        Assert.Equal(new[] { "question", "q:YES", "q:NO", "top:dep", "top:boarding", "top:notifications", "btn:HOME" }, Keys(b).Take(7));
        Assert.Equal(new[] { "DEP -03:19", "Start boarding", "Notifications" }, Texts(b).Skip(3).Take(3));
        // The countdown is a read-out; the boarding control and the notifications are buttons on the tablet's own controls.
        AssertText(b, "DEP -03:19");
        Assert.Equal("top:boarding", AssertPressable(b, "Start boarding").Item!.Key);
        Assert.Equal("top:notifications", AssertPressable(b, "Notifications").Item!.Key);
        Assert.Equal("button", Field(b, 4, "kind"));
        Assert.Equal("button", Field(b, 5, "kind"));
    }

    [Fact]
    public void The_top_bar_leads_a_page_with_no_question()
    {
        var b = BuildOf("bright", "13-aircraft-ground-services");

        Assert.Equal(new[] { "top:notifications", "btn:HOME" }, Keys(b).Take(2));
        Assert.Equal("Home", Field(b, 1, "text"));
        AssertPressable(b, "Home");
    }

    [Fact]
    public void The_boarding_control_keeps_its_key_as_its_words_change()
    {
        // START BOARDING is a button; while boarding runs the same place reads "Boarding… 10 min", text, under the same key.
        var starting = BuildOf("bright", "20-fuel-start-boarding");
        var running = BuildOf("bright", "12-aircraft-fuel-boarding");

        Assert.Equal("top:boarding", AssertPressable(starting, "Start boarding").Item!.Key);
        Assert.Equal("top:boarding", Field(starting, IndexOf(starting, "Start boarding"), "key"));
        int index = IndexOf(running, "Boarding… 10 min");
        Assert.Equal("top:boarding", Field(running, index, "key"));
        AssertText(running, "Boarding… 10 min");
    }

    [Fact]
    public void A_change_in_the_notification_button_is_announced_whatever_it_says()
    {
        // Pressing it opens or closes BlueBird's list, and its words become "Close notifications" or back: the pilot hears which.
        foreach (var (set, name, text) in new[]
        {
            ("bright", "13-aircraft-ground-services", "Notifications"),
            ("bright", "18-aircraft-ground-services-notification", "Notifications: 55"),
            ("bright", "19-notification-list", "Close notifications"),
        })
        {
            var b = BuildOf(set, name);
            AssertPressable(b, text);
            Assert.Equal("true", Field(b, IndexOf(b, text), "announceChange"));
        }
    }

    [Fact]
    public void Notifications_are_text_under_their_heading()
    {
        var b = BuildOf("bright", "19-notification-list");

        int heading = IndexOf(b, "Notifications");
        Assert.Equal("2", Field(b, heading, "level"));
        // After the top bar (its button reads Close notifications while the list is open), before the page.
        Assert.Equal("Close notifications", Field(b, heading - 1, "text"));
        Assert.Equal(new[]
        {
            "14:17Z GPU disconnected.", "14:17Z GPU connected.", "12:44Z R3 door closed.", "12:44Z R3 door opened.", "12:44Z L3 door closed.",
        }, Texts(b).Skip(heading + 1).Take(5));
        for (int row = heading + 1; row <= heading + 5; row++)
        {
            Assert.Equal("false", Field(b, row, "clickable"));
            Assert.Equal("", Field(b, row, "kind"));
            Assert.Equal("0", Field(b, row, "level"));
            Assert.Null(ActionAt(b, row));
        }
        Assert.Equal("Home", Field(b, heading + 6, "text"));
    }

    [Fact]
    public void A_notification_without_its_time_is_its_words_alone()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "13-aircraft-ground-services")
            with { Notices = new[] { new Bbs757EfbNotice("Chocks attached.", null), new Bbs757EfbNotice("GPU connected.", "14:17Z") } };
        var b = Bbs757EfbElements.Build(page, null, NoExtras);

        int heading = Enumerable.Range(0, b.Count).First(i => Field(b, i, "text") == "Notifications" && Field(b, i, "level") == "2");
        Assert.Equal(new[] { "Chocks attached.", "14:17Z GPU connected." }, Texts(b).Skip(heading + 1).Take(2));
    }

    [Fact]
    public void No_notifications_no_heading()
    {
        var b = BuildOf("bright", "13-aircraft-ground-services");

        Assert.DoesNotContain(Enumerable.Range(0, b.Count), i => Field(b, i, "text") == "Notifications" && Field(b, i, "level") == "2");
    }

    [Fact]
    public void Extras_come_after_the_page_and_act_as_extras()
    {
        var page = Bbs757EfbScreenTests.Load("bright", "53-settings-callouts");   // it scrolls: its own buttons end the window
        var extras = new[]
        {
            new Bbs757EfbExtraElement("extra:signin", Bbs757EfbExtras.SignInText, true),
            new Bbs757EfbExtraElement("extra:code", "Navigraph code: ABCD-1234", false),
        };

        var without = Texts(Bbs757EfbElements.Build(page, null, NoExtras));
        var b = Bbs757EfbElements.Build(page, null, extras);

        // The page's own elements, then the extras, then Scroll down, Scroll up, More and Read the tablet again.
        Assert.Equal(without.Take(without.Count - 4).Concat(new[] { "Sign in to Navigraph", "Navigraph code: ABCD-1234" }).Concat(without.Skip(without.Count - 4)),
            Texts(b));
        Assert.Equal(new[] { "Scroll down", "Scroll up", "More", "Read the tablet again" }, without.Skip(without.Count - 4));

        int signIn = IndexOf(b, "Sign in to Navigraph");
        Assert.Equal("button", Field(b, signIn, "kind"));
        Assert.Equal("true", Field(b, signIn, "clickable"));
        Assert.Equal("extra:signin", Field(b, signIn, "key"));
        var action = ActionAt(b, signIn)!;
        Assert.Equal(Bbs757EfbElements.ActionKind.Extra, action.Kind);
        Assert.Equal("extra:signin", action.ExtraKey);
        Assert.Null(action.Item);

        int code = IndexOf(b, "Navigraph code: ABCD-1234");
        Assert.Equal("extra:code", Field(b, code, "key"));
        AssertText(b, "Navigraph code: ABCD-1234");
    }

    [Fact]
    public void Extras_are_offered_even_when_no_page_is_shown()
    {
        var b = Bbs757EfbElements.Build(null, "The tablet reader could not open.", new[] { new Bbs757EfbExtraElement("extra:signin", "Sign in to Navigraph", true) });

        Assert.Equal(new[] { "The tablet reader could not open.", "Sign in to Navigraph", "More", "Read the tablet again" }, Texts(b));
        Assert.Equal(Bbs757EfbElements.ActionKind.Extra, ActionAt(b, 1)!.Kind);
    }

    [Fact]
    public void The_scroll_buttons_are_the_windows_own_and_scroll_the_page()
    {
        var b = BuildOf("bright", "53-settings-callouts");

        Assert.Equal(Bbs757EfbElements.ActionKind.ScrollDown, ActionAt(b, IndexOf(b, "Scroll down"))!.Kind);
        Assert.Equal(Bbs757EfbElements.ActionKind.ScrollUp, ActionAt(b, IndexOf(b, "Scroll up"))!.Kind);
        Assert.DoesNotContain("Scroll down", Texts(BuildOf("bright", "51-settings-aircraft")));
    }

    [Fact]
    public void No_classic_window_or_read_only_note()
    {
        // The window presses what the pilot presses (rule B757-19, plan 18): no hand-over to another window, no note that it only reads.
        Assert.Equal(new[] { "Tap", "Refresh", "ScrollDown", "ScrollUp", "Extra" }, Enum.GetNames<Bbs757EfbElements.ActionKind>());

        foreach (var (set, name) in new[]
        {
            ("bright", "13-aircraft-ground-services"), ("bright", "10-aircraft-presets"), ("bright", "51-settings-aircraft"),
            ("bright", "53-settings-callouts"), ("bright", "21-defuel-question"), ("dim", "01-home"),
        })
        {
            var b = BuildOf(set, name);
            var texts = Texts(b);

            Assert.DoesNotContain(texts, t => t.Contains("classic", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(texts, t => t.Contains("only reads", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(new[] { "More", "Read the tablet again" }, texts.TakeLast(2));
            Assert.Equal(Bbs757EfbElements.ActionKind.Refresh, ActionAt(b, b.Count - 1)!.Kind);
            Assert.Equal("button", Field(b, b.Count - 1, "kind"));
        }

        // Nothing to show: still the same two, after the alert.
        var none = Bbs757EfbElements.Build(null, "The BlueBird tablet cannot be reached. Is the 757 loaded?", NoExtras);
        Assert.Equal("BlueBird tablet", none.Data["page"]);
        Assert.Equal(new[] { "The BlueBird tablet cannot be reached. Is the 757 loaded?", "More", "Read the tablet again" }, Texts(none));
    }

    // ---- a Settings row's slider is a choice list in the catalog's order -----------------------------------------------

    [Fact]
    public void A_settings_slider_is_a_choice_list_in_the_catalogs_order()
    {
        var aircraft = BuildOf("bright", "51-settings-aircraft");
        int irs = IndexOf(aircraft, "IRS alignment time");
        Assert.Equal("select", Field(aircraft, irs, "type"));
        Assert.Equal(new[] { "Instant", "1 min", "2 min", "5 min", "Full (7 min+)" }, OptionsAt(aircraft, irs));
        Assert.Equal("Full (7 min+)", Field(aircraft, irs, "value"));   // the reader wrote FULL (7 MIN+): the choice it is
        int rate = IndexOf(aircraft, "Boarding & load rate");
        Assert.Equal(new[] { "Realistic", "Standard", "Quick", "Instant" }, OptionsAt(aircraft, rate));
        Assert.Equal("Realistic", Field(aircraft, rate, "value"));

        var general = BuildOf("dim", "50-settings-general");
        int theme = IndexOf(general, "EFB colors theme");
        // The slider's order, which is not the stored numbers': Light grey before Dark grey (the vocabulary's old list had them the other way).
        Assert.Equal(new[] { "Dark blue", "Light blue", "Light grey", "Dark grey" }, OptionsAt(general, theme));
        Assert.Equal("Dark blue", Field(general, theme, "value"));
        int timer = IndexOf(general, "Notifications fade-out timer");
        var seconds = OptionsAt(general, timer);
        Assert.Equal(29, seconds.Length);
        Assert.Equal("3 sec", seconds[0]);
        Assert.Equal("30 sec", seconds[^2]);
        Assert.Equal("Never", seconds[^1]);
        Assert.Equal("10 sec", Field(general, timer, "value"));
        int background = IndexOf(general, "EFB background");
        Assert.Equal(new[] { "Default", "1", "2", "3", "4", "5", "6", "7", "8", "9" }, OptionsAt(general, background));
        Assert.Equal("Default", Field(general, background, "value"));
    }

    [Fact]
    public void OptionsFor_is_the_catalogs_for_a_settings_row_and_nothing_for_any_other_slider()
    {
        var general = Bbs757EfbScreenTests.Load("dim", "50-settings-general");
        var theme = general.Items.First(i => i.Key == "sld:EFB COLORS THEME");

        Assert.Equal(new[] { "Dark blue", "Light blue", "Light grey", "Dark grey" }, Bbs757EfbElements.OptionsFor(theme, general.PageName));
        Assert.Equal(1, Bbs757EfbElements.CurrentChoice(theme with { Value = "LIGHT BLUE" }, general.PageName, Bbs757EfbElements.OptionsFor(theme, general.PageName)!));
        // The same words on a page that holds no such setting are no setting.
        Assert.Null(Bbs757EfbElements.OptionsFor(theme, "Aircraft, Ground services"));
        // A label that matches no catalog entry (a misread) offers no list, so it is judged by the page alone and never by another row's key.
        Assert.Null(Bbs757EfbElements.OptionsFor(theme with { Text = "EFB colors thme" }, general.PageName));
        // The cabin lights have no choices: a number.
        var cabin = Bbs757EfbScreenTests.Load("bright", "14-aircraft-cabin-doors");
        foreach (var light in cabin.Items.Where(i => i.Kind == Bbs757EfbKind.Slider))
            Assert.Null(Bbs757EfbElements.OptionsFor(light, cabin.PageName));
    }

    [Fact]
    public void A_slider_with_no_choices_or_no_value_read_is_a_number()
    {
        var track = new Bbs757EfbSlider(795, 1100, 1100);
        var brightness = Control(Bbs757EfbKind.Slider, "EFB brightness", "sld:EFB BRIGHTNESS MODE", new Rectangle(273, 215, 277, 30), true,
            value: "100%", slider: track);
        var unread = Control(Bbs757EfbKind.Slider, "IRS alignment time", "sld:IRS ALIGNMENT TIME", new Rectangle(273, 513, 255, 33), true,
            value: "", slider: new Bbs757EfbSlider(895, 1190, 1000));
        var model = new Bbs757EfbScreenModel("SETTINGS", "GENERAL", Array.Empty<Bbs757EfbItem>(), new[] { brightness });
        var aircraft = new Bbs757EfbScreenModel("SETTINGS", "AIRCRAFT", Array.Empty<Bbs757EfbItem>(), new[] { unread });

        var b = Bbs757EfbElements.Build(model, null, NoExtras);
        int index = IndexOf(b, "EFB brightness (100%)");
        Assert.Equal("range", Field(b, index, "type"));
        Assert.Equal("100", Field(b, index, "value"));
        Assert.Equal("0", Field(b, index, "min"));
        Assert.Equal("100", Field(b, index, "max"));
        Assert.Equal("", Field(b, index, "options"));
        AssertPressable(b, "EFB brightness (100%)");

        // The setting has choices, but the reader could not tell which is set: a number from the knob, as before.
        var b2 = Bbs757EfbElements.Build(aircraft, null, NoExtras);
        Assert.Equal("range", Field(b2, IndexOf(b2, "IRS alignment time"), "type"));
        AssertPressable(b2, "IRS alignment time");
    }

    // ---- the same in any culture ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_elements_are_the_same_in_any_Windows_culture(string culture)
    {
        var pages = new[]
        {
            Bbs757EfbScreenTests.Load("bright", "14-aircraft-cabin-doors"),    // sliders as numbers
            Bbs757EfbScreenTests.Load("bright", "51-settings-aircraft"),
            Bbs757EfbScreenTests.Load("dim", "50-settings-general"),
            Bbs757EfbScreenTests.Load("bright", "21-defuel-question"),
            Bbs757EfbScreenTests.Load("bright", "19-notification-list"),
        };
        var extras = new[] { new Bbs757EfbExtraElement("extra:code", "Navigraph code: ABCD-1234", false) };

        foreach (var page in pages)
        {
            var expected = TestCulture.Run("en-US", () => Bbs757EfbElements.Build(page, "Alert", extras).Data);
            var actual = TestCulture.Run(culture, () => Bbs757EfbElements.Build(page, "Alert", extras).Data);
            Assert.Equal(expected, actual);
        }
        Assert.Contains(pages, page => Bbs757EfbElements.Build(page, null, NoExtras).Data.Values.Contains("range"));
    }
}

public class Bbs757EfbNavigationCheckTests
{
    private static Bbs757EfbScreenModel Page(string app, string section) =>
        new(app, section, new List<Bbs757EfbItem>(), new List<Bbs757EfbItem>());

    [Theory]
    [InlineData("AIRCRAFT", "", "Aircraft", true)]                       // an app tapped on Home: its first section shows
    [InlineData("AIRCRAFT", "FUEL", "Aircraft", true)]
    [InlineData("AIRCRAFT", "FUEL", "Fuel", true)]                       // a section tapped in the menu
    [InlineData("HOME", "", "Home", true)]
    [InlineData("HOME", "", "Aircraft", false)]                          // still the old page: read again
    [InlineData("AIRCRAFT", "FUEL", "Fuel (current page)", true)]
    [InlineData("AIRCRAFT", "GROUND SERVICES", "Fuel", false)]
    public void A_navigation_tap_is_judged_by_the_page_name(string app, string section, string tapped, bool landed)
    {
        Assert.Equal(landed, Bbs757EfbPressRunner.PageIs(Page(app, section), tapped));
    }
}
