namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>One screen row as the window's source: its text as drawn (24 columns, right-trimmed) and its cells.</summary>
internal sealed record A330McduLine(int Row, string Text, IReadOnlyList<A330McduCell?> Cells);

internal static class A330McduLines
{
    public const int ScratchpadRow = 13;

    /// The 14 rows, row 0 first. Each cell shows as one character, so a column never moves.
    public static IReadOnlyList<A330McduLine> From(A330McduScreen screen)
    {
        var lines = new List<A330McduLine>(A330McduGeometry.Rows);
        for (int r = 0; r < A330McduGeometry.Rows; r++)
        {
            var cells = new A330McduCell?[A330McduGeometry.Columns];
            var sb = new System.Text.StringBuilder(A330McduGeometry.Columns);
            for (int c = 0; c < A330McduGeometry.Columns; c++)
            {
                cells[c] = screen[r, c];
                sb.Append(cells[c] is { } cell ? A330McduCharacters.Display(cell) : " ");
            }
            lines.Add(new A330McduLine(r, sb.ToString().TrimEnd(), cells));
        }
        return lines;
    }
}
