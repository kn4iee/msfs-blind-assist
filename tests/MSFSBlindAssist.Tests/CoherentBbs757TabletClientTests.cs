using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The parts of the 757 tablet client that need no simulator: when an installed agent counts as
/// deaf and is installed again (an aircraft reload can hand the client a view whose scripts have
/// not run yet: it would freeze CDU 1 on its seed and never get CDU 2), which keys it queues, and
/// what it answers with nothing to read.
/// </summary>
public class CoherentBbs757TabletClientTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false, true, null, true)]        // no listener at all
    [InlineData(false, false, 0, true)]
    [InlineData(true, true, null, false)]        // healthy
    [InlineData(true, false, null, false)]       // not ready, first seen now
    [InlineData(true, false, 4900, false)]       // not ready, still within the wait
    [InlineData(true, false, 5000, true)]        // not ready for 5 s
    [InlineData(true, false, 60000, true)]
    public void A_deaf_agent_is_installed_again(bool listening, bool ready, int? notReadyForMs, bool expected)
    {
        DateTime? since = notReadyForMs is { } ms ? Now.AddMilliseconds(-ms) : null;

        Assert.Equal(expected, CoherentBbs757TabletClient.NeedsReinstall(listening, ready, since, Now));
    }

    [Fact]
    public void Only_cockpit_keys_for_cdu_1_or_2_are_queued()
    {
        using var client = new CoherentBbs757TabletClient(() => "");

        client.PressKey(1, "RTE");
        client.PressKey(2, "KEYCLR");
        Assert.Equal(2, client.PendingKeyCount);

        client.PressKey(3, "RTE");
        client.PressKey(0, "RTE");
        client.PressKey(1, "rte");
        client.PressKey(1, "CLR_Long");
        client.PressKey(1, "RTE');x('");
        client.PressKey(1, null!);
        Assert.Equal(2, client.PendingKeyCount);
    }

    [Fact]
    public async Task With_nothing_to_read_it_is_not_readable_and_reads_no_navigraph_state()
    {
        using var client = new CoherentBbs757TabletClient(() => "");

        Assert.False(client.IsReadable);
        Assert.False(client.HasFrame(1));
        Assert.Equal((false, (string?)null, (string?)null), await client.GetNavigraphAsync());
    }
}
