using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One line of the MFD as a pilot moves through it: what kind it is (menu, button, tab, field,
/// scratchpad, text), how it is spoken, and where on the side's own MFD a click lands on it. <see cref="Carried"/>: how
/// many reads in a row it has been its last whole reading in place of a torn one (<see cref="A350MfdPage.KeepReadable"/>).</summary>
public sealed record MfdLine(string Kind, string Spoken, float ClickX, float ClickY, bool Popup = false)
{
    public int Carried { get; init; }
}

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
        "WidgetSwitcherUI", "PopupDialog", "CustomPopupDialog", "EULADialog", "DropdownMenuContainer", "SurvControlsBox",
        "UIFlightplanRevisionsMenu", "NextWaypointPopup", "NewDestPopup",
    };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>The font's degree sign after a number: a position's "07d10.5N/079d53.1E", a course's "C220d".</summary>
    private static readonly Regex PositionDegrees = new(@"(?<=\d)d(?![A-Za-z])", RegexOptions.Compiled);

    /// <summary>D-ATIS's time beside its airport.</summary>
    private static readonly Regex AtisTime = new(@"^- ?\d{4}Z$", RegexOptions.Compiled);

    /// <summary>A symbol the MFD font draws as a lowercase letter after an ident (A350MFD-Regular.ttf's outlines: a a left
    /// arrow, b a right arrow, f the overfly triangle; its other lowercase letters are small capitals). An arrow is the
    /// procedure's turn there, read in words (BASU1D's BI611, the right turn after 500 ft; a screen reader may not say
    /// a bare arrow).</summary>
    private static readonly Regex IdentSymbol = new(@"^(?<id>[A-Z0-9][A-Z0-9/.-]*)(?<g>[abf])$", RegexOptions.Compiled);

    /// <summary>Decoration: a run of spaced dashes around a marker ("- - - - END OF TMPY F-PLN - - - -").</summary>
    private static readonly Regex MarkerDashes = new(@"\s*(?:-\s){2,}-\s*", RegexOptions.Compiled);

    /// <summary>The right arrow between two words of a key: the mode it switches to (FCU BKUP's "SPD b MACH", "MAG b TRUE").</summary>
    private static readonly Regex WordArrow = new(@"(?<=\S) b (?=\S)", RegexOptions.Compiled);

    /// <summary>The triangle before a word is a delta (FUEL&amp;LOAD's "fFUEL", "fTIME").</summary>
    private static readonly Regex Delta = new(@"(?<![A-Za-z0-9])f(?=[A-Z])", RegexOptions.Compiled);

    internal static string Glyphs(string text)
    {
        var t = MarkerDashes.Replace(text, " ").Trim();
        if (t.Length == 0) t = text.Trim();
        t = PositionDegrees.Replace(t, "°");
        var m = IdentSymbol.Match(t);
        return m.Success
            ? m.Groups["id"].Value + (m.Groups["g"].Value switch { "a" => " (left turn)", "b" => " (right turn)", _ => " (overfly)" })
            : Delta.Replace(WordArrow.Replace(t, " to "), "delta ");
    }

    /// <summary>What a torn text reads as: a copy read across a frame caught a label the A350 rebuilds every frame freed,
    /// its bytes a malloc chunk's pointers (FCU BKUP's HPA -> INHG key, live 2026-10-30). No A350 MFD text holds a control
    /// character (127 captures); a torn one nearly always does, but not every one: a freed block the sim has reused can hold
    /// another string's printable text (a torn VERT REV title read "12000.000000").</summary>
    public const string Unreadable = "unreadable";

    private static bool Torn(string text) => text.Any(char.IsControl);

    /// <summary>A drawn text; <see cref="Index"/> is its place in the draw order.</summary>
    private sealed record Cell(string Text, string Colour, float X, float Y, int Index);

    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? 0 : HalfWidth;
        bool Mine(float x) => side == 0 ? x < HalfWidth : x >= HalfWidth;
        var cells = capture.Texts.Select((t, i) => (t, i)).Where(p => Mine(p.t.X) && !string.IsNullOrWhiteSpace(p.t.Text))
            .Select(p => new Cell(Torn(p.t.Text) ? Unreadable : Glyphs(Spaces.Replace(A350Ewd.WithoutLeaders(p.t.Text), " ")),
                p.t.ColourName, p.t.X - shift, p.t.Y, p.i))
            .Where(c => c.Text.Length > 0).ToList();
        // A frame can hold a text twice at one place (the F-PLN INFO pages' first frame, live 2026-10-30): the last one stands.
        cells = cells.GroupBy(c => (c.Text, c.Colour, c.X, c.Y)).Select(g => g.Last()).OrderBy(c => c.Index).ToList();
        cells = JoinTimes(cells);
        // Black text is drawn on a filled box (every page, 2026-10-27/28): the scratchpad's message on white or amber, TMPY on
        // yellow, the page's title on the light title bar. It reads as the colour of the box a pilot sees; the title bar's row
        // as the page's title (without shapes recorded a text keeps black).
        var onBox = new HashSet<Cell>();
        var titleRow = new List<Cell>();
        for (int k = 0; k < cells.Count; k++)
        {
            var c = cells[k];
            if (c.Colour != "black") continue;
            var box = (capture.Shapes ?? Array.Empty<DrawnShape>()).Where(sh => sh.Kind == "fill" && sh.Image == 0
                    && A350Rgba.NameOf(sh.Colour) != "black" && c.X + shift >= sh.X0 - 2 && c.X + shift <= sh.X1 + 2
                    && c.Y >= sh.Y0 - 2 && c.Y <= sh.Y1 + 6)
                .OrderBy(sh => (sh.X1 - sh.X0) * (sh.Y1 - sh.Y0)).FirstOrDefault();
            if (box != null)
            {
                cells[k] = c = c with { Colour = InTitleBar(c) && A350Rgba.NameOf(box.Colour) == "grey" ? TitleBar : A350Rgba.NameOf(box.Colour) };
                onBox.Add(c);
            }
            if (InTitleBar(c)) titleRow.Add(c);
        }
        // The green dot speed's label is no text but a drawn green circle, a small square stroke (PERF T.O, live).
        foreach (var sh in (capture.Shapes ?? Array.Empty<DrawnShape>()).Where(sh => sh.Kind == "stroke" && sh.Image == 0
                     && Mine((sh.X0 + sh.X1) / 2) && A350Rgba.NameOf(sh.Colour) == "green"
                     && sh.X1 - sh.X0 is >= 14 and <= 26 && Math.Abs(sh.X1 - sh.X0 - (sh.Y1 - sh.Y0)) <= 3))
            cells.Add(new Cell("Green dot", "white", sh.X0 - shift, sh.Y1, -1));
        // An open pop-up hides what was drawn under it before it: those texts and controls are not on the screen (SURV's
        // DEFAULT SETTINGS confirmation over the ADS-B TRAFFIC box and the DEFAULT SETTINGS key, live). Open is a
        // PopupDialog with texts of its own drawn after it inside it; a dismissed one keeps its element and draws none.
        // The CRASH REPORTING consent (EULADialog, over DATA/STATUS after an update) is one too. Its own texts come first
        // after it begins, inside it: the dismissed consent dialog sits in every frame, and what is drawn after it (the
        // FMS system menu beside it, a list over its place) is never its own (review, 2026-10-28).
        // (A control drawn twice in a frame, as its text above, is one control.)
        var elements = capture.Elements.Where(e => Mine(e.X) && e.RectW > 0 && e.RectH > 0).DistinctBy(e => (e.Class, e.X, e.Y, e.RectW, e.RectH)).ToList();
        // The notepad, open (KCCU NOTEPAD, live 2026-10-30): a light bar named NOTE PAD across the scratchpad's row hides CLEAR INFO and the
        // FMS message; what is left of it is its own box, the key for its saved notes in the box's row and the key right of it that closes it.
        // ATC COM's pages paint the bar and an empty box, with no control in it.
        bool notepadOpen = capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0 && Math.Abs(sh.X0 - shift - NotepadBarX0) <= 2 && Math.Abs(sh.X1 - shift - NotepadBarX1) <= 2
            && Math.Abs(sh.Y0 - NotepadBarY0) <= 2 && Math.Abs(sh.Y1 - NotepadBarY1) <= 2) == true;
        var notepad = elements.FirstOrDefault(e => e.Class == "NotepadTextbox");
        var notepadBox = !notepadOpen || notepad == null ? null : elements.FirstOrDefault(e => e.Class == "BaseUITextBox" && Math.Abs(e.X - notepad.X) <= 2 && Math.Abs(e.Y - notepad.Y) <= 2);
        if (notepadOpen)
        {
            cells.RemoveAll(c => (notepadBox == null || c.Index < notepadBox.TextStart) && c.X >= NotepadBarX0 && c.X <= NotepadBarX1 && c.Y >= NotepadBarY0 && c.Y <= NotepadBarY1);
            elements.RemoveAll(e => e.Class == "MessageListButton");
        }
        var popups = elements.Select((e, i) => (e, i)).Where(p => p.e.Class is "PopupDialog" or "EULADialog"
            && cells.Where(c => c.Index >= p.e.TextStart).MinBy(c => c.Index) is { } first && Inside(p.e, first.X, first.Y)).ToList();
        // An open drop-down menu's items (DropdownButtonUI with their own texts drawn after them) hide what they cover,
        // as a pop-up does: ACTIVE's menu over DATA/STATUS's left column (live 2026-10-28).
        var menuItems = elements.Select((e, i) => (e, i)).Where(p => p.e.Class == "DropdownButtonUI"
            && cells.Any(c => c.Index >= p.e.TextStart && Covers(p.e, c.X, c.Y))).ToList();
        popups.AddRange(menuItems);
        // A waypoint revision's pop-up (INSERT NEXT WPT, NEW DEST) or the revision menu covers the F-PLN in the menu's
        // column, from its title or first item to its lowest key: the rows beside it read without the TIME, SPD and ALT
        // it covers, as a sighted pilot sees them (ReadFlightPlan); the DISCONTINUITY marker shows either side of it. A
        // pop-up's own texts are drawn after it; what is under it before it is gone.
        var revisionPopup = RevisionPopupOf(capture, cells, Mine);
        var overlay = RevisionOverlayOf(capture, cells, revisionPopup, Mine, shift);
        if (revisionPopup != null && overlay != null)
            cells.RemoveAll(c => c.Index < overlay.Start && overlay.Covers(c.X, c.Y));
        // The consent dialog is modal: the slivers of the page left and right of it (DATA/STATUS's NAV DATABASE label,
        // which ran into the dialog's line level with it) are behind it too.
        bool Covers(DrawnElement p, float x, float y) =>
            (p.Class == "EULADialog" || (x >= p.X - shift && x <= p.X - shift + p.RectW)) && y >= p.Y && y <= p.Y + p.RectH;
        bool Inside(DrawnElement p, float x, float y) =>
            x >= p.X - shift && x <= p.X - shift + p.RectW && y >= p.Y && y <= p.Y + p.RectH;
        cells.RemoveAll(c => popups.Any(p => c.Index >= 0 && c.Index < p.e.TextStart && Covers(p.e, c.X, c.Y)));
        var hidden = elements.Select((e, i) => (e, i)).Where(q => popups.Any(p => q.i < p.i
            && Covers(p.e, q.e.X - shift + q.e.RectW / 2, q.e.Y + q.e.RectH / 2))).Select(q => q.e).ToHashSet();
        var used = new HashSet<Cell>();
        var lines = new List<(float Top, float Left, MfdLine Line)>();
        if (notepadOpen && notepad == null)
            lines.Add((NotepadBarY0, NotepadBarX0, new MfdLine("text", NotepadNoBox, (NotepadBarX0 + NotepadBarX1) / 2, (NotepadBarY0 + NotepadBarY1) / 2)));
        // The title bar: the page's title, then what else it holds, in its own box's colour (TMPY on yellow); an open menu
        // over it (ACTIVE's) hides it.
        titleRow.RemoveAll(c => !cells.Contains(c));
        if (titleRow.Count > 0)
        {
            used.UnionWith(titleRow);
            var title = titleRow.Where(c => c.Colour is TitleBar or "black").OrderBy(c => c.X).ToList();
            var parts = (title.Count > 0 ? new[] { string.Join(" ", title.Select(c => c.Text)) } : Array.Empty<string>())
                .Concat(titleRow.Except(title).OrderBy(c => c.X).Select(c => $"{c.Text}, {c.Colour}"));
            var at = title.FirstOrDefault() ?? titleRow.MinBy(c => c.X)!;
            lines.Add((at.Y - 14, at.X, new MfdLine(title.Count > 0 ? "title" : "text", string.Join("; ", parts), at.X, at.Y - 7)));
        }
        var fields = new List<(float Top, float X0, float X1, Cell? Label, string? Value, float Cx, float Cy, bool Inherited, bool Above)>();
        var leftLabels = new HashSet<Cell>();
        var checkboxLabels = new HashSet<Cell>();
        var headers = new HashSet<Cell>();
        var keys = new List<(int At, string Name, float X0, float X1, float Top, float RowY)>();   // the keys' lines, for their columns and rows
        string? pageTab = null;   // the tab the page is on
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
        // Every check box's label (its own text, or the one just right of it), taken before any box is named: a box on a
        // check box's row is that choice's value, and VERT REV's AT OR ABOVE box is drawn before its check box (review
        // 2026-10-28).
        foreach (var e in elements.Where(e => !hidden.Contains(e) && e.Class == "BaseUICheckbox"))
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            var own = cells.Where(c => c.Index >= e.TextStart && c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4).ToList();
            if (own.Count == 0 && cells.Where(c => !inControls.Contains(c) && c.X > x1 && c.X - x1 <= 60
                    && Math.Abs(c.Y - (e.Y + e.RectH * 0.75f)) <= 12).OrderBy(c => c.X).FirstOrDefault() is { } right)
                own.Add(right);
            checkboxLabels.UnionWith(own);
        }
        // A rectangle check box (MFD DATA PRINTER's AUTO PRINT boxes, live 2026-10-30) holds no text. It is on the screen when its
        // fill is drawn (RAD NAV keeps two that never are), and is named by the text nearest left on its row, which it owns when
        // close (F-PLN INIT beside its box; PRE-FLIGHT, beyond a button, stays that row's too).
        var rectangleBoxes = new Dictionary<DrawnElement, Cell?>();
        foreach (var e in elements.Where(e => !hidden.Contains(e) && e.Class == "UIRectangleCheckbox"))
        {
            float x0 = e.X - shift;
            if (capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0 && Math.Abs(sh.X0 - shift - x0) <= 2 && Math.Abs(sh.Y0 - e.Y) <= 2) != true)
                continue;
            var label = cells.Where(c => !inControls.Contains(c) && c.X < x0 && Math.Abs(c.Y - (e.Y + e.RectH * 0.75f)) <= 12)
                .OrderByDescending(c => c.X).FirstOrDefault();
            rectangleBoxes[e] = label;
            if (label != null && x0 - label.X <= RectangleBoxOwnsLabelPixels)
                used.Add(label);
        }

        // A waypoint's open revision menu or a drop-down (its items with their own texts drawn after them): its items are
        // its own, never the F-PLN's column headers (FROM P.POS DIR TO, and POSITION's REPORT, white, sit in their band,
        // live 2026-10-28); what either covers is left out of the rows under it.
        var revisionItems = elements.Where(e => !hidden.Contains(e) && e.Class == RevisionItem
            && cells.Any(c => c.Index >= e.TextStart && Covers(e, c.X, c.Y))).ToList();
        var itemCells = cells.Where(c => revisionItems.Concat(menuItems.Select(m => m.e))
            .Any(e => c.Index >= e.TextStart && Covers(e, c.X, c.Y))).ToHashSet();
        var overlays = new List<Overlay>();
        if (overlay != null)
            overlays.Add(overlay);
        if (menuItems.Count > 0)
            overlays.Add(new Overlay(menuItems.Min(m => m.e.X) - shift, menuItems.Max(m => m.e.X + m.e.RectW) - shift,
                menuItems.Min(m => m.e.Y), menuItems.Max(m => m.e.Y + m.e.RectH), menuItems.Min(m => m.e.TextStart)));

        bool requestPage = titleRow.Any(c => c.Text == "REQUEST");
        bool flightPlan = titleRow.Any(c => c.Text.EndsWith("F-PLN", StringComparison.Ordinal));
        if (flightPlan)
            ReadFlightPlan(capture, shift, Mine, cells.Where(c => !itemCells.Contains(c)).ToList(), used, lines, overlays);

        foreach (var e in elements.Where(e => !hidden.Contains(e) && !Containers.Contains(e.Class)))
        {
            // The F-PLN's row boxes are the F-PLN reader's (hidden rows' boxes reach under the bottom keys).
            if (flightPlan && e.Class == "BaseFlightPlanButton") continue;
            float x0 = e.X - shift, y0 = e.Y, x1 = x0 + e.RectW, y1 = y0 + e.RectH + 4;
            // A control holds only what was drawn after it began: an open menu covers list rows drawn before it.
            // The scratchpad's message is drawn before its box (live).
            int from = e.Class == "NotepadTextbox" ? 0 : e.TextStart;
            // A text a later control is drawn over is that control's, never a field's under it (the INSERT key over the
            // AIRWAYS list's off-screen text boxes, live 2026-10-28).
            int at = elements.IndexOf(e);
            // (A control holds only what is drawn after it begins: a field's own text under a later empty one stays the field's.)
            // A key's too: DATA/WAYPOINT's CONFIRM over the next key (live 2026-10-30).
            bool OverByLater(Cell c) => elements.Skip(at + 1).Any(o => !Containers.Contains(o.Class) && !IsField(o)
                && !hidden.Contains(o) && c.Index >= o.TextStart
                && c.X >= o.X - shift && c.X <= o.X - shift + o.RectW && c.Y >= o.Y && c.Y <= o.Y + o.RectH + 4);
            // An open list's item owns its text, over whatever key it hangs across (the notepad's saved notes over the F-PLN's INIT key, live 2026-10-30).
            bool isItem = e.Class == RevisionItem || menuItems.Any(m => m.e == e);
            var inside = InReadingOrder(cells.Where(c => !used.Contains(c) && c.Index >= from && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1
                && !OverByLater(c) && (isItem || !itemCells.Contains(c))));
            float cx = x0 + e.RectW / 2, cy = y0 + e.RectH / 2;
            string value = Words(inside.Select(c => c.Text));
            string colour = inside.FirstOrDefault(c => c.Colour != "grey")?.Colour ?? inside.FirstOrDefault()?.Colour ?? "white";
            if (e == notepadBox) continue;
            if (e.Class == "NotepadTextbox")
            {
                used.UnionWith(inside);
                lines.Add(notepadBox == null
                    ? (y0, x0, new MfdLine("scratchpad", inside.Count == 0 ? "Scratchpad: empty" : $"Scratchpad: {value}, {colour}", cx, cy))
                    : (y0, x0, new MfdLine("field", inside.Count == 0 ? "Notepad: empty" : $"Notepad: {value}, {colour}", x0 + notepadBox.RectW / 2, cy)));
                continue;
            }
            if (notepadBox != null && e.Class == "BaseTexturedButton" && Math.Abs(cy - (notepadBox.Y + notepadBox.RectH / 2f)) <= 2)
            {
                lines.Add((notepadBox.Y, x0, new MfdLine("button", x0 < notepadBox.X - shift + notepadBox.RectW + NotepadKeyReach ? "Saved notes button" : "Close notepad button", cx, cy)));
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
            if (e.Class == "UIRectangleCheckbox")
            {
                if (!rectangleBoxes.TryGetValue(e, out var rowLabel))
                    continue;
                // The title over its column; its cross is a cyan stroke inside it.
                var boxTitle = cells.Where(c => !inControls.Contains(c) && !rectangleBoxes.ContainsValue(c) && c.Colour == "white"
                    && c.X >= x0 - RectangleBoxOwnsLabelPixels && c.X <= x1 && c.Y < y0 && y0 - c.Y <= RectangleBoxTitleReachPixels)
                    .OrderByDescending(c => c.Y).ThenBy(c => Math.Abs(c.X - x0)).FirstOrDefault();
                bool crossed = capture.Shapes?.Any(sh => sh.Kind == "stroke" && sh.Image == 0 && A350Rgba.NameOf(sh.Colour) == "cyan"
                    && sh.X0 - shift >= x0 - 2 && sh.X1 - shift <= x1 + 2 && sh.Y0 >= y0 - 2 && sh.Y1 <= y0 + e.RectH + 2) == true;
                string boxName = boxTitle == null ? rowLabel?.Text ?? "" : rowLabel == null ? boxTitle.Text : $"{boxTitle.Text}: {rowLabel.Text}";
                lines.Add((y0, x0, new MfdLine("checkbox", $"{boxName} check box, {(crossed ? "checked" : "not checked")}".TrimStart(), cx, cy)));
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
                checkboxLabels.UnionWith(inside);   // a box on its row is this choice's value (VERT REV's AT OR BELOW, HOLD's TIME)
                bool check = capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0 && sh.X0 - shift >= x0 && sh.X1 - shift <= x0 + e.RectW
                    && sh.Y0 >= y0 && sh.Y1 <= y0 + e.RectH) == true;
                string state = $"{value} check box, {(check ? "checked" : "not checked")}";
                lines.Add((y0, x0, new MfdLine("checkbox", colour == "grey" ? $"{state}, unavailable" : colour == "white" ? state : $"{state}, {colour}", cx, cy)));
                continue;
            }
            if (IsField(e) && requestPage)
            {
                // ATC COM's REQUEST pages (live 2026-10-30): every template keeps its boxes in the draw, framed only while up, their rectangles over
                // the labels. Up, a box is named by the words sharing it (OCEANIC CENTER ooooo), else by the text left of it on its row.
                bool framed = capture.Shapes is not { Count: > 0 } drawn || drawn.Any(sh => sh.Kind == "stroke" && sh.Image == 0
                    && Math.Abs(sh.X0 - shift - x0) <= 4 && Math.Abs(sh.Y0 - y0) <= 4 && Math.Abs(sh.X1 - shift - x1) <= 6);
                // (Two templates' boxes can stand on one place, a pixel apart.)
                if (!framed || lines.Any(l => l.Line.Kind == "field" && Math.Abs(l.Line.ClickX - cx) <= 2 && Math.Abs(l.Line.ClickY - cy) <= 2)) continue;
                float line = y0 + e.RectH * 0.75f;
                var words = inside.Where(c => c.Colour == "white" && Word(c.Text) is not ("required" or "none")).ToList();
                var entry = inside.Except(words).ToList();
                var beside = cells.Where(c => !inside.Contains(c) && c.Colour == "white" && c.X < x0 && x0 - c.X <= 300 && Math.Abs(c.Y - line) <= 12)
                    .OrderByDescending(c => c.X).FirstOrDefault();
                used.UnionWith(inside);
                if (beside != null) used.Add(beside);
                string requestName = entry.Count > 0 && words.Count > 0 ? Words(words.Select(c => c.Text)) : beside?.Text ?? "Request";
                string requestValue = entry.Count > 0 ? $"{Words(entry.Select(c => c.Text))}, {entry[0].Colour}" : words.Count > 0 ? $"{Words(words.Select(c => c.Text))}, {words[0].Colour}" : "empty";
                lines.Add((y0, x0, new MfdLine("field", $"{requestName}: {requestValue}", cx, cy)));
                continue;
            }
            if (requestPage && e.Class == "BaseTexturedButton" && capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0
                    && Math.Abs(sh.X0 - shift - x0) <= 2 && Math.Abs(sh.Y0 - y0) <= 2) == true)
            {
                lines.Add((y0, x0, new MfdLine("button", "Delete request button", cx, cy)));
                continue;
            }
            if (IsField(e))
            {
                float rowY = y0 + e.RectH * 0.75f;
                // A check box's label on the row, whatever its colour (yellow or cyan while chosen) and though inside the
                // check box, names the box beside it: that choice's value.
                // (D-ATIS's "- 1416Z" beside its airport box is the ATIS's time, no label.)
                // (Nor is a slash or a dash between two boxes: NEW WPT's LAT / LONG, PLACE / BRG / DIST.)
                bool OnRow(Cell c) => !inside.Contains(c) && (checkboxLabels.Contains(c) || !inControls.Contains(c) && c.Colour == "white")
                    && Math.Abs(c.Y - rowY) <= 12 && !AtisTime.IsMatch(c.Text) && c.Text is not ("/" or "-");
                // The nearest label left of the field, one field each (DATA STATUS: IDLE [ ] PERF [ ] [ ]); a label
                // between two columns (RAD NAV's IDENT, FREQ, CRS) also stands right of the left column's fields.
                // (A label beside a field sits close to it, within 200 px: FUEL&LOAD's row label DEST, 465 px left of its MIN
                // FUEL AT DEST field, is the table row's, the field's the heading above it. A check box's label reaches
                // further: VERT REV's AT OR BELOW stands 277 px left of its box.)
                // (A time unit just right of a box is its own: POSITION/TIME's [ ] H [ ] MIN.)
                var beside = cells.Where(c => OnRow(c) && c.X > x1 && c.X - x1 <= 15 && c.Text is "H" or "MIN").OrderBy(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => OnRow(c) && c.X < x0 && x0 - c.X <= (checkboxLabels.Contains(c) ? 320 : 200) && !leftLabels.Contains(c))
                        .OrderByDescending(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => OnRow(c) && c.X > x1 && c.X - x1 <= 120).OrderBy(c => c.X).FirstOrDefault();
                var label = beside
                    // else a label just above it (SURV's SQWK; FUEL&LOAD's MIN FUEL AT DEST heading, 26 px up; HOLD's
                    // INBOUND CRS, at the page's margin 79 px left of its box)
                    ?? cells.Where(c => !inControls.Contains(c) && c.Colour == "white" && c.X >= x0 - 100 && c.X <= x1 && c.Y < y0 && y0 - c.Y <= 30)
                        .OrderByDescending(c => c.Y).ThenBy(c => Math.Abs(c.X - (x0 + x1) / 2)).FirstOrDefault();
                // An empty box labelled only by a text above it is no entry the page shows (FUEL&LOAD, under CI); nor is
                // one whose frame is not drawn (VERT REV's AT and AT OR ABOVE boxes, HOLD's DIST box, until chosen).
                bool Framed() => capture.Shapes is not { Count: > 0 } shapes || shapes.Any(sh => sh.Kind == "stroke" && sh.Image == 0
                    && Math.Abs(sh.X0 - shift - x0) <= 4 && Math.Abs(sh.Y0 - y0) <= 4 && Math.Abs(sh.X1 - shift - x1) <= 6);
                // A key of the box's own rectangle under it: the box takes an entry, empty or not (CLOSEST AIRPORTS' OTHER ARPT, live 2026-10-30).
                bool onKey = elements.Any(o => o.Class == "BaseUIButton" && Math.Abs(o.X - e.X) <= 2 && Math.Abs(o.Y - e.Y) <= 2 && Math.Abs(o.RectW - e.RectW) <= 2);
                if (inside.Count == 0 && !onKey && (label == null || beside == null || !Framed())) continue;
                // White text in a box with no frame is another page's drawn over a box not on the screen (D-ATIS's under its RECEIVED page, live
                // 2026-10-30); a read-only value is a box's with no frame, in its value's colour (DATA STATUS's IDLE, POSITION/TIME's UTC).
                if (inside.Count > 0 && !onKey && !Framed() && inside.All(c => c.Colour == "white")) continue;
                used.UnionWith(inside);
                if (label != null && label.X < x0) leftLabels.Add(label);
                fields.Add((y0, x0, x1, label, inside.Count == 0 ? null : $"{value}, {colour}", cx, cy, false, beside == null));
                continue;
            }
            // A key with no text of its own is named by the white text drawn just under it or over it, across its width (FCU
            // BKUP's EFIS keys: CSTR to ARPT under theirs, WX, TERR and TRAF over theirs; live 2026-10-30). Its colour is its
            // light's, an empty text inside it (grey while the backup is not in use).
            if (inside.Count == 0 && e.Class == "BaseUIButton"
                && !elements.Any(o => IsField(o) && Math.Abs(o.X - e.X) <= 2 && Math.Abs(o.Y - e.Y) <= 2 && Math.Abs(o.RectW - e.RectW) <= 2)
                && cells.Where(c => !used.Contains(c) && !inControls.Contains(c) && !onBox.Contains(c) && c.Colour == "white" && c.X >= x0 - 10 && c.X <= x1
                        && (c.Y > y1 && c.Y - y1 <= 45 || c.Y < y0 && y0 - c.Y <= 25))
                    .OrderBy(c => c.Y > y1 ? c.Y - y1 : y0 - c.Y).FirstOrDefault() is { } nameBeside)
            {
                inside = new List<Cell> { nameBeside };
                value = nameBeside.Text;
                colour = capture.Texts.Skip(from).FirstOrDefault(t => t.X - shift >= x0 && t.X - shift <= x1 && t.Y >= y0 && t.Y <= y1)?.ColourName
                    ?? "white";
            }
            if (inside.Count == 0) continue;
            used.UnionWith(inside);
            // The tab the page is on is filled black, the others dark grey (every tabbed page, 2026-10-27/28).
            bool OnTab()
            {
                bool on = capture.Shapes?.Any(sh => sh.Kind == "fill" && sh.Image == 0 && sh.Colour is { R: < 0.05f, G: < 0.05f, B: < 0.05f }
                    && sh.X0 - shift >= x0 - 1 && sh.X1 - shift <= x1 + 1 && sh.Y0 >= y0 - 1 && sh.Y1 <= y0 + e.RectH + 1) == true;
                if (on) pageTab = value;
                return on;
            }
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
                "WidgetSwitcherTab" => ("tab", OnTab() ? $"{value} tab, selected" : $"{value} tab"),
                // An open menu's item: a drop-down's, or a waypoint's revision menu's.
                "DropdownButtonUI" or RevisionItem => ("menuitem", colour == "white" ? $"{value} button" : $"{value} button, {colour}"),
                _ => ("button", colour == "white" ? $"{value} button" : $"{value} button, {colour}"),
            };
            if (kind == "button") keys.Add((lines.Count, value, x0, x1, y0, y0 + e.RectH * 0.75f));
            lines.Add((y0, x0, new MfdLine(kind, spoken, cx, cy)));
        }

        // A page with no scratchpad box (the checklists) still shows the FMS message in its place, on its box; there is
        // nothing to type into there.
        if (!elements.Any(e => e.Class == "NotepadTextbox"))
        {
            var message = InReadingOrder(cells.Where(c => !used.Contains(c) && onBox.Contains(c) && c.X >= MessageX0 && c.X <= MessageX1
                && c.Y >= MessageY0 && c.Y <= MessageY1));
            if (message.Count > 0)
            {
                used.UnionWith(message);
                lines.Add((MessageY0, MessageX0, new MfdLine("message", $"Scratchpad: {Words(message.Select(c => c.Text))}, {message[0].Colour}",
                    (MessageX0 + MessageX1) / 2, (MessageY0 + MessageY1) / 2)));
            }
        }

        // The header's flight number, top right (live: "UAE111" at x 613, y 36; dashes before INIT).
        if (cells.FirstOrDefault(c => !used.Contains(c) && !inControls.Contains(c) && c.Colour == "white" && c.Y < 45 && c.X >= 560) is { } flight)
        {
            used.Add(flight);
            lines.Add((flight.Y - 14, flight.X, new MfdLine("text", $"Flight number: {Word(flight.Text)}, white", flight.X, flight.Y - 7)));
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

        // A box with no label of its own takes its column's header: the nearest white text above it over its column (WIND
        // CRZ's ALT and T.WIND, live 2026-10-28), every word of that row over the column (FCU BKUP's MAG HDG), numbered by
        // row where its column holds several.
        string? ColumnHeader(float top, float x0, float x1, float reach = KeyColumnHeaderReach, HashSet<Cell>? took = null)
        {
            // (Under the title bar: the header's flight number is no column's, live 2026-10-30. CLOSEST AIRPORTS' last box stands 540 px under EFF WIND.)
            var over = cells.Where(c => !inFields.Contains(c) && !inControls.Contains(c) && c.Y >= TitleBottom
                && c.Colour == "white" && c.Y < top && top - c.Y <= reach && c.X >= x0 - 40 && c.X <= x1 + 40).ToList();
            if (over.Count == 0) return null;
            float row = over.Max(c => c.Y);
            var header = over.Where(c => row - c.Y <= 4).OrderBy(c => c.X).ToList();
            // (A heading over the column itself, of a row of headings; else what stands beside it, the 40 px either way.)
            if (header.Where(c => c.X >= x0 && c.X <= x1).ToList() is { Count: > 0 } own) header = own;
            // (Right over its box it heads a table, said with its values, not a page's part: FCU BKUP's EFIS CP BACKUP stays a line.)
            if (top - row <= TableHeaderOverPixels) took?.UnionWith(header);
            return string.Join(" ", header.Select(c => c.Text));
        }
        var columnNames = new Dictionary<int, string>();
        foreach (var column in fields.Select((f, k) => (f, k)).Where(p => p.f.Label == null)
                     .Select(p => (p.k, p.f, Header: ColumnHeader(p.f.Top, p.f.X0, p.f.X1, FieldColumnHeaderReach, headers))).Where(p => p.Header != null)
                     .GroupBy(p => (p.Header!, (int)Math.Round(p.f.X0))))
        {
            var rowsOf = column.OrderBy(p => p.f.Top).ToList();
            // (The row above them may have that heading for its own label: POSITION/TIME's time markers.)
            int firstRow = fields.Any(o => o.Label?.Text == column.Key.Item1 && o.Top < rowsOf[0].f.Top && Math.Abs(o.X0 - column.Key.Item2) <= 1) ? 2 : 1;
            for (int r = 0; r < rowsOf.Count; r++)
                columnNames[rowsOf[r].k] = rowsOf.Count > 1 || firstRow > 1 ? $"{column.Key.Item1} {r + firstRow}" : column.Key.Item1;
        }

        // Keys of one name in several columns take their column's header (FCU BKUP's SELECTED and MANAGED under SPD, MAG HDG,
        // ALT and V/S; live 2026-10-30).
        // Down one column their row's label tells them apart (POSITION/TIME's MODIFY beside BLOCK TIME and FLIGHT TIME, the PRINTER page's
        // PRINT beside what it prints, live 2026-10-30).
        foreach (var same in keys.GroupBy(k => k.Name).Where(g => g.Count() > 1))
        {
            foreach (var key in same)
            {
                bool downOneColumn = same.Count(k => Math.Abs(k.X0 - key.X0) <= 2) > 1;
                var rowLabel = !downOneColumn ? null : cells.Where(c => c.Colour == "white" && !inControls.Contains(c) && c.X < key.X0 && Math.Abs(c.Y - key.RowY) <= 12)
                    .OrderByDescending(c => c.X).FirstOrDefault();
                if ((rowLabel?.Text ?? ColumnHeader(key.Top, key.X0, key.X1)) is { } header)
                    lines[key.At] = lines[key.At] with { Line = lines[key.At].Line with { Spoken = $"{header} {lines[key.At].Line.Spoken}" } };
            }
        }

        // A label two fields share takes each field's column header: the nearest text above it in its column.
        int unnamed = Enumerable.Range(0, fields.Count).Count(k => fields[k].Label == null && !columnNames.ContainsKey(k));
        for (int k = 0; k < fields.Count; k++)
        {
            var f = fields[k];
            // With no label and no column, the tab the page is on says what the page's one such box holds (VERT REV's SPD);
            // with several, a tab name would mislead ("CRZ: ..." on PERF CRZ).
            // (A label may be drawn with its colon: NOTIFY TO ATC:. D-ATIS's boxes hold airports and have no label.)
            string name = f.Label?.Text.TrimEnd(':', ' ') ?? columnNames.GetValueOrDefault(k) ?? (unnamed == 1 ? pageTab : null)
                ?? (titleRow.Any(c => c.Text.StartsWith("D-ATIS", StringComparison.Ordinal)) ? "Airport" : "Field");
            if (f.Label?.Text is "H" or "MIN")
            {
                var heading = cells.Where(c => !inFields.Contains(c) && c.Colour == "white" && c.Text is not ("H" or "MIN") && c.Y < f.Top && f.Top - c.Y <= 120 && c.X <= f.X1)
                    .OrderByDescending(c => c.Y).ThenByDescending(c => c.X).FirstOrDefault();
                if (heading != null) name = $"{heading.Text} {name}";
            }
            else if (f.Label != null && !f.Inherited && !f.Above && fields.Count(o => o.Label == f.Label && !o.Inherited && !o.Above) > 1)
            {
                var header = cells.Where(c => !inFields.Contains(c) && c.Y < f.Top && c.X >= f.X0 && c.X <= f.X1)
                    .OrderByDescending(c => c.Y).FirstOrDefault();
                if (header != null) name = $"{header.Text} {name}";
            }
            lines.Add((f.Top, f.X0, new MfdLine("field", f.Value == null ? $"{name}: empty" : $"{name}: {f.Value}", f.Cx, f.Cy)));
        }
        used.UnionWith(fields.Where(f => f.Label != null).Select(f => f.Label!));
        used.UnionWith(headers);
        // A slash or dash between a row's boxes is no line of its own (NEW WPT's).
        used.UnionWith(cells.Where(c => c.Text is "/" or "-" && fields.Count(f => Math.Abs(f.Top + 26 - c.Y) <= 16) >= 2));

        var rows = A350DrawLayout.Rows(cells.Where(c => !used.Contains(c))
            .Select(c => new DrawnText(c.Text, c.X, c.Y, c.X, c.Y, 0, A350Rgba.Of(c.Colour)))).ToList();
        var paired = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            // Headings are white words: a row holding an empty value (dashes, NONE) is labels and values instead.
            if (paired.Contains(i) || rows[i].Any(c => c.Colour != "white" || Word(c.Text) is "none" || c.Text == "NONE")) continue;
            int last = i;
            // A table's rows follow its headings closely (WIND's: 83 px under them, 78 px apart, live 2026-10-28; CLOSEST AIRPORTS': 92).
            for (int j = i + 1; j < rows.Count && rows[j][0].Y - rows[last][0].Y <= (rows[i].Count == 1 ? 50 : 95); j++)
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
            ordered = WithMenuAfterItsButton(ordered, menuItems.Select(m => m.e).ToList(),
                // (The notepad's saved notes hang from its box, whatever key the top of their list comes to.)
                notepadBox != null && Math.Abs(menuItems.Max(m => m.e.Y + m.e.RectH) - notepadBox.Y) <= TouchPx ? new[] { notepadBox }
                    : elements.Where(e => !hidden.Contains(e) && !Containers.Contains(e.Class)), shift);
        if (revisionItems.Count > 0)
            ordered = WithRevisionMenuAfterTitle(ordered, revisionItems, shift);
        if (revisionPopup != null)
        {
            ordered = ordered.Select(l => l.Kind == "field" && revisionPopup.Fields.Any(e => AtCentre(l, e, shift))
                ? l with { Spoken = revisionPopup.Title + ": " + ValueOf(l.Spoken) } : l).ToList();
            ordered = WithPopUpAfterTitle(ordered, revisionPopup, cells, shift);
        }
        return ordered;
    }

    /// <summary>A revision pop-up (INSERT NEXT WPT, NEW DEST) reads together after the page title (it read woven between
    /// the F-PLN rows by height, its CANCEL key down the column, live 2026-10-28), its lines flagged so the window moves
    /// to its field as it opens: its own controls and the texts it drew (the F-PLN's DISCONTINUITY under its column is
    /// the plan's).</summary>
    private static List<MfdLine> WithPopUpAfterTitle(List<MfdLine> lines, RevisionPopup popup, List<Cell> cells, float shift)
    {
        // Below the title band, as RevisionPopupOf takes them: the page frame (flight number, title) draws in its range.
        var texts = cells.Where(c => c.Index >= popup.Start && c.Index < popup.End && c.Y >= 150).ToList();
        bool Its(MfdLine l) => popup.Own.Any(e => AtCentre(l, e, shift))
            || l.Kind == "text" && texts.Any(c => Math.Abs(c.X - l.ClickX) < 1 && Math.Abs(c.Y - 7 - l.ClickY) < 1);
        var own = lines.Where(Its).Select(l => l with { Popup = true }).ToList();
        var result = lines.Where(l => !Its(l)).ToList();
        int title = result.FindIndex(l => l.Kind == "title");
        if (title < 0)
            return lines.Select(l => Its(l) ? l with { Popup = true } : l).ToList();   // no title: in place, still the pop-up's
        result.InsertRange(title + 1, own);
        return result;
    }

    /// <summary>A field line's value: what follows its name ("C220°: none, cyan" → "none, cyan").</summary>
    private static string ValueOf(string spoken) => spoken.IndexOf(": ", StringComparison.Ordinal) is int at and >= 0 ? spoken[(at + 2)..] : spoken;

    /// <summary>An open waypoint revision pop-up: the revision menu's column it draws in, the draw indices of its texts
    /// ([Start, End)), its title (its top row: "INSERT NEXT WPT FROM BASUR"), its entry fields, and its top and bottom
    /// edges (its title's line, its CANCEL key's: NEW DEST's stands in mid-page, INSERT NEXT WPT's below the column).</summary>
    private sealed record RevisionPopup(DrawnElement Column, int Start, int End, string Title, IReadOnlyList<DrawnElement> Fields,
        float Top, float Bottom, IReadOnlyList<DrawnElement> Own);

    /// <summary>A text's height above the point it is drawn at (the MFD's texts sit on their line's lower part).</summary>
    private const float TextHeight = 22;

    /// <summary>What an open revision menu or pop-up covers, in the side's own coordinates, and the draw index it starts
    /// at (what was drawn before it under it is hidden).</summary>
    private sealed record Overlay(float X0, float X1, float Y0, float Y1, int Start)
    {
        public bool Covers(float x, float y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
    }

    /// <summary>What covers the revision menu's column: an open pop-up, from its title to its lowest key, or the open
    /// menu, from its first item to its last (the column's own rectangle stops above WHAT IF).</summary>
    private static Overlay? RevisionOverlayOf(A350DrawCapture capture, List<Cell> cells, RevisionPopup? popup,
        Func<float, bool> mine, float shift)
    {
        var column = popup?.Column
            ?? capture.Elements.FirstOrDefault(e => e.Class == "UIFlightplanRevisionsMenu" && mine(e.X) && e.RectW > 0 && e.RectH > 0);
        if (column == null)
            return null;
        float x0 = column.X - shift, x1 = x0 + column.RectW;
        if (popup != null)
            return new Overlay(x0, x1, popup.Top, popup.Bottom, popup.Start);
        var items = capture.Elements.Where(e => e.Class == RevisionItem && mine(e.X) && e.RectW > 0 && e.RectH > 0
            && cells.Any(c => c.Index >= e.TextStart && c.X >= e.X - shift && c.X <= e.X - shift + e.RectW
                && c.Y >= e.Y && c.Y <= e.Y + e.RectH)).ToList();
        return items.Count == 0 ? null
            : new Overlay(x0, x1, items.Min(e => e.Y), items.Max(e => e.Y + e.RectH), column.TextStart);
    }

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
            var own = all.Skip(i + 1).TakeWhile(e => e.TextStart < end).Where(e => mine(e.X) && e.RectW > 0 && e.RectH > 0).ToList();
            var fields = own.Where(e => e.Class is "BaseUITextBox" or "DropdownTextBox").ToList();
            float bottom = own.Select(e => e.Y + e.RectH).Concat(mineTexts.Select(c => c.Y)).Max();
            return new RevisionPopup(column, start, end, title, fields, top - TextHeight, bottom, own);
        }
        return null;
    }

    /// <summary>How far above a box or a key its column's heading may stand, and how near it heads a table.</summary>
    private const float FieldColumnHeaderReach = 560, KeyColumnHeaderReach = 300, TableHeaderOverPixels = 100;

    /// <summary>A button of a waypoint's revision menu (UIFlightplanRevisionsMenu).</summary>
    private const string RevisionItem = "FlightplanButtonActionButton";

    /// <summary>How far left a rectangle check box's row label is its own, and how far up the title over its column may stand.</summary>
    private const float RectangleBoxOwnsLabelPixels = 300, RectangleBoxTitleReachPixels = 200;

    /// <summary>The title bar's colour, as its texts read: they are the page's title, said without a colour.</summary>
    private const string TitleBar = "title";

    /// <summary>The title bar's band (its fill from y 108, its texts drawn at y 137). Above it black on a box is a key's: ATC COM's
    /// EMER tab, on amber (live 2026-10-30).</summary>
    private const float TitleTop = 108, TitleBottom = 150;

    private static bool InTitleBar(Cell c) => c.Y >= TitleTop && c.Y < TitleBottom;

    /// <summary>What an open notepad reads as on a page with no box in it (ATC COM's).</summary>
    public const string NotepadNoBox = "Notepad open, no box on this page";

    /// <summary>The open notepad's light bar (live 2026-10-30), and how far right of its box its saved notes' key stands: the box
    /// ends where that key begins, the key that closes it is 36 px on.</summary>
    private const float NotepadBarX0 = 20, NotepadBarX1 = 789, NotepadBarY0 = 950, NotepadBarY1 = 1009, NotepadKeyReach = 10;

    /// <summary>Where the FMS message shows: the scratchpad box's place on the pages that have one (x 129, y 960, 597 x 39).</summary>
    private const float MessageX0 = 129, MessageX1 = 726, MessageY0 = 960, MessageY1 = 1003;

    /// <summary>The page's title line, from its title bar ("ACTIVE/F-PLN", y 137; "ACTIVE/F-PLN; TMPY, yellow"), or null.</summary>
    public static string? PageTitle(IReadOnlyList<MfdLine> lines) => lines.FirstOrDefault(l => l.Kind == "title")?.Spoken;

    /// <summary>The FMS message line as the window compares it: the scratchpad's, or a checklist page's message; a read page
    /// showing none is an empty scratchpad (a checklist page has no box, so the first message there is a change too);
    /// null before any page is read.</summary>
    public static string? Scratchpad(IReadOnlyList<MfdLine> lines) =>
        lines.Count == 0 ? null : lines.FirstOrDefault(l => l.Kind is "scratchpad" or "message")?.Spoken ?? EmptyScratchpad;

    /// <summary>The message to speak when the FMS posted one since the last read (a sighted pilot sees it appear), else
    /// null: never on the window's first read, never for the scratchpad emptying.</summary>
    public static string? MessageJustArrived(string? before, string? now) =>
        before != null && now != null && now != before && now != EmptyScratchpad ? now : null;

    private const string EmptyScratchpad = "Scratchpad: empty";

    /// <summary>The new page's title when the page changed since the last read (a page key, a menu pick, C/L COMPLETE
    /// going back to the page before), else null: a sighted pilot sees the new title bar at once. Nothing on the window's
    /// first read.</summary>
    public static string? PageJustChanged(IReadOnlyList<MfdLine> before, IReadOnlyList<MfdLine> now) =>
        before.Count == 0 || PageTitle(now) is not { } title || !Readable(title) || title == PageTitle(before) ? null : title;

    /// <summary>How many reads in a row a torn line may read as its last whole reading before it reads unreadable again.</summary>
    public const int MaxCarried = 3;

    /// <summary>The page with each line a torn text left unreadable read as it last read whole, where the last read has a
    /// line of its kind at the same click point: the window does not flicker to "unreadable" while the A350 rebuilds a
    /// label every frame (most copies catch it torn), and a label that changed reads anew on its next whole read. Only
    /// between two reads of the same whole title, and never the title itself: every page has its title at one point and
    /// keys of other pages share click points (review 2026-10-30); at most <see cref="MaxCarried"/> reads in a row, so a
    /// label that stays torn after a press says so instead of its old state.</summary>
    public static IReadOnlyList<MfdLine> KeepReadable(IReadOnlyList<MfdLine> before, IReadOnlyList<MfdLine> now)
    {
        static bool HasUnreadable(MfdLine l) => l.Spoken.Contains(Unreadable, StringComparison.Ordinal);
        if (PageTitle(now) is not { } title || !Readable(title) || PageTitle(before) != title)
            return now;
        return now.Select(l => l.Kind != "title" && HasUnreadable(l) && before.FirstOrDefault(b => b.Kind == l.Kind && !HasUnreadable(b)
                && b.Carried < MaxCarried && Math.Abs(b.ClickX - l.ClickX) <= 2 && Math.Abs(b.ClickY - l.ClickY) <= 2) is { } whole
            ? l with { Spoken = whole.Spoken, Carried = whole.Carried + 1 } : l).ToList();
    }

    /// <summary>Whether a title can be said: some text and no control character. A copy read across a frame (fixtures taken
    /// before the allocator patch, [A350-10]) drew titles such as "¨÷é\x13…" and an empty one; announced as a new page they
    /// told the pilot nothing (review 2026-10-29).</summary>
    private static bool Readable(string title) => !string.IsNullOrWhiteSpace(title) && !title.Any(char.IsControl)
        && !title.Contains(Unreadable, StringComparison.Ordinal);

    /// <summary>Where the MFD window moves when a menu has just opened: its first item, as a sighted pilot's eyes go to
    /// it; null while none opened since the last read (an open menu keeps the pilot's own place).</summary>
    public static int? MenuJustOpened(IReadOnlyList<MfdLine> before, IReadOnlyList<MfdLine> now)
    {
        // A revision pop-up opening (chosen from the waypoint's menu): its entry field, where the pilot types.
        if (!before.Any(l => l.Popup) && now.Any(l => l.Popup))
        {
            int field = now.ToList().FindIndex(l => l.Popup && l.Kind == "field");
            return field >= 0 ? field : now.ToList().FindIndex(l => l.Popup);
        }
        // A menu is new when its first item stands where no item stood before: one replacing another (RWY's list, then
        // SID's) as well as one opening; the same menu still open, scrolled or not, keeps the pilot's place.
        var open = before.Where(l => l.Kind == "menuitem").Select(l => (l.ClickX, l.ClickY)).ToHashSet();
        for (int i = 0; i < now.Count; i++)
            if (now[i].Kind == "menuitem")
                return open.Contains((now[i].ClickX, now[i].ClickY)) ? null : i;
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
        int title = result.FindIndex(l => l.Kind == "title" && l.Spoken.Contains("F-PLN", StringComparison.Ordinal));
        if (title < 0)
            return lines;   // no title to follow (not drawn): the items stay where they read
        result.InsertRange(title + 1, menu);
        return result;
    }

    /// <summary>How far an open list may stand from the control it hangs from: RWY's list starts 6 px under its key,
    /// ACTIVE's flush with it, the notepad's saved notes 11 px over its box (live).</summary>
    private const float TouchPx = 12;

    /// <summary>An open drop-down's items read right after the control it hangs from, which says the menu is open; a
    /// sighted pilot sees the list hanging off it. That control is the one whose edge the list touches (a field's list
    /// too, and a list opening upward); a list touching none keeps its place and names no owner.</summary>
    private static List<MfdLine> WithMenuAfterItsButton(List<MfdLine> lines, List<DrawnElement> items, IEnumerable<DrawnElement> controls,
        float shift)
    {
        float x0 = items.Min(e => e.X) - shift, x1 = items.Max(e => e.X + e.RectW) - shift;
        float top = items.Min(e => e.Y), bottom = items.Max(e => e.Y + e.RectH);
        bool IsItem(MfdLine l) => items.Any(e => AtCentre(l, e, shift));
        bool In(MfdLine l, DrawnElement e) =>
            l.ClickX >= e.X - shift && l.ClickX <= e.X - shift + e.RectW && l.ClickY >= e.Y && l.ClickY <= e.Y + e.RectH;
        var menu = lines.Where(IsItem).ToList();
        var owner = controls.Where(e => !items.Contains(e) && e.X - shift < x1 && e.X - shift + e.RectW > x0
                && (Math.Abs(e.Y + e.RectH - top) <= TouchPx || Math.Abs(e.Y - bottom) <= TouchPx))
            .Select(e => lines.FirstOrDefault(l => !IsItem(l) && l.Kind is "button" or "menu" or "field" && In(l, e)))
            .FirstOrDefault(l => l != null);
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
        HashSet<Cell> used, List<(float Top, float Left, MfdLine Line)> lines, IReadOnlyList<Overlay> overlays)
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
            // FROM (the first column's heading) names the top row only while it is the FROM waypoint, which has no leg into
            // it; scrolled down, the top row is any waypoint with its leg.
            string label = (b.Y < 230 && parts.Count == 0 ? $"{from} {ident.Text}" : ident.Text) + (ident.Colour != colour ? $" ({ident.Colour})" : "");
            string legText = parts.Count > 0 ? string.Join(", ", parts) + "; " : "";
            // A column whose value an open menu or pop-up covers is left out: hidden, not absent. Each column's values are
            // drawn at one x (live: TIME 262, SPD 357, ALT 443).
            var columns = new[] { (time, 200f, 334f, 262f), (spd, 334f, 422f, 357f), (alt, 422f, 563f, 443f) }
                .Where(k => !overlays.Any(o => o.Covers(k.Item4, ident.Y)))
                .Select(k => $"{k.Item1} {Column(k.Item2, k.Item3, colour)}").Append(colour);
            lines.Add((b.Y, 0, new MfdLine("waypoint", $"{label}: {legText}{string.Join(", ", columns)}", b.X + b.W / 2, b.Y + b.H / 2)));
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

    /// <summary>A unit the MFD may draw in a colour of its own just right of its value (HOLD's LAST EXIT EFOB: "--.-"
    /// magenta, "T" cyan 8 px right).</summary>
    private static readonly Regex UnitWord = new(@"^(T|KG|LBS|NM|KT|FT|MN)$", RegexOptions.Compiled);

    /// <summary>A row's texts as tokens: each read as a word, a time drawn in parts joined ("--", grey ":", "--"), a
    /// grey unit, or a unit word just right of it, kept with the value left of it (its "d" a degree sign).</summary>
    private static List<DrawnCell> Tokens(IReadOnlyList<DrawnCell> row)
    {
        var tokens = new List<DrawnCell>();
        foreach (var c in row.OrderBy(c => c.X))
        {
            var prev = tokens.LastOrDefault();
            if (prev != null && prev.Colour is not ("grey" or "white")
                && (c.Colour == "grey" && c.X - prev.X <= 60 || UnitWord.IsMatch(c.Text) && c.X - prev.X <= 20))
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
        // A white value is a placeholder: an empty one drawn as dashes, or NONE (SEC DEPARTURE's EOSID, live 2026-10-28);
        // any other white text is a label or another row of headings.
        static bool Placeholder(DrawnCell t) => t.Text is "none" or "NONE" or "AT DEST";
        int start = tokens.Count > 0 && tokens[0].Colour == "white" && !Placeholder(tokens[0]) && hs.Count > 1 ? 1 : 0;
        if (tokens.Skip(start).Any(t => t.Colour == "white" && !Placeholder(t))) return null;
        float first = hs[0].X - 40;
        var label = tokens.Take(start).Concat(tokens.Skip(start).Where(t => t.X < first && hs.Count > 1)).ToList();
        var vals = tokens.Where(t => !label.Contains(t) && t.Colour is not ("grey" or "black")).ToList();
        if (vals.Count == 0) return null;
        float x0 = hs.Count == 1 ? hs[0].X - 150 : first, x1 = hs.Count == 1 ? hs[0].X + 150 : hs[^1].X + 160;
        if (vals.Any(v => v.X < x0 || v.X > x1)) return null;
        string colour = vals.GroupBy(v => v.Colour).OrderByDescending(g => g.Count()).First().Key;
        string Value(DrawnCell v) => v.Text + (v.Colour != colour && v.Colour != "grey" ? $" ({v.Colour})" : "");
        // Values not one per heading: each heading reads every token nearest it, its grey units too (WIND's "FL", the
        // altitude, then "none° / none KT", live 2026-10-28).
        DrawnCell Nearest(DrawnCell t) => hs.OrderBy(h => Math.Abs(h.X - t.X)).First();
        string body = hs.Count == 1
            ? $"{hs[0].Text}: {string.Join(", ", vals.Select(Value))}"
            : string.Join(", ", vals.Count == hs.Count
                ? hs.Select((h, i) => $"{h.Text} {Value(vals[i])}")
                : hs.Select(h => (h, Cells: tokens.Where(t => !label.Contains(t) && Nearest(t) == h).ToList()))
                    .Where(p => p.Cells.Any(vals.Contains))
                    .Select(p => $"{p.h.Text} {string.Join(" ", p.Cells.Select(Value))}"));
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

    /// <summary>A drawn token as said: amber boxes ("oooo") are a required entry, dashes nothing yet (a position's "--d--.-" too), "dC" degrees
    /// Celsius, a checklist's line to fill ("____") blank. Dot leaders are colons already (<see cref="A350Ewd"/>).</summary>
    private static string Word(string token) =>
        token.Length > 0 && token.Contains('o') && token.All(ch => ch is 'o' or '.') ? "required"
        : token.Length > 0 && token.Contains('-') && token.All(ch => ch is '-' or ':' or '.' or 'd') ? "none"
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
