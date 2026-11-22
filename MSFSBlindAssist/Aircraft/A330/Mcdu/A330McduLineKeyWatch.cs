using MSFSBlindAssist.Forms.IniA330;

namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// After a line key, what the window says from the pages that follow (measured 2026-11-20: an entry can be refused with
/// no message, and a message can wait behind others, so a read-back that never misleads): a new title is the title's
/// announcement and nothing more; the pressed line's data row changed, that row's listing text
/// ("3: MSF773         IRS INIT>"); only its label row changed, the label; the scratchpad line changed, its own
/// read-back and nothing more. None of these within <see cref="WatchMs"/>: "No change" only when nothing at all on the
/// page changed in that time; when another row changed, nothing (the pilot reads the list). Rows are compared as the
/// window lists them. Pure: the clock is the caller's.
/// </summary>
internal sealed class A330McduLineKeyWatch
{
    public const int WatchMs = 1500;
    public const string NoChange = "No change";

    private readonly int _line;
    private readonly long _startMs;
    private readonly (string Title, string Label, string Data, string Pad) _before;
    private readonly string[] _rowsBefore;
    private bool _pageChanged;   // a page since the press differed from the one at the press, in any row

    /// <param name="line">The pressed key's line, 1 to 6.</param>
    /// <param name="page">The page shown when the key was sent.</param>
    /// <param name="startMs">When the key was sent.</param>
    public A330McduLineKeyWatch(int line, IReadOnlyList<A330McduLine> page, long startMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(line, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(line, 6);
        _line = line;
        _startMs = startMs;
        _before = Parts(page);
        _rowsBefore = Rows(page);
    }

    /// <summary>For a page shown at <paramref name="nowMs"/>: whether the watch is over, and what to say (null: nothing).</summary>
    public (bool Over, string? Say) OnPage(IReadOnlyList<A330McduLine> page, long nowMs)
    {
        var now = Parts(page);
        if (now.Title != _before.Title) return (true, null);
        if (now.Data != _before.Data) return (true, DataRow(now.Data));
        if (now.Label != _before.Label) return (true, now.Label.Trim().Length > 0 ? now.Label.Trim() : DataRow(now.Data));
        if (now.Pad != _before.Pad) return (true, null);
        if (!_pageChanged && !Rows(page).SequenceEqual(_rowsBefore, StringComparer.Ordinal)) _pageChanged = true;
        return Expired(nowMs) ? (true, _pageChanged ? null : NoChange) : (false, null);
    }

    /// <summary>True once the watch has run its time (with no page shown it ends saying nothing).</summary>
    public bool Expired(long nowMs) => nowMs - _startMs >= WatchMs;

    private string DataRow(string data) => data.Length > 0 ? $"{_line}: {data}" : $"{_line}:";

    private (string Title, string Label, string Data, string Pad) Parts(IReadOnlyList<A330McduLine> page) => (
        CduLineText.Title(page),
        CduLineText.ForWindow(page[2 * _line - 1]),
        CduLineText.ForWindow(page[2 * _line]),
        CduLineText.ForWindow(page[A330McduLines.ScratchpadRow]));

    private static string[] Rows(IReadOnlyList<A330McduLine> page) => page.Select(CduLineText.ForWindow).ToArray();
}
