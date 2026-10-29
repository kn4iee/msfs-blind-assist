using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Visual landing guidance on the A350 starts from the A330neo's widebody profile, the closest Airbus fly-by-wire
/// widebody (itself estimates pending an in-sim calibration), not the A320 baseline every unprofiled aircraft gets; the
/// flare at about 40 ft radio altitude is Airbus's own figure for the A330/A340/A350/A380 family (Safety First).
/// </summary>
public class A350VisualGuidanceProfileTests
{
    [Fact]
    public void The_a350_flies_the_widebody_profile()
    {
        var p = new IniBuildsA350Definition().GetVisualGuidanceProfile();
        Assert.Equal(4.5, p.TypicalApproachAoaDeg);
        Assert.Equal(145.0, p.ReferenceVrefKnots);
        Assert.Equal(2.0, p.MaxPitchRateDegPerSec);
        Assert.Equal(2.5, p.MaxBankRateDegPerSec);
        Assert.Equal(70.0, p.GlideslopeAltitudeBiasFt);
        Assert.Equal(30.0, p.FlareAltitudeBiasFt);
        Assert.Equal(40.0, p.FlareTriggerWheelHeightFt);
        Assert.Equal(5.0, p.FlareTargetPitchDeg);
    }
}
