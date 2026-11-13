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

    /// <summary>The reverser call-outs' own Ctrl+M row (<see cref="A350CalloutRows.Ecam"/>).</summary>
    public const string ReverserKey = "A350_ECAM_REVERSERS", ReverserLabel = "Reverser call-outs";

    private readonly A350ReverserCallout _reversers = new();

    /// <summary>How long after a flight load (<see cref="Rebaseline"/>) the display call-outs follow the new flight
    /// silently, from its first draw: what it shows as it loads is its baseline, never news (review 2026-10-29: each line
    /// flight B showed that flight A lacked was spoken as new).</summary>
    public static readonly TimeSpan SettleAfterLoad = TimeSpan.FromSeconds(10);

    private volatile bool _rebaseline;
    private DateTime? _settleUntil;   // the reading loop's own

    private readonly Func<string, bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350EcamAnnouncer _announcer;
    private readonly Func<DateTime> _now;
    private sealed record Kept(A350DrawCapture Capture, DateTime At);
    private volatile Kept? _latest;   // one reference, written by the display tick, read by the windows

    /// <summary>The latest CTR capture that drew without a trap, or null when there is none younger than
    /// <see cref="A350PfdMonitor.MaxAge"/> (review 2026-10-29: Alt+E read the last capture whatever its age).</summary>
    public A350DrawCapture? LatestCapture => _latest is { } kept && _now() - kept.At <= A350PfdMonitor.MaxAge ? kept.Capture : null;

    /// <param name="muted">Whether a Ctrl+M row is unticked, asked by its key (the E/WD row's is <see cref="MuteKey"/>).</param>
    /// <param name="now">The time a draw arrives and is read (tests replace it).</param>
    public A350EcamMonitor(Func<string, bool> muted, Action<string> speak, Func<DateTime>? now = null)
    {
        _muted = muted;
        _speak = speak;
        _now = now ?? (() => DateTime.UtcNow);
        _announcer = new A350EcamAnnouncer(_now);
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
        var view = A350Ewd.Parse(cap);
        var lines = _announcer.Observe(view);
        var reverse = _reversers.Observe(A350Ewd.Reversers(view), now);
        if (_disposed) return;
        bool settling = _settleUntil is { } until && now < until;
        Speak(lines, settling, MuteKey);
        Speak(reverse, settling, ReverserKey);
    }

    /// <summary>Says what a call-out found, its row asked only when there is something to say.</summary>
    private void Speak(IReadOnlyList<string> lines, bool settling, string rowKey)
    {
        if (lines.Count == 0) return;
        string? quiet = settling ? "settling after a load" : _muted(rowKey) ? "muted" : null;
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

/// <summary>
/// The thrust reversers called as a pilot monitoring calls them (plan 04, from the landing of 2026-11-02 on 1.2.8), from the E/WD's
/// "REV" over each engine: "Reverse green" once every engine showing REV reads green (two reads in a row agreeing, [A350-2]), the
/// engine named when only one shows it; an engine whose REV stays amber past <see cref="AmberHold"/> named once ("Reverse 2 amber").
/// Stowed, it starts again. Pure; the levers' own sentences are A350LeverSpeech's.
/// </summary>
public sealed class A350ReverserCallout
{
    public static readonly TimeSpan AmberHold = TimeSpan.FromSeconds(3);

    private string? _previous;              // the last read, as one key
    private bool _greenSaid;
    private readonly Dictionary<int, DateTime> _amberSince = new();
    private readonly HashSet<int> _amberSaid = new();

    public IReadOnlyList<string> Observe(IReadOnlyList<(int Engine, string Colour)> reversers, DateTime now)
    {
        string key = string.Join(";", reversers.Select(r => r.Engine + r.Colour));
        bool agreed = key == _previous;
        _previous = key;
        if (reversers.Count == 0)
        {
            _greenSaid = false;
            _amberSince.Clear();
            _amberSaid.Clear();
            return Array.Empty<string>();
        }
        var said = new List<string>();
        foreach (var (engine, colour) in reversers)
        {
            if (colour != "amber")
            {
                _amberSince.Remove(engine);
                _amberSaid.Remove(engine);   // an amber that comes back (hanging as it stows) is said again
                continue;
            }
            if (!_amberSince.TryGetValue(engine, out var since))
                _amberSince[engine] = since = now;
            if (now - since >= AmberHold && _amberSaid.Add(engine))
                said.Add($"Reverse {engine} amber");
        }
        if (agreed && !_greenSaid && reversers.All(r => r.Colour == "green"))
        {
            _greenSaid = true;
            said.Add(reversers.Count == 1 ? $"Reverse {reversers[0].Engine} green" : "Reverse green");
        }
        return said;
    }
}
