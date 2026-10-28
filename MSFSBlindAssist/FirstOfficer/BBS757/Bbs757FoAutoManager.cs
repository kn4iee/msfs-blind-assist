namespace MSFSBlindAssist.FirstOfficer.BBS757;

/// <summary>
/// The BlueBird 757 First Officer's automation layer. Deliberately empty, as the MD-11's: the flap
/// lever is the Captain's (BlueBird publishes no flap speed schedule to follow; Shift+1 to Shift+6
/// read the maneuver speeds), and gear plus autopilot engagement are the universal automation
/// service's job. AutoFlapsEnabled is stored from settings and never acted on.
/// </summary>
public sealed class Bbs757FoAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
