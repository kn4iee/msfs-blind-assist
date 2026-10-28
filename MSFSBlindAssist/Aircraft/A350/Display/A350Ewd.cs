namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>An ECAM action box beside a line: cyan while the item is still to do (Airbus colours: cyan is
/// an action to take, white an action done).</summary>
public enum EcamBox { None, ToDo, Done }

/// <summary>One line of the warning display: its cells with colours, its baseline, whether the ECAM cursor
/// (the cyan wide frame the ECP wheel moves) is on it, and its action box.</summary>
public sealed record EcamLine(IReadOnlyList<DrawnCell> Cells, float Y, bool Selected, EcamBox Box)
{
    public string Text => string.Join(" ", Cells.Select(c => c.Text));

    /// <summary>The line as spoken: each colour run with its colour ("SURV TCAS STBY, amber").</summary>
    public string Spoken => A350DrawLayout.Speak(Cells);
}

/// <summary>The CTR gauge read as the pilot sees it: the engine block, the warning display, the SD page and
/// the SD's permanent data line (TAT, SAT, ISA, UTC, GW, CG, FOB).</summary>
public sealed record A350EwdView(
    IReadOnlyList<IReadOnlyList<DrawnCell>> Engine, IReadOnlyList<EcamLine> Warning,
    IReadOnlyList<IReadOnlyList<DrawnCell>> SystemDisplay, IReadOnlyList<IReadOnlyList<DrawnCell>> PermanentData,
    bool Powered);

/// <summary>Turns a CTR capture into an <see cref="A350EwdView"/> (pure). Texts are placed at their
/// baseline; a frame belongs to the warning-display row whose glyphs it overlaps most.</summary>
public static class A350Ewd
{
    /// <summary>A row's glyphs sit above its baseline: about the ECAM font's height.</summary>
    private const float GlyphHeight = 22;

    // A dot leader between an item and its action ("APU MASTER SW ......OFF", the T.O memo's "TO CONFIG ..TEST")
    // reads as a colon; a trailing one is dropped. A screen reader would otherwise say "dot" thirty times. A done
    // memo item is drawn with a spaced colon ("FLAPS : TO"), read as the leader is; never between digits (a time).
    private static readonly System.Text.RegularExpressions.Regex Leader = new(@"\s*\.{2,}\s*", System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly System.Text.RegularExpressions.Regex SpacedColon = new(@"(?<!\d)\s+:\s+", System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static string WithoutLeaders(string text)
    {
        var t = text.Trim();
        return SpacedColon.Replace(Leader.Replace(t, m => m.Index + m.Length == t.Length ? "" : ": "), ": ");
    }

    // The warning display's right column of memos (ADS-B TRAFFIC ON, COMPANY MSG, live x 1253), beside the main column
    // (memos and procedures, x 856-886); the CLEAR prompt further right (x 1514) belongs to its procedure.
    private const float MemoColumnX = 1240, MemoColumnEnd = 1480;

    private static bool InMemoColumn(DrawnCell c) => c.X >= MemoColumnX && c.X < MemoColumnEnd;

    private static bool IsDotRow(IReadOnlyList<DrawnCell> row) => row.All(c => c.Text.Trim('.', ' ').Length == 0);

    private static float Overlap(float a0, float a1, float b0, float b1) => Math.Max(0, Math.Min(a1, b1) - Math.Max(a0, b0));

    public static A350EwdView Parse(A350DrawCapture capture)
    {
        var wdRows = A350DrawLayout.Rows(A350DrawLayout.Region(capture.Texts, A350DrawLayout.CtrWarningDisplay))
            .Where(r => !IsDotRow(r)).ToList();
        var wdFrames = capture.Frames.Where(f => A350DrawLayout.CtrWarningDisplay.Contains(f.X, f.Y)).ToList();

        int RowOf(DrawnFrame f)
        {
            int best = -1; float bestOverlap = 0;
            for (int i = 0; i < wdRows.Count; i++)
            {
                float y = wdRows[i][0].Y;
                float o = Overlap(f.Y, f.Y + f.H, y - GlyphHeight, y + 2);
                if (o > bestOverlap) { bestOverlap = o; best = i; }
            }
            return best;
        }

        var selected = new HashSet<int>();
        var boxes = new Dictionary<int, EcamBox>();
        foreach (var f in wdFrames)
        {
            int row = RowOf(f);
            if (row < 0) continue;
            string colour = A350Rgba.NameOf(f.Colour);
            if (f.Wide && colour == "cyan") selected.Add(row);
            else if (!f.Wide && f.W < 30 && f.H < 30)   // an action box (15 x 20)
                boxes[row] = colour == "cyan" ? EcamBox.ToDo : EcamBox.Done;
        }

        // Each column reads on its own, the main one first: a row drawn across both is two lines.
        var main = new List<EcamLine>();
        var memos = new List<EcamLine>();
        for (int i = 0; i < wdRows.Count; i++)
        {
            var cells = wdRows[i].Select(c => c with { Text = WithoutLeaders(c.Text) }).ToList();
            var left = cells.Where(c => !InMemoColumn(c)).ToList();
            var right = cells.Where(InMemoColumn).ToList();
            if (left.Count > 0) main.Add(new EcamLine(left, left[0].Y, selected.Contains(i), boxes.GetValueOrDefault(i, EcamBox.None)));
            if (right.Count > 0) memos.Add(new EcamLine(right, right[0].Y, false, EcamBox.None));
        }
        var lines = main.Concat(memos).ToList();
        return new A350EwdView(
            A350DrawLayout.Rows(A350DrawLayout.Region(capture.Texts, A350DrawLayout.CtrEngineDisplay)),
            lines,
            A350DrawLayout.Rows(A350DrawLayout.Region(capture.Texts, A350DrawLayout.CtrSystemDisplay)),
            A350DrawLayout.Rows(A350DrawLayout.Region(capture.Texts, A350DrawLayout.CtrPermanentData)),
            capture.Texts.Any(t => !string.IsNullOrWhiteSpace(t.Text)));
    }
}
