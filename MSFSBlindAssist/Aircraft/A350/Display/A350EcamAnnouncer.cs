namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the A350's warning display says on its own (pure): each line that appears, with its colours, once
/// two reads in a row agree [A350-2]. The first agreed read is a silent baseline; an unpowered E/WD is an
/// empty one, so a cold-and-dark power-up speaks the whole list as it comes up. The cursor and the action
/// boxes are not part of a line's identity: moving the cursor says nothing here.
/// </summary>
public sealed class A350EcamAnnouncer
{
    private readonly A350StableValue<string> _stable = new();
    private List<string>? _last;

    /// <summary>The lines to speak for this read, in reading order (usually none).</summary>
    public IReadOnlyList<string> Observe(A350EwdView view)
    {
        var lines = view.Warning.Select(l => l.Spoken).ToList();
        if (!_stable.Offer(string.Join("\n", lines), out _)) return Array.Empty<string>();
        var previous = _last;
        _last = lines;
        if (previous == null) return Array.Empty<string>();

        var remaining = new Dictionary<string, int>();
        foreach (var p in previous) remaining[p] = remaining.GetValueOrDefault(p) + 1;
        var spoken = new List<string>();
        foreach (var l in lines)
        {
            if (remaining.GetValueOrDefault(l) > 0) remaining[l]--;
            else spoken.Add(l);
        }
        return spoken;
    }

    /// <summary>Forgets the baseline (aircraft reloaded, reader restarted): the next agreed read is silent.</summary>
    public void Reset()
    {
        _stable.Reset();
        _last = null;
    }
}
