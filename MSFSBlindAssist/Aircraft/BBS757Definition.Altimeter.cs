using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The altimeters, the radio minimums and the squawk: the Ctrl+B "Altimeter and minimums" window
/// and the transponder panel's squawk field. How each value is set and read back is in
/// <see cref="Bbs757Altimeters"/> and <see cref="Bbs757Squawk"/>; this partial is the sim side.
///
/// Every set is a calculator write of a stock K-event (<c>KOHLSMAN_SET</c>, the decision height
/// steps, <c>XPNDR_SET</c>), so it refuses aloud when the calculator cannot land a write
/// ([MD11-16]'s pattern; no SendEvent, so [SIM-20] has nothing to say here), runs from the UI thread
/// (the window's and the panel's callbacks; the awaits keep the context), and speaks the value READ
/// back, never the request (CORE-7). The altimeter and minimums sets ride the MCP sets' machinery
/// (<see cref="RunMcpSetAsync"/>): a newer set supersedes one in flight, a repeat of the same value
/// is ignored, a flight load or a profile switch cancels.
/// </summary>
public partial class BBS757Definition
{
    private const string AltimeterSetKey = "BBS757_ALTIMETER_SET";
    private const string MinimumsSetKey = "BBS757_MINIMUMS_SET";

    /// <summary>The open Ctrl+B window; a second press brings it forward.</summary>
    private Forms.BBS757.BBS757AltimeterForm? _altimeterForm;

    private void ShowAltimeterWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (_altimeterForm != null && !_altimeterForm.IsDisposed)
        {
            _altimeterForm.Activate();
            return;
        }

        Forms.BBS757.BBS757AltimeterForm? form = null;
        void Show(string text) => form?.ShowStatus(text);
        form = new Forms.BBS757.BBS757AltimeterForm(announcer,
            typed =>
            {
                if (!Bbs757Altimeters.TryAltimeterInput(typed, out uint parameter, out string? error)) return error;
                _ = SetAltimetersAsync(parameter, sim, announcer, Show);
                return null;
            },
            () => _ = SetAltimetersAsync(Bbs757Altimeters.StandardParameter, sim, announcer, Show),
            typed =>
            {
                if (!Bbs757Altimeters.TryMinimumsInput(typed, out int feet, out string? error)) return error;
                _ = SetMinimumsAsync(feet, sim, announcer, Show);
                return null;
            });
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_altimeterForm, form)) _altimeterForm = null;
        };
        _altimeterForm = form;
        form.Show(parentForm);
        _ = ShowCurrentAltimetersAsync(form, sim);
    }

    /// <summary>The window opens on what the aircraft shows now, in its status box (not spoken: focus lands on the altimeter box).</summary>
    private async Task ShowCurrentAltimetersAsync(Forms.BBS757.BBS757AltimeterForm form, SimConnectManager sim)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            var ct = _lifetime.Token;
            var io = ReadoutIoFor(sim);
            string now = Bbs757Altimeters.Readout(
                await io.Read(Bbs757Altimeters.Kohlsman1Key, ct), await io.Read(Bbs757Altimeters.Kohlsman2Key, ct),
                await io.Read(Bbs757Altimeters.Kohlsman3Key, ct), await io.Read(Bbs757Altimeters.DecisionHeightKey, ct));
            ct.ThrowIfCancellationRequested();
            form.ShowStatus(now);
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Debug("BBS757", $"Altimeter window: current values not read: {ex.Message}");
        }
    }

    /// <summary>Sets all three altimeters and speaks what they read ("Altimeter: 1020, 30.12"); "Altimeter unavailable" when nothing can land.</summary>
    internal Task SetAltimetersAsync(uint parameter, SimConnectManager sim, ScreenReaderAnnouncer announcer, Action<string>? onSaid = null)
        => RunMcpSetAsync(AltimeterSetKey, (int)parameter, ignoreSameValueInFlight: true, "Altimeter unavailable", sim, announcer,
            (io, ct) => Bbs757Altimeters.SetAltimetersAsync(io, parameter, ct), onSaid);

    /// <summary>Steps the radio minimums and speaks the decision height read ("DH 200"); "DH unavailable" when nothing can land.</summary>
    internal Task SetMinimumsAsync(int feet, SimConnectManager sim, ScreenReaderAnnouncer announcer, Action<string>? onSaid = null)
        => RunMcpSetAsync(MinimumsSetKey, feet, ignoreSameValueInFlight: true, Bbs757Altimeters.DhPhrase(null), sim, announcer,
            (io, ct) => Bbs757Altimeters.SetDecisionHeightAsync(io, feet, ct), onSaid);

    /// <summary>
    /// The transponder panel's squawk field: a code that is not four digits 0 to 7 is refused aloud;
    /// otherwise one <c>XPNDR_SET</c> in BCD, then <c>TRANSPONDER CODE:1</c> read back and "Squawk 4743"
    /// spoken (or what the transponder holds instead). Never throws: fire and forget from the panel.
    /// </summary>
    internal async Task SetSquawkAsync(double typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            if (_lifetime.IsCancellationRequested) return;
            if (!Bbs757Squawk.TryParse(typed, out string code, out string error))
            {
                announcer.AnnounceImmediate(error);
                return;
            }
            if (!CanWrite(sim))
            {
                Log.Debug("BBS757", "Squawk: write refused, nothing sent.");
                announcer.AnnounceImmediate("Squawk unavailable");
                return;
            }
            sim.ExecuteCalculatorCodeUnique(Bbs757Squawk.Write(code));
            await Task.Delay(ReadBackDelayMs, _lifetime.Token);
            double? read = await sim.ReadFreshAsync(Bbs757Squawk.CodeKey, FreshReadTimeoutMs, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            announcer.AnnounceImmediate(Bbs757Squawk.Confirmation(code, read));
        }
        catch (OperationCanceledException) { /* switched away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"Squawk set failed: {ex.Message}");
            announcer.AnnounceImmediate("Squawk unavailable");
        }
    }
}
