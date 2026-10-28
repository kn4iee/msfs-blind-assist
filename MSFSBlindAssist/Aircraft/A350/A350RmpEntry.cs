using System.Text;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The keypad entry the RMP window is typing (live 2026-10-28, each line's entry read from the A350's memory). The RMP
/// has a selected line: digits and CLR edit that line's entry (six characters at most; one CLR deletes one), its line
/// select key loads it as standby, and another line's key only selects that line, emptying its entry, and loads nothing:
/// the manual's select the radio, type, press to load. Its screen shows neither the selection nor the entry. So a new
/// entry first empties the selected line's entry (<see cref="ClearPresses"/> CLR presses; on an empty one CLR does
/// nothing) and presses its own line's key: on the selected line that loads the now empty entry, which does nothing; on
/// another it selects it. The window speaks the entry, as the screen cannot. Pure: the window presses the keys.
/// </summary>
public sealed class A350RmpEntry
{
    /// <summary>CLR presses that empty any line's entry: the RMP keeps six characters (12 digits typed kept 123456).</summary>
    public const int ClearPresses = 8;

    private readonly StringBuilder _typed = new();

    /// <summary>What this entry has typed, "" when nothing.</summary>
    public string Typed => _typed.ToString();

    /// <summary>The line the entry is typed on (the RMP's selected line), 0 before the first character.</summary>
    public int Line { get; private set; }

    /// <summary>The entry as spoken after a deletion ("Entry 12", "Entry empty").</summary>
    public string Spoken => _typed.Length == 0 ? "Entry empty" : "Entry " + Typed;

    /// <summary>The keys that type <paramref name="c"/> for <paramref name="line"/>: a new entry selects its line first;
    /// none when the character has no key.</summary>
    public IReadOnlyList<string> Type(char c, int line)
    {
        if (A350Rmp.KeyFor(c) is not { } key)
            return Array.Empty<string>();
        var keys = new List<string>();
        if (_typed.Length == 0)
        {
            keys.AddRange(Enumerable.Repeat("CLR", ClearPresses));
            keys.Add(A350Rmp.LineSelectKey(line));
            Line = line;
        }
        keys.Add(key);
        _typed.Append(c);
        return keys;
    }

    /// <summary>The keys that move what is typed to <paramref name="line"/>: deleted on the line it is on, that line
    /// selected (another line's key: it empties that entry), typed again. None with nothing typed or on its own line.</summary>
    public IReadOnlyList<string> MoveTo(int line)
    {
        if (_typed.Length == 0 || line == Line)
            return Array.Empty<string>();
        var keys = Enumerable.Repeat("CLR", _typed.Length).Append(A350Rmp.LineSelectKey(line))
            .Concat(Typed.Select(c => A350Rmp.KeyFor(c)!)).ToList();
        Line = line;
        return keys;
    }

    /// <summary>The key that loads the entry on <paramref name="line"/> as its standby (its line select key); the entry
    /// is over: taken, or refused and left to the next entry's clearing.</summary>
    public IReadOnlyList<string> Load(int line)
    {
        _typed.Clear();
        return new[] { A350Rmp.LineSelectKey(line) };
    }

    /// <summary>The key that deletes the last character typed (CLR), or null when nothing is typed.</summary>
    public string? DeleteLast()
    {
        if (_typed.Length == 0)
            return null;
        _typed.Length--;
        return "CLR";
    }

    /// <summary>The CLR presses that delete everything typed; the entry is then empty.</summary>
    public IReadOnlyList<string> Abandon()
    {
        var keys = Enumerable.Repeat("CLR", _typed.Length).ToArray();
        _typed.Clear();
        return keys;
    }

    /// <summary>What a line select key's read-back says: the line as it is, after, when <paramref name="typed"/> left it
    /// as it was (the RMP refused it, or it was the standby already), that the standby is unchanged.</summary>
    public static string LoadReadBack(string typed, string? before, string after) =>
        typed.Length > 0 && before == after ? $"{typed}: standby unchanged. {after}" : after;
}
