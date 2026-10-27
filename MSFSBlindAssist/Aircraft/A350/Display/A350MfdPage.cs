using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One line of the MFD as a pilot moves through it: what kind it is (menu, button, tab, field,
/// scratchpad, text), how it is spoken, and where on the side's own MFD a click lands on it.</summary>
public sealed record MfdLine(string Kind, string Spoken, float ClickX, float ClickY);

/// <summary>
/// One side's MFD page as lines (pure), from the draw's UI elements and texts by geometry: a button's, tab's or
/// menu's text is the text inside its rectangle; a field's value is the text inside it and its label the nearest
/// text left of it on its row; the scratchpad (NotepadTextbox) holds what is typed and the FMS messages. Dialogs
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
            if (e.Class.EndsWith("TextBox", StringComparison.Ordinal) || e.Class.EndsWith("Textbox", StringComparison.Ordinal))
            {
                var label = cells.Where(c => !used.Contains(c) && !inside.Contains(c) && c.X < x0 && Math.Abs(c.Y - (y0 + e.RectH * 0.75f)) <= 12)
                    .OrderByDescending(c => c.X).FirstOrDefault();
                if (label == null && inside.Count == 0) continue;
                used.UnionWith(inside);
                if (label != null) used.Add(label);
                var name = label?.Text ?? "Field";
                lines.Add((y0, x0, new MfdLine("field", inside.Count == 0 ? $"{name}: empty" : $"{name}: {value}, {colour}", cx, cy)));
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

        foreach (var row in A350DrawLayout.Rows(cells.Where(c => !used.Contains(c))
                     .Select(c => new DrawnText(c.Text, c.X, c.Y, c.X, c.Y, 0, ColourOf(c.Colour)))))
            lines.Add((row[0].Y - 14, row[0].X, new MfdLine("text", A350DrawLayout.Speak(row), row[0].X, row[0].Y - 7)));

        return lines.OrderBy(l => l.Top).ThenBy(l => l.Left).Select(l => l.Line).ToList();
    }

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
