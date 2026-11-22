namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>One screen row as the window's source: its text as drawn (24 columns, right-trimmed) and its cells.</summary>
internal sealed record A330McduLine(int Row, string Text, IReadOnlyList<A330McduCell?> Cells);

internal static class A330McduLines
{
    public const int ScratchpadRow = 13;

    /// <summary>What a flight-plan row with no name reads in its seven name cells: lower case, so it is never taken
    /// for an ident (the plan's "Measured on 2026-11-20": a row with no name is read as unnamed, never skipped).</summary>
    public const string Unnamed = "unnamed";

    /// The F-PLN's waypoint name takes columns 0 to 6; its time starts at column 8.
    private const int NameCells = 7, AfterName = 8;

    /// The F-PLN's waypoint rows: the data rows of lines 1 to 5 (line 6 is the destination's).
    private static readonly int[] WaypointRows = { 2, 4, 6, 8, 10 };

    private const string FlightPlanTitle = "FROM";

    /// <summary>
    /// The 14 rows, row 0 first. Each cell shows as one character, so a column never moves. On an F-PLN page (its
    /// title starts with FROM), a waypoint row whose name cells are empty while it has text from column 8 on reads
    /// <see cref="Unnamed"/> in those seven cells; its <see cref="A330McduLine.Cells"/> stay as drawn.
    /// </summary>
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

        if (IsFlightPlan(lines))
            foreach (int r in WaypointRows)
                if (HasNoName(lines[r])) lines[r] = lines[r] with { Text = Unnamed + lines[r].Text[NameCells..] };
        return lines;
    }

    /// <summary>True when the page is an F-PLN: its title starts with FROM ("FROM", "FROM TMPY").</summary>
    public static bool IsFlightPlan(IReadOnlyList<A330McduLine> lines) =>
        lines.Count > 0 && lines[0].Text.TrimStart().StartsWith(FlightPlanTitle, StringComparison.Ordinal);

    /// <summary>An F-PLN waypoint row's name: its first seven columns, trimmed.</summary>
    public static string WaypointName(A330McduLine line) =>
        (line.Text.Length > NameCells ? line.Text[..NameCells] : line.Text).Trim();

    private static bool HasNoName(A330McduLine line)
    {
        for (int c = 0; c < NameCells; c++) if (line.Cells[c] is not null) return false;
        for (int c = AfterName; c < line.Cells.Count; c++) if (line.Cells[c] is not null) return true;
        return false;
    }
}
