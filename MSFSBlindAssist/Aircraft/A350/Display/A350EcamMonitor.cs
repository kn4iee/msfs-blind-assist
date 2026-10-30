namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's E/WD call-outs, window open or closed: each CTR draw the display monitor's tick makes, first, on the
/// tick's one memory copy (<see cref="A350PfdMonitor.CtrDrawn"/>), each new warning-display line spoken with its colours
/// (A350EcamAnnouncer) unless the one Ctrl+M row is unticked; Ctrl+E flips that row, as on the A380. It keeps the
/// latest capture for the Alt+E window. Muted, it still follows the display, so unmuting never replays old lines.
/// </summary>
public sealed class A350EcamMonitor : IDisposable
{
    public const string MuteKey = "A350_ECAM_WD";
    public const string MuteLabel = "ECAM E/WD call-outs";

    /// <summary>How long after a flight load (<see cref="Rebaseline"/>) the display call-outs follow the new flight
    /// silently, from its first draw: what it shows as it loads is its baseline, never news (review 2026-10-29: each line
    /// flight B showed that flight A lacked was spoken as new).</summary>
    public static readonly TimeSpan SettleAfterLoad = TimeSpan.FromSeconds(10);

    private volatile bool _rebaseline;
    private DateTime? _settleUntil;   // the reading loop's own

    private readonly Func<bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350EcamAnnouncer _announcer = new();
    private readonly Func<DateTime> _now;
    private sealed record Kept(A350DrawCapture Capture, DateTime At);
    private volatile Kept? _latest;   // one reference, written by the display tick, read by the windows

    /// <summary>The latest CTR capture that drew without a trap, or null when there is none younger than
    /// <see cref="A350PfdMonitor.MaxAge"/> (review 2026-10-29: Alt+E read the last capture whatever its age).</summary>
    public A350DrawCapture? LatestCapture => _latest is { } kept && _now() - kept.At <= A350PfdMonitor.MaxAge ? kept.Capture : null;

    /// <param name="now">The time a draw arrives and is read (tests replace it).</param>
    public A350EcamMonitor(Func<bool> muted, Action<string> speak, Func<DateTime>? now = null)
    {
        _muted = muted;
        _speak = speak;
        _now = now ?? (() => DateTime.UtcNow);
    }

    /// <summary>One CTR draw read: parse, speak what appeared; nothing for no draw or a trapped one.</summary>
    public void Observe(A350DrawCapture? cap)
    {
        if (cap == null || !cap.Ok) return;
        var now = _now();
        _latest = new Kept(cap, now);
        if (_rebaseline)
        {
            _rebaseline = false;
            _announcer.Reset();
            _settleUntil = now + SettleAfterLoad;
        }
        var lines = _announcer.Observe(A350Ewd.Parse(cap));
        if (lines.Count == 0 || _disposed) return;
        string? quiet = _settleUntil is { } until && now < until ? "settling after a load" : _muted() ? "muted" : null;
        foreach (var l in lines)
        {
            if (quiet != null) A350SpeechLog.Quiet("E/WD", quiet, l);
            else _speak(l);
        }
    }

    /// <summary>A flight was loaded: the last flight's E/WD is dropped, and the new one's, from its first draw, is followed
    /// silently for <see cref="SettleAfterLoad"/> (the reading loop resets the announcer itself, which only it touches).</summary>
    public void Rebaseline()
    {
        _latest = null;
        _rebaseline = true;
    }

    /// <summary>Flips the E/WD row in a Ctrl+M disabled list; true when the call-outs are now on.</summary>
    public static bool Toggle(ICollection<string> disabled)
    {
        if (disabled.Remove(MuteKey)) return true;
        disabled.Add(MuteKey);
        return false;
    }

    public static string ToggleSpoken(bool enabled) => enabled ? "E W D monitoring enabled" : "E W D monitoring disabled";

    /// <summary>Set by <see cref="Dispose"/>: a tick that ends after it (an aircraft switch mid-draw) says nothing (review
    /// 2026-10-29).</summary>
    private volatile bool _disposed;

    public void Dispose()
    {
        _disposed = true;
    }
}
