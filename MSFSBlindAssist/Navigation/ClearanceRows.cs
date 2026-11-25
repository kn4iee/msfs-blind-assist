namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The pilot's per-row picks (hold shorts of every kind, Turn picks) are keyed by a row's position in
/// the clearance. When route loading removes rows from the clearance (a runway used as a taxi leg, a
/// first taxiway the route never drives [RTE-39]), every pick moves with the rows that remain, and a
/// pick on a removed row goes with it. One helper for every pick kind and every removal, so no map is
/// left keyed to rows that are no longer there.
/// </summary>
public static class ClearanceRows
{
    /// <summary>The first <paramref name="count"/> rows, for a removal from the front of the clearance.</summary>
    public static IReadOnlyCollection<int> Leading(int count) => Enumerable.Range(0, Math.Max(0, count)).ToList();

    /// <summary>The rows that remain once <paramref name="removedRows"/> (in the old numbering, any order)
    /// are gone, renumbered; null stays null.</summary>
    public static List<int>? Without(IReadOnlyList<int>? rows, IReadOnlyCollection<int> removedRows)
        => rows?.Where(row => !removedRows.Contains(row)).Select(row => Renumber(row, removedRows)).ToList();

    /// <inheritdoc cref="Without(IReadOnlyList{int}?, IReadOnlyCollection{int})"/>
    public static Dictionary<int, T>? Without<T>(IReadOnlyDictionary<int, T>? picks, IReadOnlyCollection<int> removedRows)
    {
        if (picks == null) return null;
        var result = new Dictionary<int, T>();
        foreach (var (row, pick) in picks)
        {
            if (removedRows.Contains(row)) continue;
            result[Renumber(row, removedRows)] = pick;
        }
        return result;
    }

    /// <summary>
    /// The stops in <paramref name="picks"/> (row, target) once <paramref name="removedRows"/> are gone: several may
    /// share a row, and all move with it; a stop on a removed row goes with it; a stop with no row of its own (a
    /// negative row) stays as it is. Null stays null.
    /// </summary>
    public static List<KeyValuePair<int, T>>? Without<T>(IReadOnlyList<KeyValuePair<int, T>>? picks,
                                                        IReadOnlyCollection<int> removedRows)
        => picks?.Where(p => p.Key < 0 || !removedRows.Contains(p.Key))
            .Select(p => p.Key < 0 ? p : new KeyValuePair<int, T>(Renumber(p.Key, removedRows), p.Value))
            .ToList();

    /// <summary>
    /// The rows the stops in <paramref name="picks"/> sit on, for <see cref="CountOn"/> and
    /// <c>UndrivenClearanceStart.PicksAllow</c>: a stop with no row of its own sits on none. Null stays null.
    /// </summary>
    public static IEnumerable<int>? RowsOf<T>(IReadOnlyList<KeyValuePair<int, T>>? picks)
        => picks?.Where(p => p.Key >= 0).Select(p => p.Key).ToList();

    /// <summary>
    /// The stops a recalculation re-applies to the rows still ahead, when the first <paramref name="rowsBehind"/> rows
    /// are behind the aircraft: a stop on one of those goes, one on a later row is numbered from the first row still
    /// ahead, and one with no row of its own stays with none.
    /// </summary>
    public static List<KeyValuePair<int, T>> After<T>(IReadOnlyList<KeyValuePair<int, T>> picks, int rowsBehind)
        => picks.Where(p => p.Key < 0 || p.Key >= rowsBehind)
            .Select(p => p.Key < 0 ? p : new KeyValuePair<int, T>(p.Key - rowsBehind, p.Value))
            .ToList();

    /// <summary>
    /// The crossings the pilot was cleared for once <paramref name="removedRows"/> are gone. A clearance to cross
    /// is not a stop on a row: one entered after a removed row stays, with no row of its own
    /// (<see cref="ClearedCrossing.SeqIndex"/> -1), so it still clears the first crossing of its runway in route
    /// order; a later one moves down with its row, and one with no row keeps none. Null stays null.
    /// </summary>
    public static List<ClearedCrossing>? Without(IReadOnlyList<ClearedCrossing>? crossings, IReadOnlyCollection<int> removedRows)
        => crossings?.Select(c => c.SeqIndex < 0 ? c
                : removedRows.Contains(c.SeqIndex) ? c with { SeqIndex = -1 }
                : c with { SeqIndex = Renumber(c.SeqIndex, removedRows) })
            .ToList();

    /// <summary>How many of the picks in <paramref name="pickRows"/> sit on one of
    /// <paramref name="removedRows"/>, and so go with it.</summary>
    public static int CountOn(IReadOnlyCollection<int> removedRows, params IEnumerable<int>?[] pickRows)
        => pickRows.Sum(rows => rows?.Count(removedRows.Contains) ?? 0);

    /// <summary>What the pilot hears when hold shorts went with a runway leg route loading skipped:
    /// a stop they set, and would otherwise expect, is not there. Null when none did.</summary>
    public static string? DroppedHoldShortsNote(int dropped) => dropped switch
    {
        <= 0 => null,
        1 => "The hold short set on a skipped runway leg was dropped.",
        _ => "The hold shorts set on skipped runway legs were dropped.",
    };

    private static int Renumber(int row, IReadOnlyCollection<int> removedRows) => row - removedRows.Count(r => r < row);
}

/// <summary>
/// The numbering of the clearance as the window entered it, for what guidance reports back about it
/// (<see cref="ClearanceProgressState"/>). Route loading removes rows from the clearance it was given (a runway used as
/// a taxi leg, a first taxiway the route never drives [RTE-39]) and numbers everything after them anew
/// (<see cref="ClearanceRows"/>); <see cref="RemovedRows"/> are those rows, numbered as entered, so a row, or a crossing's
/// Via step, can be numbered as entered again. <see cref="EnteredCrossings"/> are the crossings as entered: one whose
/// row went is held with none (<see cref="ClearanceRows.Without(IReadOnlyList{ClearedCrossing}?, IReadOnlyCollection{int})"/>),
/// and gets it back from there. Pure.
/// </summary>
public sealed record ClearanceNumbering(IReadOnlyList<int> RemovedRows, IReadOnlyList<ClearedCrossing> EnteredCrossings)
{
    /// <summary>Nothing removed, no crossings entered.</summary>
    public static readonly ClearanceNumbering None = new(Array.Empty<int>(), Array.Empty<ClearedCrossing>());

    /// <summary>A clearance as entered, with these crossings; nothing removed yet.</summary>
    public static ClearanceNumbering For(IReadOnlyList<ClearedCrossing>? enteredCrossings)
        => new(Array.Empty<int>(), enteredCrossings?.ToList() ?? new List<ClearedCrossing>());

    /// <summary>
    /// This numbering once <paramref name="rowsNow"/> are removed too: rows numbered as the clearance stands NOW, after
    /// every earlier removal (route loading removes runway legs first, then first taxiways never driven).
    /// </summary>
    public ClearanceNumbering Removing(IReadOnlyCollection<int> rowsNow)
    {
        var removed = new SortedSet<int>(RemovedRows);
        foreach (int row in rowsNow.Select(AsEntered).ToList())
            if (row >= 0) removed.Add(row);
        return this with { RemovedRows = removed.ToList() };
    }

    /// <summary>A row as the clearance stands now, numbered as entered; a negative row (none) stays as it is.</summary>
    public int AsEntered(int row)
    {
        if (row < 0) return row;
        int entered = row;
        foreach (int removed in RemovedRows)   // ascending
        {
            if (removed > entered) break;
            entered++;
        }
        return entered;
    }

    /// <summary>
    /// A crossing the pilot entered (<see cref="ClearedBy.Clearance"/>), with its Via step numbered as entered. One held
    /// with no row whose row was removed gets it back from <see cref="EnteredCrossings"/> (the same runway and place,
    /// entered with no row or on a removed row), unless two entered alike say different rows: then it keeps none. A
    /// crossing a Continue cleared stands for no step and comes back as it is.
    /// </summary>
    public ClearedCrossing AsEntered(ClearedCrossing crossing)
    {
        if (crossing.Origin != ClearedBy.Clearance) return crossing;
        if (crossing.SeqIndex >= 0) return crossing with { SeqIndex = AsEntered(crossing.SeqIndex) };
        var rows = EnteredCrossings
            .Where(e => e.Origin == ClearedBy.Clearance && e.Runway == crossing.Runway && e.At == crossing.At
                        && e.AtNodeId == crossing.AtNodeId && (e.SeqIndex < 0 || RemovedRows.Contains(e.SeqIndex)))
            .Select(e => e.SeqIndex)
            .Distinct()
            .ToList();
        return rows.Count == 1 ? crossing with { SeqIndex = rows[0] } : crossing;
    }
}
