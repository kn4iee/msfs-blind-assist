using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// BlueBird's ground power connection (<see cref="Bbs757GroundPower.ConnectedVar"/>) in the definition (plan 07;
/// the EFB hooks of BBS757Definition.Hooks.cs): a Ground Power row on the Electrical panel's status display and
/// a spoken change, the EFB window's own read of external power, and the echo windows that window opens around
/// its own run.
///
/// Speech follows CORE-7. The EFB window's Connect and Disconnect ground power buttons say what came of their
/// taps themselves (<see cref="Bbs757GroundPower.Result"/>), so the row never says that change a second time:
/// a change between <see cref="ArmGroundPowerEcho"/> (right before the taps) and the end of the run's echo
/// (<see cref="SettleGroundPowerEcho"/>) is tracked, never spoken. Any other change is spoken once ("Ground
/// power connected"), through the same <see cref="Bbs757ChangeTracker"/> as the other state rows: BlueBird
/// connecting its own ground power at a flight load is the first reading, a silent baseline, and a flight
/// load or a reconnect makes the next reading a baseline again (<see cref="OnSimContextReset"/>). MainForm's
/// Ctrl+M mute wrap gates the row ([VAR-8]); it is not wrap-exempt.
///
/// The data-definition budget: the row rides the 1 Hz batch (no individual definition of its own) and external
/// power is one individual definition, read on request.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// The map's Overhead panel with the battery, the generators and the EXT PWR button, whose status display
    /// gains the Ground Power row: BlueBird's ground power must be connected (EXT PWR AVAIL) before EXT PWR
    /// puts it on line, so the pilot sees it where EXT PWR is pressed.
    /// </summary>
    internal const string ElectricalPanel = "Electrical";

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// The ground power connection and the window's read of external power, added after the map's (never
    /// shadowing a key).
    /// </summary>
    static partial void AddEfbVariables(Dictionary<string, SimVarDefinition> vars)
    {
        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        // An announced state row like the readouts' (BBS757Definition.Readouts.cs): Continuous +
        // IsAnnounced rides the 1 Hz batch, ProcessEfbUpdate speaks its changes, and it keeps its Ctrl+M row.
        Add(Bbs757SharedKeys.GpuConnected, new SimVarDefinition
        {
            Name = Bbs757GroundPower.ConnectedVar, DisplayName = "Ground Power",
            Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not connected", [1] = "Connected" },
        });

        // The EXT PWR button's ON light, read fresh by the window just before a disconnect and never spoken, so
        // it has no Ctrl+M row ([VAR-9]) and is on no panel or display. OnRequest and under a key of its own:
        // plan 06 registers the same L:var as an announced EXT PWR row (Bbs757SharedKeys.ExtPwrOn), and two
        // Continuous keys on one name would break the batch ([VAR-7]); this one never streams, so the two
        // plans need not wait for each other.
        Add(Bbs757GroundPower.ExtPwrOnKey, new SimVarDefinition
        {
            Name = Bbs757GroundPower.ExtPwrOnVar, DisplayName = "External Power On",
            Type = SimVarType.LVar, Units = "number",
            UpdateFrequency = UpdateFrequency.OnRequest,
            ExcludeFromMonitorManager = true,
        });
    }

    // =================================================================================
    // Panels
    // =================================================================================

    /// <summary>
    /// The Ground Power row joins the Electrical panel's status display, after the rows already there. A
    /// display joins only a panel the map has: a display of a missing panel would be an orphan entry and its
    /// rows would silently vanish (<c>AddReadoutPanels</c>' rule).
    /// </summary>
    partial void AddEfbPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls,
        Dictionary<string, List<string>> displays)
    {
        if (!controls.ContainsKey(ElectricalPanel)) return;
        if (!displays.TryGetValue(ElectricalPanel, out var rows)) displays[ElectricalPanel] = rows = new List<string>();
        rows.Add(Bbs757SharedKeys.GpuConnected);
    }

    // =================================================================================
    // Status display
    // =================================================================================

    /// <summary>The Ground Power row in words; "unavailable" for a reading that is not a number.</summary>
    partial void FormatEfbDisplay(string varKey, double value, ref string? text)
    {
        if (varKey != Bbs757SharedKeys.GpuConnected) return;
        text = !double.IsFinite(value) ? "unavailable" : value > 0.5 ? "Connected" : "Not connected";
    }

    // =================================================================================
    // Announcements
    // =================================================================================

    /// <summary>
    /// The ground power row's call-out: "Ground power connected" or "Ground power disconnected" when it
    /// changes. Claims every delivery of the row (spoken or not), so the generic path never says a raw value;
    /// any other key is left alone. <see cref="SpeakChange"/> decides: the first reading is a silent baseline,
    /// an unchanged one is silent, an unreadable one is neither spoken nor a baseline, and a change inside an
    /// echo window (<see cref="ArmGroundPowerEcho"/>, <see cref="SettleGroundPowerEcho"/>) is tracked and
    /// not spoken.
    /// </summary>
    partial void ProcessEfbUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (varName != Bbs757SharedKeys.GpuConnected) return;
        handled = true;
        SpeakChange(Bbs757SharedKeys.GpuConnected, Bbs757GroundPower.ChangePhrase(value), announcer);
    }

    // =================================================================================
    // The EFB window's run
    // =================================================================================

    /// <summary>
    /// The EFB window is about to tap the GPU tile (connect or disconnect ground power): from now until
    /// <see cref="SettleGroundPowerEcho"/>, and in any case for at most
    /// <see cref="Bbs757GroundPower.WindowRunMaxMs"/>, a change of the Ground Power row is the run's own and is
    /// tracked, never spoken. The window says what came of its taps itself
    /// (<see cref="Bbs757GroundPower.Result"/>), and the screen reader read the button (CORE-7): the row saying
    /// it too would be a second announcement of one change. A run that refuses and taps nothing never calls it.
    /// Called by the window, through MainForm, right before its taps; on the UI thread, as the deliveries are
    /// (the tracker is not synchronized).
    /// </summary>
    internal void ArmGroundPowerEcho()
        => _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, NowMs() + Bbs757GroundPower.WindowRunMaxMs);

    /// <summary>
    /// The EFB window's run is over and it has said the result itself. <paramref name="after"/> is the last
    /// connection the window read. A reading (not null, not NaN or infinity) is the row's state now, so a
    /// delivery of it that has not come yet is no change. Whatever it read, the run's echo now ends
    /// <see cref="Bbs757GroundPower.WindowRunEchoMs"/> from now instead of at the end of
    /// <see cref="ArmGroundPowerEcho"/>'s <see cref="Bbs757GroundPower.WindowRunMaxMs"/>: long enough for a
    /// delivery still in flight (the batch comes once a second), short enough that another source's change
    /// after it is spoken. Called once by the window, through MainForm, when its run ends; on the UI thread.
    /// </summary>
    internal void SettleGroundPowerEcho(double? after)
    {
        if (after is double value && Bbs757GroundPower.ChangePhrase(value) is { } phrase)
            _changes.Rebaseline(Bbs757SharedKeys.GpuConnected, phrase);
        _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, NowMs() + Bbs757GroundPower.WindowRunEchoMs);
    }
}
