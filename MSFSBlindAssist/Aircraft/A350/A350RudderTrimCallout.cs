namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The rudder trim said as it moves (the owner's backlog, 2026-10-27), as the pitch trim is by the base class: in tenths of a
/// degree as the SD's F/CTL page shows it, a new tenth said only once the value is <see cref="Hysteresis"/> past its boundary
/// (the pitch trim's band: a trim resting on a boundary would flip every sample), the first read after a load silent, in
/// the status line's words (<see cref="A350StatusLines.RudderTrim"/>: "0.5 degrees left"). Pure.
/// </summary>
public sealed class A350RudderTrimCallout
{
    /// <summary>The rudder trim's status line, the call-out's Ctrl+M row.</summary>
    public const string Key = "A350_STAT_RUDDER_TRIM";

    /// <summary>The pitch trim's band (BaseAircraftDefinition.TrimHysteresis), in degrees.</summary>
    public const double Hysteresis = 0.03;

    private double? _said;   // the tenth last said, or the baseline

    /// <summary>The sentence to say for <paramref name="degrees"/> (negative nose left), or null.</summary>
    public string? Observe(double degrees)
    {
        double step = Step(degrees);
        if (_said is not double said)
        {
            _said = step;
            return null;
        }
        if (step == said || Step(degrees + (step > said ? -Hysteresis : Hysteresis)) == said)
            return null;
        _said = step;
        return "Rudder trim " + (step == 0 ? "neutral" : A350StatusLines.RudderTrim(step));
    }

    /// <summary>Makes the next read the silent baseline (a new flight).</summary>
    public void Reset() => _said = null;

    private static double Step(double degrees) => Math.Round(degrees, 1) + 0.0;   // + 0.0: -0.0 is 0
}
