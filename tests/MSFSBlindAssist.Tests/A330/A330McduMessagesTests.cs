// The FMS message on the captain's MCDU scratchpad line, spoken with the MCDU window closed under its own Ctrl+M row
// ("FMS messages"), and Backspace on a queued message in the window. On the replay displays, with no simulator: the
// watch's timer does not run, the test runs its work (WatchMcduMessages) once a reading; the window is a real form,
// shown far off the screen without taking the focus, on a clock the test moves.

using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public sealed class A330McduMessagesTests : IDisposable
{
    private const string Row = "A330_CALLOUT_FMS_MESSAGE";

    private readonly List<string> _savedMutes;
    private readonly bool _savedAlternateKeys;

    public A330McduMessagesTests()
    {
        _savedMutes = SettingsManager.Current.A330DisabledMonitorVariables;
        _savedAlternateKeys = SettingsManager.Current.MCDUUseAlternateLSKKeys;
        SettingsManager.Current.A330DisabledMonitorVariables = new List<string>();
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        SettingsManager.Current.MCDUUseAlternateLSKKeys = false;
    }

    public void Dispose()
    {
        SettingsManager.Current.A330DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        SettingsManager.Current.MCDUUseAlternateLSKKeys = _savedAlternateKeys;
    }

    private static void Mute(bool muted)
    {
        var list = SettingsManager.Current.A330DisabledMonitorVariables;
        list.Remove(Row);
        if (muted) list.Add(Row);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    // ---- frames ----

    private static A330Frame Fixture(string name) =>
        A330ReplayDisplays.FromFixtures(name).Latest(A330Gauge.McduCaptain)!;

    private static A330Frame Init() => Fixture("p03_init.json");

    private static A330Text PadText(string text, int column = 0) =>
        new(999 + column, A330McduGeometry.Left + column * A330McduGeometry.ColumnPitch,
            A330McduGeometry.Top + A330McduLines.ScratchpadRow * A330McduGeometry.RowPitch,
            text, A330McduGeometry.LargeFont, 29.5f, new A330Rgb(1, 1, 1), [A330McduGeometry.Left]);

    /// <summary>INIT with its scratchpad line holding only <paramref name="text"/> (nothing for "").</summary>
    private static A330Frame Pad(string text, A330Frame? frame = null)
    {
        frame ??= Init();
        var texts = frame.Texts.Where(t => A330McduGeometry.RowOf(t.Baseline) != A330McduLines.ScratchpadRow).ToList();
        if (text.Length > 0) texts.Add(PadText(text));
        return frame with { Texts = texts };
    }

    private static A330McduScreen Screen(A330Frame frame) => A330McduScreen.From(frame)!;

    // ---- the watch, on the definition ----

    /// <summary>The definition on the replay, with its watch's timer left to the test.</summary>
    private sealed class WatchRig : IDisposable
    {
        public WatchRig()
        {
            Def = new IniA330Definition { Displays = Displays, RunMcduWatchTimer = false };
        }

        public A330ReplayDisplays Displays { get; } = new();
        public IniA330Definition Def { get; }
        public SpeechCapture Speech { get; } = new();

        /// <summary>A lamp's update reaches the definition, as the simulator's variable updates do.</summary>
        public void Lamp(bool firstOfficer, double value) =>
            Assert.True(Def.ProcessSimVarUpdate(
                firstOfficer ? IniA330Definition.FmsLampFirstOfficerKey : IniA330Definition.FmsLampCaptainKey, value, Speech));

        /// <summary>The first variable update with the A330 active: the watch starts.</summary>
        public void Start() => Lamp(false, 0);

        /// <summary>One reading of the watch (its timer's work).</summary>
        public void Tick() => Def.WatchMcduMessages();

        public void Show(A330Frame frame) => Displays.Show(frame);

        public int CaptainWants => Displays.Wants.Count(w => w.Gauge == A330Gauge.McduCaptain);

        public void Dispose() => Def.OnSwitchedAway();
    }

    [Fact]
    public void The_row_is_FMS_messages()
    {
        Assert.Equal(new[] { (Row, "FMS messages") }, A330CalloutRows.Mcdu);
        Assert.Equal(Row, A330CalloutRows.FmsMessageKey);
    }

    [Fact]
    public void A_new_message_on_the_scratchpad_row_is_spoken_once()
    {
        using var rig = new WatchRig();
        rig.Show(Pad(""));
        Assert.Empty(rig.Displays.Wants);

        // The first variable update starts the watch: the captain's MCDU, at the background refresh.
        rig.Start();
        var want = Assert.Single(rig.Displays.Wants);
        Assert.Equal(A330Gauge.McduCaptain, want.Gauge);
        Assert.Equal(1000, want.RefreshMs);
        rig.Tick();

        // Spoken once two readings agree, and once only.
        rig.Show(Pad("INITIALIZE WEIGHT/CG"));
        rig.Tick();
        Assert.Empty(rig.Speech.All);
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: INITIALIZE WEIGHT/CG" }, rig.Speech.All);
        for (int i = 0; i < 5; i++) rig.Tick();
        Assert.Single(rig.Speech.All);
        Assert.Empty(rig.Speech.Interrupts);   // the non-interrupting announce

        // Later updates start no second watch.
        rig.Lamp(false, 0);
        rig.Lamp(true, 0);
        Assert.Single(rig.Displays.Wants);

        // The next message is said too.
        rig.Show(Pad("CHECK TAKE OFF DATA"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: INITIALIZE WEIGHT/CG", "MCDU message: CHECK TAKE OFF DATA" }, rig.Speech.All);
    }

    [Fact]
    public void Typed_text_is_never_a_message()
    {
        var m = new A330McduMessages();
        Assert.Null(m.Next(Screen(Pad("")), null));

        // The row is the typed line, a start of it (the page a frame behind the typed line), or runs past it (the typed
        // line a frame behind the page); spaces around the typed line do not count.
        foreach (var (row, typed) in new[] { ("KJFK", "KJFK"), ("KJ", "KJFK"), ("KJFK", "KJ"), ("KJFK", " KJFK ") })
            for (int i = 0; i < 3; i++)
                Assert.Null(m.Next(Screen(Pad(row)), typed));

        // A row that is none of those is a message, with a typed line or without.
        Assert.Null(m.Next(Screen(Pad("FORMAT ERROR")), "KJFK"));
        Assert.Equal("MCDU message: FORMAT ERROR", m.Next(Screen(Pad("FORMAT ERROR")), "KJFK"));

        // An empty typed line is no typed text: it hides no message.
        var empty = new A330McduMessages();
        Assert.Null(empty.Next(Screen(Pad("")), ""));
        Assert.Null(empty.Next(Screen(Pad("NOT ALLOWED")), ""));
        Assert.Equal("MCDU message: NOT ALLOWED", empty.Next(Screen(Pad("NOT ALLOWED")), ""));

        // The watch reads the captain's typed line from the display reader.
        using var rig = new WatchRig();
        rig.Show(Pad(""));
        rig.Displays.SetScratchpad(false, "");
        rig.Start();
        rig.Tick();
        rig.Displays.SetScratchpad(false, "KJFK");
        rig.Displays.SetScratchpad(true, "NOT ALLOWED");   // the first officer's typed line is not the captain's
        rig.Show(Pad("KJFK"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Empty(rig.Speech.All);
        rig.Show(Pad("NOT ALLOWED"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: NOT ALLOWED" }, rig.Speech.All);
    }

    [Fact]
    public void No_message_is_taken_while_hidden_rows_are_not_left_out()
    {
        static A330Frame Hidden(string text) => Pad(text) with { HiddenRowsLeftOut = false };
        var m = new A330McduMessages();

        // A reading that may hold a hidden list row on the line counts for nothing, not even as the first reading.
        Assert.Null(m.Next(Screen(Hidden("")), null));
        Assert.Null(m.Next(Screen(Hidden("")), null));
        Assert.Null(m.Next(Screen(Pad("NOT ALLOWED")), null));   // the first reading: the starting point
        Assert.Null(m.Next(Screen(Pad("NOT ALLOWED")), null));

        // Nor does it come between two readings: the line agrees on no message, so the message is forgotten.
        Assert.Null(m.Next(Screen(Pad("")), null));
        Assert.Null(m.Next(Screen(Hidden("NOT ALLOWED")), null));
        Assert.Null(m.Next(Screen(Pad("")), null));
        Assert.Null(m.Next(Screen(Hidden("RWY 08L")), null));
        Assert.Null(m.Next(Screen(Pad("NOT ALLOWED")), null));
        Assert.Null(m.Next(Screen(Hidden("")), null));
        Assert.Equal("MCDU message: NOT ALLOWED", m.Next(Screen(Pad("NOT ALLOWED")), null));

        // The watch says nothing from such frames.
        using var rig = new WatchRig();
        rig.Show(Hidden(""));
        rig.Start();
        rig.Tick();
        rig.Show(Hidden("08L"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_first_reading_after_start_is_silent()
    {
        using var rig = new WatchRig();
        rig.Show(Pad("INITIALIZE WEIGHT/CG"));
        rig.Start();
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Empty(rig.Speech.All);   // on the line before the watch started: its starting point

        // A flight reload: the next reading is the starting point again.
        rig.Show(Pad("CHECK TAKE OFF DATA"));
        rig.Tick();
        rig.Def.OnSimContextReset();
        for (int i = 0; i < 3; i++) rig.Tick();
        Assert.Empty(rig.Speech.All);

        rig.Show(Pad("NAV ACCUR DOWNGRADED"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: NAV ACCUR DOWNGRADED" }, rig.Speech.All);

        // A reading with no complete frame is no reading: the first one after the start is still silent.
        using var late = new WatchRig();
        late.Start();
        late.Tick();
        late.Tick();
        late.Show(Pad("INITIALIZE WEIGHT/CG"));
        late.Tick();
        late.Tick();
        Assert.Empty(late.Speech.All);
    }

    [Fact]
    public void A_message_that_returns_after_being_cleared_is_spoken_again()
    {
        using var rig = new WatchRig();
        rig.Show(Pad(""));
        rig.Start();
        rig.Tick();

        void Reading(string pad, int times = 2)
        {
            rig.Show(Pad(pad));
            for (int i = 0; i < times; i++) rig.Tick();
        }

        Reading("NOT ALLOWED");
        Reading("");
        Reading("NOT ALLOWED");
        Assert.Equal(new[] { "MCDU message: NOT ALLOWED", "MCDU message: NOT ALLOWED" }, rig.Speech.All);

        // One reading of an empty line between two of the message is no clearing: nothing more.
        Reading("", times: 1);
        Reading("NOT ALLOWED");
        Assert.Equal(2, rig.Speech.All.Count);

        // Typed text on the line clears it too.
        rig.Displays.SetScratchpad(false, "KJ");
        Reading("KJ");
        Reading("NOT ALLOWED");
        Assert.Equal(3, rig.Speech.All.Count);

        // Another message, then the first one back: each is said.
        Reading("FORMAT ERROR");
        Reading("NOT ALLOWED");
        Assert.Equal(new[]
        {
            "MCDU message: NOT ALLOWED", "MCDU message: NOT ALLOWED", "MCDU message: NOT ALLOWED",
            "MCDU message: FORMAT ERROR", "MCDU message: NOT ALLOWED",
        }, rig.Speech.All);
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        Mute(true);
        using var rig = new WatchRig();
        rig.Show(Pad(""));

        // Muted from the start: the captain's MCDU is not even wanted.
        rig.Start();
        rig.Tick();
        rig.Show(Pad("NOT ALLOWED"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Equal(0, rig.CaptainWants);
        Assert.Empty(rig.Speech.All);

        // Unmuted: wanted again on the next reading, which is the watch's starting point.
        Mute(false);
        rig.Tick();
        Assert.Equal(1, rig.CaptainWants);
        Assert.Equal(1000, rig.Displays.Wants[^1].RefreshMs);
        rig.Tick();
        Assert.Empty(rig.Speech.All);
        rig.Show(Pad("FORMAT ERROR"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: FORMAT ERROR" }, rig.Speech.All);

        // Muted again: the display is let go at the next reading, and nothing more is said.
        Mute(true);
        rig.Tick();
        Assert.Equal((A330Gauge.McduCaptain, rig.Displays.Wants[^1].Owner), Assert.Single(rig.Displays.Releases));
        rig.Show(Pad("CHECK TAKE OFF DATA"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Single(rig.Speech.All);
        Assert.Equal(1, rig.CaptainWants);
    }

    [Fact]
    public void The_slew_arrows_on_the_scratchpad_line_are_not_a_message()
    {
        var fpln = Fixture("p03_fpln.json");
        var m = new A330McduMessages();
        Assert.Null(m.Next(Screen(Pad("")), null));
        for (int i = 0; i < 3; i++) Assert.Null(m.Next(Screen(fpln), null));

        // A message on the F-PLN's scratchpad line is said without the arrows beside it.
        var withMessage = fpln with { Texts = fpln.Texts.Append(PadText("NOT IN DATABASE")).ToList() };
        Assert.Null(m.Next(Screen(withMessage), null));
        Assert.Equal("MCDU message: NOT IN DATABASE", m.Next(Screen(withMessage), null));
    }

    [Fact]
    public void The_watch_speaks_nothing_while_the_window_is_shown()
    {
        using var rig = new WatchRig();
        var windowSpeech = new SpeechCapture();
        long clock = 100_000;
        var w = new IniA330CduForm(rig.Displays, SeededSimConnectCache.With(), windowSpeech, CduFormProfile.A330(firstOfficer: false))
        {
            NowMs = () => clock,
            Delay = _ => Task.CompletedTask,
            SendPressFlag = _ => true,
            ShowInactive = true,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
        };
        // the window the MCDU key would have opened; HandleMcduHotkey takes the focus, so the field is set by reflection
        typeof(IniA330Definition).GetField("_mcduWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(rig.Def, w);

        rig.Show(Pad(""));
        rig.Start();
        w.Show();
        rig.Tick();

        // The window's own scratchpad line says the message; the watch keeps reading, silently.
        rig.Show(Pad("NOT ALLOWED"));
        for (int i = 0; i < 2; i++)
        {
            clock += 250;
            w.RefreshNow();
            rig.Tick();
        }
        Assert.Equal(new[] { "NOT ALLOWED" }, windowSpeech.All);
        Assert.Empty(rig.Speech.All);

        // Closed with the message still there: not said again.
        w.Close();
        rig.Tick();
        rig.Tick();
        Assert.Empty(rig.Speech.All);

        // A new one with the window closed is.
        rig.Show(Pad("FORMAT ERROR"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: FORMAT ERROR" }, rig.Speech.All);

        // The window shown on the first officer's side: still nothing from the watch.
        w.Show();
        w.HandleKey(new KeyEventArgs(Keys.Control | Keys.Shift | Keys.R));
        rig.Show(Pad("CHECK TAKE OFF DATA"));
        rig.Tick();
        rig.Tick();
        Assert.Single(rig.Speech.All);
    }

    [Fact]
    public void Switching_away_stops_the_watch_and_lets_the_display_go()
    {
        using var rig = new WatchRig();
        rig.Show(Pad(""));
        rig.Start();
        rig.Tick();

        rig.Def.OnSwitchedAway();
        Assert.Equal(rig.Displays.Wants.Select(w => (w.Gauge, w.Owner)), rig.Displays.Releases);
        rig.Show(Pad("NOT ALLOWED"));
        rig.Tick();
        rig.Tick();
        Assert.Empty(rig.Speech.All);

        // Back on the aircraft, its first update starts the watch again, from a silent first reading.
        rig.Start();
        Assert.Equal(2, rig.CaptainWants);
        rig.Tick();
        rig.Tick();
        Assert.Empty(rig.Speech.All);
        rig.Show(Pad("FORMAT ERROR"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "MCDU message: FORMAT ERROR" }, rig.Speech.All);
    }

    [Fact]
    public void The_lamp_variables_are_consumed_silently_with_no_row_of_their_own_and_skip_the_mute_wrap()
    {
        var def = new IniA330Definition();
        var vars = def.GetVariables();
        foreach (var (key, lvar) in new[] { (IniA330Definition.FmsLampCaptainKey, "INI_FMS1_message_light"), (IniA330Definition.FmsLampFirstOfficerKey, "INI_FMS2_message_light") })
        {
            var v = vars[key];
            Assert.Equal(lvar, v.Name);
            Assert.Equal(MSFSBlindAssist.SimConnect.SimVarType.LVar, v.Type);
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.ExcludeFromMonitorManager);   // [VAR-9]
            Assert.True(def.IsMuteWrapExempt(key));     // [VAR-8]: the branch checks the FMS messages row itself
        }
        def.OnSwitchedAway();
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_message_is_said_the_same_under_a_comma_decimal_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            using var rig = new WatchRig();
            rig.Show(Pad(""));
            rig.Start();
            rig.Tick();
            rig.Show(Pad("V1/VR/V2 DISAGREE"));
            rig.Tick();
            rig.Tick();
            Assert.Equal(new[] { "MCDU message: V1/VR/V2 DISAGREE" }, rig.Speech.All);
            return 0;
        });
    }

    // ---- Backspace in the window ----

    /// <summary>The window on the replay, on a clock the test moves; what it says and every key it writes.</summary>
    private sealed class WindowRig : IDisposable
    {
        public long Clock = 100_000;

        public WindowRig()
        {
            Window = new IniA330CduForm(Displays, SeededSimConnectCache.With(), Speech, CduFormProfile.A330(firstOfficer: false))
            {
                NowMs = () => Clock,
                Delay = ms => { Clock += ms; return Task.CompletedTask; },
                SendPressFlag = flag => { Sent.Add(flag); return true; },
                ShowInactive = true,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
        }

        public A330ReplayDisplays Displays { get; } = new();
        public SpeechCapture Speech { get; } = new();
        public List<string> Sent { get; } = new();
        public IniA330CduForm Window { get; }

        public void Tick(int ms = 250)
        {
            Clock += ms;
            Window.RefreshNow();
        }

        public void Backspace() => Window.HandleScratchpadKey(new KeyEventArgs(Keys.Back));

        public void Dispose() => Window.Dispose();
    }

    [Fact]
    public void Backspace_on_a_queued_message_says_message_cleared()
    {
        using var rig = new WindowRig();
        rig.Displays.Show(Pad(""));
        rig.Displays.SetScratchpad(false, "");
        rig.Window.Show();
        rig.Displays.Show(Pad("NOT ALLOWED"));
        rig.Tick();
        rig.Tick();
        Assert.Equal(new[] { "NOT ALLOWED" }, rig.Speech.All);

        // Backspace: CLR. The frame from before the press still holds the message; the next one does not: said once,
        // and the line's own read-back does not add "Scratchpad cleared".
        rig.Backspace();
        Assert.Equal("INI_MCDU1_CLR", rig.Sent[^1]);
        rig.Tick();
        Assert.Single(rig.Speech.All);
        rig.Displays.Show(Pad(""));
        rig.Tick();
        Assert.Equal("Message cleared: NOT ALLOWED", rig.Speech.All[^1]);
        for (int i = 0; i < 6; i++) rig.Tick();
        Assert.Equal(new[] { "NOT ALLOWED", "Message cleared: NOT ALLOWED" }, rig.Speech.All);

        // The next queued message takes its place: the line's read-back says it as usual.
        rig.Displays.Show(Pad("FORMAT ERROR"));
        rig.Tick();
        rig.Tick();
        rig.Backspace();
        rig.Displays.Show(Pad("NOT IN DATABASE"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Equal(new[] { "NOT ALLOWED", "Message cleared: NOT ALLOWED", "FORMAT ERROR", "Message cleared: FORMAT ERROR", "NOT IN DATABASE" },
            rig.Speech.All);

        // The message still there 1.5 s after the press: nothing is said for it.
        rig.Backspace();
        for (int i = 0; i < 7; i++) rig.Tick();
        Assert.Equal(5, rig.Speech.All.Count);

        // Backspace on typed text is no message cleared: what is left is said, as before.
        rig.Displays.SetScratchpad(false, "KJFK");
        rig.Displays.Show(Pad("KJFK"));
        rig.Tick();
        rig.Tick();
        Assert.Equal("KJFK", rig.Speech.All[^1]);
        rig.Backspace();
        rig.Displays.SetScratchpad(false, "KJF");
        rig.Displays.Show(Pad("KJF"));
        for (int i = 0; i < 4; i++) rig.Tick();
        Assert.Equal("KJF", rig.Speech.All[^1]);
        Assert.DoesNotContain(rig.Speech.All, s => s == "Message cleared: KJFK" || s == "Scratchpad cleared");
        Assert.Empty(rig.Speech.Interrupts);
    }
}
