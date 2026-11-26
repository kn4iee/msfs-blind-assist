namespace MSFSBlindAssist.Navigation;

/// <summary>What a runway entry list item stands for.</summary>
public enum RunwayEntryKind { FullLength, Intersection, HoldingPoint }

/// <summary>One line of the runway entry list. <see cref="ToString"/> is the label, so a list box shows it.</summary>
public sealed record RunwayEntryItem(
    RunwayEntryKind Kind, string Label, TaxiGraph.RunwayIntersection? Entry, bool Partway, string HoldKind)
{
    /// <summary>The holding point's OSM kind as the label reads it: trimmed, lower case, "" when none.</summary>
    public string HoldKind { get; init; } = RunwayEntryList.NormaliseKind(HoldKind);

    /// <summary>The entry's taxiway name, or "Full length".</summary>
    public string Name => Entry?.TaxiwayName ?? RunwayEntryList.FullLength;

    public override string ToString() => Label;
}

/// <summary>What a refill of the runway entry list does: rebuild the shown lines or leave them, which item to
/// select, and what to say (null: nothing).</summary>
public readonly record struct RunwayEntryRefill(bool Rebuild, int SelectIndex, string? Speech);

/// <summary>
/// Merges the two departure lists the planner used to offer, intersections and painted holding
/// points, into one list ordered down the runway from its takeoff end, "Full length" first.
/// </summary>
public static class RunwayEntryList
{
    public const string FullLength = "Full length";

    // [ENT-2]: the most a refill moves one entry's remaining runway (its pavement entry on the aircraft's side); a
    // match further away is another meeting of the taxiway.
    public const double MaxRefillMoveMetres = 300.0;

    /// <summary>
    /// What a refill of the shown list does with the <paramref name="rebuilt"/> one. The same lines leave the list as
    /// it is (a refill on focus must not make the screen reader read it again). With <paramref name="keepSelection"/>
    /// (the list getting focus or dropping down) the item at <paramref name="chosenIndex"/> stays chosen where it is
    /// now (<see cref="IndexToKeep"/>); when it is gone the list goes to Full length and says so once
    /// (<see cref="NoLongerOffered"/>), a background change the pilot did not make. Without it (a runway picked, a type
    /// change, an airport load) the list starts at Full length, silently.
    /// </summary>
    public static RunwayEntryRefill Refill(
        IReadOnlyList<RunwayEntryItem> shown, int chosenIndex, IReadOnlyList<RunwayEntryItem> rebuilt, bool keepSelection)
    {
        bool sameLines = shown.Count == rebuilt.Count
            && shown.Select(i => i.Label).SequenceEqual(rebuilt.Select(i => i.Label), StringComparer.Ordinal);
        RunwayEntryItem? chosen = keepSelection && chosenIndex >= 0 && chosenIndex < shown.Count
            ? shown[chosenIndex]
            : null;

        int kept = chosen == null ? -1 : sameLines ? chosenIndex : IndexToKeep(rebuilt, chosen);
        string? speech = chosen is { Kind: not RunwayEntryKind.FullLength } && kept < 0 ? NoLongerOffered(chosen) : null;
        int select = kept >= 0 ? kept : rebuilt.Count > 0 ? 0 : -1;
        return new RunwayEntryRefill(!sameLines, select, speech);
    }

    public static IReadOnlyList<RunwayEntryItem> Build(
        IReadOnlyList<TaxiGraph.RunwayIntersection> intersections,
        IReadOnlyList<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)> holdingPoints,
        double startLat, double startLon, double endLat, double endLon,
        Func<double, string> distance)
    {
        // Each list measures "along" in its own frame, so order by the projected position of every
        // entry on the one centreline.
        double Along(TaxiGraph.RunwayIntersection e) =>
            TaxiGraph.ProjectOntoCenterline(e.Latitude, e.Longitude, startLat, startLon, endLat, endLon).along;

        var candidates = new List<(double Along, int Rank, RunwayEntryItem Item)>();
        foreach (var ix in intersections)
        {
            string label = $"{ix.TaxiwayName}, {distance(ix.RemainingMeters)} remaining";
            candidates.Add((Along(ix), 0,
                new RunwayEntryItem(RunwayEntryKind.Intersection, label, ix, false, "")));
        }
        foreach (var (entry, kind, partway) in holdingPoints)
        {
            string label = $"{entry.TaxiwayName} (holding point{KindWords(kind)}), {distance(entry.RemainingMeters)} remaining";
            candidates.Add((Along(entry), 1,
                new RunwayEntryItem(RunwayEntryKind.HoldingPoint, label, entry, partway, kind)));
        }

        var items = new List<RunwayEntryItem>
        {
            new(RunwayEntryKind.FullLength, FullLength, null, false, ""),
        };
        var seen = new HashSet<string>(StringComparer.Ordinal) { FullLength };
        foreach (var c in candidates
                     .OrderBy(c => c.Along)
                     .ThenBy(c => c.Rank)
                     .ThenBy(c => c.Item.Label, StringComparer.Ordinal))
        {
            // Two items with the same text keep the first, as both old lists did.
            if (seen.Add(c.Item.Label)) items.Add(c.Item);
        }
        return items;
    }

    /// <summary>A holding point's OSM kind as one spelling: trimmed, lower case, "" when none. The label's kind
    /// words and the item's <see cref="RunwayEntryItem.HoldKind"/> both come from it, so the key matches the label.</summary>
    internal static string NormaliseKind(string? kind) => (kind ?? "").Trim().ToLowerInvariant();

    private static string KindWords(string? kind) =>
        NormaliseKind(kind) switch
        {
            "ils" => ", ILS hold",
            "intermediate" => ", intermediate hold",
            _ => "",
        };

    /// <summary>The index of the item that stands for <paramref name="kept"/> in a rebuilt list, or -1 when gone.
    /// Two meetings of one taxiway share kind, name and hold kind, so a match must also have its runway remaining
    /// within <see cref="MaxRefillMoveMetres"/> of the kept one's, and among those the nearest is kept: a lost meeting
    /// is never replaced by the other one, which would move the departure (Review Focus 5). A remaining that is not a
    /// number cannot show it is the same meeting, so it never matches.</summary>
    public static int IndexToKeep(IReadOnlyList<RunwayEntryItem> items, RunwayEntryItem? kept)
    {
        if (kept == null) return -1;
        int best = -1;
        double bestGap = double.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            if (it.Kind != kept.Kind
                || !string.Equals(it.Name, kept.Name, StringComparison.Ordinal)
                || !string.Equals(it.HoldKind, kept.HoldKind, StringComparison.Ordinal))
                continue;
            // Full length has no entry: 0 against 0.
            double gap = Math.Abs((it.Entry?.RemainingMeters ?? 0) - (kept.Entry?.RemainingMeters ?? 0));
            if (!(gap <= MaxRefillMoveMetres)) continue;   // NaN fails too
            if (gap < bestGap)
            {
                best = i;
                bestGap = gap;
            }
        }
        return best;
    }

    /// <summary>Spoken once when a refill loses the chosen entry and the list falls back to Full length.</summary>
    public static string NoLongerOffered(RunwayEntryItem gone) =>
        $"{gone.Name} is no longer offered. Full length departure.";
}
