using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The universal auto-autopilot engage on the A350, as on the eleven aircraft that have it ([FO-17], [FO-18]): closed loop
/// on the A350's own engaged state (the FCU AP 1 and AP 2 lights' rules, <c>L:INI_ap1_on</c>, <c>L:INI_ap2_on</c>), the
/// press the FCU's own AP 1 row makes, and only on a definite OFF (a push button flips: pressed while engaged, it would
/// disconnect). The stock AUTOPILOT_ON the base falls back to is not the A350's engage.
/// </summary>
public class A350AutopilotEngageTests
{
    private readonly IniBuildsA350Definition _def;
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();

    public A350AutopilotEngageTests()
    {
        _def = new IniBuildsA350Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
        };
        _def.Attach(_sim);
    }

    private void Read(double ap1, double ap2)
    {
        _cache[_def.KeyForInput("INI_ap1_on")] = ap1;
        _cache[_def.KeyForInput("INI_ap2_on")] = ap2;
    }

    [Fact]
    public void Engaged_is_either_autopilot_and_unread_is_not_known()
    {
        Assert.Null(_def.IsAutopilotEngaged(_sim));
        _cache[_def.KeyForInput("INI_ap2_on")] = 1;
        Assert.True(_def.IsAutopilotEngaged(_sim));   // AP 2 alone is an engaged autopilot
        Read(0, 0);
        Assert.False(_def.IsAutopilotEngaged(_sim));
        Read(1, 0);
        Assert.True(_def.IsAutopilotEngaged(_sim));
    }

    [Fact]
    public void A_definite_off_presses_the_FCUs_own_AP_1()
    {
        Read(0, 0);
        _def.EngageAutopilot(_sim);
        Assert.Equal(new[] { "1 (>B:AIRLINER_FCU_AP_1_Set)" }, _sent);
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.0, 1.0)]
    public void An_engaged_autopilot_is_never_pressed(double ap1, double ap2)
    {
        Read(ap1, ap2);
        _def.EngageAutopilot(_sim);
        Assert.Empty(_sent);
    }

    [Fact]
    public void An_unread_state_is_never_pressed()
    {
        _def.EngageAutopilot(_sim);
        Assert.Empty(_sent);
    }

    [Fact]
    public void It_waits_for_the_state_and_retries_as_a_toggle()
    {
        Assert.True(_def.AutopilotEngageWaitsForState);
        Assert.Equal(3000, _def.AutopilotEngageRetryIntervalMs);
    }
}
