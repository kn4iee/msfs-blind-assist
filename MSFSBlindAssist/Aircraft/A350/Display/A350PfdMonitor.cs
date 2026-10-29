using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's displays in the background: the captain's EFIS drawn every tick and its FMA changes spoken with
/// colours (A350FmaAnnouncer), each behind its column's Ctrl+M row; the first officer's EFIS every other tick;
/// and two of the other displays (MFD, FCU, RMPs, standby displays, BAT) each tick in turn, a tick's displays all
/// drawn on one memory copy. Each display's latest good draw is kept for its status display. Muted, it still
/// follows the FMA, so unmuting never replays old modes. The captain's MFD, as it comes round, gives the FMS messages
/// (A350FmsMessageAnnouncer), heard with the MFD window closed, behind their own Ctrl+M row.
/// </summary>
public sealed class A350PfdMonitor : IDisposable
{
    public const int PeriodMs = 500;

    /// <summary>How old a display's latest draw may be and still be read as its display (review 2026-10-29: kept for ever,
    /// the last flight's displays were read as live after the reader stopped). A display drawn in turn comes round every
    /// few seconds, up to about half a minute with a large memory; an older draw is no draw.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    /// <summary>The displays drawn in turn besides the EFIS.</summary>
    public static readonly IReadOnlyList<(string Gauge, int Index)> Others = new[]
    {
        ("MFD", 0), ("FCU", 0), ("RMP", 0), ("RMP", 1), ("RMP", 2), ("SFD", 0), ("SND", 0), ("BAT", 0),
    };

    private readonly Func<(string Gauge, int Index)[], Task<IReadOnlyList<A350DrawCapture?>?>> _draw;
    private readonly Func<string, bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350FmaAnnouncer _fma = new();
    private readonly A350FmsMessageAnnouncer _messages = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), (A350DrawCapture Capture, DateTime At)> _latest = new();
    private readonly Func<DateTime> _now;
    private int _tick, _other;
    private CancellationTokenSource? _cts;
    private volatile bool _rebaseline, _rebaselineMessages;
    private DateTime? _settleUntil;   // the reading loop's own

    /// <summary>A flight was loaded: the last flight's draws are dropped, and the new flight's FMA, from its first draw, is
    /// followed silently for <see cref="A350EcamMonitor.SettleAfterLoad"/> (review 2026-10-29: each column flight B
    /// showed differently from flight A was spoken); the reading loop resets the announcer itself, which only it touches.</summary>
    public void Rebaseline()
    {
        _latest.Clear();
        _rebaseline = true;
        _rebaselineMessages = true;   // the new flight's message line is its baseline, whichever read comes first
    }

    /// <summary>True while an MFD window is open: it says the FMS messages itself, so they are followed silently here (and
    /// what it said is never said again as it closes).</summary>
    public Func<bool>? MfdWindowOpen { get; set; }

    /// <summary>True while the MFD is dark: it shows no message, so its draw decides nothing.</summary>
    public Func<bool>? MfdDark { get; set; }

    /// <param name="draw">Draws the displays asked for on one memory copy, a capture (or null) for each in order; null
    /// for all while the reader cannot draw.</param>
    /// <param name="now">The time a draw arrives and is read (tests replace it).</param>
    public A350PfdMonitor(Func<(string Gauge, int Index)[], Task<IReadOnlyList<A350DrawCapture?>?>> draw, Func<string, bool> muted, Action<string> speak,
        Func<DateTime>? now = null)
    {
        _draw = draw;
        _muted = muted;
        _speak = speak;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>The E/WD's reader (<see cref="A350EcamMonitor.Observe"/>): when set, each tick also draws the CTR, first,
    /// on its own memory copy, and hands it over (review 2026-10-29: the E/WD monitor took a full copy of its own every
    /// cycle, about 650 ms at 495 MB). First: its warnings matter most, and a request retries only one trapped draw.</summary>
    public Action<A350DrawCapture?>? CtrDrawn { get; set; }

    /// <summary>A side's latest EFIS draw that did not trap, or null.</summary>
    public A350DrawCapture? Latest(int side) => Latest("EFIS", side);

    /// <summary>A display's latest draw that did not trap, or null when there is none younger than <see cref="MaxAge"/>.</summary>
    public A350DrawCapture? Latest(string gauge, int index) =>
        _latest.TryGetValue((gauge, index), out var kept) && _now() - kept.At <= MaxAge ? kept.Capture : null;

    /// <summary>One tick: the captain's EFIS, every other tick the first officer's, and two of the others, asked for
    /// at once so they are drawn on one memory copy (live 2026-10-28, with the A350's memory at 495 MB, one copy took
    /// 650 ms, and a tick asking display by display made three or four).</summary>
    public async Task TickAsync()
    {
        var ctr = CtrDrawn;
        var gauges = new List<(string Gauge, int Index)>();
        if (ctr != null) gauges.Add(("CTR", 0));
        int cptAt = gauges.Count;
        gauges.Add(("EFIS", 0));
        if (_tick++ % 2 == 1) gauges.Add(("EFIS", 1));
        for (int i = 0; i < 2; i++)
            gauges.Add(Others[_other++ % Others.Count]);
        if (await _draw(gauges.ToArray()).ConfigureAwait(false) is not { } caps) return;
        var at = _now();
        for (int n = 0; n < gauges.Count && n < caps.Count; n++)
            if (caps[n] is { Ok: true } cap) _latest[gauges[n]] = (cap, at);
        if (cptAt < caps.Count && caps[cptAt] is { Ok: true } cpt)
        {
            if (_rebaseline)
            {
                _rebaseline = false;
                _fma.Reset();
                _settleUntil = at + A350EcamMonitor.SettleAfterLoad;
            }
            bool settling = _settleUntil is { } until && at < until;
            foreach (var (key, line) in _fma.Observe(A350Pfd.Read(cpt, 0)))
                if (!settling && !_muted(key) && !_disposed) _speak(line);
        }
        int mfdAt = gauges.IndexOf(("MFD", 0));
        if (mfdAt >= 0 && mfdAt < caps.Count && caps[mfdAt] is { Ok: true } mfd)
            ObserveMessages(mfd, at);
        if (ctr != null && caps.Count > 0) ctr(caps[0]);
    }

    /// <summary>The captain's MFD drawn: a message the FMS just posted is said, unless muted, settling after a load, or said
    /// by an open MFD window.</summary>
    private void ObserveMessages(A350DrawCapture mfd, DateTime at)
    {
        if (_rebaselineMessages)
        {
            _rebaselineMessages = false;
            _messages.Reset();
        }
        if (MfdDark?.Invoke() == true)
            return;
        bool settling = _settleUntil is { } until && at < until;
        if (_messages.Observe(A350MfdPage.Read(mfd, 0)) is { } message
            && !settling && MfdWindowOpen?.Invoke() != true && !_muted(A350FmsMessageAnnouncer.MuteKey) && !_disposed)
            _speak(message);
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { await TickAsync().ConfigureAwait(false); }
                catch (Exception ex) { Log.Warn("A350Display", "display monitor: " + ex.Message); }
                try { await Task.Delay(PeriodMs, ct).ConfigureAwait(false); } catch (TaskCanceledException) { }
            }
        }, ct);
    }

    /// <summary>Set by <see cref="Dispose"/>: a tick that ends after it (an aircraft switch mid-draw) says nothing (review
    /// 2026-10-29).</summary>
    private volatile bool _disposed;

    public void Dispose()
    {
        _disposed = true;
        _cts?.Cancel();
        _cts = null;
    }
}
