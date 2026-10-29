namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// A value read from a draw, taken only when two reads in a row agree: a memory copy can span two A350
/// frames [A350-2], so a single read may show a half-updated screen, and that must never be announced.
/// </summary>
public sealed class A350StableValue<T>
{
    private T? _pending;
    private bool _hasPending;

    public T? Current { get; private set; }
    public bool HasValue { get; private set; }

    /// <summary>Offers a fresh read; true (with the value) when it is confirmed and differs from what was taken.</summary>
    public bool Offer(T value, out T taken)
    {
        taken = value;
        if (_hasPending && EqualityComparer<T>.Default.Equals(value, _pending))
        {
            if (HasValue && EqualityComparer<T>.Default.Equals(value, Current)) return false;
            Current = value;
            HasValue = true;
            return true;
        }
        _pending = value;
        _hasPending = true;
        return false;
    }

    public void Reset()
    {
        _pending = default; _hasPending = false;
        Current = default; HasValue = false;
    }
}

/// <summary>What the display reader says about itself.</summary>
public static class A350DisplayStatus
{
    public const string GettingReady = "A350 displays getting ready";
    public const string Ready = "A350 displays ready";
    public static string NotAvailable(string reason) => "A350 displays not available: " + reason;
    public static string Unreadable(string gauge, int index) => $"A350 {NameOf(gauge, index)} cannot be read at the moment";
    public static string Readable(string gauge, int index) => $"A350 {NameOf(gauge, index)} readable again";
    public const string ReadableAgain = "A350 displays readable again";

    /// <summary>Why no display can be read (<see cref="A350ReaderStatus"/>).</summary>
    public static string CannotRead(A350ReaderFailure why) => "A350 displays cannot be read: " + why switch
    {
        A350ReaderFailure.NoSim => "the simulator is not running",
        A350ReaderFailure.AccessDenied => "MSFSBA may not read the simulator; is MSFS running as administrator?",
        A350ReaderFailure.NotFound => "the A350 is not found in the simulator",
        _ => "an error in the reader, written to the log",
    };

    /// <summary>A drawn display as the pilot knows it: an EFIS is one side's PFD and ND, the CTR the E/WD and SD,
    /// MFD 0 both MFDs.</summary>
    public static string NameOf(string gauge, int index) => (gauge, index) switch
    {
        ("CTR", _) => "E/WD and SD",
        ("EFIS", 0) => "captain PFD and ND",
        ("EFIS", 1) => "first officer PFD and ND",
        ("MFD", _) => "MFDs",
        ("FCU", _) => "FCU display",
        ("RMP", _) => $"RMP {index + 1} display",
        ("SFD", _) => "standby flight display",
        ("SND", _) => "standby navigation display",
        ("BAT", _) => "BAT display",
        _ => gauge,
    };
}
