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
        "WidgetSwitcherUI", "PopupDialog", "EULADialog", "DropdownMenuContainer",
    };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private sealed record Cell(string Text, string Colour, float X, float Y);

    public static IReadOnlyList<MfdLine> Read(A350DrawCapture capture, int side)
    {
        float shift = side == 0 ? 0 : HalfWidth;
        bool Mine(float x) => side == 0 ? x < HalfWidth : x >= HalfWidth;
        var cells = capture.Texts.Where(t => Mine(t.X) && !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => new Cell(Spaces.Replace(t.Text.Trim(), " "), t.ColourName, t.X - shift, t.Y)).ToList();
        var used = new HashSet<Cell>();
        var lines = new List<(float Top, float Left, MfdLine Line)>();
        var fields = new List<(float Top, float X0, float X1, Cell? Label, string? Value, float Cx, float Cy)>();
        var leftLabels = new HashSet<Cell>();
        var inFields = capture.Elements.Where(e => Mine(e.X) && IsField(e)).SelectMany(e =>
        {
            float x0 = e.X - shift, x1 = x0 + e.RectW;
            return cells.Where(c => c.X >= x0 && c.X <= x1 && c.Y >= e.Y && c.Y <= e.Y + e.RectH + 4);
        }).ToHashSet();

        foreach (var e in capture.Elements.Where(e => Mine(e.X) && !Containers.Contains(e.Class) && e.RectW > 0 && e.RectH > 0))
        {
            float x0 = e.X - shift, y0 = e.Y, x1 = x0 + e.RectW, y1 = y0 + e.RectH + 4;
            var inside = cells.Where(c => !used.Contains(c) && c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1)
                .OrderBy(c => c.Y).ThenBy(c => c.X).ToList();
            float cx = x0 + e.RectW / 2, cy = y0 + e.RectH / 2;
            string value = string.Join(" ", inside.Select(c => Word(c.Text)));
            string colour = inside.FirstOrDefault(c => c.Colour != "grey")?.Colour ?? inside.FirstOrDefault()?.Colour ?? "white";
            if (e.Class == "NotepadTextbox")
            {
                used.UnionWith(inside);
                lines.Add((y0, x0, new MfdLine("scratchpad", inside.Count == 0 ? "Scratchpad: empty" : $"Scratchpad: {value}, {colour}", cx, cy)));
                continue;
            }
            if (IsField(e))
            {
                float rowY = y0 + e.RectH * 0.75f;
                bool OnRow(Cell c) => !inside.Contains(c) && !inFields.Contains(c) && Math.Abs(c.Y - rowY) <= 12;
                // The nearest label left of the field, one field each (DATA STATUS: IDLE [ ] PERF [ ] [ ]); a label
                // between two columns (RAD NAV's IDENT, FREQ, CRS) also stands right of the left column's fields.
                var label = cells.Where(c => OnRow(c) && c.X < x0 && !leftLabels.Contains(c)).OrderByDescending(c => c.X).FirstOrDefault()
                    ?? cells.Where(c => OnRow(c) && c.X > x1 && c.X - x1 <= 120).OrderBy(c => c.X).FirstOrDefault();
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

        foreach (var row in A350DrawLayout.Rows(cells.Where(c => !used.Contains(c))
                     .Select(c => new DrawnText(c.Text, c.X, c.Y, c.X, c.Y, 0, ColourOf(c.Colour)))))
            lines.Add((row[0].Y - 14, row[0].X, new MfdLine("text", A350DrawLayout.Speak(row), row[0].X, row[0].Y - 7)));

        return lines.OrderBy(l => l.Top).ThenBy(l => l.Left).Select(l => l.Line).ToList();
    }

    private static bool IsField(DrawnElement e) => e.Class != "NotepadTextbox" && e.RectW > 0 && e.RectH > 0
        && (e.Class.EndsWith("TextBox", StringComparison.Ordinal) || e.Class.EndsWith("Textbox", StringComparison.Ordinal));

    /// <summary>A field's drawn token as said: amber boxes ("oooo") are a required entry, dashes nothing yet,
    /// "dC" degrees Celsius.</summary>
    private static string Word(string token) =>
        token.Length > 0 && token.All(ch => ch == 'o') ? "required"
        : token.Length > 0 && token.All(ch => ch == '-') ? "none"
        : token == "dC" ? "°C" : token;

    private static A350Rgba ColourOf(string name) => name switch
    {
        "green" => new A350Rgba(0.15f, 0.85f, 0.1f, 1), "amber" => new A350Rgba(1, 0.55f, 0, 1),
        "cyan" => new A350Rgba(0.4f, 0.86f, 1, 1), "red" => new A350Rgba(1, 0.1f, 0.1f, 1),
        "magenta" => new A350Rgba(1, 0.4f, 1, 1), "black" => new A350Rgba(0, 0, 0, 1),
        "grey" => new A350Rgba(0.6f, 0.6f, 0.6f, 1), _ => new A350Rgba(1, 1, 1, 1),
    };
}
