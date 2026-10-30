namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The OIS's pause overlay, heard with its window closed (X6): the aircraft pauses the sim itself at the top of descent (OPTIONS >
/// SIMULATION > PAUSE AT T.O.D) and when the autopilot drops under time compression (AP DISCONNECT PROTECTION), and the OIS shows why
/// over its RESUME FLIGHT button. Said once a pause, in the OIS's words, with where to resume. A pause with no reason of the aircraft's
/// has no such button and is SimStateTracker's to say. Pure.
/// </summary>
public sealed class A350PauseAlerts
{
    public const string MuteKey = "A350_OIS_PAUSE", MuteLabel = "OIS pause alerts";

    private const string Resume = "The simulation has been paused. Resume with RESUME FLIGHT on the OIS.";

    private bool _said;

    /// <summary>What to say for one read, else null.</summary>
    public string? Observe(Display.A350SimPause pause)
    {
        if (!pause.IsPaused)
        {
            _said = false;
            return null;
        }
        if (_said)
            return null;
        string? why = pause.Type switch
        {
            Display.A350SimPause.TopOfDescent => "Top of descent pause.",
            Display.A350SimPause.AutopilotDisconnect => "Autopilot disconnected during time compression.",
            _ => null,
        };
        if (why == null)
            return null;
        _said = true;
        return $"{why} {Resume}";
    }
}
