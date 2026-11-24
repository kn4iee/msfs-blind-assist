// The A330's MCDU window on the display reader's frames, both sides, on the replay source: what it lists, its status
// row, what it says (once, only what the screen reader cannot say), its keys and its typing. Each window is a real
// WinForms form, shown far off the screen and without taking focus, on a clock the test moves; its key writes and its
// waits go through the window's test seams, so nothing reaches a simulator and every wait runs at once. A small MCDU
// stands in for the aircraft's typed line: each key shows on it a set time after its press, or never.

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public sealed class A330McduFormTests : IDisposable
{
    // The chords of these tests are the standard line keys (Ctrl+1 and the rest); a pilot's own setting must not change them.
    private readonly bool _savedAlternateKeys = SettingsManager.Current.MCDUUseAlternateLSKKeys;

    public A330McduFormTests() => SettingsManager.Current.MCDUUseAlternateLSKKeys = false;

    public void Dispose() => SettingsManager.Current.MCDUUseAlternateLSKKeys = _savedAlternateKeys;

    private const string InitTitle = "INIT        \U00002190\U00002192";

    /// <summary>INIT's title as the window says it: its words, without the slew arrows the title row carries.</summary>
    private const string InitSaid = "INIT";

    /// <summary>A listed title as the window says it (<see cref="CduLineText.Spoken"/>, pinned in A330McduLinesTests).</summary>
    private static string Said(string listedTitle) => CduLineText.Spoken(listedTitle);

    /// <summary>The windows of a test, its replay, what was spoken and every key flag the windows wrote.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly List<IniA330CduForm> _windows = new();
        private readonly List<(long At, bool FirstOfficer, string Key)> _due = new();

        public Rig(A330ReplayDisplays? displays = null) => Displays = displays ?? new A330ReplayDisplays();

        public A330ReplayDisplays Displays { get; }
        public SpeechCapture Speech { get; } = new();
        public SimConnectManager Sim { get; } = SeededSimConnectCache.With();
        public long Clock = 100_000;

        /// <summary>Every key flag written, with the window's clock when it was.</summary>
        public List<(long At, string Flag)> Sent { get; } = new();

        /// <summary>True: the MCDU's typed line follows the keys (the reader gives it); false: it stays as the test set it.</summary>
        public bool TypedLine;

        /// <summary>How long after its press a key shows on the typed line.</summary>
        public int LagMs = 40;

        /// <summary>Keys that never show.</summary>
        public HashSet<string> Dropped { get; } = new();

        public IniA330CduForm Window(bool firstOfficer = false)
        {
            var w = new IniA330CduForm(Displays, Sim, Speech, CduFormProfile.A330(firstOfficer))
            {
                NowMs = () => Clock,
                Delay = ms => { Advance(ms); return Task.CompletedTask; },
                SendPressFlag = Press,
                ShowInactive = true,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            _windows.Add(w);
            return w;
        }

        /// <summary>The window's clock moves on, and the keys due by then show on the typed line.</summary>
        public void Advance(int ms)
        {
            Clock += Math.Max(0, ms);
            foreach (var due in _due.Where(d => d.At <= Clock).ToList())
            {
                _due.Remove(due);
                string pad = Displays.McduScratchpad(due.FirstOfficer) ?? "";
                Displays.SetScratchpad(due.FirstOfficer, Typed(pad, due.Key));
            }
        }

        private bool Press(string flag)
        {
            Sent.Add((Clock, flag));
            bool firstOfficer = flag.StartsWith("INI_MCDU2_", StringComparison.Ordinal);
            string key = flag["INI_MCDU1_".Length..];
            if (TypedLine && !Dropped.Contains(key)) _due.Add((Clock + LagMs, firstOfficer, key));
            return true;
        }

        /// <summary>What a key does to the typed line (measured 2026-11-20).</summary>
        private static string Typed(string pad, string key) => key switch
        {
            "DECIMAL" => pad + ".",
            "SLASH" => pad + "/",
            "SPACE" => pad + " ",
            "PLUS" => pad.EndsWith('-') ? pad[..^1] + "+" : pad + "-",
            "CLR" => pad.Length > 0 ? pad[..^1] : pad,
            { Length: 1 } => pad + key,
            _ => pad,
        };

        public void Dispose()
        {
            foreach (var w in _windows)
                if (!w.IsDisposed) w.Dispose();
        }
    }

    private static A330Frame Fixture(string name) =>
        A330ReplayDisplays.FromFixtures(name).Latest(A330Gauge.McduCaptain)!;

    private static string[] Listing(A330Frame frame) =>
        CduLineText.Listing(A330McduLines.From(A330McduScreen.From(frame)!)).ToArray();

    /// <summary>The frame with the text of drawing order <paramref name="order"/> changed.</summary>
    private static A330Frame WithText(A330Frame frame, int order, string text) =>
        frame with { Texts = frame.Texts.Select(t => t.Order == order ? t with { Text = text } : t).ToList() };

    /// <summary>The frame with its scratchpad line holding only <paramref name="text"/> (nothing for "").</summary>
    private static A330Frame WithPad(A330Frame frame, string text)
    {
        var texts = frame.Texts.Where(t => A330McduGeometry.RowOf(t.Baseline) != A330McduLines.ScratchpadRow).ToList();
        if (text.Length > 0)
            texts.Add(new A330Text(999, A330McduGeometry.Left, A330McduGeometry.Top + A330McduLines.ScratchpadRow * A330McduGeometry.RowPitch,
                text, A330McduGeometry.LargeFont, 29.5f, new A330Rgb(1, 1, 1), [A330McduGeometry.Left]));
        return frame with { Texts = texts };
    }

    private static A330Frame Init() => Fixture("p03_init.json");

    /// <summary>MCDU MENU, with INIT's scratchpad line, so that only its title differs from INIT's in what is said.</summary>
    private static A330Frame Menu() => WithPad(Fixture("p03_menu.json"), "NAV ACCUR DOWNGRADED");

    private static ListBox List(Form w) => w.Controls.Find("Screen", true).OfType<ListBox>().Single();
    private static string[] Items(Form w) => List(w).Items.Cast<string>().ToArray();

    /// <summary>The title as the window says it: the list's first row, which carries the other MCDU windows' "Title: ".</summary>
    private static string TitleOf(Form w)
    {
        Assert.StartsWith("Title: ", Items(w)[0]);
        return Items(w)[0]["Title: ".Length..];
    }

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => AllControls(c).Prepend(c));

    private static Button PageKey(Form w, string label) => AllControls(w).OfType<Button>().Single(b => b.Text == label);
    private static TextBox Status(Form w) => w.Controls.Find("Status", true).OfType<TextBox>().Single();
    private static ComboBox Side(Form w) => w.Controls.Find("Side", true).OfType<ComboBox>().Single();
    private static void Key(IniA330CduForm w, Keys keys) => w.HandleKey(new KeyEventArgs(keys));

    /// <summary>Shows the window and forgets what it said on opening (its page's title, as the Fenix and FlyByWire windows
    /// say it: pinned in <see cref="A_new_title_is_said_and_the_selection_goes_to_the_first_line_after_it"/>), for the
    /// tests whose subject is something else.</summary>
    private static void Open(Rig rig, IniA330CduForm w)
    {
        w.Show();
        rig.Speech.All.Clear();
    }

    /// <summary>The window's next refresh, one refresh later on its clock.</summary>
    private static void Tick(Rig rig, IniA330CduForm w, int ms = 250)
    {
        rig.Advance(ms);
        w.RefreshNow();
    }

    // ---- The display it wants ------------------------------------------------------------------------

    [Fact]
    public void The_form_wants_its_gauge_while_visible_and_releases_it_when_hidden()
    {
        using var rig = new Rig();
        var w = rig.Window();
        Assert.Empty(rig.Displays.Wants);   // built, not shown: nothing wanted

        w.Show();
        Assert.Equal(new[] { (A330Gauge.McduCaptain, (object)w, 250) }, rig.Displays.Wants);

        w.Hide();
        Assert.Equal(new[] { (A330Gauge.McduCaptain, (object)w) }, rig.Displays.Releases);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));

        w.Show();
        Assert.Equal(250, rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));

        // The pilot's close hides the window, as today, and lets the display go.
        w.Close();
        Assert.False(w.Visible);
        Assert.Equal(2, rig.Displays.Releases.Count);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));
        w.Dispose();
        Assert.Equal(2, rig.Displays.Releases.Count);   // once

        // A window disposed while shown lets its display go too, once; the first officer's wants the first officer's display.
        var fo = rig.Window(firstOfficer: true);
        fo.Show();
        Assert.Equal(250, rig.Displays.RefreshWanted(A330Gauge.McduFirstOfficer, fo));
        fo.Dispose();
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduFirstOfficer, fo));
        Assert.Equal(rig.Displays.Wants.Select(x => (x.Gauge, x.Owner)), rig.Displays.Releases);
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void Switching_away_from_the_aircraft_closes_the_window_and_lets_its_display_go()
    {
        var replay = new A330ReplayDisplays();
        var def = new IniA330Definition { Displays = replay };
        using var rig = new Rig(replay);
        var w = rig.Window();
        w.Show();
        // the window the MCDU key would have opened; HandleMcduHotkey takes the focus, so the field is set by reflection
        FieldInfo held = typeof(IniA330Definition).GetField("_mcduWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        held.SetValue(def, w);

        def.OnSwitchedAway();

        Assert.True(w.IsDisposed);
        Assert.Null(held.GetValue(def));
        Assert.Null(replay.RefreshWanted(A330Gauge.McduCaptain, w));
        Assert.Single(replay.Releases);
    }

    // ---- The list ---------------------------------------------------------------------------------------

    [Fact]
    public void A_new_complete_frame_updates_the_list_in_place()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        Open(rig, w);

        var init = Init();
        Assert.Equal(Listing(init), Items(w));
        Assert.Equal("Title: " + InitTitle, Items(w)[0]);
        Assert.Equal("5: 20                 WIND>", Items(w)[9]);
        Assert.Equal("Scratchpad: NAV ACCUR DOWNGRADED", Items(w)[^1]);
        Assert.Equal(1, List(w).SelectedIndex);   // the first line after the title, as on the Fenix and FlyByWire windows
        Assert.Equal("MCDU: Connected", Status(w).Text);

        // The pilot is on the cost index. The next frame changes it; every other row is the same, in new strings.
        List(w).SelectedIndex = 9;
        var before = Items(w);
        rig.Displays.Show(WithText(init, 15, "30"));
        Tick(rig, w);

        Assert.Equal(before.Length, List(w).Items.Count);
        Assert.Equal("5: 30                 WIND>", List(w).Items[9]);
        for (int i = 0; i < before.Length; i++)
            if (i != 9) Assert.Same(before[i], List(w).Items[i]);   // only the changed row was rewritten
        Assert.Equal(9, List(w).SelectedIndex);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void No_complete_frame_for_three_refreshes_shows_the_status_row()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        Open(rig, w);
        var page = Items(w);
        List(w).SelectedIndex = 6;

        // The newest frame is not complete: the last complete page stays while the display reader still gives it.
        rig.Displays.Show(new A330Frame(A330Gauge.McduCaptain, [], Complete: false, Missing: 4, ReadTicks: rig.Clock));
        for (int i = 0; i < 3; i++)
        {
            Tick(rig, w);
            Assert.Equal(page, Items(w));
        }

        // No complete frame for three refreshes: the display reader gives none, and the window shows its status row.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("Reading the captain's MCDU", Status(w).Text);

        // The page back: the pilot is on the row they were on.
        rig.Displays.Show(Init());
        Tick(rig, w);
        Assert.Equal(page, Items(w));
        Assert.Equal(6, List(w).SelectedIndex);
        Assert.Equal("MCDU: Connected", Status(w).Text);

        // The status rows are the display windows' own: off, reading, and after 30 s with no complete frame, cannot read.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        rig.Displays.Show(new A330Frame(A330Gauge.McduCaptain, [], Complete: false, Missing: 0, ReadTicks: rig.Clock + 250, Drawn: false));
        Tick(rig, w);
        Assert.Equal(new[] { "Captain's MCDU is off" }, Items(w));
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Tick(rig, w, 30_000);
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, Items(w));
        Assert.Equal("The display reader cannot read the captain's MCDU", Status(w).Text);   // the reason stays in the box
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);
    }

    [Fact]
    public void Greyed_text_is_listed_as_drawn_with_or_without_colours()
    {
        var menu = Fixture("p03_menu.json");
        var plain = menu with { Texts = menu.Texts.Select(t => t with { Colour = null }).ToList() };
        using var rig = new Rig();
        rig.Displays.Show(plain);
        var w = rig.Window();
        Open(rig, w);

        Assert.Equal("MCDU: Connected", Status(w).Text);   // no colour words (owner, 2026-11-23)
        Assert.Equal(Listing(plain), Items(w));
        Assert.Contains("3: <AIDS", Items(w));

        // With its colours the page lists the same: no marker for greyed text, as on the other MCDU windows.
        var shown = Items(w);
        rig.Displays.Show(menu);
        Tick(rig, w);
        Assert.Equal("MCDU: Connected", Status(w).Text);
        Assert.Equal(shown, Items(w));
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Until_the_reader_leaves_hidden_rows_out_the_window_shows_no_page_and_says_nothing_of_it()
    {
        var hidden = Init() with { HiddenRowsLeftOut = false };
        using var rig = new Rig();
        rig.Displays.Show(hidden);
        var w = rig.Window();
        w.Show();

        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("Reading the captain's MCDU", Status(w).Text);

        // A new title, a new scratchpad line and a line key: no page is shown, so none of it is said.
        rig.Displays.Show(WithPad(Fixture("p03_menu.json") with { HiddenRowsLeftOut = false }, "FORMAT ERROR"));
        Tick(rig, w);
        Tick(rig, w);
        Key(w, Keys.Control | Keys.D3);
        Tick(rig, w, 2000);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("INI_MCDU1_LSK3L", Assert.Single(rig.Sent).Flag);

        // Frames that keep coming are a reader at work: never "cannot read".
        for (int s = 0; s < 40; s++) Tick(rig, w, 1000);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_frame_the_reader_has_not_left_hidden_rows_out_of_yet_keeps_the_page_shown_and_says_nothing()
    {
        // The reader sends every new frame once as read, then with its hidden rows left out moments later: on a page
        // change, a slew, a clock that ticks. Meanwhile the page shown stays, as the Fenix window keeps its last screen.
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        Open(rig, w);
        var init = Items(w);
        List(w).SelectedIndex = 6;

        // MENU arrives as read, with a message: INIT stays, row and status as they were, nothing said, for 2.75 s.
        var menu = WithPad(Fixture("p03_menu.json"), "FORMAT ERROR");
        rig.Displays.Show(menu with { HiddenRowsLeftOut = false });
        for (int i = 0; i < 11; i++)
        {
            Tick(rig, w);
            Assert.Equal(init, Items(w));
            Assert.Equal(6, List(w).SelectedIndex);
            Assert.Equal("MCDU: Connected", Status(w).Text);
        }
        Assert.Empty(rig.Speech.All);

        // The same frame with its hidden rows left out: the new page, its title, then its scratchpad line.
        rig.Displays.Show(menu);
        Tick(rig, w);
        Assert.Equal(Listing(menu), Items(w));
        Assert.Equal(new[] { "MCDU MENU" }, rig.Speech.All);
        Tick(rig, w);
        Assert.Equal(new[] { "MCDU MENU", "FORMAT ERROR" }, rig.Speech.All);

        // A list page as read, its hidden rows below the screen (off the grid): the page stays as well.
        var shown = Items(w);
        rig.Displays.Show(OffGrid(Init()) with { HiddenRowsLeftOut = false });
        for (int i = 0; i < 11; i++) Tick(rig, w);
        Assert.Equal(shown, Items(w));

        // 3 s after the last page read with none since: the reader is at work, but the page shown may be out of date.
        Tick(rig, w);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("Reading the captain's MCDU", Status(w).Text);
        for (int s = 0; s < 40; s++) Tick(rig, w, 1000);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));

        // The page back: the pilot's row, and nothing said but the new title.
        rig.Displays.Show(Init());
        Tick(rig, w);
        Assert.Equal(init, Items(w));
        Assert.Equal(new[] { "MCDU MENU", "FORMAT ERROR", InitSaid }, rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void A_page_with_a_text_off_the_grid_keeps_the_page_shown_and_says_nothing()
    {
        // Measured 2026-11-23, once each session: a frame with its hidden rows left out and a text off the grid. Before,
        // the page gave way to the status row for it, and the screen reader read the title row again when the page came
        // back. Now the page stays, as for a frame the reader has not sorted yet.
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        Open(rig, w);
        var init = Items(w);
        List(w).SelectedIndex = 6;

        rig.Displays.Show(OffGrid(Init()) with { HiddenRowsLeftOut = true });
        for (int i = 0; i < 11; i++)
        {
            Tick(rig, w);
            Assert.Equal(init, Items(w));
            Assert.Equal(6, List(w).SelectedIndex);
        }
        rig.Displays.Show(Init());
        Tick(rig, w);
        Assert.Equal(init, Items(w));
        Assert.Equal(6, List(w).SelectedIndex);
        Assert.Empty(rig.Speech.All);

        // Off the grid for good: after 3 s the status row, and after 30 s with no page "MCDU disconnected".
        rig.Displays.Show(OffGrid(Init()) with { HiddenRowsLeftOut = true });
        Tick(rig, w, 3000);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Tick(rig, w, 30_000);
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, Items(w));
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);
    }

    /// <summary>The frame with a list row the MCDU does not show, below the screen (row 20): off the MCDU's grid.</summary>
    private static A330Frame OffGrid(A330Frame frame) => frame with
    {
        Texts = frame.Texts.Append(new A330Text(998, A330McduGeometry.Left, A330McduGeometry.Top + 20 * A330McduGeometry.RowPitch,
            "08L", A330McduGeometry.LargeFont, 29.5f, null, [A330McduGeometry.Left])).ToList(),
    };

    [Fact]
    public void A_list_page_with_its_hidden_rows_below_the_screen_shows_reading_and_says_nothing()
    {
        // Today's list pages (measured 2026-11-22, ARRIVAL): the rows the MCDU does not show sit below the screen, so
        // the frame is off the grid, and it does not leave hidden rows out. The reader is at work: never "cannot read".
        var list = OffGrid(Init()) with { HiddenRowsLeftOut = false };
        Assert.Null(A330McduScreen.From(list));
        using var rig = new Rig();
        rig.Displays.Show(list);
        var w = rig.Window();
        w.Show();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("Reading the captain's MCDU", Status(w).Text);

        for (int s = 0; s < 40; s++) Tick(rig, w, 1000);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Assert.Equal("Reading the captain's MCDU", Status(w).Text);
        Assert.Empty(rig.Speech.All);

        // A frame off the grid that does leave hidden rows out is a geometry the window does not know: after 30 s with
        // no page, the display reader cannot read the MCDU.
        rig.Displays.Show(OffGrid(Init()) with { HiddenRowsLeftOut = true });
        Tick(rig, w);
        Assert.Equal(new[] { "Reading the captain's MCDU" }, Items(w));
        Tick(rig, w, 30_000);
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, Items(w));
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);
    }

    // ---- Spoken status (spec D3), in the Fenix and FlyByWire windows' words --------------------------------

    [Theory]
    [InlineData(A330ReaderState.NotInstalled)]
    [InlineData(A330ReaderState.NoAccess)]
    [InlineData(A330ReaderState.NotRecognised)]
    [InlineData(A330ReaderState.OutOfDate)]
    [InlineData(A330ReaderState.Failed)]
    public void Opening_the_window_while_the_reader_cannot_read_says_MCDU_disconnected_once(A330ReaderState state)
    {
        using var rig = new Rig();
        rig.Displays.SetState(state);
        var w = rig.Window();

        w.Show();
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);   // the non-interrupting announce
        Assert.Equal(new[] { A330ReaderStatus.Text(state) }, Items(w));
        Assert.Equal(A330ReaderStatus.Text(state), Status(w).Text);   // the reason is in the status box

        for (int i = 0; i < 5; i++) Tick(rig, w);
        Tick(rig, w, 30_000);
        Assert.Single(rig.Speech.All);

        // Each open says it once.
        w.Hide();
        w.Show();
        Assert.Equal(new[] { "MCDU disconnected", "MCDU disconnected" }, rig.Speech.All);
        Tick(rig, w);
        Assert.Equal(2, rig.Speech.All.Count);
    }

    [Theory]
    [InlineData(A330ReaderState.Starting)]
    [InlineData(A330ReaderState.SimulatorNotFound)]
    [InlineData(A330ReaderState.AircraftNotFound)]
    [InlineData(A330ReaderState.Stopped)]
    public void A_state_that_passes_is_shown_and_never_spoken(A330ReaderState state)
    {
        using var rig = new Rig();
        rig.Displays.SetState(state);
        var w = rig.Window();
        w.Show();
        Tick(rig, w, 60_000);

        Assert.Equal(new[] { A330ReaderStatus.Text(state) }, Items(w));
        Assert.Equal(A330ReaderStatus.Text(state), Status(w).Text);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_disconnection_is_said_once_whatever_its_reason_until_a_page_shows()
    {
        using var rig = new Rig();
        rig.Displays.SetState(A330ReaderState.NotInstalled);
        var w = rig.Window();
        w.Show();
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);

        // Another reason, a state that passes, another reason, and 30 s running with no page: still disconnected, said
        // once (the status box shows each reason).
        foreach (var state in new[] { A330ReaderState.Failed, A330ReaderState.Starting, A330ReaderState.Failed, A330ReaderState.OutOfDate })
        {
            rig.Displays.SetState(state);
            Tick(rig, w);
            Assert.Equal(A330ReaderStatus.Text(state), Status(w).Text);
        }
        rig.Displays.SetState(A330ReaderState.Running);
        Tick(rig, w);
        Tick(rig, w, 30_000);
        Assert.Equal("The display reader cannot read the captain's MCDU", Status(w).Text);
        Assert.Single(rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void When_the_reader_reads_again_the_window_says_MCDU_connected_once()
    {
        using var rig = new Rig();
        rig.Displays.SetState(A330ReaderState.Failed);
        var w = rig.Window();
        w.Show();
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);

        // Reading, but the hidden rows are not left out yet: no page, so not connected.
        rig.Displays.SetState(A330ReaderState.Running);
        rig.Displays.Show(Init() with { HiddenRowsLeftOut = false });
        Tick(rig, w);
        Assert.Single(rig.Speech.All);

        // The page: "MCDU connected", then its title, as on the FlyByWire window.
        rig.Displays.Show(Init());
        Tick(rig, w);
        Assert.Equal(new[] { "MCDU disconnected", "MCDU connected", InitSaid }, rig.Speech.All);
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(3, rig.Speech.All.Count);

        // The frames stop for a moment and come back on the same page: nothing.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w);
        rig.Displays.Show(Init());
        Tick(rig, w);
        Assert.Equal(3, rig.Speech.All.Count);

        // After the 30 s "cannot read": disconnected, then connected again with the same page (its title already said).
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w, 30_000);
        rig.Displays.Show(Init());
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(new[] { "MCDU disconnected", "MCDU connected", InitSaid, "MCDU disconnected", "MCDU connected" }, rig.Speech.All);

        // A window opened on a readable page says its title only.
        using var ready = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var r = ready.Window();
        r.Show();
        Tick(ready, r);
        Assert.Equal(new[] { InitSaid }, ready.Speech.All);
    }

    // ---- Keys and typing ---------------------------------------------------------------------------------

    [Fact]
    public async Task Keys_are_still_sent_while_the_page_cannot_be_read_and_an_entry_says_nothing_of_its_own()
    {
        using var rig = new Rig();
        rig.Displays.SetState(A330ReaderState.NotInstalled);
        var w = rig.Window();
        w.Show();

        Key(w, Keys.Alt | Keys.I);          // INIT
        Key(w, Keys.Control | Keys.D1);     // the first left line key
        Key(w, Keys.Alt | Keys.D6);         // the sixth right line key
        Key(w, Keys.PageDown);              // slew down
        await w.TypeAsync("k1.");

        Assert.Equal(new[]
        {
            "INI_MCDU1_INIT", "INI_MCDU1_LSK1L", "INI_MCDU1_LSK6R", "INI_MCDU1_DOWN",
            "INI_MCDU1_K", "INI_MCDU1_1", "INI_MCDU1_DECIMAL",
        }, rig.Sent.Select(s => s.Flag));
        for (int i = 1; i < rig.Sent.Count; i++) Assert.True(rig.Sent[i].At - rig.Sent[i - 1].At >= 150, $"key {i} came too soon");
        Assert.Equal(new[] { "MCDU disconnected" }, rig.Speech.All);   // nothing for the entry, as on the other MCDU windows

        // A page shown, and still no typed line from the reader: sent at today's pace.
        using var shown = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var s = shown.Window();
        Open(shown, s);
        await s.TypeAsync("ab");
        Assert.Equal(new[] { "INI_MCDU1_A", "INI_MCDU1_B" }, shown.Sent.Select(x => x.Flag));
        Assert.Equal(150, shown.Sent[1].At - shown.Sent[0].At);
    }

    [Fact]
    public async Task With_a_page_shown_and_no_typed_line_the_pages_scratchpad_line_confirms_the_entry()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        Open(rig, w);

        // No sentence of the window's own: the page's scratchpad line, read back once it settles, is the confirmation.
        await w.TypeAsync("ab");
        Assert.Empty(rig.Speech.All);
        rig.Displays.Show(WithPad(Init(), "AB"));
        for (int i = 0; i < 6; i++) Tick(rig, w);
        Assert.Equal(new[] { "AB" }, rig.Speech.All);

        // No page shown: nothing can confirm it, and nothing is said, as the other windows say nothing with no screen.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w);
        await w.TypeAsync("c");
        Assert.Equal(new[] { "AB" }, rig.Speech.All);
        Assert.Equal("INI_MCDU1_C", rig.Sent[^1].Flag);
    }

    /// <summary>A window on INIT with an empty scratchpad line, whose typed line follows the keys.</summary>
    private static (Rig Rig, IniA330CduForm Window) TypingOnInit()
    {
        var rig = new Rig { TypedLine = true };
        rig.Displays.Show(WithPad(Init(), ""));
        rig.Displays.SetScratchpad(false, "");
        var w = rig.Window();
        Open(rig, w);
        return (rig, w);
    }

    [Fact]
    public async Task A_message_after_a_typed_entry_and_a_line_key_is_said_once()
    {
        var (rig, w) = TypingOnInit();
        using var _ = rig;
        await w.TypeAsync("kjfk");
        Assert.Equal(new[] { "KJFK" }, rig.Speech.All);

        // At once the pilot presses the line key; the MCDU refuses the entry, and its message reaches the scratchpad
        // line well within 750 ms of the entry.
        Key(w, Keys.Control | Keys.D1);
        rig.Displays.Show(WithPad(Init(), "FORMAT ERROR"));
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(new[] { "KJFK", "FORMAT ERROR" }, rig.Speech.All);
        for (int i = 0; i < 6; i++) Tick(rig, w);
        Assert.Equal(2, rig.Speech.All.Count);
    }

    [Fact]
    public async Task Backspace_after_a_typed_entry_says_what_is_left()
    {
        var (rig, w) = TypingOnInit();
        using var _ = rig;
        await w.TypeAsync("kjfk");

        // Backspace in the emptied entry box, straight after the entry: CLR, and what is left is said.
        w.HandleScratchpadKey(new KeyEventArgs(Keys.Back));
        Assert.Equal("INI_MCDU1_CLR", rig.Sent[^1].Flag);
        rig.Displays.Show(WithPad(Init(), "KJF"));
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { "KJFK", "KJF" }, rig.Speech.All);
    }

    [Fact]
    public async Task An_entry_straight_after_CLR_starts_from_what_CLR_left_on_the_typed_line()
    {
        var (rig, w) = TypingOnInit();
        using var _ = rig;
        await w.TypeAsync("kjfk");
        Assert.Equal(new[] { "KJFK" }, rig.Speech.All);

        // CLR, whose change reaches the typed line 100 ms after its press, and at once an entry: the entry starts from
        // the typed line CLR left, so its key is not missed.
        rig.LagMs = 100;
        w.HandleScratchpadKey(new KeyEventArgs(Keys.Back));
        rig.LagMs = 40;
        await w.TypeAsync("a");
        Assert.Equal(new[] { "INI_MCDU1_CLR", "INI_MCDU1_A" }, rig.Sent.Skip(4).Select(s => s.Flag));
        Assert.True(rig.Sent[^1].At - rig.Sent[^2].At >= 150, "the entry's key came too soon after CLR");
        Assert.DoesNotContain(rig.Speech.All, s => s.StartsWith("Missed", StringComparison.Ordinal));
        Assert.Equal(new[] { "KJFK", "KJFA" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_typed_entry_the_page_catches_up_with_says_nothing_more()
    {
        var (rig, w) = TypingOnInit();
        using var _ = rig;
        await w.TypeAsync("kjfk");

        // The page's frames lag the typed line: its scratchpad line shows nothing, then part of the entry, then all of it.
        Tick(rig, w);
        rig.Displays.Show(WithPad(Init(), "KJ"));
        Tick(rig, w);
        Tick(rig, w);
        rig.Displays.Show(WithPad(Init(), "KJFK"));
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { "KJFK" }, rig.Speech.All);

        // What arrives after it, untyped, is said.
        rig.Displays.Show(WithPad(Init(), "NAV ACCUR DOWNGRADED"));
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(new[] { "KJFK", "NAV ACCUR DOWNGRADED" }, rig.Speech.All);
    }

    [Fact]
    public void Arriving_on_and_leaving_the_F_PLN_says_nothing_about_the_scratchpad()
    {
        var empty = WithPad(Init(), "");
        using var rig = new Rig();
        rig.Displays.Show(empty);
        var w = rig.Window();
        Open(rig, w);

        rig.Displays.Show(Fixture("p03_fpln.json"));
        for (int i = 0; i < 4; i++) Tick(rig, w);
        string fplnTitle = TitleOf(w);
        Assert.EndsWith("\U00002191\U00002193", Items(w)[^1]);   // the slew arrows are on the screen, and listed

        rig.Displays.Show(empty);
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { Said(fplnTitle), InitSaid }, rig.Speech.All);   // the two titles, nothing about the scratchpad
    }

    /// <summary>Waits that complete only when the test runs them, one at a time, each moving the clock by its length.</summary>
    private sealed class HeldWaits
    {
        private readonly Rig _rig;
        private readonly Queue<(int Ms, TaskCompletionSource Done)> _waits = new();

        public HeldWaits(Rig rig, IniA330CduForm w)
        {
            _rig = rig;
            w.Delay = ms =>
            {
                var done = new TaskCompletionSource();
                _waits.Enqueue((ms, done));
                return done.Task;
            };
        }

        private void RunOne()
        {
            var (ms, done) = _waits.Dequeue();
            _rig.Advance(ms);
            done.SetResult();
        }

        public void RunUntil(Func<bool> reached)
        {
            while (!reached() && _waits.Count > 0) RunOne();
            Assert.True(reached());
        }

        public void RunAll()
        {
            while (_waits.Count > 0) RunOne();
        }
    }

    [Fact]
    public void Hiding_closing_disposing_or_switching_sides_stops_an_entry_in_progress()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json")) { TypedLine = true };
        rig.Displays.SetScratchpad(false, "");
        var w = rig.Window();
        var waits = new HeldWaits(rig, w);
        Open(rig, w);
        string[] Flags() => rig.Sent.Select(s => s.Flag).ToArray();

        // Hidden while the entry waits for a key to show: the rest is not sent, and nothing is said for it.
        var entry = w.TypeAsync("kjfk");
        waits.RunUntil(() => rig.Sent.Count == 2);
        Assert.False(entry.IsCompleted);
        w.Hide();
        waits.RunAll();
        Assert.True(entry.IsCompleted);
        Assert.Equal(new[] { "INI_MCDU1_K", "INI_MCDU1_J" }, Flags());
        Assert.Empty(rig.Speech.All);

        // Closed while a key that never shows is waited for: nothing from a closed window. (Each part starts
        // once the keys already sent have shown, on a typed line emptied again.)
        w.Show();
        rig.Advance(1000);
        rig.Dropped.Add("B");
        rig.Displays.SetScratchpad(false, "");
        rig.Sent.Clear();
        entry = w.TypeAsync("abc");
        waits.RunUntil(() => rig.Sent.Count == 2);
        w.Close();
        waits.RunAll();
        Assert.True(entry.IsCompleted);
        Assert.Equal(new[] { "INI_MCDU1_A", "INI_MCDU1_B" }, Flags());
        Assert.Empty(rig.Speech.All);

        // A side switch mid-entry: the rest goes to neither side.
        w.Show();
        rig.Advance(1000);
        rig.Dropped.Clear();
        rig.Displays.SetScratchpad(false, "");
        rig.Sent.Clear();
        entry = w.TypeAsync("xyz");
        waits.RunUntil(() => rig.Sent.Count == 1);
        Key(w, Keys.Control | Keys.Shift | Keys.R);
        waits.RunAll();
        Assert.True(entry.IsCompleted);
        Assert.Equal(new[] { "INI_MCDU1_X" }, Flags());
        Assert.Equal(new[] { "Right MCDU" }, rig.Speech.All);

        // Disposed mid-entry: nothing more.
        rig.Advance(1000);
        rig.Displays.SetScratchpad(true, "");
        rig.Sent.Clear();
        entry = w.TypeAsync("12");
        waits.RunUntil(() => rig.Sent.Count == 1);
        w.Dispose();
        waits.RunAll();
        Assert.True(entry.IsCompleted);
        Assert.Equal(new[] { "INI_MCDU2_1" }, Flags());
        Assert.Equal(new[] { "Right MCDU" }, rig.Speech.All);
    }

    [Fact]
    public async Task Typed_text_is_confirmed_from_the_MCDUs_own_scratchpad()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json")) { TypedLine = true };
        rig.Displays.SetScratchpad(false, "");
        var w = rig.Window();
        Open(rig, w);

        await w.TypeAsync("kjfk");
        Assert.Equal(new[] { "INI_MCDU1_K", "INI_MCDU1_J", "INI_MCDU1_F", "INI_MCDU1_K" }, rig.Sent.Select(s => s.Flag));
        for (int i = 1; i < rig.Sent.Count; i++) Assert.True(rig.Sent[i].At - rig.Sent[i - 1].At >= 150, $"key {i} came too soon");
        Assert.Equal(new[] { "KJFK" }, rig.Speech.All);   // the scratchpad, as the MCDU shows it

        // A slow MCDU: a key is never sent before the one before it has shown.
        rig.LagMs = 400;
        rig.Displays.SetScratchpad(false, "");
        rig.Sent.Clear();
        await w.TypeAsync("ab");
        Assert.True(rig.Sent[1].At - rig.Sent[0].At >= 400);
        Assert.Equal("AB", rig.Speech.All[^1]);

        // A key that never shows in 600 ms stops the entry: the rest is not sent, and the typed line is said as it stands
        // (what the other windows' scratchpad read-back would say).
        rig.LagMs = 40;
        rig.Dropped.Add("F");
        rig.Displays.SetScratchpad(false, "");
        rig.Sent.Clear();
        await w.TypeAsync("kjfk");
        Assert.Equal(new[] { "INI_MCDU1_K", "INI_MCDU1_J", "INI_MCDU1_F" }, rig.Sent.Select(s => s.Flag));
        Assert.Equal("KJ", rig.Speech.All[^1]);
        Assert.InRange(rig.Clock - rig.Sent[^1].At, 600, 650);

        // On an empty scratchpad: nothing to say.
        rig.Dropped.Add("A");
        rig.Displays.SetScratchpad(false, "");
        await w.TypeAsync("a");
        for (int i = 0; i < 4; i++) Tick(rig, w);   // the page, unchanged: nothing
        Assert.Equal(3, rig.Speech.All.Count);

        // On what the scratchpad already held, and the +/- key: a plus is the key pressed twice.
        rig.Dropped.Clear();
        rig.Displays.SetScratchpad(false, "FL");
        await w.TypeAsync("350");
        Assert.Equal("FL350", rig.Speech.All[^1]);
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(4, rig.Speech.All.Count);
        rig.Displays.SetScratchpad(false, "");
        rig.Sent.Clear();
        await w.TypeAsync("+5");
        Assert.Equal(new[] { "INI_MCDU1_PLUS", "INI_MCDU1_PLUS", "INI_MCDU1_5" }, rig.Sent.Select(s => s.Flag));
        Assert.Equal("+5", rig.Speech.All[^1]);
        Assert.Equal(new[] { "KJFK", "AB", "KJ", "FL350", "+5" }, rig.Speech.All);
        Assert.DoesNotContain(rig.Speech.All, s => s.StartsWith("Missed", StringComparison.Ordinal));

        // The page's scratchpad line then showing what was typed says nothing more; what the pilot did not type is said.
        int said = rig.Speech.All.Count;
        rig.Displays.Show(WithPad(Init(), "+5"));
        for (int i = 0; i < 6; i++) Tick(rig, w);
        Assert.Equal(said, rig.Speech.All.Count);
        rig.Displays.Show(WithPad(Init(), "ENTRY OUT OF RANGE"));
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal("ENTRY OUT OF RANGE", rig.Speech.All[^1]);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void A_new_title_is_said_and_the_selection_goes_to_the_first_line_after_it()
    {
        // The Fenix and FlyByWire windows' rule: a title that differs from the last one said is said, opening the window
        // included, and the selection goes to line 1 (the row after the title), which the screen reader reads.
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        w.Show();
        Assert.Equal(new[] { InitSaid }, rig.Speech.All);   // the title's words, without the list's prefix and the arrows
        Assert.Equal(1, List(w).SelectedIndex);

        List(w).SelectedIndex = 6;
        rig.Displays.Show(Menu());
        Tick(rig, w);
        Assert.Equal(new[] { InitSaid, "MCDU MENU" }, rig.Speech.All);
        Assert.Equal("Title: MCDU MENU", Items(w)[0]);
        Assert.Equal(1, List(w).SelectedIndex);

        // The same title: nothing, and the pilot keeps their row.
        List(w).SelectedIndex = 2;
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(2, rig.Speech.All.Count);
        Assert.Equal(2, List(w).SelectedIndex);

        // A temporary flight plan: its title as drawn, TMPY in it, and nothing more.
        var tmpy = WithPad(WithText(Fixture("p03_fpln.json"), 9, " FROM TMPY"), "NAV ACCUR DOWNGRADED");
        rig.Displays.Show(tmpy);
        Tick(rig, w);
        string title = Said(TitleOf(w));
        Assert.Contains("TMPY", title);
        Assert.Equal(new[] { InitSaid, "MCDU MENU", title }, rig.Speech.All);

        // A gap in the frames and the same page back: not a new title.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        Tick(rig, w);
        rig.Displays.Show(tmpy);
        Tick(rig, w);
        Assert.Equal(3, rig.Speech.All.Count);

        // Closed and opened on the same page: nothing; on a page that changed meanwhile: its title.
        w.Hide();
        w.Show();
        Assert.Equal(3, rig.Speech.All.Count);
        w.Hide();
        rig.Displays.Show(Menu());
        w.Show();
        Assert.Equal("MCDU MENU", rig.Speech.All[^1]);
        Assert.Equal(4, rig.Speech.All.Count);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void Only_the_titles_words_tell_a_new_page_not_its_page_counter_or_arrows()
    {
        // The FlyByWire window says its title apart from its page number and arrows: a refresh that changes only those
        // says nothing (owner, 2026-11-23: a refresh read part of the title again).
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_data1.json"));
        var w = rig.Window();
        w.Show();
        Assert.Equal("Title: DATA INDEX   1/2 ←→", Items(w)[0]);
        Assert.Equal(new[] { "DATA INDEX" }, rig.Speech.All);

        // Page 2 of 2: the listed title line follows the counter, and nothing is said.
        List(w).SelectedIndex = 3;
        rig.Displays.Show(Fixture("p03_data2.json"));
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal("Title: DATA INDEX   2/2 ←→", Items(w)[0]);
        Assert.Single(rig.Speech.All);
        Assert.Equal(3, List(w).SelectedIndex);

        // The arrows gone from the title row: nothing either.
        var noArrows = Fixture("p03_data2.json");
        noArrows = noArrows with { Texts = noArrows.Texts.Where(t => !(A330McduGeometry.RowOf(t.Baseline) == 0 && t.Text.Trim() == "ab")).ToList() };
        rig.Displays.Show(noArrows);
        Tick(rig, w);
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public void Left_and_Right_on_the_screen_turn_the_page_as_on_the_Fenix_and_FlyByWire_windows()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        bool focused = true;
        w.ScreenHasFocus = () => focused;
        Open(rig, w);

        Key(w, Keys.Right);
        Key(w, Keys.Left);
        Key(w, Keys.Alt | Keys.Right);
        Key(w, Keys.Alt | Keys.Left);
        Assert.Equal(new[] { "INI_MCDU1_NEXT", "INI_MCDU1_PREV", "INI_MCDU1_NEXT", "INI_MCDU1_PREV" }, rig.Sent.Select(s => s.Flag));

        // Elsewhere in the window (the scratchpad entry box, the selector) Left and Right are that control's own.
        focused = false;
        rig.Sent.Clear();
        Key(w, Keys.Right);
        Key(w, Keys.Left);
        Assert.Empty(rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_line_key_says_nothing_of_its_own()
    {
        // As on the Fenix, FlyByWire, PMDG and MD-11 windows: the pilot reads the list; a new title and the scratchpad
        // line say what they always say.
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var init = Init();
        var w = rig.Window();
        Open(rig, w);

        // The pressed line's data row changes, or its label row: nothing.
        Key(w, Keys.Control | Keys.D3);
        Assert.Equal("INI_MCDU1_LSK3L", rig.Sent[^1].Flag);
        var named = WithText(init, 13, "MSF773");
        rig.Displays.Show(named);
        Tick(rig, w);
        Tick(rig, w, 2000);
        Assert.Contains("3: MSF773         IRS INIT>", Items(w));
        Key(w, Keys.Alt | Keys.D3);
        rig.Displays.Show(WithText(named, 12, "FLT NBR/CALLSIGN"));
        Tick(rig, w);
        Tick(rig, w, 2000);

        // Nothing changes at all: nothing, never "No change".
        Key(w, Keys.Control | Keys.D2);
        Tick(rig, w, 1500);
        Tick(rig, w, 2000);
        Assert.Empty(rig.Speech.All);

        // A new page: its title. The scratchpad line changing: its read-back.
        Key(w, Keys.Alt | Keys.D6);
        rig.Displays.Show(Menu());
        Tick(rig, w);
        Key(w, Keys.Control | Keys.D1);
        rig.Displays.Show(WithPad(Menu(), "NOT ALLOWED"));
        Tick(rig, w);
        Tick(rig, w);
        Tick(rig, w, 2000);
        Assert.Equal(new[] { "MCDU MENU", "NOT ALLOWED" }, rig.Speech.All);
        Assert.Equal(new[] { "INI_MCDU1_LSK3L", "INI_MCDU1_LSK3R", "INI_MCDU1_LSK2L", "INI_MCDU1_LSK6R", "INI_MCDU1_LSK1L" },
            rig.Sent.Select(s => s.Flag));
        Assert.Empty(rig.Speech.Interrupts);
    }

    // ---- Sides -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Ctrl_Shift_R_and_Ctrl_Shift_L_switch_sides()
    {
        var replay = A330ReplayDisplays.FromFixtures("p03_init.json");
        replay.Show(Menu() with { Gauge = A330Gauge.McduFirstOfficer });
        using var rig = new Rig(replay);
        var w = rig.Window();
        Open(rig, w);

        Assert.Equal("A330 MCDU (Left)", w.Text);
        Assert.Equal(new[] { "Left", "Right" }, Side(w).Items.Cast<string>());
        Assert.Equal(0, Side(w).SelectedIndex);
        Assert.Equal("Title: " + InitTitle, Items(w)[0]);

        Key(w, Keys.Control | Keys.Shift | Keys.R);
        Assert.Equal("A330 MCDU (Right)", w.Text);
        Assert.Equal(1, Side(w).SelectedIndex);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU" }, rig.Speech.All);   // the side, then the new side's page title
        Assert.Equal((A330Gauge.McduCaptain, (object)w), rig.Displays.Releases[^1]);
        Assert.Equal((A330Gauge.McduFirstOfficer, (object)w, 250), rig.Displays.Wants[^1]);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));
        Assert.Equal("Title: MCDU MENU", Items(w)[0]);
        Assert.Equal("MCDU: Connected", Status(w).Text);
        Tick(rig, w);
        // and its scratchpad, which holds a message, once its line has read the same twice
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU", "NAV ACCUR DOWNGRADED" }, rig.Speech.All);
        Tick(rig, w);
        Tick(rig, w);
        Assert.Equal(3, rig.Speech.All.Count);

        Key(w, Keys.Alt | Keys.I);
        Assert.Equal("INI_MCDU2_INIT", rig.Sent[^1].Flag);

        // The side already shown: said again (off the selector), and nothing else happens.
        int wants = rig.Displays.Wants.Count;
        Key(w, Keys.Control | Keys.Shift | Keys.R);
        Assert.Equal("Right MCDU", rig.Speech.All[^1]);
        Assert.Equal(4, rig.Speech.All.Count);
        Assert.Equal(wants, rig.Displays.Wants.Count);

        // Typing reads the first officer's typed line.
        rig.TypedLine = true;
        replay.SetScratchpad(true, "");
        replay.SetScratchpad(false, "CAPTAIN");
        await w.TypeAsync("1");
        Assert.Equal("INI_MCDU2_1", rig.Sent[^1].Flag);
        Assert.Equal("1", rig.Speech.All[^1]);

        // Back to the left.
        Key(w, Keys.Control | Keys.Shift | Keys.L);
        Assert.Equal("A330 MCDU (Left)", w.Text);
        Assert.Equal(new[] { "Left MCDU", InitSaid }, rig.Speech.All.TakeLast(2));
        Assert.Equal((A330Gauge.McduFirstOfficer, (object)w), rig.Displays.Releases[^1]);
        Assert.Equal((A330Gauge.McduCaptain, (object)w, 250), rig.Displays.Wants[^1]);
        Assert.Equal("Title: " + InitTitle, Items(w)[0]);
        Key(w, Keys.Control | Keys.D1);
        Assert.Equal("INI_MCDU1_LSK1L", rig.Sent[^1].Flag);

        // A choice in the selector switches too.
        Side(w).SelectedIndex = 1;
        Assert.Equal("A330 MCDU (Right)", w.Text);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU" }, rig.Speech.All.TakeLast(2));
        Assert.Equal(250, rig.Displays.RefreshWanted(A330Gauge.McduFirstOfficer, w));
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));

        // A window opened on the right starts there.
        var right = rig.Window(firstOfficer: true);
        Assert.Equal("A330 MCDU (Right)", right.Text);
        Assert.Equal(1, Side(right).SelectedIndex);
    }

    [Fact]
    public void A_side_switch_says_the_new_sides_page_title_and_a_waiting_scratchpad_once()
    {
        // The Fenix window's side switch: the side (off the selector), then the new side's page title, and its
        // scratchpad when it holds something; the title through the window's own title path.
        var replay = A330ReplayDisplays.FromFixtures("p03_init.json");   // the captain's: INIT, NAV ACCUR DOWNGRADED
        replay.Show(WithPad(Fixture("p03_menu.json"), "") with { Gauge = A330Gauge.McduFirstOfficer });
        using var rig = new Rig(replay);
        var w = rig.Window();
        Open(rig, w);

        // An empty scratchpad: the side and the title only.
        Key(w, Keys.Control | Keys.Shift | Keys.R);
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU" }, rig.Speech.All);

        // A scratchpad that holds a message: said once its line has read the same twice, and once only.
        Key(w, Keys.Control | Keys.Shift | Keys.L);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU", "Left MCDU", InitSaid }, rig.Speech.All);
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU", "Left MCDU", InitSaid, "NAV ACCUR DOWNGRADED" }, rig.Speech.All);

        // The new side's page arriving after a gap: its title and its scratchpad when it comes, once.
        replay.Forget(A330Gauge.McduFirstOfficer);
        Key(w, Keys.Control | Keys.Shift | Keys.R);
        Tick(rig, w);
        Assert.Equal("Right MCDU", rig.Speech.All[^1]);
        replay.Show(WithPad(Fixture("p03_menu.json"), "CHECK T.O DATA") with { Gauge = A330Gauge.McduFirstOfficer });
        for (int i = 0; i < 4; i++) Tick(rig, w);
        Assert.Equal(new[] { "Right MCDU", "MCDU MENU", "CHECK T.O DATA" }, rig.Speech.All.Skip(5));

        // On the list as well: the side and the title, and the selection goes to line 1, as on the Fenix window.
        w.ScreenHasFocus = () => true;
        List(w).SelectedIndex = 4;
        Key(w, Keys.Control | Keys.Shift | Keys.L);
        Assert.Equal(new[] { "Left MCDU", InitSaid }, rig.Speech.All.TakeLast(2));
        Assert.Equal(10, rig.Speech.All.Count);
        Assert.Equal(1, List(w).SelectedIndex);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void The_AIRPORT_key_says_nothing_of_its_own()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var fpln = WithPad(Fixture("p03_fpln1.json"), "NAV ACCUR DOWNGRADED");   // KATL26L first; INIT's scratchpad line
        var w = rig.Window();
        Open(rig, w);

        // From INIT (Alt+A): the F-PLN it opens says its title, as any new page does, and nothing more.
        Key(w, Keys.Alt | Keys.A);
        Assert.Equal("INI_MCDU1_AIRPORT", rig.Sent[^1].Flag);
        rig.Displays.Show(fpln);
        Tick(rig, w);
        Tick(rig, w, 2000);
        Assert.Equal(new[] { Said(TitleOf(w)) }, rig.Speech.All);

        // On the F-PLN (the button): the plan moved to the destination, the same title: nothing.
        PageKey(w, "&AIRPORT").PerformClick();
        Assert.Equal("INI_MCDU1_AIRPORT", rig.Sent[^1].Flag);
        rig.Displays.Show(WithText(fpln, 12, "KMCO17L"));
        Tick(rig, w);
        Tick(rig, w, 2000);
        Assert.Contains(Items(w), r => r.Contains("KMCO17L"));
        Assert.Single(rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);
    }

    [Fact]
    public void The_status_row_is_first_in_the_tab_order_then_the_side_then_the_screen()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json"));
        var w = rig.Window();
        w.Show();

        var stops = new List<Control>();
        Control? c = null;
        while ((c = w.GetNextControl(c, true)) != null)
            if (c.TabStop && c is TextBox or ComboBox or ListBox or Button) stops.Add(c);

        Assert.Same(Status(w), stops[0]);
        Assert.Same(Side(w), stops[1]);
        Assert.Same(List(w), stops[2]);
        Assert.True(Status(w).ReadOnly);
        // The Fenix and FlyByWire windows' names.
        Assert.Equal("Connection status", Status(w).AccessibleName);
        Assert.Equal("MCDU selector", Side(w).AccessibleName);
        Assert.Equal("MCDU Display", List(w).AccessibleName);
        Assert.Equal("MCDU Input", w.Controls.Find("Scratchpad", true).OfType<TextBox>().Single().AccessibleName);
        Assert.Equal(ComboBoxStyle.DropDownList, Side(w).DropDownStyle);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void What_the_window_shows_and_says_is_the_same_under_a_comma_decimal_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            using var rig = new Rig(A330ReplayDisplays.FromFixtures("p03_init.json")) { TypedLine = true };
            rig.Displays.SetScratchpad(false, "");
            var w = rig.Window();
            w.Show();
            Assert.Equal(Listing(Init()), Items(w));
            Assert.Equal("6: ---- /---\U000000B0         36090", Items(w)[^2]);

            w.TypeAsync("1.5").GetAwaiter().GetResult();
            Assert.Equal("1.5", rig.Speech.All[^1]);

            Key(w, Keys.Control | Keys.D5);
            rig.Displays.Show(WithText(Init(), 15, "2.5"));
            Tick(rig, w);
            Assert.Contains("5: 2.5                WIND>", Items(w));
            Assert.Equal("1.5", rig.Speech.All[^1]);

            rig.Displays.Show(WithPad(Init(), "2.5"));
            Tick(rig, w);
            Tick(rig, w);
            Assert.Equal("2.5", rig.Speech.All[^1]);

            rig.Displays.Forget(A330Gauge.McduCaptain);
            Tick(rig, w, 30_000);
            Assert.Equal("MCDU disconnected", rig.Speech.All[^1]);
            return 0;
        });
    }
}
