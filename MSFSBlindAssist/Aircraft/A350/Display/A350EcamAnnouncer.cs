using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What the A350's warning display says on its own (pure): each line that appears, with its colours, once two reads in a
/// row agree on it [A350-2]. Lines agree each on its own (review 2026-10-29: when the whole list had to agree, one ticking
/// line held back every other, new warnings included). A line changing only in its digits is the same line, a countdown
/// ticking, except a warning or a caution (red, amber), whose digits say which engine or system it is. The first agreed
/// read is a silent baseline; an unpowered E/WD is an empty one, so a cold-and-dark power-up speaks the whole list as it
/// comes up. The cursor and the action boxes are not part of a line's identity: moving the cursor says nothing here.
/// A memo back unchanged within <see cref="MemoReturn"/> of going is taken back silently (the owner's answer to question 3,
/// 2026-10-31: the landing memo was said in full again 5 s after an AP OFF warning had displaced it); a warning or a caution
/// (red or amber in the main column) is always said. A memo-column line whose only change is its colour is said once the new colour has held
/// <see cref="ColourHold"/>, and not at all when it reverts within it (question 4: COMPANY MSG and WXR/PWS OFF swapped colours for
/// about a second, every few minutes, on 2026-10-31).
/// </summary>
public sealed class A350EcamAnnouncer
{
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>How soon a memo that went may come back unchanged and not be said again.</summary>
    public static readonly TimeSpan MemoReturn = TimeSpan.FromSeconds(10);

    /// <summary>How long a memo's new colour holds before it is said.</summary>
    public static readonly TimeSpan ColourHold = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, string> _plainOf = new();                               // identity's text without colours
    private readonly Dictionary<string, (DateTime Since, List<string> Texts)> _colourPending = new();   // memos whose colour changed

    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, DateTime> _memosWent = new();   // memos that went, by identity, with when
    private readonly HashSet<string> _memoIds = new();                    // the identities read as memos

    public A350EcamAnnouncer(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>A memo: a line of the memo column whatever its colour, or one with no red or amber in it.</summary>
    private static bool IsMemo(EcamLine line) =>
        line.Cells.Count > 0 && line.Cells.All(A350Ewd.InMemoColumn) || !line.Cells.Any(c => c.Colour is "red" or "amber");

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
        var memos = view.Warning.Where(IsMemo).Select(IdentityOf).ToHashSet();
        var memoColumn = view.Warning.Where(l => l.Cells.Count > 0 && l.Cells.All(A350Ewd.InMemoColumn)).Select(IdentityOf).ToHashSet();
        foreach (var l in view.Warning) _plainOf[IdentityOf(l)] = l.Text;
        var now0 = _now();
        foreach (var gone in _memosWent.Where(kv => now0 - kv.Value > MemoReturn).Select(kv => kv.Key).ToList())
            _memosWent.Remove(gone);
        var read = new Dictionary<string, int>();
        foreach (var (id, _) in lines) read[id] = read.GetValueOrDefault(id) + 1;
        var previous = _previousRead;
        _previousRead = read;
        if (previous == null) return Array.Empty<string>();

        bool baseline = _agreed == null;
        _agreed ??= new Dictionary<string, int>();
        // The texts the lines agreed before this read had, without their colours: a new memo with one of them changed colour only.
        var plainBefore = _agreed.Keys.Select(k => _plainOf.GetValueOrDefault(k)).OfType<string>().ToHashSet();
        var toSay = new Dictionary<string, List<string>>();   // by identity, the texts to say
        // Every identity either read holds, and every one agreed on before: one gone from both reads has agreed on gone.
        foreach (var id in read.Keys.Union(previous.Keys).Union(_agreed.Keys).ToList())
        {
            int now = read.GetValueOrDefault(id);
            if (now != previous.GetValueOrDefault(id)) continue;   // not agreed: what was agreed stands
            int was = _agreed.GetValueOrDefault(id);
            var texts = lines.Where(l => l.Id == id).Select(l => l.Text).ToList();
            if (!baseline && was == 0 && now > 0 && memos.Contains(id) && _memosWent.Remove(id))
            {
                // A memo back unchanged within MemoReturn: taken back silently.
            }
            else if (!baseline && was == 0 && now > 0 && memoColumn.Contains(id) && _plainOf.TryGetValue(id, out var plain)
                && plainBefore.Contains(plain) && !_agreed.ContainsKey(id))
            {
                _colourPending[id] = (now0, texts);   // only its colour changed: said once it holds
            }
            else if (!baseline && now > was)
            {
                // The texts new to this identity first (a second engine's line beside the first), then any.
                var known = _agreedTexts.GetValueOrDefault(id)?.ToList() ?? new List<string>();
                var fresh = texts.Where(t => !known.Remove(t)).ToList();
                toSay[id] = fresh.Concat(texts.Except(fresh)).Take(now - was).ToList();
            }
            if (_colourPending.TryGetValue(id, out var pending))
            {
                if (now == 0)
                    _colourPending.Remove(id);    // reverted within the hold: nothing to say
                else if (now0 - pending.Since >= ColourHold)
                {
                    _colourPending.Remove(id);
                    toSay[id] = texts;
                }
            }
            if (now == 0)
            {
                if (was > 0 && _memoIds.Contains(id))
                    _memosWent[id] = now0;
                _agreed.Remove(id);
                _agreedTexts.Remove(id);
            }
            else
            {
                _agreed[id] = now;
                _agreedTexts[id] = texts;
            }
        }

        _memoIds.UnionWith(memos);
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
        _memosWent.Clear();
        _memoIds.Clear();
        _colourPending.Clear();
        _plainOf.Clear();
    }
}
