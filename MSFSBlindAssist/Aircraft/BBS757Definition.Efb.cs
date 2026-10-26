using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// BlueBird's ground power connection (<see cref="Bbs757GroundPower.ConnectedVar"/>) in the definition (plan 07;
/// the EFB hooks of BBS757Definition.Hooks.cs): a Ground Power row on the Electrical panel's status display and
/// a spoken change, the EFB window's own read of external power, and the echo that window opens around its own
/// run.
///
/// Speech follows CORE-7. The EFB window's Connect and Disconnect ground power buttons say what came of their
/// taps themselves (<see cref="Bbs757GroundPower.Result"/>), so the row never says that change a second time:
/// a change between <see cref="ArmGroundPowerEcho"/> (right before the taps) and <see cref="SettleGroundPowerEcho"/>
/// (the run's end) is tracked, never spoken. The settle ends the echo at once, as a press's read-back does, so
/// any later change is spoken, even one after a run whose taps did not take. Any other change is spoken once
/// ("Ground power connected"), through the same <see cref="Bbs757ChangeTracker"/> as the other state rows:
/// BlueBird connecting its own ground power at a flight load is the first reading, a silent baseline, and a
/// flight load or a reconnect makes the next reading a baseline again (<see cref="OnSimContextReset"/>).
/// MainForm's Ctrl+M mute wrap gates the row ([VAR-8]); it is not wrap-exempt.
///
/// The data-definition budget: the row rides the 1 Hz batch (no individual definition of its own) and external
/// power is one individual definition, read on request.
/// </summary>
public partial class BBS757Definition
{
    /// <summary>
    /// The map's panel whose status display shows the ground power row: the Overhead's Electrical panel, where
    /// EXT PWR is pressed. BlueBird's ground power must be connected (EXT PWR AVAIL) before EXT PWR puts it on
    /// line, so the pilot sees it there. Named for what it holds, not for the panel: plan 06's own constant for
    /// the same panel (BBS757Definition.Readouts.cs) cannot then collide with it.
    /// </summary>
    internal const string GroundPowerPanel = "Electrical";

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
    /// The Ground Power row joins the status display of <see cref="GroundPowerPanel"/>, after the rows already
    /// there. A display joins only a panel the map has: a display of a missing panel would be an orphan entry
    /// and its rows would silently vanish (<c>AddReadoutPanels</c>' rule).
    /// </summary>
    partial void AddEfbPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls,
        Dictionary<string, List<string>> displays)
    {
        if (!controls.ContainsKey(GroundPowerPanel)) return;
        if (!displays.TryGetValue(GroundPowerPanel, out var rows)) displays[GroundPowerPanel] = rows = new List<string>();
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
    /// an unchanged one is silent, an unreadable one is neither spoken nor a baseline, and a change inside the
    /// window's run (<see cref="ArmGroundPowerEcho"/> to <see cref="SettleGroundPowerEcho"/>) is tracked and
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
    /// <see cref="Bbs757GroundPower.WindowRunMaxMs"/> (a run that never settles), a change of the Ground Power
    /// row is the run's own and is tracked, never spoken. The window says what came of its taps itself
    /// (<see cref="Bbs757GroundPower.Result"/>), and the screen reader read the button (CORE-7): the row saying
    /// it too would be a second announcement of one change. A run that refuses and taps nothing never calls it.
    /// Called by the window, through MainForm, right before its taps; on the UI thread, as the deliveries are
    /// (the tracker is not synchronized).
    /// </summary>
    internal void ArmGroundPowerEcho()
        => _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, NowMs() + Bbs757GroundPower.WindowRunMaxMs);

    /// <summary>
    /// The EFB window's run is over and it has said the result itself. <paramref name="after"/> is the last
    /// connection the window read. A reading (not null, not NaN or infinity) is the row's state now, so the
    /// delivery of it that has not come yet is no change; an unreadable one leaves the baseline as the run
    /// tracked it. Either way the echo ends here, as a press's read-back ends its own
    /// (<c>SettleOwnWriteEcho</c>), with no tail: a delivery of the state just settled is no change, and a
    /// delivery of any other state is the aircraft's and is spoken (BlueBird connecting the ground power
    /// seconds after the window said it had not; the state after a read-back that could not be read). Called
    /// once by the window, through MainForm, when its run ends; on the UI thread.
    /// </summary>
    internal void SettleGroundPowerEcho(double? after)
    {
        if (after is double value && Bbs757GroundPower.ChangePhrase(value) is { } phrase)
            _changes.Rebaseline(Bbs757SharedKeys.GpuConnected, phrase);
        _changes.ArmEcho(Bbs757SharedKeys.GpuConnected, 0);
    }
}
