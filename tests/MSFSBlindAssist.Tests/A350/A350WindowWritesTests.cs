using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>What windows send (review 3, 2026-10-30, I8, I9).</summary>
public class A350WindowWritesTests
{
    // Two quick Enters in the Surroundings window sent their RMP keys at 150 ms each with no guard: the digits mixed ("VVCCBBII" on the
    // MFD before its Busy), a wrong frequency could load, and with Shift+Enter it swapped to active. The newer tune's CLRs start it clean.
    [Fact]
    public async Task A_newer_tune_stops_the_older_ones_keys()
    {
        var sent = new List<string>();
        var gates = new Queue<TaskCompletionSource>();
        var def = new IniBuildsA350Definition
        {
            CanLand = _ => true,
            Send = (_, rpn) => sent.Add(rpn),
            Delay = _ =>
            {
                var gate = new TaskCompletionSource();
                gates.Enqueue(gate);
                return gate.Task;
            },
        };
        var sim = new SimConnectManager(IntPtr.Zero);
        var older = def.TuneCom1Own(sim, 118_700_000, false)!;
        Assert.Single(sent);                       // its first key; the second waits for the gap
        var newer = def.TuneCom1Own(sim, 121_500_000, false)!;
        int newerStart = sent.Count - 1;
        while (gates.Count > 0 && !(older.IsCompleted && newer.IsCompleted))
            gates.Dequeue().SetResult();
        await Task.WhenAll(older, newer).WaitAsync(TimeSpan.FromSeconds(5));
        var expected = MSFSBlindAssist.Aircraft.A350.A350Rmp.ComTuningKeys(121_500_000, false)
            .Select(k => MSFSBlindAssist.Aircraft.A350.A350Rmp.Press(1, k)).ToList();
        Assert.Equal(expected, sent.Skip(newerStart).ToList());
        Assert.Null(await older);
    }

    [Fact]
    public void A_row_a_window_just_set_is_its_own_echo_for_a_moment()
    {
        long now = 100_000;
        var def = new IniBuildsA350Definition { Clock = () => now };
        Assert.False(def.WindowEchoActive("A350_EFIS_RANGE_LEFT"));
        def.ArmWindowEcho("A350_EFIS_RANGE_LEFT");
        Assert.True(def.WindowEchoActive("A350_EFIS_RANGE_LEFT"));
        Assert.False(def.WindowEchoActive("A350_EFIS_MODE_LEFT"));
        now += IniBuildsA350Definition.WindowEchoMs + 1;
        Assert.False(def.WindowEchoActive("A350_EFIS_RANGE_LEFT"));
    }
}
