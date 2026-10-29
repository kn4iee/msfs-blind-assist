using System.Text.RegularExpressions;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// BlueBird's tablet words, for snapping what OCR read to what the tablet says. Every list comes from
/// the tablet itself (the 2026-10-26 page crawl, read by OCR and checked by eye against the pictures,
/// and the EFB strings in BBS752.wasm: docs/b757/evidence/wasm-findings.md section 6). A read is
/// snapped only when it is close to one known word (edit distance at most a third of its length);
/// anything else is kept as read, so a label BlueBird adds later still reaches the pilot. Pure.
/// </summary>
public static class Bbs757EfbVocabulary
{
    /// <summary>The five apps on the home screen, as their page titles read.</summary>
    public static readonly string[] Apps = { "AIRCRAFT", "NAVIGATION", "SYSTEMS", "CHECKLIST", "SETTINGS" };

    /// <summary>Each app's side menu, top to bottom (the menu items the side column offers).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Menus = new Dictionary<string, string[]>
    {
        ["AIRCRAFT"] = new[] { "COCKPIT PRESETS", "FUEL", "WEIGHT & BALANCE", "GROUND SERVICES", "CABIN & DOORS", "MAINTENANCE", "FAILURES" },
        ["NAVIGATION"] = new[] { "ACARS DASHBOARD", "OPERATING FP", "WEATHER & NOTAM", "CHARTS", "MAP", "PERF CALC" },
        ["SETTINGS"] = new[] { "GENERAL", "AIRCRAFT", "LIVERY OPTIONS", "CALLOUTS", "CABIN AUDIO", "AIRAC DATA" },
        ["CHECKLIST"] = new[] { "RESET ALL", "INTERACTIVE", "BEFORE START", "AFTER START", "BEFORE TAKE OFF", "AFTER TAKE OFF",
                                "IN RANGE", "APPROACH", "BEFORE LANDING", "AFTER LANDING", "SHUT DOWN" },
    };

    /// <summary>Labels seen in page content (buttons, tiles, headings, row labels).</summary>
    public static readonly string[] Labels =
    {
        // Aircraft > Cockpit presets
        "PRESETS", "ON GROUND", "IN FLIGHT", "CUSTOM", "COLD & DARK", "TURNAROUND", "BEFORE START", "READY FOR TAXI",
        "READY FOR TAKEOFF", "AFTER TAKEOFF", "CRUISE", "APPROACH", "FINAL APPROACH", "SAVE USER CONFIG", "LOAD USER CONFIG",
        "QUICK START", "APU QUICK START", "ENGINE QUICK START", "IRS RAPID ALIGN", "CIRCUITS STATE", "PUSH ALL CBs",
        "ALL BREAKERS PUSHED IN", "BATTERY", "EXTERNAL POWER",
        // Ground services
        "REMOVE ALL", "JETWAY REMOVED", "JETWAY CONNECTED", "GPU REMOVED", "GPU CONNECTED", "FWD STAIR OFF", "FWD STAIR ON",
        "CARGO SVC OFF", "CARGO SVC ON", "GROUND A/C", "LEFT AIR START", "RIGHT AIR START", "CHOCKS", "FUEL TRUCK OFF",
        "FUEL TRUCK ON", "AFT STAIRS OFF", "AFT STAIRS ON", "CATERING OFF", "CATERING ON", "GND SRVC Mode:", "REQ PB TUG",
        "REMOVE TUG",
        // Cabin & doors
        "CLOSE ALL", "COCKPIT OPEN", "COCKPIT CLOSED", "CABIN LIGHTING", "GROUND SERVICE OFF", "GROUND SERVICE ON",
        "CEILING", "WINDOW", "ENTRY", "LEFT FWD", "RIGHT FWD", "FWD CARGO", "LEFT MID", "RIGHT MID", "AFT CARGO",
        "LEFT AFT", "RIGHT AFT",
        // Fuel, weight & balance
        "LEFT", "CENTER", "RIGHT", "TOTAL FUEL", "IMPORT FROM SIMBRIEF", "EMPTY", "ZFW", "ZFW CG", "TAKE-OFF", "LANDING",
        "FORWARD CARGO", "GROSS WEIGHT", "CENTER OF GRAVITY", "CG (%MAC)",
        // Maintenance
        "BRAKE TEMPERATURE", "LANDING GEAR", "HYDRAULIC", "OXYGEN", "FIRE PROTECT", "APU", "LEFT GEAR", "RIGHT GEAR",
        "LANDING GEAR DOORS", "REPAIR", "OPEN", "CLOSE",
        // Failures
        "CATALOG", "ACTIVE / ARMED", "FIRES", "DETECTION", "ENGINES / APU", "AUTOMATION", "ELECTRICAL", "AVIONICS",
        "ICE / RAIN", "DOORS", "AIR CONDITIONING", "FLIGHT CONTROLS", "HYDRAULICS", "LIGHTS", "FAILURE", "STATUS",
        "ACTIONS", "READY", "FAIL NOW", "SET", "RANDOM FAILURES:", "NOTIFICATIONS:", "L ENGINE FIRE", "R ENGINE FIRE",
        "APU FIRE", "FWD CARGO FIRE", "AFT CARGO FIRE",
        // Checklist
        "RESET", "RESET ALL", "INTERACTIVE",
        // Navigation
        "SIGN IN TO NAVIGRAPH",
        // Systems
        "SELECT",
        // Settings > General
        "LOCK KEYBOARD TO EFB", "EFB POWERED ON DURING INITIALIZATION", "EFB BRIGHTNESS MODE", "EFB BACKGROUND",
        "LIVE AUTOBRAKE INFO", "NOTIFICATIONS FADE-OUT TIMER", "EFB COLORS THEME", "EFB BRIGHTNESS",
        // Settings > Aircraft
        "ADAPTIVE COCKPIT LIGHTING", "TOD PAUSE DISTANCE (NM)", "NOSEWHEEL STEERING", "DISPLAYS BOOTUP SPEED",
        "SYNC BAROMETERS", "INITIAL COCKPIT DOOR", "PARKING BRAKE MODE", "IRS ALIGNMENT TIME", "BOARDING & LOAD RATE",
        "AUTOMATIC BOARDING", "AUTOMATIC GPU/JETWAY", "HOPPIE ACARS", "CPDLC NETWORK", "INITIAL CHOCKS IN PLACE",
        "DIRTY PANEL STATES",
        // Settings > Livery options, Callouts, Cabin audio
        "EHSI UP REFERENCE", "VNAV TAKEOFF BEHAVIOR", "EGPWS CALLOUTS", "PILOT CALLOUTS", "RADIO ALTIMETER (2500)",
        "RADIO ALTITUDE", "80 KTS", "V1", "ROTATE", "POSITIVE RATE", "ANNOUNCEMENTS", "SAFETY RELATED ANNOUNCEMENTS",
        "NON-SAFETY RELATED ANNOUNCEMENTS", "FLIGHT ATTENDANT CALLS", "PASSENGER SOUNDS",
        // Settings > AIRAC data
        "UNITS", "SIMBRIEF ID NUMBER", "HOPPIE LOGON CODE", "SAYINTENTIONS ACARS KEY", "LATEST CYCLE:", "INSTALLED CYCLE:",
        "LOGOUT", "UPDATE", "REMOVE",
    };

    /// <summary>Notes the tablet prints in small text (snapped like labels, by edit distance).</summary>
    public static readonly string[] Notes =
    {
        "Choose SAVE or LOAD, then select a panel state.",
        "Map tiles require the Navigraph tiles permission.",
        "Selections save automatically for this aircraft registration.",
        "Or scan this QR code using your phone!",
    };

    /// <summary>Setting values (the green word at the right of a settings row).</summary>
    public static readonly string[] Values =
    {
        "ON", "OFF", "AUTO", "MANUAL", "AUTOMATIC", "DEFAULT", "REALISTIC", "RUDDER", "FAST", "EASY", "OPEN", "CLOSED",
        "STANDARD", "QUICK", "INSTANT", "NEVER", "1 MIN", "2 MIN", "5 MIN", "FULL (7 MIN+)", "IMPERIAL", "METRIC",
        "HOPPIE", "SAYINTENTIONS", "OPTION 1", "OPTION 2", "TRACK UP", "HEADING UP", "MSFS", "BBS", "DARK BLUE",
        "LIGHT BLUE", "DARK GREY", "LIGHT GREY", "DARK", "CHECKED", "ALIGNED", "NORMAL", "CLOSED & LOCKED",
    };

    /// <summary>
    /// The option lists of the settings that are sliders, in the slider's order (left to right),
    /// for the window's choice lists. A slider not listed is offered as a number (seconds, miles)
    /// or as left/right steps. Measured by tapping along each track (docs/b757/evidence/flight-11.md).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> SliderOptions = new Dictionary<string, string[]>
    {
        ["IRS ALIGNMENT TIME"] = new[] { "INSTANT", "1 MIN", "2 MIN", "5 MIN", "FULL (7 MIN+)" },
        ["BOARDING & LOAD RATE"] = new[] { "REALISTIC", "STANDARD", "QUICK", "INSTANT" },
        ["NOTIFICATIONS FADE-OUT TIMER"] = new[] { "2 SEC", "4 SEC", "6 SEC", "8 SEC", "10 SEC", "12 SEC", "14 SEC", "16 SEC",
                                                   "18 SEC", "20 SEC", "22 SEC", "24 SEC", "26 SEC", "28 SEC", "30 SEC", "NEVER" },
        ["EFB COLORS THEME"] = new[] { "DARK", "LIGHT BLUE", "DARK GREY", "LIGHT GREY", "DARK BLUE" },
        ["CPDLC NETWORK"] = new[] { "HOPPIE", "SAYINTENTIONS" },
    };

    /// <summary>Rows whose value is a credential: never shown, spoken or logged (plan 11).</summary>
    public static readonly string[] CredentialLabels = { "SIMBRIEF ID NUMBER", "HOPPIE LOGON CODE", "SAYINTENTIONS ACARS KEY" };

    /// <summary>Whether a read is exactly one of the tablet's words (so a two-letter ON is a word,
    /// not an icon glyph).</summary>
    public static bool IsKnown(string t) => All.Contains(t.Trim(), StringComparer.OrdinalIgnoreCase);

    public static bool IsCredential(string label) =>
        CredentialLabels.Any(c => string.Equals(c, label, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] All =
        Apps.Concat(Menus.Values.SelectMany(m => m)).Concat(Labels).Concat(Values).Concat(Notes).Distinct(StringComparer.Ordinal).ToArray();

    // OCR's habit of reading a small dim "1" or "0" as a letter before a unit ("IO SEC", "to SEC").
    private static readonly Regex SecondsMisread = new(@"^(?:[Il|][Oo0]|[tT][oO])\s*SEC$", RegexOptions.CultureInvariant);
    private static readonly Regex Seconds = new(@"^(\d{1,2})\s*SEC$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// What OCR read, snapped to the tablet's word when it is one. Strips a stray icon glyph read
    /// as a leading token ("E) FUEL", "e CABIN & DOORS", "V AIRCRAFT"), fixes the seconds misread,
    /// keeps the state after a label that ends in a colon ("RANDOM FAILURES: OFF"), then snaps to
    /// the nearest known word within the edit-distance limit; else the read, trimmed.
    /// </summary>
    public static string Snap(string read)
    {
        string t = (read ?? "").Trim();
        if (t.Length == 0) return t;
        if (SecondsMisread.IsMatch(t)) return "10 SEC";
        var sec = Seconds.Match(t);
        if (sec.Success) return sec.Groups[1].Value + " SEC";

        // A label and its state on one line (PaddleOCR reads "RANDOM FAILURES: OFF" whole): the label
        // snaps and the rest stays as its value. Snapped whole, the line was the bare label, its state lost.
        int colon = t.IndexOf(':');
        if (colon > 0 && t[(colon + 1)..].Trim() is { Length: > 0 } value
            && Nearest(t[..(colon + 1)]) is { } label && label.EndsWith(':'))
            return label + " " + value;

        // A dropdown arrow or a stray mark read as a one-letter last word ("SET w", measured).
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1 && words[^1].Length == 1 && !char.IsDigit(words[^1][0]))
        {
            var trimmed = string.Join(' ', words[..^1]);
            if (Nearest(trimmed) is string tb) return tb;
        }

        var best = Nearest(t);
        if (best != null) return best;
        // A leading icon glyph: drop the first short token and try again.
        int sp = t.IndexOf(' ');
        if (sp > 0 && sp <= 2)
        {
            var rest = t.Substring(sp + 1).Trim();
            var b2 = Nearest(rest);
            if (b2 != null) return b2;
            if (rest.Length >= 3 && sp <= 2 && !char.IsLetterOrDigit(t[sp - 1])) return rest;
        }
        return t;
    }

    /// <summary>
    /// The known word nearest <paramref name="t"/>, or null when none is within the limit. A read
    /// with small letters ("Parking brake", "Windows": checklist lines) is matched only against the
    /// known words that have small letters too (the notes): the tablet's labels are capitals, and
    /// snapping a checklist line to a label is how "Parking brake" became PARKING BRAKE MODE. A read
    /// with a number of its own ("ACTIVE 0 ARMED 0", the Failures counts) is matched only against the
    /// known words with a figure: snapped to ACTIVE / ARMED, the counts were lost.
    /// </summary>
    public static string? Nearest(string t)
    {
        if (t.Length < 2) return null;
        // Mixed case = mostly small letters (a sentence); a capitals label with a stray small letter
        // or two read off noise ("RIOHT an START", "Apu FIRE") is still a label.
        int letters = t.Count(char.IsLetter), lower = t.Count(char.IsLower);
        bool mixed = letters > 0 && lower * 2 > letters;
        bool number = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(word => word.All(char.IsDigit));
        string up = t.ToUpperInvariant();
        string? best = null;
        int bestD = int.MaxValue;
        foreach (var w in All)
        {
            if (mixed && !w.Any(char.IsLower)) continue;
            if (number && !w.Any(char.IsDigit)) continue;
            if (Math.Abs(w.Length - up.Length) > Math.Max(2, w.Length / 3)) continue;
            int d = Distance(up, w.ToUpperInvariant());
            if (d < bestD) { bestD = d; best = w; }
        }
        if (best != null)
        {
            int limit = best.Length <= 3 ? 0 : best.Length <= 5 ? 1 : best.Length / 3;
            if (bestD <= limit) return best;
        }
        return mixed ? null : ByWords(up);
    }

    /// <summary>
    /// A read that dropped a word ("RICHT START", "FUEL OFF", "REQ PB", measured): the ONE known label
    /// whose words contain the read's words in order, each within one edit (exact for words of three
    /// letters or fewer), with exactly one word missing, read in two words or more. Null when none or
    /// more than one fit.
    /// </summary>
    private static string? ByWords(string up)
    {
        var read = up.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (read.Length < 2) return null;
        string? hit = null;
        foreach (var w in All)
        {
            var label = w.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (label.Length != read.Length + 1) continue;
            int j = 0;
            foreach (var lw in label)
            {
                if (j < read.Length && WordMatch(read[j], lw)) j++;
            }
            if (j != read.Length) continue;
            if (hit != null && hit != w) return null;   // ambiguous
            hit = w;
        }
        return hit;
    }

    private static bool WordMatch(string read, string word) =>
        word.Length <= 3 ? read == word : Distance(read, word) <= 1;

    /// <summary>Levenshtein distance.</summary>
    public static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    /// <summary>"COCKPIT PRESETS" → "Cockpit presets"; keeps short all-caps tokens (GPU, APU, IRS,
    /// CG, ZFW, ACARS, NM, PB, SVC, A/C) as they are, so a screen reader spells them.</summary>
    public static string Pretty(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Any(char.IsLower)) return s;
        var words = s.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            var w = words[i];
            if (w.Length <= 1 || Acronyms.Contains(w.Trim('(', ')', ':', ',')) || w.Any(char.IsDigit)) continue;
            if (!w.Any(char.IsLetter)) continue;
            words[i] = (i == 0 ? w.Substring(0, 1) : w.Substring(0, 1).ToLowerInvariant()) + w.Substring(1).ToLowerInvariant();
        }
        return string.Join(' ', words);
    }

    private static readonly HashSet<string> Acronyms = new(StringComparer.Ordinal)
    {
        "GPU", "APU", "IRS", "CG", "ZFW", "ACARS", "NM", "PB", "SVC", "A/C", "EFB", "TOD", "CPDLC", "FP", "NOTAM", "PERF",
        "AIRAC", "SIMBRIEF", "ID", "EHSI", "VNAV", "EGPWS", "RA", "KTS", "V1", "CBs", "MAC", "%MAC", "FWD", "AFT", "L", "R",
        "BBS", "MSFS", "GND", "SRVC", "UP", "OK", "OFP",
    };
}
