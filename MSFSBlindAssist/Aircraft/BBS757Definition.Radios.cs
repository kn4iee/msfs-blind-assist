using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's radios (plan 04), in the hooks plan 01 laid (BBS757Definition.Hooks.cs). So far: output
/// N, which reads BlueBird's own VOR tunings and courses, the Ctrl+N window for the ILS, and the COM radios
/// panel.
///
/// Output N. Until it landed N fell through to MainForm's shared readout of the simulator's NAV radios,
/// which BlueBird does not use (<see cref="UsesStockNavReceivers"/>). The tunings are on the NAV RADIO page
/// of the first officer's CDU, so N brings that page up there (never on the captain's CDU, ruling R52;
/// <see cref="Bbs757NavRad"/> reads it) and leaves it there. The courses are BlueBird's own L:vars, read
/// fresh. Nothing is written.
///
/// The Ctrl+N window (<see cref="ShowNavRadiosWindow"/>). One window per definition, shown again on a later
/// Ctrl+N and closed by <see cref="CloseRadioWindows"/>. It opens on N's readout and the FMC's runway ILS
/// from APPROACH REF, read on the first officer's CDU in the background (<see cref="ReadRunwayIlsAsync"/>),
/// which is left on NAV RADIO; the status is spoken once, when it is filled. Its one field, the ILS course,
/// is written as a calculator write and read back (<see cref="SetIlsCourseAsync"/>). The VORs are not in it:
/// a VOR is tuned, and its course set, on the CDU.
///
/// The COM radios. BlueBird's VHF L, C and R are the simulator's own COM 1, 2 and 3 (measured), so the six
/// active and standby frequencies are the stock <c>COM ACTIVE/STANDBY FREQUENCY:n</c> on the 1 Hz batch:
/// shown in the "COM radios" panel's status display with three decimals, and spoken when one changes, as
/// on the other Boeings ("COM1 active 124.850", ruling R53). A standby typed in goes out as the stock
/// <c>K:COMn_STBY_RADIO_SET_HZ</c> (ruling R54) and is confirmed by what the radio reads back. A transfer tries
/// the stock <c>COMn_RADIO_SWAP</c> first and, when BlueBird ignores it, presses the button of the tuning
/// panel whose lights select the radio (<see cref="TransferAsync"/>).
/// </summary>
public partial class BBS757Definition
{
    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// The VOR and ILS course variables N reads fresh (<see cref="Bbs757NavRad.CourseVariables"/>): OnRequest
    /// L:vars on no panel, never announced, so no Ctrl+M row ([VAR-9]). Then the COM radios' fifteen: for each
    /// radio the active and standby rows (the stock COM variables, in MHz, on the 1 Hz batch so they ride
    /// no data definition of their own, with a Ctrl+M row each), the standby field and the transfer button
    /// (events, no data definition), and the three tuning panels' lights (OnRequest, read for the
    /// transfer's fallback and never spoken, so no Ctrl+M row).
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

        void Add(string key, SimVarDefinition def)
        {
            if (!vars.ContainsKey(key)) vars[key] = def;
        }

        for (int com = 1; com <= 3; com++)
        {
            Add(Bbs757Radios.ActiveKey(com), StreamedRow($"COM ACTIVE FREQUENCY:{com}", $"COM{com} Active", SimVarType.SimVar, "MHz"));
            Add(Bbs757Radios.StandbyKey(com), StreamedRow($"COM STANDBY FREQUENCY:{com}", $"COM{com} Standby", SimVarType.SimVar, "MHz"));

            // The squawk field's pattern: a key containing "_SET" is what MainForm renders as a box Enter
            // applies. The definition takes its text first (HandleRadioTypedText), so the event named
            // here is never sent from the registration; it only keeps the field an event.
            Add(Bbs757Radios.SetKey(com), new SimVarDefinition
            {
                Name = Bbs757Radios.StandbySetEvent(com), DisplayName = $"COM{com} Standby Frequency",
                Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
            });
            Add(Bbs757Radios.TransferKey(com), new SimVarDefinition
            {
                Name = Bbs757Radios.SwapEvent(com), DisplayName = $"COM{com} Transfer",
                Type = SimVarType.Event, UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsButton = true, IsMomentary = true,
            });
        }

        foreach (var (key, lvar, label) in Bbs757Radios.RtpLights)
        {
            Add(key, new SimVarDefinition
            {
                Name = lvar, DisplayName = label,
                Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
                ExcludeFromMonitorManager = true,
            });
        }
    }

    // =================================================================================
    // Panel
    // =================================================================================

    /// <summary>The map's section the COM radios panel joins.</summary>
    internal const string PedestalSection = "Pedestal";

    /// <summary>The app's own panel for the three COM radios.</summary>
    internal const string ComRadiosPanel = "COM radios";

    /// <summary>
    /// The "COM radios" panel, in the Pedestal right after the transponder's (its neighbour on the aft
    /// pedestal), or at the end of the section when the map has no such panel: for each radio its standby
    /// field then its transfer button, and a status display of the six frequencies.
    /// </summary>
    partial void AddRadioPanels(Dictionary<string, List<string>> structure, Dictionary<string, List<string>> controls,
        Dictionary<string, List<string>> displays)
    {
        var fields = new List<string>();
        var rows = new List<string>();
        for (int com = 1; com <= 3; com++)
        {
            fields.Add(Bbs757Radios.SetKey(com));
            fields.Add(Bbs757Radios.TransferKey(com));
            rows.Add(Bbs757Radios.ActiveKey(com));
            rows.Add(Bbs757Radios.StandbyKey(com));
        }
        controls[ComRadiosPanel] = fields;
        displays[ComRadiosPanel] = rows;

        if (!structure.TryGetValue(PedestalSection, out var panels)) structure[PedestalSection] = panels = new List<string>();
        int transponder = panels.IndexOf(TransponderPanel);
        panels.Insert(transponder < 0 ? panels.Count : transponder + 1, ComRadiosPanel);
    }

    // =================================================================================
    // Announcements and the status display
    // =================================================================================

    /// <summary>The six frequency rows by key: the radio and whether the row is its active frequency.</summary>
    private static readonly Dictionary<string, (int Com, bool Active)> ComRows = BuildComRows();

    private static Dictionary<string, (int Com, bool Active)> BuildComRows()
    {
        var rows = new Dictionary<string, (int, bool)>(StringComparer.Ordinal);
        for (int com = 1; com <= 3; com++)
        {
            rows[Bbs757Radios.ActiveKey(com)] = (com, true);
            rows[Bbs757Radios.StandbyKey(com)] = (com, false);
        }
        return rows;
    }

    /// <summary>
    /// The six frequency rows speak a change once ("COM1 active 124.850"), after a silent first reading
    /// and never for the app's own standby set (its echo window, <see cref="ArmOwnWriteEcho"/>). The
    /// words are the three-decimal frequency, so the simulator's float noise (121.9 read as
    /// 121.89999999999999) is one phrase and never a change. MainForm's mute wrap gates a Ctrl+M-muted row
    /// ([VAR-8]). Every other key is left to the next hook.
    /// </summary>
    partial void ProcessRadioUpdate(string varName, double value, ScreenReaderAnnouncer announcer, ref bool handled)
    {
        if (!ComRows.TryGetValue(varName, out var row)) return;
        handled = true;
        if (!double.IsFinite(value)) return;
        SpeakChange(varName,
            row.Active ? Bbs757Radios.ActivePhrase(row.Com, value) : Bbs757Radios.StandbyPhrase(row.Com, value),
            announcer);
    }

    /// <summary>The six frequency rows show three decimals ("121.900", where the unit alone would round to "122").</summary>
    partial void FormatRadioDisplay(string varKey, double value, ref string? text)
    {
        if (ComRows.ContainsKey(varKey)) text = Bbs757Radios.Frequency(value);
    }

    // =================================================================================
    // The standby field
    // =================================================================================

    /// <summary>The radio a standby field belongs to (1 to 3), or 0 for any other key.</summary>
    private static int ComOfSetKey(string varKey)
    {
        for (int com = 1; com <= 3; com++)
            if (varKey == Bbs757Radios.SetKey(com)) return com;
        return 0;
    }

    /// <summary>
    /// A standby field's raw text, taken before MainForm parses it (an empty box and a frequency off the
    /// channel grid each get their own words). Claimed at once, refusals included, so the generic path
    /// never writes its own. The set runs on its own task: fire and forget, it never throws.
    /// </summary>
    partial void HandleRadioTypedText(string varKey, string text, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        ref bool handled)
    {
        int com = ComOfSetKey(varKey);
        if (com == 0) return;
        handled = true;
        _ = SetStandbyAsync(com, text, simConnect, announcer);
    }

    /// <summary>
    /// A typed standby: refused aloud when it is not a channel of the band (<see cref="Bbs757Radios.TryParseCom"/>
    /// says why) or when no calculator write can land ("COM1 unavailable", nothing sent); otherwise the
    /// stock <c>COMn_STBY_RADIO_SET_HZ</c>, then, after <see cref="ReadBackDelayMs"/>, the standby row
    /// read fresh and what it holds spoken ("COM1 standby 121.500"), never the request (CORE-7). The row's
    /// own change, which the batch delivers meanwhile, is that write's echo: it is not said a second time,
    /// and the read-back closes the window. A radio that cannot be read back is not claimed to be tuned and
    /// keeps the window open for the late delivery. Never throws; silent after a switch away.
    /// </summary>
    internal async Task SetStandbyAsync(int com, string typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            if (!Bbs757Radios.TryParseCom(typed, out double mhz, out string error))
            {
                announcer.AnnounceImmediate(error);   // a validation error always speaks
                return;
            }

            var io = WriteIoFor(sim);
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", $"COM{com} standby: write refused, nothing sent.");
                announcer.AnnounceImmediate($"COM{com} unavailable");
                return;
            }

            string standby = Bbs757Radios.StandbyKey(com);
            OnOwnWrite(Bbs757Radios.SetKey(com));   // plan 13
            ArmOwnWriteEcho(standby);
            io.Write(Bbs757Radios.StandbySetCode(com, mhz));

            await io.Delay(ReadBackDelayMs, _lifetime.Token);
            double? read = await io.ReadFresh(standby, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            if (read is double held && double.IsFinite(held))
            {
                string phrase = Bbs757Radios.StandbyPhrase(com, held);
                SettleOwnWriteEcho(standby, phrase);
                announcer.AnnounceImmediate(phrase);
            }
            else
            {
                announcer.AnnounceImmediate($"{Bbs757Radios.StandbyPhrase(com, mhz)} sent, the radio did not report back");
            }
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"COM{com} standby set failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate($"COM{com} unavailable");
        }
    }

    // =================================================================================
    // The transfer
    // =================================================================================

    /// <summary>The radios whose transfer is running now (each swap toggles, so two overlapping ones could undo each other).</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, bool> _transfersRunning = new();

    /// <summary>The radio a transfer button belongs to (1 to 3), or 0 for any other key.</summary>
    private static int ComOfTransferKey(string varKey)
    {
        for (int com = 1; com <= 3; com++)
            if (varKey == Bbs757Radios.TransferKey(com)) return com;
        return 0;
    }

    /// <summary>
    /// A press of a COM transfer button, claimed at once so the generic path never sends its own write.
    /// Every other key is left to the next hook. The transfer runs on its own task: fire and forget, it
    /// never throws.
    /// </summary>
    partial void HandleRadioSet(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        ref bool handled)
    {
        int com = ComOfTransferKey(varKey);
        if (com == 0) return;
        handled = true;
        _ = TransferAsync(com, simConnect, announcer);
    }

    /// <summary>
    /// Transfers a radio, the stock swap first (R54: <c>COMn_RADIO_SWAP</c>, measured to work and hold on all
    /// three) and the tuning panel's button as the fallback for a swap BlueBird ignores.
    ///
    /// "COM1 unavailable" and nothing sent when no calculator write can land. Otherwise both rows are read
    /// fresh, their echo windows opened (the batch's own two changes are this write's, CORE-7), the swap
    /// written and, after <see cref="ReadBackDelayMs"/>, both rows read again. What is spoken is what the
    /// radio reports: "COM1 active 121.500, standby 124.850", once. Swapped means the new active is the old
    /// standby and the new standby the old active, at 1 kHz, so equal rows (COM 3 today) count as swapped.
    /// A row unreadable before the swap still gets the swap and, when both read after it, the sentence (you
    /// cannot tell whether it was ignored); one unreadable after it says "COM1 transfer sent, the radio did
    /// not report back" and keeps the windows open for the late delivery. A swap that left the rows as they
    /// were goes to <see cref="PanelFallbackAsync"/>. A second press of the same radio while its transfer
    /// runs is refused aloud; other radios run at once. Never throws; silent after a switch away.
    /// </summary>
    internal async Task TransferAsync(int com, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        bool running = false;
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            if (!_transfersRunning.TryAdd(com, true))
            {
                announcer.AnnounceImmediate(Bbs757Radios.TransferInProgressPhrase(com));
                return;
            }
            running = true;

            var io = WriteIoFor(sim);
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", $"COM{com} transfer: write refused, nothing sent.");
                announcer.AnnounceImmediate($"COM{com} unavailable");
                return;
            }

            var (activeBefore, standbyBefore) = await ReadComRowsAsync(io, com, ct);
            ct.ThrowIfCancellationRequested();

            ArmComEchoes(com);
            OnOwnWrite(Bbs757Radios.TransferKey(com));   // plan 13
            io.Write(Bbs757Radios.SwapCode(com));

            await io.Delay(ReadBackDelayMs, ct);
            var (active, standby) = await ReadComRowsAsync(io, com, ct);
            ct.ThrowIfCancellationRequested();

            if (active is not double a || standby is not double s)
            {
                announcer.AnnounceImmediate(Bbs757Radios.TransferNoReportPhrase(com));
                return;
            }
            if (activeBefore is not double a0 || standbyBefore is not double s0 || Bbs757Radios.Swapped(a0, s0, a, s))
            {
                SpeakTransfer(com, a, s, announcer);
                return;
            }

            await PanelFallbackAsync(com, io, a, s, announcer, ct);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"COM{com} transfer failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) announcer.AnnounceImmediate($"COM{com} unavailable");
        }
        finally
        {
            if (running) _transfersRunning.TryRemove(com, out _);
        }
    }

    /// <summary>
    /// The stock swap was ignored (the rows are as they were: <paramref name="active"/> and
    /// <paramref name="standby"/>). The tuning panel whose lights select the radio is found from the three
    /// panels' lights read fresh (<see cref="Bbs757Radios.RtpLightsKeys"/>, <see cref="Bbs757Radios.TransferPanelFor"/>):
    /// none, or lights that cannot be read, says "No tuning panel selects COM1" and presses nothing. Otherwise
    /// the panel's button is held, 1, <see cref="Bbs757Write.ButtonHoldMs"/> ms, 0, and the rows read again after
    /// <see cref="ReadBackDelayMs"/>: swapped speaks the sentence, anything else "COM1 did not transfer". The
    /// release is never canceled (a button is always let go, as <see cref="PressAsync"/>); the write is refused
    /// aloud when the link was lost meanwhile, and the echo windows are opened again for the button's own change.
    /// </summary>
    private async Task PanelFallbackAsync(int com, Bbs757SimIo io, double active, double standby,
        ScreenReaderAnnouncer announcer, CancellationToken ct)
    {
        var lights = new double?[Bbs757Radios.RtpLightsKeys.Count];
        for (int i = 0; i < lights.Length; i++)
            lights[i] = await io.ReadFresh(Bbs757Radios.RtpLightsKeys[i], ct);
        ct.ThrowIfCancellationRequested();

        if (Bbs757Radios.TransferPanelFor(com, lights[0], lights[1], lights[2]) is not int panel)
        {
            Log.Debug("BBS757", $"COM{com} transfer: the stock swap was ignored and no tuning panel selects it.");
            SettleComRows(com, active, standby);   // nothing changed: no echo is owed
            announcer.AnnounceImmediate(Bbs757Radios.NoPanelPhrase(com));
            return;
        }

        if (!io.CanWrite())
        {
            SettleComRows(com, active, standby);
            announcer.AnnounceImmediate($"COM{com} unavailable");
            return;
        }

        ArmComEchoes(com);
        OnOwnWrite(Bbs757Radios.TransferKey(com));   // plan 13
        io.Write(Bbs757Radios.PanelPressCode(panel));
        await io.Delay(Bbs757Write.ButtonHoldMs, CancellationToken.None);
        io.Write(Bbs757Radios.PanelReleaseCode(panel));

        await io.Delay(ReadBackDelayMs, ct);
        var (activeAfter, standbyAfter) = await ReadComRowsAsync(io, com, ct);
        ct.ThrowIfCancellationRequested();

        if (activeAfter is not double a || standbyAfter is not double s)
        {
            announcer.AnnounceImmediate(Bbs757Radios.TransferNoReportPhrase(com));
            return;
        }
        if (Bbs757Radios.Swapped(active, standby, a, s))
        {
            SpeakTransfer(com, a, s, announcer);
            return;
        }
        SettleComRows(com, a, s);
        announcer.AnnounceImmediate(Bbs757Radios.NotTransferredPhrase(com));
    }

    /// <summary>Both rows of a radio read fresh; a row that cannot be read, or is not a number, is null.</summary>
    private static async Task<(double? Active, double? Standby)> ReadComRowsAsync(Bbs757SimIo io, int com, CancellationToken ct)
    {
        double? active = await io.ReadFresh(Bbs757Radios.ActiveKey(com), ct);
        double? standby = await io.ReadFresh(Bbs757Radios.StandbyKey(com), ct);
        return (active is double a && double.IsFinite(a) ? a : null, standby is double s && double.IsFinite(s) ? s : null);
    }

    /// <summary>The app is about to write the radio: the batch's changes to both rows are that write's echo (CORE-7).</summary>
    private void ArmComEchoes(int com)
    {
        ArmOwnWriteEcho(Bbs757Radios.ActiveKey(com));
        ArmOwnWriteEcho(Bbs757Radios.StandbyKey(com));
    }

    /// <summary>A read-back fixed what both rows hold: that is their baseline now and their echo windows close.</summary>
    private void SettleComRows(int com, double active, double standby)
    {
        SettleOwnWriteEcho(Bbs757Radios.ActiveKey(com), Bbs757Radios.ActivePhrase(com, active));
        SettleOwnWriteEcho(Bbs757Radios.StandbyKey(com), Bbs757Radios.StandbyPhrase(com, standby));
    }

    /// <summary>The transfer's one sentence, from what the radio reports; the rows' own changes were its echo.</summary>
    private void SpeakTransfer(int com, double active, double standby, ScreenReaderAnnouncer announcer)
    {
        SettleComRows(com, active, standby);
        announcer.AnnounceImmediate(Bbs757Radios.TransferPhrase(com, active, standby));
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    /// <summary>
    /// Output N: the 757's own nav radios, read on a task of its own so the UI thread never waits on the
    /// CDU. Input Ctrl+N: the nav radios window.
    /// </summary>
    partial void HandleRadioHotkey(HotkeyAction action, SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ReadNavRadioInfo:
                handled = true;
                _ = ReadNavRadiosAsync(simConnect, announcer);   // never throws: it catches and logs
                break;
            case HotkeyAction.SetNavRadios:
                handled = true;
                hotkeyManager.ExitInputHotkeyMode();
                ShowNavRadiosWindow(simConnect, announcer, parentForm);
                break;
        }
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
            announcer.AnnounceImmediate(await NavRadiosReadoutAsync(sim, pages, _lifetime.Token));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Nav radio readout failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested)
                announcer.AnnounceImmediate(Bbs757NavRad.Readout(null, null, null, null));   // a readout never goes silent
        }
    }

    /// <summary>
    /// N's sentence (<see cref="Bbs757NavRad.Readout"/>): the three courses read fresh, the first officer's CDU
    /// brought to NAV RADIO (or found there). Throws when a course read throws or the token fires; the page
    /// service itself never does, so a CDU that cannot be had gives the readout without frequencies.
    /// </summary>
    private async Task<string> NavRadiosReadoutAsync(SimConnectManager sim, Bbs757CduPages pages, CancellationToken ct)
    {
        var io = ReadoutIo?.Invoke(sim) ?? ReadoutIoFor(sim);
        int? vorL = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.VorLCourseKey, ct));
        int? vorR = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.VorRCourseKey, ct));
        int? ils = Bbs757NavRad.Course(await io.Read(Bbs757NavRad.IlsCourseKey, ct));

        var screen = await pages.ShowPageAsync(Bbs757CduPages.FirstOfficer, Bbs757NavRad.PageKey, Bbs757NavRad.IsNavRadio, ct);
        ct.ThrowIfCancellationRequested();
        return Bbs757NavRad.Readout(screen == null ? null : Bbs757NavRad.Parse(screen), vorL, vorR, ils);
    }

    // =================================================================================
    // The Ctrl+N window
    // =================================================================================

    /// <summary>What the status says while the window reads the nav radios.</summary>
    internal const string NavRadiosReadingMessage = "Reading the nav radios…";

    /// <summary>The Ctrl+N window; a later Ctrl+N brings it forward. Null before the first and after <see cref="CloseRadioWindows"/>.</summary>
    private Forms.BBS757.BBS757NavRadiosForm? _navRadiosForm;

    /// <summary>The Ctrl+N window, for the tests.</summary>
    internal Forms.BBS757.BBS757NavRadiosForm? NavRadiosWindow => _navRadiosForm;

    /// <summary>The read that fills the Ctrl+N window opened last (it never throws), for the tests; null before the first.</summary>
    internal Task? NavRadiosFill { get; private set; }

    /// <summary>Shows a radio window given its owner, replaced by tests (a test window is never shown); null: <c>Show(owner)</c>.</summary>
    internal Action<Form, Form?>? ShowRadioForm { get; init; }

    /// <summary>
    /// Ctrl+N: the one nav radios window, created on the first press and brought forward on a later one
    /// (its status is then what it last read). The status opens on <see cref="NavRadiosReadingMessage"/>
    /// while <see cref="FillNavRadiosWindowAsync"/> reads in the background. Runs on the UI thread, so the
    /// tablet client is made there (<paramref name="pages"/> given: a test's page service).
    /// </summary>
    internal Forms.BBS757.BBS757NavRadiosForm ShowNavRadiosWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer,
        Form? parentForm, Bbs757CduPages? pages = null)
    {
        if (_navRadiosForm != null && !_navRadiosForm.IsDisposed)
        {
            _navRadiosForm.Activate();
            return _navRadiosForm;
        }

        pages ??= CduPages;
        Forms.BBS757.BBS757NavRadiosForm? form = null;
        void Show(string text) => form?.ShowStatus(text);
        form = new Forms.BBS757.BBS757NavRadiosForm(announcer, typed =>
        {
            if (!Bbs757NavRad.TryParseCourse(typed, out int degrees, out string error)) return error;
            _ = SetIlsCourseAsync(degrees, sim, announcer, Show);   // never throws: it catches and logs
            return null;
        });
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_navRadiosForm, form)) _navRadiosForm = null;
        };
        _navRadiosForm = form;
        form.ShowStatus(NavRadiosReadingMessage);
        if (ShowRadioForm != null) ShowRadioForm(form, parentForm);
        else form.Show(parentForm);
        NavRadiosFill = FillNavRadiosWindowAsync(form, sim, announcer, pages);
        return form;
    }

    /// <summary>
    /// Fills the window's status: N's readout, then the FMC's runway ILS (<see cref="Bbs757NavRad.RunwayIlsPhrase"/>;
    /// "FMC runway ILS unavailable" when APPROACH REF cannot be had), and speaks it once: the screen reader
    /// cannot know the box changed. A courses read that fails still gives the readout with everything
    /// unavailable. Silent when the window was closed or the profile switched away meanwhile. Never throws.
    /// </summary>
    private async Task FillNavRadiosWindowAsync(Forms.BBS757.BBS757NavRadiosForm form, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, Bbs757CduPages pages)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            string readout;
            try
            {
                readout = await NavRadiosReadoutAsync(sim, pages, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error("BBS757", $"Nav radios window: the readout failed: {ex.Message}");
                readout = Bbs757NavRad.Readout(null, null, null, null);
            }
            var ils = await ReadRunwayIlsAsync(pages, ct);
            ct.ThrowIfCancellationRequested();

            string status = $"{readout} {Bbs757NavRad.RunwayIlsPhrase(ils)}.";
            if (form.IsDisposed) return;
            form.ShowStatus(status);
            announcer.AnnounceImmediate(status);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Nav radios window failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The FMC's runway ILS, read from APPROACH REF on the first officer's CDU (R52) and then the CDU is put
    /// back on NAV RADIO, whether or not it was found. The way in (measured on the ground): INIT gave POS INIT
    /// or PERF INIT, whose 6L (<c>&lt;INDEX</c>) gave INIT/REF INDEX, whose 5L (<c>&lt;APPROACH</c>) gave
    /// APPROACH REF; in flight INIT may open APPROACH REF at once (Boeing's INIT REF depends on the phase;
    /// unmeasured), or the index. Every step is checked by the page it shows; a step that does not give the
    /// expected page, a CDU that is busy or unreachable, or a page with no ILS rows gives null. Never throws
    /// (a token that fires gives null too; the caller checks it).
    /// </summary>
    internal async Task<Bbs757RunwayIls?> ReadRunwayIlsAsync(Bbs757CduPages pages, CancellationToken ct)
    {
        Bbs757RunwayIls? ils = null;
        try
        {
            ils = await FindRunwayIlsAsync(pages, ct);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"APPROACH REF read failed: {ex.Message}");
        }

        // The page the app leaves the FO CDU on. A call that refuses (the CDU busy) presses nothing.
        await pages.ShowPageAsync(Bbs757CduPages.FirstOfficer, Bbs757NavRad.PageKey, Bbs757NavRad.IsNavRadio, ct);
        return ils;
    }

    private static async Task<Bbs757RunwayIls?> FindRunwayIlsAsync(Bbs757CduPages pages, CancellationToken ct)
    {
        const int cdu = Bbs757CduPages.FirstOfficer;
        var screen = await pages.ShowPageAsync(cdu, Bbs757CduKeys.Init,
            s => Bbs757NavRad.IsApproachRef(s) || Bbs757NavRad.IsInitRefIndex(s) || Bbs757NavRad.HasIndexKey(s), ct);
        if (screen == null) return null;

        if (!Bbs757NavRad.IsApproachRef(screen))
        {
            if (!Bbs757NavRad.IsInitRefIndex(screen))
            {
                screen = await pages.PressKeysAsync(cdu, new[] { Bbs757CduKeys.Lsk(right: false, 6) }, ct);   // <INDEX
                if (screen == null || !Bbs757NavRad.IsInitRefIndex(screen)) return null;
            }
            screen = await pages.PressKeysAsync(cdu, new[] { Bbs757CduKeys.Lsk(right: false, 5) }, ct);      // <APPROACH
            if (screen == null || !Bbs757NavRad.IsApproachRef(screen)) return null;
        }
        return Bbs757NavRad.ParseApproachRefIls(screen);
    }

    /// <summary>
    /// Sets the ILS course: "ILS course unavailable" and nothing written when no calculator write can land;
    /// otherwise the course is written, and after <see cref="ReadBackDelayMs"/> read fresh and spoken from
    /// what came back ("ILS course 095", three digits), never the request (CORE-7). A course that cannot be
    /// read back is not claimed: "ILS course 095 sent, not read back". Every result is also handed to
    /// <paramref name="onSaid"/> (the window's status). Never throws; silent after a switch away.
    /// </summary>
    internal async Task SetIlsCourseAsync(int degrees, SimConnectManager sim, ScreenReaderAnnouncer announcer, Action<string>? onSaid = null)
    {
        void Say(string text)
        {
            onSaid?.Invoke(text);
            announcer.AnnounceImmediate(text);
        }

        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var io = WriteIoFor(sim);
            if (!io.CanWrite())
            {
                Log.Debug("BBS757", "ILS course: write refused, nothing sent.");
                Say("ILS course unavailable");
                return;
            }

            OnOwnWrite(Bbs757NavRad.IlsCourseKey);   // plan 13
            io.Write(Bbs757NavRad.CourseCode(Bbs757NavRad.IlsCourseLVar, degrees));

            await io.Delay(ReadBackDelayMs, _lifetime.Token);
            double? read = await io.ReadFresh(Bbs757NavRad.IlsCourseKey, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            Say(Bbs757NavRad.Course(read) is int held
                ? $"ILS course {held.ToString("000", CultureInfo.InvariantCulture)}"
                : $"ILS course {degrees.ToString("000", CultureInfo.InvariantCulture)} sent, not read back");
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"ILS course set failed: {ex.Message}");
            if (!_lifetime.IsCancellationRequested) Say("ILS course unavailable");
        }
    }

    /// <summary>Closes and disposes the radio windows (a profile switch): the Ctrl+N window.</summary>
    partial void CloseRadioWindows()
    {
        var form = _navRadiosForm;
        _navRadiosForm = null;
        if (form == null || form.IsDisposed) return;
        try
        {
            form.Close();
            if (!form.IsDisposed) form.Dispose();
        }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"Nav radios window did not close: {ex.Message}");
        }
    }
}
