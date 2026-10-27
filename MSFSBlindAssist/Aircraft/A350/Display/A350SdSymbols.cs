namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A sprite of ECAM/EcamElements.png by its centre in the atlas: what kind of symbol it is, its own
/// colour, a valve bar's angle on the sprite (0 horizontal, 90 vertical) and the word it shows.</summary>
public sealed record SdSprite(string Kind, string Colour, int Bar = 0, string? Word = null);

/// <summary>A symbol at a fixed place on an SD page: its name, the centre of its box on screen, its kind, the
/// angle of the line it sits on (a valve; 0 horizontal, 90 vertical, 45 rising right) and, for a level marker,
/// the top and bottom of its gauge.</summary>
public sealed record SdSymbol(string Label, float X, float Y, string Kind, int Flow = 90, float Top = 0, float Bottom = 0)
{
    /// <summary>What this symbol's sprite word and colour mean here ("up" on a battery is charging); null keeps the word.</summary>
    public Func<string?, string, string?>? Say { get; init; }
}

/// <summary>A flight-control surface drawn as a plain bar from a neutral band (Base0..Base1): its column on screen
/// and how many pixels a degree of deflection adds. A spoiler's band is one line and it only rises.</summary>
public sealed record SdBar(string Label, float X0, float X1, float Base0, float Base1, float PxPerDegree, bool Spoiler = false);

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
        // Arrows (a pressure, a supply, a flow): the direction and the colour are the reading.
        [(708, 17)] = new("arrow", "white", Word: "down"), [(733, 17)] = new("arrow", "white", Word: "up"),
        [(755, 17)] = new("arrow", "green", Word: "down"), [(780, 17)] = new("arrow", "green", Word: "up"),
        [(1628, 358)] = new("arrow", "white", Word: "up"),
        [(1437, 409)] = new("arrow", "white", Word: "up"), [(1464, 409)] = new("arrow", "amber", Word: "up"),
        [(1490, 409)] = new("arrow", "green", Word: "up"),
        // Bus diamonds: filled green, filled amber, white outline.
        [(644, 18)] = new("state", "green"), [(669, 18)] = new("state", "amber"), [(694, 18)] = new("state", "white"),
        // Flaps (a ring and a bar): cargo isolation valves, outflow valves.
        [(1566, 301)] = new("valve", "white", Bar: 0), [(1563, 381)] = new("flap", "green"), [(1619, 382)] = new("flap", "green"),
        // Doors (green: closed).
        [(995, 190)] = new("door", "green"), [(995, 218)] = new("door", "green"), [(995, 240)] = new("door", "green"),
        [(994, 271)] = new("door", "green"), [(1050, 240)] = new("door", "green"),
        [(1022, 190)] = new("door", "amber"),   // live: door L1 opened from the OIS
        // Engine numbers, large and small: amber while the engine is not running.
        [(934, 17)] = new("number", "white"), [(962, 17)] = new("number", "white"),
        [(934, 49)] = new("number", "amber"), [(962, 49)] = new("number", "amber"),
        [(934, 78)] = new("number", "white"), [(956, 78)] = new("number", "white"),
        [(934, 109)] = new("number", "amber"), [(956, 110)] = new("number", "amber"),
        [(1463, 357)] = new("number", "white"), [(1488, 357)] = new("number", "white"),
        [(1511, 357)] = new("number", "amber"), [(1532, 357)] = new("number", "amber"),
        [(1556, 357)] = new("number", "green"), [(1579, 357)] = new("number", "green"),
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

    /// <summary>A flap in a horizontal skin line: flat is shut, upright fully open (live: the outflow valves
    /// stand upright on the ground with the packs on).</summary>
    internal static double FlapOpenPercent(float turn)
    {
        float a = Math.Abs(turn) % 180;
        return Math.Round(Math.Min(a, 180 - a) / 90 * 100);
    }

    /// <summary>A surface bar's deflection: how far it reaches past its neutral band, in degrees.</summary>
    internal static string BarWord(SdBar bar, float y0, float y1)
    {
        float up = bar.Base0 - y0, down = y1 - bar.Base1;
        if (bar.Spoiler) return up <= 1 ? "retracted" : $"{Math.Round(up / bar.PxPerDegree):0} degrees up";
        if (up > 0.5f) return $"{Math.Round(up / bar.PxPerDegree):0} degrees up";
        if (down > 0.5f) return $"{Math.Round(down / bar.PxPerDegree):0} degrees down";
        return "neutral";
    }

    private static IEnumerable<string> ReadBars(IReadOnlyList<DrawnShape> shapes, int page)
    {
        if (!Bars.TryGetValue(page, out var bars)) yield break;
        var fills = shapes.Where(s => s.Image == 0 && s.Kind == "fill").ToList();
        foreach (var bar in bars)
        {
            // The bar is the plain fill in the surface's column that touches its neutral band (the limit ticks
            // above and below share the column but not the band).
            var f = fills.FirstOrDefault(s => Math.Abs(s.X0 - bar.X0) <= 2 && Math.Abs(s.X1 - bar.X1) <= 2
                && s.Y0 <= bar.Base1 + 1 && s.Y1 >= bar.Base0 - 1);
            if (f == null) continue;
            yield return $"{bar.Label}: {BarWord(bar, f.Y0, f.Y1)}, {A350Rgba.NameOf(f.Colour)}";
            if (bar.Label == "Right elevator" && page == 10)
            {
                // The rudder: a thin bar under the rudder trim, centred at 410.5 when neutral.
                var r = fills.FirstOrDefault(s => s.X1 - s.X0 <= 6 && s.X0 >= 395 && s.X1 <= 427 && s.Y0 >= 395 && s.Y1 <= 445);
                if (r != null)
                {
                    float d = (r.X0 + r.X1) / 2 - 410.5f;
                    yield return $"Rudder: {(Math.Abs(d) < 1 ? "centred" : d < 0 ? "left" : "right")}, {A350Rgba.NameOf(r.Colour)}";
                }
            }
        }
    }

    private static SdBar Spoiler(string label, float x0, float x1, float baseY) => new(label, x0, x1, baseY, baseY, 1, Spoiler: true);

    // F/CTL (live 2026-10-27, A350Probe sdmap): a deflection of 1 grows an aileron or elevator bar 3 px, a spoiler 1 px.
    internal static readonly IReadOnlyDictionary<int, SdBar[]> Bars = new Dictionary<int, SdBar[]>
    {
        [10] = new[]
        {
            new SdBar("Left outer aileron", 56, 71, 776, 779, 3), new SdBar("Left inner aileron", 77, 92, 776, 779, 3),
            new SdBar("Right inner aileron", 725, 740, 776, 779, 3), new SdBar("Right outer aileron", 747, 762, 776, 779, 3),
            new SdBar("Left elevator", 233, 248, 534, 537, 3), new SdBar("Right elevator", 570, 585, 534, 537, 3),
            Spoiler("Left spoiler 1", 344, 359, 787), Spoiler("Left spoiler 2", 315, 330, 787),
            Spoiler("Left spoiler 3", 257, 272, 777), Spoiler("Left spoiler 4", 228, 243, 777),
            Spoiler("Left spoiler 5", 200, 215, 777), Spoiler("Left spoiler 6", 170, 185, 777),
            Spoiler("Left spoiler 7", 141, 157, 777),
            Spoiler("Right spoiler 1", 460, 475, 787), Spoiler("Right spoiler 2", 488, 503, 787),
            Spoiler("Right spoiler 3", 546, 561, 777), Spoiler("Right spoiler 4", 575, 590, 777),
            Spoiler("Right spoiler 5", 604, 619, 777), Spoiler("Right spoiler 6", 633, 648, 777),
            Spoiler("Right spoiler 7", 662, 677, 777),
        },
    };

    public static IReadOnlyList<string> Read(IReadOnlyList<DrawnShape>? shapes, int page)
    {
        var o = new List<string>();
        if (shapes == null) return o;
        o.AddRange(ReadBars(shapes, page));
        if (!Pages.TryGetValue(page, out var symbols)) return o;
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
                "flap" => $"{FlapOpenPercent(A350ShapeReader.AngleDegrees(shape)):0} percent open",
                "door" => colour == "green" ? "closed" : "open",
                "level" => $"{Math.Round((sym.Bottom - (shape.Y0 + shape.Y1) / 2) / (sym.Bottom - sym.Top) * 100):0} percent",
                _ => sprite.Word,
            };
            if (sym.Say != null) word = sym.Say(word, colour);
            o.Add(word == null ? $"{sym.Label}: {colour}" : $"{sym.Label}: {word}, {colour}");
        }
        return o;
    }

    private static SdSymbol V(string label, float x, float y, int flow) => new(label, x, y, "valve", flow);
    private static SdSymbol S(string label, float x, float y) => new(label, x, y, "state");
    private static SdSymbol Said(string label, float x, float y, Func<string?, string, string?> say) => new(label, x, y, "state") { Say = say };
    private static SdSymbol D(string label, float x, float y) => new(label, x, y, "door");

    // Airbus colours on an arrow: green flowing or supplying, amber not (a fault), white not (normal).
    private static string? Flow(string? w, string c) => c == "green" ? "flow" : "no flow";
    private static string? Supply(string? w, string c) => c == "green" ? "supplying" : c == "amber" ? "fault" : "not supplying";
    private static string? Battery(string? w, string c) => w == "up" ? "charging" : w == "down" ? "discharging" : w;
    private static string? Pressure(string? w, string c) => c == "amber" ? "low" : "normal";
    private static string? On(string? w, string c) => "on";
    // A TR's arrows stay white while it carries its load (live: 21 to 104 A): only amber is news.
    private static string? Tr(string? w, string c) => c == "amber" ? "fault" : "normal";

    // Screen centres from the copy's draws (A350Probe sdsweep copy page); names from the panel's own controls.
    internal static readonly IReadOnlyDictionary<int, SdSymbol[]> Pages = new Dictionary<int, SdSymbol[]>
    {
        [2] = new[]
        {
            Said("Green system pressure", 172, 301, Pressure), Said("Yellow system pressure", 632, 301, Pressure),
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
        [3] = new[]
        {
            Said("BAT 1", 89, 474, Battery), Said("BAT EMER 1", 264, 474, Battery),
            Said("BAT EMER 2", 558, 474, Battery), Said("BAT 2", 733, 474, Battery),
            Said("TR 1 input", 113, 862, Tr), Said("TR EMER 1 input", 295, 862, Tr),
            Said("TR EMER 2 input", 527, 862, Tr), Said("TR 2 input", 710, 862, Tr),
        },
        [4] = new[]
        {
            S("AC EMER 1 bus indicator", 231, 372), S("AC EMER 2 bus indicator", 589, 372),
            S("AC 1A bus indicator", 55, 605), S("AC 1B bus indicator", 207, 605),
            S("AC 2B bus indicator", 611, 605), S("AC 2A bus indicator", 763, 605),
            Said("EXT 1", 106, 506, Supply), Said("EXT 2", 713, 506, Supply),
            Said("APU GEN", 409, 727, Supply), Said("RAT", 411, 430, Supply),
            Said("TR EMER 1 feed", 263, 425, Tr), Said("TR EMER 2 feed", 555, 425, Tr),
            Said("TR 1 feed", 67, 660, Tr), Said("TR 2 feed", 752, 660, Tr),
        },
        [5] = new[]
        {
            Said("Pack 1 outlet", 205, 322, Flow), Said("Pack 2 outlet", 615, 322, Flow),
            V("Pack 1 valve", 202, 534, 90), V("Pack 2 valve", 612, 534, 90),
            V("Ram air valve", 757, 393, 90), V("Crossbleed valve", 471, 610, 0), V("APU bleed valve", 410, 665, 90),
            V("Engine 1 IP valve", 203, 767, 90), V("Engine 1 HP valve", 235, 835, 90),
            V("Engine 2 IP valve", 613, 767, 90), V("Engine 2 HP valve", 645, 835, 90),
            S("Engine 1", 173, 816), S("Engine 2", 583, 816),
            Said("Left wing anti-ice", 168, 610, On), Said("Right wing anti-ice", 646, 610, On),
            Said("ASU", 338, 621, Flow),
        },
        [6] = new[]
        {
            Said("Pack 1 outlet", 335, 818, Flow), Said("Pack 2 outlet", 458, 818, Flow),
            V("Hot air 1 valve", 87, 760, 90), V("Hot air 2 valve", 709, 760, 90), V("Ram air valve", 613, 847, 90),
            Said("Hot air 1 outlet", 196, 725, Flow), Said("Hot air 2 outlet", 618, 725, Flow),
            V("Forward cargo isolation valve", 270, 640, 45), V("Aft cargo isolation valve", 561, 640, 45),
            V("Bulk cargo isolation valve", 677, 640, 45),
        },
        [7] = new[]
        {
            new SdSymbol("Avionics extract overboard valve", 158, 839, "flap"),
            new SdSymbol("Forward outflow valve", 324, 803, "flap"), new SdSymbol("Aft outflow valve", 588, 803, "flap"),
            Said("Pack 1 inlet", 104, 866, Flow), Said("Pack 2 inlet", 689, 866, Flow),
        },
        [11] = new[]
        {
            D("Door 1L", 272, 423), D("Door 1R", 345, 423), D("Door 2L", 272, 526), D("Door 2R", 345, 526),
            D("Door 3L", 272, 667), D("Door 3R", 345, 667), D("Door 4L", 272, 838), D("Door 4R", 345, 838),
            // The two hatches by the nose: not the OIS avionics bay door (live: opening that moved neither).
            D("Nose hatch, left", 292, 364), D("Nose hatch, right", 327, 384),
            D("Forward cargo door", 341, 471), D("Aft cargo door", 341, 761), D("Bulk cargo door", 272, 801),
        },
    };
}
