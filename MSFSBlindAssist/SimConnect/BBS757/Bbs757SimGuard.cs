using System.Diagnostics;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>Whether a Microsoft Flight Simulator process runs, as <see cref="Bbs757SimGuard.Check()"/> finds it.</summary>
public enum Bbs757SimState
{
    /// <summary>No simulator process runs, so the 757 is not loaded and BlueBird's settings files may be written.</summary>
    NotRunning,

    /// <summary>A simulator process runs: the 757 may be loaded, and BlueBird may write its own copy over its files.</summary>
    Running,

    /// <summary>The process list could not be read, so nothing is known. Treated as running: nothing is written.</summary>
    Unknown,
}

/// <summary>
/// When the app may write BlueBird's settings files: only while no simulator process runs. BlueBird reads those files
/// once, when the 757 loads, and while it is loaded it writes its own copy back over them, so a value written then is
/// ignored and overwritten (docs/b757/evidence/efb-settings.md, section 1). An aircraft can also stay loaded behind the
/// simulator's main menu, so the screen cannot say whether the 757 is loaded; the one safe moment is with no simulator
/// process at all, MSFS 2024's (<c>FlightSimulator2024</c>) or MSFS 2020's (<c>FlightSimulator</c>). A check that fails
/// is <see cref="Bbs757SimState.Unknown"/>, never "not running": the settings stay read-only. The guard asks Windows
/// itself and not <c>Utils/SimulatorDetector</c>, which answers "Unknown" both when no simulator runs and when its check
/// fails, the two answers this guard must tell apart. A caller asks again immediately before each write, as the
/// simulator may have started since it last asked. Pure but for <see cref="Check()"/>, which lists the machine's processes.
/// </summary>
public static class Bbs757SimGuard
{
    /// <summary>
    /// The process names, as Windows reports them (no <c>.exe</c>), of the two simulators the app supports: MSFS 2024
    /// first, then MSFS 2020. A process of either one means a simulator runs.
    /// </summary>
    public static readonly IReadOnlyList<string> ProcessNames = Array.AsReadOnly(new[] { "FlightSimulator2024", "FlightSimulator" });

    /// <summary>What is said when a change is tried while the settings are read-only.</summary>
    public const string LockedText = "Close the simulator to change BlueBird's settings.";

    /// <summary>What is said, once, when a simulator starts while the Settings tab is open.</summary>
    public const string SimStartedText = "Simulator started: BlueBird's settings are read-only.";

    /// <summary>What is said, once, when the simulator closes while the Settings tab is open.</summary>
    public const string SimClosedText = "Simulator closed: BlueBird's settings can be changed.";

    // What the Settings tab's status box shows, one text for each case (StatusText picks one).
    private const string ClosedText =
        "The simulator is closed. Changes are saved to BlueBird's settings at once and take effect when the 757 next loads.";
    private const string RunningText =
        "The simulator is running, so BlueBird's settings are read-only. Close the simulator to change them.";
    private const string UnknownText =
        "The app can't tell whether the simulator is running, so BlueBird's settings are read-only.";
    private const string NoSettingsText =
        "BlueBird hasn't created its settings yet. Load the 757 once, then close the simulator to change them.";

    /// <summary>
    /// Whether a simulator runs now: <see cref="Process.GetProcessesByName(string)"/> for each of
    /// <see cref="ProcessNames"/>, the processes it returns disposed. <see cref="Bbs757SimState.Running"/> when any
    /// process is found, <see cref="Bbs757SimState.Unknown"/> when the list could not be read, else
    /// <see cref="Bbs757SimState.NotRunning"/>. Never throws.
    /// </summary>
    public static Bbs757SimState Check() => Check(CountProcesses);

    /// <summary>
    /// <see cref="Check()"/> with <paramref name="countProcesses"/>, how many processes bear a name, doing the asking:
    /// internal, so the tests answer for the machine without a simulator. A name with a count above 0 means
    /// <see cref="Bbs757SimState.Running"/>, whatever became of the other name; with none seen, a count that throws (any
    /// exception) means <see cref="Bbs757SimState.Unknown"/>, else <see cref="Bbs757SimState.NotRunning"/>. The answer
    /// does not depend on the order the names are asked in. Never throws for what <paramref name="countProcesses"/> throws.
    /// </summary>
    internal static Bbs757SimState Check(Func<string, int> countProcesses)
    {
        bool failed = false;
        foreach (string name in ProcessNames)
        {
            try
            {
                if (countProcesses(name) > 0) return Bbs757SimState.Running;
            }
            catch (Exception)
            {
                // Listing processes fails in several ways (Win32Exception, InvalidOperationException, ...): all of them
                // leave it unknown whether a simulator runs, and the other name may still show one.
                failed = true;
            }
        }
        return failed ? Bbs757SimState.Unknown : Bbs757SimState.NotRunning;
    }

    /// <summary>
    /// Whether a change may be saved: the simulator is known not to run (<see cref="Bbs757SimState.NotRunning"/>: a
    /// check that failed is not enough) and BlueBird's settings file exists (the app never creates one).
    /// </summary>
    public static bool SettingsWritable(Bbs757SimState state, bool settingsFileExists) =>
        state == Bbs757SimState.NotRunning && settingsFileExists;

    /// <summary>
    /// What the Settings tab's status box says. A missing settings file comes first, whatever the simulator does: there
    /// is nothing to change until BlueBird has made one, and its text sends the pilot to load the 757 once. Then a
    /// simulator that runs, then a check that failed (a state that is none of the three reads as failed), then, with the
    /// simulator closed, that changes are saved at once. That last text ends with the <paramref name="simulatorName"/>
    /// folder sentence when it is not null: the caller names the simulator only when more than one work folder exists (a
    /// pilot with both simulators), so the pilot knows which folder the changes go to. No other text names one: with
    /// those, nothing is saved.
    /// </summary>
    public static string StatusText(Bbs757SimState state, bool settingsFileExists, string? simulatorName)
    {
        if (!settingsFileExists) return NoSettingsText;
        return state switch
        {
            Bbs757SimState.Running => RunningText,
            Bbs757SimState.NotRunning when simulatorName is not null => ClosedText + $" Changes go to the {simulatorName} folder.",
            Bbs757SimState.NotRunning => ClosedText,
            _ => UnknownText,
        };
    }

    // How many processes bear the name. Each Process object holds a handle, so every one that is returned is disposed.
    private static int CountProcesses(string name)
    {
        Process[] found = Process.GetProcessesByName(name);
        try
        {
            return found.Length;
        }
        finally
        {
            foreach (Process process in found) process.Dispose();
        }
    }
}
