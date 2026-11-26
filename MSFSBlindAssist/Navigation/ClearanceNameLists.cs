// The name box's contents for each kind of clearance step, likely names first. Pure: it takes plain
// lists (the window supplies them from the graph), so it is tested without one.

namespace MSFSBlindAssist.Navigation;

public static class ClearanceNameLists
{
    /// <summary>One entry of a name box. A combo holds the item and shows it by <see cref="ToString"/>;
    /// what a pick IS comes from <see cref="Kind"/>, never from its text.</summary>
    public sealed record Item(string Label, ClearanceTargetKind Kind, string Name, NamedHoldingPoint? HoldingPoint = null)
    {
        public override string ToString() => Label;
    }

    /// <summary>Taxiways for a Via, Left onto or Right onto step. [RTE-10]: only the taxiway of an
    /// immediately preceding Via-kind step is left out.</summary>
    public static List<Item> Taxiways(IReadOnlyList<string> nearestFirst, IReadOnlyList<string> connectedToLastVia,
        IReadOnlyList<string> allTaxiways, IReadOnlyList<ClearanceStep> steps)
    {
        var names = new List<string>();
        if (LastVia(steps) == null)
        {
            names.AddRange(nearestFirst);
        }
        else
        {
            names.AddRange(connectedToLastVia);
            AddRestAlphabetically(names, allTaxiways);
        }
        return LeaveOutPrevious(names, steps)
            .Select(n => new Item(n, ClearanceTargetKind.Taxiway, n)).ToList();
    }

    /// <summary>Runways (those the last Via taxiway crosses first), then taxiways as in <see cref="Taxiways"/>.</summary>
    public static List<Item> CrossTargets(IReadOnlyList<string> runwayIds, Func<string, IReadOnlyList<string>> taxiwaysCrossingRunway,
        IReadOnlyList<string> connectedToLastVia, IReadOnlyList<string> allTaxiways, IReadOnlyList<ClearanceStep> steps)
    {
        var items = RunwayItems(runwayIds, taxiwaysCrossingRunway, steps);
        // No nearest list here: before any Via step the taxiways are simply alphabetical.
        var alphabetical = allTaxiways.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        items.AddRange(Taxiways(alphabetical, connectedToLastVia, allTaxiways, steps));
        return items;
    }

    /// <summary>Runways as for a crossing, then the taxiways and holding points merged ([HLD-23]). Every point is
    /// offered, one read from a sign included, for every destination type and anywhere in the list ([HLD-18] since
    /// 2026-11-24; OMDB "taxi via K, hold short of KK"), its label saying it is approximate.</summary>
    public static List<Item> HoldTargets(IReadOnlyList<string> runwayIds, Func<string, IReadOnlyList<string>> taxiwaysCrossingRunway,
        IReadOnlyList<string> allTaxiways, IReadOnlyList<NamedHoldingPoint> holdingPoints, IReadOnlyList<ClearanceStep> steps)
    {
        var items = RunwayItems(runwayIds, taxiwaysCrossingRunway, steps);
        foreach (var entry in HoldShortTargetList.Merge(allTaxiways, holdingPoints))
        {
            items.Add(entry.HoldingPoint == null
                ? new Item(entry.Label, ClearanceTargetKind.Taxiway, entry.Name)
                : new Item(entry.Label, ClearanceTargetKind.HoldingPoint, entry.Name, entry.HoldingPoint));
        }
        return items;
    }

    private static ClearanceStep? LastVia(IReadOnlyList<ClearanceStep> steps)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
            if (steps[i].IsVia) return steps[i];
        return null;
    }

    private static void AddRestAlphabetically(List<string> names, IReadOnlyList<string> allTaxiways)
    {
        var have = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        foreach (var name in allTaxiways.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            if (have.Add(name)) names.Add(name);
    }

    private static IEnumerable<string> LeaveOutPrevious(List<string> names, IReadOnlyList<ClearanceStep> steps)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previous = steps.Count > 0 && steps[^1].IsVia ? steps[^1].Name : null;
        foreach (var name in names)
        {
            if (previous != null && string.Equals(name, previous, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(name)) yield return name;
        }
    }

    private static List<Item> RunwayItems(IReadOnlyList<string> runwayIds,
        Func<string, IReadOnlyList<string>> taxiwaysCrossingRunway, IReadOnlyList<ClearanceStep> steps)
    {
        var last = LastVia(steps);
        var likely = new List<string>();
        var rest = new List<string>();
        foreach (var runway in runwayIds)
        {
            bool crossed = last != null && taxiwaysCrossingRunway(runway)
                .Any(t => string.Equals(t, last.Name, StringComparison.OrdinalIgnoreCase));
            (crossed ? likely : rest).Add(runway);
        }
        return likely.Concat(rest).Select(r => new Item(r, ClearanceTargetKind.Runway, r)).ToList();
    }
}
