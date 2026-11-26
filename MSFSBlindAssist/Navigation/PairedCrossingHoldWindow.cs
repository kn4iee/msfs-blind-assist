namespace MSFSBlindAssist.Navigation;

/// <summary>
/// When a second Continue counts as "cleared across both" for a paired-runway crossing
/// (<c>TaxiGuidanceManager.ContinuePastHoldShort</c>). The window opens <see cref="MinDelaySeconds"/>
/// after the offer, never at once, so a key bounce or a quick double press cannot clear the second
/// runway's stop before the pilot has heard the offer (PR #324 review). A press before it opens is
/// ignored and keeps the stop: the safe direction. The same delay guards a Continue before the line
/// pressed straight after the Continue that resumed taxiing (<see cref="IgnoresEarlyContinue"/>).
/// </summary>
public static class PairedCrossingHoldWindow
{
    /// <summary>Longer than a key bounce or a quick double press, shorter than the offer sentence.</summary>
    public const double MinDelaySeconds = 2.0;

    /// <summary>How long the offer stands.</summary>
    public const double WindowSeconds = 12.0;

    /// <summary>The window an offer made at <paramref name="offeredAt"/> opens.</summary>
    public static (DateTime From, DateTime Until) Open(DateTime offeredAt)
        => (offeredAt.AddSeconds(MinDelaySeconds), offeredAt.AddSeconds(WindowSeconds));

    /// <summary>True when <paramref name="now"/> lies inside [<paramref name="from"/>, <paramref name="until"/>].</summary>
    public static bool Accepts(DateTime now, DateTime from, DateTime until)
        => now >= from && now <= until;

    /// <summary>
    /// True when a Continue before the line at <paramref name="now"/> comes less than <see cref="MinDelaySeconds"/>
    /// after the Continue that resumed taxiing at <paramref name="resumedAt"/> (<see cref="DateTime.MinValue"/>: none),
    /// and is ignored (owner, 2026-11-25: "Ignore it"). A quick double press at a hold would otherwise clear the next
    /// runway's stop: one under about 90 m ahead is counted down in the first frame after the resume, which is all
    /// <c>ClearedCrossingHolds.CanClearEarly</c> asks. A press after the delay works as before.
    /// </summary>
    public static bool IgnoresEarlyContinue(DateTime now, DateTime resumedAt)
        => resumedAt != DateTime.MinValue && now >= resumedAt && now < resumedAt.AddSeconds(MinDelaySeconds);
}
