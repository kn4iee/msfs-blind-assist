using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Forms.IniA330;

/// <summary>One key of a typed entry, and the MCDU's typed line once that key has shown on it.</summary>
internal readonly record struct CduTypedKey(string Key, string Expected);

/// <summary>
/// The aircraft-specific half of the MCDU window (<see cref="IniA330CduForm"/>): the side's press-flag prefix and
/// display, the page keys and their shortcuts, and what each typed character presses. Kept apart from the form so the
/// window itself carries no A330 names.
/// </summary>
internal sealed record CduFormProfile(
    string Name,
    string KeyPrefix,
    A330Gauge Gauge,
    bool FirstOfficer,
    (string label, string key, string tip)[] PageKeys,
    IReadOnlyDictionary<Keys, string> AltKeys,
    string SlewUp,
    string SlewDown,
    string NextPage,
    string? PrevPage,
    Func<char, string?> KeyForChar)
{
    private const string CaptainPrefix = "INI_MCDU1_";
    private const string FirstOfficerPrefix = "INI_MCDU2_";

    /// <summary>What the cockpit calls the unit, used in the window title and every status message.</summary>
    public string Device { get; init; } = "MCDU";

    /// <summary>
    /// The A330 MCDU of one side: the captain's (<c>INI_MCDU1_</c>, <see cref="A330Gauge.McduCaptain"/>) or the first
    /// officer's (<c>INI_MCDU2_</c>, <see cref="A330Gauge.McduFirstOfficer"/>); both sides have the same keys. Key
    /// names are the aircraft's own <c>INI_MCDU1_&lt;KEY&gt;</c> press flags (write 1, the module consumes it), measured
    /// 2026-10-16 by pressing each and reading the page title: DIR, PROG, PERF, INIT, DATA, FPLAN, RADNAV, FUEL, SEC,
    /// ATC, MENU, AIRPORT, PREV, NEXT. They follow the iniBuilds A320's names (HubHop), which is where the slew keys
    /// (UARROW / DOWN), OVERFLY and the punctuation (DECIMAL, SPACE, PLUS, SLASH) come from; each did what its name
    /// says when measured on 2026-11-20. Page Up/Down slew and Alt+Left/Right page, as in the app's other Airbus MCDU
    /// windows.
    /// </summary>
    public static CduFormProfile A330(bool firstOfficer) => new(
        "A330", firstOfficer ? FirstOfficerPrefix : CaptainPrefix,
        firstOfficer ? A330Gauge.McduFirstOfficer : A330Gauge.McduCaptain, firstOfficer,
        new[]
        {
            ("&DIR", "DIR", "Alt+D"), ("PROG", "PROG", "Alt+G"), ("&PERF", "PERF", "Alt+P"), ("&INIT", "INIT", "Alt+I"),
            ("D&ATA", "DATA", "Alt+A"), ("&F-PLN", "FPLAN", "Alt+F"), ("&RAD NAV", "RADNAV", "Alt+R"),
            ("FUEL PRED", "FUEL", "Alt+U"), ("SEC F-PLN", "SEC", "Alt+X"), ("ATC COMM", "ATC", "Alt+T"),
            ("MCDU &MENU", "MENU", "Alt+M"), ("AIRPORT", "AIRPORT", "Alt+O"), ("OVFY", "OVERFLY", "Alt+V"),
            ("Previous page", "PREV", "Alt+Left"), ("Next page", "NEXT", "Alt+Right"),
            ("Slew up", "UARROW", "Page Up"), ("Slew down", "DOWN", "Page Down"), ("CLR", "CLR", "Backspace"),
        },
        new Dictionary<Keys, string>
        {
            [Keys.D] = "DIR", [Keys.G] = "PROG", [Keys.P] = "PERF", [Keys.I] = "INIT", [Keys.A] = "DATA",
            [Keys.F] = "FPLAN", [Keys.R] = "RADNAV", [Keys.U] = "FUEL", [Keys.X] = "SEC",
            [Keys.T] = "ATC", [Keys.M] = "MENU", [Keys.O] = "AIRPORT", [Keys.V] = "OVERFLY",
        },
        // Key for key, as the FlyByWire A320 and Fenix windows: Page Up and Alt+Up press the MCDU's
        // up-arrow key, Page Down and Alt+Down its down-arrow key (the owner's ruling, 2026-11-20; this
        // window had them swapped). What the keys do is the aircraft's: UARROW moves on to later rows
        // (measured live 2026-10-17 on the F-PLN page and 2026-11-20 on the arrival lists), DOWN back
        // toward the top.
        SlewUp: "UARROW", SlewDown: "DOWN", NextPage: "NEXT", PrevPage: "PREV",
        KeyForChar: A330KeyForChar);

    /// <summary>The same window on the other side's MCDU: its press flags and its display; everything else kept.</summary>
    public CduFormProfile ForSide(bool firstOfficer) => this with
    {
        KeyPrefix = firstOfficer ? FirstOfficerPrefix : CaptainPrefix,
        Gauge = firstOfficer ? A330Gauge.McduFirstOfficer : A330Gauge.McduCaptain,
        FirstOfficer = firstOfficer,
    };

    /// <summary>
    /// The keys that type <paramref name="text"/> on a typed line that holds <paramref name="pad"/>, each with the line
    /// it should leave. Upper case; a character with no key is left out. A <c>+</c> is the +/- key pressed twice: the
    /// first press types <c>-</c>, the second turns that <c>-</c> into <c>+</c> (measured 2026-11-20). Pure.
    /// </summary>
    public IReadOnlyList<CduTypedKey> KeysToType(string text, string pad)
    {
        var keys = new List<CduTypedKey>();
        string shown = pad;
        foreach (char c in text.ToUpperInvariant())
        {
            if (KeyForChar(c) is not { } key) continue;
            if (c == '+')
            {
                shown += "-";
                keys.Add(new CduTypedKey(key, shown));
                shown = shown[..^1] + "+";
                keys.Add(new CduTypedKey(key, shown));
                continue;
            }
            shown += c;
            keys.Add(new CduTypedKey(key, shown));
        }
        return keys;
    }

    internal static string? A330KeyForChar(char c) => c switch
    {
        >= 'A' and <= 'Z' => c.ToString(),
        >= '0' and <= '9' => c.ToString(),
        '.' => "DECIMAL",
        '/' => "SLASH",
        '-' or '+' => "PLUS",   // the A330 MCDU has one +/- key
        ' ' => "SPACE",
        _ => null,
    };
}

/// <summary>
/// A CDU line as a screen reader user hears it, with what the A330 says in colour that matters for
/// operating it: text drawn GREY is not available yet (an option the FMS will not take, a field
/// that is not active) and is marked so — nothing else in the line can tell the pilot. Every other
/// colour is read as plain text. Pure.
/// </summary>
internal static class CduLineText
{
    private const int MaxRowCells = 37;
    private const string Unavailable = " (unavailable)";
    private const string Inop = " (inop)";

    private static bool IsGrey(MSFSBlindAssist.Aircraft.A330.Mcdu.A330McduCell? cell)
    {
        if (cell?.Colour is not { } c) return false;
        float max = Math.Max(c.R, Math.Max(c.G, c.B)), min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max - min <= 0.02f && max < 0.5f;
    }

    /// <summary>
    /// The row's text with the greyed-prompt marker: one marker after each run of grey fields, written into
    /// the blanks after the run (never moving a later field) or after the row's last character, and never
    /// taking the row past 37 cells. A cell with no colour is never grey. Pure.
    /// </summary>
    public static string ForWindow(MSFSBlindAssist.Aircraft.A330.Mcdu.A330McduLine line)
    {
        var chars = new char[MaxRowCells];
        Array.Fill(chars, ' ');
        int n = Math.Min(line.Text.Length, MaxRowCells);
        for (int i = 0; i < n; i++) chars[i] = line.Text[i];

        // fields: runs of non-blank cells with at most one blank between two of them
        var fields = new List<(int Start, int End, bool Grey)>();
        int col = 0, width = Math.Min(line.Cells.Count, line.Text.Length);
        while (col < width)
        {
            if (line.Text[col] == ' ') { col++; continue; }
            int start = col, end = col, nonBlank = 0, grey = 0;
            while (col < width)
            {
                if (line.Text[col] != ' ') { end = col; nonBlank++; if (IsGrey(line.Cells[col])) grey++; col++; }
                else if (col + 1 < width && line.Text[col + 1] != ' ') col++;
                else break;
            }
            fields.Add((start, end, grey * 2 > nonBlank));
        }

        int length = line.Text.Length;
        for (int f = 0; f < fields.Count; f++)
        {
            if (!fields[f].Grey || (f + 1 < fields.Count && fields[f + 1].Grey)) continue;
            int after = fields[f].End + 1;
            if (f + 1 == fields.Count)
            {
                string marker = length + Unavailable.Length > MaxRowCells ? Unavailable.TrimStart() : Unavailable;
                for (int k = 0; k < marker.Length && after + k < MaxRowCells; k++) chars[after + k] = marker[k];
            }
            else
            {
                int gap = fields[f + 1].Start - after;
                string? marker = gap >= 16 ? Unavailable : gap >= 9 ? Inop : null;
                if (marker != null) for (int k = 0; k < marker.Length; k++) chars[after + k] = marker[k];
            }
        }
        return new string(chars).TrimEnd();
    }

    /// <summary>The window's list from the 14 lines: title, each label row (indented, no number), each data row "n: value", then the scratchpad.</summary>
    public static IReadOnlyList<string> Listing(IReadOnlyList<MSFSBlindAssist.Aircraft.A330.Mcdu.A330McduLine> lines)
    {
        var result = new List<string> { ForWindow(lines[0]).Trim() };
        for (int k = 1; k <= 6; k++)
        {
            string label = ForWindow(lines[2 * k - 1]);
            if (label.Length > 0) result.Add(("   " + label).TrimEnd());
            string data = ForWindow(lines[2 * k]);
            result.Add(data.Length > 0 ? $"{k}: {data}" : $"{k}:");
        }
        result.Add(("Scratchpad: " + ForWindow(lines[MSFSBlindAssist.Aircraft.A330.Mcdu.A330McduLines.ScratchpadRow])).TrimEnd());
        return result;
    }

    public static string WithColour(A330CduLine line)
    {
        var text = line.Text;
        if (line.Chars.Count == 0) return IniA330CduForm.Tidy(text);
        // align every non-space character of the text with the glyph it came from ("×" reads as "><")
        var colours = new string[text.Length];
        int gi = 0, sub = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == ' ' || gi >= line.Chars.Count) { colours[i] = ""; continue; }
            var c = line.Chars[gi];
            colours[i] = c.Colour;
            int width = c.Text == A330CduScreenReader.OverlappedMarkers ? 2 : Math.Max(1, c.Text.Length);
            if (++sub >= width) { gi++; sub = 0; }
        }
        return Annotate(text, colours);
    }

    /// <param name="colours">One colour per character of <paramref name="text"/> ("" for spaces).</param>
    internal static string Annotate(string text, string[] colours)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            if (text[i] == ' ') { sb.Append(' '); i++; continue; }
            int s = i;
            while (i < text.Length && text[i] != ' ') i++;
            bool grey = Enumerable.Range(s, i - s).Count(k => colours[k] == CduColour.Grey) * 2 > i - s;
            sb.Append(text, s, i - s);
            if (grey)
            {
                // a run of grey words gets ONE marker, after its last word
                int j = i;
                while (j < text.Length && text[j] == ' ') j++;
                int e = j;
                while (e < text.Length && text[e] != ' ') e++;
                bool nextGrey = e > j && Enumerable.Range(j, e - j).Count(k => colours[k] == CduColour.Grey) * 2 > e - j;
                if (!nextGrey || j - i >= 3) sb.Append(" (unavailable)");
            }
        }
        return IniA330CduForm.Tidy(sb.ToString());
    }
}
