using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;

namespace MSFSBlindAssist.Aircraft.A330.Sweep;

/// <summary>
/// Keys that share one state variable (the ECAM page keys on <c>INI_ECAM_ACTIVE_PAGE</c>, the radio panel's radio keys
/// on <c>INI_SELECTED_RADIO_CPT</c>): each press selects that key's value, and a press of the key already selected can
/// undo it (an ECAM page key's second press can take the display back to another page), so the selected key is never
/// pressed. The sweep takes them as ONE unit: the journal keeps the shared start once, each key is pressed in turn,
/// and the unit is put back by pressing the ONE key that selects the start.
/// <see cref="Key"/> is the state variable; <see cref="Members"/> the keys in the map's order, denied keys left out;
/// <see cref="KnownValues"/> what the map says each key selects (from the status row that names the state's values,
/// by the key's label ending in the value's name); <see cref="Noun"/> what one value is ("page", "radio").
/// </summary>
public sealed record KeyFamily(string Key, string State, string Label, string Noun, IReadOnlyList<string> Members,
    IReadOnlyDictionary<string, double> KnownValues, IReadOnlyDictionary<double, string> ValueNames)
{
    /// <summary>The key the map says selects <paramref name="value"/>, or null.</summary>
    public string? KnownKeyFor(double value) =>
        Members.FirstOrDefault(m => KnownValues.TryGetValue(m, out double v) && SweepTiming.Same(v, value));

    /// <summary>The name of a value ("VHF 2"), or its number.</summary>
    public string NameOf(double? value) =>
        value is double v && ValueNames.FirstOrDefault(kv => SweepTiming.Same(kv.Key, v)) is { Value: { } name } ? name : SweepTsv.Num(value);

    /// <summary>
    /// The ONE key whose press puts the state back to <paramref name="start"/>: a key this run saw select it (one the map
    /// agrees with first, else the last seen); else the key the map names, unless this run saw that key select something
    /// else. Null when no key is known to select it: then nothing is pressed.
    /// </summary>
    public string? RestoreKey(double start, IReadOnlyDictionary<string, double>? seen = null)
    {
        var sawIt = Members.Where(m => seen != null && seen.TryGetValue(m, out double v) && SweepTiming.Same(v, start)).ToList();
        if (sawIt.Count > 0)
            return sawIt.FirstOrDefault(m => KnownValues.TryGetValue(m, out double k) && SweepTiming.Same(k, start)) ?? sawIt[^1];
        string? known = KnownKeyFor(start);
        if (known != null && seen != null && seen.TryGetValue(known, out double other) && !SweepTiming.Same(other, start)) return null;
        return known;
    }
}

internal static class KeyFamilies
{
    /// <summary>Every state variable two or more keys (buttons with a <c>state</c>) share, in the map's order.</summary>
    public static IReadOnlyList<KeyFamily> Find(IReadOnlyList<Control> controls, IReadOnlyDictionary<string, string>? denied = null)
    {
        var families = new List<KeyFamily>();
        var keys = controls.Where(c => c.Kind == "button" && !string.IsNullOrWhiteSpace(c.State)).ToList();
        foreach (var group in keys.GroupBy(c => A330ControlMap.ParseReadKind(c.State!).name, StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() < 2) continue;
            string state = group.Key;
            var status = controls.FirstOrDefault(c => c.Kind == "status" && c.Positions is { Count: > 0 } && !string.IsNullOrWhiteSpace(c.Read)
                && string.Equals(A330ControlMap.ParseReadKind(c.Read!).name, state, StringComparison.OrdinalIgnoreCase));
            var names = status?.Positions!.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.First().Label) ?? new Dictionary<double, string>();
            var members = group.Where(c => denied == null || !denied.ContainsKey(c.Key)).ToList();
            var known = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var m in members)
            {
                // the value whose name the key's label ends with ("Radio panel VHF 2" selects "VHF 2"); the longest name wins
                var match = names.Where(n => n.Value.Length > 0 && m.Label.EndsWith(" " + n.Value, StringComparison.Ordinal))
                    .OrderByDescending(n => n.Value.Length).FirstOrDefault();
                if (match.Value != null) known[m.Key] = match.Key;
            }
            string noun = status?.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.ToLowerInvariant() ?? "value";
            string label = status != null ? $"{status.Label} keys" : $"keys sharing {state}";
            families.Add(new KeyFamily(state, state, label, noun, members.Select(c => c.Key).ToList(), known, names));
        }
        return families;
    }
}
