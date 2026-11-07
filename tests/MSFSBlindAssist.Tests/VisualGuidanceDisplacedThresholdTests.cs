// Visual guidance's glidepath starts at the runway's landing threshold, not its pavement start
// (NavigationCalculator.LandingThreshold; RunwayLandingThresholdTests pins the point itself). This pins
// the whole path against a real ILS: where the threshold is displaced the glideslope crosses the painted
// threshold, so a path measured from the pavement start sits higher by the offset times tan(angle):
// 2,055 ft of offset is 108 ft at 3 degrees, every approach, every aircraft.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class VisualGuidanceDisplacedThresholdTests
{
    private const double FeetPerNm = 6076.12;
    private const double OffsetFt = 2055.0;

    private static Runway End(double offsetFt) => new()
    {
        AirportICAO = "TEST",
        RunwayID = "13R",
        Heading = 121.0,
        StartLat = 40.6480,
        StartLon = -73.8160,
        ThresholdOffset = offsetFt,
        ThresholdElevation = 13.0,
    };

    // A point on the extended centerline, before the given point by distanceNm.
    private static (double Lat, double Lon) Before(double lat, double lon, double heading, double distanceNm) =>
        NavigationCalculator.CalculateTouchdownAimPoint(lat, lon, (heading + 180.0) % 360.0, distanceNm * FeetPerNm);

    [Fact]
    public void An_aircraft_on_the_ils_reads_on_the_path_over_a_displaced_threshold()
    {
        // Exactly on the ILS (3 degrees, crossing the threshold at the 50 ft datum the bias stands for): on the
        // path at every half mile from 5 NM; measured from the pavement start, which comes first on the
        // approach, it would read about 108 ft high.
        const double angleDeg = 3.0;
        const double biasFt = 50.0;
        var rwy = End(OffsetFt);
        var threshold = VisualGuidanceThreshold.Of(rwy);
        var pavementStart = new VisualGuidanceThreshold(rwy.StartLat, rwy.StartLon, rwy.Heading);

        for (double d = 5.0; d >= 0.5; d -= 0.5)
        {
            var aircraft = Before(threshold.Lat, threshold.Lon, rwy.Heading, d);
            double heightFt = d * FeetPerNm * Math.Tan(angleDeg * Math.PI / 180.0) + biasFt;

            double along = VisualGuidanceJoin.Locate(aircraft.Lat, aircraft.Lon, threshold).AlongNm;
            double alongFromStart = VisualGuidanceJoin.Locate(aircraft.Lat, aircraft.Lon, pavementStart).AlongNm;

            Assert.InRange(heightFt - VisualGuidanceVertical.PathHeightFt(along, angleDeg, biasFt), -2.0, 2.0);
            Assert.InRange(heightFt - VisualGuidanceVertical.PathHeightFt(alongFromStart, angleDeg, biasFt), 106.0, 110.0);
        }
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(-0.3)]
    public void Cross_track_is_unchanged_on_a_displaced_runway(double sideNm)
    {
        // The threshold is on the start's centerline, so the sideways distance is the same from either.
        var rwy = End(OffsetFt);
        var threshold = VisualGuidanceThreshold.Of(rwy);
        var onCenterline = Before(threshold.Lat, threshold.Lon, rwy.Heading, 4.0);
        (double Lat, double Lon) aircraft = NavigationCalculator.CalculateTouchdownAimPoint(
            onCenterline.Lat, onCenterline.Lon, (rwy.Heading + 90.0) % 360.0, sideNm * FeetPerNm);

        double fromThreshold = VisualGuidanceJoin.Locate(aircraft.Lat, aircraft.Lon, threshold).CrossNm;
        double fromStart = VisualGuidanceJoin.Locate(aircraft.Lat, aircraft.Lon,
            new VisualGuidanceThreshold(rwy.StartLat, rwy.StartLon, rwy.Heading)).CrossNm;

        // Within 6 ft: the great circle's course drifts by thousandths of a degree over the offset.
        Assert.InRange(Math.Abs(fromStart - fromThreshold), 0.0, 0.001);
        Assert.Equal(Math.Abs(sideNm), Math.Abs(fromThreshold), 3);
        Assert.Equal(Math.Sign(fromStart), Math.Sign(fromThreshold));
    }
}
