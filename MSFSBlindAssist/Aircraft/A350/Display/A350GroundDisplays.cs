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
        foreach (var traffic in cells.Where(c => A350NdMap.TrafficText.IsMatch(c.Text)).ToList())
        {
            used.Add(traffic);
            trafficLines.Add($"Traffic {A350NdMap.TrafficAltitude(traffic.Text)}, {traffic.Colour}");
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
        // The safe altitude under the vertical display (MSA or MORA, white), its value just above its name ("3" and "8"), or
        // just under it low down.
        foreach (var name in cells.Where(c => !used.Contains(c) && c.Text is "MSA" or "MORA" && c.Y >= 900).ToList())
        {
            var value = cells.Where(c => !used.Contains(c) && c != name && c.Text.Length > 0 && c.Text.All(char.IsDigit)
                && Math.Abs(name.Y - c.Y) is > 0 and <= 40 && Math.Abs(c.X - name.X) <= 50).OrderBy(c => c.X).ToList();
            used.Add(name);
            used.UnionWith(value);
            o.Add(value.Count > 0 ? $"{name.Text}: {string.Concat(value.Select(c => c.Text))}, {value[0].Colour}" : $"{name.Text}, {name.Colour}");
        }
        o.AddRange(trafficLines);
        o.AddRange(Rest(cells, used));
        return o;
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
    /// <summary>The HUD (live 2026-10-30): the FMA's columns in its top rows, each scale's numbers the clip leaves, the values by name. Its
    /// altitude drum's digits are no reading; the rest reads as rows. A stowed combiner draws nothing.</summary>
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
        Named("Selected altitude", c => c.X >= 1400 && c.Text.Length >= 3 && c.Text.All(char.IsDigit));
        if (cells.FirstOrDefault(c => c.Text == "QNH") is { } qnh)
        {
            used.Add(qnh);
            Named("QNH", c => c.Y == qnh.Y && c.X > qnh.X);
        }
        // The wind's three texts are at one height left of the speed scale's right edge (303, /, 8).
        var slash = cells.FirstOrDefault(c => c.Text == "/");
        if (slash != null)
            Named("Wind", c => Math.Abs(c.Y - slash.Y) <= 2 && c.X < 700);
        Named("Speed scale", c => c.X is >= 150 and < 240 && c.Text.All(char.IsDigit) && c.Colour != "black", ", ");
        Named("Altitude scale", c => Math.Abs(c.X - 1303) <= 2 && c.Text.Length == 3 && c.Text.All(char.IsDigit), ", ");
        // The heading scale's numbers, tens of degrees, stand on one line that moves with the pitch; the first two-digit run across the width.
        var heading = cells.Where(c => !used.Contains(c) && c.Text.Length == 2 && c.Text.All(char.IsDigit) && c.X < 1450 && c.X > 240 && Math.Abs(c.X - 1379) > 2)
            .GroupBy(c => (int)Math.Round(c.Y / 40)).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count()).FirstOrDefault()?.ToList();
        if (heading != null)
        {
            used.UnionWith(heading);
            o.Add($"Heading scale: {string.Join(", ", heading.OrderBy(c => c.X).Select(c => c.Text))}, {heading[0].Colour}");
        }
        // The altitude drum's digits: its column and the rolling tens beside it.
        used.UnionWith(cells.Where(c => c.X is >= 1300 and < 1400 && c.Y is >= 500 and < 800 && c.Text.TrimStart('-').All(char.IsDigit)));
        // What it does not name reads as drawn; the numbers left are scales' (the pitch ladder), a lone letter the compass's.
        o.AddRange(Rest(cells.Where(c => c.Text.Any(char.IsLetter) && c.Text.Trim().Length > 1), used));
        return o;
    }

    private const float HudFmaBottom = 230;

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

    /// <summary>The standby flight display: speed and altitude (its tapes), baro, heading (scale labels 2.3 px a degree,
    /// index at x 145), position and its source; flags as drawn.</summary>
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
        foreach (var b in cells.Where(c => c.Colour == "cyan" && Number(c)))
        {
            used.Add(b);
            o.Add(A350Pfd.UnansweredBaro(new[] { b.Text }) ? "Baro: not available" : $"Baro: {b.Text}, cyan");
        }
        var scale = cells.Where(c => c.Y is >= 230 and < 250 && c.Colour == "white" && c.Text.Length == 2 && c.Text.All(char.IsDigit)).ToList();
        if (scale.Count > 0)
        {
            var nearest = scale.OrderBy(c => Math.Abs(c.X - 145)).First();
            o.Add(string.Create(CultureInfo.InvariantCulture, $"Heading: {(int.Parse(nearest.Text, CultureInfo.InvariantCulture) * 10 + (145 - nearest.X) / 2.3 + 360) % 360:0}"));
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
