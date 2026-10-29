using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the A350's warning display says on its own (pure): each line that appears, with its colours, once two reads in a
/// row agree on it [A350-2]. Lines agree each on its own (review 2026-10-29: when the whole list had to agree, one ticking
/// line held back every other, new warnings included). A line changing only in its digits is the same line, a countdown
/// ticking, except a warning or a caution (red, amber), whose digits say which engine or system it is. The first agreed
/// read is a silent baseline; an unpowered E/WD is an empty one, so a cold-and-dark power-up speaks the whole list as it
/// comes up. The cursor and the action boxes are not part of a line's identity: moving the cursor says nothing here.
/// </summary>
public sealed class A350EcamAnnouncer
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    private Dictionary<string, int>? _previousRead;   // the last read's lines, by identity, counted
    private Dictionary<string, int>? _agreed;          // the lines two reads agreed on; null until the baseline
    private readonly Dictionary<string, List<string>> _agreedTexts = new();   // the texts last agreed, by identity

    /// <summary>A line's identity: its spoken text, its digits masked unless it is a warning or a caution.</summary>
    private static string IdentityOf(EcamLine line) =>
        line.Cells.Any(c => c.Colour is "red" or "amber") ? line.Spoken : Digits.Replace(line.Spoken, "#");

    /// <summary>The lines to speak for this read, in reading order (usually none).</summary>
    public IReadOnlyList<string> Observe(A350EwdView view)
    {
        var lines = view.Warning.Select(l => (Id: IdentityOf(l), Text: l.Spoken)).ToList();
        var read = new Dictionary<string, int>();
        foreach (var (id, _) in lines) read[id] = read.GetValueOrDefault(id) + 1;
        var previous = _previousRead;
        _previousRead = read;
        if (previous == null) return Array.Empty<string>();

        bool baseline = _agreed == null;
        _agreed ??= new Dictionary<string, int>();
        var toSay = new Dictionary<string, List<string>>();   // by identity, the texts to say
        // Every identity either read holds, and every one agreed on before: one gone from both reads has agreed on gone.
        foreach (var id in read.Keys.Union(previous.Keys).Union(_agreed.Keys).ToList())
        {
            int now = read.GetValueOrDefault(id);
            if (now != previous.GetValueOrDefault(id)) continue;   // not agreed: what was agreed stands
            int was = _agreed.GetValueOrDefault(id);
            var texts = lines.Where(l => l.Id == id).Select(l => l.Text).ToList();
            if (!baseline && now > was)
            {
                // The texts new to this identity first (a second engine's line beside the first), then any.
                var known = _agreedTexts.GetValueOrDefault(id)?.ToList() ?? new List<string>();
                var fresh = texts.Where(t => !known.Remove(t)).ToList();
                toSay[id] = fresh.Concat(texts.Except(fresh)).Take(now - was).ToList();
            }
            if (now == 0)
            {
                _agreed.Remove(id);
                _agreedTexts.Remove(id);
            }
            else
            {
                _agreed[id] = now;
                _agreedTexts[id] = texts;
            }
        }

        var spoken = new List<string>();
        foreach (var (id, text) in lines)
            if (toSay.TryGetValue(id, out var say) && say.Remove(text))
                spoken.Add(text);
        return spoken;
    }

    /// <summary>Forgets the baseline (aircraft reloaded, reader restarted): the next agreed read is silent.</summary>
    public void Reset()
    {
        _previousRead = null;
        _agreed = null;
        _agreedTexts.Clear();
    }
}
