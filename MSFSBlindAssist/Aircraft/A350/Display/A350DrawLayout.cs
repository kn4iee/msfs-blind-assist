namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>One drawn text as read: trimmed, with its colour's name and its screen position.</summary>
public sealed record DrawnCell(string Text, string Colour, float X, float Y);

/// <summary>A screen rectangle, right and bottom exclusive.</summary>
public readonly record struct A350Rect(float X0, float Y0, float X1, float Y1)
{
    public bool Contains(float x, float y) => x >= X0 && x < X1 && y >= Y0 && y < Y1;
}

/// <summary>
/// Reading order for what a gauge drew: texts grouped into rows (by their y in steps of a row height)
/// and read left to right, and the rectangles that cut a gauge into the displays it draws
/// (docs/inibuilds-a350.md "Gauges and where they draw"). A row is spoken with its colours, because on
/// the A350 a colour carries meaning (amber caution, red warning, cyan action, green normal).
/// </summary>
public static class A350DrawLayout
{
    private const float Far = float.MaxValue;

    /// <summary>CTR: the engine display (left, top), the SD page, the SD permanent data line, the WD (right).</summary>
    public static readonly A350Rect CtrEngineDisplay = new(0, 0, 822, 300);
    public static readonly A350Rect CtrSystemDisplay = new(0, 300, 822, 940);
    public static readonly A350Rect CtrPermanentData = new(0, 940, 822, Far);
    public static readonly A350Rect CtrWarningDisplay = new(822, 0, Far, Far);

    /// <summary>EFIS: the PFD on the left half, the ND on the right (the ND subtracts 836 itself).</summary>
    public static readonly A350Rect EfisPfd = new(0, 0, 836, Far);
    public static readonly A350Rect EfisNd = new(836, 0, Far, Far);

    public static IReadOnlyList<DrawnText> Region(IEnumerable<DrawnText> texts, A350Rect r) =>
        texts.Where(t => r.Contains(t.X, t.Y)).ToList();

    private static int RowOf(float y, float rowHeight) => (int)MathF.Floor(y / rowHeight + 0.5f);   // JavaScript Math.round

    /// <summary>Non-blank texts as rows in reading order (the prototype's toLines).</summary>
    public static IReadOnlyList<IReadOnlyList<DrawnCell>> Rows(IEnumerable<DrawnText> texts, float rowHeight = 12)
    {
        var rows = new List<IReadOnlyList<DrawnCell>>();
        List<DrawnCell>? row = null;
        int? last = null;
        foreach (var t in texts.Where(t => !string.IsNullOrWhiteSpace(t.Text))
                     .OrderBy(t => RowOf(t.Y, rowHeight)).ThenBy(t => t.X))
        {
            int k = RowOf(t.Y, rowHeight);
            if (row == null || k != last) { row = new List<DrawnCell>(); rows.Add(row); last = k; }
            row.Add(new DrawnCell(t.Text.Trim(), t.ColourName, t.X, t.Y));
        }
        return rows;
    }

    /// <summary>A row as spoken: each run of one colour, then that colour ("PARK BRK, green; ON, amber").</summary>
    public static string Speak(IReadOnlyList<DrawnCell> row)
    {
        var parts = new List<string>();
        int i = 0;
        while (i < row.Count)
        {
            int j = i;
            while (j + 1 < row.Count && row[j + 1].Colour == row[i].Colour) j++;
            parts.Add(string.Join(" ", row.Skip(i).Take(j - i + 1).Select(c => c.Text)) + ", " + row[i].Colour);
            i = j + 1;
        }
        return string.Join("; ", parts);
    }
}
