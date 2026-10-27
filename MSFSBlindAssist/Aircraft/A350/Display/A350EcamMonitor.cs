using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's E/WD call-outs, window open or closed: the CTR drawn twice a second, each new warning-display
/// line spoken with its colours (A350EcamAnnouncer) unless the one Ctrl+M row is unticked; Ctrl+E flips
/// that row, as on the A380. It keeps the latest capture for the Alt+E window. Muted, it still follows the
/// display, so unmuting never replays old lines.
/// </summary>
public sealed class A350EcamMonitor : IDisposable
{
    public const string MuteKey = "A350_ECAM_WD";
    public const string MuteLabel = "ECAM E/WD call-outs";
    public const int PeriodMs = 500;

    private readonly Func<Task<A350DrawCapture?>> _drawCtr;
    private readonly Func<bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350EcamAnnouncer _announcer = new();
    private CancellationTokenSource? _cts;

    /// <summary>The latest CTR capture that drew without a trap, or null.</summary>
    public A350DrawCapture? LatestCapture { get; private set; }

    public A350EcamMonitor(Func<Task<A350DrawCapture?>> drawCtr, Func<bool> muted, Action<string> speak)
    {
        _drawCtr = drawCtr;
        _muted = muted;
        _speak = speak;
    }

    /// <summary>One read: draw, parse, speak what appeared.</summary>
    public async Task TickAsync()
    {
        var cap = await _drawCtr().ConfigureAwait(false);
        if (cap == null || !cap.Ok) return;
        LatestCapture = cap;
        var lines = _announcer.Observe(A350Ewd.Parse(cap));
        if (lines.Count == 0 || _muted()) return;
        foreach (var l in lines) _speak(l);
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
                catch (Exception ex) { Log.Warn("A350Display", "E/WD monitor: " + ex.Message); }
                try { await Task.Delay(PeriodMs, ct).ConfigureAwait(false); } catch (TaskCanceledException) { }
            }
        }, ct);
    }

    /// <summary>Flips the E/WD row in a Ctrl+M disabled list; true when the call-outs are now on.</summary>
    public static bool Toggle(ICollection<string> disabled)
    {
        if (disabled.Remove(MuteKey)) return true;
        disabled.Add(MuteKey);
        return false;
    }

    public static string ToggleSpoken(bool enabled) => enabled ? "E W D monitoring enabled" : "E W D monitoring disabled";

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
