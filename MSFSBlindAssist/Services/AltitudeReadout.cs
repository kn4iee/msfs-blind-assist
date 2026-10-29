using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The A and Q read-outs (altitude above sea level, above ground) of an aircraft with a metric altitude mode
/// (<see cref="Aircraft.IAircraftDefinition.MetricAltitude"/>): metres while it is on, feet otherwise, the unit named
/// either way since it can change. Invariant digits, a plain minus below sea level, and never "-0".
/// </summary>
public static class AltitudeReadout
{
    public static string Text(double feet, bool metric)
    {
        double value = Math.Round(metric ? feet * 0.3048 : feet, MidpointRounding.AwayFromZero) + 0.0;   // + 0.0: -0 is 0
        return value.ToString("0", CultureInfo.InvariantCulture) + (metric ? " meters" : " feet");
    }
}
