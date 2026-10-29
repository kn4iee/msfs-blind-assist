namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The PFD read from an EFIS draw (pure), by area in the PFD's own coordinates (the captain's PFD is the
/// right half of EFIS#0, so its x is shifted by 836): the FMA strip across the top, the speed tape, the
/// altitude tape with its drum, QNH and target, the radio altitude under the aircraft symbol, the heading
/// scale and the line under it. Tape and drum labels are read as the values they show, never as text. What
/// fits no area (a red ATT flag) reads as drawn, with its colour.
/// Calibrated live on the stand at VCBI (2026-10-27): 12.8 ft put the drum window at y 422, 219 degrees
/// put the heading index at x 359 with 7 px a degree. The FMA columns' bounds are provisional until flown.
/// </summary>
public static class A350Pfd
{
    private const float PfdWidth = 836, FmaBottom = 125, BottomLine = 860;   // the FMA has three rows (live: A/THR at y 113)
    private const float WindowY = 389, TapePxPerFoot = 0.35f, DrumPxPerFoot = 21.75f / 20, HeadingIndexX = 359, HeadingPxPerDegree = 7;
    private const float SpeedPxPerKnot = 4.875f, SpeedRefPattern = -885.2f, SpeedRefKnots = 240.2f;
    private const float PitchPxPerDegree = 8.61f, PitchZeroImageY = 620.5f;

    /// <summary>The speed the tape's offset reads at its 30 kt floor and a knot over (32 on the stand, live).</summary>
    private const double SpeedFloorReads = 33;

    /// <summary>Where V1 is drawn above the speed tape while off its scale, under the speed target (live: 142 at x 129,
    /// y 207; the target at x 153, y 174).</summary>
    private const float V1X = 129, V1Y = 207;

    /// <summary>The FMA's five columns: A/THR, vertical, lateral, approach capability (its third row is the minimum, read
    /// apart), and the AP/FD/A/THR engagement column (live: "1 FD 2" at x 685-761).</summary>
    private static readonly (string Name, float X0, float X1)[] FmaColumns =
    {
        ("autothrust", 0, 170), ("vertical", 170, 340), ("lateral", 340, 510), ("approach", 510, 670), ("engagement", 670, PfdWidth),
    };

    /// <summary>The altitude at the window (y 389): the tape's label gives it roughly (0.35 px a foot), the drum's
    /// tens label exactly (21.75 px per 20 ft); the reading is the value with the drum's last two digits nearest
    /// the tape's.</summary>
    internal static int Altitude(int tapeHundreds, float tapeY, int tens, float tensY)
    {
        double rough = tapeHundreds * 100 + (tapeY - WindowY) / TapePxPerFoot;
        double last = tens + (tensY - WindowY) / DrumPxPerFoot;
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

    private static DrawnShape? Sprite(A350DrawCapture capture, int side, (float X, float Y) atlas) =>
        capture.Shapes?.FirstOrDefault(s => s.Image > 1 && Math.Abs(s.X0 - s.PatternX - atlas.X) < 1.5f
            && Math.Abs(s.Y0 - s.PatternY - atlas.Y) < 1.5f && ((s.X0 + s.X1) / 2 >= PfdWidth) == (side == 0));

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

        // The tape's selected altitude symbol (a box, its value at x 597) is drawn out of the tape's range too, held at the
        // screen's top (y 38; "400" at x 646 in STD) or bottom (y 742), inside the FMA's top row where the tape's clip hides
        // it: the readout at the tape's edge shows that altitude (live 2026-10-28). Magenta, the symbol is the constraint the
        // vertical mode levels at (at the hold, the SID's 5000), which the readout does not show.
        var heldSymbol = cells.Where(c => c.X is >= 585 and < 700 && (c.Y is >= 20 and < 60 || c.Y >= 700)
            && c.Colour is "cyan" or "magenta" && Digits(c.Text)).ToList();
        used.UnionWith(heldSymbol);

        // The selected altitude, read after the FMA: in range the tape's symbol; beyond the tape's range the readout at the
        // tape's top or bottom, "FL" at 587 and its number at 688 (live 2026-10-28: y 172 above, 615 below), one value.
        var selectedAltitude = cells.Where(c => !used.Contains(c) && c.Colour == "cyan" && (c.X is >= 585 and < 620 && c.Y < 640
                || c.X is >= 620 and < 720 && c.Y is >= FmaBottom and < 640))
            .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
        if (selectedAltitude.Count == 0)
            selectedAltitude = heldSymbol.Where(c => c.Colour == "cyan").ToList();
        used.UnionWith(selectedAltitude);
        var constraint = heldSymbol.Where(c => c.Colour == "magenta").Concat(cells.Where(c => !used.Contains(c) && c.Colour == "magenta"
            && c.X is >= 585 and < 620 && c.Y is >= FmaBottom and < 640 && Digits(c.Text))).ToList();
        used.UnionWith(constraint);

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
        foreach (var c in constraint)
            o.Add($"Altitude constraint: {c.Text.Trim()}, magenta");

        // Speed and attitude are textures: the speed tape's offset, the horizon under the attitude centre. On the stand
        // the tape rests on its 30 kt floor, which its offset reads as 32.
        if (PatternOf(capture, side, 75, 2285) is { } speedTape)
        {
            double knots = Math.Round(SpeedRefKnots + (speedTape.PatternY - SpeedRefPattern) / SpeedPxPerKnot, MidpointRounding.AwayFromZero);
            o.Add(knots <= SpeedFloorReads ? "Speed: 30 knots or less" : $"Speed: {knots:0} knots");
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
                o.Add(Math.Abs(bank) < 1 ? "Bank: wings level" : $"Bank: {Math.Abs(Math.Round(bank)):0} degrees {(bank > 0 ? "left" : "right")}");
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
            used.UnionWith(lsBlock);
            var ident = lsBlock.FirstOrDefault(c => c.Colour == "magenta" && c.Text.Length > 0 && c.Text.All(char.IsLetter));
            var rows = lsBlock.Where(c => c != ident && c.Text.Trim() != "nm").GroupBy(c => (int)MathF.Round(c.Y / 12)).OrderBy(g => g.Key)
                .Select(g => string.Concat(g.OrderBy(c => c.X).Select(c => c.Text))).ToList();
            string head = string.Join(" ", new[] { ident?.Text, rows.Count > 0 ? rows[0] : null }.Where(s => !string.IsNullOrEmpty(s)));
            string dme = rows.Count > 1 ? $", {rows[1]} NM" : "";
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
        // digits only repeat it.
        var tapeLabels = cells.Where(c => c.X is >= 585 and < 610 && c.Colour == "white" && c.Text.Length == 3 && c.Text.All(char.IsDigit)).ToList();
        var drumTens = cells.Where(c => c.X is >= 658 and < 676 && c.Y is >= 330 and < 460 && c.Colour == "green"
            && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        used.UnionWith(drumTens);
        used.UnionWith(cells.Where(c => c.X is >= 605 and < 658 && c.Y is >= 330 and < 460 && c.Colour == "green"));
        if (tapeLabels.Count > 0 && drumTens.Count > 0)
        {
            var tape = tapeLabels.OrderBy(c => Math.Abs(c.Y - WindowY)).First();
            var tens = drumTens.OrderBy(c => Math.Abs(c.Y - WindowY)).First();
            o.Add($"Altitude: {Altitude(int.Parse(tape.Text), tape.Y, int.Parse(tens.Text), tens.Y)} feet");
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
                ? $"{label.Text}: {A350DrawLayout.Speak(value)}"
                : "Baro: " + A350DrawLayout.Speak(baro));
        }

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
            o.Add($"Heading: {Math.Round(heading) % 360:0}");

            static string Degrees(double d) => $"{((int)Math.Round(d) % 360 + 360) % 360:000}";
            float At(DrawnShape sh) => (sh.X0 + sh.X1) / 2 - shift;
            // The selected heading: the cyan number beside the scale while the target is off it (taken above; the bug
            // then waits at the scale's edge), else the bug (shown in HDG, not in LOC or NAV).
            if (offScaleHeading.Count > 0)
                o.Add($"Selected heading: {offScaleHeading[0].Text}, cyan");
            else if (Sprite(capture, side, SelectedHeadingAtlas) is { } bug)
                o.Add($"Selected heading: {Degrees(heading + (At(bug) - HeadingIndexX) / HeadingPxPerDegree)}, cyan");
            if (Sprite(capture, side, TrackAtlas) is { } track)
                o.Add($"Track: {Degrees(heading + (At(track) - HeadingIndexX) / HeadingPxPerDegree)}");
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

}
