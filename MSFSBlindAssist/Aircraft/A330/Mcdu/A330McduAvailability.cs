namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// What the MCDU window says about whether it can read the MCDU (spec D3), in the Fenix and FlyByWire windows' words
/// (owner, 2026-11-23): "MCDU disconnected" once when it cannot (a lasting reader state, or the 30 s "cannot read"),
/// whatever the reason and however often the reason changes (the status box shows the reason), and after it "MCDU
/// connected" once, with the first page shown. A state on its way (starting, waiting for the aircraft, reading) says
/// nothing and forgets nothing. Never an AI read. Pure.
/// </summary>
internal sealed class A330McduAvailability
{
    public const string Connected = "MCDU connected", Disconnected = "MCDU disconnected";

    private bool _disconnected;   // "MCDU disconnected" was said and no page has been shown since

    /// <summary>The window was opened: an MCDU it cannot read is said again.</summary>
    public void Opened() => _disconnected = false;

    /// <summary>The sentence to say at this refresh, or null.</summary>
    /// <param name="unavailableReason">Why the MCDU cannot be read, or null.</param>
    /// <param name="pageShown">True when the window lists a page.</param>
    public string? Next(string? unavailableReason, bool pageShown)
    {
        if (pageShown)
        {
            if (!_disconnected) return null;
            _disconnected = false;
            return Connected;
        }
        if (unavailableReason is null || _disconnected) return null;
        _disconnected = true;
        return Disconnected;
    }
}
