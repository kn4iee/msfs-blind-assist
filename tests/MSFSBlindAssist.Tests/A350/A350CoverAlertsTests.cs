using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The OIS's cover banners heard with its window closed (X6): the OIS shows "Engine covers are still in place." while
/// L:INI_COVERS_ENABLED is set (MSFS 2024) and "Pitot covers are still in place." while A:COVER ON:2 is set on the ground
/// (its decoded bundle, 2026-10-30; OIS GROUND > COVERS set both live). Each is said as it appears, and once after a load
/// that shows it (a sighted pilot sees the banner whenever the OIS is in view); nothing as it goes. One Ctrl+M row.
/// </summary>
public class A350CoverAlertsTests
{
    [Fact]
    public void Each_banner_is_said_as_it_appears_and_never_as_it_goes()
    {
        var alerts = new A350CoverAlerts();
        Assert.Empty(alerts.Observe(engines: false, pitot: false, onGround: true));
        Assert.Equal(new[] { A350CoverAlerts.Engine, A350CoverAlerts.Pitot }, alerts.Observe(true, true, true));
        Assert.Empty(alerts.Observe(true, true, true));
        Assert.Empty(alerts.Observe(false, false, true));
        Assert.Equal(new[] { A350CoverAlerts.Pitot }, alerts.Observe(false, true, true));
    }

    [Fact]
    public void A_banner_showing_at_the_first_look_is_said_too()
    {
        Assert.Equal(new[] { A350CoverAlerts.Engine }, new A350CoverAlerts().Observe(true, false, true));
    }

    [Fact]
    public void The_pitot_banner_shows_on_the_ground_only()
    {
        var alerts = new A350CoverAlerts();
        Assert.Empty(alerts.Observe(false, true, onGround: false));
        Assert.Equal(new[] { A350CoverAlerts.Pitot }, alerts.Observe(false, true, onGround: true));
    }

    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);

    private IniBuildsA350Definition Def()
    {
        var def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
            ActiveBatches = _ => new[] { 0 },
        };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        _ = def.GetVariables();
        return def;
    }

    private void Batch(IniBuildsA350Definition def, long ms = 1000)
    {
        _now += ms;
        def.ProcessSimVarUpdate(A350CoverAlerts.EnginesKey, _cache.GetValueOrDefault(A350CoverAlerts.EnginesKey), _speech);
        def.OnContinuousBatchDelivered(0);
    }

    [Fact]
    public void The_definition_says_them_after_the_seed_settles_behind_their_row()
    {
        var def = Def();
        var vars = def.GetVariables();
        Assert.Equal("INI_COVERS_ENABLED", vars[A350CoverAlerts.EnginesKey].Name);
        Assert.Equal(("COVER ON:2", "bool"), (vars[A350CoverAlerts.PitotKey].Name, vars[A350CoverAlerts.PitotKey].Units));
        Assert.Equal(A350CoverAlerts.MuteLabel, Assert.Single(MSFSBlindAssist.Services.MonitorRowBuilder.Build(vars),
            r => r.Key == A350CoverAlerts.EnginesKey).Label);
        Assert.True(vars[A350CoverAlerts.PitotKey].ExcludeFromMonitorManager);

        _cache[A350CoverAlerts.EnginesKey] = 1;
        _cache[A350CoverAlerts.PitotKey] = 1;
        _cache["SIM_ON_GROUND"] = 1;
        Batch(def);   // the seed still settling: nothing yet
        Assert.Empty(_speech.All);
        Batch(def, MSFSBlindAssist.Aircraft.MD11.Md11SeedGate.CeilingMs);
        Assert.Equal(new[] { A350CoverAlerts.Engine, A350CoverAlerts.Pitot }, _speech.All);
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        _muted.Add(A350CoverAlerts.EnginesKey);
        var def = Def();
        _cache[A350CoverAlerts.EnginesKey] = 1;
        _cache["SIM_ON_GROUND"] = 1;
        Batch(def);
        Batch(def, MSFSBlindAssist.Aircraft.MD11.Md11SeedGate.CeilingMs);
        Assert.Empty(_speech.All);
    }
}
