using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Parity task 8 (2026-10-29): "IRS aligned", said once as the last IR that is on finishes aligning, what a sighted
/// pilot sees as the alignment memo goes (the E/WD call-outs never say a line going); judged on whole samples, the first a
/// silent baseline.</summary>
public class A350IrsAlignmentTests
{
    private static (bool On, bool Aligned)?[] Irs(params (bool On, bool Aligned)[] irs) => irs.Select(i => ((bool, bool)?)i).ToArray();

    [Fact]
    public void The_last_ir_to_align_says_irs_aligned_once()
    {
        var irs = new A350IrsAlignment();
        Assert.Null(irs.Observe(Irs((true, false), (true, false), (true, false))));   // the baseline
        Assert.Null(irs.Observe(Irs((true, true), (true, false), (true, false))));
        Assert.Equal("IRS aligned", irs.Observe(Irs((true, true), (true, true), (true, true))));
        Assert.Null(irs.Observe(Irs((true, true), (true, true), (true, true))));
    }

    [Fact]
    public void An_ir_switched_off_is_not_waited_for()
    {
        var irs = new A350IrsAlignment();
        irs.Observe(Irs((true, false), (true, false), (false, false)));
        Assert.Equal("IRS aligned", irs.Observe(Irs((true, true), (true, true), (false, false))));
    }

    [Fact]
    public void Aligned_at_the_first_sample_is_a_baseline_and_none_on_is_never_aligned()
    {
        var irs = new A350IrsAlignment();
        Assert.Null(irs.Observe(Irs((true, true), (true, true), (true, true))));
        var off = new A350IrsAlignment();
        off.Observe(Irs((false, false), (false, false), (false, false)));
        Assert.Null(off.Observe(Irs((false, true), (false, true), (false, true))));
    }

    [Fact]
    public void An_unread_ir_decides_nothing_and_a_reset_takes_a_new_baseline()
    {
        var irs = new A350IrsAlignment();
        Assert.Null(irs.Observe(new (bool, bool)?[] { (true, false), null, (true, false) }));
        irs.Observe(Irs((true, false), (true, false), (true, false)));
        irs.Reset();
        Assert.Null(irs.Observe(Irs((true, true), (true, true), (true, true))));   // after a load: the new flight's state
    }
}

/// <summary>The definition's IRS alignment call-out: each IR's alignment beside its pushbutton, and "IRS aligned" at a batch
/// end, muted by its own Ctrl+M row, silent until the deliveries after a load have settled.</summary>
public class A350IrsAlignmentCalloutTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;

    public A350IrsAlignmentCalloutTests()
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
    }

    private void Deliver(string key, double value)
    {
        _cache[key] = value;
        _def.ProcessSimVarUpdate(key, value, _speech);
    }

    private void Irs(double aligned)
    {
        for (int n = 1; n <= 3; n++)
        {
            Deliver($"A350_IRS_IR_{n}", 1);
            Deliver($"A350_STAT_IR{n}_ALIGNED", aligned);
        }
    }

    private void BatchAfter(int ms)
    {
        _now += ms;
        _def.OnContinuousBatchDelivered(0);
    }

    /// <summary>The deliveries after the arm settle: a first whole cycle, then the seed's ceiling (no light moves here, so
    /// the gate never sees the aircraft publish and only the ceiling releases it).</summary>
    private void Settle()
    {
        BatchAfter(1000);
        BatchAfter(MSFSBlindAssist.Aircraft.MD11.Md11SeedGate.CeilingMs);
    }

    [Fact]
    public void Each_ir_shows_its_alignment_beside_its_pushbutton()
    {
        var displays = _def.GetPanelDisplayVariables();
        var irs = displays.Single(p => p.Value.Contains("A350_STAT_IR1_ALIGNED")).Value;
        Assert.Contains("A350_STAT_IR2_ALIGNED", irs);
        Assert.Contains("A350_STAT_IR3_ALIGNED", irs);
        Assert.True(_def.TryGetDisplayOverride("A350_STAT_IR1_ALIGNED", 1, out var text));
        Assert.Equal("Aligned", text);
    }

    [Fact]
    public void Irs_aligned_is_said_once_the_last_ir_aligns()
    {
        Irs(0);
        Settle();   // the not-aligned IRs are the baseline
        Irs(1);
        BatchAfter(1000);
        BatchAfter(1000);   // still aligned: said once
        Assert.Equal(new[] { "IRS aligned" }, _speech.All);
        Assert.Equal("IRS alignment", Assert.Single(MSFSBlindAssist.Services.MonitorRowBuilder.Build(_def.GetVariables()),
            r => r.Key == IniBuildsA350Definition.IrsAlignmentKey).Label);
    }

    [Fact]
    public void A_muted_call_out_says_nothing()
    {
        _muted.Add(IniBuildsA350Definition.IrsAlignmentKey);
        Irs(0);
        Settle();
        Irs(1);
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_flight_loaded_aligned_is_its_baseline()
    {
        Irs(0);
        Settle();
        _def.OnSimContextReset();
        Irs(1);   // the new flight's IRs, aligned already
        Settle();
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }

    // Before the deliveries after a load settle, nothing is judged: the IRs aligning then are the new flight's state.
    [Fact]
    public void Nothing_is_judged_before_the_deliveries_settle()
    {
        Irs(0);
        BatchAfter(1000);
        Irs(1);
        BatchAfter(1000);
        Assert.Empty(_speech.All);
    }
}
