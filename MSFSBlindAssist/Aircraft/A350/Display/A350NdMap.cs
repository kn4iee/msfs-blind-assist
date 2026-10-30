namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The A350's airport map (ANF) as the ND draws it at a zoom range (pure; live at OMDB 2026-10-28). Its names come as
/// texts once the draw code's aircraft position is answered:
/// <list type="bullet">
/// <item>through the map's transform: taxiways grey and buildings cyan (local origin 0,0), runway idents white (local 0,5);</item>
/// <item>untransformed: the aircraft's position box yellow under the map, the airport's name top right, the zoom range
/// a cyan number left.</item>
/// </list>
/// Each name reads once, nearest the aircraft first. The compass labels (white, drawn at local -3,-517 through a
/// rotation) lie on a circle about the aircraft symbol, which is the reference; with no compass drawn, the ND's centre.
/// The compass labels are a scale, never read as text.
/// </summary>
public static class A350NdMap
{
    private const float NdCentreX = 418, NdCentreY = 512;

    /// <summary>PLAN's compass letters, at their own local places whichever way the function panel moves them: the map's
    /// centre is 11 px right of N and S, halfway between them (live 2026-10-30, panel closed 404,516.5, open 404,396.5).</summary>
    private static bool IsPlanLetter(DrawnText t) => t.ColourName == "white" && t.Text.Trim() is "N" or "E" or "S" or "W"
        && (AtLocal(t, 393, 300) || AtLocal(t, 393, 733) || AtLocal(t, 173, 517) || AtLocal(t, 615, 517));
    private const float PlanLetterToCentre = 11;

    /// <summary>Pixels per metre at each zoom step, the same in every mode, from a runway's idents against its length (VCBI
    /// 04/22, 3341 m, live 2026-10-30): the label says 0.2, 0.5, 1, 2, 5 in ARC and half that (0.1, 0.25, 0.5, 1, 2.5) in
    /// ROSE and PLAN, where its mark is half as far out.</summary>
    private static readonly IReadOnlyDictionary<string, float> PixelsPerMetre = new Dictionary<string, float>(StringComparer.Ordinal)
    {
        ["0.2"] = 1.3235f, ["0.5"] = 0.5402f, ["1"] = 0.2791f, ["2"] = 0.1485f, ["5"] = 0.0702f,
    };

    /// <summary>The ARC label of a ROSE or PLAN label (its mark is half as far out).</summary>
    private static readonly IReadOnlyDictionary<string, string> HalfMark = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["0.1"] = "0.2", ["0.25"] = "0.5", ["0.5"] = "1", ["1"] = "2", ["2.5"] = "5",
    };

    /// <summary>One name on the map, where it is drawn (its nearest to the reference, the ND's coordinates), and in words
    /// where it lies: from the aircraft on a heading-up map, from the map's centre on a north-up one.</summary>
    public sealed record NdMapItem(string Kind, string Name, string Colour, float X, float Y, string? Where);

    /// <summary>A stand's name: a letter or two and a number ("A6", "GA5"), where a building has words.</summary>
    private static readonly System.Text.RegularExpressions.Regex StandName = new(@"^[A-Z]{0,3}\d+[A-Z]?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>What the map is reading against: the reference point, whether it is north-up (PLAN), and its scale if read.</summary>
    public readonly record struct NdMapFrame(float X, float Y, bool NorthUp, float? PixelsPerMetre);

    private static bool IsMapName(DrawnText t) => !string.IsNullOrWhiteSpace(t.Text)
        && (AtLocal(t, 0, 0) || t.ColourName == "white" && AtLocal(t, 0, 5));

    /// <summary>A runway's name along its centre: "04", "-", "22", white, each through the map's transform at a small x of its
    /// own at local y 0 (8.5, -7.5, -39.5 at VCBI; live 2026-10-30).</summary>
    private static bool IsRunwayNamePart(DrawnText t) => t.ColourName == "white" && Math.Abs(t.LocalY) < 0.5f && Math.Abs(t.LocalX) is > 0.5f and < 200
        && Math.Abs(t.X - t.LocalX) > 0.5f && t.Text.Trim() is { Length: > 0 and <= 3 } part && (part == "-" || part.Any(char.IsDigit));

    /// <summary>The runway names drawn along their centres, in the draw's order: each ident, a dash, the other ident.</summary>
    private static IReadOnlyList<(string Name, DrawnText First, IReadOnlyList<DrawnText> Parts)> RunwayNames(IReadOnlyList<DrawnText> texts)
    {
        var o = new List<(string, DrawnText, IReadOnlyList<DrawnText>)>();
        var parts = texts.Where(IsRunwayNamePart).ToList();
        for (int i = 0; i + 2 < parts.Count; i++)
            if (parts[i + 1].Text.Trim() == "-" && parts[i].Text.Trim() != "-" && parts[i + 2].Text.Trim() != "-")
            {
                o.Add(($"{parts[i].Text.Trim()}-{parts[i + 2].Text.Trim()}", parts[i], new[] { parts[i], parts[i + 1], parts[i + 2] }));
                i += 2;
            }
        return o;
    }

    /// <summary>The frame of a map's texts: heading-up about the compass's centre (ARC, ROSE: its labels at local -3,-517 or
    /// -3,-272), north-up about the N, E, S and W letters' centre (PLAN); the scale from the range label beside it.</summary>
    public static NdMapFrame? FrameOf(IReadOnlyList<DrawnText> texts)
    {
        var compass = texts.Where(t => !string.IsNullOrWhiteSpace(t.Text) && t.ColourName == "white"
            && (AtLocal(t, -3, -517) || AtLocal(t, -3, -272))).ToList();
        float? scale = null;
        foreach (var t in texts.Where(t => t.ColourName == "cyan" && Untransformed(t) is var plain && Math.Abs(t.X - t.LocalX) < 0.5f && t.Y is >= 250 and < 760 && t.X < 300))
        {
            string label = t.Text.Trim();
            if (compass.Any(c => AtLocal(c, -3, -272)) || compass.Count == 0)
                label = HalfMark.TryGetValue(label, out var arc) ? arc : label;
            if (PixelsPerMetre.TryGetValue(label, out var s)) { scale = s; break; }
        }
        if (CircleCentre(compass) is { } centre) return new NdMapFrame(centre.X, centre.Y, false, scale);
        var north = texts.FirstOrDefault(t => IsPlanLetter(t) && t.Text.Trim() == "N");
        var south = texts.FirstOrDefault(t => IsPlanLetter(t) && t.Text.Trim() == "S");
        if (north != null && south != null) return new NdMapFrame(north.X + PlanLetterToCentre, (north.Y + south.Y) / 2, true, scale);
        return null;
    }

    /// <summary>A point's place in words: "2 o'clock, 96 m" from the aircraft, "north east, 200 m from the map's centre" on PLAN.</summary>
    internal static string? Place(NdMapFrame frame, float x, float y)
    {
        if (frame.PixelsPerMetre is not float scale) return null;
        double dx = x - frame.X, dy = y - frame.Y;
        double metres = Math.Sqrt(dx * dx + dy * dy) / scale;
        string far = metres >= 1000
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{metres / 1000:0.0} km")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Math.Round(metres / 5) * 5:0} m");
        double degrees = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        if (frame.NorthUp)
        {
            string[] points = { "north", "north east", "east", "south east", "south", "south west", "west", "north west" };
            return $"{points[(int)Math.Round(degrees / 45) % 8]}, {far} from the map's centre";
        }
        int hour = (int)Math.Round(degrees / 30) % 12;
        return $"{(hour == 0 ? 12 : hour)} o'clock, {far}";
    }

    /// <summary>Each name on the map once, its nearest drawing, nearest first, by kind. What is drawn off the ND still reads:
    /// the ND's far runway ident says where the runway is.</summary>
    public static IReadOnlyList<NdMapItem> Items(IReadOnlyList<DrawnText> texts)
    {
        var names = texts.Where(IsMapName).ToList();
        if (names.Count == 0) return Array.Empty<NdMapItem>();
        var frame = FrameOf(texts) ?? new NdMapFrame(NdCentreX, NdCentreY, false, null);
        static string KindOf(DrawnText t) => AtLocal(t, 0, 5) ? "Runway"
            : t.ColourName == "cyan" ? (StandName.IsMatch(t.Text.Trim()) ? "Stand" : "Building")
            : t.ColourName == "green" ? "Landmark" : "Taxiway";
        var items = names.GroupBy(t => (Kind: KindOf(t), Name: t.Text.Trim()))
            .Select(g =>
            {
                var at = g.OrderBy(t => (t.X - frame.X) * (t.X - frame.X) + (t.Y - frame.Y) * (t.Y - frame.Y)).First();
                return new NdMapItem(g.Key.Kind, g.Key.Name, at.ColourName, at.X, at.Y, Place(frame, at.X, at.Y));
            }).ToList();
        foreach (var (name, first, _) in RunwayNames(texts))
            if (items.All(i => i.Name != name))
                items.Add(new NdMapItem("Runway", name, first.ColourName, first.X, first.Y, Place(frame, first.X, first.Y)));
        return items.OrderBy(i => (i.X - frame.X) * (i.X - frame.X) + (i.Y - frame.Y) * (i.Y - frame.Y)).ToList();
    }

    private static bool AtLocal(DrawnText t, float x, float y) => Math.Abs(t.LocalX - x) < 0.5f && Math.Abs(t.LocalY - y) < 0.5f;

    /// <summary>Drawn where its own coordinates say: no transform moved it.</summary>
    private static bool Untransformed(DrawnText t) => Math.Abs(t.X - t.LocalX) < 0.5f && Math.Abs(t.Y - t.LocalY) < 0.5f;

    /// <summary>The map's lines from an ND's texts (in the ND's own coordinates), none while no map name is drawn;
    /// every text read here, and the compass labels, are added to <paramref name="used"/>.</summary>
    public static IReadOnlyList<string> Read(IReadOnlyList<DrawnText> texts, ISet<DrawnText> used)
    {
        var named = texts.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        var runways = named.Where(t => t.ColourName == "white" && AtLocal(t, 0, 5)).ToList();
        // Grey at OMDB, olive at VCBI (0.71, 0.67, 0.20; live 2026-10-30): whatever is not a stand, a building or the tower.
        var taxiways = named.Where(t => t.ColourName is not ("cyan" or "green") && AtLocal(t, 0, 0)).ToList();
        var buildings = named.Where(t => t.ColourName == "cyan" && AtLocal(t, 0, 0)).ToList();
        var landmarks = named.Where(t => t.ColourName == "green" && AtLocal(t, 0, 0)).ToList();
        if (runways.Count + taxiways.Count + buildings.Count == 0) return Array.Empty<string>();

        var compass = named.Where(t => t.ColourName == "white" && (AtLocal(t, -3, -517) || AtLocal(t, -3, -272))).ToList();
        var (rx, ry) = CircleCentre(compass) ?? (NdCentreX, NdCentreY);
        foreach (var t in compass) used.Add(t);
        // PLAN's compass letters are a scale too.
        foreach (var t in named.Where(IsPlanLetter)) used.Add(t);
        var runwayNames = RunwayNames(named);
        foreach (var (_, _, parts) in runwayNames)
            foreach (var t in parts) used.Add(t);

        var o = new List<string>();
        var airport = named.Where(t => t.ColourName == "white" && Untransformed(t) && t.X >= 560 && t.Y < 70)
            .OrderBy(t => MathF.Round(t.Y / 12)).ThenBy(t => t.X).ToList();
        if (airport.Count > 0)
            o.Add("Airport: " + string.Join(" ", airport.Select(t => t.Text.Trim())));
        // The box under the map: it moves up with the function panel open (y 757, else 969).
        var position = named.Where(t => t.ColourName == "yellow" && Untransformed(t) && t.Y >= 700).ToList();
        if (position.Count > 0)
            o.Add($"Position: {string.Join(" ", position.Select(t => t.Text.Trim()))}, yellow");
        // Left of the map, its y with the map's centre (y 320 with the function panel open, else 537).
        // (Its y moves with the panel, its x never.)
        var range = named.Where(t => t.ColourName == "cyan" && Math.Abs(t.X - t.LocalX) < 0.5f && t.X < 300 && t.Y is >= 250 and < 760
            && t.Text.Trim().All(ch => char.IsDigit(ch) || ch == '.')).ToList();
        if (range.Count > 0)
            o.Add($"Map range: {range[0].Text.Trim()} NM, cyan");
        foreach (var t in airport.Concat(position).Concat(range)) used.Add(t);

        void Names(string label, List<DrawnText> kind, string colour)
        {
            if (kind.Count == 0) return;
            foreach (var t in kind) used.Add(t);
            var nearest = kind.GroupBy(t => t.Text.Trim())
                .Select(g => (Name: g.Key, Distance: g.Min(t => (t.X - rx) * (t.X - rx) + (t.Y - ry) * (t.Y - ry))))
                .OrderBy(n => n.Distance).Select(n => n.Name);
            o.Add(colour.Length > 0 ? $"{label}: {string.Join(", ", nearest)}, {colour}" : $"{label}: {string.Join(", ", nearest)}");
        }
        Names("Runways", runways, "white");
        Names("Taxiways", taxiways, taxiways.Select(t => t.ColourName).Distinct().Count() == 1 ? taxiways.FirstOrDefault()?.ColourName ?? "" : "");
        Names("Stands and buildings", buildings, "cyan");
        Names("Landmarks", landmarks, "green");
        if (runwayNames.Count > 0)
            o.Add($"Runway names: {string.Join(", ", runwayNames.Select(r => r.Name))}, white");
        return o;
    }

    /// <summary>The centre of the circle the points lie on (least squares), or null with fewer than three.</summary>
    internal static (float X, float Y)? CircleCentre(IReadOnlyList<DrawnText> points)
    {
        if (points.Count < 3) return null;
        // x² + y² + D x + E y + F = 0, solved for D, E, F by the normal equations.
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sz = 0, sxz = 0, syz = 0;
        int n = points.Count;
        foreach (var p in points)
        {
            double x = p.X, y = p.Y, z = -(x * x + y * y);
            sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sz += z; sxz += x * z; syz += y * z;
        }
        double[,] a = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        double[] b = { sxz, syz, sz };
        if (Solve3(a, b) is not { } s) return null;
        return ((float)(-s[0] / 2), (float)(-s[1] / 2));
    }

    private static double[]? Solve3(double[,] a, double[] b)
    {
        double Det(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
        double d = Det(a);
        if (Math.Abs(d) < 1e-9) return null;
        var r = new double[3];
        for (int c = 0; c < 3; c++)
        {
            var m = (double[,])a.Clone();
            for (int row = 0; row < 3; row++) m[row, c] = b[row];
            r[c] = Det(m) / d;
        }
        return r;
    }
}
