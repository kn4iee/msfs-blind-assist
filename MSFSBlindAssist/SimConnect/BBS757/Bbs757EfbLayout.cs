using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>A point on BlueBird's EFB, in the tablet's own pixels (its gauge is 1397 wide).</summary>
public sealed record EfbPoint(int X, int Y);

/// <summary>
/// Where BlueBird's EFB tablet ("BBOS", release 0.1.0) has its buttons, and the click sequences the
/// 757 EFB window runs through <see cref="CoherentBbs757EfbClient"/>. The tablet is a WASM picture:
/// nothing on it can be read or found by name, so every button is a position measured on the
/// tablet (docs/b757/evidence/efb-and-manual.md). Positions marked "est." were not clicked yet:
/// they follow the measured tiles' grid and are to be measured in the sim. A BlueBird update can
/// move any of them, so they all live in this one table.
///
/// Every sequence that navigates starts from the home screen (Home, then 1500 ms), so it works
/// whatever page the tablet was left on. The one exception is <see cref="AnswerUnits"/>: the weight
/// units question is a dialog over the ACARS dashboard, answered where it is.
///
/// The UPDATE button on the AIRAC page and BlueBird's REMOVE button share one spot
/// (<see cref="UpdateButton"/>): only <see cref="UpdateNavData"/> clicks there, and the window runs
/// it only when <see cref="ShouldUpdate"/> says the installed cycle is out of date. Pure.
/// </summary>
public static class Bbs757EfbLayout
{
    /// <summary>After a click that opens an app or a page.</summary>
    public const int NavigateDelayMs = 1500;

    /// <summary>After the SimBrief ID field, while the on-screen keypad opens.</summary>
    public const int KeypadOpenDelayMs = 1000;

    /// <summary>Between keypad digits.</summary>
    public const int DigitDelayMs = 500;

    /// <summary>After the keypad's OK, while BlueBird stores the ID.</summary>
    public const int KeypadOkDelayMs = 1000;

    /// <summary>How long the AIRAC page is left to show its state before UPDATE is clicked.</summary>
    public const int AiracPageSettleMs = 4000;

    // ---- positions (measured unless marked est.) ---------------------------

    /// <summary>The home button, top left: the home screen from any app.</summary>
    public static readonly EfbPoint Home = new(19, 62);

    // Home screen icons.
    public static readonly EfbPoint AircraftIcon = new(320, 190);
    public static readonly EfbPoint NavigationIcon = new(503, 190);
    public static readonly EfbPoint SettingsIcon = new(1073, 190);

    // Settings menu.
    public static readonly EfbPoint AiracData = new(95, 486);

    // AIRAC data page.
    public static readonly EfbPoint SimBriefField = new(842, 126);

    /// <summary>
    /// UPDATE on the AIRAC data page while a newer cycle is available, and BlueBird's REMOVE (the
    /// installed nav data) once the installed cycle is current: the same spot.
    /// </summary>
    public static readonly EfbPoint UpdateButton = new(1291, 337);

    // The on-screen numeric keypad the SimBrief ID field opens.
    public static readonly EfbPoint Key7 = new(823, 255);
    public static readonly EfbPoint Key8 = new(936, 255);
    public static readonly EfbPoint Key9 = new(1052, 255);
    public static readonly EfbPoint Key4 = new(823, 334);
    public static readonly EfbPoint Key5 = new(936, 334);
    public static readonly EfbPoint Key6 = new(1052, 334);
    public static readonly EfbPoint Key1 = new(823, 413);
    public static readonly EfbPoint Key2 = new(936, 413);
    public static readonly EfbPoint Key3 = new(1052, 413);
    public static readonly EfbPoint Key0 = new(936, 489);
    public static readonly EfbPoint KeyBackspace = new(823, 565);
    public static readonly EfbPoint KeyDelete = new(936, 565);
    public static readonly EfbPoint KeyOk = new(1052, 565);

    // Navigation app.
    public static readonly EfbPoint AcarsDashboard = new(135, 179);

    // ACARS dashboard.
    public static readonly EfbPoint FetchOfpButton = new(441, 120);

    // The weight units question after an OFP fetch.
    public static readonly EfbPoint UnitsYes = new(390, 658);
    public static readonly EfbPoint UnitsNo = new(993, 658);            // est.

    // Cockpit presets (the Aircraft app opens on COCKPIT PRESETS).
    public static readonly EfbPoint ColdAndDark = new(433, 214);        // est.
    public static readonly EfbPoint Turnaround = new(433, 266);         // est.
    public static readonly EfbPoint BeforeStart = new(433, 319);
    public static readonly EfbPoint ReadyForTaxi = new(433, 372);       // est.
    public static readonly EfbPoint ReadyForTakeoff = new(433, 424);    // est.
    public static readonly EfbPoint AfterTakeoff = new(818, 214);       // est.
    public static readonly EfbPoint Cruise = new(818, 266);             // est.
    public static readonly EfbPoint Approach = new(818, 319);           // est.
    public static readonly EfbPoint FinalApproach = new(818, 372);      // est.

    // Quick start (same page).
    public static readonly EfbPoint ApuQuickStart = new(433, 524);
    public static readonly EfbPoint EngineQuickStart = new(818, 524);   // est.
    public static readonly EfbPoint IrsRapidAlign = new(1197, 524);     // est.

    private static readonly (string Name, EfbPoint Point)[] Presets =
    {
        ("Cold and dark", ColdAndDark),
        ("Turnaround", Turnaround),
        ("Before start", BeforeStart),
        ("Ready for taxi", ReadyForTaxi),
        ("Ready for takeoff", ReadyForTakeoff),
        ("After takeoff", AfterTakeoff),
        ("Cruise", Cruise),
        ("Approach", Approach),
        ("Final approach", FinalApproach),
    };

    private static readonly (string Name, EfbPoint Point)[] QuickStarts =
    {
        ("APU quick start", ApuQuickStart),
        ("Engine quick start", EngineQuickStart),
        ("IRS rapid align", IrsRapidAlign),
    };

    /// <summary>The cockpit presets, as the window lists them, in the tablet's order.</summary>
    public static IReadOnlyList<string> PresetNames { get; } = Presets.Select(p => p.Name).ToArray();

    /// <summary>The quick start tiles: "APU quick start", "Engine quick start", "IRS rapid align".</summary>
    public static IReadOnlyList<string> QuickStartNames { get; } = QuickStarts.Select(p => p.Name).ToArray();

    // ---- the nav data update guard -----------------------------------------

    /// <summary>
    /// Whether the nav data update may click: only when nothing is installed or the installed cycle
    /// is older than the one in force on <paramref name="today"/> (UTC). Once the installed cycle
    /// is current, the UPDATE spot is BlueBird's REMOVE button.
    /// </summary>
    public static bool ShouldUpdate(int? installed, DateOnly today) =>
        installed is null || installed < AiracCycle.ForDate(today);

    // ---- SimBrief ID ---------------------------------------------------------

    /// <summary>
    /// Whether <paramref name="text"/> is a SimBrief ID the keypad can type and BlueBird can store:
    /// ASCII digits only (no sign, space or separator), 1 to int.MaxValue. Never parsed as a
    /// number in the PC's culture; the digits are typed as they are.
    /// </summary>
    public static bool IsSimBriefId(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 10) return false;
        long value = 0;
        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c)) return false;
            value = value * 10 + (c - '0');
        }
        return value is >= 1 and <= int.MaxValue;
    }

    /// <summary>The value of a SimBrief ID <see cref="IsSimBriefId"/> accepted.</summary>
    public static long SimBriefIdValue(string digits)
    {
        if (!IsSimBriefId(digits)) throw new ArgumentException("Not a SimBrief ID.", nameof(digits));
        long value = 0;
        foreach (char c in digits) value = value * 10 + (c - '0');
        return value;
    }

    private static EfbPoint KeyFor(char digit) => digit switch
    {
        '0' => Key0, '1' => Key1, '2' => Key2, '3' => Key3, '4' => Key4,
        '5' => Key5, '6' => Key6, '7' => Key7, '8' => Key8, '9' => Key9,
        _ => throw new ArgumentException("Not a keypad digit.", nameof(digit)),
    };

    // ---- sequences -------------------------------------------------------------

    /// <summary>Home, Settings, AIRAC data.</summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> OpenAiracPage() => new[]
    {
        (Home, NavigateDelayMs),
        (SettingsIcon, NavigateDelayMs),
        (AiracData, NavigateDelayMs),
    };

    /// <summary>
    /// The AIRAC page, left <see cref="AiracPageSettleMs"/> to show its state, then UPDATE. One
    /// sequence, so the click runs on the page the same run opened (a link lost between two runs
    /// could otherwise land it on another page). Run it only when <see cref="ShouldUpdate"/> says so.
    /// </summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> UpdateNavData()
    {
        var seq = OpenAiracPage().ToList();
        seq[^1] = (seq[^1].Point, seq[^1].DelayAfterMs + AiracPageSettleMs);
        seq.Add((UpdateButton, 0));
        return seq;
    }

    /// <summary>The AIRAC page, the SimBrief ID field, each digit 500 ms apart, then OK.</summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> EnterSimBriefId(string digits)
    {
        if (!IsSimBriefId(digits)) throw new ArgumentException("A SimBrief ID is digits only.", nameof(digits));
        var seq = OpenAiracPage().ToList();
        seq.Add((SimBriefField, KeypadOpenDelayMs));
        foreach (char c in digits) seq.Add((KeyFor(c), DigitDelayMs));
        seq.Add((KeyOk, KeypadOkDelayMs));
        return seq;
    }

    /// <summary>Home, Navigation, ACARS dashboard, FETCH SIMBRIEF OFP.</summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> FetchOfp() => new[]
    {
        (Home, NavigateDelayMs),
        (NavigationIcon, NavigateDelayMs),
        (AcarsDashboard, NavigateDelayMs),
        (FetchOfpButton, NavigateDelayMs),
    };

    /// <summary>
    /// The answer to "switch the aircraft's weight units to the plan's?": YES (accept) or NO
    /// (keep). The dialog is on screen, so this is one click where it is, not from the home screen.
    /// </summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> AnswerUnits(bool accept) => new[]
    {
        (accept ? UnitsYes : UnitsNo, NavigateDelayMs),
    };

    /// <summary>Home, Aircraft (opens on COCKPIT PRESETS), then the preset's tile.</summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> LoadPreset(string name) =>
        FromAircraftApp(Lookup(Presets, name, nameof(name)));

    /// <summary>Home, Aircraft, then the quick start tile ("APU quick start", "Engine quick start", "IRS rapid align").</summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> QuickStart(string name) =>
        FromAircraftApp(Lookup(QuickStarts, name, nameof(name)));

    private static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> FromAircraftApp(EfbPoint tile) => new[]
    {
        (Home, NavigateDelayMs),
        (AircraftIcon, NavigateDelayMs),
        (tile, NavigateDelayMs),
    };

    private static EfbPoint Lookup((string Name, EfbPoint Point)[] table, string name, string paramName)
    {
        foreach (var (n, p) in table)
            if (string.Equals(n, name, StringComparison.Ordinal)) return p;
        throw new ArgumentException($"No EFB tile named '{name}'.", paramName);
    }

    /// <summary>
    /// A sequence as the agent's <c>clickSeq</c> takes it: <c>[[x,y,delayAfterMs],…]</c>, integers
    /// written invariant.
    /// </summary>
    public static string ToJson(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < seq.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var (p, delay) = seq[i];
            sb.Append('[')
              .Append(p.X.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(p.Y.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(delay.ToString(CultureInfo.InvariantCulture)).Append(']');
        }
        return sb.Append(']').ToString();
    }

    /// <summary>The whole time a sequence takes, every click's delay after it included.</summary>
    public static int TotalMs(IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq) => seq.Sum(s => s.DelayAfterMs);
}
