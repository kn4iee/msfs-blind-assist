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

    /// <summary>The FMA's five columns: A/THR, vertical, lateral, approach capability, and the AP/FD/A/THR
    /// engagement column (live: "1 FD 2" at x 685-761).</summary>
    private static readonly (string Name, float X0, float X1)[] FmaColumns =
    {
        ("autothrust", 0, 170), ("vertical", 170, 340), ("lateral", 340, 510), ("approach", 510, 640), ("engagement", 640, PfdWidth),
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

        // The selected altitude, at the top of the altitude tape, reaches up into the FMA strip too: taken out
        // first, read after the FMA.
        var selectedAltitude = cells.Where(c => c.X is >= 585 and < 620 && c.Y < 640 && c.Colour == "cyan").ToList();
        used.UnionWith(selectedAltitude);

        // The FMA strip, one column at a time.
        foreach (var (name, x0, x1) in FmaColumns)
        {
            var col = cells.Where(c => !used.Contains(c) && c.Y < FmaBottom && c.X >= x0 && c.X < x1).ToList();
            if (col.Count == 0) continue;
            used.UnionWith(col);
            var rows = A350DrawLayout.Rows(col.Select(ToText));
            o.Add($"FMA {name}: " + (rows.SelectMany(r => r).Select(c => c.Colour).Distinct().Count() == 1
                ? string.Join("; ", rows.Select(r => string.Join(" ", r.Select(c => c.Text)))) + ", " + rows[0][0].Colour
                : string.Join("; ", rows.Select(A350DrawLayout.Speak))));
        }

        foreach (var c in selectedAltitude) o.Add($"Selected altitude: {c.Text}, cyan");

        // Speed and attitude are textures: the speed tape's offset, the horizon under the attitude centre.
        if (PatternOf(capture, side, 75, 2285) is { } speedTape)
            o.Add($"Speed: {Math.Round(SpeedRefKnots + (speedTape.PatternY - SpeedRefPattern) / SpeedPxPerKnot, MidpointRounding.AwayFromZero):0} knots");
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

        // The speed tape: its target in cyan.
        foreach (var c in cells.Where(c => c.X < 250 && c.Y >= FmaBottom && c.Y < BottomLine && c.Colour == "cyan"))
        {
            used.Add(c);
            o.Add($"Selected speed: {c.Text}, cyan");
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
        var baro = cells.Where(c => c.X is >= 580 and < 720 && c.Y is >= 640 and < 680).ToList();
        if (baro.Count > 0)
        {
            used.UnionWith(baro);
            var label = baro.FirstOrDefault(c => c.Text is "QNH" or "QFE");
            var value = baro.Where(c => c != label).ToList();
            o.Add(label != null && value.Count > 0
                ? $"{label.Text}: {A350DrawLayout.Speak(value)}"
                : "Baro: " + A350DrawLayout.Speak(baro));
        }

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
        }

        // Everything else as drawn: flags, messages, the line under the heading scale.
        foreach (var row in A350DrawLayout.Rows(cells.Where(c => !used.Contains(c)).Select(ToText)))
            o.Add(A350DrawLayout.Speak(row));
        return o;
    }

    private static DrawnText ToText(DrawnCell c) => new(c.Text, c.X, c.Y, c.X, c.Y, 0, ColourOf(c.Colour));

    private static A350Rgba ColourOf(string name) => name switch
    {
        "green" => new A350Rgba(0.15f, 0.85f, 0.1f, 1), "amber" => new A350Rgba(1, 0.55f, 0, 1),
        "cyan" => new A350Rgba(0.4f, 0.86f, 1, 1), "red" => new A350Rgba(1, 0.1f, 0.1f, 1),
        "magenta" => new A350Rgba(1, 0.4f, 1, 1), _ => new A350Rgba(1, 1, 1, 1),
    };
}
