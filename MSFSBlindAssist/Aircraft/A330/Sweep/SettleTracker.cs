namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>The live sweep's timing (plan 04, Task 4 Step 2), in one place so the runner and its tests agree.</summary>
internal static class SweepTiming
{
    /// <summary>The least time between two writes to the aircraft.</summary>
    public const int WriteGapMs = 400;

    /// <summary>How often a read is polled while it settles.</summary>
    public const int PollMs = 100;

    /// <summary>The longest a write's read and proof are watched: a write that moved nothing is called dead only after it.</summary>
    public const int LimitMs = 5000;

    /// <summary>A pass that settled after this long is lagged (still a pass; its time goes to plan 06). It is also
    /// the panel's own read-back delay (<c>IniA330Definition.ReadBackDelayMs</c>), which plan 06 replaces by the
    /// times measured here.</summary>
    public const int LaggedMs = 1500;

    /// <summary>Two snapshots this far apart before the first write: what moves between them moves on its own,
    /// and is left out of every write's side effects.</summary>
    public const int NoiseWindowMs = 2000;

    /// <summary>Two readings closer than this are the same (the app's change tolerance).</summary>
    public const double SameTolerance = 0.001;

    public static bool Same(double? a, double? b) => a is double x && b is double y && Math.Abs(x - y) <= SameTolerance;

    /// <summary>How long the second write of a position is watched when it moves nothing: twice the time the first
    /// write took to settle (a flip back comes about as late as the first change did), at least the lagged threshold,
    /// at most the limit. A first write with no settle time (it never settled) gives the limit.</summary>
    public static int RepeatQuietMs(int? firstSettledMs) =>
        firstSettledMs is int ms ? Math.Clamp(2 * ms, LaggedMs, LimitMs) : LimitMs;
}

/// <summary>One reading to watch: how to read it, and its value before the write.</summary>
internal sealed record SettleChannel(Func<double?> Read, double? Baseline);

/// <summary>Where a watched reading ended. <see cref="SettledMs"/>: when (ms after the write) the value it settled on
/// was first read; null when it never moved from its baseline, or never settled (<see cref="TimedOut"/>).</summary>
internal sealed record SettleResult(double? Value, int? SettledMs, bool Moved, bool TimedOut);

/// <summary>
/// Watches readings after a write until they settle: polled every <see cref="SweepTiming.PollMs"/>, a reading is
/// settled when two reads in a row agree. Two reads of the value it had BEFORE the write are not yet a settle (the
/// aircraft may not have started to move): an unmoved reading is settled only once <c>quietMs</c> has passed.
/// Everything stops at <c>limitMs</c>. The clock and the sleep are injected, so it is pinned without a sim.
/// </summary>
internal static class SettleTracker
{
    public static SettleResult Track(Func<double?> read, double? baseline, Func<long> nowMs, Action<int> sleep,
        int quietMs = SweepTiming.LimitMs, int limitMs = SweepTiming.LimitMs) =>
        Track(new[] { new SettleChannel(read, baseline) }, nowMs, sleep, quietMs, limitMs)[0];

    /// <summary>Watches every channel together until all have settled, or the limit.</summary>
    public static IReadOnlyList<SettleResult> Track(IReadOnlyList<SettleChannel> channels, Func<long> nowMs, Action<int> sleep,
        int quietMs = SweepTiming.LimitMs, int limitMs = SweepTiming.LimitMs)
    {
        int n = channels.Count;
        var last = new double?[n];
        var runStart = new int[n];
        var stable = new bool[n];
        long start = nowMs();
        int t;
        while (true)
        {
            sleep(SweepTiming.PollMs);
            t = (int)(nowMs() - start);
            for (int i = 0; i < n; i++)
            {
                double? v = channels[i].Read();
                if (v is double d && last[i] is double p && SweepTiming.Same(d, p)) stable[i] = true;
                else { stable[i] = false; runStart[i] = t; }
                last[i] = v;
            }
            bool done = true;
            for (int i = 0; i < n && done; i++)
                done = stable[i] && (Moved(last[i], channels[i].Baseline) || t >= quietMs);
            if (done || t >= limitMs) break;
        }

        var results = new SettleResult[n];
        for (int i = 0; i < n; i++)
        {
            bool moved = Moved(last[i], channels[i].Baseline);
            results[i] = new SettleResult(last[i], stable[i] && moved ? runStart[i] : null, moved, TimedOut: !stable[i]);
        }
        return results;
    }

    /// <summary>A reading differs from its baseline. With no baseline (it was not read before) nothing can be said to
    /// have moved: the reading is "not read" before, never "moved".</summary>
    private static bool Moved(double? value, double? baseline) =>
        value is double v && baseline is double b && Math.Abs(v - b) > SweepTiming.SameTolerance;
}
