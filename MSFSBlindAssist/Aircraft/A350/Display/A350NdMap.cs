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

    private static bool AtLocal(DrawnText t, float x, float y) => Math.Abs(t.LocalX - x) < 0.5f && Math.Abs(t.LocalY - y) < 0.5f;

    /// <summary>Drawn where its own coordinates say: no transform moved it.</summary>
    private static bool Untransformed(DrawnText t) => Math.Abs(t.X - t.LocalX) < 0.5f && Math.Abs(t.Y - t.LocalY) < 0.5f;

    /// <summary>The map's lines from an ND's texts (in the ND's own coordinates), none while no map name is drawn;
    /// every text read here, and the compass labels, are added to <paramref name="used"/>.</summary>
    public static IReadOnlyList<string> Read(IReadOnlyList<DrawnText> texts, ISet<DrawnText> used)
    {
        var named = texts.Where(t => !string.IsNullOrWhiteSpace(t.Text)).ToList();
        var runways = named.Where(t => t.ColourName == "white" && AtLocal(t, 0, 5)).ToList();
        var taxiways = named.Where(t => t.ColourName == "grey" && AtLocal(t, 0, 0)).ToList();
        var buildings = named.Where(t => t.ColourName == "cyan" && AtLocal(t, 0, 0)).ToList();
        if (runways.Count + taxiways.Count + buildings.Count == 0) return Array.Empty<string>();

        var compass = named.Where(t => t.ColourName == "white" && AtLocal(t, -3, -517)).ToList();
        var (rx, ry) = CircleCentre(compass) ?? (NdCentreX, NdCentreY);
        foreach (var t in compass) used.Add(t);

        var o = new List<string>();
        var airport = named.Where(t => t.ColourName == "white" && Untransformed(t) && t.X >= 560 && t.Y < 70)
            .OrderBy(t => MathF.Round(t.Y / 12)).ThenBy(t => t.X).ToList();
        if (airport.Count > 0)
            o.Add("Airport: " + string.Join(" ", airport.Select(t => t.Text.Trim())));
        var position = named.Where(t => t.ColourName == "yellow" && Untransformed(t) && t.Y >= 900).ToList();
        if (position.Count > 0)
            o.Add($"Position: {string.Join(" ", position.Select(t => t.Text.Trim()))}, yellow");
        var range = named.Where(t => t.ColourName == "cyan" && Untransformed(t) && t.X < 120 && t.Y is >= 450 and < 650
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
            o.Add($"{label}: {string.Join(", ", nearest)}, {colour}");
        }
        Names("Runways", runways, "white");
        Names("Taxiways", taxiways, "grey");
        Names("Buildings", buildings, "cyan");
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
