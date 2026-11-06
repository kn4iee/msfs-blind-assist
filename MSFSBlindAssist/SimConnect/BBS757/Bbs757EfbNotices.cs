namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Deciding which of BlueBird's notification-panel entries to speak. The panel's entries are read into
/// <see cref="Bbs757EfbScreenModel.Notices"/>; this picks the ones new since the last read, each spoken once per
/// appearance, and stays quiet for a short window after the pilot's own press (the press's own outcome says enough).
/// Pure.
/// </summary>
public static class Bbs757EfbNotices
{
    /// <summary>How long after the pilot's own press no notification is spoken: the press's own outcome says enough.</summary>
    public const int QuietAfterPressMs = 4000;

    /// <summary>
    /// What to say for the panel's entries: the texts in <paramref name="now"/> not in <paramref name="before"/> (the same
    /// words at the same time), joined with spaces, top first; null when there are none or the pilot's own press was
    /// less than <see cref="QuietAfterPressMs"/> ago. The words match with case, spaces and punctuation ignored, and a
    /// time not read matches any (small text wobbles between reads); the same words at another time are a new
    /// appearance ("Chocks attached." at 12:06Z and again at 12:09Z).
    /// </summary>
    public static string? ToSpeak(IReadOnlyList<Bbs757EfbNotice> before, IReadOnlyList<Bbs757EfbNotice> now, long msSinceOwnPress)
    {
        if (msSinceOwnPress < QuietAfterPressMs) return null;
        var fresh = now.Where(n => !before.Any(b => Same(b, n))).Select(n => n.Text).ToList();
        return fresh.Count == 0 ? null : string.Join(" ", fresh);
    }

    private static bool Same(Bbs757EfbNotice a, Bbs757EfbNotice b) =>
        Bare(a.Text) == Bare(b.Text) && (a.Time == null || b.Time == null || a.Time == b.Time);

    private static string Bare(string t) => string.Concat(t.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));
}
