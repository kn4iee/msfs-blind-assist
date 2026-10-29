namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// The FMS message line as the MFD window follows it (pure): a message is said on the first read that shows it
/// (<see cref="A350MfdPage.MessageJustArrived"/>). It starts from the background's agreed read
/// (<see cref="A350PfdMonitor.MfdWindowOpening"/>), so a message the background has read only once is the window's to say,
/// and its last read of the captain's MFD goes back to the background as it closes (<see cref="A350PfdMonitor.MfdWindowClosed"/>),
/// so what the window said is not said again (final review 2026-10-29: a message closing the window was said twice, and
/// one posted as it opened was said by neither).
/// </summary>
public sealed class A350MfdWindowMessages
{
    private string? _last;   // the last read, of either side; null: the next read is the baseline

    public A350MfdWindowMessages(string? backgroundAgreed) => _last = backgroundAgreed;

    /// <summary>The window's last read of the captain's message line, or null when it has read none: the background reads
    /// the captain's MFD only, so a read of the first officer's is never handed to it.</summary>
    public string? Captain { get; private set; }

    /// <summary>The message to say now, or null, from a read of <paramref name="side"/>'s page (0 the captain's).</summary>
    public string? Observe(IReadOnlyList<MfdLine> lines, int side)
    {
        var pad = A350MfdPage.Scratchpad(lines);
        var said = A350MfdPage.MessageJustArrived(_last, pad);
        if (pad != null)
        {
            _last = pad;
            if (side == 0) Captain = pad;
        }
        return said;
    }
}
