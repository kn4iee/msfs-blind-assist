using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The A350's module stopping (X7): its heartbeat L:var stands still while both the sim's clock and the wall clock run on
/// for <see cref="A350ModuleWatchdog.StoppedAfter"/>. The sim's clock stands still while paused (never a stop); the wall
/// clock catches a clock jump or a high sim rate the sim's clock alone would take for one.
/// </summary>
public class A350ModuleWatchdogTests
{
    private readonly A350ModuleWatchdog _dog = new();
    private double _sim = 50_000;   // the sim's clock, seconds
    private long _wall = 1_000_000; // the wall clock, ms

    private string? Look(double beat, double simSeconds = 1, long wallMs = 1000)
    {
        _sim += simSeconds;
        _wall += wallMs;
        return _dog.Observe(beat, _sim, _wall);
    }

    [Fact]
    public void A_beating_module_says_nothing()
    {
        for (int beat = 1; beat < 60; beat++)
            Assert.Null(Look(beat));
    }

    [Fact]
    public void A_heartbeat_stopped_while_both_clocks_run_is_said_once()
    {
        Look(7);
        for (int s = 1; s < 10; s++)
            Assert.Null(Look(7));
        Assert.Equal(A350ModuleWatchdog.Stopped, Look(7));
        for (int s = 0; s < 30; s++)
            Assert.Null(Look(7));
    }

    [Fact]
    public void A_pause_is_never_a_stop()
    {
        Look(7);
        for (int s = 0; s < 120; s++)
            Assert.Null(Look(7, simSeconds: 0));
    }

    [Fact]
    public void A_clock_jump_or_a_high_sim_rate_alone_is_never_a_stop()
    {
        Look(7);
        Assert.Null(Look(7, simSeconds: 3600));   // the time of day changed
        Assert.Null(Look(8, simSeconds: 16));     // 16x: the next beat came a second later
        Assert.Null(Look(8, simSeconds: 16));
        Assert.Null(Look(9, simSeconds: 16));
    }

    [Fact]
    public void A_module_beating_again_is_said()
    {
        Look(7);
        for (int s = 0; s < 10; s++) Look(7);
        Assert.Equal(A350ModuleWatchdog.Responding, Look(1));
        Assert.Null(Look(2));
    }

    // A flight load: the sim's clock may go back, and the new flight's module beats from its own start.
    [Fact]
    public void The_sim_clock_going_back_or_a_reset_starts_again()
    {
        Look(7);
        for (int s = 0; s < 9; s++) Look(7);
        Assert.Null(Look(7, simSeconds: -20_000));
        for (int s = 0; s < 9; s++) Assert.Null(Look(7));

        _dog.Reset();
        for (int s = 0; s < 10; s++) Assert.Null(Look(7));
        Assert.Equal(A350ModuleWatchdog.Stopped, Look(7));
    }

    [Fact]
    public void Nothing_is_judged_without_both_values()
    {
        Assert.Null(_dog.Observe(null, 1, 1));
        Assert.Null(_dog.Observe(1, null, 1));
    }
}

/// <summary>The watchdog in the definition: the heartbeat and the sim's clock streamed and consumed silently, judged at each
/// batch end, said behind its own Ctrl+M row.</summary>
public class A350ModuleWatchdogCalloutTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;

    public A350ModuleWatchdogCalloutTests()
    {
        _def = new IniBuildsA350Definition
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
        _def.Attach(new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero));
        _ = _def.GetVariables();   // MainForm registers them before any value arrives
    }

    private void Deliver(string key, double value)
    {
        _cache[key] = value;
        Assert.True(_def.ProcessSimVarUpdate(key, value, _speech));   // consumed: never MainForm's generic announce
    }

    /// <summary>A second of the sim: its clock delivered, the heartbeat when it moved, the batch end.</summary>
    private void Second(double? beat)
    {
        _now += 1000;
        Deliver(A350ModuleWatchdog.SimTimeKey, (_cache.TryGetValue(A350ModuleWatchdog.SimTimeKey, out var t) ? t : 60_000) + 1);
        if (beat is double b) Deliver(A350ModuleWatchdog.HeartbeatKey, b);
        _def.OnContinuousBatchDelivered(0);
    }

    [Fact]
    public void Both_inputs_stream_from_the_aircraft_and_the_sim()
    {
        var vars = _def.GetVariables();
        Assert.Equal("INI_UPDATELOOP_HEARTBEAT", vars[A350ModuleWatchdog.HeartbeatKey].Name);
        Assert.Equal(("ABSOLUTE TIME", "seconds"), (vars[A350ModuleWatchdog.SimTimeKey].Name, vars[A350ModuleWatchdog.SimTimeKey].Units));
        Assert.True(vars[A350ModuleWatchdog.SimTimeKey].ExcludeFromMonitorManager);
        Assert.Equal(A350ModuleWatchdog.MuteLabel, Assert.Single(MSFSBlindAssist.Services.MonitorRowBuilder.Build(vars),
            r => r.Key == A350ModuleWatchdog.HeartbeatKey).Label);
    }

    [Fact]
    public void A_stopped_module_is_said_once_and_its_return_too()
    {
        Second(1);
        for (int s = 0; s < 12; s++) Second(null);
        Assert.Equal(new[] { A350ModuleWatchdog.Stopped }, _speech.All);

        Second(2);
        Assert.Equal(new[] { A350ModuleWatchdog.Stopped, A350ModuleWatchdog.Responding }, _speech.All);
    }

    [Fact]
    public void A_muted_row_says_nothing()
    {
        _muted.Add(A350ModuleWatchdog.HeartbeatKey);
        Second(1);
        for (int s = 0; s < 12; s++) Second(null);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_flight_load_starts_the_watch_again()
    {
        Second(1);
        for (int s = 0; s < 8; s++) Second(null);
        _def.OnSimContextReset();
        for (int s = 0; s < 8; s++) Second(null);
        Assert.Empty(_speech.All);
    }
}
