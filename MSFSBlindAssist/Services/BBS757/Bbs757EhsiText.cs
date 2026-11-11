using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What the 757 EHSI window lists, what the reader says by itself and the Ctrl+W line, from one reading (plan 26). Only
/// what the screen draws (docs/b757/evidence/displays-ehsi.md, Steps 2 and 3): a map item only inside BlueBird's own clip
/// (<see cref="Bbs757EhsiGeometry.OnScreen"/>), rows top to bottom, BlueBird's printed words and formats ("NM", the
/// top line's "z", the DATA ETA's "Z", "HDG", "TRU", "EXCESS DATA"), every number culture-invariant. Pure, and never throws
/// for any reading a sampler returns, a corrupt one included. The map's constants come with the reading
/// (<see cref="Bbs757EhsiReading.Constants"/>); the words are BlueBird's, as the evidence records them.
/// </summary>
public static class Bbs757EhsiText
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The modes as the layout's word table names them.
    private const string MapMode = "MAP", PlanMode = "PLAN", VorMode = "VOR", IlsMode = "ILS";

    // BlueBird's words on the screen.
    private const string HdgWord = "HDG", TrkWord = "TRK", MagWord = "M", TrueWord = "TRU", MapFlagWord = "MAP", NmWord = "NM",
        EtaSuffix = "z", DataEtaSuffix = "Z", ExcessWord = "EXCESS DATA", NoNumber = "---";

    private const string Unavailable = "EHSI waypoint unavailable: ";
    private const string NoReadingYet = "no reading yet";
    private const string DisplayDark = "display dark";
    private const string NoActiveWaypoint = "No active waypoint";
    private const string DiscontinuityName = "discontinuity";
    private const string UnnamedWaypoint = "unnamed waypoint";

    /// <summary>Half a degree: within it of straight ahead (or behind) the bug reads "ahead" (or "behind"), as a place does.</summary>
    private const double AlignedDegrees = 0.5;

    /// <summary>Under this distance from the map's center a PLAN leg names the center (the screen's one decimal rounds it
    /// to 0.0).</summary>
    private const double CenterNm = 0.05;

    // ---- the rows ----

    /// <summary>
    /// The window's rows, top to bottom, each only when the screen draws it:
    /// <list type="number">
    /// <item>Not powered: "EHSI: dark", alone.</item>
    /// <item>"EHSI: MAP, range 160" (", centered" when centered).</item>
    /// <item>Valid: "Top: HDG 119 M" (TRK with track up, TRU for a true reference). Not valid: "Top: ---" while aligning,
    /// then "Flag: HDG" (or TRK), and in MAP "Flag: MAP". While the range changes, nothing more.</item>
    /// <item>VOR and ILS: "VOR mode: not read yet" (plan 28's), and nothing more. MAP and PLAN: "Top left: 37.5 NM" (or
    /// "no distance") and "Top right: 1533.3z" (or "no ETA").</item>
    /// <item>Map rows, in a full reading, in MAP while valid or in PLAN, by their place on the screen (top to bottom, left to
    /// right): each leg with a symbol ("Map: KOLM, active, 37.5 NM, 87° right"; an inactive route's "Map, inactive route: ",
    /// a pending modification's "Map, modification: "; with DATA on its constraint and ETA as drawn), and the points
    /// within range + the margin ("Map: T/C, …", "Map: KSEA airport, …"). In PLAN the place is from the leg at the map's
    /// center ("13.4 NM north-east of KOLM"), else "the center". A discontinuity draws nothing.</item>
    /// <item>Valid: "Heading bug: 147, 28° right".</item>
    /// <item>"Message: EXCESS DATA" at the layout's count, else "Messages: none".</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<string> Rows(Bbs757EhsiReading r)
    {
        if (!r.Powered) return new[] { "EHSI: dark" };
        var rows = new List<string>
        {
            "EHSI: " + ModeWord(r) + ", range " + Number(r.Range) + (r.Centered ? ", centered" : ""),
        };
        if (r.Valid)
            rows.Add("Top: " + HeadingWord(r) + " " + ThreeDigits(r.Number) + " " + (r.TrueReference ? TrueWord : MagWord));
        else
        {
            if (r.Aligning) rows.Add("Top: " + NoNumber);
            rows.Add("Flag: " + HeadingWord(r));
            if (r.Mode == MapMode) rows.Add("Flag: " + MapFlagWord);
        }
        if (r.RangeChanging) return rows;
        if (r.Mode is VorMode or IlsMode)
        {
            rows.Add(r.Mode + " mode: not read yet");
            return rows;
        }

        double? distance = Distance(r);
        rows.Add("Top left: " + (distance is { } nm ? Bbs757EhsiGeometry.TopLineText(nm) + " " + NmWord : "no distance"));
        rows.Add("Top right: " + (Eta(r, distance) is { } eta ? eta + EtaSuffix : "no ETA"));
        rows.AddRange(MapRows(r));
        if (r.Valid) rows.Add("Heading bug: " + ThreeDigits(r.HeadingBug) + ", " + Relative(r.HeadingBug, r.Number));
        rows.Add(Excess(r) ? "Message: " + ExcessWord : "Messages: none");
        return rows;
    }

    private static string ModeWord(Bbs757EhsiReading r) => r.Mode ?? Bbs757WordTable.Unrecognised;

    private static string HeadingWord(Bbs757EhsiReading r) => r.TrackUp ? TrkWord : HdgWord;

    /// <summary>BlueBird's <c>round % 360</c> in three digits ("119"; 360 → "000"); not a number: "---".</summary>
    private static string ThreeDigits(double value)
    {
        if (!double.IsFinite(value)) return NoNumber;
        double whole = Math.Round(value, MidpointRounding.AwayFromZero) % 360.0;
        if (whole < 0) whole += 360.0;
        return ((int)whole).ToString("D3", Inv);
    }

    private static string Number(double value) => value.ToString("0.##", Inv);

    /// <summary>The top line's distance to the active leg, or null where it draws dashes (no leg, or a VM or FM leg). A
    /// discontinuity is at 0° N 0° E and the screen prints the distance there: so does this.</summary>
    private static double? Distance(Bbs757EhsiReading r)
    {
        if (r.Active is not { } a || a.Manual) return null;
        double nm = Bbs757EhsiGeometry.TopLineDistance(r.Latitude, r.Longitude, a.Latitude, a.Longitude);
        return double.IsFinite(nm) ? nm : null;
    }

    private static string? Eta(Bbs757EhsiReading r, double? distance) =>
        distance is { } nm ? Bbs757EhsiGeometry.Eta(r.Clock, nm, r.GroundSpeed, r.Constants.MinEtaGroundSpeed) : null;

    /// <summary>"28° right" from <paramref name="from"/> to <paramref name="to"/> folded to ±180, "ahead" or "behind" within
    /// half a degree; "unknown" for a value that is not a number.</summary>
    private static string Relative(double to, double from)
    {
        if (!double.IsFinite(to) || !double.IsFinite(from)) return "unknown";
        double off = Bbs757EhsiGeometry.OffTop(to, from);
        double abs = Math.Abs(off);
        if (abs <= AlignedDegrees) return "ahead";
        if (abs >= 180.0 - AlignedDegrees) return "behind";
        return Math.Round(abs, MidpointRounding.AwayFromZero).ToString("F0", Inv) + "° " + (off < 0 ? "left" : "right");
    }

    private static bool MapOrPlan(string? mode) => mode is MapMode or PlanMode;

    /// <summary>EXCESS DATA: drawn in MAP and PLAN when the frame's count of background items reaches the layout's limit.</summary>
    private static bool Excess(Bbs757EhsiReading r) =>
        r.Powered && MapOrPlan(r.Mode) && r.ExcessCount >= r.Constants.ExcessLimit;

    // ---- the map ----

    private readonly record struct MapItem(double Y, double X, string Text);

    private static IEnumerable<string> MapRows(Bbs757EhsiReading r)
    {
        bool plan = r.Mode == PlanMode;
        if (!((r.Mode == MapMode && r.Valid) || plan) || r.Route is null) return Array.Empty<string>();

        var items = new List<MapItem>();
        bool pending = r.Modification is not null;
        string? center = plan ? CenterName(r) : null;

        // The route's legs: "active" marks only the leg the top line uses, and only while the route is active and no
        // modification is pending (the modification's own rows show the leg the top line then uses).
        bool activeMarked = false;
        foreach (var leg in r.Route)
        {
            bool active = r.RouteActive && !pending && !activeMarked && r.Active is { } a && a == leg;
            activeMarked |= active;
            AddLeg(items, r, leg, r.RouteActive ? "Map: " : "Map, inactive route: ", active, center);
        }
        foreach (var leg in r.Modification ?? Array.Empty<Bbs757EhsiLeg>())
            AddLeg(items, r, leg, "Map, modification: ", false, center);

        foreach (var point in r.Points ?? Array.Empty<Bbs757EhsiPoint>())
        {
            if (Where(r, point.Latitude, point.Longitude, center) is not { } at) continue;
            if (!(at.Nm <= r.Range + r.Constants.MarkerMargin)) continue;
            string name = point.Airport ? point.Word + " airport" : point.Word;
            items.Add(new MapItem(at.Y, at.X, "Map: " + name + ", " + at.Place));
        }

        // Top to bottom, then left to right; OrderBy is stable, so one spot keeps the draw's order.
        return items.OrderBy(i => i.Y).ThenBy(i => i.X).Select(i => i.Text).ToArray();
    }

    private static void AddLeg(List<MapItem> items, Bbs757EhsiReading r, Bbs757EhsiLeg leg, string prefix, bool active, string? center)
    {
        if (!leg.Symbol || leg.Discontinuity) return;
        if (Where(r, leg.Latitude, leg.Longitude, center) is not { } at) return;
        string text = prefix + LegName(leg) + (active ? ", active" : "") + ", " + at.Place;
        if (r.DataSwitch)
        {
            if (Constraint(leg) is { } constraint) text += ", " + constraint;
            if (DataEta(leg.Eta) is { } eta) text += ", " + eta;
        }
        items.Add(new MapItem(at.Y, at.X, text));
    }

    private static string LegName(Bbs757EhsiLeg leg) => leg.Label.Length > 0 ? leg.Label : UnnamedWaypoint;

    /// <summary>Where an item is: its pixel, its distance from the map's center and its place in words; null when the clip
    /// cuts it.</summary>
    private static (double X, double Y, double Nm, string Place)? Where(Bbs757EhsiReading r, double lat, double lon, string? center)
    {
        var c = r.Constants;
        var (x, y, nm, bearing) = Bbs757EhsiGeometry.Project(r.Map, r.Range, c.RadiusFactor, lat, lon);
        if (!Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset)) return null;
        string place = center is not null
            ? Bbs757EhsiGeometry.PlanPlace(nm, bearing, center)
            : Bbs757EhsiGeometry.Place(nm, Bbs757EhsiGeometry.OffTop(bearing, r.Map.Rotation));
        return (x, y, nm, place);
    }

    /// <summary>PLAN's center by name: the first leg (the route's, then the modification's) at the map's center, else
    /// "the center".</summary>
    private static string CenterName(Bbs757EhsiReading r)
    {
        foreach (var leg in (r.Route ?? Array.Empty<Bbs757EhsiLeg>()).Concat(r.Modification ?? Array.Empty<Bbs757EhsiLeg>()))
        {
            if (leg.Discontinuity || leg.Label.Length == 0) continue;
            var (_, _, nm, _) = Bbs757EhsiGeometry.Project(r.Map, 1, 1, leg.Latitude, leg.Longitude);
            if (nm < CenterNm) return leg.Label;
        }
        return "the center";
    }

    /// <summary>A leg's constraint as the DATA switch draws it: kind 1 "2300", 2 "2300A", 3 "2300B", 4 "<second>A<first>B",
    /// 5 "<second>A"; none for any other kind.</summary>
    private static string? Constraint(Bbs757EhsiLeg leg)
    {
        string first = leg.ConstraintFirst.ToString(Inv), second = leg.ConstraintSecond.ToString(Inv);
        return leg.ConstraintKind switch
        {
            1 => first,
            2 => first + "A",
            3 => first + "B",
            4 => second + "A" + first + "B",
            5 => second + "A",
            _ => null,
        };
    }

    /// <summary>A leg's ETA as the DATA switch draws it: HHMM truncated, four digits, "Z" ("1518Z"); none below 0.</summary>
    private static string? DataEta(double eta) =>
        double.IsFinite(eta) && eta >= 0 && eta < int.MaxValue ? ((int)Math.Truncate(eta)).ToString("D4", Inv) + DataEtaSuffix : null;

    // ---- what is spoken ----

    /// <summary>One flag or message on the screen: <see cref="Key"/> tells it apart (the heading flag is one flag whether
    /// it says HDG or TRK), <see cref="Word"/> is what the screen prints.</summary>
    private readonly record struct Item(string Key, string Word);

    private const string HeadingFlag = "heading", MapFlag = "map", ExcessMessage = "excess";

    /// <summary>The flags drawn, only while powered: the heading (or track) flag while not valid, and in MAP the MAP flag.</summary>
    private static List<Item> Flags(Bbs757EhsiReading r)
    {
        var flags = new List<Item>(2);
        if (!r.Powered || r.Valid) return flags;
        flags.Add(new Item(HeadingFlag, HeadingWord(r)));
        if (r.Mode == MapMode) flags.Add(new Item(MapFlag, MapFlagWord));
        return flags;
    }

    private static List<Item> Messages(Bbs757EhsiReading r) =>
        Excess(r) ? new List<Item> { new(ExcessMessage, ExcessWord) } : new List<Item>();

    /// <summary>
    /// What changed from <paramref name="heard"/> to <paramref name="now"/>, all on the one Ctrl+M row
    /// (<see cref="Bbs757EhsiDisplay.MuteKey"/>), in this order; the words are the same with or without
    /// <paramref name="withPrefix"/> ("EHSI" names the display):
    /// <list type="number">
    /// <item>"EHSI message: EXCESS DATA", "EHSI message gone: EXCESS DATA".</item>
    /// <item>"EHSI flag: HDG, MAP", "EHSI flag gone: HDG, MAP" (each change of one settle joined).</item>
    /// <item>The active waypoint's label changing, while both are powered: "EHSI: KOLM, 37 NM" (whole NM, truncated;
    /// without the distance where the top line draws dashes, and in VOR and ILS, where it shows DME), or
    /// "EHSI: no active waypoint".</item>
    /// </list>
    /// A flag or message that comes or goes only because the mode changed (the MAP flag, EXCESS DATA, which some modes do
    /// not draw) is not spoken: the pilot turned the selector, and the window shows it. Nothing else is ever spoken: no
    /// mode, range, CTR, DATA, heading, bug, route edit or map item. Going dark or lit is the monitor's.
    /// </summary>
    public static IReadOnlyList<Bbs757SpokenLine> Changes(Bbs757EhsiReading heard, Bbs757EhsiReading now, bool withPrefix)
    {
        var lines = new List<Bbs757SpokenLine>();
        bool sameMode = string.Equals(heard.Mode, now.Mode, StringComparison.Ordinal);
        bool Comparable(Item item) => item.Key switch
        {
            MapFlag => sameMode,
            ExcessMessage => sameMode || (MapOrPlan(heard.Mode) && MapOrPlan(now.Mode)),
            _ => true,
        };
        Diff(lines, Messages(heard), Messages(now), "EHSI message: ", "EHSI message gone: ", Comparable);
        Diff(lines, Flags(heard), Flags(now), "EHSI flag: ", "EHSI flag gone: ", Comparable);

        if (heard.Powered && now.Powered
            && !string.Equals(heard.Active?.Label, now.Active?.Label, StringComparison.Ordinal))
            lines.Add(Spoken("EHSI: " + WaypointChange(now)));
        return lines;
    }

    private static void Diff(List<Bbs757SpokenLine> lines, List<Item> before, List<Item> after, string newLine, string goneLine,
        Func<Item, bool> comparable)
    {
        var added = after.Where(i => comparable(i) && !before.Any(b => b.Key == i.Key)).Select(i => i.Word).ToList();
        var gone = before.Where(i => comparable(i) && !after.Any(a => a.Key == i.Key)).Select(i => i.Word).ToList();
        if (added.Count > 0) lines.Add(Spoken(newLine + string.Join(", ", added)));
        if (gone.Count > 0) lines.Add(Spoken(goneLine + string.Join(", ", gone)));
    }

    private static Bbs757SpokenLine Spoken(string text) => new(Bbs757EhsiDisplay.MuteKey, text);

    private static string WaypointChange(Bbs757EhsiReading r)
    {
        if (r.Active is not { } a) return "no active waypoint";
        string name = WaypointName(a);
        if (!MapOrPlan(r.Mode) || Distance(r) is not { } nm) return name;
        return name + ", " + Math.Truncate(nm).ToString("F0", Inv) + " " + NmWord;
    }

    /// <summary>The active leg's name as the pilot hears it: its label, "discontinuity" for a discontinuity or an empty
    /// label.</summary>
    private static string WaypointName(Bbs757EhsiLeg a) =>
        a.Discontinuity || a.Label.Length == 0 ? DiscontinuityName : a.Label;

    // ---- Ctrl+W ----

    /// <summary>
    /// The Ctrl+W line from a reading, the owner's wording: the active waypoint's name, then the top line's distance and
    /// ETA as drawn: "KOLM, 37.5 NM, 1533.3z"; "KOLM, 37.5 NM, no ETA"; a VM or FM leg "KOLM, no distance, no ETA"; no leg
    /// "No active waypoint". In VOR and ILS the top line shows DME (plan 28's): "KOLM, EHSI in VOR mode". Not powered:
    /// "EHSI waypoint unavailable: display dark"; a corrupt reading: "EHSI waypoint unavailable: no reading yet"
    /// (<see cref="Bbs757EhsiDisplay.WaypointLine"/> names the monitor's reason instead).
    /// </summary>
    public static string WaypointLine(Bbs757EhsiReading r)
    {
        if (r.Corrupt) return Unavailable + NoReadingYet;
        if (!r.Powered) return Unavailable + DisplayDark;
        if (r.Active is not { } a) return NoActiveWaypoint;
        string name = WaypointName(a);
        if (r.Mode is VorMode or IlsMode) return name + ", EHSI in " + r.Mode + " mode";
        if (Distance(r) is not { } nm) return name + ", no distance, no ETA";
        return name + ", " + Bbs757EhsiGeometry.TopLineText(nm) + " " + NmWord + ", "
            + (Eta(r, nm) is { } eta ? eta + EtaSuffix : "no ETA");
    }

    /// <summary>The Ctrl+W line while the monitor may have no reading: <paramref name="unavailableReason"/> (the monitor's
    /// own, "no reading yet" when null) when there is none or it is corrupt.</summary>
    internal static string WaypointLine(Bbs757EhsiReading? latest, string? unavailableReason) =>
        latest is null || latest.Corrupt ? Unavailable + (unavailableReason ?? NoReadingYet) : WaypointLine(latest);
}
