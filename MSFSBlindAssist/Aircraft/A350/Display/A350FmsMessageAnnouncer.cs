namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The FMS messages heard with the MFD window closed (pure), as the A32NX's MCDU says them ([A320-34]): from each
/// background read of the captain's MFD, the message the FMS just posted in the scratchpad (or in its place on a checklist
/// page), said as the MFD window says it (<see cref="A350MfdPage.MessageJustArrived"/>) once two reads in a row agree on it,
/// so a copy read across a frame never speaks its one-off text. The first agreed read is the silent baseline; an emptied
/// scratchpad says nothing; a read with no page decides nothing. An MFD window starts from <see cref="Agreed"/> and hands its
/// own last read back as it closes (<see cref="Baseline"/>), under <see cref="A350PfdMonitor"/>'s lock.
/// </summary>
public sealed class A350FmsMessageAnnouncer
{
    /// <summary>The call-out's Ctrl+M row.</summary>
    public const string MuteKey = "A350_FMS_MESSAGES";
    public const string MuteLabel = "FMS messages (MFD scratchpad)";

    private string? _agreed;    // the scratchpad two reads last agreed on; null: the next agreement is the baseline
    private string? _pending;   // the last read, waiting for a second that agrees

    /// <summary>The scratchpad two reads last agreed on, or null before the first agreement.</summary>
    public string? Agreed => _agreed;

    /// <summary>The message to say now, or null.</summary>
    public string? Observe(IReadOnlyList<MfdLine> lines)
    {
        if (A350MfdPage.Scratchpad(lines) is not { } pad)
            return null;
        var previous = _pending;
        _pending = pad;
        if (pad != previous)
            return null;
        var said = A350MfdPage.MessageJustArrived(_agreed, pad);
        _agreed = pad;
        return said;
    }

    /// <summary>Takes <paramref name="pad"/> as agreed, as two agreeing reads would: an MFD window's last read as it closes,
    /// a message the window said itself.</summary>
    public void Baseline(string pad) { _agreed = pad; _pending = pad; }

    public void Reset() { _agreed = null; _pending = null; }
}
