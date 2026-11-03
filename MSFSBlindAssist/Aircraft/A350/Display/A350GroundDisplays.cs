using System.Globalization;
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

    internal static List<DrawnCell> Cells(IEnumerable<DrawnText> texts) => A350DrawLayout.Rows(Distinct(texts)).SelectMany(r => r).ToList();

    private static string Speak(IEnumerable<DrawnCell> cells) => A350DrawLayout.Speak(cells.OrderBy(c => c.X).ToList());

    private static IEnumerable<string> Rest(IEnumerable<DrawnCell> cells, HashSet<DrawnCell> used) =>
        cells.Where(c => !used.Contains(c)).GroupBy(c => (int)MathF.Floor(c.Y / 12 + 0.5f)).OrderBy(g => g.Key).Select(g => Speak(g));

    private static bool Number(DrawnCell c) => c.Text.Length > 0 && c.Text.All(ch => char.IsDigit(ch) || ch == '.');

    /// <summary>The TO waypoint's name as a side's ND draws it (<see cref="ReadNd"/>'s "To waypoint" line, without its colour and
    /// the rest of its block), or null when it draws none (the ROSE LS receiver's corner is no waypoint).</summary>
    public static string? ToWaypointName(A350DrawCapture capture, int side)
    {
        const string Prefix = "To waypoint: ";
        var line = ReadNd(capture, side).FirstOrDefault(l => l.StartsWith(Prefix, StringComparison.Ordinal));
        if (line == null) return null;
        string name = line[Prefix.Length..].Split("; ")[0];
        int colour = name.LastIndexOf(", ", StringComparison.Ordinal);
        return colour > 0 ? name[..colour] : name;
    }

    /// <summary>A side's ND (EFIS#0's right half, EFIS#1's left half), in its own coordinates.</summary>
    /// <param name="skip">Texts another reader has taken (the ND's controls, <see cref="A350NdPage"/>).</param>
    public static IReadOnlyList<string> ReadNd(A350DrawCapture capture, int side, Func<DrawnText, bool>? skip = null)
    {
        float shift = side == 0 ? HalfWidth : 0;
        var texts = A350DrawLayout.Region(A350DrawLayout.SplitEfis(capture.Texts, side).Nd.Where(t => skip?.Invoke(t) != true), A350DrawLayout.EfisNd(side))
            .Select(t => t with { X = t.X - shift }).Where(t => t.X is >= 0 and < HalfWidth).ToList();
        // The airport map at a zoom range (A350NdMap): read by kind, before the rows; its texts are not read again.
        var mapUsed = new HashSet<DrawnText>();
        var map = A350NdMap.Read(texts, mapUsed);
        var cells = Cells(texts.Where(t => !mapUsed.Contains(t)));
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();
        var trafficLines = new List<string>();
        void Field(string label, Func<DrawnCell, bool> where)
        {
            var hit = cells.Where(c => !used.Contains(c) && where(c)).ToList();
            if (hit.Count == 0) return;
            used.UnionWith(hit);
            o.Add($"{label}: {Speak(hit)}");
        }
        // GS and TAS, green; with the airport map (zoom range, live 2026-10-28) their white labels are drawn too and GS's
        // value moves left to x 64 (else x 122). The labels are not read.
        used.UnionWith(cells.Where(c => c.Colour == "white" && c.Text is "GS" or "TAS" && Math.Abs(c.Y - 35) <= 8));
        var drawn = A350DrawLayout.SplitEfis(capture.Texts, side).Nd;
        foreach (var traffic in cells.Where(c => A350NdMap.TrafficText.IsMatch(c.Text)).ToList())
        {
            used.Add(traffic);
            // Where it was drawn, for what lies beside it: the arrow is in the capture's coordinates, the cell in the ND's.
            var figure = drawn.FirstOrDefault(t => t.Text.Trim() == traffic.Text && Math.Abs(t.X - shift - traffic.X) <= 2 && Math.Abs(t.Y - traffic.Y) <= 12);
            if (figure == null)
            {
                trafficLines.Add($"Traffic {A350NdMap.TrafficAltitude(traffic.Text)}, {traffic.Colour}");
                continue;
            }
            var ident = A350NdMap.TrafficIdentOf(drawn, figure);
            foreach (var part in ident.Texts)
                used.UnionWith(cells.Where(c => c.Text == part.Text.Trim() && Math.Abs(c.X - (part.X - shift)) <= 2 && Math.Abs(c.Y - part.Y) <= 12));
            var (head, tail) = A350NdMap.TrafficWords(ident, capture.Shapes, figure);
            trafficLines.Add($"Traffic {head}{tail}, {traffic.Colour}");
        }
        Field("Ground speed", c => c.Colour == "green" && Math.Abs(c.Y - 35) <= 8 && (Math.Abs(c.X - 122) <= 12 || Math.Abs(c.X - 64) <= 12));
        // The BTV block at the top left (ND::DrawBTVInfo, live 2026-10-30), where the wind is drawn without it.
        foreach (var (name, y) in new[] { ("RWY", 64f), ("EXIT", 89f), ("ROT", 112f), ("TURNAROUND", 136f) })
        {
            var row = cells.Where(c => !used.Contains(c) && Math.Abs(c.Y - y) <= 8 && c.X < 400).OrderBy(c => c.X).ToList();
            if (row.Count == 0 || row[0].Text.Trim() != name) continue;
            used.UnionWith(row);
            string colour = row.Skip(1).FirstOrDefault(c => c.Colour is not ("white" or "cyan"))?.Colour ?? row[0].Colour;
            o.Add(BtvRow(name, row.SelectMany(c => c.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList(), colour));
        }
        Field("True airspeed", c => c.Colour == "green" && Math.Abs(c.X - 175) <= 12 && Math.Abs(c.Y - 35) <= 8);
        Field("Wind", c => c.X is >= 60 and < 130 && Math.Abs(c.Y - 63) <= 8);
        // The procedure at the top (ND::DrawApproachInfo, x 270: the SID on the stand, live 2026-10-30) and the chronometer
        // at the bottom left (ND::DrawChrono, x 57 and 114: minutes and seconds, hours and minutes past the hour).
        Field("Procedure", c => c.X is >= 200 and < 420 && Math.Abs(c.Y - 35) <= 8);
        Field("Chrono", c => c.Colour == "green" && c.X is >= 40 and < 130 && Math.Abs(c.Y - 660) <= 8 && Number(c));
        Field("Radar mode", c => A350NdMap.IsRadarMode(c.Text));
        // The ANF's two-line flag at the top: the airport shown is not in the flight plan (PLAN).
        var notInPlan = cells.Where(c => c.Text is "ARPT NOT IN" or "ACTIVE F-PLN").OrderBy(c => c.Y).ToList();
        if (notInPlan.Count == 2)
        {
            used.UnionWith(notInPlan);
            o.Add($"ARPT NOT IN ACTIVE F-PLN, {notInPlan[0].Colour}");
        }
        o.AddRange(map);
        // The same corner on ROSE LS (live on the arrival to OMDB, 2026-10-30): the receiver (white), its frequency ("110", ".",
        // "10"), the course under it ("CRS" and its value), the ident under that.
        if (cells.FirstOrDefault(c => !used.Contains(c) && c.Text == "CRS" && c.X >= 560 && Math.Abs(c.Y - 69) <= 8) is { } courseName)
        {
            used.Add(courseName);
            List<DrawnCell> Row(float y) => cells.Where(c => !used.Contains(c) && c.X >= 560 && Math.Abs(c.Y - y) <= 8).OrderBy(c => c.X).ToList();
            List<DrawnCell> top = Row(36), course = Row(69), ident = Row(100);
            used.UnionWith(top.Concat(course).Concat(ident));
            var value = top.Where(c => c.Colour != "white").ToList();
            var said = new List<string>();
            said.AddRange(top.Where(c => c.Colour == "white").Select(c => c.Text));
            if (value.Count > 0) said.Add(string.Concat(value.Select(c => c.Text)));
            if (course.Count > 0) said.Add($"course {string.Concat(course.Select(c => c.Text))}");
            said.AddRange(ident.Select(c => c.Text));
            o.Add($"Receiver: {string.Join(", ", said)}, {value.Concat(course).Concat(ident).FirstOrDefault()?.Colour ?? courseName.Colour}");
        }
        // The TO waypoint block, top right (live in the climb): the name (white) and the track to it (green, x 759), the
        // distance a line under ("6." and a smaller "2", then a cyan NM), the ETA under that. One line, as a pilot reads it.
        var toName = cells.Where(c => !used.Contains(c) && c.X is >= 560 and < 740 && Math.Abs(c.Y - 36) <= 8).ToList();
        if (toName.Count > 0)
        {
            used.UnionWith(toName);
            var parts = new List<string> { Speak(toName) };
            var track = cells.Where(c => !used.Contains(c) && c.X >= 740 && Math.Abs(c.Y - 36) <= 8).OrderBy(c => c.X).ToList();
            if (track.Count > 0)
            {
                used.UnionWith(track);
                parts.Add($"track {string.Concat(track.Select(c => c.Text))}°, {track[0].Colour}");
            }
            var distance = cells.Where(c => !used.Contains(c) && c.X >= 680 && Math.Abs(c.Y - 69) <= 8).OrderBy(c => c.X).ToList();
            var digits = distance.Where(c => c.Text != "NM").ToList();
            if (digits.Count > 0)
            {
                used.UnionWith(distance);
                parts.Add($"{string.Concat(digits.Select(c => c.Text))} NM, {digits[0].Colour}");
            }
            var eta = cells.Where(c => !used.Contains(c) && c.X >= 700 && Math.Abs(c.Y - 100) <= 8).OrderBy(c => c.X).ToList();
            if (eta.Count > 0)
            {
                used.UnionWith(eta);
                parts.Add($"ETA {string.Concat(eta.Select(c => c.Text))}, {eta[0].Colour}");
            }
            o.Add("To waypoint: " + string.Join("; ", parts));
        }
        // The selected altitude beyond the vertical display's altitude range: a cyan value at its top left (y 786) or bottom
        // left (y 1010), "FL 400" in STD (live 2026-10-28); it is neither a range nor an altitude label.
        Field("Vertical display selected altitude", c => c.Colour == "cyan" && Math.Abs(c.X - 105) <= 12
            && (Math.Abs(c.Y - 786) <= 8 || Math.Abs(c.Y - 1010) <= 8));
        var distances = cells.Where(c => !used.Contains(c) && c.Y is >= 775 and < 800 && c.Colour == "cyan" && Number(c)).OrderBy(c => c.X).ToList();
        Field("Vertical display range", c => c.Y is >= 775 and < 800 && c.Colour == "cyan" && Number(c));
        // The labels run up to the range line and past it (the top one at y 799 on the stand at VCBI, 2026-10-28; y 773 at FL300, 2026-10-30).
        var altitudes = cells.Where(c => !used.Contains(c) && c.X is >= 90 and < 150 && (c.Y >= 810 || c.Y >= 770 && c.Colour == "white")
            && Number(c)).OrderBy(c => c.Y).ToList();
        if (altitudes.Count > 0)
        {
            used.UnionWith(altitudes);
            // In flight levels the scale says so with a white "FL" left of its labels.
            var unit = cells.FirstOrDefault(c => !used.Contains(c) && c.Text == "FL" && c.Colour == "white" && c.X < 90 && c.Y >= 810);
            if (unit != null) used.Add(unit);
            o.Add("Vertical display altitudes: " + (unit != null ? "FL " : "") + string.Join(" ", altitudes.Select(c => c.Text)) + ", " + altitudes[0].Colour);
        }
        var scales = VerticalScales.Of(distances, altitudes, cells.Any(c => c.Text == "FL" && c.Colour == "white" && c.X < 90 && c.Y >= 810));
        // The safe altitude (MSA or MORA): in the display a line of its colour at its height, its name about 8.5 px over it wherever it
        // lies (y 792 on the stand at VCBI, 874 at OMDB, 905 on the arrival, live 2026-10-30), never a waypoint's; under the display, with
        // no line, its figure in its colour just above its name ("3" and "8"), or just under it low down. The line comes first and the
        // figure keeps to the name's colour: a runway's ident by the name ("22", white) or a constraint ("500", green) is no figure of its.
        foreach (var name in cells.Where(c => !used.Contains(c) && c.Text is "MSA" or "MORA" && c.Y >= 770).ToList())
        {
            used.Add(name);
            var line = scales == null ? null : (capture.Shapes ?? Array.Empty<DrawnShape>()).FirstOrDefault(s => s.Kind == "stroke" && s.X1 - s.X0 >= VerticalScales.MinLinePixels
                && s.Y1 - s.Y0 <= VerticalScales.MaxLineHeight && s.X0 - shift <= name.X && s.X1 - shift >= name.X && Math.Abs((s.Y0 + s.Y1) / 2 - name.Y) <= 20
                && A350Rgba.NameOf(s.Colour) == name.Colour);
            if (line != null)
            {
                o.Add($"{name.Text}: about {scales!.FeetAt((line.Y0 + line.Y1) / 2)} feet, {name.Colour}");
                continue;
            }
            var value = cells.Where(c => !used.Contains(c) && c.Colour == name.Colour && c.Text.Length > 0 && c.Text.All(char.IsDigit)
                && Math.Abs(name.Y - c.Y) is > 0 and <= 40 && Math.Abs(c.X - name.X) <= 50).OrderBy(c => c.X).ToList();
            used.UnionWith(value);
            o.Add(value.Count > 0 ? $"{name.Text}: {string.Concat(value.Select(c => c.Text))}, {value[0].Colour}" : $"{name.Text}, {name.Colour}");
        }
        // A cell keeps neither its place in the draw order nor whether the clip hid it, so each is found again where it was drawn.
        int DrawnAt(DrawnCell c)
        {
            for (int i = 0; i < drawn.Count; i++)
                if (Math.Abs(drawn[i].X - shift - c.X) < 0.5f && Math.Abs(drawn[i].Y - c.Y) < 0.5f && drawn[i].Text.Trim() == c.Text) return i;
            return -1;
        }
        // The vertical display's waypoints, each name beside its symbol, on the two scales. Its own texts are drawn after its range
        // labels: the range map's names drawn behind it, under the map, come before them (MCT and BASUR at FL300, MIN on the arrival, live
        // 2026-10-30). A text the clip hides is off the display (ODLAL past 40 NM on the arrival).
        if (scales != null)
        {
            int labels = distances.Select(DrawnAt).Where(i => i >= 0).DefaultIfEmpty(int.MaxValue).Min();
            bool Own(DrawnCell c) => DrawnAt(c) > labels;
            bool OnDisplay(DrawnCell c) => DrawnAt(c) is var i && i > labels && !drawn[i].Clipped;
            foreach (var name in cells.Where(c => !used.Contains(c) && scales.HoldsName(c) && OnDisplay(c)).OrderBy(c => c.X).ToList())
            {
                used.Add(name);
                // The runway: the airport's name over the runway's ident at one x (VCBI over 22 on the stand), above its two white lines.
                if (name.Colour == "white" && cells.FirstOrDefault(c => !used.Contains(c) && c.Colour == "white" && VerticalScales.RunwayIdent.IsMatch(c.Text)
                        && Math.Abs(c.X - name.X) <= 2 && Math.Abs(c.Y - name.Y - VerticalScales.IdentUnderName) <= 3 && Own(c)) is { } ident)
                {
                    used.Add(ident);
                    o.Add($"Vertical display: {name.Text} {ident.Text}, {name.Colour}"
                        + (VerticalScales.RunwayAt(name, ident, capture.Shapes, shift) is { } at ? $": {scales.NmAt(at.X)}, {scales.FeetAt(at.Y)} feet" : ""));
                    continue;
                }
                string shown = A350NdMap.TimeMarker.Match(name.Text) is { Success: true } marker ? "Time marker " + marker.Groups[1].Value : name.Text;
                o.Add($"Vertical display: {shown}, {name.Colour}: {scales.NmAt(name.X - VerticalScales.NameRightOfSymbol)}, {scales.FeetAt(name.Y - VerticalScales.NameUnderSymbol)} feet");
            }
        }
        // What the clip hides in the vertical display is not on the screen, so it is not spoken, as on the PFD; the map's own drawing of a
        // name still reads (ODLAL on the arrival, the plan's OMDB 12L).
        used.UnionWith(cells.Where(c => !used.Contains(c) && c.Y is >= VerticalScales.PlotTop and < VerticalScales.PlotBottom && DrawnAt(c) is var i and >= 0 && drawn[i].Clipped));
        o.AddRange(trafficLines);
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>The vertical display's two scales, read from their labels (live 2026-10-30, 40 NM: "0" to "40" at x 175 to 707; "0" to "15000" at y 971
    /// to 784, the tick about 10 px above its label's y). <see cref="EndX"/> is the last range label's.</summary>
    private sealed record VerticalScales(float ZeroX, float PixelsPerNm, float ZeroTickY, float PixelsPerFoot, float EndX)
    {
        internal const float NameRightOfSymbol = 6, NameUnderSymbol = 16, MinLinePixels = 100, IdentUnderName = 20;

        /// <summary>A line is a level stroke: every MSA line drawn is 4 px tall, and a step drawn as one path is no level.</summary>
        internal const float MaxLineHeight = 6;
        internal const float PlotTop = 800, PlotBottom = 1000;
        private const float TickOverLabel = 10;

        /// <summary>A runway's ident under the airport's name: "22", "12L".</summary>
        internal static readonly System.Text.RegularExpressions.Regex RunwayIdent = new(@"^\d{2}[LCR]?$", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>The runway under its label: the left end of its two white lines and their middle, the field's height (live on the
        /// stand at VCBI, 2026-10-28: the name 10.5 px right of their left end, the ident 8.5 px over the upper line, the lines 10 px
        /// apart; the same over OMDB 12L on the arrival); null without both lines (no shapes recorded): one alone, or one stroked twice
        /// as the draw strokes some lines, lies 5 px off the field's height, about 400 ft at 40 NM.</summary>
        public static (float X, float Y)? RunwayAt(DrawnCell name, DrawnCell ident, IReadOnlyList<DrawnShape>? shapes, float shift)
        {
            const float LeftOfName = 10.5f, LinesApart = 10;
            var lines = (shapes ?? Array.Empty<DrawnShape>()).Where(s => s.Kind == "stroke" && A350Rgba.NameOf(s.Colour) == "white" && s.Y1 - s.Y0 <= 4
                && Math.Abs(name.X - (s.X0 - shift) - LeftOfName) <= 3 && s.Y0 - ident.Y is >= 4 and <= 24).ToList();
            if (lines.Count < 2 || Math.Abs(lines.Max(s => s.Y0) - lines.Min(s => s.Y0) - LinesApart) > 3) return null;
            return (lines.Min(s => s.X0) - shift, (lines.Min(s => s.Y0) + lines.Max(s => s.Y1)) / 2);
        }

        /// <summary>A scale label as a number another program wrote (CORE-17): the A350 draws "2.5" and "7.5" whatever the PC's culture.</summary>
        private static double? Value(DrawnCell c, double scale) =>
            MSFSBlindAssist.Utils.NumberParsing.TryParseExternal(c.Text, out double v) ? v * scale : null;

        public static VerticalScales? Of(IReadOnlyList<DrawnCell> distances, IReadOnlyList<DrawnCell> altitudes, bool flightLevels)
        {
            if (distances.Count < 2 || altitudes.Count < 2) return null;
            var (d0, d1) = (distances[0], distances[^1]);
            var (a1, a0) = (altitudes[0], altitudes[^1]);   // top down: the highest first
            double feet = flightLevels ? 100 : 1;
            if (Value(d0, 1) is not { } nm0 || Value(d1, 1) is not { } nm1 || Value(a0, feet) is not { } ft0 || Value(a1, feet) is not { } ft1 || nm1 <= nm0 || ft1 <= ft0)
                return null;
            float perNm = (float)((d1.X - d0.X) / (nm1 - nm0)), perFoot = (float)((a0.Y - a1.Y) / (ft1 - ft0));
            return new VerticalScales(d0.X - (float)(nm0 * perNm), perNm, a0.Y - TickOverLabel + (float)(ft0 * perFoot), perFoot, d1.X);
        }

        /// <summary>A waypoint's or a pseudo waypoint's name inside the plot, its symbol within the range. A lone letter is a mark of its
        /// own, as on the range map (a magenta D in its circle on the arrival's path); a runway's ident reads with the airport's name over
        /// it; red and amber are the ND's flags.</summary>
        public bool HoldsName(DrawnCell c) => c.Y is >= PlotTop and < PlotBottom && c.X >= ZeroX && c.X - NameRightOfSymbol <= EndX
            && c.Colour is not ("red" or "amber") && !c.Text.Contains(' ') && !RunwayIdent.IsMatch(c.Text)
            && (c.Text.Length > 1 && c.Text.Any(char.IsLetter) || A350NdMap.TimeMarker.IsMatch(c.Text));

        public string NmAt(float x)
        {
            double nm = Math.Max(0, (x - ZeroX) / PixelsPerNm);
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(nm >= 10 ? Math.Round(nm) : Math.Round(nm, 1)):0.#} NM");
        }

        public string FeetAt(float y) =>
            ((int)(Math.Round((ZeroTickY - y) / PixelsPerFoot / 100) * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A row of the ND's BTV block by name, from its words as drawn: "RWY 12L - 3900 M - WET-5", "EXIT M9 2087 M" (and
    /// "TOO SHORT" when the stop line is past it), "ROT 63 ''", "TURNAROUND 152 ' / 169 '" (minutes, after MAX and after IDLE
    /// reverse: the manual's page 66).</summary>
    internal static string BtvRow(string name, IReadOnlyList<string> words, string colour)
    {
        var w = words.Skip(1).Where(t => t != "-").ToList();
        return name switch
        {
            "RWY" when w.Count >= 4 => $"Landing runway: {w[0]}, {w[1]} {w[2]}, {string.Join(" ", w.Skip(3))}, {colour}",
            "EXIT" when w.Count >= 2 && w[0].All(char.IsDigit) => $"BTV exit: {w[0]} {w[1]}" + (w.Count > 2 ? $", {string.Join(" ", w.Skip(2))}" : "") + $", {colour}",
            "EXIT" when w.Count >= 3 => $"BTV exit: {w[0]}, {w[1]} {w[2]}" + (w.Count > 3 ? $", {string.Join(" ", w.Skip(3))}" : "") + $", {colour}",
            "ROT" when w.Count >= 1 => $"Runway occupancy: {w[0]} seconds, {colour}",
            "TURNAROUND" when w.Count >= 3 && w.Where(t => t.Any(char.IsDigit)).ToList() is { Count: 2 } t => $"Turnaround: {t[0]} minutes with max reverse, {t[1]} minutes with idle reverse, {colour}",
            _ => $"{string.Join(" ", words)}, {colour}",
        };
    }

    public static IReadOnlyList<string> ReadFcu(A350DrawCapture capture)
    {
        // The EFIS control panels' legends (y 255-405) are buttons the panels already read.
        var cells = Cells(capture.Texts).Where(c => c.Y is < 255 or >= 405).ToList();
        var used = new HashSet<DrawnCell>(cells.Where(c => c.Text is "SPD" or "HDG" or "ALT" or "V/S"));
        var o = new List<string>();
        void Field(string label, float x, float y, Func<string, string>? number = null)
        {
            var hit = cells.Where(c => !used.Contains(c) && Math.Abs(c.X - x) <= 12 && Math.Abs(c.Y - y) <= 8).ToList();
            if (hit.Count == 0) return;
            used.UnionWith(hit);
            o.Add(number == null ? $"{label}: {Speak(hit)}"
                : $"{label}: {number(string.Concat(hit.OrderBy(c => c.X).Select(c => c.Text.Trim())))}, {hit[0].Colour}");
        }
        Field("Speed", 158, 105); Field("Heading", 455, 105);
        Field("Altitude", 241, 225, FcuAltitude); Field("Vertical speed", 275, 225, FcuVerticalSpeed);
        foreach (var (label, x0, x1) in new[] { ("Captain baro", 0f, 150f), ("First officer baro", 150f, 400f) })
        {
            var baro = cells.Where(c => !used.Contains(c) && c.X >= x0 && c.X < x1 && c.Y is >= 420 and < 520).OrderBy(c => c.Y).ToList();
            if (baro.Count == 0) continue;
            used.UnionWith(baro);
            o.Add(A350Pfd.UnansweredBaro(baro.Select(c => c.Text)) ? $"{label}: not available"
                : $"{label}: " + string.Join(" ", baro.Select(c => c.Text)) + ", " + baro[^1].Colour);
        }
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>The FCU's altitude window as a number: it draws five digits ("00100").</summary>
    private static string FcuAltitude(string drawn) =>
        drawn.Length > 0 && drawn.All(char.IsDigit) ? (drawn.TrimStart('0') is { Length: > 0 } t ? t : "0") : drawn;

    /// <summary>The FCU's V/S window as a number, signed as the FCU windows say it: it draws the hundreds' zeros small, as
    /// "oo" ("+12oo" for +1200); dashes and an FPA read as drawn.</summary>
    private static string FcuVerticalSpeed(string drawn)
    {
        var t = drawn.EndsWith("oo", StringComparison.Ordinal) ? drawn[..^2] + "00" : drawn;
        return int.TryParse(t, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int fpm)
            ? fpm.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture) : drawn;
    }

    /// <summary>An RMP's screen, page by page (<see cref="A350RmpPage"/>).</summary>
    public static IReadOnlyList<string> ReadRmp(A350DrawCapture capture) => A350RmpPage.Read(capture).Select(l => l.Spoken).ToList();

    /// <summary>The overhead BAT display (the battery the BAT selector picks): seven-segment digits, the decimal
    /// point drawn as its own text before the last digit (live: "282" and "." for 28.2 V).</summary>
    /// <summary>The HUD (live 2026-10-30): the FMA's columns in its top rows, each scale's numbers the clip leaves, the values by name;
    /// the altitude drum's digits are no reading; the rest reads as rows. A stowed combiner draws nothing.</summary>
    public static IReadOnlyList<string> ReadHud(A350DrawCapture capture)
    {
        var cells = capture.Texts.Where(t => !t.Clipped && !string.IsNullOrWhiteSpace(t.Text)).Select(t => new DrawnCell(t.Text.Trim(), t.ColourName, t.X, t.Y)).ToList();
        if (cells.Count == 0) return new[] { "HUD: blank" };
        var o = new List<string>();
        var used = new HashSet<DrawnCell>();
        void Column(string name, float x0, float x1)
        {
            var column = cells.Where(c => c.Y < HudFmaBottom && c.X >= x0 && c.X < x1).OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            if (column.Count == 0) return;
            used.UnionWith(column);
            var rows = column.GroupBy(c => (int)Math.Round(c.Y / 10)).Select(g => string.Join(" ", g.Select(c => c.Text)));
            o.Add($"FMA {name}: {string.Join("; ", rows)}, {column[0].Colour}");
        }
        Column("autothrust", 0, 520);
        Column("vertical", 520, 760);
        Column("lateral", 760, 1100);
        Column("engagement", 1100, float.MaxValue);
        // The horizon's heading labels first: turned with the bank they can fall in any column, but unturned (their local place) they
        // stand on one row, 531 px apart, one after the other. The heading reads off that row at the boresight, the bank is the row's
        // turn, the pitch its height.
        List<string>? horizonLines = null;
        if (HudHorizon(capture.Texts) is { } horizon)
        {
            used.UnionWith(cells.Where(c => horizon.Any(t => t.Text.Trim() == c.Text && t.X == c.X && t.Y == c.Y)));
            var near = horizon.OrderBy(t => Math.Abs(t.LocalX - HudBoresightX)).First();
            int degrees = (int)Math.Round(HeadingLabel(near) * 10 + (HudBoresightX - near.LocalX) / HudHeadingPxPerDegree, MidpointRounding.AwayFromZero);
            var (first, last) = (horizon[0], horizon[^1]);
            double turn = Math.Atan2(last.Y - first.Y, last.X - first.X) - Math.Atan2(last.LocalY - first.LocalY, last.LocalX - first.LocalX);
            horizonLines = new List<string> { string.Create(CultureInfo.InvariantCulture, $"Heading: {(degrees % 360 + 360) % 360:000}, {near.ColourName}") };
            horizonLines.AddRange(A350Pfd.Attitude((horizon.Average(t => t.LocalY) - HudHorizonZeroY) / HudPitchPxPerDegree, turn * 180 / Math.PI));
            horizonLines.Add($"Heading scale: {string.Join(", ", horizon.Select(t => t.Text.Trim()))}, {first.ColourName}");
        }
        void Named(string name, Func<DrawnCell, bool> which, string separator = " ")
        {
            var found = cells.Where(c => !used.Contains(c) && which(c)).OrderBy(c => c.X).ToList();
            if (found.Count == 0) return;
            used.UnionWith(found);
            o.Add($"{name}: {string.Join(separator, found.Select(c => c.Text))}, {found[0].Colour}");
        }
        foreach (var gs in cells.Where(c => !used.Contains(c) && c.Text.StartsWith("GS ", StringComparison.Ordinal)).ToList())
        {
            used.Add(gs);
            o.Add($"Ground speed: {gs.Text[3..]}, {gs.Colour}");
        }
        bool Digits(DrawnCell c) => c.Text.Length > 0 && c.Text.All(char.IsDigit);
        // The speed tape's labels (x 189) read off at its window; on the stand it rests on its 30 kt floor, as the PFD's.
        var speedLabels = cells.Where(c => !used.Contains(c) && Math.Abs(c.X - HudSpeedLabelX) <= 2 && Digits(c) && c.Colour != "black").ToList();
        if (speedLabels.OrderBy(c => Math.Abs(c.Y - HudSpeedWindowY)).FirstOrDefault() is { } speed)
        {
            double knots = Math.Round(int.Parse(speed.Text, CultureInfo.InvariantCulture) + (speed.Y - HudSpeedWindowY) / HudSpeedPxPerKnot, MidpointRounding.AwayFromZero);
            o.Add(knots <= 30 ? $"Speed: 30 knots or less, {speed.Colour}" : string.Create(CultureInfo.InvariantCulture, $"Speed: {knots:0} knots, {speed.Colour}"));
        }
        // The speed target above the tape: its number black on a filled box, said in the box's colour.
        foreach (var target in cells.Where(c => !used.Contains(c) && Math.Abs(c.X - 193) <= 4 && Math.Abs(c.Y - 410) <= 8 && Digits(c)).ToList())
        {
            used.Add(target);
            var box = capture.Shapes?.LastOrDefault(s => s.Image == 0 && s.Kind == "fill" && s.X1 - s.X0 < 200
                && s.X0 <= target.X && s.X1 >= target.X && s.Y0 <= target.Y && s.Y1 >= target.Y);
            o.Add($"Speed target: {target.Text}, {(target.Colour == "black" && box != null ? A350Rgba.NameOf(box.Colour) : target.Colour)}");
        }
        Named("V1", c => Math.Abs(c.X - 220) <= 4 && Digits(c) && c.Text.Length >= 2);
        foreach (var mach in cells.Where(c => !used.Contains(c) && c.Text.Length > 1 && c.Text[0] == '.' && c.Text[1..].All(char.IsDigit)).ToList())
        {
            used.Add(mach);
            o.Add($"Mach: 0{mach.Text}, {mach.Colour}");
        }
        // The altitude: the tape's hundreds (x 1303) and the drum's tens (x 1379), each nearest its window; the drum's other
        // digits are its own.
        var altTape = cells.Where(c => Math.Abs(c.X - 1303) <= 2 && c.Text.Length == 3 && Digits(c)).OrderBy(c => Math.Abs(c.Y - HudAltitudeWindowY)).FirstOrDefault();
        var altDrum = cells.Where(c => Math.Abs(c.X - 1379) <= 2 && c.Text.Length == 2 && Digits(c)).OrderBy(c => Math.Abs(c.Y - HudAltitudeWindowY)).FirstOrDefault();
        if (altTape != null && altDrum != null)
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Altitude: {A350Pfd.Altitude(int.Parse(altTape.Text, CultureInfo.InvariantCulture), altTape.Y, int.Parse(altDrum.Text, CultureInfo.InvariantCulture), altDrum.Y, HudAltitudeWindowY, HudTapePxPerFoot, HudDrumPxPerFoot)} feet, {altDrum.Colour}"));
        // The V/S beside the altitude (x 1494), in hundreds of feet a minute, moving with its pointer.
        foreach (var vs in cells.Where(c => !used.Contains(c) && Math.Abs(c.X - 1494) <= 4 && int.TryParse(c.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)).ToList())
        {
            used.Add(vs);
            int fpm = int.Parse(vs.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) * 100;
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Vertical speed: {Math.Abs(fpm)} feet per minute{(fpm > 0 ? " up" : fpm < 0 ? " down" : "")}, {vs.Colour}"));
        }
        // The selected altitude: on the tape at its height (x 1325), or parked above or below it when off the scale (x 1409).
        Named("Selected altitude", c => (c.X >= 1400 && c.Text.Length >= 3 || Math.Abs(c.X - 1325) <= 2 && c.Text.Length >= 3) && Digits(c));
        // The LS block (x 142): its source, ident, frequency and distance top to bottom; an ident not decoded shows the frequency again.
        var ls = cells.Where(c => !used.Contains(c) && Math.Abs(c.X - 142) <= 4 && c.Y is >= 980 and < 1130).OrderBy(c => c.Y).ToList();
        if (ls.Count > 0)
        {
            used.UnionWith(ls);
            var words = ls.Select(c => c.Text).Where((t, i) => i == 0 || t != ls[i - 1].Text).ToList();
            o.Add($"LS: {string.Join(", ", words)}, {ls[0].Colour}");
        }
        if (cells.FirstOrDefault(c => c.Text == "QNH") is { } qnh)
        {
            used.Add(qnh);
            Named("QNH", c => c.Y == qnh.Y && c.X > qnh.X);
        }
        // The wind's three texts are at one height left of the speed scale's right edge (303, /, 8).
        var slash = cells.FirstOrDefault(c => c.Text == "/");
        if (slash != null)
            Named("Wind", c => Math.Abs(c.Y - slash.Y) <= 2 && c.X < 700);
        Named("Speed scale", c => Math.Abs(c.X - HudSpeedLabelX) <= 2 && Digits(c) && c.Colour != "black", ", ");
        Named("Altitude scale", c => Math.Abs(c.X - 1303) <= 2 && c.Text.Length == 3 && c.Text.All(char.IsDigit), ", ");
        if (horizonLines != null)
            o.AddRange(horizonLines);
        // The altitude drum's digits: its column and the rolling tens beside it.
        used.UnionWith(cells.Where(c => c.X is >= 1300 and < 1400 && c.Y is >= 500 and < 800 && c.Text.TrimStart('-').All(char.IsDigit)));
        // What it does not name reads as drawn, a lone letter too (the N every flight frame draws at one place: no compass letter).
        o.AddRange(Rest(cells, used));
        return o;
    }

    private const float HudFmaBottom = 230;

    /// <summary>The HUD's scales, fitted on the second flight of 2026-11-03 against the PFD (level rounds; plan 24): the speed tape's
    /// labels at x 189, 5.4 px a knot, its window at y 648; the altitude tape 0.388 px a foot and the drum's tens 1.275 px a foot,
    /// their window at y 645; the horizon's heading labels 53.1 px a degree, the boresight at x 800.</summary>
    private const float HudSpeedLabelX = 189, HudSpeedWindowY = 648, HudSpeedPxPerKnot = 5.4f,
        HudAltitudeWindowY = 645, HudTapePxPerFoot = 0.388f, HudDrumPxPerFoot = 1.275f, HudBoresightX = 800, HudHeadingPxPerDegree = 53.1f;

    /// <summary>The horizon's height for the pitch, fitted the same way: 45 px a degree, zero pitch at y 459 (the stand's -0.5 at 437,
    /// level at 10,000 ft 2.35 at 565 and 0.3 at 472.7); its labels are tens of degrees, 531 px apart.</summary>
    private const float HudHorizonZeroY = 459, HudPitchPxPerDegree = 45, HudHeadingLabelSpacing = 531;

    private static int HeadingLabel(DrawnText t) => int.Parse(t.Text.Trim(), CultureInfo.InvariantCulture);

    /// <summary>The horizon's heading labels: the longest run of two-digit numbers on one unturned row (local y within 2 px), each
    /// 531 px after the one before (local x) and the next ten degrees; null with fewer than two.</summary>
    internal static List<DrawnText>? HudHorizon(IEnumerable<DrawnText> texts)
    {
        var two = texts.Where(t => !t.Clipped && t.Text.Trim() is { Length: 2 } s && s.All(char.IsDigit)).ToList();
        List<DrawnText>? best = null;
        foreach (var seed in two)
        {
            var row = two.Where(t => Math.Abs(t.LocalY - seed.LocalY) <= 2).OrderBy(t => t.LocalX).ToList();
            var chain = new List<DrawnText> { seed };
            foreach (var t in row.Where(t => t.LocalX > seed.LocalX))
                if (Math.Abs(t.LocalX - chain[^1].LocalX - HudHeadingLabelSpacing) <= 15 && (HeadingLabel(t) - HeadingLabel(chain[^1]) + 36) % 36 == 1)
                    chain.Add(t);
            if (chain.Count >= 2 && (best == null || chain.Count > best.Count))
                best = chain;
        }
        return best;
    }

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

    /// <summary>The standby flight display's tapes, read as the PFD's (<see cref="A350Pfd.Altitude(int, float, int, float,
    /// float, float, float)"/>): the window's text line at y 132.5 (its box 106 to 143, the digits' baseline 8 px under its
    /// middle; on the stand the drum reads 70 ft there, the PFD 71 from the same copy, and the speed tape its 30 kt floor);
    /// speed labels every 20 kt, 44 px apart; the altitude tape's hundreds 70 px per 500 ft; the drum's tens 26 px per
    /// 20 ft, evenly spaced (no perspective, unlike the PFD's).</summary>
    private const float SfdWindowY = 132.5f, SfdSpeedPxPerKnot = 2.2f, SfdTapePxPerFoot = 0.14f, SfdDrumPxPerFoot = 1.3f;

    /// <summary>The SFD's pitch ladder, measured in plan 08's flight (2026-11-03) against the sim's attitude: labels every 10 degrees
    /// 49 px apart at local x 116, each drawn 8 px under its line; the horizon at y 125.0 at zero pitch; the heading scale's labels
    /// unturned on the row at y 239.</summary>
    private const float SfdPitchPxPerDegree = 4.9f, SfdPitchZeroY = 125.0f, SfdLadderX = 116, SfdLabelBelowLine = 8, SfdHeadingRowY = 239;

    /// <summary>The SFD's heading scale: 2.3 px a degree, its index the yellow mark centred at x 142 (in flight against the sim's
    /// heading, plan 08: the stand's 145 read every heading 1.3 degrees high).</summary>
    private const float SfdHeadingIndexX = 142, SfdHeadingPxPerDegree = 2.3f;

    /// <summary>A heading scale label: drawn unturned on its row, where a ladder label passes only turned or off the row.</summary>
    private static bool OnSfdHeadingRow(DrawnText t) =>
        MathF.Abs(t.Y - SfdHeadingRowY) < 0.5f && MathF.Abs(t.LocalY - t.Y) < 0.5f && MathF.Abs(t.LocalX - t.X) < 0.5f;

    /// <summary>The SFD's pitch (up positive) and bank (left positive) from its ladder's labels. The draw turns the ladder by the bank
    /// after placing it by the pitch, so each label's local place gives the horizon (above it the values fall towards it, below they
    /// rise; the split taken is the one whose labels agree), and the line through the two labels farthest apart, before and after the
    /// turn, the bank. Null with fewer than two labels or no split that agrees within 2 px.</summary>
    internal static (double Pitch, double Bank)? SfdAttitude(IEnumerable<DrawnText> texts)
    {
        var labels = Distinct(texts)
            .Where(t => t.ColourName == "white" && MathF.Abs(t.LocalX - SfdLadderX) < 1 && !OnSfdHeadingRow(t)
                && t.Text.Trim() is { Length: 2 } s && s[0] is >= '1' and <= '9' && s[1] == '0')
            .OrderBy(t => t.LocalY).ToList();
        if (labels.Count < 2) return null;
        int Value(DrawnText t) => (t.Text.Trim()[0] - '0') * 10;
        double? horizon = null, spread = null;
        for (int split = 0; split <= labels.Count; split++)
        {
            var above = labels.Take(split).ToList();
            var below = labels.Skip(split).ToList();
            if (above.Zip(above.Skip(1)).Any(p => Value(p.First) <= Value(p.Second)) || below.Zip(below.Skip(1)).Any(p => Value(p.First) >= Value(p.Second)))
                continue;
            var lines = above.Select(t => t.LocalY - SfdLabelBelowLine + SfdPitchPxPerDegree * Value(t))
                .Concat(below.Select(t => t.LocalY - SfdLabelBelowLine - SfdPitchPxPerDegree * Value(t))).ToList();
            double s = lines.Max() - lines.Min();
            if (spread == null || s < spread) (spread, horizon) = (s, lines.Average());
        }
        if (horizon is not { } h || spread > 2) return null;
        var (first, last) = (labels[0], labels[^1]);
        double turn = Math.Atan2(last.X - first.X, last.Y - first.Y) - Math.Atan2(last.LocalX - first.LocalX, last.LocalY - first.LocalY);
        return ((h - SfdPitchZeroY) / SfdPitchPxPerDegree, -turn * 180 / Math.PI);
    }

    /// <summary>The standby flight display: speed and altitude (its tapes), pitch and bank (its ladder), baro, heading (its scale,
    /// read at the index), position and its source; flags as drawn.</summary>
    public static IReadOnlyList<string> ReadSfd(A350DrawCapture capture)
    {
        var cells = Cells(capture.Texts);
        var used = new HashSet<DrawnCell>();
        var o = new List<string>();
        // The speed tape's labels (x 42, white), the label nearest the window read off; on the ground the tape rests on its
        // 30 kt floor, as the PFD's (review 2026-10-29: the SFD read no speed or altitude at all).
        var speedLabels = cells.Where(c => c.X is >= 20 and < 60 && c.Colour == "white" && c.Text.Length is 2 or 3 && c.Text.All(char.IsDigit)).ToList();
        if (speedLabels.Count > 0)
        {
            var s = speedLabels.OrderBy(c => Math.Abs(c.Y - SfdWindowY)).First();
            double knots = Math.Round(int.Parse(s.Text) + (s.Y - SfdWindowY) / SfdSpeedPxPerKnot, MidpointRounding.AwayFromZero);
            o.Add(knots <= 30 ? "Speed: 30 knots or less" : $"Speed: {knots.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} knots");
        }
        // The altitude: the tape's hundreds (x 243, white, three digits) and the drum's tens (x 259), each nearest the window.
        var tape = cells.Where(c => c.X is >= 235 and < 252 && c.Colour == "white" && c.Text.Length == 3 && c.Text.All(char.IsDigit)).ToList();
        var drum = cells.Where(c => c.X is >= 252 and < 270 && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        if (tape.Count > 0 && drum.Count > 0)
        {
            var t = tape.OrderBy(c => Math.Abs(c.Y - SfdWindowY)).First();
            var d = drum.OrderBy(c => Math.Abs(c.Y - SfdWindowY)).First();
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Altitude: {A350Pfd.Altitude(int.Parse(t.Text, CultureInfo.InvariantCulture), t.Y, int.Parse(d.Text, CultureInfo.InvariantCulture), d.Y, SfdWindowY, SfdTapePxPerFoot, SfdDrumPxPerFoot)} feet"));
        }
        // The attitude, said as the PFD says it (plan 08, 2026-11-02: the SFD read no pitch or bank, its horizon being graphics).
        if (SfdAttitude(capture.Texts) is { } attitude) o.AddRange(A350Pfd.Attitude(attitude.Pitch, attitude.Bank));
        foreach (var b in cells.Where(c => c.Colour == "cyan" && Number(c)))
        {
            used.Add(b);
            o.Add(A350Pfd.UnansweredBaro(new[] { b.Text }) ? "Baro: not available" : $"Baro: {b.Text}, cyan");
        }
        // The heading scale's labels, unturned on their row: a ladder label crossing the row in a turn is none (plan 08's right turn).
        // From 000 to 080 a label is one digit; each is read off at the index.
        var scale = Distinct(capture.Texts).Where(t => t.ColourName == "white" && OnSfdHeadingRow(t) && t.Text.Trim() is { Length: 1 or 2 } s && s.All(char.IsDigit)).ToList();
        if (scale.Count > 0)
        {
            var nearest = scale.OrderBy(t => Math.Abs(t.X - SfdHeadingIndexX)).First();
            int heading = (int)Math.Round(int.Parse(nearest.Text.Trim(), CultureInfo.InvariantCulture) * 10 + (SfdHeadingIndexX - nearest.X) / SfdHeadingPxPerDegree);
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Heading: {(heading % 360 + 360) % 360:000}"));
        }
        // The LS block bottom left (x 14: the source over the frequency, magenta) and the heading source written top to bottom at
        // x 68 ("I", "R", "3"), both read since plan 24 (a number and lone letters before).
        var ls = cells.Where(c => !used.Contains(c) && c.X < 40 && c.Y is >= 236 and < 275).OrderBy(c => c.Y).ToList();
        if (ls.Count > 0)
        {
            used.UnionWith(ls);
            o.Add($"LS: {string.Join(", ", ls.Select(c => c.Text))}, {ls[0].Colour}");
        }
        var source = cells.Where(c => !used.Contains(c) && Math.Abs(c.X - 68) <= 2 && c.Y is >= 228 and < 270 && c.Text.Length == 1).OrderBy(c => c.Y).ToList();
        if (source.Count > 1)
        {
            used.UnionWith(source);
            o.Add($"Heading source: {string.Concat(source.Select(c => c.Text))}, {source[0].Colour}");
        }
        Position(cells, used, o);
        // Tape, drum and scale numbers are not text to read.
        used.UnionWith(cells.Where(c => Number(c) || c.Text.Length == 1));
        o.AddRange(Rest(cells, used));
        return o;
    }

    /// <summary>The SND's rose (plan 24, the second flight of 2026-11-03): labels every 30 degrees at radius 70 about (150, 150), each
    /// drawn from (-2, -70) turned by its bearing less the heading, so the heading is each label's bearing less its turn; their
    /// mean, rounded, is read at the top.</summary>
    private const float SndRoseX = 150, SndRoseY = 150;

    private static string? SndHeading(IEnumerable<DrawnText> texts)
    {
        var labels = Distinct(texts).Where(t => !t.Clipped && MathF.Abs(t.LocalX + 2) < 1 && MathF.Abs(t.LocalY + 70) < 1
            && t.Text.Trim() is { Length: 1 or 2 } s && s.All(char.IsDigit)).ToList();
        if (labels.Count == 0) return null;
        double offset = Math.Atan2(-2, 70);
        double sin = 0, cos = 0;
        foreach (var t in labels)
        {
            double turn = Math.Atan2(t.X - SndRoseX, SndRoseY - t.Y) - offset;
            double heading = int.Parse(t.Text.Trim(), CultureInfo.InvariantCulture) * 10 * Math.PI / 180 - turn;
            sin += Math.Sin(heading); cos += Math.Cos(heading);
        }
        int degrees = (int)Math.Round(Math.Atan2(sin, cos) * 180 / Math.PI, MidpointRounding.AwayFromZero);
        return string.Create(CultureInfo.InvariantCulture, $"Heading: {(degrees % 360 + 360) % 360:000}, {labels[0].ColourName}");
    }

    /// <summary>The standby navigation display: ground speed, heading source, its heading off the rose, position and its source.</summary>
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
        if (SndHeading(capture.Texts) is { } heading)
            o.Add(heading);
        Position(cells, used, o);
        used.UnionWith(cells.Where(Number));   // the compass rose's labels
        o.AddRange(Rest(cells, used));
        return o;
    }
}
