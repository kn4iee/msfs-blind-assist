// The A330's MCDU window never slews a list past its top or its end: a frame says whether its page has a list the
// display clips and whether rows of it are hidden above or below, and the window sends Page Up (the MCDU's UARROW, on to
// later rows) only while rows are hidden below, Page Down (DOWN, back toward the top) only while rows are hidden above,
// one slew at a time, judged only on a page (a frame the reader has left the hidden rows out of). The aircraft counts a
// slew press past either end, so the next presses the other way only undo it and the list looks stuck (measured
// 2026-11-20). A press not sent says nothing, as a press at a list's end on the other MCDU windows (owner, 2026-11-23).
// Each window is a real WinForms form, shown far off the screen and without taking focus, on a clock the test moves; its
// key writes and its waits go through the window's test seams.

using System.Drawing;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public sealed class A330McduSlewTests
{
    // ---- The frame's list edges on the wire ---------------------------------------------------------------

    private const string Frame = "{\"gauge\":\"McduCaptain\",\"complete\":true,\"missing\":0,\"hiddenRowsLeftOut\":true,\"texts\":[]";

    /// <summary>The frame object with <paramref name="extra"/> inside it, parsed both ways: alone (a fixture file) and as a reader line.</summary>
    private static A330Frame?[] Parsed(string extra) =>
    [
        A330ReaderProtocol.ParseFrame(Frame + extra + "}", 1),
        A330ReaderProtocol.Parse("{\"frame\":" + Frame + extra + "}}", 1)?.Frame,
    ];

    [Fact]
    public void A_frame_without_list_edges_reads_them_false()
    {
        foreach (var f in Parsed(""))
        {
            Assert.NotNull(f);
            Assert.False(f!.List);
            Assert.False(f.ListAbove);
            Assert.False(f.ListBelow);
            Assert.True(f.HiddenRowsLeftOut);
        }
    }

    [Theory]
    [InlineData("list", true, false, false)]
    [InlineData("listAbove", false, true, false)]
    [InlineData("listBelow", false, false, true)]
    public void Each_list_edge_reads_true_when_present_and_false_when_false(string key, bool list, bool above, bool below)
    {
        foreach (var f in Parsed(",\"" + key + "\":true"))
        {
            Assert.NotNull(f);
            Assert.Equal((list, above, below), (f!.List, f.ListAbove, f.ListBelow));
            Assert.True(f.Complete);
            Assert.True(f.HiddenRowsLeftOut);
        }
        foreach (var f in Parsed(",\"" + key + "\":false"))
            Assert.Equal((false, false, false), (f!.List, f.ListAbove, f.ListBelow));
    }

    [Fact]
    public void All_three_list_edges_read_together()
    {
        foreach (var f in Parsed(",\"list\":true,\"listAbove\":true,\"listBelow\":true"))
            Assert.Equal((true, true, true), (f!.List, f.ListAbove, f.ListBelow));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("listAbove")]
    [InlineData("listBelow")]
    public void A_list_edge_that_is_not_true_or_false_refuses_the_frame_as_hiddenRowsLeftOut_does(string key)
    {
        foreach (string value in new[] { "1", "\"true\"", "null" })
            Assert.All(Parsed(",\"" + key + "\":" + value), f => Assert.Null(f));
    }

    // ---- The window --------------------------------------------------------------------------------------

    /// <summary>The window of a test, its replay, what was spoken and every key flag it wrote.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly List<IniA330CduForm> _windows = new();

        public A330ReplayDisplays Displays { get; } = new();
        public SpeechCapture Speech { get; } = new();
        public SimConnectManager Sim { get; } = SeededSimConnectCache.With();
        public long Clock = 100_000;

        /// <summary>Every key flag written.</summary>
        public List<string> Sent { get; } = new();

        /// <summary>The waits a held window has started and not yet finished.</summary>
        public Queue<(int Ms, TaskCompletionSource Done)> Held { get; } = new();

        /// <summary>A window on the captain's MCDU, shown. Its waits run at once, or, held, only when the test runs them.</summary>
        public IniA330CduForm Window(bool held = false)
        {
            var w = new IniA330CduForm(Displays, Sim, Speech, CduFormProfile.A330(false))
            {
                NowMs = () => Clock,
                SendPressFlag = flag => { Sent.Add(flag); return true; },
                ShowInactive = true,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
            };
            if (held) w.Delay = ms => { var done = new TaskCompletionSource(); Held.Enqueue((ms, done)); return done.Task; };
            else w.Delay = ms => { Clock += Math.Max(0, ms); return Task.CompletedTask; };
            _windows.Add(w);
            w.Show();
            Speech.All.Clear();   // the page title said on opening, as the Fenix and FlyByWire windows say it (A330McduFormTests)
            return w;
        }

        /// <summary>Runs the held waits, each moving the clock by its length, until <paramref name="ms"/> have passed or none is left.</summary>
        public void RunHeld(int ms)
        {
            long until = Clock + ms;
            while (Held.Count > 0 && Clock < until)
            {
                var (wait, done) = Held.Dequeue();
                Clock += wait;
                done.SetResult();
            }
        }

        public void Dispose()
        {
            foreach (var w in _windows)
                if (!w.IsDisposed) w.Dispose();
        }
    }

    /// <summary>DEPARTURES, its runways a list (the fixture carries no list edges).</summary>
    private static A330Frame Departures() =>
        A330ReplayDisplays.FromFixtures("p03_departures.json").Latest(A330Gauge.McduCaptain)!;

    /// <summary>DEPARTURES as a list the display clips, with rows hidden above and below as given, its first runway row
    /// <paramref name="first"/> (another value is the list slewed).</summary>
    private static A330Frame Runways(bool above, bool below, string first = "a08L")
    {
        var f = Departures();
        return f with
        {
            Texts = f.Texts.Select(t => t.Order == 17 ? t with { Text = first } : t).ToList(),
            List = true, ListAbove = above, ListBelow = below,
        };
    }

    private static void Key(IniA330CduForm w, Keys keys) => w.HandleKey(new KeyEventArgs(keys));

    private static IEnumerable<Control> AllControls(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(c => AllControls(c).Prepend(c));

    private static void Click(Form w, string label) => AllControls(w).OfType<Button>().Single(b => b.Text == label).PerformClick();

    /// <summary>The next frame, and the window's next refresh with it.</summary>
    private static void Next(Rig rig, IniA330CduForm w, A330Frame frame)
    {
        rig.Displays.Show(frame);
        rig.Clock += 250;
        w.RefreshNow();
    }

    [Fact]
    public void Page_Up_is_sent_only_while_rows_are_hidden_below_and_otherwise_does_nothing_silently()
    {
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window();

        Key(w, Keys.PageUp);
        Assert.Equal(new[] { "INI_MCDU1_UARROW" }, rig.Sent);

        // The list at its end: nothing sent, Page Up and Alt+Up alike, and nothing said (as on the other MCDU windows).
        Next(rig, w, Runways(above: true, below: false, first: "a26L"));
        Key(w, Keys.PageUp);
        Key(w, Keys.Alt | Keys.Up);
        Assert.Single(rig.Sent);

        // Page Down still goes: rows are hidden above.
        Key(w, Keys.PageDown);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Page_Down_is_sent_only_while_rows_are_hidden_above_and_otherwise_does_nothing_silently()
    {
        // The owner's case: a list at its top, where a slew toward the top is counted and undoes the next one down.
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window();

        Key(w, Keys.PageDown);
        Key(w, Keys.Alt | Keys.Down);
        Assert.Empty(rig.Sent);

        // Slewed on: rows are hidden above, so Page Down goes.
        Key(w, Keys.PageUp);
        Next(rig, w, Runways(above: true, below: true, first: "a26R"));
        Key(w, Keys.PageDown);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);

        // Back at the top: nothing sent.
        Next(rig, w, Runways(above: false, below: true));
        Key(w, Keys.Alt | Keys.Down);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Without_a_clipped_list_or_without_a_page_the_slew_keys_go_as_today()
    {
        // A page with no clipped list, or a frame that does not say: both keys, every press, nothing said.
        using var rig = new Rig();
        rig.Displays.Show(Departures());
        var w = rig.Window();
        Key(w, Keys.PageUp);
        Key(w, Keys.PageUp);
        Key(w, Keys.PageDown);
        Key(w, Keys.Alt | Keys.Down);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW", "INI_MCDU1_DOWN", "INI_MCDU1_DOWN" }, rig.Sent);

        // No frame, so no page shown.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        rig.Clock += 250;
        w.RefreshNow();
        rig.Sent.Clear();
        Key(w, Keys.PageUp);
        Key(w, Keys.PageDown);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);

        // A list frame that does not leave hidden rows out, and none that does within the wait: no page shown either,
        // whatever its edges say.
        Next(rig, w, Runways(above: false, below: false) with { HiddenRowsLeftOut = false });
        rig.Sent.Clear();
        Key(w, Keys.PageUp);
        Key(w, Keys.PageDown);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_second_slew_waits_for_a_new_page_and_with_none_in_1_5_s_is_not_sent()
    {
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window(held: true);

        Key(w, Keys.PageUp);
        Assert.Equal(new[] { "INI_MCDU1_UARROW" }, rig.Sent);

        // Again before any frame shows the slew: the press waits for one, and sends nothing meanwhile.
        Key(w, Keys.PageUp);
        rig.RunHeld(400);
        Assert.Single(rig.Sent);
        Assert.NotEmpty(rig.Held);

        // The page comes, with rows still below: the press goes.
        rig.Displays.Show(Runways(above: true, below: true, first: "a26R"));
        rig.RunHeld(100);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW" }, rig.Sent);
        Assert.Empty(rig.Held);

        // A third, and no new page comes in 1.5 s: the list did not move, so nothing is sent.
        Key(w, Keys.PageUp);
        long pressed = rig.Clock;
        rig.RunHeld(1450);
        Assert.NotEmpty(rig.Held);
        rig.RunHeld(100);
        Assert.InRange(rig.Clock - pressed, IniA330CduForm.SlewShowsWithinMs, IniA330CduForm.SlewShowsWithinMs + 25);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Empty(rig.Held);

        // Still no new page: another press the same way waits again, and is not sent again.
        Key(w, Keys.Alt | Keys.Up);
        rig.RunHeld(1600);
        Assert.Equal(2, rig.Sent.Count);
        Assert.Empty(rig.Held);

        // A press the other way does not wait for that slew: it is judged at once on the page, which has rows hidden above.
        Key(w, Keys.PageDown);
        Assert.Empty(rig.Held);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_slew_waits_for_the_reader_to_leave_the_hidden_rows_out_of_the_newest_frame()
    {
        // The reader sends each new frame once as read (its list keys not known yet, the hidden rows in it), then with
        // the hidden rows left out. A press is never judged on the frame as read, nor on the page it replaced: the
        // second Page Up here would take the list past its end (the owner's stuck ARRIVAL list).
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window(held: true);
        Key(w, Keys.PageUp);
        Key(w, Keys.PageUp);
        rig.Displays.Show(Runways(above: false, below: false, first: "a26L") with { List = false, HiddenRowsLeftOut = false });
        rig.RunHeld(500);
        Assert.Single(rig.Sent);
        Assert.NotEmpty(rig.Held);

        rig.Displays.Show(Runways(above: true, below: false, first: "a26L"));
        rig.RunHeld(25);
        Assert.Empty(rig.Held);
        Assert.Equal(new[] { "INI_MCDU1_UARROW" }, rig.Sent);

        // A new page on its way the same: the page shown (no list) does not judge a slew on the list arriving.
        Next(rig, w, Departures());
        rig.Displays.Show(Runways(above: false, below: false) with { List = false, HiddenRowsLeftOut = false });
        Key(w, Keys.PageDown);
        rig.RunHeld(300);
        Assert.NotEmpty(rig.Held);
        rig.Displays.Show(Runways(above: false, below: true));
        rig.RunHeld(25);
        Assert.Empty(rig.Held);
        Assert.Single(rig.Sent);   // at the list's top: not sent

        // No page within 1.5 s: judged on the page shown.
        rig.Displays.Show(Runways(above: false, below: false) with { List = false, HiddenRowsLeftOut = false });
        Key(w, Keys.PageUp);
        rig.RunHeld(1600);
        Assert.Empty(rig.Held);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW" }, rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_press_the_other_way_is_judged_at_once_on_the_page_shown()
    {
        // Rows hidden both ways: Page Up goes, and Page Down straight after it goes too, without waiting for a frame to
        // show the Page Up (only the key pace between them).
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: true, below: true));
        var w = rig.Window();
        Key(w, Keys.PageUp);
        long pressed = rig.Clock;
        Key(w, Keys.PageDown);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);
        Assert.Equal(150, rig.Clock - pressed);

        // The two undo each other, so the page shown is where the list is: the next press is judged on it at once.
        pressed = rig.Clock;
        Key(w, Keys.Alt | Keys.Down);
        Assert.Equal("INI_MCDU1_DOWN", rig.Sent[^1]);
        Assert.Equal(150, rig.Clock - pressed);

        // And that one is awaited by the next press its way, as any slew is.
        pressed = rig.Clock;
        Key(w, Keys.PageDown);
        Assert.Equal(3, rig.Sent.Count);
        Assert.Equal(IniA330CduForm.SlewShowsWithinMs, rig.Clock - pressed);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_press_the_other_way_the_page_does_not_allow_is_refused_at_once_and_the_slew_before_it_is_still_awaited()
    {
        // At the list's top: Page Up goes; Page Down straight after it is judged at once on the page shown, still at the top.
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window();
        Key(w, Keys.PageUp);
        long pressed = rig.Clock;
        Key(w, Keys.PageDown);
        Assert.Equal(pressed, rig.Clock);
        Assert.Single(rig.Sent);

        // The Page Up has still not shown, so another Page Up waits for it: it could take the list past its end.
        Key(w, Keys.PageUp);
        Assert.Equal(IniA330CduForm.SlewShowsWithinMs, rig.Clock - pressed);
        Assert.Single(rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Frames_stopping_while_a_slew_waits_send_it_as_with_no_page_shown()
    {
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window(held: true);
        Key(w, Keys.PageUp);
        Key(w, Keys.PageUp);
        rig.RunHeld(200);
        Assert.Single(rig.Sent);

        // No frame any more: the window shows its status row, and the key goes as it always has, with nothing said.
        rig.Displays.Forget(A330Gauge.McduCaptain);
        rig.RunHeld(25);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW" }, rig.Sent);
        Assert.Empty(rig.Held);
        Assert.Equal(new[] { "Reading the captain's MCDU" },
            w.Controls.Find("Screen", true).OfType<ListBox>().Single().Items.Cast<string>());
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void A_slew_waiting_for_a_frame_when_the_window_hides_sends_nothing_and_says_nothing()
    {
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window(held: true);
        Key(w, Keys.PageUp);
        Key(w, Keys.PageUp);
        rig.RunHeld(200);

        w.Hide();
        rig.RunHeld(2000);
        Assert.Empty(rig.Held);
        Assert.Single(rig.Sent);
        Assert.Empty(rig.Speech.All);

        // Shown again, the window judges on the page as it is: it waits for nothing.
        w.Show();
        Key(w, Keys.PageUp);
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_UARROW" }, rig.Sent);
    }

    [Fact]
    public void The_slew_buttons_follow_the_same_rule()
    {
        using var rig = new Rig();
        rig.Displays.Show(Runways(above: false, below: true));
        var w = rig.Window();

        Click(w, "Slew down");
        Assert.Empty(rig.Sent);
        Click(w, "Slew up");
        Assert.Equal(new[] { "INI_MCDU1_UARROW" }, rig.Sent);

        // Again before a new page, and none comes in 1.5 s.
        Click(w, "Slew up");
        Assert.Single(rig.Sent);

        Next(rig, w, Runways(above: true, below: false, first: "a26L"));
        Click(w, "Slew up");
        Click(w, "Slew down");
        Assert.Equal(new[] { "INI_MCDU1_UARROW", "INI_MCDU1_DOWN" }, rig.Sent);
        Assert.Empty(rig.Speech.All);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_window_says_nothing_under_a_comma_decimal_culture_either(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            using var rig = new Rig();
            rig.Displays.Show(Runways(above: false, below: false));
            var w = rig.Window();
            Key(w, Keys.PageUp);
            Key(w, Keys.PageDown);
            Assert.Empty(rig.Speech.All);
            Assert.Empty(rig.Sent);
            return 0;
        });
    }
}
