using System.Globalization;
namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The PFD read from an EFIS draw (pure), by area in the PFD's own coordinates (the captain's PFD is the
/// right half of EFIS#0, so its x is shifted by 836): the FMA strip across the top, the speed tape, the
/// altitude tape with its drum, QNH and target, the radio altitude under the aircraft symbol, the heading
/// scale and the line under it. Tape and drum labels are read as the values they show, never as text. What
/// fits no area (a red ATT flag) reads as drawn, with its colour.
/// Calibrated live on the stand at VCBI (2026-10-27): 219 degrees put the heading index at x 359 with 7 px a degree.
/// The altitude window (y 389) reads the tape's label with the drum; the FMA strip (to y 125, three rows) was read in
/// flight on 2026-10-28.
/// </summary>
public static class A350Pfd
{
    private const float PfdWidth = 836, FmaBottom = 125, BottomLine = 860;   // the FMA has three rows (live: A/THR at y 113)
    private const float WindowY = 389, TapePxPerFoot = 0.35f, DrumPxPerFoot = 21.75f / 20, HeadingIndexX = 359, HeadingPxPerDegree = 7;
    /// <summary>The speed tape is SpeedTape.png (75 x 2285), its 10 kt ticks 48 1/3 px apart (30 kt at row 2283.59 up to
    /// 500 kt at row 11.75): 145 px every 30 kt. Live pair: 240.2 kt with the tape at -885.2. (Read at 4.875 px a knot,
    /// every speed away from 240 read high: the stand's 30 kt floor as 32, an approach's 140 as 141.)</summary>
    private const float SpeedPxPerKnot = 145f / 30, SpeedRefPattern = -885.2f, SpeedRefKnots = 240.2f;
    private const float PitchPxPerDegree = 8.61f, PitchZeroImageY = 620.5f;

    /// <summary>The speed the tape's offset reads at its 30 kt floor (30.03 on the stand, live).</summary>
    private const double SpeedFloorReads = 30;

    /// <summary>The 30 kt tick's centre row in SpeedTape.png, which a symbol is read against: the texture itself, no index
    /// (every speed target triangle in the captures reads within 0.15 kt of its whole-knot target this way).</summary>
    private const float TapeFloorRow = 2283.59f;

    /// <summary>The tape's visible window: ScalesBackground.png's speed window, opaque from y 180 to 584, the tape's bottom
    /// frame at 585 (about 42 kt either way). A symbol drawn outside it is clipped (an off-scale target is written as a
    /// number instead), or parked at y 139.5 or 623.5, sometimes with no value at all (VFE next with the flaps FULL).</summary>
    private const float TapeTop = 180, TapeBottom = 585;

    /// <summary>The altitude tape's visible window: ScalesBackground.png's altitude column is opaque from y 177 to 584 (the
    /// drum's window cut out of it at 363 to 403). The tape's selected altitude symbol outside it is hidden by the clip.</summary>
    private const float AltitudeTapeTop = 177, AltitudeTapeBottom = 585;

    /// <summary>Where the tape's selected altitude symbol writes its value: x 597, or 646 in STD (its box 587 to 691).</summary>
    private const float SymbolX = 597, StdSymbolX = 646;

    public const string GreenDot = "Green dot", SSpeed = "S speed", FSpeed = "F speed", VfeNext = "VFE next";

    /// <summary>Speeds the tape shows only as symbols, by where each sprite sits in PFDA350Elements.png, each marking its
    /// speed at its middle: the green dot (a green circle), F (a dash and the letter), S under it in the atlas (not yet
    /// seen drawn: configuration 1), VFE next (an amber "="). Read live in a climb, level flight, a turn, a downwind and an
    /// ILS approach: VFE next 255 kt clean at three speeds, 186 kt in both captures of one approach.</summary>
    public static readonly IReadOnlyList<(string Name, float AtlasX, float AtlasY, string Colour)> TapeSymbols = new[]
    {
        (GreenDot, 976f, 719f, "green"), (SSpeed, 887f, 757f, "green"), (FSpeed, 887f, 724f, "green"), (VfeNext, 1001f, 702f, "amber"),
    };

    /// <summary>The speed target's triangle in the atlas: cyan selected, magenta managed.</summary>
    private const float TargetAtlasX = 957, TargetAtlasY = 748;

    /// <summary>The speed the tape shows at screen y, read off the texture's 30 kt tick.</summary>
    internal static double TapeKnotsAt(DrawnShape tape, float y) => 30 + (TapeFloorRow - (y - tape.PatternY)) / SpeedPxPerKnot;

    /// <summary>A sprite in the speed tape's band (x 0 to 250 of the side's PFD) by where it sits in the atlas.</summary>
    private static DrawnShape? TapeSprite(A350DrawCapture capture, int side, float atlasX, float atlasY) =>
        Sprite(capture, side, (atlasX, atlasY), s => s.X0 - (side == 0 ? PfdWidth : 0) is >= 0 and < 250);

    /// <summary>A tape symbol's speed in whole knots, read off the tape where its middle sits; or, drawn outside the tape's
    /// visible window, which way (+1 above, -1 below, Knots 0). Null while the tape does not draw it.</summary>
    public static (double Knots, int OffScale)? TapeSymbol(A350DrawCapture capture, int side, string name)
    {
        var (_, atlasX, atlasY, _) = TapeSymbols.First(t => t.Name == name);
        if (PatternOf(capture, side, 75, 2285) is not { } tape || TapeSprite(capture, side, atlasX, atlasY) is not { } symbol)
            return null;
        float middle = (symbol.Y0 + symbol.Y1) / 2;
        return middle < TapeTop ? (0, 1)
            : middle >= TapeBottom ? (0, -1)
            : (Math.Round(TapeKnotsAt(tape, middle), MidpointRounding.AwayFromZero), 0);
    }

    private static string Knots(double knots) => knots.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Whether a baro's drawn digits read a setting of 0: no altimeter reads 0, so it is a sim value the draw code
    /// asked for that MSFSBA had not answered yet (review 2026-10-29: "QNH: 0, cyan" after a load). Such a baro is not
    /// available, never spoken as a setting. The digits are read together, as a value may be drawn a cell per digit.</summary>
    internal static bool UnansweredBaro(IEnumerable<string> texts) =>
        string.Concat(texts.Select(t => t.Trim()).Where(t => t.Any(char.IsDigit))) is { Length: > 0 } digits
        && MSFSBlindAssist.Utils.NumberParsing.TryParseExternal(digits, out double setting) && setting == 0;

    /// <summary>Where V1 is drawn above the speed tape while off its scale, under the speed target (live: 142 at x 129,
    /// y 207; the target at x 153, y 174).</summary>
    private const float V1X = 129, V1Y = 207;

    /// <summary>The LS block's rows, lower left (live 2026-10-28): ident, frequency, DME.</summary>
    private const float LsIdentY = 689, LsFrequencyY = 717, LsDmeY = 745;

    /// <summary>The FMA's five columns: A/THR, vertical, lateral, approach capability (its third row is the minimum, read
    /// apart), and the AP/FD/A/THR engagement column (live: "1 FD 2" at x 685-761).</summary>
    private static readonly (string Name, float X0, float X1)[] FmaColumns =
    {
        ("autothrust", 0, 170), ("vertical", 170, 340), ("lateral", 340, 510), ("approach", 510, 670), ("engagement", 670, PfdWidth),
    };

    /// <summary>The altitude at the window (y 389): the tape's label gives it roughly (0.35 px a foot), the drum's
    /// tens label exactly (21.75 px per 20 ft); the reading is the value with the drum's last two digits nearest
    /// the tape's.</summary>
    internal static int Altitude(int tapeHundreds, float tapeY, int tens, float tensY) =>
        Altitude(tapeHundreds, tapeY, tens, tensY, WindowY, TapePxPerFoot, DrumPxPerFoot);

    /// <summary>The same reading on a display whose window, tape and drum are drawn at its own scale (the SFD's).</summary>
    internal static int Altitude(int tapeHundreds, float tapeY, int tens, float tensY, float windowY, float tapePxPerFoot, float drumPxPerFoot)
    {
        double rough = tapeHundreds * 100 + (tapeY - windowY) / tapePxPerFoot;
        double last = tens + (tensY - windowY) / drumPxPerFoot;
        return (int)Math.Round(Math.Round((rough - last) / 100) * 100 + last, MidpointRounding.AwayFromZero);
    }

    /// <summary>A pattern by its drawn size (the image numbers change per session): the speed tape 75 x 2285,
    /// the horizon 298 x 1168.</summary>
    private static DrawnShape? PatternOf(A350DrawCapture capture, int side, float w, float h) =>
        capture.Shapes?.FirstOrDefault(s => Math.Abs(s.PatternW - w) < 2 && Math.Abs(s.PatternH - h) < 2
            && ((s.X0 + s.X1) / 2 >= PfdWidth) == (side == 0));

    /// <summary>The LS scales and diamonds (PFDA350Elements.png), by drawn size: the LOC scale 294 x 35 under the
    /// attitude, the G/S scale 12 x 294 beside it; their dots 70.25 px apart around a centre 146.25 px (LOC) and
    /// 146.75 px (G/S) in.</summary>
    private const float DotPx = 70.25f, LocCentre = 146.25f, GsCentre = 146.75f;

    /// <summary>Heading scale sprites by where they sit in their atlas (the shape's box less its pattern origin):
    /// the selected heading bug, the track diamond.</summary>
    private static readonly (float X, float Y) SelectedHeadingAtlas = (811, 333), TrackAtlas = (296, 281);

    /// <summary>The baro box's STD, which is no text: a sprite, "STD" in cyan boxed in yellow (66 x 36).</summary>
    private static readonly (float X, float Y) StdAtlas = (954, 545);

    private static DrawnShape? Sprite(A350DrawCapture capture, int side, (float X, float Y) atlas, Func<DrawnShape, bool>? where = null) =>
        capture.Shapes?.FirstOrDefault(s => s.Image > 1 && Math.Abs(s.X0 - s.PatternX - atlas.X) < 1.5f
            && Math.Abs(s.Y0 - s.PatternY - atlas.Y) < 1.5f && ((s.X0 + s.X1) / 2 >= PfdWidth) == (side == 0) && (where?.Invoke(s) ?? true));

    private static IEnumerable<DrawnShape> PfdSprites(A350DrawCapture capture, int side, float w, float h) =>
        (capture.Shapes ?? Array.Empty<DrawnShape>()).Where(s => s.Image > 1 && Math.Abs(s.X1 - s.X0 - w) < 1.5f
            && Math.Abs(s.Y1 - s.Y0 - h) < 1.5f && ((s.X0 + s.X1) / 2 >= PfdWidth) == (side == 0));

    /// <summary>The ILS deviation in dots, where the aircraft is (the A380's words): a diamond right of centre is
    /// the localizer right of the aircraft. Nothing while the scales are not drawn (LS off).</summary>
    private static IEnumerable<string> Deviation(A350DrawCapture capture, int side)
    {
        static string Dots(double dots, string more, string less, string beam) =>
            Math.Abs(dots) < 0.05 ? $"on the {beam}"
            : $"{Math.Abs(dots).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} dots {(dots > 0 ? more : less)} {beam}";
        if (PfdSprites(capture, side, 294, 35).FirstOrDefault() is { } loc)
        {
            var diamond = PfdSprites(capture, side, 40, 28).FirstOrDefault(d => (d.Y0 + d.Y1) / 2 >= loc.Y0 - 10 && (d.Y0 + d.Y1) / 2 <= loc.Y1 + 10);
            yield return "Localizer: " + (diamond == null ? "off scale"
                : Dots(((diamond.X0 + diamond.X1) / 2 - (loc.X0 + LocCentre)) / DotPx, "left of", "right of", "localizer"));
        }
        if (PfdSprites(capture, side, 12, 294).FirstOrDefault() is { } gs)
        {
            var diamond = PfdSprites(capture, side, 27, 39).FirstOrDefault(d => (d.X0 + d.X1) / 2 >= gs.X0 - 20 && (d.X0 + d.X1) / 2 <= gs.X1 + 20);
            yield return "Glideslope: " + (diamond == null ? "off scale"
                : Dots(((diamond.Y0 + diamond.Y1) / 2 - (gs.Y0 + GsCentre)) / DotPx, "above", "below", "glideslope"));
        }
    }

    public static IReadOnlyList<string> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? PfdWidth : 0;
        var texts = A350DrawLayout.Region(A350DrawLayout.SplitEfis(capture.Texts, side).Pfd, A350DrawLayout.EfisPfd(side))
            .Select(t => t with { X = t.X - shift }).Where(t => t.X is >= 0 and < PfdWidth).ToList();
        var cells = A350DrawLayout.Rows(texts).SelectMany(r => r).ToList();
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();

        // The altitude tape's scale labels reach up into the FMA strip: never read as text.
        used.UnionWith(cells.Where(c => c.X is >= 585 and < 610 && c.Colour == "white" && c.Text.Length == 3 && c.Text.All(char.IsDigit)));

        // The tape's selected altitude symbol (a box, its value at x 597; "400" at x 646 in STD) is drawn where the altitude
        // puts it even outside the tape's visible window, where the tape's clip hides it: on its way out it crosses the FMA
        // strip above or the baro and the heading scale's band below, and beyond the tape's range it is held at the screen's
        // top (y 38) or bottom (y 742) (live 2026-10-28; review 2026-10-29: moved along the tape it read as an FMA column,
        // the baro and the selected heading). Taken first and never read where it lies. Cyan, it is the selected altitude,
        // which the readout at the tape's edge shows while it is hidden; magenta, the constraint the vertical mode levels
        // at (at the hold, the SID's 5000), which the readout does not show.
        var altitudeSymbol = cells.Where(c => (Math.Abs(c.X - SymbolX) <= 3 || Math.Abs(c.X - StdSymbolX) <= 3)
            && c.Colour is "cyan" or "magenta" && Digits(c.Text)).ToList();
        used.UnionWith(altitudeSymbol);
        static bool Hidden(DrawnCell c) => c.Y < AltitudeTapeTop || c.Y >= AltitudeTapeBottom;

        // The selected altitude, read after the FMA: beyond the tape's range the readout at the tape's top or bottom, "FL"
        // at 587 and its number at 688 (live 2026-10-28: y 172 above, 615 below), one value; in range the tape's symbol.
        var selectedAltitude = cells.Where(c => !used.Contains(c) && c.Colour == "cyan" && c.X is >= 585 and < 720
                && c.Y is >= FmaBottom and < 640 && !MetricText.IsMatch(c.Text.Trim()))
            .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        used.UnionWith(selectedAltitude);
        if (selectedAltitude.Count == 0)
            selectedAltitude = altitudeSymbol.Where(c => c.Colour == "cyan").OrderBy(Hidden).Take(1).ToList();
        var constraint = altitudeSymbol.Where(c => c.Colour == "magenta").ToList();

        // The minimum set on PERF APPR, on the FMA's fourth column's third row: "RADIO" or "BARO", white, and its height in
        // cyan (live 2026-10-28, RADIO 50); the column's top rows are the approach capability.
        var minimum = cells.Where(c => !used.Contains(c) && c.X is >= 510 and < 670 && Math.Abs(c.Y - 113) <= 8).OrderBy(c => c.X).ToList();
        if (minimum.Any(c => c.Text.Trim() is "RADIO" or "BARO")) used.UnionWith(minimum);
        else minimum.Clear();

        // The FMA strip, one column at a time.
        foreach (var (name, x0, x1) in FmaColumns)
        {
            var col = cells.Where(c => !used.Contains(c) && c.Y < FmaBottom && c.X >= x0 && c.X < x1).ToList();
            if (col.Count == 0) continue;
            used.UnionWith(col);
            // The font's small capitals are lowercase letters ("ap1" for AP1, "man flx" for MAN FLX): read as capitals.
            var rows = A350DrawLayout.Rows(col.Select(c => ToText(c with { Text = c.Text.ToUpperInvariant() })));
            o.Add($"FMA {name}: " + (rows.SelectMany(r => r).Select(c => c.Colour).Distinct().Count() == 1
                ? string.Join("; ", rows.Select(r => string.Join(" ", r.Select(c => c.Text)))) + ", " + rows[0][0].Colour
                : string.Join("; ", rows.Select(A350DrawLayout.Speak))));
        }

        if (minimum.Count > 0)
            o.Add($"Minimum: {string.Join(" ", minimum.Select(c => c.Text.Trim()))}, "
                + (minimum.LastOrDefault(c => c.Text.Trim() is not ("RADIO" or "BARO")) ?? minimum[0]).Colour);
        if (selectedAltitude.Count > 0)
            o.Add($"Selected altitude: {string.Join(" ", selectedAltitude.Select(c => c.Text))}, cyan");
        // METRIC ALT (live 2026-10-30): the selected altitude in metres above the altitude tape ("30M" at x 547 y 175 with
        // 100 ft selected, "1520M" with 5000), cyan.
        foreach (var c in cells.Where(c => !used.Contains(c) && c.Colour == "cyan" && c.X is >= 500 and < 720
                     && c.Y is >= FmaBottom and < BottomLine && MetricText.IsMatch(c.Text.Trim())))
        {
            used.Add(c);
            o.Add($"Selected altitude in metres: {c.Text.Trim()[..^1]}, cyan");
        }
        foreach (var c in constraint)
            o.Add($"Altitude constraint: {c.Text.Trim()}, magenta");

        // Speed and attitude are textures: the speed tape's offset, the horizon under the attitude centre. On the stand
        // the tape rests on its 30 kt floor, which its offset reads as 30.03.
        if (PatternOf(capture, side, 75, 2285) is { } speedTape)
        {
            double knots = Math.Round(SpeedRefKnots + (speedTape.PatternY - SpeedRefPattern) / SpeedPxPerKnot, MidpointRounding.AwayFromZero);
            o.Add(knots <= SpeedFloorReads ? "Speed: 30 knots or less" : string.Create(CultureInfo.InvariantCulture, $"Speed: {knots:0} knots"));
            // The tape's symbols as the pilot sees them: only inside its visible window.
            foreach (var (name, _, _, colour) in TapeSymbols)
                if (TapeSymbol(capture, side, name) is { OffScale: 0 } symbol)
                    o.Add($"{name}: {Knots(symbol.Knots)}, {colour}");
            // The speed target on the visible tape: its triangle, cyan selected, magenta managed (off the scale the aircraft
            // writes it as a number instead, read with the texts below).
            if (TapeSprite(capture, side, TargetAtlasX, TargetAtlasY) is { } target && (target.Y0 + target.Y1) / 2 is var middle
                && middle >= TapeTop && middle < TapeBottom)
            {
                string at = Knots(Math.Round(TapeKnotsAt(speedTape, middle), MidpointRounding.AwayFromZero));
                o.Add(A350Rgba.NameOf(target.Colour) == "magenta" ? $"Speed target: {at}, magenta" : $"Selected speed: {at}, cyan");
            }
        }
        // The Mach number under the speed tape (live in the climb: ".681", green).
        foreach (var c in cells.Where(c => !used.Contains(c) && c.X < 140 && c.Y is >= 640 and < 680 && MachText.IsMatch(c.Text.Trim())))
        {
            used.Add(c);
            o.Add($"Mach: 0{c.Text.Trim()}, {c.Colour}");
        }
        if (PatternOf(capture, side, 298, 1168) is { } horizon)
        {
            float dx = HeadingIndexX + (side == 0 ? PfdWidth : 0) - horizon.PatternX, dy = WindowY - horizon.PatternY;
            float det = horizon.PatternA * horizon.PatternD - horizon.PatternB * horizon.PatternC;
            if (Math.Abs(det) > 1e-6f)
            {
                double pitch = (PitchZeroImageY - (-horizon.PatternB * dx + horizon.PatternA * dy) / det) / PitchPxPerDegree;
                o.Add($"Pitch: {Math.Abs(pitch).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} degrees {(pitch >= 0 ? "up" : "down")}");
                double bank = A350ShapeReader.AngleDegrees(horizon);
                o.Add(Math.Abs(bank) < 1 ? "Bank: wings level" : string.Create(CultureInfo.InvariantCulture, $"Bank: {Math.Abs(Math.Round(bank)):0} degrees {(bank > 0 ? "left" : "right")}"));
            }
        }

        // The LS block, lower left (LS on): ident (none until it decodes), frequency (drawn "109." and "90"), DME and
        // its "nm", magenta; and the course pointer, a magenta number at the heading scale's end while off it. Taken
        // before the speed tape, whose targets share the band; read after the deviation.
        var ls = new List<string>();
        var lsBlock = cells.Where(c => c.X < 140 && c.Y is >= 670 and < 760 && (c.Colour == "magenta" || c.Text.Trim() == "nm"))
            .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();   // the heading scale's labels share the band, white
        if (lsBlock.Any(c => c.Colour == "magenta"))
        {
            // Each row on its own line (live: ident y 689, frequency 717, DME 745): an ident with digits (FLS, GLS) is
            // still the ident. A text on none of them is no part of the block and reads as drawn.
            static bool OnLine(DrawnCell c) => new[] { LsIdentY, LsFrequencyY, LsDmeY }.Any(y => Math.Abs(c.Y - y) <= 8);
            used.UnionWith(lsBlock.Where(OnLine));
            string Row(float y) => string.Concat(lsBlock.Where(c => c.Text.Trim() != "nm" && Math.Abs(c.Y - y) <= 8)
                .OrderBy(c => c.X).Select(c => c.Text.Trim()));
            string head = string.Join(" ", new[] { Row(LsIdentY), Row(LsFrequencyY) }.Where(s => s.Length > 0));
            string dme = Row(LsDmeY) is { Length: > 0 } d ? $", {d} NM" : "";
            ls.Add($"LS: {head}{dme}, magenta");
        }
        foreach (var c in cells.Where(c => !used.Contains(c) && c.Colour == "magenta" && c.Y is >= 725 and < 760 && c.X is >= 140 and < 600
                     && c.Text.Length == 3 && c.Text.All(char.IsDigit)))
        {
            used.Add(c);
            ls.Add($"LS course: {c.Text}, magenta");
        }

        // The selected heading while off the heading scale: a cyan number at the scale's end. At its left end it sits inside
        // the speed tape's band and was read as a speed too (live 2026-10-28 "Selected speed: 305" with the speed managed):
        // taken before the speed tape, read with the heading.
        var offScaleHeading = cells.Where(c => !used.Contains(c) && c.Colour == "cyan" && c.Y is >= 675 and < 705
            && c.Text.Length == 3 && c.Text.All(char.IsDigit)).ToList();
        used.UnionWith(offScaleHeading);

        // The speed tape: its target while off the scale, magenta managed (the V2 before takeoff), cyan selected; V1 in
        // cyan under it.
        foreach (var c in cells.Where(c => !used.Contains(c) && c.X < 250 && c.Y >= FmaBottom && c.Y < BottomLine
                     && (c.Colour == "cyan" || c.Colour == "magenta" && c.Text.Length > 0 && c.Text.All(char.IsDigit))))
        {
            used.Add(c);
            o.Add(c.Colour == "magenta" ? $"Speed target: {c.Text}, magenta"
                : Math.Abs(c.X - V1X) <= 8 && Math.Abs(c.Y - V1Y) <= 8 ? $"V1: {c.Text}, cyan"
                : $"Selected speed: {c.Text}, cyan");
        }

        // The altitude: the tape's label nearest the window and the drum's tens label nearest it; the drum's big
        // digits only repeat it. The drum is green, amber below the BARO minimum (review 2026-10-29: only green was
        // read, so the altitude went unread there), and the line says which.
        static bool Drum(string colour) => colour is "green" or "amber";
        var tapeLabels = cells.Where(c => c.X is >= 585 and < 610 && c.Colour == "white" && c.Text.Length == 3 && c.Text.All(char.IsDigit)).ToList();
        var drumTens = cells.Where(c => c.X is >= 658 and < 676 && c.Y is >= 330 and < 460 && Drum(c.Colour)
            && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        used.UnionWith(drumTens);
        used.UnionWith(cells.Where(c => c.X is >= 605 and < 658 && c.Y is >= 330 and < 460 && Drum(c.Colour)));
        if (tapeLabels.Count > 0 && drumTens.Count > 0)
        {
            var tape = tapeLabels.OrderBy(c => Math.Abs(c.Y - WindowY)).First();
            var tens = drumTens.OrderBy(c => Math.Abs(c.Y - WindowY)).First();
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Altitude: {Altitude(int.Parse(tape.Text, CultureInfo.InvariantCulture), tape.Y, int.Parse(tens.Text, CultureInfo.InvariantCulture), tens.Y)} feet, {tens.Colour}"));
        }
        // METRIC ALT: the altitude in metres in the box under the tape ("20" at x 692 y 696 with the field at 81 ft), in the
        // drum's colour.
        foreach (var c in cells.Where(c => !used.Contains(c) && Drum(c.Colour) && c.X is >= 660 and < 720 && c.Y is >= 680 and < 715
                     && c.Text.Trim() is { Length: > 0 } m && m.TrimStart('-').All(char.IsDigit)))
        {
            used.Add(c);
            o.Add($"Altitude in metres: {c.Text.Trim()}, {c.Colour}");
        }

        // The vertical speed, in hundreds of feet a minute beside the altitude tape: above the window climbing.
        foreach (var c in cells.Where(c => c.X >= 720 && c.Y is >= FmaBottom and < BottomLine && c.Text.All(char.IsDigit) && c.Text.Length is 1 or 2))
        {
            used.Add(c);
            o.Add($"Vertical speed: {int.Parse(c.Text) * 100} feet per minute {(c.Y < WindowY ? "up" : "down")}, {c.Colour}");
        }
        var baro = cells.Where(c => !used.Contains(c) && c.X is >= 580 and < 720 && c.Y is >= 640 and < 680).ToList();
        if (baro.Count > 0)
        {
            used.UnionWith(baro);
            var label = baro.FirstOrDefault(c => c.Text is "QNH" or "QFE");
            var value = baro.Where(c => c != label).ToList();
            o.Add(label != null && value.Count > 0
                ? UnansweredBaro(value.Select(c => c.Text)) ? $"{label.Text}: not available" : $"{label.Text}: {A350DrawLayout.Speak(value)}"
                : "Baro: " + A350DrawLayout.Speak(baro));
        }
        // In STD the box holds no text but its sprite (review 2026-10-29: the PFD read no baro at all).
        if (Sprite(capture, side, StdAtlas) != null)
            o.Add("Baro: STD, cyan in a yellow box");

        o.AddRange(Deviation(capture, side));
        o.AddRange(ls);

        // The radio altitude, under the aircraft symbol.
        foreach (var c in cells.Where(c => Math.Abs(c.X - HeadingIndexX) <= 40 && c.Y is >= 540 and < 620 && c.Text.All(ch => char.IsDigit(ch) || ch == '-')))
        {
            used.Add(c);
            o.Add($"Radio altitude: {c.Text}, {c.Colour}");
        }

        // The heading scale: labels every 10 degrees, 7 px a degree, the index at x 359.
        var scale = cells.Where(c => c.Y is >= 725 and < 750 && c.Colour == "white" && c.Text.Length <= 2 && c.Text.All(char.IsDigit)).ToList();
        if (scale.Count > 0)
        {
            used.UnionWith(scale);
            var nearest = scale.OrderBy(c => Math.Abs(c.X - HeadingIndexX)).First();
            double heading = (int.Parse(nearest.Text) * 10 + (HeadingIndexX - nearest.X) / HeadingPxPerDegree + 360) % 360;
            o.Add($"Heading: {Degrees(heading)}");   // three digits, as the selected heading and the track

            static string Degrees(double d) => ((int)Math.Round(d) % 360 + 360) % 360 is var h ? h.ToString("000", CultureInfo.InvariantCulture) : "";
            float At(DrawnShape sh) => (sh.X0 + sh.X1) / 2 - shift;
            // The selected heading: the cyan number beside the scale while the target is off it (taken above; the bug
            // then waits at the scale's edge), else the bug (shown in HDG, not in LOC or NAV).
            if (offScaleHeading.Count > 0)
                o.Add($"Selected heading: {offScaleHeading[0].Text}, cyan");
            else if (Sprite(capture, side, SelectedHeadingAtlas) is { } bug)
                o.Add($"Selected heading: {Degrees(heading + (At(bug) - HeadingIndexX) / HeadingPxPerDegree)}, cyan");
            // The track diamond is a green sprite: its colour is the atlas's, never a drawn text's (as STD's).
            if (Sprite(capture, side, TrackAtlas) is { } track)
                o.Add($"Track: {Degrees(heading + (At(track) - HeadingIndexX) / HeadingPxPerDegree)}, green");
        }
        else if (offScaleHeading.Count > 0)
            o.Add($"Selected heading: {offScaleHeading[0].Text}, cyan");

        // The flap indication at the bottom: "S" and "F" labels and the configuration, cyan while moving.
        var flaps = cells.Where(c => c.X < 330 && c.Y >= 900).ToList();
        var config = flaps.Where(c => c.Text is not ("S" or "F")).ToList();
        if (config.Count > 0)
        {
            used.UnionWith(flaps);
            o.Add("Flaps: " + A350DrawLayout.Speak(config));
        }

        // Everything else as drawn: flags, messages, the line under the heading scale. A two-line message there is one
        // text (OUT OF over RANGE, red, beside the white T.O THS), placed between its lines so it reads in its row.
        var rest = cells.Where(c => !used.Contains(c)).ToList();
        foreach (var upper in rest.Where(c => c.Y >= BottomLine).OrderBy(c => c.Y).ToList())
            if (rest.Contains(upper) && rest.FirstOrDefault(c => c != upper && c.Colour == upper.Colour && Math.Abs(c.X - upper.X) <= 6
                    && c.Y - upper.Y is >= 12 and <= 30) is { } lower)
            {
                rest.Remove(lower);
                rest[rest.IndexOf(upper)] = upper with { Text = upper.Text + " " + lower.Text, Y = (upper.Y + lower.Y) / 2 };
            }
        foreach (var row in A350DrawLayout.Rows(rest.Select(ToText)))
            o.Add(A350DrawLayout.Speak(row));
        return o;
    }

    private static DrawnText ToText(DrawnCell c) => new(c.Text, c.X, c.Y, c.X, c.Y, 0, A350Rgba.Of(c.Colour));

    private static bool Digits(string s) => s.Trim() is { Length: > 0 } t && t.All(char.IsDigit);

    private static readonly System.Text.RegularExpressions.Regex MachText = new(@"^\.\d{2,3}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>A metric altitude box's text: metres, then "M" ("1520M").</summary>
    private static readonly System.Text.RegularExpressions.Regex MetricText = new(@"^-?\d+M$", System.Text.RegularExpressions.RegexOptions.Compiled);

}
