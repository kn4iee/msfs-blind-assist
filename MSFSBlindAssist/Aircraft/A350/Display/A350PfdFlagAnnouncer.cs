using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the PFD flags on its own, said as it appears (pure): from each PFD read, the lines that fit no area
/// (<see cref="A350Pfd.PfdRead.Rest"/>): PULL UP, ATT, TCAS, DH, AUTO BRK OFF, DECEL, RWY TOO SHORT, MAX REVERSE and whatever
/// else the aircraft draws there, with no list of them to go stale. A sighted pilot's eye goes to these: anything with a red
/// or amber part, or a plain message of words. A figure in another colour (the T.O THS FOR row, the magenta ILS) is read in the
/// PFD's status display and never said.
/// A flag is said once it is in <see cref="Needed"/> of the last <see cref="Window"/> reads, so a flashing one is caught (two
/// reads in a row would never agree on it: MASTER WARN flashes about as fast as the reads) and one torn read is not. It is new
/// again only after <see cref="GoneAfter"/> reads without it, so its flashing never repeats it. A flag is the same flag while
/// only its figures move. Nothing is said when one goes: the status display shows it, as the sighted pilot's PFD does.
/// </summary>
public sealed class A350PfdFlagAnnouncer
{
    public const string MuteKey = "A350_PFD_FLAGS";
    public const string MuteLabel = "PFD flags and warnings";
    public const int Window = 4, Needed = 2, GoneAfter = 6;

    private static readonly Regex Figure = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

    private readonly Queue<HashSet<string>> _reads = new();
    private readonly Dictionary<string, int> _said = new();   // each flag said, and the read it was last in
    private int _read;

    /// <summary>Whether a line of the rest is a flag: a part in red or amber, or nothing but words in white or green.</summary>
    public static bool IsFlag(string line)
    {
        var parts = line.Split("; ");
        return parts.Any(p => p.EndsWith(", red", StringComparison.Ordinal) || p.EndsWith(", amber", StringComparison.Ordinal))
            || parts.All(p => p.LastIndexOf(", ", StringComparison.Ordinal) is var comma and > 0
                && p[(comma + 2)..] is "white" or "green" && p[..comma].Any(char.IsLetter) && !p[..comma].Any(char.IsDigit));
    }

    /// <summary>One read's rest; the flags to say now.</summary>
    public IReadOnlyList<string> Observe(IEnumerable<string> rest)
    {
        _read++;
        var flags = rest.Where(IsFlag).ToList();
        _reads.Enqueue(flags.Select(f => Figure.Replace(f, "#")).ToHashSet(StringComparer.Ordinal));
        if (_reads.Count > Window) _reads.Dequeue();

        var said = new List<string>();
        foreach (var flag in flags)
        {
            string key = Figure.Replace(flag, "#");
            if (_said.ContainsKey(key))
                _said[key] = _read;
            else if (_reads.Count(r => r.Contains(key)) >= Needed)
            {
                _said[key] = _read;
                said.Add(flag);
            }
        }
        foreach (var gone in _said.Where(s => _read - s.Value >= GoneAfter).Select(s => s.Key).ToList())
            _said.Remove(gone);
        return said;
    }

    public void Reset()
    {
        _reads.Clear();
        _said.Clear();
        _read = 0;
    }
}
