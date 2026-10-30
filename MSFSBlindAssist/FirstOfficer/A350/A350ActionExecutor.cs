using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.A350;

/// <summary>
/// Drives the iniBuilds A350 for the First Officer through the panel rows' own write path
/// (<see cref="IniBuildsA350Definition.ApplyForFirstOfficer"/>), so a flow moves a switch exactly as the pilot's panel does, plus
/// a few names of its own for what is more than one row (<see cref="ExternalPowerKey"/>, the baros) or a held press. A button
/// that flips what it switches is pressed only on a definite mismatch: an unread state is refused and reported, never guessed.
/// A suppressed announcer keeps the First Officer's own step call-outs the single voice.
/// </summary>
public sealed class A350ActionExecutor : IFoActionExecutor
{
    private const int WriteSpacingMs = 200;

    /// <summary>The executor's own names.</summary>
    public const string FireTestKey = "FIRE_TEST", ToConfigTestKey = "TO_CONFIG_TEST", BaroStdKey = "BARO_STD", BaroQnhKey = "BARO_QNH",
        BaroUnitHectopascalsKey = "BARO_UNIT_HPA", ExternalPowerKey = "EXTERNAL_POWER";

    /// <summary>How long the fire test is held: its warnings and chime come only after 2.5 s (A350WritePlan, measured).</summary>
    public const int FireTestHoldMs = 3000;

    /// <summary>How long T.O CONFIG is held: at 250 ms the memo's TO CONFIG TEST stayed undone (A350WritePlan, measured); the
    /// A380's and the A32NX's 2 s.</summary>
    public const int ToConfigHoldMs = 2000;

    private const string FireTestRow = "A350_FIRE_TEST", ToConfigRow = "A350_ECAM_TO_CONFIG";

    /// <summary>Buttons that flip what they switch, each with the variable that reads it.</summary>
    private static readonly IReadOnlyDictionary<string, string> FlippingButtons = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A350_LDG_AUTO_BRK"] = A350StateEvaluator.AutoBrakeArmed,
        ["A350_APU_START"] = A350StateEvaluator.ApuStartButton,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HeldPresses _held = new();
    private SimConnectManager? _sc;
    private IniBuildsA350Definition? _def;
    private ScreenReaderAnnouncer? _announcer;
    private DateTime _lastWriteUtc = DateTime.MinValue;

    public void SetSimConnect(SimConnectManager? sc) => _sc = sc;
    public void SetDefinition(IniBuildsA350Definition def) => _def = def;
    public void SetAnnouncer(ScreenReaderAnnouncer announcer) => _announcer = announcer;

    public bool IsAvailable => _sc is { IsConnected: true } && _def != null && _announcer != null;

    /// <summary>The window is closing: a held test is let go now, while the sim still listens.</summary>
    public void ReleaseHeldPresses() => _held.ReleaseAll();

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync();
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                if (step.EventName == FireTestKey) return await FireTestAsync();
                if (step.EventName == ToConfigTestKey) return await TakeoffConfigTestAsync();
                return await DispatchAsync(step.EventName, step.TargetValue);

            case FlowStepActionType.SetSwitchMultiple:
                await _gate.WaitAsync();
                try
                {
                    bool ok = true;
                    foreach (var (name, target) in step.MultiActions)
                        ok &= await DispatchCoreAsync(name, target);
                    return ok;
                }
                finally { _gate.Release(); }

            default:
                return false;
        }
    }

    private async Task<bool> DispatchAsync(string name, int? target)
    {
        await _gate.WaitAsync();
        try { return await DispatchCoreAsync(name, target); }
        finally { _gate.Release(); }
    }

    // Must be called inside _gate.
    private async Task<bool> DispatchCoreAsync(string name, int? target)
    {
        if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
        await PaceAsync();
        // Again after the pacing wait: a closed window (an aircraft switch) detaches the executor meanwhile, and the write must
        // not reach the next aircraft.
        if (_sc is not { IsConnected: true } sc) return false;
        bool ok = name switch
        {
            BaroStdKey => ApplySilent("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", 1, sc) & ApplySilent("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH", 1, sc),
            BaroQnhKey => ApplySilent("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", 1, sc) & ApplySilent("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL", 1, sc),
            BaroUnitHectopascalsKey => PressEachNotAt(1, sc, ("A350_EFIS_QNH_UNIT_SELECTOR_LEFT", A350StateEvaluator.BaroUnit1),
                ("A350_EFIS_QNH_UNIT_SELECTOR_RIGHT", A350StateEvaluator.BaroUnit2)),
            ExternalPowerKey => PressEachNotAt(target ?? 1, sc, ("A350_ELEC_EXT_1", A350StateEvaluator.ExtAOnline), ("A350_ELEC_EXT_2", A350StateEvaluator.ExtBOnline)),
            _ when FlippingButtons.TryGetValue(name, out var state) => PressEachNotAt(target ?? 1, sc, (name, state)),
            _ => ApplySilent(name, target ?? 1, sc),
        };
        _lastWriteUtc = DateTime.UtcNow;
        return ok;
    }

    /// <summary>Presses each button whose state differs from <paramref name="target"/>. Any state unread presses nothing and fails:
    /// a press flips, so a guess could undo what the pilot set.</summary>
    private bool PressEachNotAt(int target, SimConnectManager sc, params (string Row, string State)[] buttons)
    {
        var states = buttons.Select(b => _def!.CachedInput(sc, b.State)).ToList();
        if (states.Contains(null))
        {
            Log.Warn("fo_a350", $"{string.Join(", ", buttons.Select(b => b.Row))}: state unread; nothing pressed");
            return false;
        }
        bool ok = true;
        for (int n = 0; n < buttons.Length; n++)
            if ((states[n]!.Value > 0.5 ? 1 : 0) != target)
                ok &= ApplySilent(buttons[n].Row, 1, sc);
        return ok;
    }

    // The panel's write under a Suppressed wrap: the definition's own "<name> pressed" and refusals are dropped and the First
    // Officer's call-out stays the one voice; the prior state is restored (never clobbering the startup grace period).
    private bool ApplySilent(string rowKey, double value, SimConnectManager sc)
    {
        if (_def == null || _announcer == null) return false;
        bool prior = _announcer.Suppressed;
        _announcer.Suppressed = true;
        try
        {
            if (_def.ApplyForFirstOfficer(rowKey, value, sc, _announcer)) return true;
            Log.Warn("fo_a350", $"{rowKey}: no such row; nothing written");
            return false;
        }
        finally { _announcer.Suppressed = prior; }
    }

    private async Task PaceAsync()
    {
        var since = DateTime.UtcNow - _lastWriteUtc;
        var gap = TimeSpan.FromMilliseconds(WriteSpacingMs);
        if (since < gap) await Task.Delay(gap - since);
    }

    /// <summary>Write one control by its row key (the checklist's CheckAction path).</summary>
    public Task<bool> Set(string varKey, int value) => DispatchAsync(varKey, value);

    /// <summary>The fire test, held: warnings and the chime while held are the blind pilot's verification.</summary>
    public Task<bool> FireTestAsync() => HoldAsync(FireTestRow, FireTestHoldMs);

    /// <summary>The T.O CONFIG key, held so the memo's TO CONFIG TEST completes.</summary>
    public Task<bool> TakeoffConfigTestAsync() => HoldAsync(ToConfigRow, ToConfigHoldMs);

    /// <summary>A button the aircraft holds until pressed again (A350WritePlan.IsLatched): pressed, held, pressed again, whatever
    /// happens, through the manager the press went through, and at once if the window closes mid-hold. Holds the gate for the
    /// whole test, so WaitForDispatchDrainAsync covers the checklist's grace.</summary>
    public async Task<bool> HoldAsync(string rowKey, int holdMs)
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            if (_sc is not { IsConnected: true } sc) return false;   // detached during the wait: presses nothing
            long hold = _held.Begin(() => { if (sc.IsConnected) ApplySilent(rowKey, 1, sc); });
            try
            {
                ApplySilent(rowKey, 1, sc);
                _lastWriteUtc = DateTime.UtcNow;
                await Task.Delay(holdMs);
                return true;
            }
            finally
            {
                _held.End(hold);
                _lastWriteUtc = DateTime.UtcNow;
            }
        }
        finally { _gate.Release(); }
    }

    // ---- Convenience methods for the auto-manager and the phase monitor ----

    /// <summary>Both baros to STD or QNH (live 2026-10-30: each knob's push is STD and its pull QNH, pressed again no change).</summary>
    public Task<bool> SetBaroStd(bool std) => DispatchAsync(std ? BaroStdKey : BaroQnhKey, 1);
    public Task<bool> SetLandingLights(int on) => DispatchAsync("A350_LIGHTS_EXT_LANDING", on);

    /// <summary>The nose light's selector: 0 T.O, 1 Taxi, 2 Off.</summary>
    public Task<bool> SetNoseLight(int position) => DispatchAsync("A350_LIGHTS_EXT_NOSE", position);

    /// <summary>The seat belt switch: 0 On, 2 Off (1 is Auto).</summary>
    public Task<bool> SetSeatbeltSign(bool on) => Set("A350_SIGNS_SEAT_BELTS", on ? 0 : 2);
}
