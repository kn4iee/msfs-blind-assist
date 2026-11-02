using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350.Display;

public enum A350FactsPart { Table, Variables }

public enum A350FactsVerdict { Undecided, Agrees, Disagrees }

/// <summary>One name read both ways in one copy: from the copy (its variable-table slot, or the engine's answer to a draw's
/// ask) and from MSFSBA's cached L:var.</summary>
public sealed record A350FactsPair(A350FactsPart Part, string Name, double FromCopy, double FromSim);

/// <summary>
/// Whether what the reader takes from the copy's memory is what the A350 publishes as L:vars (pure; the service feeds it).
/// After an update a layout that looks right but reads shifted values would be spoken with confidence: the probe's sweeps once
/// named every value 24 entries off while the table's own check passed. It runs at every start, so a new version is checked
/// the first time it flies, and nothing of it is kept between sessions. Only ever read: nothing goes to the sim or the copy
/// ([A350-1]).
/// </summary>
public sealed class A350FactsCheck
{
    public const double Tolerance = 0.5;
    public const int CopiesToDecide = 3;
    public static readonly TimeSpan Apart = TimeSpan.FromSeconds(2);

    private sealed class Part
    {
        public A350FactsVerdict Verdict;
        public int Run;
        public bool RunDisagrees;
        public DateTime? LastCounted;
        public IReadOnlyList<A350FactsPair> Pairs = Array.Empty<A350FactsPair>();
    }

    private readonly Dictionary<A350FactsPart, Part> _parts = new() { [A350FactsPart.Table] = new(), [A350FactsPart.Variables] = new() };
    private readonly Dictionary<(A350FactsPart, string), double> _lastSim = new();

    /// <summary>One copy's pairs; returns the parts this copy decided (each part decides once).</summary>
    public IReadOnlyList<A350FactsPart> Add(IReadOnlyList<A350FactsPair> pairs, DateTime utc)
    {
        var decided = new List<A350FactsPart>();
        foreach (var (name, part) in _parts)
        {
            if (part.Verdict != A350FactsVerdict.Undecided) continue;
            var counted = new List<A350FactsPair>();
            foreach (var pair in pairs.Where(p => p.Part == name))
            {
                // A value moving between the copy and the sim read (VLS as the weight changes, a knob being turned) never decides.
                bool steady = _lastSim.TryGetValue((name, pair.Name), out var last) && last == pair.FromSim;
                _lastSim[(name, pair.Name)] = pair.FromSim;
                if (steady && (pair.FromCopy != 0 || pair.FromSim != 0)) counted.Add(pair);
            }
            if (counted.Count == 0 || part.LastCounted is { } at && utc - at < Apart) continue;
            part.LastCounted = utc;
            bool disagrees = counted.Any(Differs);
            if (part.Run > 0 && part.RunDisagrees != disagrees) part.Run = 0;
            part.RunDisagrees = disagrees;
            part.Run++;
            part.Pairs = counted;
            if (part.Run < CopiesToDecide) continue;
            part.Verdict = disagrees ? A350FactsVerdict.Disagrees : A350FactsVerdict.Agrees;
            decided.Add(name);
        }
        return decided;
    }

    private static bool Differs(A350FactsPair pair) => Math.Abs(pair.FromCopy - pair.FromSim) > Tolerance;

    public A350FactsVerdict Verdict(A350FactsPart part) => _parts[part].Verdict;

    /// <summary>Whether no part is left to decide.</summary>
    public bool Done => _parts.Values.All(p => p.Verdict != A350FactsVerdict.Undecided);

    /// <summary>"facts check, table: agrees on 5 pairs (INI_VLS_SPEED 166 in the copy, 166 in the sim; …)", invariant numbers; a
    /// disagreement lists each disagreeing pair's two values.</summary>
    public string LogLine(A350FactsPart part)
    {
        var p = _parts[part];
        string name = Word(part);
        if (p.Verdict == A350FactsVerdict.Undecided) return $"facts check, {name}: undecided";
        var shown = p.Verdict == A350FactsVerdict.Disagrees ? p.Pairs.Where(Differs).ToList() : p.Pairs;
        string how = p.Verdict == A350FactsVerdict.Disagrees
            ? $"disagrees on {shown.Count} of {p.Pairs.Count} pairs"
            : $"agrees on {p.Pairs.Count} pair{(p.Pairs.Count == 1 ? "" : "s")}";
        return $"facts check, {name}: {how} ({string.Join("; ", shown.Select(Pair))})";
    }

    private static string Pair(A350FactsPair p) =>
        string.Create(CultureInfo.InvariantCulture, $"{p.Name} {p.FromCopy:0.###} in the copy, {p.FromSim:0.###} in the sim");

    /// <summary>The end of the reader's minute line: "facts: table agrees, variables undecided".</summary>
    public string MinuteWords =>
        $"facts: {Word(A350FactsPart.Table)} {Word(_parts[A350FactsPart.Table].Verdict)}, {Word(A350FactsPart.Variables)} {Word(_parts[A350FactsPart.Variables].Verdict)}";

    private static string Word(A350FactsPart part) => part == A350FactsPart.Table ? "table" : "variables";

    private static string Word(A350FactsVerdict verdict) => verdict switch
    {
        A350FactsVerdict.Agrees => "agrees",
        A350FactsVerdict.Disagrees => "disagrees",
        _ => "undecided",
    };
}
