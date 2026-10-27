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
    private const float PfdWidth = 836, FmaBottom = 100, BottomLine = 860;
    private const float DrumWindowY = 422, DrumPxPerFoot = 21.75f / 20, HeadingIndexX = 359, HeadingPxPerDegree = 7;

    /// <summary>The FMA's five columns: A/THR, vertical, lateral, approach capability, and the AP/FD/A/THR
    /// engagement column (live: "1 FD 2" at x 685-761).</summary>
    private static readonly (string Name, float X0, float X1)[] FmaColumns =
    {
        ("autothrust", 0, 170), ("vertical", 170, 340), ("lateral", 340, 510), ("approach", 510, 640), ("engagement", 640, PfdWidth),
    };

    /// <summary>The altitude a drum shows: the hundreds from its big digits, plus the tens label's value at
    /// the window (a label below the window is a value already passed).</summary>
    internal static int DrumAltitude(int hundreds, int label, float labelY) =>
        (int)Math.Round(hundreds * 100 + label + (labelY - DrumWindowY) / DrumPxPerFoot);

    public static IReadOnlyList<string> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? PfdWidth : 0;
        var texts = A350DrawLayout.Region(capture.Texts, A350DrawLayout.EfisPfd(side))
            .Select(t => t with { X = t.X - shift }).ToList();
        var cells = A350DrawLayout.Rows(texts).SelectMany(r => r).ToList();
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();

        // The altitude tape's scale labels reach up into the FMA strip: never read as text.
        used.UnionWith(cells.Where(c => c.X is >= 585 and < 610 && c.Colour == "white" && c.Text.Length == 3 && c.Text.All(char.IsDigit)));

        // The FMA strip, one column at a time.
        foreach (var (name, x0, x1) in FmaColumns)
        {
            var col = cells.Where(c => !used.Contains(c) && c.Y < FmaBottom && c.X >= x0 && c.X < x1).ToList();
            if (col.Count == 0) continue;
            used.UnionWith(col);
            o.Add($"FMA {name}: " + string.Join("; ", A350DrawLayout.Rows(col.Select(ToText)).Select(A350DrawLayout.Speak)));
        }

        // The speed tape: its target in cyan.
        foreach (var c in cells.Where(c => c.X < 250 && c.Y >= FmaBottom && c.Y < BottomLine && c.Colour == "cyan"))
        {
            used.Add(c);
            o.Add($"Selected speed: {c.Text}, cyan");
        }

        // The altitude tape: scale labels (skipped), target (cyan), drum (read as the altitude), QNH.
        var drumTens = cells.Where(c => c.X is >= 658 and < 676 && c.Y is >= 330 and < 460 && c.Colour == "green"
            && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        var drumBig = cells.Where(c => c.X is >= 605 and < 658 && c.Y is >= 330 and < 460 && c.Colour == "green").ToList();
        if (drumTens.Count > 0 && drumBig.Count > 0)
        {
            used.UnionWith(drumTens);
            used.UnionWith(drumBig);
            var tens = drumTens.OrderBy(c => Math.Abs(c.Y - DrumWindowY)).First();
            var middle = drumBig.GroupBy(c => (int)MathF.Round(c.Y)).OrderBy(g => Math.Abs(g.Key - 393 - (DrumWindowY - 422))).First()
                .OrderBy(c => c.X).Where(c => c.X >= 640);
            if (int.TryParse(string.Concat(middle.Select(c => c.Text)), out int hundreds))
                o.Add($"Altitude: {DrumAltitude(hundreds, int.Parse(tens.Text), tens.Y)} feet");
        }
        foreach (var c in cells.Where(c => c.X is >= 585 and < 620 && c.Y < 640 && c.Colour == "cyan" && !used.Contains(c)))
        {
            used.Add(c);
            o.Add($"Selected altitude: {c.Text}, cyan");
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
