using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The A350's doors, as the A380's (its _doorDefs): each door is the stock INTERACTIVE POINT OPEN:n (0 to 1, open past
/// 0.05; the same points 0 to 12 in all six presets' flight_model.cfg), shown on the Doors and Hatches panel, said "open"
/// or "closed" once per transition by whatever moved it (the OIS GROUND page, GSX), each with its own Ctrl+M row, named as
/// MSFSBA's OIS window names them ("Door L1", "Forward cargo door").
/// </summary>
public class A350DoorsTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;

    public A350DoorsTests()
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
        _def.Attach(new SimConnectManager(IntPtr.Zero));
        _ = _def.GetVariables();   // MainForm registers them before any value arrives
    }

    private static string Key(string name) => A350Doors.All.Single(d => d.Name == name).Key;

    private void Deliver(string key, double value)
    {
        _cache[key] = value;
        Assert.True(_def.ProcessSimVarUpdate(key, value, _speech));   // consumed: never MainForm's generic announce
    }

    /// <summary>The deliveries after a connect settle: a first whole cycle, then the seed's ceiling (nothing seedable
    /// moves here, so only the ceiling releases it), seeding what was never delivered from the cache.</summary>
    private void Settle()
    {
        _now += 1000;
        _def.OnContinuousBatchDelivered(0);
        _now += MSFSBlindAssist.Aircraft.MD11.Md11SeedGate.CeilingMs;
        _def.OnContinuousBatchDelivered(0);
    }

    [Theory]
    [InlineData(0, "Door L1")]
    [InlineData(1, "Door R1")]
    [InlineData(2, "Door L2")]
    [InlineData(3, "Door R2")]
    [InlineData(4, "Door L3")]
    [InlineData(5, "Door R3")]
    [InlineData(9, "Door L4")]
    [InlineData(8, "Door R4")]
    [InlineData(7, "Forward cargo door")]
    [InlineData(6, "Aft cargo door")]
    [InlineData(12, "Bulk cargo door")]
    [InlineData(11, "Fuel truck")]
    public void Each_door_is_its_interactive_point_with_its_own_row_and_display(int point, string name)
    {
        var vars = _def.GetVariables();
        var def = vars[Key(name)];
        Assert.Equal(($"INTERACTIVE POINT OPEN:{point}", "percent over 100", SimVarType.SimVar), (def.Name, def.Units, def.Type));
        Assert.Equal(name, Assert.Single(MSFSBlindAssist.Services.MonitorRowBuilder.Build(vars), r => r.Key == Key(name)).Label);
        var panel = _def.GetPanelDisplayVariables().Single(p => p.Value.Contains(Key(name)));
        Assert.Contains("A350_COCKPIT_DOOR_LOCK", _def.GetPanelControls()[panel.Key]);   // the Doors and Hatches panel
        Assert.True(_def.TryGetDisplayOverride(Key(name), 1, out var open));
        Assert.True(_def.TryGetDisplayOverride(Key(name), 0, out var closed));
        Assert.Equal(point == 11 ? ("Connected", "Disconnected") : ("Open", "Closed"), (open, closed));
    }

    [Fact]
    public void A_door_moved_after_the_settle_is_said_once_per_transition()
    {
        _cache[Key("Door L1")] = 0;
        Settle();
        Deliver(Key("Door L1"), 0.02);   // still closed
        Deliver(Key("Door L1"), 0.4);    // opening: open
        Deliver(Key("Door L1"), 1);
        Deliver(Key("Door L1"), 0);
        Assert.Equal(new[] { "Door L1 open", "Door L1 closed" }, _speech.All);
    }

    // MainForm hands a definition no first snapshot: the seed takes a door's settled value from the cache, so its first
    // change after a connect or a load is heard.
    [Fact]
    public void A_door_never_delivered_is_seeded_so_its_first_change_is_heard()
    {
        _cache[Key("Forward cargo door")] = 0;
        Settle();
        Deliver(Key("Forward cargo door"), 1);
        Assert.Equal(new[] { "Forward cargo door open" }, _speech.All);
    }

    [Fact]
    public void Nothing_is_said_while_the_seed_settles_or_for_a_muted_door()
    {
        Deliver(Key("Door R2"), 1);   // during the settle: the new flight's state
        _muted.Add(Key("Door L1"));
        _cache[Key("Door L1")] = 0;
        Settle();
        Deliver(Key("Door L1"), 1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_fuel_truck_connects_and_disconnects()
    {
        _cache[Key("Fuel truck")] = 0;
        Settle();
        Deliver(Key("Fuel truck"), 1);
        Deliver(Key("Fuel truck"), 0);
        Assert.Equal(new[] { "Fuel truck connected", "Fuel truck disconnected" }, _speech.All);
    }
}
