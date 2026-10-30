using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The VHF and squawk call-outs, as the A380's: the A350 keeps the stock COM 1 and 2 frequencies in step with its RMPs
/// (live 2026-10-30: VHF1 122.800 / 123.450 and VHF2 121.500 / 124.850 on the RMP, the same in COM ACTIVE/STANDBY
/// FREQUENCY:1 and :2; VHF3 is the datalink radio, DATA, and the stock COM 3 is not its mirror) and TRANSPONDER CODE:1
/// with its squawk (2000), so a change made anywhere is said: "VHF 1 active 121.900", "Squawk 1234", each with its row.
/// </summary>
public class A350RadioCalloutsTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;

    public A350RadioCalloutsTests()
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
        _ = _def.GetVariables();
    }

    private void Deliver(string key, double value)
    {
        _cache[key] = value;
        Assert.True(_def.ProcessSimVarUpdate(key, value, _speech));
    }

    private void Settle()
    {
        _now += 1000;
        _def.OnContinuousBatchDelivered(0);
        _now += MSFSBlindAssist.Aircraft.MD11.Md11SeedGate.CeilingMs;
        _def.OnContinuousBatchDelivered(0);
    }

    [Fact]
    public void The_radios_stream_from_the_stock_com_and_transponder_each_with_its_row()
    {
        var vars = _def.GetVariables();
        Assert.Equal(("COM ACTIVE FREQUENCY:1", "MHz"), (vars[A350RadioCallouts.ActiveKey(1)].Name, vars[A350RadioCallouts.ActiveKey(1)].Units));
        Assert.Equal(("COM STANDBY FREQUENCY:2", "MHz"), (vars[A350RadioCallouts.StandbyKey(2)].Name, vars[A350RadioCallouts.StandbyKey(2)].Units));
        Assert.Equal(("TRANSPONDER CODE:1", "BCO16"), (vars[A350RadioCallouts.SquawkKey].Name, vars[A350RadioCallouts.SquawkKey].Units));
        Assert.False(vars.Values.Any(v => v.Name.StartsWith("COM ACTIVE FREQUENCY:3")));   // VHF3 is the datalink radio
        var rows = MSFSBlindAssist.Services.MonitorRowBuilder.Build(vars);
        Assert.Equal("VHF 1 active frequency", Assert.Single(rows, r => r.Key == A350RadioCallouts.ActiveKey(1)).Label);
        Assert.Equal("Squawk code", Assert.Single(rows, r => r.Key == A350RadioCallouts.SquawkKey).Label);
    }

    [Fact]
    public void A_frequency_changed_anywhere_is_said_once_in_any_culture()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            _cache[A350RadioCallouts.ActiveKey(1)] = 122.8;
            _cache[A350RadioCallouts.StandbyKey(1)] = 123.45;
            Settle();
            Deliver(A350RadioCallouts.StandbyKey(1), 121.9);
            Deliver(A350RadioCallouts.ActiveKey(1), 121.9);   // swapped in
            Deliver(A350RadioCallouts.StandbyKey(1), 122.8);
            Deliver(A350RadioCallouts.ActiveKey(1), 121.9);   // no change
            Assert.Equal(new[] { "VHF 1 standby 121.900", "VHF 1 active 121.900", "VHF 1 standby 122.800" }, _speech.All);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Nothing_outside_the_vhf_band_while_settling_or_muted()
    {
        Deliver(A350RadioCallouts.ActiveKey(2), 121.5);   // during the settle
        _cache[A350RadioCallouts.ActiveKey(1)] = 122.8;
        _muted.Add(A350RadioCallouts.StandbyKey(2));
        _cache[A350RadioCallouts.StandbyKey(2)] = 124.85;
        Settle();
        Deliver(A350RadioCallouts.ActiveKey(1), 0);       // unpowered
        Deliver(A350RadioCallouts.StandbyKey(2), 118.1);  // muted
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_squawk_set_elsewhere_is_said_and_the_rmp_windows_own_once_not()
    {
        _cache[A350RadioCallouts.SquawkKey] = 0x2000;
        Settle();
        Deliver(A350RadioCallouts.SquawkKey, 0x4321);
        _def.NoteWindowSquawk(0x1200);   // the RMP window set it and said it
        Deliver(A350RadioCallouts.SquawkKey, 0x1200);
        Deliver(A350RadioCallouts.SquawkKey, 0x7000);
        Assert.Equal(new[] { "Squawk 4321", "Squawk 7000" }, _speech.All);
    }
}
