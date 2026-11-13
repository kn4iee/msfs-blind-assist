namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Which draw a display window shows on each side (plan 26's re-review), shared by the display windows and the ND window. A press's
/// fresh draw is shown over the monitor's draw from before it (<see cref="A350ShownCapture"/>), on its own side only, so a side switched
/// while it was drawn, or switched back to, shows its own display. A newer draw of the monitor's replaces it, and it is dropped then, so it
/// is never shown again. With no draw of the monitor's at all (just after a flight load, or none young enough) it stands while it is
/// younger than the monitor's own limit (<see cref="A350PfdMonitor.MaxAge"/>), never longer. UI thread only.
/// </summary>
public sealed class A350FreshDraws
{
    private readonly A350ShownCapture[] _shown = { new(), new() };
    private readonly (A350DrawCapture? Draw, long At)[] _fresh = new (A350DrawCapture?, long)[2];
    private readonly Func<long> _clock;
    private readonly long _maxAgeMs;

    /// <param name="clock">Milliseconds, steadily rising (tests replace it).</param>
    /// <param name="maxAgeMs">How long a fresh draw stands with no draw of the monitor's.</param>
    public A350FreshDraws(Func<long> clock, long maxAgeMs)
    {
        _clock = clock;
        _maxAgeMs = maxAgeMs;
    }

    /// <summary>A fresh draw of <paramref name="side"/>, taken while the monitor's latest of that side was <paramref name="latestNow"/>.</summary>
    public void Took(int side, A350DrawCapture fresh, A350DrawCapture? latestNow)
    {
        _shown[side].Fresh(latestNow);
        _fresh[side] = (fresh, _clock());
    }

    /// <summary>The draw to show for <paramref name="side"/>, the monitor's <paramref name="latest"/> being what it is now.</summary>
    public A350DrawCapture? ToShow(int side, A350DrawCapture? latest)
    {
        var (fresh, at) = _fresh[side];
        bool young = fresh != null && _clock() - at <= _maxAgeMs;
        if (latest == null) return young ? fresh : null;
        if (_shown[side].Superseded(latest)) return young ? fresh : latest;
        _fresh[side] = (null, 0);   // a draw newer than the fresh one: it has done its work
        return latest;
    }
}
