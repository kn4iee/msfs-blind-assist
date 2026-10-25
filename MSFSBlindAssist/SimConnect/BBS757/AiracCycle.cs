namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The AIRAC cycle in force on a date. Cycles run 28 days, anchored on 2501 effective 2025-01-23,
/// and are numbered by the calendar year of their effective date: YY then the cycle's ordinal in
/// that year, restarting at 01 with the first cycle effective in the year (2610 is effective
/// 2026-10-01; 2020 had fourteen, its last 2014 effective 2020-12-31). Pure.
///
/// The 757 EFB window compares BlueBird's installed cycle with this one: the AIRAC page's UPDATE
/// button turns into REMOVE once the installed cycle is current, so the app must know before it
/// clicks there (<see cref="Bbs757EfbLayout.ShouldUpdate"/>). Pass the UTC date: cycles change at
/// 00:00 UTC.
/// </summary>
public static class AiracCycle
{
    private const int CycleDays = 28;
    // Cycle 2501, the first cycle effective in 2025.
    private static readonly DateOnly Anchor = new(2025, 1, 23);

    /// <summary>The cycle number (YYNN, for example 2610) in force on <paramref name="date"/>.</summary>
    public static int ForDate(DateOnly date)
    {
        // The cycle in force: the last one effective on or before the date, k cycles from the anchor.
        int k = FloorDiv(date.DayNumber - Anchor.DayNumber, CycleDays);
        int year = Anchor.AddDays(k * CycleDays).Year;

        // The first cycle effective in that calendar year, counted the same way.
        int first = CeilDiv(new DateOnly(year, 1, 1).DayNumber - Anchor.DayNumber, CycleDays);
        return (year % 100) * 100 + (k - first + 1);
    }

    /// <summary>
    /// The date cycle <paramref name="cycle"/> (YYNN) took or takes effect, 00:00 UTC. Throws for a
    /// number no cycle has (NN past the year's last cycle, or not a cycle number at all).
    /// </summary>
    public static DateOnly EffectiveDate(int cycle)
    {
        if (!IsCycleNumber(cycle)) throw new ArgumentOutOfRangeException(nameof(cycle), cycle, "Not an AIRAC cycle number.");
        int year = 2000 + cycle / 100;
        int first = CeilDiv(new DateOnly(year, 1, 1).DayNumber - Anchor.DayNumber, CycleDays);
        var effective = Anchor.AddDays((first + cycle % 100 - 1) * CycleDays);
        if (effective.Year != year)
            throw new ArgumentOutOfRangeException(nameof(cycle), cycle, "That year has fewer cycles.");
        return effective;
    }

    /// <summary>
    /// Whether <paramref name="cycle"/> is shaped like a cycle number: four digits, its last two
    /// 01 to 14.
    /// </summary>
    public static bool IsCycleNumber(int cycle) => cycle is >= 1000 and <= 9999 && cycle % 100 is >= 1 and <= 14;

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

    private static int CeilDiv(int a, int b) => a >= 0 ? (a + b - 1) / b : -(-a / b);
}
