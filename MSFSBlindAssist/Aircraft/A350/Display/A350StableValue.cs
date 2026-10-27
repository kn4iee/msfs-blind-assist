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
}
