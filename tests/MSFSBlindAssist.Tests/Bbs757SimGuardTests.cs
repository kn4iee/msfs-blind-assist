using System.ComponentModel;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the app may write BlueBird's settings (<see cref="Bbs757SimGuard"/>): only with no simulator process running,
/// a check that fails counting as unknown and as read-only as a running simulator; and the texts the Settings tab shows
/// and speaks for each state, pinned to the spec's words. The process check is the internal overload, fed a counter of
/// the test's own, so nothing here needs a simulator or is disturbed by one; the one test of the real check asks only
/// that it can read the process list.
/// </summary>
public class Bbs757SimGuardTests
{
    private const string Sim2024 = "FlightSimulator2024";
    private const string Sim2020 = "FlightSimulator";

    // The Settings tab's status texts, as the spec words them (global constraints, "Exact texts").
    private const string Closed =
        "The simulator is closed. Changes are saved to BlueBird's settings at once and take effect when the 757 next loads.";
    private const string Running =
        "The simulator is running, so BlueBird's settings are read-only. Close the simulator to change them.";
    private const string Unknown =
        "The app can't tell whether the simulator is running, so BlueBird's settings are read-only.";
    private const string NoSettings =
        "BlueBird hasn't created its settings yet. Load the 757 once, then close the simulator to change them.";

    // ---- the check --------------------------------------------------------------------------------

    [Fact]
    public void The_check_looks_for_both_simulators_processes()
    {
        Assert.Equal(new[] { Sim2024, Sim2020 }, Bbs757SimGuard.ProcessNames);
    }

    [Theory]
    [InlineData(Sim2024, 1)]
    [InlineData(Sim2020, 1)]
    [InlineData(Sim2020, 2)]   // more than one process of a name: still running
    public void Any_simulator_process_means_running(string runningName, int count)
    {
        var state = Bbs757SimGuard.Check(name => name == runningName ? count : 0);

        Assert.Equal(Bbs757SimState.Running, state);
    }

    [Fact]
    public void No_process_means_not_running()
    {
        var asked = new List<string>();

        var state = Bbs757SimGuard.Check(name =>
        {
            asked.Add(name);
            return 0;
        });

        Assert.Equal(Bbs757SimState.NotRunning, state);
        // Every name was looked for: a pilot may fly either simulator.
        Assert.Equal(Bbs757SimGuard.ProcessNames, asked);
    }

    [Theory]   // whatever the process list threw: the app can't tell
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(NotSupportedException))]
    public void A_failed_check_is_unknown(Type failure)
    {
        var state = Bbs757SimGuard.Check(_ => throw (Exception)Activator.CreateInstance(failure)!);

        Assert.Equal(Bbs757SimState.Unknown, state);
    }

    [Theory]   // one of the two names fails to be checked, whichever it is
    // A simulator that was seen is running, whatever became of the other check.
    [InlineData(Sim2024, 1, Bbs757SimState.Running)]
    [InlineData(Sim2020, 1, Bbs757SimState.Running)]
    // With none seen and one check failed, the app can't tell.
    [InlineData(Sim2024, 0, Bbs757SimState.Unknown)]
    [InlineData(Sim2020, 0, Bbs757SimState.Unknown)]
    public void A_simulator_seen_outranks_a_failed_check(string failingName, int otherCount, Bbs757SimState expected)
    {
        var state = Bbs757SimGuard.Check(name => name == failingName ? throw new InvalidOperationException() : otherCount);

        Assert.Equal(expected, state);
    }

    [Fact]
    public void The_real_check_reads_the_process_list()
    {
        // Whether a simulator runs on this machine is not for the test to say. But a check that fell into its catch
        // would keep the Settings tab read-only for good, and the tests above, which feed the guard a counter of their
        // own, cannot see that.
        Assert.NotEqual(Bbs757SimState.Unknown, Bbs757SimGuard.Check());
    }

    // ---- when writing is allowed --------------------------------------------------------------------

    [Theory]
    [InlineData(Bbs757SimState.NotRunning, true, true)]
    [InlineData(Bbs757SimState.NotRunning, false, false)]   // BlueBird has made no settings file: nothing to change
    [InlineData(Bbs757SimState.Running, true, false)]
    [InlineData(Bbs757SimState.Running, false, false)]
    [InlineData(Bbs757SimState.Unknown, true, false)]       // a failed check counts as running
    [InlineData(Bbs757SimState.Unknown, false, false)]
    public void Only_not_running_with_a_file_is_writable(Bbs757SimState state, bool settingsFileExists, bool writable)
    {
        Assert.Equal(writable, Bbs757SimGuard.SettingsWritable(state, settingsFileExists));
    }

    // ---- what the tab says ------------------------------------------------------------------------

    [Theory]
    // No settings file: what the pilot needs to know, whatever the simulator is doing, a folder named or not.
    [InlineData(Bbs757SimState.NotRunning, false, null, NoSettings)]
    [InlineData(Bbs757SimState.NotRunning, false, "MSFS 2024", NoSettings)]
    [InlineData(Bbs757SimState.Running, false, null, NoSettings)]
    [InlineData(Bbs757SimState.Running, false, "MSFS 2024", NoSettings)]
    [InlineData(Bbs757SimState.Unknown, false, null, NoSettings)]
    [InlineData(Bbs757SimState.Unknown, false, "MSFS 2024", NoSettings)]
    // A simulator running, or a check that failed: read-only, and no folder is named, as nothing is being saved.
    [InlineData(Bbs757SimState.Running, true, null, Running)]
    [InlineData(Bbs757SimState.Running, true, "MSFS 2024", Running)]
    [InlineData(Bbs757SimState.Unknown, true, null, Unknown)]
    [InlineData(Bbs757SimState.Unknown, true, "MSFS 2020", Unknown)]
    // The simulator closed: changes are saved at once, and the folder is named only when the caller names one.
    [InlineData(Bbs757SimState.NotRunning, true, null, Closed)]
    [InlineData(Bbs757SimState.NotRunning, true, "MSFS 2024", Closed + " Changes go to the MSFS 2024 folder.")]
    [InlineData(Bbs757SimState.NotRunning, true, "MSFS 2020", Closed + " Changes go to the MSFS 2020 folder.")]
    public void The_status_texts_are_the_spec_s(Bbs757SimState state, bool settingsFileExists, string? simulatorName, string expected)
    {
        Assert.Equal(expected, Bbs757SimGuard.StatusText(state, settingsFileExists, simulatorName));
    }

    [Fact]
    public void The_spoken_texts_are_the_spec_s()
    {
        Assert.Equal("Close the simulator to change BlueBird's settings.", Bbs757SimGuard.LockedText);
        Assert.Equal("Simulator started: BlueBird's settings are read-only.", Bbs757SimGuard.SimStartedText);
        Assert.Equal("Simulator closed: BlueBird's settings can be changed.", Bbs757SimGuard.SimClosedText);
    }
}
