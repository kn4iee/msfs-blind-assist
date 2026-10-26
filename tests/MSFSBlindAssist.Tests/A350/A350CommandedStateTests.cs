using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>What MSFSBA just commanded outranks the 1 Hz cache for a short while: a push button FLIPS, so a second
/// pick planned from the stale cache would flip it back.</summary>
public class A350CommandedStateTests
{
    [Fact]
    public void A_fresh_command_outranks_the_cache()
    {
        var state = new A350CommandedState();
        state.Record("L:INI_BATTERY_1_SWITCH", 1, nowMs: 1000);
        Assert.Equal(1, state.Resolve("L:INI_BATTERY_1_SWITCH", cached: 0, nowMs: 1000 + A350CommandedState.HoldMs));
    }

    [Fact]
    public void An_old_command_gives_way_to_the_cache()
    {
        var state = new A350CommandedState();
        state.Record("L:INI_BATTERY_1_SWITCH", 1, nowMs: 1000);
        Assert.Equal(0, state.Resolve("L:INI_BATTERY_1_SWITCH", cached: 0, nowMs: 1001 + A350CommandedState.HoldMs));
    }

    [Fact]
    public void With_no_command_the_cache_answers_even_when_unread()
    {
        Assert.Null(new A350CommandedState().Resolve("L:X", cached: null, nowMs: 0));
    }
}
