using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's PFDs in the background: the captain's EFIS (side 0) drawn every tick and its FMA changes spoken
/// with colours (A350FmaAnnouncer), each behind its column's Ctrl+M row; the first officer's (side 1) drawn
/// every other tick. Each side's latest good draw is kept for its PFD status display. Muted, it still follows
/// the FMA, so unmuting never replays old modes.
/// </summary>
public sealed class A350PfdMonitor : IDisposable
{
    public const int PeriodMs = 500;

    private readonly Func<int, Task<A350DrawCapture?>> _drawEfis;
    private readonly Func<string, bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350FmaAnnouncer _fma = new();
    private readonly A350DrawCapture?[] _latest = new A350DrawCapture?[2];
    private int _tick;
    private CancellationTokenSource? _cts;

    public A350PfdMonitor(Func<int, Task<A350DrawCapture?>> drawEfis, Func<string, bool> muted, Action<string> speak)
    {
        _drawEfis = drawEfis;
        _muted = muted;
        _speak = speak;
    }

    /// <summary>A side's latest EFIS draw that did not trap, or null.</summary>
    public A350DrawCapture? Latest(int side) => _latest[side];

    public async Task TickAsync()
    {
        var cpt = await _drawEfis(0).ConfigureAwait(false);
        if (cpt is { Ok: true })
        {
            _latest[0] = cpt;
            foreach (var (key, line) in _fma.Observe(A350Pfd.Read(cpt, 0)))
                if (!_muted(key)) _speak(line);
        }
        if (_tick++ % 2 == 1 && await _drawEfis(1).ConfigureAwait(false) is { Ok: true } fo)
            _latest[1] = fo;
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
                catch (Exception ex) { Log.Warn("A350Display", "PFD monitor: " + ex.Message); }
                try { await Task.Delay(PeriodMs, ct).ConfigureAwait(false); } catch (TaskCanceledException) { }
            }
        }, ct);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
