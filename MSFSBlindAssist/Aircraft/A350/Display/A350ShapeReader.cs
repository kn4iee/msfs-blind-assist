namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One shape NanoVG rendered (a valve, a pump, a needle, a door): what kind of render call, the box
/// it covers on the gauge, the image it painted with (0 for a plain colour) and its colour.</summary>
public sealed record DrawnShape(string Kind, float X0, float Y0, float X1, float Y1, int Image, A350Rgba? Colour = null)
{
    /// <summary>An image paint's pattern: where the image's origin lands and its drawn size. With the shape's box
    /// they say which sprite of an atlas was drawn (a pump running or off, a valve open or closed).</summary>
    /// <summary>The pattern's transform (NanoVG's xform: x' = A x + C y + X, y' = B x + D y + Y).</summary>
    public float PatternA { get; init; } = 1;
    public float PatternB { get; init; }
    public float PatternC { get; init; }
    public float PatternD { get; init; } = 1;
    public float PatternX { get; init; }
    public float PatternY { get; init; }
    public float PatternW { get; init; }
    public float PatternH { get; init; }
}

/// <summary>
/// Reads a NanoVG render call's shape out of the module's memory (pure). The A350 compiles NanoVG in and
/// renders through the sim's fsRenderFill / fsRenderStroke / fsRenderTriangles, whose arguments are wasm32
/// pointers: NVGpaint (76 bytes: innerColor at +40, image at +72), the fill's bounds (x0, y0, x1, y1),
/// NVGpath (40 bytes: stroke vertices at +24, their count at +28) and NVGvertex (x, y, u, v). Already in
/// gauge coordinates: NanoVG applied its transforms. A pointer outside memory gives null.
/// </summary>
public static class A350ShapeReader
{
    private const int PaintOrigin = 16, PaintExtent = 24, PaintColour = 40, PaintImage = 72, PaintSize = 76, PathSize = 40, PathStroke = 24, VertexSize = 16;

    private static bool In(ReadOnlySpan<byte> m, long at, long len) => at >= 0 && len >= 0 && at + len <= m.Length;
    private static float Fl(ReadOnlySpan<byte> m, int at) => BitConverter.ToSingle(m.Slice(at, 4));
    private static int In32(ReadOnlySpan<byte> m, int at) => BitConverter.ToInt32(m.Slice(at, 4));

    private static (A350Rgba Colour, int Image)? Paint(ReadOnlySpan<byte> m, int paint) =>
        In(m, paint, PaintSize)
            ? (new A350Rgba(Fl(m, paint + PaintColour), Fl(m, paint + PaintColour + 4), Fl(m, paint + PaintColour + 8), Fl(m, paint + PaintColour + 12)),
               In32(m, paint + PaintImage))
            : null;

    private static DrawnShape WithPattern(ReadOnlySpan<byte> m, int paint, DrawnShape s) => s with
    {
        PatternA = Fl(m, paint), PatternB = Fl(m, paint + 4), PatternC = Fl(m, paint + 8), PatternD = Fl(m, paint + 12),
        PatternX = Fl(m, paint + PaintOrigin), PatternY = Fl(m, paint + PaintOrigin + 4),
        PatternW = Fl(m, paint + PaintExtent), PatternH = Fl(m, paint + PaintExtent + 4),
    };

    /// <summary>Where the shape's centre comes from in its image, in pattern units (the atlas's pixels when the
    /// pattern is drawn at the image's own size): which sprite it is, however it is turned.</summary>
    public static (float X, float Y) SpriteCentre(DrawnShape s)
    {
        float cx = (s.X0 + s.X1) / 2 - s.PatternX, cy = (s.Y0 + s.Y1) / 2 - s.PatternY;
        float det = s.PatternA * s.PatternD - s.PatternB * s.PatternC;
        if (Math.Abs(det) < 1e-6f) return (float.NaN, float.NaN);
        return ((s.PatternD * cx - s.PatternC * cy) / det, (-s.PatternB * cx + s.PatternA * cy) / det);
    }

    /// <summary>How far the pattern is turned, in degrees clockwise on screen (a needle's or a valve's turn).</summary>
    public static float AngleDegrees(DrawnShape s) => MathF.Atan2(s.PatternB, s.PatternA) * 180 / MathF.PI;

    public static DrawnShape? Fill(ReadOnlySpan<byte> m, int paint, int bounds)
    {
        if (Paint(m, paint) is not var (colour, image) || !In(m, bounds, 16)) return null;
        return WithPattern(m, paint, new DrawnShape("fill", Fl(m, bounds), Fl(m, bounds + 4), Fl(m, bounds + 8), Fl(m, bounds + 12), image, colour));
    }

    public static DrawnShape? Stroke(ReadOnlySpan<byte> m, int paint, int paths, int npaths)
    {
        if (Paint(m, paint) is not var (colour, image) || npaths <= 0 || !In(m, paths, (long)npaths * PathSize)) return null;
        var box = new Box();
        for (int i = 0; i < npaths; i++)
        {
            int p = paths + i * PathSize;
            if (!box.AddVertices(m, In32(m, p + PathStroke), In32(m, p + PathStroke + 4))) return null;
        }
        return box.Empty ? null : WithPattern(m, paint, new DrawnShape("stroke", box.X0, box.Y0, box.X1, box.Y1, image, colour));
    }

    public static DrawnShape? Triangles(ReadOnlySpan<byte> m, int paint, int verts, int nverts)
    {
        if (Paint(m, paint) is not var (colour, image)) return null;
        var box = new Box();
        if (!box.AddVertices(m, verts, nverts) || box.Empty) return null;
        return WithPattern(m, paint, new DrawnShape("triangles", box.X0, box.Y0, box.X1, box.Y1, image, colour));
    }

    private sealed class Box
    {
        public float X0 = float.MaxValue, Y0 = float.MaxValue, X1 = float.MinValue, Y1 = float.MinValue;
        public bool Empty => X0 > X1;

        public bool AddVertices(ReadOnlySpan<byte> m, int at, int count)
        {
            if (count <= 0) return true;
            if (!In(m, at, (long)count * VertexSize)) return false;
            for (int i = 0; i < count; i++)
            {
                float x = Fl(m, at + i * VertexSize), y = Fl(m, at + i * VertexSize + 4);
                X0 = Math.Min(X0, x); Y0 = Math.Min(Y0, y); X1 = Math.Max(X1, x); Y1 = Math.Max(Y1, y);
            }
            return true;
        }
    }
}
