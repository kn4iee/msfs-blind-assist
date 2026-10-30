using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 11 (2026-10-29): the FMS messages heard with the MFD window closed, as the A32NX's MCDU says them
/// ([A320-34]): the background display monitor already draws the MFD in turn, and a message the FMS posts in the scratchpad
/// is said once two reads agree on it, behind its own Ctrl+M row, followed silently while an MFD window is open (it says
/// them itself) and never decided from a dark MFD.</summary>
public class A350FmsMessageAnnouncerTests
{
    internal static IReadOnlyList<MfdLine> Page(string? message) => message == null
        ? new[] { new MfdLine("title", "INIT", 0, 0) }
        : new[] { new MfdLine("title", "INIT", 0, 0), new MfdLine("scratchpad", $"Scratchpad: {message}, white", 400, 980) };

    [Fact]
    public void A_message_is_said_once_two_reads_agree_on_it()
    {
        var a = new A350FmsMessageAnnouncer();
        Assert.Null(a.Observe(Page(null)));
        Assert.Null(a.Observe(Page(null)));   // the baseline
        Assert.Null(a.Observe(Page("NAV ACCURACY UPGRADED")));
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, white", a.Observe(Page("NAV ACCURACY UPGRADED")));
        Assert.Null(a.Observe(Page("NAV ACCURACY UPGRADED")));
    }

    [Fact]
    public void A_text_seen_in_one_read_only_is_never_said()
    {
        var a = new A350FmsMessageAnnouncer();
        a.Observe(Page(null));
        a.Observe(Page(null));
        Assert.Null(a.Observe(Page("¨÷é")));   // a copy read across a frame
        Assert.Null(a.Observe(Page(null)));
        Assert.Null(a.Observe(Page(null)));
    }

    // Review 2026-10-30: a torn read reads "unreadable" since 2318eb76b, and two torn reads agree: "Scratchpad: unreadable"
    // was said, then the message again once whole reads came back. A torn read is no read.
    [Fact]
    public void A_torn_read_is_no_read()
    {
        var a = new A350FmsMessageAnnouncer();
        a.Observe(Page("CHECK T.O DATA"));
        a.Observe(Page("CHECK T.O DATA"));   // the baseline
        Assert.Null(a.Observe(Page(A350MfdPage.Unreadable)));
        Assert.Null(a.Observe(Page(A350MfdPage.Unreadable)));
        Assert.Null(a.Observe(Page("CHECK T.O DATA")));
        Assert.Null(a.Observe(Page("CHECK T.O DATA")));
        Assert.Equal("Scratchpad: CHECK T.O DATA, white", a.Agreed);
    }

    [Fact]
    public void The_first_agreed_read_is_the_baseline_and_an_emptied_scratchpad_says_nothing()
    {
        var a = new A350FmsMessageAnnouncer();
        Assert.Null(a.Observe(Page("CHECK TAKEOFF DATA")));
        Assert.Null(a.Observe(Page("CHECK TAKEOFF DATA")));   // showing as MSFSBA starts: the baseline
        Assert.Null(a.Observe(Page(null)));
        Assert.Null(a.Observe(Page(null)));   // cleared
    }

    [Fact]
    public void A_read_with_no_page_decides_nothing()
    {
        var a = new A350FmsMessageAnnouncer();
        a.Observe(Page(null));
        a.Observe(Page(null));
        Assert.Null(a.Observe(Page("NAV ACCURACY UPGRADED")));
        Assert.Null(a.Observe(Array.Empty<MfdLine>()));   // no page drawn: neither breaks nor makes an agreement
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, white", a.Observe(Page("NAV ACCURACY UPGRADED")));
    }

    [Fact]
    public void A_reset_takes_a_new_baseline()
    {
        var a = new A350FmsMessageAnnouncer();
        a.Observe(Page(null));
        a.Observe(Page(null));
        a.Reset();
        Assert.Null(a.Observe(Page("NAV ACCURACY UPGRADED")));
        Assert.Null(a.Observe(Page("NAV ACCURACY UPGRADED")));
    }
}

public class A350FmsMessageMonitorTests
{
    private static A350DrawCapture Load(string name) =>
        A350Fixtures.Capture(name + ".json");

    /// <summary>The APPROACH checklist page with the FMS message NAV ACCURACY UPGRADED in the scratchpad's place.</summary>
    private static readonly Lazy<A350DrawCapture> Posted = new(() => Load("mfd_cl_approach"));

    /// <summary>The same page before the message.</summary>
    private static readonly Lazy<A350DrawCapture> Quiet = new(() => Posted.Value with
    {
        Texts = Posted.Value.Texts.Select(t => t.Text == "NAV ACCURACY UPGRADED" ? t with { Text = " " } : t).ToList(),
    });

    private const string Said = "Scratchpad: NAV ACCURACY UPGRADED, white";

    private readonly List<string> _said = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Queue<A350DrawCapture> _mfd = new();
    private readonly A350PfdMonitor _monitor;

    public A350FmsMessageMonitorTests()
    {
        _monitor = new A350PfdMonitor(A350PfdMonitorTests.Each((g, i) => g == "MFD" && i == 0 && _mfd.Count > 0 ? _mfd.Dequeue() : null),
            _muted.Contains, _said.Add);
    }

    /// <summary>Ticks until each MFD capture given has been drawn (the MFD comes round in turn with the other displays).</summary>
    private async Task MfdReads(params A350DrawCapture[] captures)
    {
        foreach (var c in captures) _mfd.Enqueue(c);
        for (int guard = 0; _mfd.Count > 0 && guard < 100; guard++)
            await _monitor.TickAsync();
        Assert.Empty(_mfd);
    }

    [Fact]
    public async Task A_message_the_fms_posts_is_said_with_the_mfd_window_closed()
    {
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value, Posted.Value, Posted.Value);
        Assert.Equal(new[] { Said }, _said);
    }

    [Fact]
    public async Task Muted_it_says_nothing()
    {
        _muted.Add(A350FmsMessageAnnouncer.MuteKey);
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value, Posted.Value);
        Assert.Empty(_said);
    }

    // The MFD window says the message itself; what it said is not said again when it closes.
    [Fact]
    public async Task With_an_mfd_window_open_it_is_followed_silently()
    {
        bool open = true;
        _monitor.MfdWindowOpen = () => open;
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value, Posted.Value);
        open = false;
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Empty(_said);
    }

    // The window says a message on its own read. Closed before the background's second read of it, the window hands its
    // read back, so the background does not say it again (final review 2026-10-29: it was said twice).
    [Fact]
    public async Task A_message_the_window_said_is_not_said_again_after_it_closes()
    {
        bool open = false;
        _monitor.MfdWindowOpen = () => open;
        await MfdReads(Quiet.Value, Quiet.Value);
        var window = new A350MfdWindowMessages(_monitor.MfdWindowOpening(() => open = true));
        Assert.Null(window.Observe(A350MfdPage.Read(Quiet.Value, 0), 0));
        Assert.Equal(Said, window.Observe(A350MfdPage.Read(Posted.Value, 0), 0));
        await MfdReads(Posted.Value);   // the background's first read of it: pending
        _monitor.MfdWindowClosed(window.Captain, () => open = false);
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Empty(_said);
    }

    // What the window never saw is still said once it has closed.
    [Fact]
    public async Task A_message_posted_after_the_window_closes_is_said()
    {
        bool open = false;
        _monitor.MfdWindowOpen = () => open;
        var window = new A350MfdWindowMessages(_monitor.MfdWindowOpening(() => open = true));
        await MfdReads(Quiet.Value, Quiet.Value);
        window.Observe(A350MfdPage.Read(Quiet.Value, 0), 0);
        _monitor.MfdWindowClosed(window.Captain, () => open = false);
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Equal(new[] { Said }, _said);
    }

    // A message the background has read once as the window opens is the window's to say, from the background's agreed
    // read (final review 2026-10-29: the window took it as its silent first read, and neither said it).
    [Fact]
    public async Task A_message_pending_as_the_window_opens_is_said_by_the_window_once()
    {
        bool open = false;
        _monitor.MfdWindowOpen = () => open;
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value);
        var window = new A350MfdWindowMessages(_monitor.MfdWindowOpening(() => open = true));
        Assert.Equal(Said, window.Observe(A350MfdPage.Read(Posted.Value, 0), 0));
        await MfdReads(Posted.Value, Posted.Value);
        _monitor.MfdWindowClosed(window.Captain, () => open = false);
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Empty(_said);
    }

    // A capture the MFD reader cannot read (a torn copy that draws without trapping) costs that read of the message line,
    // never the tick: the E/WD gets its CTR on every tick, first (final review 2026-10-29).
    [Fact]
    public async Task A_capture_the_mfd_reader_cannot_read_costs_the_ewd_nothing()
    {
        int ctrs = 0, ticks = 0;
        _monitor.CtrDrawn = _ => ctrs++;
        _mfd.Enqueue(Posted.Value with { Texts = null! });
        for (; _mfd.Count > 0 && ticks < 100; ticks++)
            await _monitor.TickAsync();
        Assert.Equal(ticks, ctrs);
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value, Posted.Value);
        Assert.Equal(new[] { Said }, _said);
    }

    // The E/WD's reader is handed its CTR first now; one that throws costs the other readers nothing either.
    [Fact]
    public async Task An_ewd_reader_that_throws_costs_the_message_line_nothing()
    {
        _monitor.CtrDrawn = _ => throw new InvalidOperationException("a torn CTR");
        await MfdReads(Quiet.Value, Quiet.Value, Posted.Value, Posted.Value);
        Assert.Equal(new[] { Said }, _said);
    }

    // A dark MFD shows nothing: a message posted then is said as the screen lights with it showing.
    [Fact]
    public async Task A_dark_mfd_decides_nothing()
    {
        bool dark = false;
        _monitor.MfdDark = () => dark;
        await MfdReads(Quiet.Value, Quiet.Value);
        dark = true;
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Empty(_said);
        dark = false;
        await MfdReads(Posted.Value, Posted.Value);
        Assert.Equal(new[] { Said }, _said);
    }

    // A flight load: the new flight's scratchpad is its baseline.
    [Fact]
    public async Task After_a_flight_load_the_new_flight_s_message_is_its_baseline()
    {
        await MfdReads(Quiet.Value, Quiet.Value);
        _monitor.Rebaseline();
        await MfdReads(Posted.Value, Posted.Value, Posted.Value);
        Assert.Empty(_said);
    }

    [Fact]
    public void Its_ctrl_m_row_is_listed()
    {
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(new Dictionary<string, MSFSBlindAssist.SimConnect.SimVarDefinition>());
        Assert.Equal(A350FmsMessageAnnouncer.MuteLabel, Assert.Single(rows, r => r.Key == A350FmsMessageAnnouncer.MuteKey).Label);
    }
}

/// <summary>The MFD window's message line: said on the first read that shows a new message, starting from the background's
/// agreed read, and its last read of the captain's MFD handed back as it closes.</summary>
public class A350MfdWindowMessagesTests
{
    private static IReadOnlyList<MfdLine> Page(string? message) => A350FmsMessageAnnouncerTests.Page(message);

    [Fact]
    public void Its_first_read_is_said_against_the_background_s_agreed_read()
    {
        var w = new A350MfdWindowMessages("Scratchpad: empty");
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, white", w.Observe(Page("NAV ACCURACY UPGRADED"), 0));
        Assert.Null(w.Observe(Page("NAV ACCURACY UPGRADED"), 0));
    }

    [Fact]
    public void With_no_background_read_its_first_read_is_the_baseline()
    {
        var w = new A350MfdWindowMessages(null);
        Assert.Null(w.Observe(Page("NAV ACCURACY UPGRADED"), 0));
        Assert.Equal("Scratchpad: CHECK TAKEOFF DATA, white", w.Observe(Page("CHECK TAKEOFF DATA"), 0));
    }

    [Fact]
    public void A_torn_read_is_no_read_for_the_window_either()
    {
        var w = new A350MfdWindowMessages(null);
        Assert.Null(w.Observe(Page(A350MfdPage.Unreadable), 0));   // its first read, torn: not the baseline
        Assert.Null(w.Captain);
        Assert.Null(w.Observe(Page("NAV ACCURACY UPGRADED"), 0));   // the baseline
        Assert.Null(w.Observe(Page(A350MfdPage.Unreadable), 0));
        Assert.Null(w.Observe(Page("NAV ACCURACY UPGRADED"), 0));   // the same message, not said again
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, white", w.Captain);
    }

    [Fact]
    public void It_hands_back_only_what_it_read_on_the_captain_s_mfd()
    {
        var w = new A350MfdWindowMessages("Scratchpad: empty");
        Assert.Null(w.Captain);   // the background's own read is never handed back as the window's
        w.Observe(Page("NAV ACCURACY UPGRADED"), 0);
        w.Observe(Page("CHECK TAKEOFF DATA"), 1);
        w.Observe(Array.Empty<MfdLine>(), 0);   // no page: not a read
        Assert.Equal("Scratchpad: NAV ACCURACY UPGRADED, white", w.Captain);
    }
}
