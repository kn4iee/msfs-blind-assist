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
/// A flag absent from a read between two that have it flashes, and is said once with ", flashing".
/// Each message <see cref="Messages"/> names has its own Ctrl+M row (plan 07, the owner's answer to question 6); a flag it does not
/// name stays on <see cref="MuteKey"/>'s row. Until <see cref="SplitOldMute"/> has run, that old row still mutes every message.
/// </summary>
public sealed class A350PfdFlagAnnouncer
{
    public const string MuteKey = "A350_PFD_FLAGS";
    public const string MuteLabel = "Other PFD flags and warnings";
    public const int Window = 4, Needed = 2, GoneAfter = 6;

    /// <summary>The PFD messages with a row of their own: those seen drawn or in the module's own PFD strings (docs/inibuilds-a350.md,
    /// "The PFD's flags"; 1.2.8's read from its memory 2026-11-03: WIND SHEAR in two words, no W/S AHEAD or STALL anywhere, the stall
    /// being its horn alone). A message whose words go on after one of these is that one's (the emergency descent's two lines, "AUTO
    /// EMER DES" over "IN 12 S", "ENGAGED" or "COMPLETED"; "EMER DES" over "ARMED").</summary>
    public static readonly IReadOnlyList<string> Messages = new[]
    {
        "PULL UP", "SINK RATE", "GLIDE SLOPE", "DON'T SINK", "ATT", "TCAS", "DH", "AUTO BRK OFF", "DECEL", "RWY TOO SHORT", "MAX REVERSE",
        "MAX BRAKING", "SPD BRK AUTO EXT", "T.O THS OUT OF RANGE", "WIND SHEAR", "AP OFF", "A/THR OFF", "AUTO EMER DES", "EMER DES",
    };

    /// <summary>In the disabled list once the old row's mute has been carried to every message's own.</summary>
    public const string SplitDone = "A350_PFD_MSG_SPLIT_DONE";

    public static string KeyFor(string message) => "A350_PFD_MSG_" + Regex.Replace(message.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');

    private static readonly Regex Figure = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

    /// <summary>A flag's words, its colours and figures left out ("DH 200, amber" is DH).</summary>
    private static string WordsOf(string flag) => Regex.Replace(Figure.Replace(string.Join(" ", flag.Split("; ")
        .Select(p => p.LastIndexOf(", ", StringComparison.Ordinal) is var c and > 0 ? p[..c] : p)), ""), @"\s+", " ").Trim().ToUpperInvariant();

    /// <summary>The Ctrl+M row a flag is muted by: its message's own (the longest whose words it is or starts with, word by word), else
    /// the old row.</summary>
    public static string MuteKeyFor(string flag)
    {
        string words = WordsOf(flag);
        return Messages.Where(m => words == m || words.StartsWith(m + " ", StringComparison.Ordinal)).OrderByDescending(m => m.Length)
            .FirstOrDefault() is { } message ? KeyFor(message) : MuteKey;
    }

    /// <summary>Whether a flag is muted: by its row, or, until the split has run, by the old row that once held them all.</summary>
    public static bool IsMuted(string flag, Func<string, bool> muted)
    {
        string key = MuteKeyFor(flag);
        return muted(key) || key != MuteKey && muted(MuteKey) && !muted(SplitDone);
    }

    /// <summary>Once: with the old row unticked, every message's own row starts unticked; a row ticked back later stays so.</summary>
    public static void SplitOldMute(ICollection<string> disabled)
    {
        if (disabled.Contains(SplitDone)) return;
        if (disabled.Contains(MuteKey))
            foreach (var key in Messages.Select(KeyFor).Where(k => !disabled.Contains(k)))
                disabled.Add(key);
        disabled.Add(SplitDone);
    }

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
                // In the window, away from a read between two that have it: it flashes.
                var seen = _reads.Select(r => r.Contains(key)).ToList();
                int first = seen.IndexOf(true);
                said.Add(seen.Skip(first).Contains(false) ? flag + A350FlashingLines.FlashingSuffix : flag);
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
