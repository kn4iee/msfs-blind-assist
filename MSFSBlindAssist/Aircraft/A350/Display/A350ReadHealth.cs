namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// Whether each display can be read (pure; the service reports every draw). A draw fails when its copy tears, and a
/// few in a row are normal; live 2026-10-28 every draw failed for many minutes and the FMA call-outs, the ECAM and the
/// MFD window simply went quiet. When a display's draws have failed for <see cref="UnreadableAfter"/>, at least
/// <see cref="FailuresToSay"/> of them in a row (a display drawn only every 15 s while nothing reads it failed twice and
/// was said unreadable, final review 2026-10-30), the pilot is told once which display it is, and told again once <see cref="GoodToRecover"/> of its draws in a row read (one good draw
/// among failures said so and flapped, review 2026-10-28). Each display keeps its own count: live 2026-10-28 the E/WD
/// read on some draws while the PFD, ND and MFD never did, and one count for all let each E/WD read hide them.
/// A pause in the reader's draws longer than <see cref="Pause"/> (no A350 while a flight loads, nothing asking) starts
/// every display's window again. A pause is the time from one draw's end to the next one's start, of any display: live
/// 2026-10-28, with the A350's memory at 495 MB, one draw took 5 to 9 s, so measuring from end to end called every draw
/// a pause and nothing was ever said; and a display the monitor draws in turn may come round only every half minute.
/// </summary>
public sealed class A350ReadHealth
{
    public static readonly TimeSpan UnreadableAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Pause = TimeSpan.FromSeconds(5);
    public const int GoodToRecover = 3;
    public const int FailuresToSay = 3;

    private sealed class Display
    {
        public DateTime? FailingSince;
        public bool Said;
        public int Good, Failed;
    }

    private readonly Dictionary<(string, int), Display> _displays = new();
    private DateTime? _last;

    /// <summary>One draw of a display (<paramref name="gauge"/>, <paramref name="index"/>), started at
    /// <paramref name="started"/> and ended at <paramref name="ended"/>; the sentence to speak, if any.</summary>
    public string? Report(string gauge, int index, bool ok, DateTime started, DateTime ended)
    {
        if (_last is { } last && started - last > Pause)
            foreach (var d in _displays.Values)
            {
                d.FailingSince = null;
                d.Failed = 0;
            }
        if (_last is not { } previous || ended > previous) _last = ended;
        if (!_displays.TryGetValue((gauge, index), out var display))
            _displays[(gauge, index)] = display = new Display();
        if (ok)
        {
            display.FailingSince = null;
            display.Failed = 0;
            if (!display.Said || ++display.Good < GoodToRecover)
                return null;
            display.Said = false;
            display.Good = 0;
            return A350DisplayStatus.Readable(gauge, index);
        }
        display.Good = 0;
        display.Failed++;
        display.FailingSince ??= started;
        if (display.Said || ended - display.FailingSince < UnreadableAfter || display.Failed < FailuresToSay) return null;
        display.Said = true;
        return A350DisplayStatus.Unreadable(gauge, index);
    }
}

/// <summary>Why the reader could not draw at all: no sim process, no access to it, no A350 in it, or an error in the
/// reader itself (a copy or a draw that threw).</summary>
public enum A350ReaderFailure { NoSim, AccessDenied, NotFound, Error }

/// <summary>
/// Whether the reader can reach the A350 at all (pure; the service reports each draw request's outcome). Review
/// 2026-10-29: while the A350 could not be found, every draw request walked the sim's whole address space back to back,
/// and the call-outs simply stopped, unannounced. After a look that found nothing the next waits <see cref="RetryAfter"/>.
/// A cause is said once, and once more when it changes: no access to the sim at once (it never cures itself: MSFS running
/// as administrator), the rest after <see cref="SayAfter"/> of an A350 flight (a flight load takes that long to publish
/// the A350); outside an A350 flight (the menu, another aircraft) nothing is said and nothing counts. A request that
/// draws after a cause was said says the displays read again, once.
/// </summary>
public sealed class A350ReaderStatus
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan SayAfter = TimeSpan.FromSeconds(30);

    private DateTime? _lookFailedAt, _failingSince;
    private A350ReaderFailure? _said;

    /// <summary>Whether the reader may look for the A350 now: never within <see cref="RetryAfter"/> of a look that found
    /// nothing (about 25,000 region queries and over a second each).</summary>
    public bool MayLocate(DateTime now) => _lookFailedAt is not { } at || now - at >= RetryAfter;

    /// <summary>A draw request's outcome: null when it drew, else why it could not; the sentence to speak, if any.</summary>
    public string? Report(A350ReaderFailure? failure, bool a350Flight, DateTime now)
    {
        if (failure is not { } why)
        {
            _lookFailedAt = _failingSince = null;
            if (_said == null) return null;
            _said = null;
            return A350DisplayStatus.ReadableAgain;
        }
        if (why != A350ReaderFailure.Error) _lookFailedAt = now;
        if (!a350Flight)
        {
            _failingSince = null;
            return null;
        }
        _failingSince ??= now;
        if (_said == why || why != A350ReaderFailure.AccessDenied && now - _failingSince < SayAfter) return null;
        _said = why;
        return A350DisplayStatus.CannotRead(why);
    }
}
