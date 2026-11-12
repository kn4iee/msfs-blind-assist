namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The map guides' rows (plan 28), from <see cref="Bbs757EhsiReading.MapGuides"/>: the FIX pages' fixes, circles and
/// radials, the off-path descent circles and the altitude range arc among the map's items, each only where some of it is
/// inside the screen's clip; the VNAV path deviation at the right side. Nothing here is spoken.
/// </summary>
public static partial class Bbs757EhsiText
{
    /// <summary>A circle's samples are at most this many pixels apart around it on the screen (great-circle points,
    /// projected), and at least <see cref="MinCircleSamples"/> of them, at most <see cref="MaxCircleSamples"/>.</summary>
    private const double CircleSamplePixels = 20;

    /// <summary>A circle's fewest samples: every 5 degrees, for a circle small on the screen.</summary>
    private const int MinCircleSamples = 72;

    /// <summary>A circle's most samples, whatever its size: a 300 NM circle at range 10 then has about 54 px between them.</summary>
    private const int MaxCircleSamples = 2048;

    /// <summary>A radial's dashes are sampled at least every this many pixels along each dash.</summary>
    private const double RadialSamplePixels = 5;

    /// <summary>The most dashes of a radial looked at (the layout's count is 18): a wild layout value cannot spin.</summary>
    private const int MaxRadialDashes = 256;

    /// <summary>
    /// The map guides among the map's items, in MAP and PLAN:
    /// <list type="bullet">
    /// <item>Each fix: "Map: fix TCM, 19.1 NM, 160° left" while its point is inside the clip; for each entry, "Map: fix
    /// TCM, circle 15 NM" while a sample of that circle is, and "Map: fix TCM, radial 300" while a sample along its dashes
    /// is (MAP: from the fix's point, the radial minus the heading number off the screen's up). In PLAN BlueBird turns a
    /// radial by the magnetic variation at the fix (f69687), which the reader does not have: there a radial is listed
    /// whenever the fix's point or one of its circles is.</item>
    /// <item>"Map: off-path descent circles, 45 NM and 38 NM around 30.2 NM, 15° left" (clean, then speedbrake, whole NM;
    /// the place of their center) while a sample of either is inside the clip.</item>
    /// <item>MAP while the IRS is valid: "Map: altitude range arc, 12.3 NM, ahead" (BlueBird's distance, one decimal, and the
    /// direction of its point) while that distance is at most the range plus the layout's margin and the point is inside
    /// the clip.</item>
    /// </list>
    /// A row that is not on its own point is ordered by its topmost sample on the screen.
    /// </summary>
    private static void AddMapGuides(List<MapItem> items, Bbs757EhsiReading r, string? center)
    {
        if (r.MapGuides is not { } g) return;
        bool plan = Is(r, Bbs757EhsiMode.Plan);
        foreach (var fix in g.Fixes)
        {
            if (fix.Position is not { } p) continue;   // listed without a place: UnplacedGuideRows
            string prefix = FixPrefix(fix);
            var point = Locate(r, p.Latitude, p.Longitude, center);
            (double X, double Y)? shown = point.OnScreen ? (point.X, point.Y) : null;
            if (point.OnScreen) items.Add(new MapItem(point.Y, point.X, prefix + point.Place));
            foreach (var entry in fix.Entries)
                if (entry.Distance is { } nm && CircleOnScreen(r, p, nm) is { } at)
                {
                    items.Add(new MapItem(at.Y, at.X, prefix + "circle " + nm.ToString(Inv) + " NM"));
                    shown ??= at;
                }
            foreach (var entry in fix.Entries)
            {
                if (entry.Radial is not { } radial) continue;
                var at = plan ? shown : RadialOnScreen(r, point.X, point.Y, radial - r.Number, g.Dashes);
                if (at is { } a) items.Add(new MapItem(a.Y, a.X, prefix + "radial " + radial.ToString("D3", Inv)));
            }
        }

        if (g.Offpath is { } o)
        {
            var clean = CircleOnScreen(r, o.Center, o.CleanRadius);
            var speedbrake = CircleOnScreen(r, o.Center, o.SpeedbrakeRadius);
            if (clean is not null || speedbrake is not null)
            {
                var point = Locate(r, o.Center.Latitude, o.Center.Longitude, center);
                var at = point.OnScreen ? (point.X, point.Y) : Topmost(clean, speedbrake);
                items.Add(new MapItem(at.Y, at.X, "Map: off-path descent circles, " + WholeNm(o.CleanRadius) + " NM and "
                    + WholeNm(o.SpeedbrakeRadius) + " NM around " + point.Place));
            }
        }

        // Within the drawn range plus the layout's margin only: in near-level flight BlueBird's distance runs to tens of
        // thousands of NM and the point wraps the globe.
        if (g.Arc is { } arc && Is(r, Bbs757EhsiMode.Map) && r.Valid && arc.Distance <= r.Range + r.Constants.MarkerMargin)
        {
            var c = r.Constants;
            var (x, y, _, bearing) = Bbs757EhsiGeometry.Project(r.Map, r.Range, c.RadiusFactor, arc.Point.Latitude, arc.Point.Longitude);
            if (Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset))
                items.Add(new MapItem(y, x, "Map: altitude range arc, "
                    + Bbs757EhsiGeometry.Place(arc.Distance, Bbs757EhsiGeometry.OffTop(bearing, r.Map.Rotation))));
        }
    }

    /// <summary>The fixes the reader cannot place (a navaid fix while the layout lacks the leg position rule): "Map: fix
    /// TCM, position not read", after the map's placed items.</summary>
    private static IEnumerable<string> UnplacedGuideRows(Bbs757EhsiReading r) =>
        r.MapGuides is { } g
            ? g.Fixes.Where(f => f.Position is null).Select(f => FixPrefix(f) + "position not read")
            : Array.Empty<string>();

    /// <summary>The VNAV path deviation scale, in MAP: "Right side: VNAV path deviation 120 ft" (the stored value rounded
    /// to whole feet, signed as stored: which sign is above the path is not measured), ", at full scale" at or beyond the
    /// layout's clamp.</summary>
    private static void VnavDeviationRow(List<string> rows, Bbs757EhsiReading r)
    {
        if (r.MapGuides is not { PathDeviation: { } feet } g || !Is(r, Bbs757EhsiMode.Map)) return;
        long whole = (long)Math.Round(feet, MidpointRounding.AwayFromZero);
        rows.Add("Right side: VNAV path deviation " + whole.ToString(Inv) + " ft"
            + (Math.Abs(feet) >= g.DeviationClamp ? ", at full scale" : ""));
    }

    private static string FixPrefix(Bbs757EhsiFix fix) => "Map: fix" + (fix.Name.Length > 0 ? " " + fix.Name : "") + ", ";

    private static string WholeNm(double nm) => Math.Round(nm, MidpointRounding.AwayFromZero).ToString("F0", Inv);

    private static (double X, double Y) Topmost((double X, double Y)? a, (double X, double Y)? b) =>
        a is not { } p ? b!.Value : b is not { } q ? p : (q.Y < p.Y || (q.Y == p.Y && q.X < p.X) ? q : p);

    /// <summary>The topmost of a circle's samples inside the clip (<paramref name="nm"/> from its center along the great
    /// circle, projected), null when none is. The samples are spaced by the circle's size on the screen, at most
    /// <see cref="CircleSamplePixels"/> apart (within <see cref="MinCircleSamples"/> to <see cref="MaxCircleSamples"/>):
    /// fixed 5-degree steps put a 300 NM circle's samples 26 NM apart, wider than the screen at range 10 or 20.</summary>
    private static (double X, double Y)? CircleOnScreen(Bbs757EhsiReading r, Bbs757EhsiMapPoint center, double nm)
    {
        var c = r.Constants;
        double pixels = 2 * Math.PI * nm / r.Range * (r.Map.Height * c.RadiusFactor);
        int samples = double.IsFinite(pixels)
            ? (int)Math.Clamp(Math.Ceiling(pixels / CircleSamplePixels), MinCircleSamples, MaxCircleSamples)
            : MinCircleSamples;
        (double X, double Y)? top = null;
        for (int i = 0; i < samples; i++)
        {
            double bearing = 360.0 * i / samples;
            var (lat, lon) = Bbs757EhsiGeometry.Destination(center.Latitude, center.Longitude, bearing, nm);
            var (x, y, _, _) = Bbs757EhsiGeometry.Project(r.Map, r.Range, c.RadiusFactor, lat, lon);
            if (!Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset)) continue;
            top = Topmost(top, (x, y));
        }
        return top;
    }

    /// <summary>The first sample along a radial's dashes inside the clip: from the fix's pixel, <paramref name="angle"/>
    /// degrees clockwise from the screen's up, each dash from its start for its length, sampled at least every
    /// <see cref="RadialSamplePixels"/> and at both ends. Null when none is, and for a dash length or step that is not a
    /// finite positive number; at most <see cref="MaxRadialDashes"/> dashes are looked at.</summary>
    private static (double X, double Y)? RadialOnScreen(Bbs757EhsiReading r, double fixX, double fixY, double angle,
        Bbs757EhsiRadialDashes dashes)
    {
        if (!double.IsFinite(fixX) || !double.IsFinite(fixY) || !double.IsFinite(angle) || !double.IsFinite(dashes.Start)
            || !double.IsFinite(dashes.Length) || !double.IsFinite(dashes.Step) || !(dashes.Length > 0) || !(dashes.Step > 0))
            return null;
        var c = r.Constants;
        double radians = angle * Math.PI / 180.0, sin = Math.Sin(radians), cos = Math.Cos(radians);
        double length = dashes.Length;
        int count = Math.Min(dashes.Count, MaxRadialDashes);
        for (int i = 0; i < count; i++)
        {
            double start = dashes.Start + i * dashes.Step;
            for (double along = 0; ; along += RadialSamplePixels)
            {
                double d = start + Math.Min(along, length);
                double x = fixX + d * sin, y = fixY - d * cos;
                if (Bbs757EhsiGeometry.OnScreen(r.Map, r.Centered, x, y, c.RadiusFactor, c.AircraftYFactor, c.ClipInset)) return (x, y);
                if (along >= length) break;
            }
        }
        return null;
    }
}
