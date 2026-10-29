using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What D and Shift+D say: the route distance to the destination and the distance to the top of descent, in the words of the
/// PMDGs and the FBW Airbuses ("541 miles to destination: 01:16:44"). Both are the A350's own VNAV variables (its var table):
/// the stock GPS flight plan is not the A350's FMS. Their meanings are the iniBuilds A300's, the same vendor's FMS with the
/// same names, measured there in flight (2026-10-14): <c>FG_VNAV_DIST_DEST</c> is the route distance to the destination,
/// <c>INI_TD_DIST_TOTAL</c> the distance to the top of descent, negative once past it; <c>INI_TD_DIST</c> is a figure within
/// a leg and not used. Every one reads 0 with no route (the A350 on the stand, 2026-10-26), so 0 is "not available", never
/// "0 miles". Still to check on the A350 itself in flight (the sim list). Pure: the time comes from the caller's formatter.
/// </summary>
public static class A350RouteDistances
{
    public const string DestinationVar = "FG_VNAV_DIST_DEST";
    public const string TopOfDescentVar = "INI_TD_DIST_TOTAL";

    /// <summary>Half a mile: a destination nearer than this is no route to speak of (no route reads 0), and a top of descent
    /// this close or behind is past (the FBW Airbuses' limit for the top of descent).</summary>
    private const double MinimumNm = 0.5;

    /// <summary>D: "541 miles to destination: 01:16:44", <paramref name="eta"/> giving the time at the ground speed (empty when
    /// too slow for it to mean anything).</summary>
    public static string Destination(double destinationNm, double groundSpeedKnots, Func<double, double, string> eta) =>
        HasRoute(destinationNm)
            ? Miles(destinationNm, "destination", groundSpeedKnots, eta)
            : "Distance to destination not available";

    /// <summary>Shift+D: "443 miles to top of descent: 01:02:53". No route (the destination distance says whether there is one)
    /// is not available; a top of descent reading exactly 0 is not yet computed; half a mile ahead or less, or behind, is past.</summary>
    public static string TopOfDescent(double destinationNm, double toTopOfDescentNm, double groundSpeedKnots,
        Func<double, double, string> eta)
    {
        if (!HasRoute(destinationNm) || !double.IsFinite(toTopOfDescentNm))
            return "Top of descent not available";
        if (toTopOfDescentNm == 0)
            return "Top of descent not yet computed";
        return toTopOfDescentNm <= MinimumNm
            ? "Past top of descent"
            : Miles(toTopOfDescentNm, "top of descent", groundSpeedKnots, eta);
    }

    /// <summary>A distance that is no number (NaN, infinity) is no route, and never spoken.</summary>
    private static bool HasRoute(double destinationNm) => double.IsFinite(destinationNm) && destinationNm >= MinimumNm;

    /// <summary>Whole miles, an exact half rounded up ("F0" would round it to even and say "0 miles" at the least distance that
    /// passes), "mile" for one.</summary>
    private static string Miles(double nm, string to, double groundSpeedKnots, Func<double, double, string> eta)
    {
        double whole = Math.Round(nm, MidpointRounding.AwayFromZero);
        return $"{whole.ToString("F0", CultureInfo.InvariantCulture)} {(whole == 1 ? "mile" : "miles")} to {to}{eta(nm, groundSpeedKnots)}";
    }
}
