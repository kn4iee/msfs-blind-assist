namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// Decides which of the 757's state call-outs (flaps, gear, speedbrake, parking brake, the MCP
/// lights, the EXEC light) are spoken: a phrase per row, spoken when it CHANGES. Pure; the definition
/// feeds it deliveries on the UI thread, with the time.
/// <list type="bullet">
/// <item>Baseline-first: the first phrase of a row after connecting or a flight load is remembered,
/// never spoken (the cockpit as found is no change).</item>
/// <item>An unchanged phrase is silent (a forced re-read, a jitter inside one phrase).</item>
/// <item>An echo window: the app's own press or pick of the control that moves this row arms it, and
/// a change inside it is tracked, never spoken. The press says its own read-back ("CMD R on",
/// CORE-7), or the screen reader read the pick; the change would say it a second time.</item>
/// </list>
/// </summary>
public sealed class Bbs757ChangeTracker
{
    private readonly Dictionary<string, string> _last = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _echoUntil = new(StringComparer.Ordinal);

    /// <summary>The phrase to speak for this delivery, or null (a baseline, no change, or an echo).</summary>
    public string? Observe(string row, string phrase, long nowMs)
    {
        bool first = !_last.TryGetValue(row, out string? last);
        _last[row] = phrase;
        if (first || last == phrase) return null;
        if (_echoUntil.TryGetValue(row, out long until) && nowMs < until) return null;
        return phrase;
    }

    /// <summary>A change of <paramref name="row"/> before <paramref name="untilMs"/> is the app's own write's echo.</summary>
    public void ArmEcho(string row, long untilMs) => _echoUntil[row] = untilMs;

    /// <summary>A read-back spoke this phrase: it is the row's state now, so a delivery of it is no change.</summary>
    public void Rebaseline(string row, string phrase) => _last[row] = phrase;

    /// <summary>The next phrase of every row is a baseline again (a reconnect or a flight load).</summary>
    public void Reset()
    {
        _last.Clear();
        _echoUntil.Clear();
    }
}
