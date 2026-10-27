namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What a row of the CDU window is.</summary>
public enum Bbs757CduRowKind { Title, Label, Value, Scratchpad, Blank }

/// <summary>
/// One row of the CDU window: its kind, the line select key it belongs to (1-6 for a label or a
/// value row, 0 otherwise) and the text the window lists. Kind and line are the row's identity: they
/// survive the screen changing around it, where an index does not.
/// </summary>
public sealed record Bbs757CduRow(Bbs757CduRowKind Kind, int Line, string Text);

/// <summary>
/// The CDU window's rows with an identity, so the cursor follows the pilot's line instead of a list
/// index ([MD11-17]'s rule, the MD-11's <c>Md11McduRows</c>). A label row is listed only when it has
/// text, so a constraint deleted above the cursor removes a row and shifts every index below it; a
/// title going ACT → MOD after an entry is the same page. Before this, both moved the cursor (plan 08).
/// The texts are the window's today (<see cref="Bbs757CduFrame.ToDisplayLines"/> lists them); plan 09
/// changes them. <see cref="Bbs757CduRowKind.Blank"/> is not produced yet: it is plan 09's, for rows it
/// keeps on screen that carry nothing. Pure.
/// </summary>
public static class Bbs757CduRows
{
    /// <summary>
    /// The title; then for each line select key its label row indented three spaces (left out when
    /// blank) and its data row numbered "n:"; then the scratchpad. Trailing spaces are dropped,
    /// leading ones kept, so a right-hand field stays on the right in braille.
    /// </summary>
    public static IReadOnlyList<Bbs757CduRow> Build(Bbs757CduScreen screen)
    {
        var rows = new List<Bbs757CduRow> { new(Bbs757CduRowKind.Title, 0, screen.Lines[0].TrimEnd()) };
        for (int key = 1; key <= 6; key++)
        {
            string label = screen.Lines[key * 2 - 1].TrimEnd();
            string data = screen.Lines[key * 2].TrimEnd();
            if (label.Trim().Length > 0) rows.Add(new(Bbs757CduRowKind.Label, key, "   " + label));
            rows.Add(new(Bbs757CduRowKind.Value, key, data.Trim().Length > 0 ? $"{key}: {data}" : $"{key}:"));
        }
        rows.Add(new(Bbs757CduRowKind.Scratchpad, 0, screen.Lines[Bbs757CduFrame.ScratchpadRow].TrimEnd()));
        return rows;
    }

    /// <summary>
    /// Where the cursor goes in <paramref name="after"/> when it was on <paramref name="selected"/> in
    /// <paramref name="before"/>: the row with the same kind and line; if that row is gone (a label that
    /// went blank), the first row of the same line; else the same index, clamped. With nothing to go
    /// on (no rows before, or no row selected) the index is kept, clamped.
    /// </summary>
    public static int Restore(IReadOnlyList<Bbs757CduRow> before, int selected, IReadOnlyList<Bbs757CduRow> after)
    {
        if (after.Count == 0) return -1;
        int Clamp(int i) => Math.Clamp(i, 0, after.Count - 1);
        if (selected < 0 || selected >= before.Count) return Clamp(selected < 0 ? 0 : selected);

        var was = before[selected];
        for (int i = 0; i < after.Count; i++)
            if (after[i].Kind == was.Kind && after[i].Line == was.Line) return i;
        if (was.Line > 0)
            for (int i = 0; i < after.Count; i++)
                if (after[i].Line == was.Line) return i;
        return Clamp(selected);
    }

    /// <summary>Where a new page starts: line 1's value row, else the first row.</summary>
    public static int PageStart(IReadOnlyList<Bbs757CduRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == Bbs757CduRowKind.Value && rows[i].Line == 1) return i;
        return 0;
    }

    /// <summary>
    /// Whether a new title row is another page: the page itself (<see cref="Bbs757CduTitle.Identity"/>,
    /// ACT and MOD folded away) or its page number differs. ACT RTE 1 LEGS 1/7 → MOD RTE 1 LEGS 1/7 is
    /// the same page, and so is 1/2 → 1/3 (an inserted waypoint adding a page: the MD-11's rule, the
    /// cursor stays); 1/7 → 2/7 is not.
    /// </summary>
    public static bool IsPageChange(string oldTitleRow, string newTitleRow) =>
        Bbs757CduTitle.Identity(oldTitleRow) != Bbs757CduTitle.Identity(newTitleRow)
        || Bbs757CduTitle.Counter(oldTitleRow)?.Page != Bbs757CduTitle.Counter(newTitleRow)?.Page;

    /// <summary>
    /// Whether the title says something new (<see cref="Bbs757CduTitle.Spoken"/> differs; a blank title
    /// never does): ACT → MOD and 1/2 → 1/3 included. The window speaks it, as every other aircraft's
    /// CDU window speaks a changed title (owner, 2026-10-27); whether the cursor moves is
    /// <see cref="IsPageChange"/>'s question.
    /// </summary>
    public static bool IsTitleChange(string oldTitleRow, string newTitleRow)
    {
        string spoken = Bbs757CduTitle.Spoken(newTitleRow);
        return spoken.Length > 0 && spoken != Bbs757CduTitle.Spoken(oldTitleRow);
    }
}
