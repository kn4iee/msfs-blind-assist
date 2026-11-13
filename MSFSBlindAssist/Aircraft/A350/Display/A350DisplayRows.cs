namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// A display window's rows (pure), as the Fenix's display windows make theirs (plan 26; `FenixChannelDisplayForm.RowsFor` and
/// `RowToFollow`): what the display shows, one row each, or one status row "&lt;display&gt;: not available, &lt;reason&gt;" in place of
/// the content, so the list never holds a stale row and never an empty one. The reasons: "display off" (its screen is dark, whatever
/// the last draw held), "not read yet" (no draw young enough), "nothing shown" (a draw with nothing on it) and "read failed" (a reader
/// that threw; it costs that window only, reported once per distinct failure by the caller's report).
/// </summary>
public static class A350DisplayRows
{
    private static string StatusStart(string display) => display + ": not available, ";

    private static IReadOnlyList<string> Status(string display, string reason) => new[] { StatusStart(display) + reason };

    /// <summary>The rows the window lists for one read of <paramref name="display"/>.</summary>
    /// <param name="read">The display's lines from a draw; it runs on the UI thread and does no I/O.</param>
    /// <param name="report">Where a reader that throws is reported, as "read failed: &lt;type&gt;: &lt;message&gt;".</param>
    public static IReadOnlyList<string> For(string display, A350DrawCapture? capture, bool dark,
        Func<A350DrawCapture, IReadOnlyList<string>> read, Action<string>? report = null)
    {
        if (dark) return Status(display, "display off");
        if (capture is null) return Status(display, "not read yet");
        IReadOnlyList<string> rows;
        try
        {
            rows = read(capture);
        }
        catch (Exception ex)
        {
            report?.Invoke($"read failed: {ex.GetType().Name}: {ex.Message}");
            return Status(display, "read failed");
        }
        // The list box throws on a null row, and on the timer's tick: nothing shown.
        return rows is { Count: > 0 } && rows.All(r => r is not null) ? rows : Status(display, "nothing shown");
    }

    /// <summary>Whether <paramref name="rows"/> is <paramref name="display"/>'s one status row: nobody's row to follow.</summary>
    public static bool IsStatusRow(string display, IReadOnlyList<string> rows) =>
        rows.Count == 1 && rows[0].StartsWith(StatusStart(display), StringComparison.Ordinal);

    /// <summary>The index in <paramref name="rows"/> of the row the pilot was on (<paramref name="followed"/>, its text before the
    /// refresh), or -1 when none reads like it: a row with that exact text, the nearest to <paramref name="currentIndex"/> when several
    /// are; else the row sharing the most leading words with it, at least one (split on spaces, compared ordinally), the nearest on a
    /// tie. "Altitude -63 feet, STD" is followed to "Altitude -64 feet, STD" though a row appeared above it.</summary>
    public static int RowToFollow(IReadOnlyList<string> rows, string? followed, int currentIndex)
    {
        if (followed is null) return -1;
        int exact = Nearest(rows, currentIndex, row => string.Equals(row, followed, StringComparison.Ordinal) ? 1 : 0);
        if (exact != -1) return exact;
        var words = Words(followed);
        return Nearest(rows, currentIndex, row => LeadingWordsShared(words, Words(row)));
    }

    // The index of the row with the highest score above 0, the nearest to currentIndex among equals (the lower index when two are
    // as near); -1 when no row scores.
    private static int Nearest(IReadOnlyList<string> rows, int currentIndex, Func<string, int> score)
    {
        int best = -1, bestScore = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            int s = score(rows[i]);
            if (s == 0) continue;
            if (s > bestScore || (s == bestScore && Math.Abs(i - currentIndex) < Math.Abs(best - currentIndex)))
            {
                best = i;
                bestScore = s;
            }
        }
        return best;
    }

    private static string[] Words(string row) => row.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static int LeadingWordsShared(string[] first, string[] second)
    {
        int shared = 0;
        while (shared < first.Length && shared < second.Length && string.Equals(first[shared], second[shared], StringComparison.Ordinal))
            shared++;
        return shared;
    }
}
