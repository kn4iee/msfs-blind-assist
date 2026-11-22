// The A330's live display window, the base of every A330 display window (MCDU, E/WD, lower ECAM, PFD, ND, ISIS, DCDU),
// on the replay source: its list, its status rows, the displays it wants, its cockpit controls under the list, its keys,
// and its silence. Each window is a real WinForms form, shown far off the screen and without taking focus (the test
// window's ShowWithoutActivation), so a button is clickable as the pilot's is. The cockpit controls come from a map the
// test builds itself, never the generated control map's labels, and every wording is written out here.

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

public class A330DisplayWindowTests
{
    private const string SelectorKey = "TEST_SELECTOR";
    private const string ModeKey = "TEST_MODE";
    private const string PushKey = "TEST_PUSH";
    private const string Title = "Test display";
    private const string ListName = "Test display list";

    private static A330ControlMap Map() => new()
    {
        Controls =
        {
            new A330ControlMap.Control
            {
                Key = SelectorKey, Section = "Test section", Panel = "Test panel", Label = "Test selector", Kind = "select",
                Event = "AIRLINER_TEST_SELECTOR", Mode = "set", Read = "L:INI_TEST_SELECTOR",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 1, Label = "Off" },
                    new A330ControlMap.Position { Value = 1, Input = 0, Label = "On" },
                },
            },
            new A330ControlMap.Control
            {
                Key = ModeKey, Section = "Test section", Panel = "Test panel", Label = "Test mode", Kind = "select",
                Event = "AIRLINER_TEST_MODE", Mode = "set", Read = "L:INI_TEST_MODE",
                Positions = new()
                {
                    new A330ControlMap.Position { Value = 0, Input = 0, Label = "Left" },
                    new A330ControlMap.Position { Value = 1, Input = 1, Label = "Centre" },
                    new A330ControlMap.Position { Value = 2, Input = 2, Label = "Right" },
                },
            },
            new A330ControlMap.Control
            {
                Key = PushKey, Section = "Test section", Panel = "Test panel", Label = "Test key", Kind = "button",
                Event = "AIRLINER_TEST_PUSH", Mode = "push",
            },
        },
    };

    /// <summary>The definition over the test's map, with every panel write recorded and answered: the real write needs the
    /// SimConnect SDK's native library, which the test run does not have, and the window's press only has to reach it.</summary>
    private sealed class Recording : IniA330Definition
    {
        public Recording() : base(Map()) { }

        public List<(string Key, double Value, SimVarDefinition Def)> Sets { get; } = new();

        public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
            SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        {
            Sets.Add((varKey, value, varDef));
            return true;
        }
    }

    /// <summary>A display window of the tests: each text of the frame, trimmed, in the frame's order. Its keys and its
    /// waiting rows are set from its own constructor, after the base's, as a real window's are.</summary>
    private sealed class TestWindow : A330DisplayWindow
    {
        private readonly IReadOnlyList<A330KeyRow> _keys;
        private readonly bool _colours;
        private readonly bool _listRows;

        public TestWindow(Rig rig, A330Gauge gauge, IReadOnlyList<A330KeyRow>? keys, int refreshMs, bool colours, bool listRows)
            : base(Title, ListName, rig.Def, rig.Sim, rig.Speech, gauge, refreshMs)
        {
            _keys = keys ?? [];
            _colours = colours;
            _listRows = listRows;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
        }

        /// <summary>When set, the rows instead of each text trimmed.</summary>
        public Func<A330Frame, IReadOnlyList<string>>? Compose;

        protected override IReadOnlyList<string> Rows(A330Frame frame) =>
            Compose?.Invoke(frame) ?? frame.Texts.Select(t => t.Text.Trim()).ToList();

        protected override IReadOnlyList<A330KeyRow> KeyRows => _keys;
        protected override bool NeedsColours => _colours;
        protected override bool NeedsHiddenRowsLeftOut => _listRows;

        // The tests never take the focus of whoever is at the computer.
        protected override bool ShowWithoutActivation => true;

        /// <summary>A key press as the message loop hands it to the window.</summary>
        public bool Press(Keys key, bool autoRepeat = false)
        {
            // WM_KEYDOWN; bit 30 of lParam marks an auto-repeat
            var msg = Message.Create(IntPtr.Zero, 0x0100, (IntPtr)(int)key, (IntPtr)(autoRepeat ? (1 << 30) | 1 : 1));
            return ProcessCmdKey(ref msg, key);
        }

        public DisplayListBox List() => Controls.Find(ListName, true).OfType<DisplayListBox>().Single();

        public string[] Items() => List().Items.Cast<string>().ToArray();

        public T Control<T>(string name) where T : Control => Controls.Find(name, true).OfType<T>().Single();
    }

    private sealed class Rig : IDisposable
    {
        private readonly List<TestWindow> _windows = new();

        public Rig(A330ReplayDisplays? displays = null, params (string Key, double Value)[] cache)
        {
            Displays = displays ?? new A330ReplayDisplays();
            Def.Displays = Displays;
            Sim = SeededSimConnectCache.With(cache);
        }

        public Recording Def { get; } = new();
        public SimConnectManager Sim { get; }
        public SpeechCapture Speech { get; } = new();
        public A330ReplayDisplays Displays { get; }
        public long Clock = 100_000;

        public TestWindow Window(A330Gauge gauge = A330Gauge.McduCaptain, IReadOnlyList<A330KeyRow>? keys = null,
            int refreshMs = 250, bool colours = false, bool listRows = false)
        {
            var w = new TestWindow(this, gauge, keys, refreshMs, colours, listRows) { NowMs = () => Clock };
            _windows.Add(w);
            return w;
        }

        /// <summary>The value SimConnect last delivered for <paramref name="key"/>.</summary>
        public void Cockpit(string key, double value) => SeededSimConnectCache.Cache(Sim)[key] = value;

        public void Dispose()
        {
            foreach (var w in _windows)
                if (!w.IsDisposed) w.Dispose();
        }
    }

    private static A330Frame Mcdu() => A330ReplayDisplays.FromFixtures("p02_mcdu_init.json").Latest(A330Gauge.McduCaptain)!;

    /// <summary>A frame of a display that draws nothing, read at <paramref name="readTicks"/> (the window's clock).</summary>
    private static A330Frame NotDrawn(A330Gauge gauge, long readTicks) =>
        new(gauge, [], Complete: false, Missing: 0, ReadTicks: readTicks, Drawn: false);

    private static readonly MethodInfo RaiseCommitted =
        typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>The pilot's choice in a list, as Windows Forms sees it: the selection moves (SelectedIndexChanged), then
    /// the pilot commits it (SelectionChangeCommitted: a click, Enter, an arrow in the closed list).</summary>
    private static void Choose(ComboBox combo, int index)
    {
        combo.SelectedIndex = index;
        RaiseCommitted.Invoke(combo, [EventArgs.Empty]);
    }

    private static A330Frame Partial(A330Gauge gauge) =>
        new(gauge, [new A330Text(1, 10, 20, "INIT", "F", 20, null, [10, 11, 12, 13])], Complete: false, Missing: 3, ReadTicks: 0);

    private static A330Frame WithoutColours(A330Frame frame) =>
        frame with { Texts = frame.Texts.Select(t => t with { Colour = null }).ToList() };

    // ---- The list ----------------------------------------------------------------------------------

    [Fact]
    public void The_window_lists_the_frames_rows_and_updates_in_place()
    {
        var replay = A330ReplayDisplays.FromFixtures("p02_mcdu_init.json");
        using var rig = new Rig(replay);
        var w = rig.Window();

        w.Show();

        var list = w.List();
        Assert.Equal(Title, w.Text);
        Assert.Equal(ListName, list.Name);
        Assert.Equal(ListName, list.AccessibleName);
        var frame = replay.Latest(A330Gauge.McduCaptain)!;
        Assert.Equal(frame.Texts.Select(t => t.Text.Trim()), w.Items());
        Assert.Equal(new[] { "INIT", "ab", "CO RTE", "FROM/TO" }, w.Items().Take(4));
        Assert.Equal(0, list.SelectedIndex);   // the reader has a current row to land on

        // The pilot is on row 4. The next frame changes row 2; every other text is the same, in new strings.
        list.SelectedIndex = 4;
        var before = w.Items();
        var texts = frame.Texts.Select((t, i) => t with { Text = i == 2 ? "CO RTE CHANGED" : new string(t.Text.AsSpan()) }).ToList();
        replay.Show(frame with { Texts = texts });
        w.RefreshNow();

        Assert.Equal(before.Length, list.Items.Count);
        Assert.Equal("CO RTE CHANGED", list.Items[2]);
        for (int i = 0; i < before.Length; i++)
            if (i != 2) Assert.Same(before[i], list.Items[i]);   // only the changed row was rewritten
        Assert.Equal(4, list.SelectedIndex);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_rows_composer_that_throws_keeps_the_rows_shown()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p02_mcdu_init.json"));
        var w = rig.Window();
        w.Show();
        var shown = w.Items();

        w.Compose = _ => throw new InvalidOperationException("injected");
        var thrown = Record.Exception(w.RefreshNow);

        Assert.Null(thrown);
        Assert.Equal(shown, w.Items());
        Assert.Empty(rig.Speech.All);
    }

    // ---- Wanting the display ----------------------------------------------------------------------

    [Fact]
    public void It_wants_its_gauge_while_visible_and_releases_it_when_hidden()
    {
        using var rig = new Rig();
        var w = rig.Window(A330Gauge.Ewd, refreshMs: 400);
        Assert.Empty(rig.Displays.Wants);   // built, not shown: nothing wanted

        w.Show();
        Assert.Equal(new[] { (A330Gauge.Ewd, (object)w, 400) }, rig.Displays.Wants);
        Assert.Equal(400, rig.Displays.RefreshWanted(A330Gauge.Ewd, w));

        w.Hide();
        Assert.Equal(new[] { (A330Gauge.Ewd, (object)w) }, rig.Displays.Releases);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.Ewd, w));

        w.Show();
        Assert.Equal(2, rig.Displays.Wants.Count);
        Assert.Equal(400, rig.Displays.RefreshWanted(A330Gauge.Ewd, w));

        w.Close();
        Assert.True(w.IsDisposed);
        Assert.Equal(2, rig.Displays.Releases.Count);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.Ewd, w));

        // A window disposed while shown lets its display go too, once.
        var other = rig.Window(A330Gauge.Isis);
        other.Show();
        Assert.Equal(250, rig.Displays.RefreshWanted(A330Gauge.Isis, other));
        other.Dispose();
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.Isis, other));
        Assert.Equal(rig.Displays.Wants.Count, rig.Displays.Releases.Count);
        Assert.Equal(rig.Displays.Wants.Select(x => (x.Gauge, x.Owner)), rig.Displays.Releases);
    }

    // ---- Status rows --------------------------------------------------------------------------------

    [Fact]
    public void The_status_row_shows_while_the_reader_is_not_running()
    {
        using var rig = new Rig();
        var w = rig.Window();
        rig.Displays.SetState(A330ReaderState.Starting);

        w.Show();
        Assert.Equal(new[] { "A330 displays starting" }, w.Items());

        rig.Displays.SetState(A330ReaderState.NotInstalled);
        w.RefreshNow();
        Assert.Equal(new[] { "A330 displays unavailable: the display reader is not installed" }, w.Items());

        foreach (var state in Enum.GetValues<A330ReaderState>().Where(s => s != A330ReaderState.Running))
        {
            rig.Displays.SetState(state);
            w.RefreshNow();
            Assert.Equal(new[] { A330ReaderStatus.Text(state) }, w.Items());
            Assert.NotEqual("", w.Items()[0]);
        }

        // The state comes before a frame that is not complete, drawn or not.
        rig.Displays.SetState(A330ReaderState.Starting);
        rig.Displays.Show(NotDrawn(A330Gauge.McduCaptain, rig.Clock));
        w.RefreshNow();
        Assert.Equal(new[] { "A330 displays starting" }, w.Items());
        rig.Displays.Show(Partial(A330Gauge.McduCaptain));
        w.RefreshNow();
        Assert.Equal(new[] { "A330 displays starting" }, w.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Reading_shows_while_running_with_no_complete_frame()
    {
        using var rig = new Rig();
        var w = rig.Window();

        w.Show();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());

        rig.Displays.Show(Partial(A330Gauge.McduCaptain));
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());

        // Another display's frame is not this one's.
        rig.Displays.Show(Mcdu() with { Gauge = A330Gauge.McduFirstOfficer });
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_display_with_no_frame_says_reading()
    {
        using var rig = new Rig();
        var w = rig.Window(A330Gauge.Ewd);

        w.Show();
        Assert.Equal(new[] { "Reading the E/WD" }, w.Items());

        rig.Clock += 29_999;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the E/WD" }, w.Items());
    }

    [Fact]
    public void After_30_seconds_with_no_frame_the_window_says_it_cannot_read_it()
    {
        using var rig = new Rig();
        var w = rig.Window();
        rig.Clock = 100_000;
        w.Show();

        rig.Clock = 129_999;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());

        rig.Clock = 130_000;
        w.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, w.Items());

        // A complete frame starts the count again: its rows show, and once it is gone the window reads anew.
        rig.Displays.Show(Mcdu());
        rig.Clock = 130_500;
        w.RefreshNow();
        Assert.Equal("INIT", w.Items()[0]);
        rig.Displays.Forget(A330Gauge.McduCaptain);
        rig.Clock = 160_499;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 160_500;
        w.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, w.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_count_starts_again_after_a_frame_that_is_not_drawn_a_start_or_a_new_open()
    {
        using var rig = new Rig();
        var w = rig.Window();
        rig.Clock = 100_000;
        w.Show();

        // Not drawn: the display is off, and the count starts from there.
        rig.Clock = 131_000;
        rig.Displays.Show(NotDrawn(A330Gauge.McduCaptain, rig.Clock));
        w.RefreshNow();
        Assert.Equal(new[] { "Captain's MCDU is off" }, w.Items());
        rig.Displays.Show(Partial(A330Gauge.McduCaptain));
        rig.Clock = 160_999;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 161_000;
        w.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, w.Items());

        // The reader starting again: the count runs from when it is Running, not from when the window opened.
        rig.Displays.SetState(A330ReaderState.Starting);
        rig.Clock = 162_000;
        w.RefreshNow();
        Assert.Equal(new[] { "A330 displays starting" }, w.Items());
        rig.Displays.SetState(A330ReaderState.Running);
        rig.Clock = 200_000;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 229_999;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 230_000;
        w.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, w.Items());

        // Hidden and shown again: the count runs from the new open.
        w.Hide();
        rig.Clock = 240_000;
        w.Show();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 269_999;
        w.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, w.Items());
        rig.Clock = 270_000;
        w.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, w.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_display_that_is_not_drawn_says_it_is_off()
    {
        using var rig = new Rig();
        long t0 = rig.Clock;
        rig.Displays.Show(NotDrawn(A330Gauge.McduCaptain, t0));
        rig.Displays.Show(NotDrawn(A330Gauge.Sd, t0));
        var mcdu = rig.Window();                                  // 250 ms: fresh for three refreshes, 750 ms
        var sd = rig.Window(A330Gauge.Sd, refreshMs: 1000);      // 1 s: fresh for 3 s

        mcdu.Show();
        sd.Show();

        Assert.Equal(new[] { "Captain's MCDU is off" }, mcdu.Items());
        Assert.Equal(new[] { "System display is off" }, sd.Items());

        // A frame is fresh for three of the window's own refreshes: three seconds for the 1 s window.
        rig.Clock = t0 + 3000;
        sd.RefreshNow();
        Assert.Equal(new[] { "System display is off" }, sd.Items());
        rig.Clock = t0 + 3001;
        sd.RefreshNow();
        Assert.Equal(new[] { "Reading the system display" }, sd.Items());

        // While such frames keep coming the display stays off, long after, and never "cannot read".
        for (int s = 1; s <= 120; s++)
        {
            rig.Clock = t0 + s * 1000;
            rig.Displays.Show(NotDrawn(A330Gauge.McduCaptain, rig.Clock));
            mcdu.RefreshNow();
            Assert.Equal(new[] { "Captain's MCDU is off" }, mcdu.Items());
        }

        // A frame older than three of the window's refreshes is no frame: the window reads, and 30 s after the last
        // fresh one it cannot read. The count does not start again from the old frame.
        long last = rig.Clock;
        rig.Clock = last + 750;
        mcdu.RefreshNow();
        Assert.Equal(new[] { "Captain's MCDU is off" }, mcdu.Items());
        rig.Clock = last + 751;
        mcdu.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, mcdu.Items());
        rig.Clock = last + 750 + 29_999;
        mcdu.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, mcdu.Items());
        rig.Clock = last + 750 + 30_000;
        mcdu.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, mcdu.Items());

        // Cold and dark the pilot opened the window and closed it; the aircraft is powered and the window opened again,
        // with only that old frame kept: it reads, never "off".
        mcdu.Close();
        var again = rig.Window();
        rig.Clock += 60_000;
        again.Show();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, again.Items());
        rig.Clock += 30_000;
        again.RefreshNow();
        Assert.Equal(new[] { "The display reader cannot read the captain's MCDU" }, again.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData(A330Gauge.McduCaptain, "Reading the captain's MCDU", "Captain's MCDU is off", "The display reader cannot read the captain's MCDU")]
    [InlineData(A330Gauge.McduFirstOfficer, "Reading the first officer's MCDU", "First officer's MCDU is off", "The display reader cannot read the first officer's MCDU")]
    [InlineData(A330Gauge.Ewd, "Reading the E/WD", "E/WD is off", "The display reader cannot read the E/WD")]
    [InlineData(A330Gauge.Sd, "Reading the system display", "System display is off", "The display reader cannot read the system display")]
    [InlineData(A330Gauge.PfdCaptain, "Reading the captain's PFD", "Captain's PFD is off", "The display reader cannot read the captain's PFD")]
    [InlineData(A330Gauge.PfdFirstOfficer, "Reading the first officer's PFD", "First officer's PFD is off", "The display reader cannot read the first officer's PFD")]
    [InlineData(A330Gauge.NdCaptain, "Reading the captain's ND", "Captain's ND is off", "The display reader cannot read the captain's ND")]
    [InlineData(A330Gauge.NdFirstOfficer, "Reading the first officer's ND", "First officer's ND is off", "The display reader cannot read the first officer's ND")]
    [InlineData(A330Gauge.DcduCaptain, "Reading the captain's DCDU", "Captain's DCDU is off", "The display reader cannot read the captain's DCDU")]
    [InlineData(A330Gauge.DcduFirstOfficer, "Reading the first officer's DCDU", "First officer's DCDU is off", "The display reader cannot read the first officer's DCDU")]
    [InlineData(A330Gauge.Isis, "Reading the ISIS", "ISIS is off", "The display reader cannot read the ISIS")]
    public void Each_display_is_named_in_its_rows(A330Gauge gauge, string reading, string off, string cannotRead)
    {
        using var rig = new Rig();
        var w = rig.Window(gauge);
        w.Show();
        Assert.Equal(new[] { reading }, w.Items());
        rig.Clock += 30_000;
        w.RefreshNow();
        Assert.Equal(new[] { cannotRead }, w.Items());
        rig.Displays.Show(NotDrawn(gauge, rig.Clock));
        w.RefreshNow();
        Assert.Equal(new[] { off }, w.Items());
    }

    [Fact]
    public void Every_display_has_a_name()
    {
        Assert.Equal("captain's MCDU", A330GaugeNames.Name(A330Gauge.McduCaptain));
        Assert.Equal("first officer's MCDU", A330GaugeNames.Name(A330Gauge.McduFirstOfficer));
        Assert.Equal("E/WD", A330GaugeNames.Name(A330Gauge.Ewd));
        Assert.Equal("system display", A330GaugeNames.Name(A330Gauge.Sd));
        Assert.Equal("captain's PFD", A330GaugeNames.Name(A330Gauge.PfdCaptain));
        Assert.Equal("first officer's PFD", A330GaugeNames.Name(A330Gauge.PfdFirstOfficer));
        Assert.Equal("captain's ND", A330GaugeNames.Name(A330Gauge.NdCaptain));
        Assert.Equal("first officer's ND", A330GaugeNames.Name(A330Gauge.NdFirstOfficer));
        Assert.Equal("captain's DCDU", A330GaugeNames.Name(A330Gauge.DcduCaptain));
        Assert.Equal("first officer's DCDU", A330GaugeNames.Name(A330Gauge.DcduFirstOfficer));
        Assert.Equal("ISIS", A330GaugeNames.Name(A330Gauge.Isis));
        Assert.Equal(11, Enum.GetValues<A330Gauge>().Select(A330GaugeNames.Name).Distinct().Count());
    }

    // ---- What a window waits for (spec M14, M15) --------------------------------------------------

    [Fact]
    public void A_window_needing_colours_says_colours_not_read_yet()
    {
        var frame = Mcdu();
        var rows = frame.Texts.Select(t => t.Text.Trim()).ToArray();
        using var rig = new Rig();
        var needs = rig.Window(colours: true);
        var plain = rig.Window(A330Gauge.McduFirstOfficer);
        rig.Displays.Show(WithoutColours(frame));
        rig.Displays.Show(WithoutColours(frame) with { Gauge = A330Gauge.McduFirstOfficer });
        needs.Show();
        plain.Show();

        Assert.Equal(new[] { "Colours not read yet" }.Concat(rows), needs.Items());
        Assert.Equal(rows, plain.Items());   // a window that needs no colour waits for none

        rig.Displays.Show(frame);   // the fixture's colours
        needs.RefreshNow();
        Assert.Equal(rows, needs.Items());

        // Only over a frame's rows: with no frame the window is reading, nothing else.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        needs.RefreshNow();
        Assert.Equal(new[] { "Reading the captain's MCDU" }, needs.Items());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_window_needing_list_rows_says_list_rows_not_read_yet()
    {
        var frame = Mcdu();
        Assert.False(frame.HiddenRowsLeftOut);
        var rows = frame.Texts.Select(t => t.Text.Trim()).ToArray();
        using var rig = new Rig();
        rig.Displays.Show(frame);
        var w = rig.Window(listRows: true);
        w.Show();

        Assert.Equal(new[] { "List rows not read yet" }.Concat(rows), w.Items());

        rig.Displays.Show(frame with { HiddenRowsLeftOut = true });
        w.RefreshNow();
        Assert.Equal(rows, w.Items());

        // Both: colours first.
        var both = rig.Window(A330Gauge.McduFirstOfficer, colours: true, listRows: true);
        rig.Displays.Show(WithoutColours(frame) with { Gauge = A330Gauge.McduFirstOfficer });
        both.Show();
        Assert.Equal(new[] { "Colours not read yet", "List rows not read yet" }.Concat(rows), both.Items());
        Assert.Empty(rig.Speech.All);
    }

    // ---- The cockpit controls under the list (spec D9) ---------------------------------------------

    [Fact]
    public void A_key_row_press_goes_through_PressFromWindow()
    {
        using var rig = new Rig(null, (ModeKey, 1));
        var w = rig.Window(keys: [new(SelectorKey, 1), new(PushKey, 1), new(ModeKey)]);
        w.Show();
        Assert.Empty(rig.Def.Sets);   // opening presses nothing
        var vars = rig.Def.GetVariables();

        w.Control<Button>(SelectorKey).PerformClick();
        Assert.Equal((SelectorKey, 1.0, vars[SelectorKey]), Assert.Single(rig.Def.Sets));

        w.Control<Button>(PushKey).PerformClick();
        Assert.Equal((PushKey, 1.0, vars[PushKey]), rig.Def.Sets[^1]);

        Choose(w.Control<ComboBox>(ModeKey), 2);   // the pilot's choice: Right
        Assert.Equal((ModeKey, 2.0, vars[ModeKey]), rig.Def.Sets[^1]);
        Assert.Equal(3, rig.Def.Sets.Count);
        Assert.Empty(rig.Speech.All);   // the press's own read-back is the definition's; the window adds nothing
    }

    [Fact]
    public void A_choice_list_shows_the_controls_position_and_never_presses_for_it()
    {
        using var rig = new Rig(null, (ModeKey, 1));
        var w = rig.Window(keys: [new(ModeKey)]);
        w.Show();
        var combo = w.Control<ComboBox>(ModeKey);

        Assert.Equal(ComboBoxStyle.DropDownList, combo.DropDownStyle);
        Assert.Equal(new[] { "Left", "Centre", "Right" }, combo.Items.Cast<string>());
        Assert.Equal("Test mode", combo.AccessibleName);
        Assert.Equal("Centre", combo.SelectedItem);

        // The cockpit changes: the list follows, pressing nothing.
        rig.Cockpit(ModeKey, 2);
        w.RefreshNow();
        Assert.Equal("Right", combo.SelectedItem);
        Assert.Empty(rig.Def.Sets);

        // A selection that moves without a commit presses nothing: the pilot browsing an open list (each arrow a
        // SelectedIndexChanged), a list cancelled after it, or a selection set in code.
        combo.SelectedIndex = 0;
        combo.SelectedIndex = 1;
        combo.SelectedIndex = 2;
        Assert.Empty(rig.Def.Sets);

        // The pilot's commit presses once, the position committed, and stays shown until the control's state changes.
        Choose(combo, 0);
        Assert.Equal((ModeKey, 0.0), (rig.Def.Sets.Single().Key, rig.Def.Sets.Single().Value));
        w.RefreshNow();
        Assert.Equal("Left", combo.SelectedItem);
        rig.Cockpit(ModeKey, 0);
        w.RefreshNow();
        Assert.Equal("Left", combo.SelectedItem);
        Assert.Single(rig.Def.Sets);

        // A browse through an open list then a commit: one press, of the position committed.
        combo.SelectedIndex = 1;
        Choose(combo, 2);
        Assert.Equal(2, rig.Def.Sets.Count);
        Assert.Equal((ModeKey, 2.0), (rig.Def.Sets[^1].Key, rig.Def.Sets[^1].Value));

        // Unread: no position is shown, rather than a guess.
        using var unread = new Rig();
        var u = unread.Window(keys: [new(ModeKey)]);
        u.Show();
        Assert.Equal(-1, u.Control<ComboBox>(ModeKey).SelectedIndex);
        Assert.Empty(unread.Def.Sets);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_key_rows_label_carries_the_controls_state()
    {
        using var rig = new Rig();
        var w = rig.Window(keys: [new(SelectorKey, 1), new(PushKey, 1), new(ModeKey)]);
        w.Show();
        var selector = w.Control<Button>(SelectorKey);
        var push = w.Control<Button>(PushKey);

        // Unread, and a push key, which has no state: the label alone.
        Assert.Equal("Test selector", selector.Text);
        Assert.Equal("Test key", push.Text);
        Assert.Equal("Test mode", w.Control<Label>(ModeKey + " label").Text);

        rig.Cockpit(SelectorKey, 1);
        w.RefreshNow();
        Assert.Equal("Test selector: On", selector.Text);
        Assert.Equal("Test selector: On", selector.AccessibilityObject.Name);

        int changes = 0;
        selector.TextChanged += (_, _) => changes++;
        w.RefreshNow();
        w.RefreshNow();
        Assert.Equal(0, changes);   // a label is set only when its text changes

        rig.Cockpit(SelectorKey, 0);
        w.RefreshNow();
        Assert.Equal("Test selector: Off", selector.Text);
        Assert.Equal(1, changes);
        Assert.Equal("Test key", push.Text);
        Assert.Empty(rig.Def.Sets);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_keys_follow_the_list_in_the_tab_order_and_a_key_that_is_no_control_is_left_out()
    {
        using var rig = new Rig();
        var w = rig.Window(keys: [new(PushKey, 1), new("NO_SUCH_KEY", 1), new(ModeKey), new(SelectorKey, 0)]);
        w.Show();

        var list = w.List();
        var panel = w.Controls.Cast<Control>().Single(c => c != list);
        Assert.Equal(0, list.TabIndex);
        Assert.True(panel.TabIndex > list.TabIndex);
        var keys = panel.Controls.Cast<Control>().ToList();
        Assert.Equal(new[] { PushKey, ModeKey + " label", ModeKey, SelectorKey }, keys.Select(c => c.Name));
        Assert.Equal(new[] { typeof(Button), typeof(Label), typeof(ComboBox), typeof(Button) }, keys.Select(c => c.GetType()));
        Assert.Equal(Enumerable.Range(0, keys.Count), keys.Select(c => c.TabIndex));
        Assert.Empty(w.Controls.Find("NO_SUCH_KEY", true));

        // A window with no keys holds its list alone.
        var bare = rig.Window(A330Gauge.Ewd);
        bare.Show();
        Assert.Same(bare.List(), Assert.Single(bare.Controls.Cast<Control>()));
    }

    // ---- Keys and silence ---------------------------------------------------------------------------

    [Fact]
    public void F5_and_open_say_nothing()
    {
        var replay = A330ReplayDisplays.FromFixtures("p02_mcdu_init.json");
        using var rig = new Rig(replay, (SelectorKey, 1));
        var w = rig.Window(keys: [new(SelectorKey, 1)]);

        w.Show();
        Assert.Equal("INIT", w.Items()[0]);
        Assert.Empty(rig.Speech.All);

        // F5 reads the display again now, without a word; a held key's repeats do not.
        var frame = replay.Latest(A330Gauge.McduCaptain)!;
        replay.Show(frame with { Texts = [frame.Texts[0] with { Text = "  INIT B" }, .. frame.Texts.Skip(1)] });
        Assert.True(w.Press(Keys.F5, autoRepeat: true));
        Assert.Equal("INIT", w.Items()[0]);
        Assert.True(w.Press(Keys.F5));
        Assert.Equal("INIT B", w.Items()[0]);

        w.Hide();
        w.Show();
        w.RefreshNow();
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Def.Sets);
    }

    [Fact]
    public void Escape_closes_the_window_and_lets_its_display_go_without_a_word()
    {
        using var rig = new Rig(A330ReplayDisplays.FromFixtures("p02_mcdu_init.json"));
        var w = rig.Window();
        w.Show();
        bool closed = false;
        w.FormClosed += (_, _) => closed = true;

        Assert.True(w.Press(Keys.Escape));

        Assert.True(closed);
        Assert.True(w.IsDisposed);
        Assert.Null(rig.Displays.RefreshWanted(A330Gauge.McduCaptain, w));
        Assert.Single(rig.Displays.Releases);
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_rows_and_labels_are_the_same_under_a_comma_decimal_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            using var rig = new Rig(null, (SelectorKey, 2.5));
            var w = rig.Window(A330Gauge.Ewd, keys: [new(SelectorKey, 1)]);
            w.Show();
            Assert.Equal(new[] { "Reading the E/WD" }, w.Items());
            Assert.Equal("Test selector: 2.5", w.Control<Button>(SelectorKey).Text);
            rig.Clock += 30_000;
            w.RefreshNow();
            Assert.Equal(new[] { "The display reader cannot read the E/WD" }, w.Items());
            return 0;
        });
    }
}
