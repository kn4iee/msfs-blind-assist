using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

internal readonly record struct A330McduCell(char Character, bool Small, A330Rgb? Colour);   // null while the reader gives no colour (M14)

/// <summary>The MCDU screen's 14 rows of 24 cells, placed from one complete frame.</summary>
internal sealed class A330McduScreen
{
    private readonly A330McduCell?[,] _cells = new A330McduCell?[A330McduGeometry.Rows, A330McduGeometry.Columns];

    private A330McduScreen(bool hiddenRowsLeftOut) => HiddenRowsLeftOut = hiddenRowsLeftOut;

    /// True when the reader vouches that rows the display does not show are left out of the frame.
    public bool HiddenRowsLeftOut { get; }

    /// A page counter ("1/7") drawn less than this many rows under the title row's baseline is the title row's.
    private const double CounterOnTitleRowBelow = 0.75;

    /// <summary>
    /// Null when the frame is not complete or not drawn, is not an MCDU's, or a text's first character rounds to a cell
    /// outside the 14 rows and 24 columns. A text takes its nearest row, with one exception: a page counter
    /// (<c>1/7</c>) drawn less than <see cref="CounterOnTitleRowBelow"/> of a row under the title (P/N XLOAD's sits
    /// 0.54 under, measured 2026-11-20) goes on the title row at its own column, when no other text fills those cells
    /// of it, as the Fenix and FlyByWire windows show the page counter on the title line.
    /// </summary>
    public static A330McduScreen? From(A330Frame frame)
    {
        if (frame.Gauge != A330Gauge.McduCaptain && frame.Gauge != A330Gauge.McduFirstOfficer) return null;
        if (!frame.Complete || !frame.Drawn) return null;

        var placed = new List<(A330Text Text, int Row, int Column)>();
        foreach (var text in frame.Texts.OrderBy(t => t.Order))
        {
            if (string.IsNullOrEmpty(text.Text)) continue;
            int row = A330McduGeometry.RowOf(text.Baseline);
            int column = A330McduGeometry.ColumnOf(text.X);
            if (row < 0 || row >= A330McduGeometry.Rows || column < 0 || column >= A330McduGeometry.Columns) return null;
            placed.Add((text, row, column));
        }

        var titleRowTaken = new bool[A330McduGeometry.Columns];
        foreach (var p in placed.Where(p => p.Row == 0)) Take(titleRowTaken, p.Text.Text, p.Column);
        for (int i = 0; i < placed.Count; i++)
        {
            var (text, row, column) = placed[i];
            if (row != 1 || A330McduGeometry.RowPosition(text.Baseline) >= CounterOnTitleRowBelow) continue;
            if (!IsPageCounter(text.Text) || !Free(titleRowTaken, text.Text, column)) continue;
            placed[i] = (text, 0, column);
            Take(titleRowTaken, text.Text, column);
        }

        var screen = new A330McduScreen(frame.HiddenRowsLeftOut);
        foreach (var (text, row, column) in placed)
        {
            bool small = text.Font == A330McduGeometry.SmallFont;
            for (int k = 0; k < text.Text.Length; k++)
            {
                int c = column + k;
                if (c >= A330McduGeometry.Columns) break;
                char ch = text.Text[k];
                if (ch == ' ') continue;
                screen._cells[row, c] = new A330McduCell(ch, small, text.Colour);
            }
        }
        return screen;
    }

    /// A page counter: digits, a slash, digits ("1/7", "12/14").
    private static bool IsPageCounter(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, "^[0-9]+/[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// Marks the cells of a row a text fills (a space fills none, as when it is placed).
    private static void Take(bool[] taken, string text, int column)
    {
        for (int k = 0; k < text.Length && column + k < A330McduGeometry.Columns; k++)
            if (text[k] != ' ') taken[column + k] = true;
    }

    /// True when none of the cells the text would fill is taken.
    private static bool Free(bool[] taken, string text, int column)
    {
        for (int k = 0; k < text.Length && column + k < A330McduGeometry.Columns; k++)
            if (text[k] != ' ' && taken[column + k]) return false;
        return true;
    }

    public A330McduCell? this[int row, int column] =>
        row < 0 || row >= A330McduGeometry.Rows || column < 0 || column >= A330McduGeometry.Columns ? null : _cells[row, column];

    /// The row's characters, spaces for empty cells, right-trimmed.
    public string Text(int row)
    {
        var sb = new System.Text.StringBuilder(A330McduGeometry.Columns);
        for (int c = 0; c < A330McduGeometry.Columns; c++) sb.Append(this[row, c]?.Character ?? ' ');
        return sb.ToString().TrimEnd();
    }
}
