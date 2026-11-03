// Visual guidance's glidepath starts at the runway's landing threshold, not its pavement start.
//
// Where the threshold is displaced (Runway.ThresholdOffset, feet), a real ILS glideslope crosses
// the painted threshold, so a glidepath measured from the start sits higher by the offset times
// tan(angle): 2,055 ft of offset is 108 ft at 3 degrees, every approach, every aircraft.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RunwayGlidepathOriginTests
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
    private static (double Lat, double Lon) Before((double Lat, double Lon) point, double heading, double distanceNm) =>
        NavigationCalculator.CalculateTouchdownAimPoint(point.Lat, point.Lon, (heading + 180.0) % 360.0, distanceNm * FeetPerNm);

    [Fact]
    public void No_offset_gives_the_start()
    {
        var rwy = End(0.0);

        var origin = RunwayGlidepathOrigin.Of(rwy);

        Assert.Equal(rwy.StartLat, origin.Lat);
        Assert.Equal(rwy.StartLon, origin.Lon);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-50.0)]
    [InlineData(double.PositiveInfinity)]
    public void An_unknown_offset_gives_the_start(double offsetFt)
    {
        var rwy = End(offsetFt);

        var origin = RunwayGlidepathOrigin.Of(rwy);

        Assert.Equal(rwy.StartLat, origin.Lat);
        Assert.Equal(rwy.StartLon, origin.Lon);
    }

    [Fact]
    public void An_offset_moves_the_origin_along_the_heading()
    {
        var rwy = End(OffsetFt);

        var origin = RunwayGlidepathOrigin.Of(rwy);

        Assert.Equal(OffsetFt / FeetPerNm, NavigationCalculator.CalculateDistance(rwy.StartLat, rwy.StartLon, origin.Lat, origin.Lon), 4);
        Assert.Equal(rwy.Heading, NavigationCalculator.CalculateBearing(rwy.StartLat, rwy.StartLon, origin.Lat, origin.Lon), 1);
    }

    [Fact]
    public void Only_this_ends_offset_applies()
    {
        // One runway, two ends: the reciprocal's displaced threshold is its own, never this end's.
        var thisEnd = End(0.0);
        var reciprocal = new Runway
        {
            AirportICAO = "TEST",
            RunwayID = "31L",
            Heading = 301.0,
            StartLat = 40.6280,
            StartLon = -73.7700,
            ThresholdOffset = OffsetFt,
        };

        Assert.Equal((thisEnd.StartLat, thisEnd.StartLon), RunwayGlidepathOrigin.Of(thisEnd));
        Assert.NotEqual((reciprocal.StartLat, reciprocal.StartLon), RunwayGlidepathOrigin.Of(reciprocal));
    }

    [Fact]
    public void The_ideal_altitude_at_a_distance_is_measured_from_the_threshold()
    {
        var rwy = End(OffsetFt);
        var threshold = RunwayGlidepathOrigin.Of(rwy);
        var aircraft = Before(threshold, rwy.Heading, 3.0);

        Assert.Equal(3.0, RunwayGlidepathOrigin.DistanceNm(rwy, aircraft.Lat, aircraft.Lon), 3);
        // The pavement start comes first on the approach: from it, the offset short.
        Assert.Equal(3.0 - OffsetFt / FeetPerNm,
            NavigationCalculator.CalculateDistance(aircraft.Lat, aircraft.Lon, rwy.StartLat, rwy.StartLon), 3);
    }

    [Theory]
    [InlineData(0.3)]
    [InlineData(-0.3)]
    public void Cross_track_is_unchanged_on_a_displaced_runway(double sideNm)
    {
        // The threshold is on the start's centerline, so the cross-track error is the same from either.
        var rwy = End(OffsetFt);
        var onCenterline = Before(RunwayGlidepathOrigin.Of(rwy), rwy.Heading, 4.0);
        (double Lat, double Lon) aircraft = NavigationCalculator.CalculateTouchdownAimPoint(
            onCenterline.Lat, onCenterline.Lon, (rwy.Heading + 90.0) % 360.0, sideNm * FeetPerNm);
        var origin = RunwayGlidepathOrigin.Of(rwy);

        // The distance off the centerline, and the side (the guidance uses only the sign of CalculateCrossTrackError).
        double fromStart = NavigationCalculator.CalculateDistanceToLocalizer(aircraft.Lat, aircraft.Lon, rwy.StartLat, rwy.StartLon, rwy.Heading);
        double fromThreshold = NavigationCalculator.CalculateDistanceToLocalizer(aircraft.Lat, aircraft.Lon, origin.Lat, origin.Lon, rwy.Heading);
        int sideFromStart = Math.Sign(NavigationCalculator.CalculateCrossTrackError(aircraft.Lat, aircraft.Lon, rwy.StartLat, rwy.StartLon, rwy.Heading));
        int sideFromThreshold = Math.Sign(NavigationCalculator.CalculateCrossTrackError(aircraft.Lat, aircraft.Lon, origin.Lat, origin.Lon, rwy.Heading));

        // Within 6 ft: the great circle's course drifts by thousandths of a degree over the offset (2 ft here).
        Assert.InRange(Math.Abs(fromStart - fromThreshold), 0.0, 0.001);
        Assert.Equal(Math.Abs(sideNm), fromThreshold, 3);
        Assert.Equal(sideFromStart, sideFromThreshold);
    }

    [Fact]
    public void The_glidepath_error_on_a_displaced_runway_matches_the_ils()
    {
        // An aircraft exactly on the ILS (3 degrees, crossing the threshold at the 50 ft datum the bias
        // stands for) reads on the glidepath at every half mile from 5 NM; measured from the pavement
        // start, which comes first on the approach, it read about 108 ft high.
        const double angleDeg = 3.0;
        const double biasFt = 50.0;
        var rwy = End(OffsetFt);
        var threshold = RunwayGlidepathOrigin.Of(rwy);

        for (double d = 5.0; d >= 0.5; d -= 0.5)
        {
            var aircraft = Before(threshold, rwy.Heading, d);
            double altMsl = rwy.ThresholdElevation + d * FeetPerNm * Math.Tan(angleDeg * Math.PI / 180.0) + biasFt;

            double error = VisualGuidanceManager.GlidepathAltitudeErrorFt(
                altMsl, rwy.ThresholdElevation, RunwayGlidepathOrigin.DistanceNm(rwy, aircraft.Lat, aircraft.Lon), angleDeg, biasFt);
            double fromStart = VisualGuidanceManager.GlidepathAltitudeErrorFt(
                altMsl, rwy.ThresholdElevation,
                NavigationCalculator.CalculateDistance(aircraft.Lat, aircraft.Lon, rwy.StartLat, rwy.StartLon), angleDeg, biasFt);

            Assert.InRange(error, -2.0, 2.0);
            Assert.InRange(fromStart, 106.0, 110.0);
        }
    }
}
