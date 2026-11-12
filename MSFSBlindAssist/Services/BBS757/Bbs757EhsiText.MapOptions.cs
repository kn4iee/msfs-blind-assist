namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map options' rows (plan 28), from <see cref="Bbs757EhsiReading.MapOptions"/>, as BlueBird draws them: the tuned
/// stations and the items its loop draws, each inside the screen's clip among the map's items by its place; and the VOR
/// course lines after them. None of it is spoken; EXCESS DATA stays plan 26's message.
/// </summary>
public static partial class Bbs757EhsiText
{
    // The app's names for what the screen draws as symbols with a label (BlueBird prints no kind word).
    private const string AirportWord = "airport", WaypointWord = "waypoint", NdbWord = "NDB", NavaidWord = "navaid",
        TunedWord = "tuned";

    // The app's names for the course lines' receivers: the screen prints only the course by the line.
    private const string VorLeftName = "VOR L", VorRightName = "VOR R";

    /// <summary>The tuned stations ("Map: VORTAC SEA, tuned, 0.5 NM, 173° left") and the loop's items ("Map: VOR/DME PAE,
    /// …", "Map: airport KBFI, …", "Map: waypoint HAVHO, …", "Map: NDB FHR, …"), each only inside the screen's clip: one the
    /// loop counted but the clip cuts is not listed.</summary>
    private static void AddMapOptions(List<MapItem> items, Bbs757EhsiReading r, string? center)
    {
        if (r.MapOptions is not { } o) return;
        foreach (var (item, tuned) in o.Tuned.Select(t => (t, true)).Concat(o.Drawn.Select(d => (d, false))))
        {
            if (Where(r, item.Latitude, item.Longitude, center) is not { } at) continue;
            items.Add(new MapItem(at.Y, at.X, "Map: " + OptionName(item, o.Kinds) + (tuned ? ", " + TunedWord : "") + ", " + at.Place));
        }
    }

    /// <summary>An item's name: its kind's word, then its label when it has one (an unnamed point is not given a name).</summary>
    private static string OptionName(Bbs757EhsiMapOption item, Bbs757EhsiNavaidKinds kinds)
    {
        string word = item.Kind switch
        {
            Bbs757EhsiMapItemKind.Navaid => NavaidKindWord(item.NavaidKind, kinds),
            Bbs757EhsiMapItemKind.Airport => AirportWord,
            Bbs757EhsiMapItemKind.Waypoint => WaypointWord,
            _ => NdbWord,
        };
        return item.Name.Length > 0 ? word + " " + item.Name : word;
    }

    private static string NavaidKindWord(int kind, Bbs757EhsiNavaidKinds k) =>
        kind == k.Vor ? "VOR"
        : kind == k.VorDme ? "VOR/DME"
        : kind == k.Vortac ? "VORTAC"
        : kind == k.Tacan ? "TACAN"
        : kind == k.Dme ? "DME"
        : NavaidWord;

    /// <summary>
    /// The VOR course lines, "Map: VOR L course 187 through SEA" (the course as its label prints it), after the map's
    /// items. A line runs both ways through its station across the whole screen, and the reader has no magnetic variation
    /// to project it, so it is listed whenever BlueBird draws it (the reading holds only those), with no on-screen test.
    /// </summary>
    private static IEnumerable<string> CourseLineRows(Bbs757EhsiReading r) =>
        (r.MapOptions?.CourseLines ?? Array.Empty<Bbs757EhsiCourseLine>()).Select(line =>
            "Map: " + (line.Left ? VorLeftName : VorRightName) + " course " + ThreeDigits(line.Course) + " through " + line.Station);
}
