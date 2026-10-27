namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The CDU window's typed entries (the scratchpad box, Alt+S then Enter): the keys, the faster key
/// timing they go out at, and the check that the scratchpad holds what was typed, read back once the keys
/// have gone out (plan 08).
///
/// Every other key keeps the tablet client's standard 250 ms hold and 400 ms gap (0.65 s a key):
/// page keys, line select keys, CLR and DEL, and every key of the CDU page service. Typed entries go
/// out at 100 ms / 300 ms (0.4 s a key; "KATL" in 1.6 s instead of 2.6 s). Measured on the first
/// officer's CDU typing <c>KATL1100.5</c> (docs/b757/evidence/flight-08.md), both days on APU power:
/// on 2026-10-26 every pair down to 100 / 50 landed 5 of 5; on 2026-10-27, through the tablet agent's
/// own press as the app types and with the sim busier (the EFB reader's ND pop-out open), 100 / 100
/// landed only 6 of 10, losing a repeated digit ("KATL100.5"), while 100 / 200 landed 15 of 15 and
/// 100 / 300 10 of 10. 100 / 300 keeps a step of margin over the fastest pair that never missed. Never
/// shorten the gap on one quiet measurement: a repeated character needs the key released long enough
/// to count twice, and a busier sim needs longer. Pure.
/// </summary>
public static class Bbs757CduTyping
{
    /// <summary>How long a typed key is held (the client's floor).</summary>
    public const int HoldMs = 100;

    /// <summary>The pause after a typed key's release before the next.</summary>
    public const int GapMs = 300;

    /// <summary>The CDU keys that type <paramref name="text"/> (<see cref="Bbs757CduKeys.ForChar"/> of each
    /// character; a character with no key is skipped, as the window always has).</summary>
    public static IReadOnlyList<string> Keys(string text) =>
        (text ?? "").SelectMany(Bbs757CduKeys.ForChar).ToList();

    /// <summary>What those keys put in the scratchpad: each character that has a key, upper case.</summary>
    public static string Expected(string text) =>
        new((text ?? "").Where(c => Bbs757CduKeys.ForChar(c).Count > 0).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// Null when the scratchpad (trimmed) holds the typed text: on its own, or after what the scratchpad
    /// already held before the typing (<paramref name="scratchpadBefore"/>; the CDU adds to an entry).
    /// Otherwise what to say: "Scratchpad shows KAT", "Scratchpad is empty". A CDU message over the
    /// entry is said as the scratchpad shows it ("Scratchpad shows INVALID ENTRY").
    /// </summary>
    public static string? Mismatch(string typed, Bbs757CduScreen screen, string scratchpadBefore = "")
    {
        string want = Expected(typed).Trim();
        string shows = screen.Scratchpad;
        if (want.Length == 0) return null;
        if (shows == want) return null;
        string before = (scratchpadBefore ?? "").Trim();
        if (before.Length > 0 && shows == (before + Expected(typed)).Trim()) return null;
        return shows.Length == 0 ? "Scratchpad is empty" : "Scratchpad shows " + shows;
    }

    /// <summary>
    /// What the window says once a typed entry's keys have gone out: the scratchpad as it shows it when it
    /// holds the entry ("KATL"; the read-back every CDU window gives, and the guide's promise that the pilot
    /// hears what the CDU took), else <see cref="Mismatch"/>'s "Scratchpad shows KAT". Null when there is
    /// nothing to say (nothing typable typed on an empty scratchpad).
    /// </summary>
    public static string? ReadBack(string typed, Bbs757CduScreen screen, string scratchpadBefore = "") =>
        Mismatch(typed, screen, scratchpadBefore) ?? (screen.Scratchpad.Length > 0 ? screen.Scratchpad : null);
}
