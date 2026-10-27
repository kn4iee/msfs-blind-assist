using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One line of the MFD as a pilot moves through it: what kind it is (menu, button, tab, field,
/// scratchpad, text), how it is spoken, and where on the side's own MFD a click lands on it.</summary>
public sealed record MfdLine(string Kind, string Spoken, float ClickX, float ClickY);

/// <summary>
/// One side's MFD page as lines (pure), from the draw's UI elements and texts by geometry: a button's, tab's or
/// menu's text is the text inside its rectangle; a field's value is the text inside it and its label the nearest
/// text left of it on its row (else right of it, between two columns, with each column's header); the scratchpad (NotepadTextbox) holds what is typed and the FMS messages. Dialogs
/// and tab switchers are containers, never lines (the dismissed crash-report dialog's rectangle covers the whole
/// page); an element with nothing in it is a hidden tab's or an icon. Texts in no element read as rows. The
/// first officer's MFD is drawn translated by 836: its lines are in its own coordinates.
/// </summary>
public static class A350MfdPage
{
    private const float HalfWidth = 836;

    private static readonly HashSet<string> Containers = new(StringComparer.Ordinal)
    {
        "WidgetSwitcherUI", "PopupDialog", "EULADialog", "DropdownMenuContainer", "SurvControlsBox",
        "UIFlightplanRevisionsMenu", "NextWaypointPopup", "NewDestPopup",
    };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The font's degree sign inside a position: "07d10.5N/079d53.1E".</summary>
    private static readonly Regex PositionDegrees = new(@"(?<=\d)d(?=\d)", RegexOptions.Compiled);

    /// <summary>A drawn text; <see cref="Index"/> is its place in the draw order.</summary>
    private sealed record Cell(string Text, string Colour, float X, float Y, int Index);

    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? 0 : HalfWidth;
        bool Mine(float x) => side == 0 ? x < HalfWidth : x >= HalfWidth;
        var cells = capture.Texts.Select((t, i) => (t, i)).Where(p => Mine(p.t.X) && !string.IsNullOrWhiteSpace(p.t.Text))
            .Select(p => new Cell(PositionDegrees.Replace(Spaces.Replace(A350Ewd.WithoutLeaders(p.t.Text), " "), "°"), p.t.ColourName, p.t.X - shift, p.t.Y, p.i))
            .Where(c => c.Text.Length > 0).ToList();
        var used = new HashSet<Cell>();
        var lines = new List<(float Top, float Left, MfdLine Line)>();
        var fields = new List<(float Top, float X0, float X1, Cell? Label, string? Value, float Cx, float Cy)>();
        var leftLabels = new HashSet<Cell>();
        var headers = new HashSet<Cell>();
        // Texts inside a control are its own, never another control's label or header.
        var inControls = capture.Elements.Where(e => Mine(e.X) && !Containers.Contains(e.Class) && e.RectW > 0 && e.RectH > 0).SelectMany(e =>
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            return cells.Where(c => c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4);
        }).ToHashSet();
        var inFields = capture.Elements.Where(e => Mine(e.X) && IsField(e)).SelectMany(e =>
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            return cells.Where(c => c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4);
        }).ToHashSet();

        bool flightPlan = cells.Any(c => c.Colour == "black" && c.Y < 150 && c.Text.EndsWith("F-PLN", StringComparison.Ordinal));
        if (flightPlan)
            ReadFlightPlan(capture, shift, Mine, cells, used, lines);

        foreach (var e in capture.Elements.Where(e => Mine(e.X) && !Containers.Contains(e.Class) && e.RectW > 0 && e.RectH > 0))
        {
            // The F-PLN's row boxes are the F-PLN reader's (hidden rows' boxes reach under the bottom keys).
            if (flightPlan && e.Class == "BaseFlightPlanButton") continue;
            float x0 = e.X - shift, y0 = e.Y, x1 = x0 + e.RectW, y1 = y0 + e.RectH + 4;
            // A control holds only what was drawn after it began: an open menu covers list rows drawn before it.
            // The scratchpad's message is drawn before its box (live).
            int from = e.Class == "NotepadTextbox" ? 0 : e.TextStart;
            var inside = cells.Where(c => !used.Contains(c) && c.Index >= from && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1)
                .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            float cx = x0 + e.RectW / 2, cy = y0 + e.RectH / 2;
            string value = Words(inside.Select(c => c.Text));
            string colour = inside.FirstOrDefault(c => c.Colour != "grey")?.Colour ?? inside.FirstOrDefault()?.Colour ?? "white";
            if (e.Class == "NotepadTextbox")
            {
                used.UnionWith(inside);
                lines.Add((y0, x0, new MfdLine("scratchpad", inside.Count == 0 ? "Scratchpad: empty" : $"Scratchpad: {value}, {colour}", cx, cy)));
                continue;
            }
            if (e.Class.EndsWith("BoxItem", StringComparison.Ordinal))
            {
                // One option of a selector box (SURV CONTROLS): an unselected option is grey.
                if (inside.Count == 0) continue;
                used.UnionWith(inside);
                var box = capture.Elements.FirstOrDefault(b => b.Class == e.Class[..^4] && b.X <= e.X && b.X + b.RectW >= e.X + e.RectW
                    && b.Y <= e.Y && b.Y + b.RectH >= e.Y + e.RectH);
                var header = box == null ? new List<Cell>() : cells.Where(c => !inControls.Contains(c) && c.X >= box.X - shift
                    && c.X <= box.X - shift + box.RectW && c.Y >= box.Y - 50 && c.Y < box.Y).OrderBy(c => c.Y).ToList();
                headers.UnionWith(header);
                string name = header.Count == 0 ? value : $"{string.Join(" ", header.Select(c => c.Text))}: {value}";
                // A box's options read together, in order (top before left would interleave the boxes).
                float top = box?.Y ?? y0, left = (box == null ? x0 : box.X - shift) + (y0 - top) / 1000f;
                lines.Add((top, left, new MfdLine("option", inside.All(c => c.Colour == "grey") ? name : $"{name}, selected, {colour}", cx, cy)));
                continue;
            }
            if (e.Class == "BaseUICheckbox")
            {
                if (inside.Count == 0) continue;
                used.UnionWith(inside);
                bool check = capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0 && sh.X0 - shift >= x0 && sh.X1 - shift <= x0 + e.RectW
                    && sh.Y0 >= y0 && sh.Y1 <= y0 + e.RectH) == true;
                string state = $"{value} check box, {(check ? "checked" : "not checked")}";
                lines.Add((y0, x0, new MfdLine("checkbox", colour == "grey" ? $"{state}, unavailable" : colour == "white" ? state : $"{state}, {colour}", cx, cy)));
                continue;
            }
            if (IsField(e))
            {
                float rowY = y0 + e.RectH * 0.75f;
                bool OnRow(Cell c) => !inside.Contains(c) && !inControls.Contains(c) && Math.Abs(c.Y - rowY) <= 12;
                // The nearest label left of the field, one field each (DATA STATUS: IDLE [ ] PERF [ ] [ ]); a label
                // between two columns (RAD NAV's IDENT, FREQ, CRS) also stands right of the left column's fields.
                var label = cells.Where(c => OnRow(c) && c.X < x0 && !leftLabels.Contains(c)).OrderByDescending(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => OnRow(c) && c.X > x1 && c.X - x1 <= 120).OrderBy(c => c.X).FirstOrDefault()
                    // else a label just above it (SURV's SQWK)
                    ?? cells.Where(c => !inControls.Contains(c) && c.X >= x0 && c.X <= x1 && c.Y < y0 && y0 - c.Y <= 20).OrderByDescending(c => c.Y).FirstOrDefault();
                if (label == null && inside.Count == 0) continue;
                used.UnionWith(inside);
                if (label != null && label.X < x0) leftLabels.Add(label);
                fields.Add((y0, x0, x1, label, inside.Count == 0 ? null : $"{value}, {colour}", cx, cy));
                continue;
            }
            if (inside.Count == 0) continue;
            used.UnionWith(inside);
            var (kind, spoken) = e.Class switch
            {
                "MainHeaderDropdownMenu" => ("menu", $"{value} menu, {colour}"),
                "WidgetSwitcherTab" => ("tab", $"{value} tab"),
                _ => ("button", colour == "white" ? $"{value} button" : $"{value} button, {colour}"),
            };
            lines.Add((y0, x0, new MfdLine(kind, spoken, cx, cy)));
        }

        // A label two fields share takes each field's column header: the nearest text above it in its column.
        foreach (var f in fields)
        {
            string name = f.Label?.Text ?? "Field";
            if (f.Label != null && fields.Count(o => o.Label == f.Label) > 1)
            {
                var header = cells.Where(c => !inFields.Contains(c) && c.Y < f.Top && c.X >= f.X0 && c.X <= f.X1)
                    .OrderByDescending(c => c.Y).FirstOrDefault();
                if (header != null) name = $"{header.Text} {name}";
            }
            lines.Add((f.Top, f.X0, new MfdLine("field", f.Value == null ? $"{name}: empty" : $"{name}: {f.Value}", f.Cx, f.Cy)));
        }
        used.UnionWith(fields.Where(f => f.Label != null).Select(f => f.Label!));
        used.UnionWith(headers);

        var rows = A350DrawLayout.Rows(cells.Where(c => !used.Contains(c))
            .Select(c => new DrawnText(c.Text, c.X, c.Y, c.X, c.Y, 0, ColourOf(c.Colour)))).ToList();
        var paired = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (paired.Contains(i) || rows[i].Count < 2 || rows[i].Any(c => c.Colour != "white")) continue;
            int j = ValuesUnder(rows, i, paired);
            if (j < 0) continue;
            paired.Add(i);
            paired.Add(j);
            lines.Add((rows[i][0].Y - 14, rows[i][0].X, new MfdLine("text", Table(rows[i], rows[j]), rows[j][0].X, rows[j][0].Y - 7)));
        }
        for (int i = 0; i < rows.Count; i++)
            if (!paired.Contains(i))
                lines.Add((rows[i][0].Y - 14, rows[i][0].X, new MfdLine("text", A350DrawLayout.Speak(rows[i]), rows[i][0].X, rows[i][0].Y - 7)));

        return lines.OrderBy(l => l.Top).ThenBy(l => l.Left).Select(l => l.Line).ToList();
    }

    private static readonly Regex DashRun = new(@"(\s*-){2,}\s*", RegexOptions.Compiled);
    private static readonly Regex TrackDegrees = new(@"^(\d{1,3})d$", RegexOptions.Compiled);

    /// <summary>
    /// An F-PLN page (live, ACTIVE/F-PLN): each waypoint row is four boxes (ident, time, speed, altitude;
    /// <c>BaseFlightPlanButton</c>), the leg into it (airway, track, distance, FPA) drawn just above its row, and the
    /// DISCONTINUITY and END rows full width. One line per waypoint, labelled with the page's own column headers
    /// (FROM, TIME, the SPD ALT menu's words, TRK, DIST, FPA); only the rows above the destination bar, as a sighted
    /// pilot sees them (the draw goes on below it, hidden); the destination bar as one line.
    /// </summary>
    private static void ReadFlightPlan(A350DrawCapture capture, float shift, Func<float, bool> mine, List<Cell> cells,
        HashSet<Cell> used, List<(float Top, float Left, MfdLine Line)> lines)
    {
        var boxes = capture.Elements.Where(e => mine(e.X) && e.Class == "BaseFlightPlanButton")
            .Select(e => (X: e.X - shift, e.Y, W: e.RectW, H: e.RectH)).ToList();
        if (boxes.Count == 0) return;
        // The destination bar's top: its DEST and destination keys (live: y 816 and 825).
        float barTop = capture.Elements.Where(e => mine(e.X) && e.Y > 780 && e.Y < 900
                && e.Class is "BaseUIButton" or "BaseTexturedButton").Select(e => e.Y).DefaultIfEmpty(816).Min();

        // The column headers (live: FROM 25, TIME 262, TRK 605, DIST 722, FPA 741 at y 174; SPD ALT is a menu).
        var header = cells.Where(c => c.Colour == "white" && c.Y is > 160 and < 190 && !c.Text.Contains("  ")).ToList();
        string? Header(float x0, float x1) => header.FirstOrDefault(c => c.X >= x0 && c.X < x1)?.Text;
        string from = Header(0, 100) ?? "FROM", time = Header(200, 334) ?? "TIME";
        var spdAlt = (cells.FirstOrDefault(c => c.Y is > 160 and < 190 && c.Text.Contains("  "))?.Text ?? "SPD ALT")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string spd = spdAlt.ElementAtOrDefault(0) ?? "SPD", alt = spdAlt.ElementAtOrDefault(1) ?? "ALT";
        var legHeaders = header.Where(c => c.X >= 560).OrderBy(c => c.X).ToList();
        used.UnionWith(header);
        int headerIndex = header.Select(c => c.Index).DefaultIfEmpty(int.MaxValue).Min();

        // Hidden behind the destination bar: the list's texts (drawn after the scratchpad's message and before the
        // header) at or below the bar's top.
        int listStart = cells.Where(c => c.Index < headerIndex && boxes.Any(b => b.Y < barTop && c.X >= b.X && c.X <= b.X + b.W && c.Y >= b.Y && c.Y <= b.Y + b.H))
            .Select(c => c.Index).DefaultIfEmpty(headerIndex).Min();
        bool InList(Cell c) => c.Index >= listStart && c.Index < headerIndex;
        used.UnionWith(cells.Where(c => InList(c) && c.Y >= barTop));

        static string Value(Cell c) => TrackDegrees.Match(c.Text) is { Success: true } m ? m.Groups[1].Value + "°" : Word(c.Text);
        string ValueOf(Cell? c, string identColour) =>
            c == null ? "none" : Value(c) + (c.Colour != identColour && c.Colour != "grey" ? $" ({c.Colour})" : "");

        float prevBottom = header.Select(c => c.Y).DefaultIfEmpty(174).Max() + 10;   // the first row's leg starts under the header
        var rows = boxes.Where(b => b.Y + b.H <= barTop && b.X < 40 && (b.W < 200 || b.W > 600)).OrderBy(b => b.Y).ToList();
        foreach (var b in rows)
        {
            var inBox = cells.Where(c => !used.Contains(c) && InList(c) && c.X >= b.X && c.X <= b.X + b.W && c.Y >= b.Y && c.Y <= b.Y + b.H).ToList();
            if (b.W > 600)
            {
                // DISCONTINUITY, END OF F-PLN: the marker without its dashes.
                used.UnionWith(inBox);
                if (inBox.Count > 0)
                    lines.Add((b.Y, 0, new MfdLine("text", $"{DashRun.Replace(inBox[0].Text, " ").Trim()}, {inBox[0].Colour}", b.X + b.W / 2, b.Y + b.H / 2)));
                prevBottom = b.Y + b.H;
                continue;
            }
            var ident = inBox.OrderBy(c => c.X).FirstOrDefault();
            if (ident == null) { prevBottom = b.Y + b.H; continue; }
            used.Add(ident);
            var row = cells.Where(c => !used.Contains(c) && InList(c) && Math.Abs(c.Y - ident.Y) <= 6 && c.X >= 200 && c.X < 563).ToList();
            Cell? At(float x0, float x1) => row.FirstOrDefault(c => c.X >= x0 && c.X < x1);
            Cell? t = At(200, 334), sp = At(334, 422), al = At(422, 563);
            used.UnionWith(row);
            var leg = cells.Where(c => !used.Contains(c) && InList(c) && c.Y >= prevBottom && c.Y < b.Y && (c.X < 200 || c.X >= 560)).ToList();
            used.UnionWith(leg);
            var parts = new List<string>();
            if (leg.FirstOrDefault(c => c.X < 200) is { } via) parts.Add("via " + via.Text);
            foreach (var c in leg.Where(c => c.X >= 560).OrderBy(c => c.X))
            {
                var name = legHeaders.OrderBy(h => Math.Abs(h.X - c.X)).FirstOrDefault()?.Text;
                parts.Add(name == null ? ValueOf(c, ident.Colour) : $"{name} {ValueOf(c, ident.Colour)}");
            }
            string label = b.Y < 230 ? $"{from} {ident.Text}" : ident.Text;
            string legText = parts.Count > 0 ? string.Join(", ", parts) + "; " : "";
            lines.Add((b.Y, 0, new MfdLine("waypoint",
                $"{label}: {legText}{time} {ValueOf(t, ident.Colour)}, {spd} {ValueOf(sp, ident.Colour)}, {alt} {ValueOf(al, ident.Colour)}, {ident.Colour}",
                b.X + b.W / 2, b.Y + b.H / 2)));
            prevBottom = b.Y + b.H;
        }

        // The destination bar: its key (the destination, which opens its arrival page), time, EFOB and distance.
        if (capture.Elements.FirstOrDefault(e => mine(e.X) && e.Class == "BaseUIButton" && e.X - shift < 40 && e.Y >= barTop - 1 && e.Y < barTop + 40) is { } dest)
        {
            float dx = dest.X - shift;
            var key = cells.FirstOrDefault(c => !used.Contains(c) && c.X >= dx && c.X <= dx + dest.RectW && c.Y >= dest.Y && c.Y <= dest.Y + dest.RectH + 4);
            var data = cells.Where(c => !used.Contains(c) && c != key && c.Y >= barTop && c.Y < barTop + 60 && c.X >= 200 && c.X < 560)
                .OrderBy(c => c.X).ToList();
            if (key != null && data.Count > 0)
            {
                used.Add(key);
                used.UnionWith(data);
                var values = data.Where(c => c.Colour != "grey").ToList();
                string Unit(Cell v) => data.FirstOrDefault(u => u.Colour == "grey" && u.X > v.X && u.X - v.X < 40) is { } u ? " " + u.Text : "";
                var named = values.Select((v, i) => $"{(i switch { 0 => "time", 1 => "EFOB", 2 => "distance", _ => "value" })} {Word(v.Text)}{Unit(v)}");
                lines.Add((dest.Y, 0, new MfdLine("button", $"Destination {key.Text}: {string.Join(", ", named)}, {values.First().Colour}",
                    dx + dest.RectW / 2, dest.Y + dest.RectH / 2)));
            }
        }
    }

    /// <summary>The row of values under a row of white headings: the first row within 60 px below it, all of it not white,
    /// under the headings' span (DEPARTURE's FROM VCBI row sits between its headings and their values); -1 if none.</summary>
    private static int ValuesUnder(IReadOnlyList<IReadOnlyList<DrawnCell>> rows, int header, HashSet<int> paired)
    {
        var h = rows[header];
        float x0 = h.Min(c => c.X) - 40, x1 = h.Max(c => c.X) + 80;
        for (int j = header + 1; j < rows.Count && rows[j][0].Y - h[0].Y <= 60; j++)
        {
            var v = rows[j];
            if (paired.Contains(j) || v.Any(c => c.Colour == "white") || v.All(c => c.Colour == "grey")) continue;
            if (v.All(c => c.X >= x0 && c.X <= x1)) return j;
        }
        return -1;
    }

    /// <summary>Headings and their values as one line: "RWY none, LENGTH none M, CRS none°, green". A grey unit belongs
    /// to the value left of it (its "d" a degree sign); values pair with headings in order when the counts match,
    /// else with the nearest heading.</summary>
    private static string Table(IReadOnlyList<DrawnCell> headers, IReadOnlyList<DrawnCell> values)
    {
        var vals = values.Where(c => c.Colour != "grey").OrderBy(c => c.X).ToList();
        string With(DrawnCell v)
        {
            var unit = values.Where(u => u.Colour == "grey" && u.X > v.X && vals.LastOrDefault(o => o.X < u.X) == v).Select(u => u.Text).FirstOrDefault();
            return Word(v.Text) + (unit == null ? "" : unit == "d" ? "°" : " " + unit);
        }
        var hs = headers.OrderBy(c => c.X).ToList();
        var parts = vals.Count == hs.Count
            ? hs.Select((h, i) => $"{h.Text} {With(vals[i])}")
            : vals.Select(v => $"{hs.OrderBy(h => Math.Abs(h.X - v.X)).First().Text} {With(v)}");
        return $"{string.Join(", ", parts)}, {vals.First().Colour}";
    }

    private static bool IsField(DrawnElement e) => e.Class != "NotepadTextbox" && e.RectW > 0 && e.RectH > 0
        && (e.Class.EndsWith("TextBox", StringComparison.Ordinal) || e.Class.EndsWith("Textbox", StringComparison.Ordinal));

    /// <summary>A drawn token as said: amber boxes ("oooo") are a required entry, dashes nothing yet, "dC" degrees
    /// Celsius, a checklist's line to fill ("____") blank. Dot leaders are colons already (<see cref="A350Ewd"/>).</summary>
    private static string Word(string token) =>
        token.Length > 0 && token.All(ch => ch == 'o') ? "required"
        : token.Length > 0 && token.Contains('-') && token.All(ch => ch is '-' or ':' or '.') ? "none"
        : token == "dC" ? "°C" : Blank.Replace(token, "blank");

    /// <summary>A field's tokens as said; a lone "d" is the degree sign of the number before it ("F040 d").</summary>
    private static string Words(IEnumerable<string> tokens)
    {
        var words = new List<string>();
        foreach (var t in tokens)
        {
            if (t == "d" && words.Count > 0) words[^1] += "°";
            else words.Add(Word(t));
        }
        return string.Join(" ", words);
    }

    private static readonly Regex Blank = new(@"_{3,}", RegexOptions.Compiled);

    private static A350Rgba ColourOf(string name) => name switch
    {
        "green" => new A350Rgba(0.15f, 0.85f, 0.1f, 1), "amber" => new A350Rgba(1, 0.55f, 0, 1),
        "cyan" => new A350Rgba(0.4f, 0.86f, 1, 1), "red" => new A350Rgba(1, 0.1f, 0.1f, 1),
        "magenta" => new A350Rgba(1, 0.4f, 1, 1), "black" => new A350Rgba(0, 0, 0, 1),
        "grey" => new A350Rgba(0.6f, 0.6f, 0.6f, 1), _ => new A350Rgba(1, 1, 1, 1),
    };
}
