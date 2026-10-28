using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>The loaded 757's engine type, as its title names it; <see cref="Unknown"/> until a title names one.</summary>
public enum Bbs757EngineType
{
    /// <summary>No title yet, or one that names neither type (or both): every control is shown.</summary>
    Unknown,

    /// <summary>Pratt &amp; Whitney PW2000: the title says "PW".</summary>
    Pw,

    /// <summary>Rolls-Royce RB211: the title says "RR".</summary>
    Rr,
}

/// <summary>
/// Which engine type the loaded BlueBird 757 has, and which of the map's controls its panels show (plan 12 task 8; the
/// owner found the Engines panel "cluttered" with both types' fuel control switches).
///
/// BlueBird ships a PW and an RR cockpit, and a few controls are in one of them only: the map's
/// <see cref="Bbs757Control.Variant"/> (the PW and the RR fuel control switches, the RR electronic engine control switches
/// and their guards). The aircraft TITLE names the type: every title the package ships carries the word "PW" or "RR"
/// ("BlueBird 757-200 RR", read 2026-10-28; the full list is pinned by Bbs757VariantTests). The word is matched whole and
/// in any case. A title naming neither or both, or no title yet, is <see cref="Bbs757EngineType.Unknown"/>, and an unknown
/// type shows every control, as the panels did before the map said which type has a control.
///
/// Pure: <c>BBS757Definition</c> reads the title and rewrites its panel lists with <see cref="Rewrite"/>.
/// </summary>
public static class Bbs757Variant
{
    private static readonly Regex PwWord = new(@"\bPW\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RrWord = new(@"\bRR\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The engine type a title names: the whole word "PW" or "RR" in any case; neither, both or none is unknown.</summary>
    public static Bbs757EngineType FromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return Bbs757EngineType.Unknown;
        bool pw = PwWord.IsMatch(title), rr = RrWord.IsMatch(title);
        if (pw == rr) return Bbs757EngineType.Unknown;
        return pw ? Bbs757EngineType.Pw : Bbs757EngineType.Rr;
    }

    /// <summary>
    /// False only for a control of the OTHER engine type's cockpit: a control both cockpits have (no variant), one of this
    /// type's, and every control while the type is unknown are shown.
    /// </summary>
    public static bool IsFitted(string? variant, Bbs757EngineType engines) => engines switch
    {
        Bbs757EngineType.Pw => !string.Equals(variant, Bbs757Variants.Rr, StringComparison.OrdinalIgnoreCase),
        Bbs757EngineType.Rr => !string.Equals(variant, Bbs757Variants.Pw, StringComparison.OrdinalIgnoreCase),
        _ => true,
    };

    /// <summary>The nodes of the map controls the loaded engine type lacks; none while it is unknown.</summary>
    public static HashSet<string> HiddenNodes(IEnumerable<Bbs757Control> controls, Bbs757EngineType engines) =>
        controls.Where(c => !IsFitted(c.Variant, engines)).Select(c => c.Node).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Rewrites the panel lists a definition hands out IN PLACE, from the full lists it built: each panel's controls less the
    /// hidden ones, and each section's panels less a panel that only hidden controls were on and that has no status display
    /// (a panel that had no control to begin with stays). The same dictionaries and lists are kept, because the base class
    /// caches the controls dictionary and MainForm re-reads both each time it lists a section or builds a panel. Hiding
    /// nothing brings back every list as it was built.
    /// </summary>
    public static void Rewrite(
        IReadOnlyDictionary<string, List<string>> allStructure, IReadOnlyDictionary<string, List<string>> allControls,
        IReadOnlyDictionary<string, List<string>> displays,
        Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls, IReadOnlySet<string> hidden)
    {
        foreach (var (panel, all) in allControls)
        {
            if (!controls.TryGetValue(panel, out var shown)) continue;
            shown.Clear();
            shown.AddRange(all.Where(key => !hidden.Contains(key)));
        }

        foreach (var (section, all) in allStructure)
        {
            if (!structure.TryGetValue(section, out var shown)) continue;
            shown.Clear();
            shown.AddRange(all.Where(panel => !EmptiedByHiding(panel)));
        }

        bool EmptiedByHiding(string panel) =>
            allControls.TryGetValue(panel, out var all) && all.Count > 0 && all.All(hidden.Contains)
            && !(displays.TryGetValue(panel, out var rows) && rows.Count > 0);
    }
}
