namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A sprite of ECAM/EcamElements.png by its centre in the atlas: what kind of symbol it is, its own
/// colour, a valve bar's angle on the sprite (0 horizontal, 90 vertical) and the word it shows.</summary>
public sealed record SdSprite(string Kind, string Colour, int Bar = 0, string? Word = null);

/// <summary>A symbol at a fixed place on an SD page: its name, the centre of its box on screen, its kind, the
/// angle of the line it sits on (a valve; 0 horizontal, 90 vertical, 45 rising right) and, for a level marker,
/// the top and bottom of its gauge.</summary>
public sealed record SdSymbol(string Label, float X, float Y, string Kind, int Flow = 90, float Top = 0, float Bottom = 0);

/// <summary>
/// The SD's symbols as drawn (pure): each NanoVG shape painted from the ECAM atlas (image 22) is matched to a
/// page's symbol by where it is, and read by which sprite it is: "Engine 1 green pump: LO, amber". A valve is
/// open when its bar lies along the flow line, whatever way the sprite is turned. The colour is the paint's
/// tint, or the sprite's own colour under a white tint. A symbol the page does not draw is not read.
/// </summary>
public static class A350SdSymbols
{
    private const int Atlas = 22;
    private const float Reach = 12, SpriteReach = 4;

    private static readonly Dictionary<(int X, int Y), SdSprite> Catalogue = new()
    {
        // Valves: a ring with a bar.
        [(842, 72)] = new("valve", "green", Bar: 90), [(898, 72)] = new("valve", "green", Bar: 0),
        [(842, 118)] = new("valve", "amber", Bar: 0),
        [(1221, 364)] = new("valve", "green", Bar: 0), [(1270, 364)] = new("valve", "green", Bar: 90),
        [(1321, 364)] = new("valve", "amber", Bar: 0), [(1370, 364)] = new("valve", "amber", Bar: 90),
        [(1119, 364)] = new("state", "green", Word: "in transit"), [(1170, 364)] = new("state", "amber", Word: "in transit"),
        [(1418, 364)] = new("state", "amber", Word: "failed"),
        // Pump boxes.
        [(842, 24)] = new("state", "green", Word: "running"), [(842, 166)] = new("state", "green", Word: "standby"),
        [(898, 24)] = new("state", "amber", Word: "LO"), [(898, 119)] = new("state", "amber", Word: "off"),
        // Pump triangles (left, right, down): filled running, outline not running.
        [(885, 169)] = Tri("white", false), [(911, 169)] = Tri("white", false), [(937, 169)] = Tri("white", false),
        [(885, 195)] = Tri("amber", true), [(911, 195)] = Tri("amber", true), [(937, 195)] = Tri("amber", true),
        [(885, 219)] = Tri("amber", false), [(911, 219)] = Tri("amber", false), [(937, 219)] = Tri("amber", false),
        [(885, 245)] = Tri("green", true), [(911, 245)] = Tri("green", true), [(937, 245)] = Tri("green", true),
        [(885, 271)] = Tri("green", false), [(911, 271)] = Tri("green", false), [(937, 271)] = Tri("green", false),
        // Pressure triangles: their tint is the reading.
        [(708, 17)] = new("pressure", "white"), [(733, 17)] = new("pressure", "white"),
        [(755, 17)] = new("pressure", "green"), [(780, 17)] = new("pressure", "green"),
        // Engine numbers, large and small: amber while the engine is not running.
        [(934, 17)] = new("number", "white"), [(962, 17)] = new("number", "white"),
        [(934, 49)] = new("number", "amber"), [(962, 49)] = new("number", "amber"),
        [(934, 78)] = new("number", "white"), [(956, 78)] = new("number", "white"),
        [(934, 109)] = new("number", "amber"), [(956, 110)] = new("number", "amber"),
        // Level markers.
        [(803, 23)] = new("level", "green"),
    };

    private static SdSprite Tri(string colour, bool filled) => new("state", colour, Word: filled ? "running" : "not running");

    internal static SdSprite? SpriteAt(float x, float y)
    {
        foreach (var (k, v) in Catalogue)
            if (Math.Abs(k.X - x) <= SpriteReach && Math.Abs(k.Y - y) <= SpriteReach) return v;
        return null;
    }

    /// <summary>Open when the bar, turned with its sprite, lies within 45 degrees of the flow line.</summary>
    internal static string ValveWord(SdSprite valve, float turn, int flow)
    {
        float d = ((valve.Bar + turn - flow) % 180 + 180) % 180;
        return d < 45 || d > 135 ? "open" : "closed";
    }

    public static IReadOnlyList<string> Read(IReadOnlyList<DrawnShape>? shapes, int page)
    {
        var o = new List<string>();
        if (shapes == null || !Pages.TryGetValue(page, out var symbols)) return o;
        var sprites = shapes.Where(s => s.Image == Atlas).ToList();
        foreach (var sym in symbols)
        {
            var shape = sprites.Where(s => Math.Abs((s.X0 + s.X1) / 2 - sym.X) <= Reach && Math.Abs((s.Y0 + s.Y1) / 2 - sym.Y) <= Reach)
                .OrderBy(s => Math.Abs((s.X0 + s.X1) / 2 - sym.X) + Math.Abs((s.Y0 + s.Y1) / 2 - sym.Y)).FirstOrDefault();
            if (shape == null) continue;
            var (sx, sy) = A350ShapeReader.SpriteCentre(shape);
            var sprite = SpriteAt(sx, sy);
            if (sprite == null) { MSFSBlindAssist.Utils.Logging.Log.Debug("A350Display", $"SD {page}: unknown sprite {sx:0},{sy:0} at {sym.Label}"); continue; }
            var tint = A350Rgba.NameOf(shape.Colour);
            var colour = tint == "white" ? sprite.Colour : tint;
            string? word = sprite.Kind switch
            {
                "valve" => ValveWord(sprite, A350ShapeReader.AngleDegrees(shape), sym.Flow),
                "pressure" => colour == "amber" ? "low" : "normal",
                "level" => $"{Math.Round((sym.Bottom - (shape.Y0 + shape.Y1) / 2) / (sym.Bottom - sym.Top) * 100):0} percent",
                _ => sprite.Word,
            };
            o.Add(word == null ? $"{sym.Label}: {colour}" : $"{sym.Label}: {word}, {colour}");
        }
        return o;
    }

    private static SdSymbol V(string label, float x, float y, int flow) => new(label, x, y, "valve", flow);
    private static SdSymbol S(string label, float x, float y) => new(label, x, y, "state");

    // Screen centres from the copy's draws (A350Probe sdsweep copy page); names from the panel's own controls.
    internal static readonly IReadOnlyDictionary<int, SdSymbol[]> Pages = new Dictionary<int, SdSymbol[]>
    {
        [2] = new[]
        {
            S("Green system pressure", 172, 301), S("Yellow system pressure", 632, 301),
            S("Engine 1 green pump", 105, 615), V("Engine 1 green supply valve", 105, 678, 90),
            S("Engine 1 yellow pump", 202, 446), V("Engine 1 yellow supply valve", 202, 510, 90),
            S("Engine 2 green pump", 612, 615), V("Engine 2 green supply valve", 612, 679, 90),
            S("Engine 2 yellow pump", 704, 446), V("Engine 2 yellow supply valve", 704, 510, 90),
            S("Green electric pump", 338, 343), S("Yellow electric pump", 474, 343),
            S("Engine 1", 83, 463), S("Engine 2", 592, 463),
            new("Green reservoir level", 123, 771, "level", Top: 730, Bottom: 870),
            new("Yellow reservoir level", 722, 771, "level", Top: 730, Bottom: 870),
        },
        [8] = new[]
        {
            S("Left main pump", 132, 608), S("Left standby pump", 181, 608),
            S("Center left pump", 309, 608), S("Center right pump", 512, 608),
            S("Right main pump", 640, 608), S("Right standby pump", 689, 608),
            V("Crossfeed A valve", 378, 512, 0), V("Crossfeed B valve", 445, 484, 0),
            V("Engine 1 LP valve", 157, 466, 90), V("Engine 2 LP valve", 665, 466, 90),
            S("Engine 1", 127, 447), S("Engine 2", 636, 447),
        },
    };
}
