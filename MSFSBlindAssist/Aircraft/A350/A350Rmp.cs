namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// Pressing an RMP's keys (manual p. 74-75, live 2026-10-28): a page key, the frequency typed on the keypad (the
/// leading 1 and the point optional: 2 1 9 is 121.900), the line select key loads it as standby, the activation key
/// swaps it in. Each key is one Set of 1 on its input event (AIRLINER_RMP_&lt;key&gt;_&lt;rmp&gt;); RMP 1 is the captain's,
/// 2 the first officer's, 3 aft. Pure: the window sends the strings.
/// </summary>
public static class A350Rmp
{
    /// <summary>The page keys, in the panel's order.</summary>
    public static readonly IReadOnlyList<string> Pages = new[] { "VHF", "HF", "TEL", "SQWK", "MENU", "NAV" };

    /// <summary>The keypad key a typed character presses, or null.</summary>
    public static string? KeyFor(char c) => c switch
    {
        >= '0' and <= '9' => c.ToString(),
        '.' => "PERIOD",
        _ => null,
    };

    public static string LineSelectKey(int line) => "LSK" + line;

    public static string ActivationKey(int line) => "ADK" + line;

    /// <summary>The RMP's STBY RAD NAV (manual NAV page, iniBuilds' FAQ): it opens the LS and VOR pages. Nothing in the cockpit writes the
    /// variable behind it in 1.2.7, and each RMP reads its own: INI_CPT_, INI_FO_ or INI_OVHD_RADIO_MODE (RMP::Startup, live 2026-10-30).</summary>
    public const string StbyRadNavKey = "STBY RAD NAV";

    /// <summary>The variable behind <see cref="StbyRadNavKey"/> on RMP <paramref name="rmp"/>.</summary>
    public static string RadioModeVar(int rmp) => $"INI_{(rmp == 1 ? "CPT" : rmp == 2 ? "FO" : "OVHD")}_RADIO_MODE";

    /// <summary>The calculator string that presses <paramref name="key"/> on RMP <paramref name="rmp"/>.</summary>
    public static string Press(int rmp, string key) => key == StbyRadNavKey
        ? $"(L:{RadioModeVar(rmp)}) ! (>L:{RadioModeVar(rmp)})"
        : $"1 (>B:AIRLINER_RMP_{key}_{rmp}_Set)";

    /// <summary>The keypad keys a typed LS or VOR entry takes, or null when it is out of range: a frequency 108.00 to 117.95 with its two
    /// decimals (the point optional, 11390 is 113.90; 109.5 is 109.50), a course 0 to 360 in three digits. The field takes it as its digits
    /// complete (live 2026-10-30).</summary>
    public static IReadOnlyList<string>? TuningKeys(string typed, bool frequency)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (frequency)
        {
            string text = typed.Contains('.') || typed.Length != 5 ? typed : typed[..3] + "." + typed[3..];
            if (!text.All(c => char.IsDigit(c) || c == '.') || !double.TryParse(text, System.Globalization.NumberStyles.Float, culture, out double mhz) || mhz is < 108 or > 117.95)
                return null;
            return mhz.ToString("000.00", culture).Select(c => KeyFor(c)!).ToList();
        }
        if (typed.Length is 0 or > 3 || !typed.All(char.IsDigit) || int.Parse(typed, culture) > 360)
            return null;
        return typed.PadLeft(3, '0').Select(c => KeyFor(c)!).ToList();
    }

    /// <summary>A squawk for the stock XPNDR_SET (BCD, one octal digit a nibble), or null unless four digits 0-7. The
    /// A350 keeps TRANSPONDER CODE:1 and its RMPs in step (live: 8192 put SQUAWK : 2000 on RMP 1), whatever the
    /// keypad's entry state, as the A380 window sets its squawk.</summary>
    public static int? SquawkBcd(string code)
    {
        if (code.Length != 4 || code.Any(c => c is < '0' or > '7')) return null;
        return code.Aggregate(0, (bcd, c) => (bcd << 4) | (c - '0'));
    }

    /// <summary>What a page key's fresh draw says: up to three lines it changed, or that it changed nothing (the TEL key
    /// leaves the screen as it was in 1.2.7, live 2026-10-30).</summary>
    public static string PageReadBack(string page, IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        // STBY RAD NAV's own state is the row's call-out; what it did to the page is this.
        var changed = after.Where(l => !before.Contains(l) && !l.StartsWith(StbyRadNavKey + ":", StringComparison.Ordinal)).Take(3).ToList();
        return changed.Count > 0 ? string.Join(". ", changed) : $"{page} page: screen unchanged";
    }

    /// <summary>The gauge an RMP's screen is drawn by (RMP#0 is RMP 1).</summary>
    public static (string Gauge, int Index) Gauge(int rmp) => ("RMP", rmp - 1);
}
