using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 11 (2026-10-29): the FMS messages heard with the MFD window closed, as the A32NX's MCDU says them
/// ([A320-34]): the background display monitor already draws the MFD in turn, and a message the FMS posts in the scratchpad
/// is said once two reads agree on it, behind its own Ctrl+M row, followed silently while an MFD window is open (it says
/// them itself) and never decided from a dark MFD.</summary>
public class A350FmsMessageAnnouncerTests
{
    private static IReadOnlyList<MfdLine> Page(string? message) => message == null
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
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

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
