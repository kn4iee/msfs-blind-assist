namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>A line of an RMP's screen and what presses it.</summary>
/// <param name="Key">The key a press on this line presses (A350Rmp's name: LSKn the right-hand key of row n, ADKn the left-hand one), or null.</param>
/// <param name="Typed">The line takes a typed entry: its key, then the digits (the LS and VOR pages' frequencies and courses).</param>
/// <param name="IsFrequency">A typed line's entry is a frequency, else a course.</param>
public sealed record RmpLine(string Spoken, string? Key = null, bool Typed = false, bool IsFrequency = false);

/// <summary>
/// An RMP's screen, page by page, read from where each page draws its texts (live 2026-10-30): the radio pages' active and standby, the
/// NAV page and its LS and VOR pages (a frequency left under FREQ, a course right under CRS, each entry taken once its digits are
/// complete), the squawk, the MENU and the datalink router's pages. The screen has three rows of side keys: y about 160, 340 and 516;
/// a right-hand item draws its ">" at x 1037, a left-hand one its "&lt;" at x 40. The SQUAWK line is at the foot of most pages. Pure.
/// </summary>
public static class A350RmpPage
{
    private const float LeftColumn = 400, RightColumn = 600;

    /// <summary>The row of side keys a text stands on, 1 to 3.</summary>
    private static int Row(float y) => y < 250 ? 1 : y < 430 ? 2 : 3;

    private static string Words(IEnumerable<DrawnCell> cells) => string.Join(" ", cells.OrderBy(c => c.X).Select(c => c.Text));

    public static IReadOnlyList<RmpLine> Read(A350DrawCapture capture)
    {
        var cells = A350GroundDisplays.Cells(capture.Texts);
        var squawk = cells.Where(c => c.Text.StartsWith("SQUAWK", StringComparison.Ordinal)).ToList();
        var page = cells.Except(squawk).ToList();
        var lines = new List<RmpLine>();
        if (page.Any(c => c.Text == "FREQ")) TuningPage(page, lines);
        else if (page.Any(c => c.Text == "STBY RAD NAV")) NavPage(page, lines);
        else if (page.Any(c => c.Text == "DATALINK COM STATUS")) ComStatus(page, lines);
        else if (page.Any(c => c.Text is "DATALINK INIT" or "VHF DATALINK PROVIDER")) LabelledPage(page, lines);
        else if (page.Any(c => c.Text == "SQWK")) SquawkPage(page, lines);
        else if (page.Any(c => c.Text is "MENU" or "DATALINK ROUTER" && c.Y < 120)) KeyList(page, lines);
        else RadioPage(page, lines);
        lines.AddRange(squawk.Select(c => new RmpLine(A350DrawLayout.Speak(new[] { c }))));
        return lines;
    }

    /// <summary>The radio pages: each radio named in the middle column, active left, standby right (cyan: the one the knob tunes).</summary>
    private static void RadioPage(List<DrawnCell> cells, List<RmpLine> lines)
    {
        var used = new HashSet<DrawnCell>(cells.Where(c => c.Text == "STBY" && c.Y < 120));
        foreach (var name in cells.Where(c => c.X is >= LeftColumn and < RightColumn && c.Y >= 120).OrderBy(c => c.Y).ToList())
        {
            var active = cells.FirstOrDefault(c => !used.Contains(c) && c.X < LeftColumn && Math.Abs(c.Y - name.Y) <= 12);
            var standby = cells.FirstOrDefault(c => !used.Contains(c) && c.X >= RightColumn && Math.Abs(c.Y - name.Y) <= 12);
            if (active == null && standby == null) continue;
            used.Add(name);
            var parts = new List<string>();
            if (active != null) { used.Add(active); parts.Add($"active {active.Text}, {active.Colour}"); }
            if (standby != null) { used.Add(standby); parts.Add($"standby {standby.Text}, {standby.Colour}"); }
            lines.Add(new RmpLine($"{name.Text}: " + string.Join("; ", parts)));
        }
        foreach (var rest in cells.Where(c => !used.Contains(c)).OrderBy(c => c.Y).ThenBy(c => c.X))
            lines.Add(new RmpLine(A350DrawLayout.Speak(new[] { rest }), rest.X >= RightColumn && rest.Y >= 430 ? A350Rmp.LineSelectKey(Row(rest.Y)) : null));
    }

    private static void NavPage(List<DrawnCell> cells, List<RmpLine> lines)
    {
        var items = RightHandItems(cells);
        bool on = cells.Any(c => c.Text == "LS" && c.Colour == "white");
        lines.Add(new RmpLine("STBY RAD NAV: " + (on ? "On" : "Off"), A350Rmp.StbyRadNavKey));
        lines.AddRange(items);
    }

    /// <summary>The LS and VOR pages: a frequency left under FREQ, a course right under CRS, per receiver.</summary>
    private static void TuningPage(List<DrawnCell> cells, List<RmpLine> lines)
    {
        var values = cells.Where(c => c.Text is not ("FREQ" or "CRS") && c.Y > 120 && (c.X < LeftColumn || c.X is >= 800 and < 950)).ToList();
        foreach (int row in values.Select(v => Row(v.Y)).Distinct().OrderBy(r => r))
        {
            string name = cells.FirstOrDefault(c => c.X is >= LeftColumn and < RightColumn && c.Text.StartsWith("VOR", StringComparison.Ordinal) && Row(c.Y) == row)?.Text ?? "LS";
            foreach (var value in values.Where(v => Row(v.Y) == row).OrderBy(v => v.X))
            {
                bool frequency = value.X < LeftColumn;
                string shown = value.Text.Contains('!') ? "not set" : value.Text;
                lines.Add(new RmpLine($"{name} {(frequency ? "frequency" : "course")}: {shown}, {value.Colour}",
                    frequency ? A350Rmp.ActivationKey(row) : A350Rmp.LineSelectKey(row), Typed: true, IsFrequency: frequency));
            }
        }
        // The status in the middle (ILS over ACTIVE) reads as one line, and the page's own item (GLS).
        var status = cells.Where(c => c.X is >= LeftColumn and < RightColumn && !c.Text.StartsWith("VOR", StringComparison.Ordinal)).OrderBy(c => c.Y).ToList();
        if (status.Count > 0)
            lines.Add(new RmpLine($"{string.Join(" ", status.Select(c => c.Text))}, {status[0].Colour}"));
        lines.AddRange(RightHandItems(cells.Where(c => c.Text != "CRS" && !values.Contains(c)).ToList()));
    }

    private static void SquawkPage(List<DrawnCell> cells, List<RmpLine> lines)
    {
        var label = cells.First(c => c.Text == "SQWK");
        var code = cells.FirstOrDefault(c => c.Text.All(char.IsDigit) && c.Text.Length == 4);
        foreach (var c in cells.Where(c => c != label && c != code).OrderBy(c => c.Y))
        {
            // The mode over the label; the code under it; the ident key on the third row's left.
            if (code != null && c.Y > code.Y && lines.All(l => !l.Spoken.StartsWith("SQWK", StringComparison.Ordinal)))
                lines.Add(new RmpLine($"SQWK: {code.Text}, {code.Colour}"));
            lines.Add(new RmpLine(A350DrawLayout.Speak(new[] { c }), c.X < LeftColumn ? A350Rmp.ActivationKey(Row(c.Y)) : null));
        }
        if (code != null && lines.All(l => !l.Spoken.StartsWith("SQWK", StringComparison.Ordinal)))
            lines.Add(new RmpLine($"SQWK: {code.Text}, {code.Colour}"));
    }

    /// <summary>A page's title over its side keys: the MENU and the datalink router.</summary>
    private static void KeyList(List<DrawnCell> cells, List<RmpLine> lines)
    {
        lines.AddRange(cells.Where(c => c.Y < 120).Select(c => new RmpLine(A350DrawLayout.Speak(new[] { c }))));
        lines.AddRange(RightHandItems(cells));
        lines.AddRange(LeftHandItems(cells));
    }

    /// <summary>The right-hand items: a text beside the ">" on its row, its key that row's.</summary>
    private static IEnumerable<RmpLine> RightHandItems(List<DrawnCell> cells) =>
        cells.Where(c => c.Text == ">").OrderBy(c => c.Y).Select(arrow =>
        {
            var label = cells.FirstOrDefault(c => c.Text != ">" && c.X >= RightColumn && Math.Abs(c.Y - arrow.Y) <= 12);
            return new RmpLine($"{label?.Text} >, {arrow.Colour}".TrimStart(), A350Rmp.LineSelectKey(Row(arrow.Y)));
        });

    /// <summary>The left-hand items: "&lt;" and the text beside it, its key that row's.</summary>
    private static IEnumerable<RmpLine> LeftHandItems(List<DrawnCell> cells) =>
        cells.Where(c => c.Text == "<").OrderBy(c => c.Y).Select(arrow =>
        {
            var label = cells.FirstOrDefault(c => c.Text != "<" && c.X < LeftColumn && Math.Abs(c.Y - arrow.Y) <= 12);
            return new RmpLine($"< {label?.Text}, {arrow.Colour}", A350Rmp.ActivationKey(Row(arrow.Y)));
        });

    /// <summary>DATALINK INIT and VHF DATALINK PROVIDER: a title; each label over its value, on the left and on the right; a cyan key.</summary>
    private static void LabelledPage(List<DrawnCell> cells, List<RmpLine> lines)
    {
        lines.AddRange(cells.Where(c => c.Y < 120).Select(c => new RmpLine(A350DrawLayout.Speak(new[] { c }))));
        var body = cells.Where(c => c.Y >= 120 && c.Text != "<" && !LeftHandItems(cells).Any(l => l.Spoken.Contains(c.Text, StringComparison.Ordinal) && c.Y > 480)).ToList();
        var keys = body.Where(c => c.Colour == "cyan").OrderBy(c => c.Y).ToList();
        body = body.Except(keys).ToList();
        foreach (var column in new[] { body.Where(c => c.X < LeftColumn).ToList(), body.Where(c => c.X >= LeftColumn).ToList() })
        {
            var labels = column.Where(c => c.Colour == "white" && column.Any(v => v.Y > c.Y && v.Y - c.Y < 120)).OrderBy(c => c.Y).Take(1).ToList();
            foreach (var label in labels)
            {
                var values = column.Where(v => v.Y > label.Y && v.Y - label.Y < 120).ToList();
                lines.Add(new RmpLine($"{label.Text}: {Words(values)}, {values[0].Colour}"));
            }
        }
        if (keys.Count > 0)
            lines.Add(new RmpLine($"{string.Join(" ", keys.Select(c => c.Text))}, {keys[0].Colour}", A350Rmp.ActivationKey(Row(keys[^1].Y))));
        lines.AddRange(LeftHandItems(cells));
    }

    /// <summary>DATALINK COM STATUS: a table of the data radios (name, number, equipment status, data link status).</summary>
    private static void ComStatus(List<DrawnCell> cells, List<RmpLine> lines)
    {
        lines.AddRange(cells.Where(c => c.Y < 120).Select(c => new RmpLine(A350DrawLayout.Speak(new[] { c }))));
        foreach (var name in cells.Where(c => c.X < 100 && c.Y > 300).OrderBy(c => c.Y))
        {
            var row = cells.Where(c => c != name && Math.Abs(c.Y - name.Y) <= 12).OrderBy(c => c.X).ToList();
            string number = string.Concat(row.Where(c => c.X < 250).Select(c => c.Text));
            var equipment = row.FirstOrDefault(c => c.X is >= 250 and < 450);
            var link = row.FirstOrDefault(c => c.X >= 450);
            lines.Add(new RmpLine($"{name.Text}{(number.Length > 0 ? " " + number : "")}: EQPT STATUS {equipment?.Text}, DTLNK COM STATUS {link?.Text}, {(equipment ?? name).Colour}"));
        }
    }
}
