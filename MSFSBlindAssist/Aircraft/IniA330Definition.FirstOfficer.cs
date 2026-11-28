// Plan 06 owns this file: the First Officer.

using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Aircraft.A330;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>The First Officer's own read-only keys (FirstOfficer/IniAirbus).</summary>
    internal const string FoEng1AvailKey = "A330_FO_ENG1_AVAIL", FoEng2AvailKey = "A330_FO_ENG2_AVAIL",
        FoApuFlapKey = "A330_FO_APU_FLAP", FoGearLeftKey = "A330_FO_GEAR_LEFT", FoGearRightKey = "A330_FO_GEAR_RIGHT";

    internal static readonly (string key, string name, string display, string units, SimVarType type)[] FoReadVars =
    {
        (FoEng1AvailKey, "ENGINE_1_AVAIL", "Engine 1 available", "number", SimVarType.LVar),
        (FoEng2AvailKey, "ENGINE_2_AVAIL", "Engine 2 available", "number", SimVarType.LVar),
        (FoApuFlapKey, "INI_APU_FLAP_PERCENT", "APU air intake flap", "number", SimVarType.LVar),
        (FoGearLeftKey, "GEAR LEFT POSITION", "Left main gear", "percent", SimVarType.SimVar),
        (FoGearRightKey, "GEAR RIGHT POSITION", "Right main gear", "percent", SimVarType.SimVar),
    };

    /// <summary>
    /// How a First Officer write of <paramref name="key"/> reaches the aircraft through
    /// <see cref="HandleUIVariableSet"/>, so the First Officer can refuse a write that cannot land
    /// instead of reporting success (HandleUIVariableSet itself only speaks the failure, and the First
    /// Officer suppresses that). <c>Calc</c>: the calculator path (MobiFlight) — a push, rpn or toggle
    /// control. <c>InputEvent</c>: SetInputEvent / a value press on this input
    /// event. <c>Toggle</c>: the control presses only when its read differs, so an unread state must not
    /// be written. <c>HoldMs</c>: a held momentary key (the tests). Null: not a control the First Officer
    /// may write.
    /// </summary>
    internal (bool Calc, string? InputEvent, bool Toggle, int HoldMs)? FoWriteRoute(string key)
    {
        GetVariables();
        if (key == A330SharedKeys.Autobrake)
            return (false, AutobrakeEvent(3), false, 0);
        if (!_controlsByKey.TryGetValue(key, out var c) || c.Kind == "status") return null;
        return c.Mode switch
        {
            "push" when !string.IsNullOrEmpty(c.Event) => (true, null, false, c.HoldMs ?? 0),
            "rpn" when !string.IsNullOrEmpty(c.Rpn) => (true, null, false, c.HoldMs ?? 0),
            // the push-buttons are pressed by value through their input event (measured by the panel sweep, 2026-11-27)
            "toggle" when c.Press is not null && string.IsNullOrEmpty(c.Rpn) && !string.IsNullOrEmpty(c.Event) => (false, c.Event, true, 0),
            "toggle" when !string.IsNullOrEmpty(c.Rpn) || !string.IsNullOrEmpty(c.Event) => (true, null, true, 0),
            "set" when !string.IsNullOrEmpty(c.Event) => (false, c.Event, false, 0),
            _ => null,
        };
    }

    /// <summary>The read-back value of the generated control <paramref name="key"/> for the position
    /// labelled <paramref name="label"/>, or null when the control or label does not exist (tests pin the
    /// First Officer's values against the measured map).</summary>
    internal double? FoPositionValue(string key, string label)
    {
        GetVariables();
        return _controlsByKey.TryGetValue(key, out var c)
            ? c.Positions?.FirstOrDefault(p => p.Label == label)?.Value : null;
    }

    partial void AddFirstOfficerVariables(Dictionary<string, SimVarDefinition> vars)
    {
        // ---------------- First Officer reads (OnRequest, silent; polled by the FO window) ----------------
        // Each name is in the aircraft's own panel-state files (data/panelstates/*.ini in the streamed
        // package) with values that follow the state they name: ENGINE_n_AVAIL 0 cold and
        // dark, 1 ready for take-off (the E/WD's green AVAIL); INI_APU_FLAP_PERCENT 0 cold and dark, 1 with
        // the APU running or still spinning down. Read only — the First Officer never writes them.
        foreach (var (key, name, display, units, type) in FoReadVars)
            if (!vars.ContainsKey(key)) OnRequestVar(vars, key, name, display, units, type);
    }
}
