using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The PFD's flashing lines as a status display says them. The A350 flashes a line by leaving it out of every other draw (QNH
/// above the transition altitude, STD below the transition level, live 2026-10-30), so one read has it and the next has not. A
/// line that comes back within <see cref="Window"/> of leaving flashes: it is said so, and held in its place while it is out. One
/// that simply goes is gone at once. Only what can flash is followed: the FMA's columns, the baro and the flags of the rest; the
/// tapes' figures come and go by themselves. Reads come from the UI thread and from a hotkey, hence the lock.
/// </summary>
public sealed class A350FlashingLines
{
    /// <summary>How long a line may be out and still be flashing, and how long one reads as flashing after it last came back:
    /// the reads are a second apart and beat with the flash (live, at 4x: runs of several reads alike).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(4);

    private const string Flashing = ", flashing";
    private static readonly Regex Figure = new(@"\d+(\.\d+)?", RegexOptions.Compiled);

    private sealed class Seen
    {
        public string Line = "";
        public int Place;
        public DateTime? LeftAt;
        public DateTime FlashingUntil;
    }

    private readonly Dictionary<string, Seen> _seen = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private static bool CanFlash(string line, int place, int restFrom) => place >= restFrom
        || line.StartsWith("FMA ", StringComparison.Ordinal) || line.StartsWith("QNH: ", StringComparison.Ordinal)
        || line.StartsWith("QFE: ", StringComparison.Ordinal) || line.StartsWith("Baro: ", StringComparison.Ordinal);

    /// <summary>A line's name: its label, or a flag with its figures left out (a flag is the same flag while they move).</summary>
    private static string KeyOf(string line) => line.IndexOf(": ", StringComparison.Ordinal) is var colon and > 0 ? line[..colon] : Figure.Replace(line, "#");

    /// <summary>One read of the PFD at <paramref name="now"/>, as its status display says it.</summary>
    public IReadOnlyList<string> Apply(A350Pfd.PfdRead read, DateTime now)
    {
        lock (_gate)
        {
            var here = new HashSet<string>(StringComparer.Ordinal);
            var said = new List<string>(read.Lines.Count);
            for (int n = 0; n < read.Lines.Count; n++)
            {
                string line = read.Lines[n];
                if (!CanFlash(line, n, read.RestFrom))
                {
                    said.Add(line);
                    continue;
                }
                string key = KeyOf(line);
                here.Add(key);
                if (!_seen.TryGetValue(key, out var seen)) _seen[key] = seen = new Seen();
                if (seen.LeftAt is { } left && now - left <= Window) seen.FlashingUntil = now + Window;
                seen.LeftAt = null;
                seen.Line = line;
                seen.Place = said.Count;
                said.Add(now < seen.FlashingUntil ? line + Flashing : line);
            }
            foreach (var (key, seen) in _seen.Where(s => !here.Contains(s.Key)).OrderBy(s => s.Value.Place).ToList())
            {
                seen.LeftAt ??= now;
                if (now < seen.FlashingUntil) said.Insert(Math.Min(seen.Place, said.Count), seen.Line + Flashing);
                else if (now - seen.LeftAt > Window) _seen.Remove(key);
            }
            return said;
        }
    }
}
