namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The ND, FCU, RMP and standby displays read by name (pure), from live draws on the stand (2026-10-27). Scale
/// and tape labels are not read as text; what is not named reads as drawn, with its colour. The ND map's own
/// symbols (route, waypoints, traffic) are read in flight.
/// </summary>
public static class A350GroundDisplays
{
    private const float HalfWidth = 836;

    /// <summary>Draws repeat a text in place (the ND's "PPOS" twice): one of each.</summary>
    private static List<DrawnText> Distinct(IEnumerable<DrawnText> texts) =>
        texts.Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .GroupBy(t => (t.Text.Trim(), (int)MathF.Round(t.X), (int)MathF.Round(t.Y))).Select(g => g.First()).ToList();

    private static List<DrawnCell> Cells(IEnumerable<DrawnText> texts) => A350DrawLayout.Rows(Distinct(texts)).SelectMany(r => r).ToList();

    private static string Speak(IEnumerable<DrawnCell> cells) => A350DrawLayout.Speak(cells.OrderBy(c => c.X).ToList());

    private static IEnumerable<string> Rest(IEnumerable<DrawnCell> cells, HashSet<DrawnCell> used) =>
        cells.Where(c => !used.Contains(c)).GroupBy(c => (int)MathF.Floor(c.Y / 12 + 0.5f)).OrderBy(g => g.Key).Select(g => Speak(g));

    private static bool Number(DrawnCell c) => c.Text.Length > 0 && c.Text.All(ch => char.IsDigit(ch) || ch == '.');

    /// <summary>A side's ND (EFIS#0's left half, EFIS#1's right half), in its own coordinates.</summary>
    public static IReadOnlyList<string> ReadNd(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? 0 : HalfWidth;
        var cells = Cells(A350DrawLayout.Region(capture.Texts, A350DrawLayout.EfisNd(side)).Select(t => t with { X = t.X - shift })
            .Where(t => t.X is >= 0 and < HalfWidth));
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();
        void Field(string label, Func<DrawnCell, bool> where)
        {
            var hit = cells.Where(c => !used.Contains(c) && where(c)).ToList();
            if (hit.Count == 0) return;
            used.UnionWith(hit);
            o.Add($"{label}: {Speak(hit)}");
        }
        Field("Ground speed", c => Math.Abs(c.X - 122) <= 12 && Math.Abs(c.Y - 35) <= 8);
        Field("True airspeed", c => Math.Abs(c.X - 175) <= 12 && Math.Abs(c.Y - 35) <= 8);
        Field("Wind", c => c.X is >= 60 and < 130 && Math.Abs(c.Y - 63) <= 8);
        Field("To waypoint", c => c.X >= 560 && Math.Abs(c.Y - 36) <= 8);
        Field("Vertical display range", c => c.Y is >= 775 and < 800 && c.Colour == "cyan" && Number(c));
        var altitudes = cells.Where(c => c.X is >= 90 and < 150 && c.Y >= 810 && Number(c)).OrderBy(c => c.Y).ToList();
        if (altitudes.Count > 0)
        {
            used.UnionWith(altitudes);
            o.Add("Vertical display altitudes: " + string.Join(" ", altitudes.Select(c => c.Text)) + ", " + altitudes[0].Colour);
        }
        o.AddRange(Rest(cells, used));
        return o;
    }

    public static IReadOnlyList<string> ReadFcu(A350DrawCapture capture)
    {
        // The EFIS control panels' legends (y 255-405) are buttons the panels already read.
        var cells = Cells(capture.Texts).Where(c => c.Y is < 255 or >= 405).ToList();
        var used = new HashSet<DrawnCell>(cells.Where(c => c.Text is "SPD" or "HDG" or "ALT" or "V/S"));
        var o = new List<string>();
        void Field(string label, float x, float y)
        {
            var hit = cells.Where(c => !used.Contains(c) && Math.Abs(c.X - x) <= 12 && Math.Abs(c.Y - y) <= 8).ToList();
            if (hit.Count == 0) return;
            used.UnionWith(hit);
            o.Add($"{label}: {Speak(hit)}");
        }
        Field("Speed", 158, 105); Field("Heading", 455, 105); Field("Altitude", 241, 225); Field("Vertical speed", 275, 225);
        foreach (var (label, x0, x1) in new[] { ("Captain baro", 0f, 150f), ("First officer baro", 150f, 400f) })
        {
            var baro = cells.Where(c => !used.Contains(c) && c.X >= x0 && c.X < x1 && c.Y is >= 420 and < 520).OrderBy(c => c.Y).ToList();
            if (baro.Count == 0) continue;
            used.UnionWith(baro);
            o.Add($"{label}: " + string.Join(" ", baro.Select(c => c.Text)) + ", " + baro[^1].Colour);
        }
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>An RMP's radio page: each radio named in the middle column, its active frequency left, its
    /// standby right (cyan: the one the knob tunes).</summary>
    public static IReadOnlyList<string> ReadRmp(A350DrawCapture capture)
    {
        var cells = Cells(capture.Texts);
        var used = new HashSet<DrawnCell>(cells.Where(c => c.Text == "STBY" && c.Y < 120));
        var o = new List<string>();
        foreach (var name in cells.Where(c => c.X is >= 400 and < 600 && c.Y >= 120).OrderBy(c => c.Y).ToList())
        {
            var active = cells.FirstOrDefault(c => !used.Contains(c) && c.X < 400 && Math.Abs(c.Y - name.Y) <= 12);
            var standby = cells.FirstOrDefault(c => !used.Contains(c) && c.X >= 600 && Math.Abs(c.Y - name.Y) <= 12);
            if (active == null && standby == null) continue;
            used.Add(name);
            var parts = new List<string>();
            if (active != null) { used.Add(active); parts.Add($"active {active.Text}, {active.Colour}"); }
            if (standby != null) { used.Add(standby); parts.Add($"standby {standby.Text}, {standby.Colour}"); }
            o.Add($"{name.Text}: " + string.Join("; ", parts));
        }
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>The overhead BAT display (the battery the BAT selector picks): seven-segment digits, the decimal
    /// point drawn as its own text before the last digit (live: "282" and "." for 28.2 V).</summary>
    public static IReadOnlyList<string> ReadBat(A350DrawCapture capture)
    {
        var cells = Cells(capture.Texts);
        var digits = cells.FirstOrDefault(c => c.Text.All(char.IsDigit));
        if (digits == null) return new[] { "Battery voltage display: blank" };
        var text = cells.Any(c => c.Text == ".") && digits.Text.Length > 1 ? digits.Text[..^1] + "." + digits.Text[^1] : digits.Text;
        return new[] { $"Battery voltage: {text} V, {digits.Colour}" };
    }

    private static void Position(List<DrawnCell> cells, HashSet<DrawnCell> used, List<string> o)
    {
        foreach (var p in cells.Where(c => c.Text.Contains('|') && c.Text.Contains('/')))
        {
            used.Add(p);
            o.Add($"Position: {p.Text.Replace('|', ' ').Replace('/', ' ')}, {p.Colour}");
        }
        foreach (var s in cells.Where(c => c.Text.StartsWith("GP", StringComparison.Ordinal) || c.Text == "IRS"))
        {
            used.Add(s);
            o.Add($"Position source: {s.Text}, {s.Colour}");
        }
    }

    /// <summary>The standby flight display: baro, heading (scale labels 2.3 px a degree, index at x 145),
    /// position and its source; flags as drawn. Its speed and altitude tapes are read in flight.</summary>
    public static IReadOnlyList<string> ReadSfd(A350DrawCapture capture)
    {
        var cells = Cells(capture.Texts);
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();
        foreach (var b in cells.Where(c => c.Colour == "cyan" && Number(c)))
        {
            used.Add(b);
            o.Add($"Baro: {b.Text}, cyan");
        }
        var scale = cells.Where(c => c.Y is >= 230 and < 250 && c.Colour == "white" && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        if (scale.Count > 0)
        {
            var nearest = scale.OrderBy(c => Math.Abs(c.X - 145)).First();
            o.Add($"Heading: {(int.Parse(nearest.Text) * 10 + (145 - nearest.X) / 2.3 + 360) % 360:0}");
        }
        Position(cells, used, o);
        // Tape, drum and scale numbers, and the vertical source letters, are not text to read.
        used.UnionWith(cells.Where(c => Number(c) || c.Text.Length == 1));
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>The standby navigation display: ground speed, heading source, position and its source.</summary>
    public static IReadOnlyList<string> ReadSnd(A350DrawCapture capture)
    {
        var cells = Cells(capture.Texts);
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();
        var gs = cells.FirstOrDefault(c => c.Text == "GS");
        var gsValue = gs == null ? null : cells.FirstOrDefault(c => c != gs && Math.Abs(c.Y - gs.Y) <= 6 && c.X > gs.X && c.X - gs.X < 60);
        if (gs != null && gsValue != null)
        {
            used.Add(gs); used.Add(gsValue);
            o.Add($"Ground speed: {gsValue.Text}, {gsValue.Colour}");
        }
        foreach (var s in cells.Where(c => c.Y < 60 && c.Text is "IR1" or "IR2" or "IR3"))
        {
            used.Add(s);
            o.Add($"Heading source: {s.Text}, {s.Colour}");
        }
        Position(cells, used, o);
        used.UnionWith(cells.Where(Number));   // the compass rose's labels
        o.AddRange(Rest(cells, used));
        return o;
    }
}
