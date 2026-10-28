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

    /// <summary>The font's degree sign after a number: a position's "07d10.5N/079d53.1E", a course's "C220d".</summary>
    private static readonly Regex PositionDegrees = new(@"(?<=\d)d(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>A symbol the MFD font draws as a lowercase letter after an ident (A350MFD-Regular.ttf's outlines: a a left
    /// arrow, b a right arrow, f the overfly triangle; its other lowercase letters are small capitals). An arrow is the
    /// procedure's turn there, read in words (BASU1D's BI611, the right turn after 500 ft; a screen reader may not say
    /// a bare arrow).</summary>
    private static readonly Regex IdentSymbol = new(@"^(?<id>[A-Z0-9][A-Z0-9/.-]*)(?<g>[abf])$", RegexOptions.Compiled);

    /// <summary>Decoration: a run of spaced dashes around a marker ("- - - - END OF TMPY F-PLN - - - -").</summary>
    private static readonly Regex MarkerDashes = new(@"\s*(?:-\s){2,}-\s*", RegexOptions.Compiled);

    private static string Glyphs(string text)
    {
        var t = MarkerDashes.Replace(text, " ").Trim();
        if (t.Length == 0) t = text.Trim();
        t = PositionDegrees.Replace(t, "°");
        var m = IdentSymbol.Match(t);
        return m.Success
            ? m.Groups["id"].Value + (m.Groups["g"].Value switch { "a" => " (left turn)", "b" => " (right turn)", _ => " (overfly)" })
            : t;
    }

    /// <summary>A drawn text; <see cref="Index"/> is its place in the draw order.</summary>
    private sealed record Cell(string Text, string Colour, float X, float Y, int Index);

    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? 0 : HalfWidth;
        bool Mine(float x) => side == 0 ? x < HalfWidth : x >= HalfWidth;
        var cells = capture.Texts.Select((t, i) => (t, i)).Where(p => Mine(p.t.X) && !string.IsNullOrWhiteSpace(p.t.Text))
            .Select(p => new Cell(Glyphs(Spaces.Replace(A350Ewd.WithoutLeaders(p.t.Text), " ")), p.t.ColourName, p.t.X - shift, p.t.Y, p.i))
            .Where(c => c.Text.Length > 0).ToList();
        cells = JoinTimes(cells);
        // The green dot speed's label is no text but a drawn green circle, a small square stroke (PERF T.O, live).
        foreach (var sh in (capture.Shapes ?? Array.Empty<DrawnShape>()).Where(sh => sh.Kind == "stroke" && sh.Image == 0
                     && Mine((sh.X0 + sh.X1) / 2) && A350Rgba.NameOf(sh.Colour) == "green"
                     && sh.X1 - sh.X0 is >= 14 and <= 26 && Math.Abs(sh.X1 - sh.X0 - (sh.Y1 - sh.Y0)) <= 3))
            cells.Add(new Cell("Green dot", "white", sh.X0 - shift, sh.Y1, -1));
        // An open pop-up hides what was drawn under it before it: those texts and controls are not on the screen (SURV's
        // DEFAULT SETTINGS confirmation over the ADS-B TRAFFIC box and the DEFAULT SETTINGS key, live). Open is a
        // PopupDialog with texts of its own drawn after it inside it; a dismissed one keeps its element and draws none.
        // The CRASH REPORTING consent (EULADialog, over DATA/STATUS after an update) is one too.
        var elements = capture.Elements.Where(e => Mine(e.X) && e.RectW > 0 && e.RectH > 0).ToList();
        var popups = elements.Select((e, i) => (e, i)).Where(p => p.e.Class is "PopupDialog" or "EULADialog"
            && cells.Any(c => c.Index >= p.e.TextStart && Covers(p.e, c.X, c.Y))).ToList();
        // An open drop-down menu's items (DropdownButtonUI with their own texts drawn after them) hide what they cover,
        // as a pop-up does: ACTIVE's menu over DATA/STATUS's left column (live 2026-10-28).
        var menuItems = elements.Select((e, i) => (e, i)).Where(p => p.e.Class == "DropdownButtonUI"
            && cells.Any(c => c.Index >= p.e.TextStart && Covers(p.e, c.X, c.Y))).ToList();
        popups.AddRange(menuItems);
        // A waypoint revision's pop-up (INSERT NEXT WPT, NEW DEST) hides the F-PLN in the revision menu's column under it.
        var revisionPopup = RevisionPopupOf(capture, cells, Mine);
        if (revisionPopup != null)
            cells.RemoveAll(c => c.Index < revisionPopup.Start && Covers(revisionPopup.Column, c.X, c.Y));
        // The consent dialog is modal: the slivers of the page left and right of it (DATA/STATUS's NAV DATABASE label,
        // which ran into the dialog's line level with it) are behind it too.
        bool Covers(DrawnElement p, float x, float y) =>
            (p.Class == "EULADialog" || (x >= p.X - shift && x <= p.X - shift + p.RectW)) && y >= p.Y && y <= p.Y + p.RectH;
        cells.RemoveAll(c => popups.Any(p => c.Index >= 0 && c.Index < p.e.TextStart && Covers(p.e, c.X, c.Y)));
        var hidden = elements.Select((e, i) => (e, i)).Where(q => popups.Any(p => q.i < p.i
            && Covers(p.e, q.e.X - shift + q.e.RectW / 2, q.e.Y + q.e.RectH / 2))).Select(q => q.e).ToHashSet();
        var used = new HashSet<Cell>();
        var lines = new List<(float Top, float Left, MfdLine Line)>();
        var fields = new List<(float Top, float X0, float X1, Cell? Label, string? Value, float Cx, float Cy, bool Inherited, bool Above)>();
        var leftLabels = new HashSet<Cell>();
        var checkboxLabels = new HashSet<Cell>();
        var headers = new HashSet<Cell>();
        // Texts inside a control are its own, never another control's label or header.
        var inControls = elements.Where(e => !hidden.Contains(e) && !Containers.Contains(e.Class)).SelectMany(e =>
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            return cells.Where(c => c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4);
        }).ToHashSet();
        var inFields = elements.Where(e => !hidden.Contains(e) && IsField(e)).SelectMany(e =>
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            return cells.Where(c => c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4);
        }).ToHashSet();

        // A waypoint's open revision menu (its items with their own texts drawn after them): its items are its own, never
        // the F-PLN's column headers (FROM P.POS DIR TO, white, sits in their band, live 2026-10-28).
        var revisionItems = elements.Where(e => !hidden.Contains(e) && e.Class == RevisionItem
            && cells.Any(c => c.Index >= e.TextStart && Covers(e, c.X, c.Y))).ToList();
        var revisionCells = cells.Where(c => revisionItems.Any(e => c.Index >= e.TextStart && Covers(e, c.X, c.Y))).ToHashSet();

        bool flightPlan = cells.Any(c => c.Colour == "black" && c.Y < 150 && c.Text.EndsWith("F-PLN", StringComparison.Ordinal));
        if (flightPlan)
            ReadFlightPlan(capture, shift, Mine, cells.Where(c => !revisionCells.Contains(c)).ToList(), used, lines);

        foreach (var e in elements.Where(e => !hidden.Contains(e) && !Containers.Contains(e.Class)))
        {
            // The F-PLN's row boxes are the F-PLN reader's (hidden rows' boxes reach under the bottom keys).
            if (flightPlan && e.Class == "BaseFlightPlanButton") continue;
            float x0 = e.X - shift, y0 = e.Y, x1 = x0 + e.RectW, y1 = y0 + e.RectH + 4;
            // A control holds only what was drawn after it began: an open menu covers list rows drawn before it.
            // The scratchpad's message is drawn before its box (live).
            int from = e.Class == "NotepadTextbox" ? 0 : e.TextStart;
            var inside = InReadingOrder(cells.Where(c => !used.Contains(c) && c.Index >= from && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1));
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
                // A box with its label right of it, outside its rectangle (PERF T.O's TOGA and FLEX, any colour: FLEX
                // is cyan while selected).
                if (inside.Count == 0
                    && cells.Where(c => !used.Contains(c) && !inControls.Contains(c) && c.X > x1 && c.X - x1 <= 60
                        && Math.Abs(c.Y - (y0 + e.RectH * 0.75f)) <= 12).OrderBy(c => c.X).FirstOrDefault() is { } boxLabel)
                {
                    inside = new List<Cell> { boxLabel };
                    value = boxLabel.Text;
                    colour = boxLabel.Colour;
                    checkboxLabels.Add(boxLabel);
                }
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
                bool OnRow(Cell c) => !inside.Contains(c) && !inControls.Contains(c) && (c.Colour == "white" || checkboxLabels.Contains(c))
                    && Math.Abs(c.Y - rowY) <= 12;
                // The nearest label left of the field, one field each (DATA STATUS: IDLE [ ] PERF [ ] [ ]); a label
                // between two columns (RAD NAV's IDENT, FREQ, CRS) also stands right of the left column's fields.
                // (A label beside a field sits close to it, within 200 px: FUEL&LOAD's row label DEST, 465 px left of its MIN
                // FUEL AT DEST field, is the table row's, the field's the heading above it.)
                var beside = cells.Where(c => OnRow(c) && c.X < x0 && x0 - c.X <= 200 && !leftLabels.Contains(c)).OrderByDescending(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => OnRow(c) && c.X > x1 && c.X - x1 <= 120).OrderBy(c => c.X).FirstOrDefault();
                var label = beside
                    // else a label just above it (SURV's SQWK; FUEL&LOAD's MIN FUEL AT DEST heading, 26 px up)
                    ?? cells.Where(c => !inControls.Contains(c) && c.Colour == "white" && c.X >= x0 - 40 && c.X <= x1 && c.Y < y0 && y0 - c.Y <= 30)
                        .OrderByDescending(c => c.Y).FirstOrDefault();
                // An empty box labelled only by a text above it is no entry the page shows (FUEL&LOAD, under CI).
                if (inside.Count == 0 && (label == null || beside == null)) continue;
                used.UnionWith(inside);
                if (label != null && label.X < x0) leftLabels.Add(label);
                fields.Add((y0, x0, x1, label, inside.Count == 0 ? null : $"{value}, {colour}", cx, cy, false, beside == null));
                continue;
            }
            if (inside.Count == 0) continue;
            used.UnionWith(inside);
            string menuLabel = "";
            if (e.Class == "MainHeaderDropdownMenu" && y0 > 100
                && (cells.Where(c => !used.Contains(c) && !inControls.Contains(c) && c.Colour == "white" && c.X < x0 && x0 - c.X <= 160
                        && Math.Abs(c.Y - (y0 + e.RectH * 0.75f)) <= 12).OrderByDescending(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => !used.Contains(c) && !inControls.Contains(c) && c.Colour == "white" && c.X >= x0 - 40 && c.X <= x1
                        && c.Y < y0 && y0 - c.Y <= 30).OrderByDescending(c => c.Y).FirstOrDefault()) is { } ml)
            {
                used.Add(ml);
                menuLabel = ml.Text.TrimEnd(':') + ": ";
            }
            var (kind, spoken) = e.Class switch
            {
                "MainHeaderDropdownMenu" => ("menu", $"{menuLabel}{value} menu, {colour}"),
                "WidgetSwitcherTab" => ("tab", $"{value} tab"),
                // An open menu's item: a drop-down's, or a waypoint's revision menu's.
                "DropdownButtonUI" or RevisionItem => ("menuitem", colour == "white" ? $"{value} button" : $"{value} button, {colour}"),
                _ => ("button", colour == "white" ? $"{value} button" : $"{value} button, {colour}"),
            };
            lines.Add((y0, x0, new MfdLine(kind, spoken, cx, cy)));
        }

        // A field with no label of its own shares the label of the field left of it on its row (FUEL&LOAD's RTE RSV
        // weight and percentage, FINAL fuel and time).
        for (int k = 0; k < fields.Count; k++)
        {
            var f = fields[k];
            if (f.Label != null) continue;
            var left = fields.Where(o => o.Label != null && !o.Inherited && Math.Abs(o.Top - f.Top) <= 12 && o.X1 <= f.X0 + 1 && f.X0 - o.X1 <= 60)
                .OrderByDescending(o => o.X1).FirstOrDefault();
            if (left.Label != null) fields[k] = f with { Label = left.Label, Inherited = true };
        }

        // A label two fields share takes each field's column header: the nearest text above it in its column.
        foreach (var f in fields)
        {
            string name = f.Label?.Text ?? "Field";
            if (f.Label != null && !f.Inherited && !f.Above && fields.Count(o => o.Label == f.Label && !o.Inherited && !o.Above) > 1)
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
            .Select(c => new DrawnText(c.Text, c.X, c.Y, c.X, c.Y, 0, A350Rgba.Of(c.Colour)))).ToList();
        var paired = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (paired.Contains(i) || rows[i].Any(c => c.Colour != "white")) continue;
            int last = i;
            for (int j = i + 1; j < rows.Count && rows[j][0].Y - rows[last][0].Y <= (rows[i].Count == 1 ? 50 : 60); j++)
            {
                if (paired.Contains(j)) continue;
                if (TableRow(rows[i], rows[j]) is not { } line)
                {
                    if (last != i) break;   // the rows of a table follow each other; before the first, a row may sit between
                    continue;
                }
                paired.Add(j);
                lines.Add((rows[j][0].Y - 14, rows[j][0].X, new MfdLine("text", line, rows[j][0].X, rows[j][0].Y - 7)));
                last = j;
            }
            if (last != i) paired.Add(i);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (paired.Contains(i)) continue;
            var row = rows[i];
            string spoken = Row(row);
            if (row.All(c => c.Colour != "white")
                && fields.Where(f => f.Label != null && f.X1 <= row[0].X + 1 && Math.Abs(f.Top + 30 - row[0].Y) <= 12)
                    .OrderByDescending(f => f.X1).FirstOrDefault() is { Label: { } owner })
                spoken = $"{owner.Text}: {spoken}";
            lines.Add((row[0].Y - 14, row[0].X, new MfdLine("text", spoken, row[0].X, row[0].Y - 7)));
        }

        var ordered = lines.OrderBy(l => l.Top).ThenBy(l => l.Left).Select(l => l.Line).ToList();
        if (menuItems.Count > 0)
            ordered = WithMenuAfterItsButton(ordered, menuItems.Select(m => m.e).ToList(), shift);
        if (revisionItems.Count > 0)
            ordered = WithRevisionMenuAfterTitle(ordered, revisionItems, shift);
        if (revisionPopup != null)
            ordered = ordered.Select(l => l.Kind == "field" && revisionPopup.Fields.Any(e => AtCentre(l, e, shift))
                ? l with { Spoken = revisionPopup.Title + ": " + ValueOf(l.Spoken) } : l).ToList();
        return ordered;
    }

    /// <summary>A field line's value: what follows its name ("C220°: none, cyan" → "none, cyan").</summary>
    private static string ValueOf(string spoken) => spoken.IndexOf(": ", StringComparison.Ordinal) is int at and >= 0 ? spoken[(at + 2)..] : spoken;

    /// <summary>An open waypoint revision pop-up: the revision menu's column it draws in, the draw indices of its texts
    /// ([Start, End)), its title (its top row: "INSERT NEXT WPT FROM BASUR") and its entry fields.</summary>
    private sealed record RevisionPopup(DrawnElement Column, int Start, int End, string Title, IReadOnlyList<DrawnElement> Fields);

    /// <summary>
    /// INSERT NEXT WPT and NEW DEST (NextWaypointPopup, NewDestPopup) have no rectangle of their own: they draw in the
    /// revision menu's column (UIFlightplanRevisionsMenu) after it, one after the other, before the page frame
    /// (MessageListButton, the header). One is open while texts are drawn between its start and the next of those, below
    /// the title band (1.2.7, live 2026-10-28); a closed one draws none there.
    /// </summary>
    private static RevisionPopup? RevisionPopupOf(A350DrawCapture capture, List<Cell> cells, Func<float, bool> mine)
    {
        var all = capture.Elements;
        var column = all.FirstOrDefault(e => e.Class == "UIFlightplanRevisionsMenu" && mine(e.X) && e.RectW > 0 && e.RectH > 0);
        if (column == null)
            return null;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Class is not ("NextWaypointPopup" or "NewDestPopup"))
                continue;
            int start = all[i].TextStart;
            int end = all.Skip(i + 1).Where(e => e.Class is "NewDestPopup" or "MessageListButton" or "DropdownMenuContainer" or "MainHeaderUIButton")
                .Select(e => e.TextStart).DefaultIfEmpty(int.MaxValue).First();
            // Below the page title's band: the page frame drawn next (the flight number, the title) is not the pop-up's.
            var mineTexts = cells.Where(c => c.Index >= start && c.Index < end && c.Y >= 150).ToList();
            if (mineTexts.Count == 0)
                continue;
            float top = mineTexts.Min(c => c.Y);
            string title = string.Join(" ", mineTexts.Where(c => c.Y - top <= 10).OrderBy(c => c.X).Select(c => c.Text));
            var fields = all.Skip(i + 1).TakeWhile(e => e.TextStart < end)
                .Where(e => e.Class is "BaseUITextBox" or "DropdownTextBox" && mine(e.X)).ToList();
            return new RevisionPopup(column, start, end, title, fields);
        }
        return null;
    }

    /// <summary>A button of a waypoint's revision menu (UIFlightplanRevisionsMenu).</summary>
    private const string RevisionItem = "FlightplanButtonActionButton";

    /// <summary>Where the MFD window moves when a menu has just opened: its first item, as a sighted pilot's eyes go to
    /// it; null while none opened since the last read (an open menu keeps the pilot's own place).</summary>
    public static int? MenuJustOpened(IReadOnlyList<MfdLine> before, IReadOnlyList<MfdLine> now)
    {
        if (before.Any(l => l.Kind == "menuitem"))
            return null;
        for (int i = 0; i < now.Count; i++)
            if (now[i].Kind == "menuitem")
                return i;
        return null;
    }

    private static bool AtCentre(MfdLine l, DrawnElement e, float shift) =>
        Math.Abs(l.ClickX - (e.X - shift + e.RectW / 2)) < 1 && Math.Abs(l.ClickY - (e.Y + e.RectH / 2)) < 1;

    /// <summary>A waypoint's revision menu is drawn at a fixed place and names no waypoint: its items read together
    /// after the page title, top to bottom (the MFD window moves to the first when a menu opens).</summary>
    private static List<MfdLine> WithRevisionMenuAfterTitle(List<MfdLine> lines, List<DrawnElement> items, float shift)
    {
        bool IsItem(MfdLine l) => items.Any(e => AtCentre(l, e, shift));
        var menu = lines.Where(IsItem).OrderBy(l => l.ClickY).ToList();
        var result = lines.Where(l => !IsItem(l)).ToList();
        int title = result.FindIndex(l => l.Kind == "text" && l.Spoken.Contains("F-PLN", StringComparison.Ordinal));
        result.InsertRange(title + 1, menu);
        return result;
    }

    /// <summary>An open drop-down's items read right after the button or menu it hangs from (the one above its column),
    /// which says the menu is open; a sighted pilot sees the list hanging off it.</summary>
    private static List<MfdLine> WithMenuAfterItsButton(List<MfdLine> lines, List<DrawnElement> items, float shift)
    {
        float x0 = items.Min(e => e.X) - shift, x1 = items.Max(e => e.X + e.RectW) - shift, top = items.Min(e => e.Y);
        bool IsItem(MfdLine l) => items.Any(e => Math.Abs(l.ClickX - (e.X - shift + e.RectW / 2)) < 1
            && Math.Abs(l.ClickY - (e.Y + e.RectH / 2)) < 1);
        var menu = lines.Where(IsItem).ToList();
        var owner = lines.Where(l => !IsItem(l) && l.Kind is "button" or "menu" && l.ClickY < top && l.ClickX >= x0 && l.ClickX <= x1)
            .OrderByDescending(l => l.ClickY).FirstOrDefault();
        if (owner == null || menu.Count == 0)
            return lines;
        var result = lines.Where(l => !IsItem(l)).ToList();
        int at = result.IndexOf(owner);
        result[at] = owner with { Spoken = owner.Spoken + ", menu open" };
        result.InsertRange(at + 1, menu);
        return result;
    }

    private static readonly Regex TrackDegrees = new(@"^(\d{1,3})d$", RegexOptions.Compiled);

    /// <summary>A leg's flight path angle, signed with one decimal ("-3.0").</summary>
    private static readonly Regex FlightPathAngle = new(@"^[+-]\d{1,2}\.\d$", RegexOptions.Compiled);

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
            .Select(e => (X: e.X - shift, e.Y, W: e.RectW, H: e.RectH)).ToList();   // none while the plan is empty
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

        // A ditto mark is the value above (the speed a row keeps), in words.
        static string Value(Cell c) => c.Text == "\"" ? "same as above"
            : TrackDegrees.Match(c.Text) is { Success: true } m ? m.Groups[1].Value + "°" : Word(c.Text);
        string ValueOf(Cell? c, string rowColour) =>
            c == null ? "none" : Value(c) + (c.Colour != rowColour && c.Colour != "grey" ? $" ({c.Colour})" : "");

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
                    lines.Add((b.Y, 0, new MfdLine("text", $"{inBox[0].Text}, {inBox[0].Colour}", b.X + b.W / 2, b.Y + b.H / 2)));
                prevBottom = b.Y + b.H;
                continue;
            }
            var ident = inBox.OrderBy(c => c.X).FirstOrDefault();
            if (ident == null) { prevBottom = b.Y + b.H; continue; }
            used.Add(ident);
            var row = cells.Where(c => !used.Contains(c) && InList(c) && Math.Abs(c.Y - ident.Y) <= 6 && c.X >= 200 && c.X < 563).ToList();
            // A column's cells left to right: its value, and a constraint's star read as its prediction (the Airbus FMS's
            // magenta star met, amber missed; live, BI613's "*" and 5000).
            string Column(float x0, float x1, string rowColour)
            {
                var inColumn = row.Where(c => c.X >= x0 && c.X < x1).OrderBy(c => c.X).ToList();
                var star = inColumn.FirstOrDefault(c => c.Text == "*");
                var values = inColumn.Where(c => c != star).Select(c => ValueOf(c, rowColour)).ToList();
                string text = values.Count == 0 ? "none" : string.Join(" ", values);
                return star == null ? text : text + star.Colour switch
                {
                    "magenta" => " (constraint met)",
                    "amber" => " (constraint missed)",
                    _ => $" (constraint, {star.Colour})",
                };
            }
            used.UnionWith(row);
            var leg = cells.Where(c => !used.Contains(c) && InList(c) && c.Y >= prevBottom && c.Y < b.Y && (c.X < 200 || c.X >= 560)).ToList();
            used.UnionWith(leg);
            // The row's colour: its values' most common (the TO waypoint's ident is white in a yellow plan).
            string colour = row.Concat(leg.Where(c => c.X >= 560)).Where(c => c.Colour != "grey").GroupBy(c => c.Colour)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? ident.Colour;
            var parts = new List<string>();
            if (leg.FirstOrDefault(c => c.X < 200) is { } via) parts.Add("via " + via.Text);
            foreach (var c in leg.Where(c => c.X >= 560).OrderBy(c => c.X))
            {
                // A signed decimal is the leg's flight path angle (the ILS final's "-3.0", white), drawn beside its
                // distance, nearer the DIST header than the FPA one (live, OMDB 12L).
                if (FlightPathAngle.IsMatch(c.Text))
                {
                    parts.Add($"FPA {c.Text}°" + (c.Colour != colour && c.Colour != "grey" ? $" ({c.Colour})" : ""));
                    continue;
                }
                var name = legHeaders.OrderBy(h => Math.Abs(h.X - c.X)).FirstOrDefault()?.Text;
                parts.Add(name == null ? ValueOf(c, colour) : $"{name} {ValueOf(c, colour)}");
            }
            string label = (b.Y < 230 ? $"{from} {ident.Text}" : ident.Text) + (ident.Colour != colour ? $" ({ident.Colour})" : "");
            string legText = parts.Count > 0 ? string.Join(", ", parts) + "; " : "";
            lines.Add((b.Y, 0, new MfdLine("waypoint",
                $"{label}: {legText}{time} {Column(200, 334, colour)}, {spd} {Column(334, 422, colour)}, {alt} {Column(422, 563, colour)}, {colour}",
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

    /// <summary>A row's texts as tokens: each read as a word, a time drawn in parts joined ("--", grey ":", "--"), a
    /// grey unit kept with the value left of it (its "d" a degree sign).</summary>
    private static List<DrawnCell> Tokens(IReadOnlyList<DrawnCell> row)
    {
        var tokens = new List<DrawnCell>();
        foreach (var c in row.OrderBy(c => c.X))
        {
            var prev = tokens.LastOrDefault();
            if (c.Colour == "grey" && prev != null && prev.Colour is not ("grey" or "white") && c.X - prev.X <= 60)
                tokens[^1] = prev with { Text = prev.Text + (c.Text == "d" ? "°" : " " + c.Text) };
            else
                tokens.Add(c with { Text = Word(c.Text) });
        }
        return tokens;
    }

    /// <summary>A text row as spoken: a row of white labels and values reads "LABEL: value, value; LABEL: value, colour"
    /// (a label with values both sides, RAD NAV's CLASS, takes both); any other row by its colour runs.</summary>
    private static string Row(IReadOnlyList<DrawnCell> row)
    {
        var tokens = Tokens(row);
        var values = tokens.Where(t => t.Colour is not ("white" or "grey" or "black")).ToList();
        if (values.Count == 0 || !tokens.Any(t => t.Colour == "white"))
            return A350DrawLayout.Speak(tokens);
        string colour = values.GroupBy(v => v.Colour).OrderByDescending(g => g.Count()).First().Key;
        string Value(DrawnCell v) => v.Text + (v.Colour != colour ? $" ({v.Colour})" : "");
        var segments = new List<(string? Label, List<string> Values)>();
        foreach (var t in tokens)
        {
            if (t.Colour == "white") segments.Add((t.Text, new List<string>()));
            else if (t.Colour != "grey" && t.Colour != "black")
            {
                if (segments.Count == 0) segments.Add((null, new List<string>()));
                segments[^1].Values.Add(Value(t));
            }
        }
        if (segments.Count > 1 && segments[0].Label == null)
        {
            segments[1].Values.InsertRange(0, segments[0].Values);   // values left of a centre label are its too
            segments.RemoveAt(0);
        }
        string Segment((string? Label, List<string> Values) g) => g.Label == null ? string.Join(", ", g.Values)
            : g.Values.Count == 0 ? g.Label
            : $"{(g.Label.EndsWith(':') ? g.Label : g.Label + ":")} {string.Join(", ", g.Values)}";
        return $"{string.Join("; ", segments.Select(Segment))}, {colour}";
    }

    /// <summary>A row of a table under white headings, or null: its values pair with the headings (in order when the
    /// counts match, else with the nearest heading; LENGTH's value ends nearer CRS than LENGTH); a white row label and
    /// any values left of the first heading name the row (FUEL&amp;LOAD's "DEST OMDB"); a single heading takes all.</summary>
    private static string? TableRow(IReadOnlyList<DrawnCell> headers, IReadOnlyList<DrawnCell> row)
    {
        var hs = headers.OrderBy(c => c.X).ToList();
        var tokens = Tokens(row);
        int start = tokens.Count > 0 && tokens[0].Colour == "white" && hs.Count > 1 ? 1 : 0;
        if (tokens.Skip(start).Any(t => t.Colour == "white")) return null;
        float first = hs[0].X - 40;
        var label = tokens.Take(start).Concat(tokens.Skip(start).Where(t => t.X < first && hs.Count > 1)).ToList();
        var vals = tokens.Where(t => !label.Contains(t) && t.Colour is not ("grey" or "black")).ToList();
        if (vals.Count == 0) return null;
        float x0 = hs.Count == 1 ? hs[0].X - 150 : first, x1 = hs.Count == 1 ? hs[0].X + 150 : hs[^1].X + 160;
        if (vals.Any(v => v.X < x0 || v.X > x1)) return null;
        string colour = vals.GroupBy(v => v.Colour).OrderByDescending(g => g.Count()).First().Key;
        string Value(DrawnCell v) => v.Text + (v.Colour != colour ? $" ({v.Colour})" : "");
        string body = hs.Count == 1
            ? $"{hs[0].Text}: {string.Join(", ", vals.Select(Value))}"
            : string.Join(", ", vals.Count == hs.Count
                ? hs.Select((h, i) => $"{h.Text} {Value(vals[i])}")
                : vals.Select(v => $"{hs.OrderBy(h => Math.Abs(h.X - v.X)).First().Text} {Value(v)}"));
        return label.Count == 0 ? $"{body}, {colour}" : $"{string.Join(" ", label.Select(l => l.Text))}: {body}, {colour}";
    }

    /// <summary>Texts in reading order: by line (within 8 px of a line's top text: a value sits a few px below its unit),
    /// then left to right.</summary>
    private static List<Cell> InReadingOrder(IEnumerable<Cell> cells)
    {
        var result = new List<Cell>();
        foreach (var line in cells.OrderBy(c => c.Y).Aggregate(new List<List<Cell>>(), (ls, c) =>
                 {
                     if (ls.Count == 0 || c.Y - ls[^1][0].Y > 8) ls.Add(new List<Cell>());
                     ls[^1].Add(c);
                     return ls;
                 }))
            result.AddRange(line.OrderBy(c => c.X));
        return result;
    }

    private static readonly Regex TimePart = new(@"^[0-9-]{1,2}$", RegexOptions.Compiled);

    /// <summary>A time drawn as "--", a grey ":" and "--" (FUEL&amp;LOAD, live) as one text.</summary>
    private static List<Cell> JoinTimes(List<Cell> cells)
    {
        var colons = cells.Where(c => c.Text == ":" && c.Colour == "grey").ToList();
        foreach (var colon in colons)
        {
            var a = cells.Where(c => TimePart.IsMatch(c.Text) && Math.Abs(c.Y - colon.Y) <= 4 && c.X < colon.X && colon.X - c.X <= 14)
                .OrderByDescending(c => c.X).FirstOrDefault();
            var b = cells.Where(c => TimePart.IsMatch(c.Text) && Math.Abs(c.Y - colon.Y) <= 4 && c.X > colon.X && c.X - colon.X <= 14)
                .OrderBy(c => c.X).FirstOrDefault();
            if (a == null || b == null) continue;
            cells.Remove(colon);
            cells.Remove(b);
            cells[cells.IndexOf(a)] = a with { Text = a.Text + ":" + b.Text };
        }
        return cells;
    }

    private static bool IsField(DrawnElement e) => e.Class != "NotepadTextbox" && e.RectW > 0 && e.RectH > 0
        && (e.Class.EndsWith("TextBox", StringComparison.Ordinal) || e.Class.EndsWith("Textbox", StringComparison.Ordinal));

    /// <summary>A drawn token as said: amber boxes ("oooo") are a required entry, dashes nothing yet, "dC" degrees
    /// Celsius, a checklist's line to fill ("____") blank. Dot leaders are colons already (<see cref="A350Ewd"/>).</summary>
    private static string Word(string token) =>
        token.Length > 0 && token.Contains('o') && token.All(ch => ch is 'o' or '.') ? "required"
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

}
