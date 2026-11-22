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

    /// Null when the frame is not complete or not drawn, is not an MCDU's, or a text's first character rounds to a cell outside the 14 rows and 24 columns.
    public static A330McduScreen? From(A330Frame frame)
    {
        if (frame.Gauge != A330Gauge.McduCaptain && frame.Gauge != A330Gauge.McduFirstOfficer) return null;
        if (!frame.Complete || !frame.Drawn) return null;

        var screen = new A330McduScreen(frame.HiddenRowsLeftOut);
        foreach (var text in frame.Texts.OrderBy(t => t.Order))
        {
            if (string.IsNullOrEmpty(text.Text)) continue;
            int row = A330McduGeometry.RowOf(text.Baseline);
            int column = A330McduGeometry.ColumnOf(text.X);
            if (row < 0 || row >= A330McduGeometry.Rows || column < 0 || column >= A330McduGeometry.Columns) return null;

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
