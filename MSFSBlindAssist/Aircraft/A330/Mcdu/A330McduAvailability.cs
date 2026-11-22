namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// What the MCDU window says about whether it can read the MCDU (spec D3), at most once per reason while it is open:
/// "A330 MCDU unavailable: &lt;reason&gt;" when the reason is new (a lasting reader state, or the 30 s "cannot read"),
/// and after such a sentence "MCDU ready" once, with the first page shown. A state on its way (starting, waiting for
/// the aircraft, reading) says nothing and forgets nothing: the same reason after it is not said again. Never an AI
/// read. Pure.
/// </summary>
internal sealed class A330McduAvailability
{
    public const string Ready = "MCDU ready";

    private string? _said;     // the reason last said, until a page is shown
    private bool _readyOwed;   // an unavailable sentence was said and no page has been shown since

    /// <summary>The window was opened: a reason it cannot read is said again.</summary>
    public void Opened()
    {
        _said = null;
        _readyOwed = false;
    }

    /// <summary>The sentence to say at this refresh, or null.</summary>
    /// <param name="unavailableReason">Why the MCDU cannot be read, or null.</param>
    /// <param name="pageShown">True when the window lists a page.</param>
    public string? Next(string? unavailableReason, bool pageShown)
    {
        if (pageShown)
        {
            _said = null;
            if (!_readyOwed) return null;
            _readyOwed = false;
            return Ready;
        }
        if (unavailableReason is null || unavailableReason == _said) return null;
        _said = unavailableReason;
        _readyOwed = true;
        return "A330 MCDU unavailable: " + unavailableReason;
    }
}
