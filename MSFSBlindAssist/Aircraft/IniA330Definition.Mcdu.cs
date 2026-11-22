// Plan 03 owns this file: the MCDU.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Aircraft.A330.Mcdu;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The MCDU: its window, and the FMS messages call-out (the "FMS messages" row in Ctrl+M).
/// </summary>
/// <remarks>
/// <para>FMS messages with the window closed. The A330's variable updates flow only while it is the active aircraft, so
/// the first update of either FMS message lamp (<c>INI_FMS1_message_light</c>, <c>INI_FMS2_message_light</c>, consumed
/// here) starts the watch: a UI timer at the background refresh and, while the row is not muted, the captain's MCDU
/// wanted from the display reader at that refresh. Each reading takes the latest complete frame and speaks the message
/// <see cref="A330McduMessages"/> gives through the non-interrupting announce. Muted, the display is let go and nothing
/// is said; unmuted, it is wanted again at the next reading, which starts afresh (silent). While the MCDU window is shown
/// (either side) the watch says nothing, as the window's own scratchpad line says it, but keeps reading, so closing the
/// window does not repeat what the pilot heard. A switch away stops the timer and lets the display go; a flight reload
/// or a reconnect makes the next reading silent again.</para>
/// <para>The lamps. While the frames cannot say the message (hidden list rows, spec M15), the lamps say one is waiting,
/// in the Fenix's MCDU FM lamp sentences, under the same row (<see cref="SpeakFmsLamp"/>).</para>
/// <para>Everything here runs on the UI thread: the variable updates and the timer's ticks.</para>
/// </remarks>
public partial class IniA330Definition
{
    /// <summary>The MCDU window this definition opened (also a tracked window), closed on a switch away.</summary>
    private Forms.IniA330.IniA330CduForm? _mcduWindow;

    // ---- FMS messages ----

    internal const string FmsLampCaptainKey = "A330_FMS1_MESSAGE_LIGHT";
    internal const string FmsLampFirstOfficerKey = "A330_FMS2_MESSAGE_LIGHT";

    /// <summary>The watch's refresh: the display reader's background refresh (plan 02).</summary>
    internal const int McduWatchRefreshMs = 1000;

    private static readonly Dictionary<double, string> FmsLampValues = new() { [0] = "Off", [1] = "On" };

    /// <summary>The lamps' names in what is said: the Fenix's MCDU FM lamp rows' own (its <c>I_CDU1_FM</c> and <c>I_CDU2_FM</c>).</summary>
    private const string FmsLampCaptainName = "MCDU FM Captain", FmsLampFirstOfficerName = "MCDU FM FO";

    private readonly bool?[] _fmsLampLit = new bool?[2];   // captain, first officer; null until the first reading

    private readonly object _mcduWatchOwner = new();
    private bool _mcduWatchOn;
    private System.Windows.Forms.Timer? _mcduWatchTimer;
    private ScreenReaderAnnouncer? _mcduWatchAnnouncer;
    private A330McduMessages? _mcduMessages;   // set while the captain's MCDU is wanted

    /// <summary>Test seam: whether the watch runs its own timer (a test runs <see cref="WatchMcduMessages"/> itself).</summary>
    internal bool RunMcduWatchTimer { get; set; } = true;

    partial void AddMcduVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // Consumed by ProcessMcduUpdate, never spoken by the generic monitor, no row of their own ([VAR-9]).
        Watched(vars, FmsLampCaptainKey, "INI_FMS1_message_light", FmsLampCaptainName, FmsLampValues, silent: true);
        Watched(vars, FmsLampFirstOfficerKey, "INI_FMS2_message_light", FmsLampFirstOfficerName, FmsLampValues, silent: true);
    }

    partial void ProcessMcduUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        int side = varName == FmsLampCaptainKey ? 0 : varName == FmsLampFirstOfficerKey ? 1 : -1;
        if (side < 0) return;
        handled = true;
        StartMcduWatch(announcer);
        SpeakFmsLamp(side, value, announcer);
    }

    /// <summary>An FMS message lamp says a message is waiting while the frames cannot say the message itself, in the
    /// Fenix's MCDU FM lamp sentence, under the FMS messages row: the first reading silent, a change said once. Measured
    /// 2026-11-22: both lamps 0 with no message; until a reading with a message queued, 0 is off and any other value lit.
    /// The captain's lamp speaks only while the captain's MCDU has no complete frame that leaves hidden list rows out
    /// (none, one off its grid, or one that may hold them); the first officer's always, as that MCDU is not watched.</summary>
    private void SpeakFmsLamp(int side, double value, ScreenReaderAnnouncer announcer)
    {
        bool lit = value != 0;
        bool? before = _fmsLampLit[side];
        _fmsLampLit[side] = lit;
        if (before is null || before == lit) return;
        if (IsCalloutMuted(A330CalloutRows.FmsMessageKey)) return;
        if (side == 0 && CaptainFramesSayTheMessage()) return;
        announcer.Announce($"{(side == 0 ? FmsLampCaptainName : FmsLampFirstOfficerName)}: {FmsLampValues[lit ? 1 : 0]}");
    }

    /// <summary>True while the captain's MCDU has a complete frame the watch can read a message from.</summary>
    private bool CaptainFramesSayTheMessage() =>
        Displays.Latest(A330Gauge.McduCaptain) is { } frame && A330McduScreen.From(frame) is { HiddenRowsLeftOut: true };

    /// <summary>The lamps' branch speaks the FMS messages row's call-out and checks that row itself ([VAR-8]).</summary>
    partial void IsMcduMuteWrapExempt(string varKey, ref bool exempt)
    {
        if (varKey == FmsLampCaptainKey || varKey == FmsLampFirstOfficerKey) exempt = true;
    }

    partial void ResetMcduBaselines(bool contextReset)
    {
        if (_mcduMessages is not null) _mcduMessages = new A330McduMessages();
        Array.Clear(_fmsLampLit);
    }

    /// <summary>Starts the watch, once. UI thread.</summary>
    private void StartMcduWatch(ScreenReaderAnnouncer announcer)
    {
        _mcduWatchAnnouncer = announcer;
        if (_mcduWatchOn) return;
        _mcduWatchOn = true;
        if (!IsCalloutMuted(A330CalloutRows.FmsMessageKey)) WantMcduCaptain();
        if (!RunMcduWatchTimer) return;
        _mcduWatchTimer = new System.Windows.Forms.Timer { Interval = McduWatchRefreshMs };
        _mcduWatchTimer.Tick += (_, _) => WatchMcduMessages();
        _mcduWatchTimer.Start();
    }

    /// <summary>One reading of the watch (its timer's work). UI thread.</summary>
    internal void WatchMcduMessages()
    {
        if (!_mcduWatchOn) return;
        if (IsCalloutMuted(A330CalloutRows.FmsMessageKey))
        {
            ReleaseMcduCaptain();
            return;
        }
        if (_mcduMessages is null) WantMcduCaptain();
        if (Displays.Latest(A330Gauge.McduCaptain) is not { } frame || A330McduScreen.From(frame) is not { } screen) return;
        string? say = _mcduMessages!.Next(screen, Displays.McduScratchpad(firstOfficer: false));
        if (say is null || McduWindowShown) return;
        _mcduWatchAnnouncer?.Announce(say);
    }

    /// <summary>True while the MCDU window is shown, on either side: its own scratchpad line speaks the row.</summary>
    private bool McduWindowShown => _mcduWindow is { IsDisposed: false, Visible: true };

    /// <summary>Wants the captain's MCDU, with a fresh message watch whose first reading is silent.</summary>
    private void WantMcduCaptain()
    {
        _mcduMessages = new A330McduMessages();
        Displays.Want(A330Gauge.McduCaptain, _mcduWatchOwner, McduWatchRefreshMs);
    }

    private void ReleaseMcduCaptain()
    {
        if (_mcduMessages is null) return;
        _mcduMessages = null;
        Displays.Release(A330Gauge.McduCaptain, _mcduWatchOwner);
    }

    private void StopMcduWatch()
    {
        _mcduWatchTimer?.Stop();
        _mcduWatchTimer?.Dispose();
        _mcduWatchTimer = null;
        ReleaseMcduCaptain();
        _mcduWatchOn = false;
        _mcduWatchAnnouncer = null;
        Array.Clear(_fmsLampLit);
    }

    // ---- the window ----

    partial void HandleMcduHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ShowFenixMCDU:
                hotkeys.ExitInputHotkeyMode();
                // The captain's side first; the window switches sides itself (Ctrl+Shift+L / Ctrl+Shift+R).
                ShowTrackedWindow(
                    () => _mcduWindow = new Forms.IniA330.IniA330CduForm(Displays, sim, announcer, Forms.IniA330.CduFormProfile.A330(firstOfficer: false)),
                    f => f.ShowForm());
                handled = true; return;
        }
    }

    /// <summary>The aircraft was switched away from: the FMS message watch stops and lets its display go, and the MCDU
    /// window is disposed, which lets its display go (the tracked windows' own teardown then skips it).</summary>
    partial void CloseMcduWindows()
    {
        StopMcduWatch();
        var window = _mcduWindow;
        _mcduWindow = null;
        if (window is null || window.IsDisposed) return;
        window.Dispose();
    }
}
