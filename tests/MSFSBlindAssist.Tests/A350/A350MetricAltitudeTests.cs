using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 10 (2026-10-29): the A350's METRIC ALT pushbutton (its FCU row, INI_FCU_METRIC_STATE: 0 "only feet",
/// 1 "showing meters") is the aircraft's metric altitude, as the A380's MTRS: the A and Q read-outs in metres and the
/// altitude call-outs in 300 m bands while it is on.</summary>
public class A350MetricAltitudeTests
{
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;

    public A350MetricAltitudeTests()
    {
        _def = new IniBuildsA350Definition
        {
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = _ => false,
        };
        _def.Attach(new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero));
    }

    [Fact]
    public void The_metric_alt_row_reads_the_aircraft_s_own_state()
    {
        var row = _def.GetVariables()[IniBuildsA350Definition.MetricAltitudeKey];
        Assert.Equal("INI_FCU_METRIC_STATE", row.Name);
        Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous, row.UpdateFrequency);
    }

    [Fact]
    public void Feet_until_metric_alt_shows_meters()
    {
        Assert.False(_def.MetricAltitude);   // unread: the A350 has the mode, its rest is feet
        _cache[IniBuildsA350Definition.MetricAltitudeKey] = 1;
        Assert.True(_def.MetricAltitude);
        _cache[IniBuildsA350Definition.MetricAltitudeKey] = 0;
        Assert.False(_def.MetricAltitude);
    }
}
