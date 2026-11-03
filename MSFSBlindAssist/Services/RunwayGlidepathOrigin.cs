using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Where a runway end's glidepath starts: its landing threshold, which is the pavement start
/// (<see cref="Runway.StartLat"/>, <see cref="Runway.StartLon"/>) moved along
/// <see cref="Runway.Heading"/> by <see cref="Runway.ThresholdOffset"/> (feet, as
/// <c>LittleNavMapProvider</c> stores <c>runway_end.offset_threshold</c>). A real glideslope crosses the
/// painted threshold, so a glidepath measured from the start of a displaced runway sits higher by the
/// offset times tan(angle) (2,055 ft is 108 ft at 3 degrees). Only the glidepath's distance and
/// altitude use it: the centerline, and so the cross-track error, is the same from the start.
/// </summary>
internal static class RunwayGlidepathOrigin
{
    /// <summary>The end's landing threshold; its start when the offset is 0 or unknown (not a finite, positive number).</summary>
    public static (double Lat, double Lon) Of(Runway runway)
    {
        double offsetFt = runway.ThresholdOffset;
        if (!double.IsFinite(offsetFt) || offsetFt <= 0.0)
            return (runway.StartLat, runway.StartLon);
        return NavigationCalculator.CalculateTouchdownAimPoint(runway.StartLat, runway.StartLon, runway.Heading, offsetFt);
    }

    /// <summary>Distance in nautical miles from the end's landing threshold to the point.</summary>
    public static double DistanceNm(Runway runway, double lat, double lon)
    {
        var (originLat, originLon) = Of(runway);
        return NavigationCalculator.CalculateDistance(lat, lon, originLat, originLon);
    }
}
