using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's radios (plan 04), in the hooks plan 01 laid (BBS757Definition.Hooks.cs). So far: output
/// N, which reads BlueBird's own VOR tunings and courses. Until it landed N fell through to MainForm's
/// shared readout of the simulator's NAV radios, which BlueBird does not use (<see cref="UsesStockNavReceivers"/>).
///
/// The tunings are on the NAV RADIO page of the first officer's CDU, so N brings that page up there
/// (never on the captain's CDU, ruling R52; <see cref="Bbs757NavRad"/> reads it) and leaves it there.
/// The courses are BlueBird's own L:vars, read fresh. Nothing is written.
/// </summary>
public partial class BBS757Definition
{
    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// The VOR and ILS course variables N reads fresh (<see cref="Bbs757NavRad.CourseVariables"/>): OnRequest
    /// L:vars on no panel, never announced, so no Ctrl+M row ([VAR-9]).
    /// </summary>
    static partial void AddRadioVariables(Dictionary<string, SimVarDefinition> vars)
    {
        foreach (var (key, lvar, label) in Bbs757NavRad.CourseVariables)
        {
            if (vars.ContainsKey(key)) continue;   // never shadow a map control or a base var
            vars[key] = new SimVarDefinition
            {
                Name = lvar, DisplayName = label,
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            };
        }
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    /// <summary>Output N: the 757's own nav radios. It reads on a task of its own, so the UI thread never waits on the CDU.</summary>
    partial void HandleRadioHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager, ref bool handled)
    {
        if (action != HotkeyAction.ReadNavRadioInfo) return;
        handled = true;
        _ = ReadNavRadiosAsync(simConnect, announcer);   // never throws: it catches and logs
    }

    /// <summary>
    /// Reads the three courses fresh, brings the first officer's CDU to NAV RADIO (or finds it there) and
    /// speaks <see cref="Bbs757NavRad.Readout"/> at once. When the page cannot be had (the CDU busy with the
    /// pilot's keys, the tablet view unreachable) it speaks the courses and says the frequencies are
    /// unavailable; a courses read that fails says everything is unavailable. Never throws: fire and
    /// forget from the hotkey. Silent after a switch away.
    /// </summary>
    /// <param name="pages">The page service; the definition's own when null (tests give one over a fake CDU).</param>
    internal async Task ReadNavRadiosAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, Bbs757CduPages? pages = null)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            // Before the first await, on the caller's (UI) thread: the tablet client is made there.
            pages ??= CduPages;
            var ct = _lifetime.Token;
            var io = ReadoutIo?.Invoke(sim) ?? ReadoutIoFor(sim);
            int? vorL = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.VorLCourseKey, ct));
            int? vorR = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.VorRCourseKey, ct));
            int? ils = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.IlsCourseKey, ct));

            var screen = await pages.ShowPageAsync(Bbs757CduPages.FirstOfficer, Bbs757NavRad.PageKey, Bbs757NavRad.IsNavRadio, ct);
            ct.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(Bbs757NavRad.Readout(screen == null ? null : Bbs757NavRad.Parse(screen), vorL, vorR, ils));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Nav radio readout failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested)
                announcer.AnnounceImmediate(Bbs757NavRad.Readout(null, null, null, null));   // a readout never goes silent
        }
    }
}
