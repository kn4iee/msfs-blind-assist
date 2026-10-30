using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>One NAV radio as spoken: frequency, then what it receives ("Nav 1: 113.90, IDBL, localizer course 119, DME 12.3 nautical miles"),
/// "no signal" with none. Numbers with a point in every culture.</summary>
public static class NavRadioFormat
{
    public static string Single(string label, double freq, double hasNav, double hasLoc, double hasGS, double hasDME, double dme, double locCourse,
        double gsAngle, string? ident, string? name)
    {
        var info = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{label}: {freq:F2}") };
        if (hasNav <= 0)
        {
            info.Add("no signal");
            return string.Join(", ", info);
        }
        if (!string.IsNullOrWhiteSpace(ident))
            info.Add(ident);
        if (!string.IsNullOrWhiteSpace(name))
            info.Add(name);
        if (hasLoc > 0)
            info.Add(string.Create(CultureInfo.InvariantCulture, $"localizer course {(int)locCourse}"));
        if (hasGS > 0)
            info.Add(string.Create(CultureInfo.InvariantCulture, $"glideslope {gsAngle:F1} degrees"));
        if (hasDME > 0)
            info.Add(string.Create(CultureInfo.InvariantCulture, $"DME {dme:F1} nautical miles"));
        return string.Join(", ", info);
    }
}
