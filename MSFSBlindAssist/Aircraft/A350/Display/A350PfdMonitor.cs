using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's displays in the background: the captain's EFIS drawn every tick and its FMA changes spoken with
/// colours (A350FmaAnnouncer), each behind its column's Ctrl+M row; the first officer's EFIS every other tick;
/// and two of the other displays (MFD, FCU, RMPs, standby displays, BAT) each tick in turn. Each display's
/// latest good draw is kept for its status display. Muted, it still follows the FMA, so unmuting never replays
/// old modes.
/// </summary>
public sealed class A350PfdMonitor : IDisposable
{
    public const int PeriodMs = 500;

    /// <summary>The displays drawn in turn besides the EFIS.</summary>
    public static readonly IReadOnlyList<(string Gauge, int Index)> Others = new[]
    {
        ("MFD", 0), ("FCU", 0), ("RMP", 0), ("RMP", 1), ("RMP", 2), ("SFD", 0), ("SND", 0), ("BAT", 0),
    };

    private readonly Func<string, int, Task<A350DrawCapture?>> _draw;
    private readonly Func<string, bool> _muted;
    private readonly Action<string> _speak;
    private readonly A350FmaAnnouncer _fma = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string, int), A350DrawCapture> _latest = new();
    private int _tick, _other;
    private CancellationTokenSource? _cts;

    public A350PfdMonitor(Func<string, int, Task<A350DrawCapture?>> draw, Func<string, bool> muted, Action<string> speak)
    {
        _draw = draw;
        _muted = muted;
        _speak = speak;
    }

    /// <summary>A side's latest EFIS draw that did not trap, or null.</summary>
    public A350DrawCapture? Latest(int side) => Latest("EFIS", side);

    /// <summary>A display's latest draw that did not trap, or null.</summary>
    public A350DrawCapture? Latest(string gauge, int index) => _latest.GetValueOrDefault((gauge, index));

    private async Task<A350DrawCapture?> Draw(string gauge, int index)
    {
        var cap = await _draw(gauge, index).ConfigureAwait(false);
        if (cap is { Ok: true }) _latest[(gauge, index)] = cap;
        return cap is { Ok: true } ? cap : null;
    }

    public async Task TickAsync()
    {
        if (await Draw("EFIS", 0).ConfigureAwait(false) is { } cpt)
            foreach (var (key, line) in _fma.Observe(A350Pfd.Read(cpt, 0)))
                if (!_muted(key)) _speak(line);
        if (_tick++ % 2 == 1) await Draw("EFIS", 1).ConfigureAwait(false);
        for (int i = 0; i < 2; i++)
        {
            var (gauge, index) = Others[_other++ % Others.Count];
            await Draw(gauge, index).ConfigureAwait(false);
        }
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

    public void Dispose()
    {
        _cts?.Cancel();
        _cts = null;
    }
}
