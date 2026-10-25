using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>A point on BlueBird's EFB, in the tablet's own pixels (its gauge is 1397 wide).</summary>
public sealed record EfbPoint(int X, int Y);

/// <summary>What <see cref="Bbs757EfbLayout.MayClickUpdate"/> decided, the first reason not to click winning.</summary>
public enum Bbs757UpdateGate
{
    /// <summary>The spot is UPDATE as far as anything outside the aircraft can tell: click.</summary>
    Allowed,
    /// <summary>The installed cycle is current: the spot is BlueBird's REMOVE.</summary>
    Current,
    /// <summary>Not signed in to Navigraph: BlueBird cannot have found a newer cycle.</summary>
    NotSignedIn,
    /// <summary>The cycle in force took effect today (UTC): no page load qualifies before the next midnight UTC.</summary>
    NewToday,
    /// <summary>
    /// The page loaded before the cycle's first full day, or the pilot was seen signed out during
    /// this load: BlueBird's load-time check cannot have seen the cycle. A reload is needed.
    /// </summary>
    ReloadFirst,
    /// <summary>The page loaded under a minute ago: BlueBird may still be checking.</summary>
    StillChecking,
}

/// <summary>Which kind of click sequence ran last (the units answer may only follow a finished fetch).</summary>
public enum Bbs757EfbRunKind { Update, SimBriefId, FetchOfp, Units, Preset, QuickStart, Steering }

/// <summary>What became of a SimBrief ID entry, judged from the stored ID before and after.</summary>
public enum Bbs757SimBriefEntry { Saved, NotSaved, Different }

/// <summary>
/// Conditions the EFB agent checks on its own page before a run starts, so a check made by the
/// app and the click land on the same page: the page loaded at or after
/// <see cref="NotBeforeLoadMs"/> (Unix ms, UTC), it is at least <see cref="MinPageAgeMs"/> old,
/// no run with <see cref="OnceKey"/> has started on it, and it is the page that loaded at
/// <see cref="SamePageAsMs"/>. Null means no such condition.
/// </summary>
public sealed record Bbs757EfbRunGuard(
    long? NotBeforeLoadMs = null, int? MinPageAgeMs = null, string? OnceKey = null, long? SamePageAsMs = null);

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
/// (<see cref="UpdateButton"/>): only <see cref="UpdateNavData"/> clicks there. The window runs it
/// only when <see cref="MayClickUpdate"/> allows it, under <see cref="UpdateGuard"/>, which the agent
/// checks again on the page it clicks; the agent refuses any click near the spot without that
/// guard. Pure.
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

    /// <summary>Keypad backspaces before the SimBrief ID's digits: more than any stored ID has.</summary>
    public const int SimBriefBackspaces = 10;

    /// <summary>How old the tablet's page must be before UPDATE: BlueBird checks for a new cycle at load.</summary>
    public const int UpdateMinPageAgeMs = 60_000;

    /// <summary>The nav data update runs at most once per page (flight load).</summary>
    public const string UpdateOnceKey = "airac-update";

    // ---- positions (measured unless marked est.) ---------------------------

    /// <summary>The home button, top left: the home screen from any app.</summary>
    public static readonly EfbPoint Home = new(19, 62);

    // Home screen icons.
    public static readonly EfbPoint AircraftIcon = new(320, 190);
    public static readonly EfbPoint NavigationIcon = new(503, 190);
    public static readonly EfbPoint SettingsIcon = new(1073, 190);

    // Settings menu.
    public static readonly EfbPoint AiracData = new(95, 486);

    /// <summary>The Settings menu's AIRCRAFT item (measured 2026-10-25, session 6).</summary>
    public static readonly EfbPoint SettingsAircraft = new(98, 244);

    /// <summary>
    /// The NOSEWHEEL STEERING switch on Settings &gt; AIRCRAFT, third row (measured 2026-10-25, session 6:
    /// one click switched REALISTIC, the tiller, to RUDDER). It TOGGLES: a click while it is RUDDER
    /// switches it back to the tiller, so it is clicked only under <see cref="MayClickSteering"/>.
    /// </summary>
    public static readonly EfbPoint NosewheelSteeringSwitch = new(1154, 249);

    // AIRAC data page.
    public static readonly EfbPoint SimBriefField = new(842, 126);

    /// <summary>
    /// UPDATE on the AIRAC data page while a newer cycle is available, and BlueBird's REMOVE (the
    /// installed nav data) once the installed cycle is current: the same spot.
    /// </summary>
    public static readonly EfbPoint UpdateButton = new(1291, 337);

    /// <summary>
    /// How far either side of <see cref="UpdateButton"/>, across and down, a click counts as one on
    /// the UPDATE/REMOVE button: the agent refuses such a click without the update's page guards
    /// (its UPDATE_HALF_W and UPDATE_HALF_H). Wider than the button, so no near miss slips through.
    /// </summary>
    public const int UpdateSpotHalfWidth = 100;

    /// <inheritdoc cref="UpdateSpotHalfWidth"/>
    public const int UpdateSpotHalfHeight = 40;

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

    /// <summary>The presets for an aircraft on the ground (the tablet's left column); the others are for flight.</summary>
    private static readonly HashSet<string> GroundPresets = new(StringComparer.Ordinal)
    {
        "Cold and dark", "Turnaround", "Before start", "Ready for taxi", "Ready for takeoff",
    };

    /// <summary>
    /// Whether <paramref name="name"/> is a ground preset (Cold and dark to Ready for takeoff) rather
    /// than a flight one (After takeoff, Cruise, Approach, Final approach). Throws for a name that is
    /// no preset.
    /// </summary>
    public static bool IsGroundPreset(string name)
    {
        Lookup(Presets, name, nameof(name));
        return GroundPresets.Contains(name);
    }

    /// <summary>
    /// Null when <paramref name="name"/> may load with the aircraft <paramref name="onGround"/> or
    /// not: a ground preset only on the ground, a flight preset only airborne (loading Cold and dark
    /// in flight, or Cruise at the gate, would set the panel for the wrong phase). Otherwise what is
    /// said instead: "That preset is for the ground." or "That preset is for flight."
    /// </summary>
    public static string? PresetRefusal(string name, bool onGround)
    {
        bool ground = IsGroundPreset(name);
        if (ground == onGround) return null;
        return ground ? "That preset is for the ground." : "That preset is for flight.";
    }

    // ---- weight units ----------------------------------------------------------

    /// <summary>"pounds" or "kilograms": how the window names a unit system.</summary>
    public static string UnitsName(Bbs757WeightUnits units) =>
        units == Bbs757WeightUnits.Pounds ? "pounds" : "kilograms";

    /// <summary>
    /// What the window says after Accept units or Keep units, from BlueBird's unit system read
    /// before and after the click: "Weight units now pounds", "Weight units unchanged, pounds",
    /// "Weight units are pounds" (nothing read before), or "The weight units could not be read".
    /// </summary>
    public static string UnitsAnswerPhrase(Bbs757WeightUnits? before, Bbs757WeightUnits? after)
    {
        if (after is not Bbs757WeightUnits now) return "The weight units could not be read";
        if (before is not Bbs757WeightUnits was) return $"Weight units are {UnitsName(now)}";
        return was == now ? $"Weight units unchanged, {UnitsName(now)}" : $"Weight units now {UnitsName(now)}";
    }

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

    /// <summary>
    /// The earliest page load that can have seen the cycle in force on <paramref name="todayUtc"/>:
    /// 00:00 UTC the day after it took effect (a margin for Navigraph's switchover hour and for
    /// clock skew).
    /// </summary>
    public static DateTime EarliestPageLoadUtc(DateOnly todayUtc) =>
        AiracCycle.EffectiveDate(AiracCycle.ForDate(todayUtc)).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    /// <summary>
    /// Whether the nav data update may click the UPDATE spot, which is BlueBird's REMOVE whenever
    /// BlueBird saw no newer cycle. BlueBird looks for one only when the flight loads, signed in,
    /// inside its WASM, where nothing outside can read the answer. So the click needs:
    /// <see cref="ShouldUpdate"/>; the pilot signed in, and not seen signed out during this load
    /// (a sign-in after the load needs a reload); the tablet's page loaded at or after
    /// <see cref="EarliestPageLoadUtc"/>; and that page at least <see cref="UpdateMinPageAgeMs"/>
    /// old. Pure; all times UTC.
    /// </summary>
    public static Bbs757UpdateGate MayClickUpdate(int? installed, DateTime nowUtc, DateTime pageLoadedUtc,
        bool signedIn, bool sawSignedOutThisLoad)
    {
        var today = DateOnly.FromDateTime(nowUtc);
        if (!ShouldUpdate(installed, today)) return Bbs757UpdateGate.Current;
        if (!signedIn) return Bbs757UpdateGate.NotSignedIn;
        var earliest = EarliestPageLoadUtc(today);
        if (nowUtc < earliest) return Bbs757UpdateGate.NewToday;
        if (sawSignedOutThisLoad || pageLoadedUtc < earliest) return Bbs757UpdateGate.ReloadFirst;
        if ((nowUtc - pageLoadedUtc).TotalMilliseconds < UpdateMinPageAgeMs) return Bbs757UpdateGate.StillChecking;
        return Bbs757UpdateGate.Allowed;
    }

    /// <summary>
    /// Whether a signed-out Navigraph reading belongs to the page that loaded at
    /// <paramref name="pageLoadedUtc"/>: taken at or after its load. A reading from an earlier
    /// flight does not count; that load's check is not this one's.
    /// </summary>
    public static bool SignedOutDuringLoad(DateTime? lastSignedOutUtc, DateTime pageLoadedUtc) =>
        lastSignedOutUtc is { } t && t >= pageLoadedUtc;

    /// <summary>
    /// The guards the update sequence runs under, so the agent checks them again on the page it
    /// clicks: loaded at or after <see cref="EarliestPageLoadUtc"/>, at least a minute old, no
    /// earlier update on that page, and the very page the window judged
    /// (<paramref name="judgedPageMs"/>, its load time as <see cref="CoherentBbs757EfbClient.ReadPageAsync"/>
    /// read it): a flight reloaded between the judgment and the click is another page.
    /// </summary>
    public static Bbs757EfbRunGuard UpdateGuard(DateOnly todayUtc, long judgedPageMs) => new(
        NotBeforeLoadMs: new DateTimeOffset(EarliestPageLoadUtc(todayUtc)).ToUnixTimeMilliseconds(),
        MinPageAgeMs: UpdateMinPageAgeMs,
        OnceKey: UpdateOnceKey,
        SamePageAsMs: judgedPageMs);

    /// <summary>
    /// Whether "switch weight units?" can be on screen to answer: only right after a fetch that
    /// finished, with no other run since.
    /// </summary>
    public static bool MayAnswerUnits(Bbs757EfbRunKind? lastRun, bool lastRunDone) =>
        lastRun == Bbs757EfbRunKind.FetchOfp && lastRunDone;

    /// <summary>
    /// A SimBrief ID entry judged from BlueBird's stored ID before and after it: stored as typed;
    /// unchanged (nothing taken); or changed to something else (an appended entry, a cleared one).
    /// </summary>
    public static Bbs757SimBriefEntry JudgeSimBriefEntry(long? before, long? after, long typed)
    {
        if (after == typed) return Bbs757SimBriefEntry.Saved;
        if (after == before) return Bbs757SimBriefEntry.NotSaved;
        return Bbs757SimBriefEntry.Different;
    }

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
    /// could otherwise land it on another page). Run it only when <see cref="MayClickUpdate"/> allows
    /// it, and only under <see cref="UpdateGuard"/>: the agent refuses it without those guards.
    /// </summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> UpdateNavData()
    {
        var seq = OpenAiracPage().ToList();
        seq[^1] = (seq[^1].Point, seq[^1].DelayAfterMs + AiracPageSettleMs);
        seq.Add((UpdateButton, 0));
        return seq;
    }

    /// <summary>
    /// The AIRAC page, the SimBrief ID field, <see cref="SimBriefBackspaces"/> backspaces (the
    /// keypad may open on the stored ID; whether it does is unmeasured), each digit, then OK: every
    /// keypad key 500 ms after the one before. Never the keypad's DEL, whose effect is unmeasured.
    /// </summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> EnterSimBriefId(string digits)
    {
        if (!IsSimBriefId(digits)) throw new ArgumentException("A SimBrief ID is digits only.", nameof(digits));
        var seq = OpenAiracPage().ToList();
        seq.Add((SimBriefField, KeypadOpenDelayMs));
        for (int i = 0; i < SimBriefBackspaces; i++) seq.Add((KeyBackspace, DigitDelayMs));
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

    // ---- nosewheel steering ----------------------------------------------------------

    public const string SteeringAlreadyRudderText = "Steering is already rudder";

    /// <summary>
    /// Home, Settings, AIRCRAFT, then the NOSEWHEEL STEERING switch once. Run it only when
    /// <see cref="MayClickSteering"/> allows it: the switch toggles.
    /// </summary>
    public static IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> SetSteeringToRudder() => new[]
    {
        (Home, NavigateDelayMs),
        (SettingsIcon, NavigateDelayMs),
        (SettingsAircraft, NavigateDelayMs),
        (NosewheelSteeringSwitch, NavigateDelayMs),
    };

    /// <summary>
    /// Whether the steering switch may be clicked: only while BlueBird's <c>tiller_realistic</c> reads
    /// true. Already false (rudder), a click would switch it BACK to the tiller; unreadable, nothing
    /// says which way one click would go.
    /// </summary>
    public static bool MayClickSteering(bool? tillerRealistic) => tillerRealistic == true;

    /// <summary>
    /// What the steering click came to, from <c>tiller_realistic</c> read after it: false "Steering set
    /// to rudder", still true "BlueBird did not change the steering. It is still the tiller.",
    /// unreadable "The steering setting could not be read back.".
    /// </summary>
    public static string SteeringResultPhrase(bool? tillerRealisticAfter) => tillerRealisticAfter switch
    {
        false => "Steering set to rudder",
        true => "BlueBird did not change the steering. It is still the tiller.",
        null => "The steering setting could not be read back.",
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
