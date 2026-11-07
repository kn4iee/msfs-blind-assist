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
        Assert.Equal(40.0, p.FlareTriggerWheelHeightFt);
    }

    // The flare bias is the datum's height above the wheels in the flare attitude, which the guidance subtracts there (review
    // 2026-11-07: 15, the parked height with the struts compressed, put the wheels about 3 ft high in the flare). The flight model's
    // main gear contact is 17.63 ft aft of and 16.45 ft below the datum: at +5 degrees, 16.45 cos 5 + 17.63 sin 5 = 17.9.
    [Fact]
    public void The_a350_flare_bias_is_the_datum_height_in_the_flare_attitude()
    {
        double flarePitch = 5.0 * Math.PI / 180.0;
        double datumAboveWheelsFt = 16.45 * Math.Cos(flarePitch) + 17.63 * Math.Sin(flarePitch);
        Assert.Equal(datumAboveWheelsFt, new IniBuildsA350Definition().GetVisualGuidanceProfile().FlareAltitudeBiasFt, 0);
    }

    // Plan 18 Task 6: the glidepath bias as the guidance now measures, from the landing threshold (NavigationCalculator.LandingThreshold since the visual guidance overhaul, oasis1701/msfs-blind-assist#345). Three coupled
    // ILS approaches, each the datum over d x tan 3 with d from the threshold: OMDB 12L twice (2026-10-30, threshold displaced 1493 ft,
    // medians 52 and 53 ft) and VCBI 04 (2026-11-03, no displacement, 53 to 56 ft from 5 NM to 0.5 NM, median 54): 53.
    [Fact]
    public void The_a350_biases_are_measured_from_the_threshold()
    {
        Assert.Equal(53.0, new IniBuildsA350Definition().GetVisualGuidanceProfile().GlideslopeAltitudeBiasFt);
    }
}
