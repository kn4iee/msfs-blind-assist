namespace MSFSBlindAssist.Aircraft.A330.Display;

/// <summary>
/// Decides what the pilot hears about the display reader, and when. A lasting reason (no reader installed, no access,
/// an unrecognised or out of date setup, a stopped reader) is spoken once, not before 30 seconds after the aircraft
/// became active, so the reader's normal start-up says nothing; a different lasting reason is spoken once at once; the
/// reader running again after a spoken reason says "A330 displays ready" once. A passing state is never spoken.
/// Pure: the caller supplies the clock (milliseconds) and speaks the line.
/// </summary>
internal sealed class A330ReaderNotice
{
    internal const long QuietMs = 30_000;
    internal const string Ready = "A330 displays ready";

    private readonly long _activeAtMs;
    private A330ReaderState? _spoken;   // the lasting state last spoken; null when none is outstanding

    public A330ReaderNotice(long activeAtMs) => _activeAtMs = activeAtMs;

    public string? Next(A330ReaderState state, long nowTicks)
    {
        if (A330ReaderStatus.IsLasting(state))
        {
            if (_spoken == state) return null;
            // the first notice waits out the quiet period; a new reason after one was spoken does not
            if (_spoken == null && nowTicks - _activeAtMs < QuietMs) return null;
            _spoken = state;
            return A330ReaderStatus.Text(state) + ". The MCDU window cannot read its screen.";
        }
        if (state == A330ReaderState.Running && _spoken != null)
        {
            _spoken = null;
            return Ready;
        }
        return null;
    }
}
