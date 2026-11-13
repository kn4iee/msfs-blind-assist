using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// A display window's controls are the panels' own rows (plan 26, [A350-17]): a state read as the panel shows it, and a press as the
/// panel makes it. The definition's adapter is <see cref="A350WindowRows"/>; the tests fake it.
/// </summary>
public interface IA350WindowRows
{
    /// <summary>The row's state as its panel shows it ("On", "Captain baro unit: Hectopascals"), or null while unread.</summary>
    string? State(string row);

    /// <summary>Presses the row, or sets it to <paramref name="value"/>, as its panel does.</summary>
    void Press(string row, double value = 1);

    /// <summary>A two-position row pressed to its other position, as the FCU windows' FD.</summary>
    void Toggle(string row);

    /// <summary>A typed row (a QNH) applied with <paramref name="text"/>, as the panel's field applies it on Enter.</summary>
    void Type(string row, string text);
}

/// <summary>
/// The A350 definition's rows behind a display window, as its FCU windows press them: the state from
/// <see cref="IniBuildsA350Definition.TryDescribeControlState"/> (else the row's value word), a press through
/// <see cref="IniBuildsA350Definition.PressRow"/> with the row armed as the window's own echo, a toggle through
/// <see cref="IniBuildsA350Definition.PressToggleFromWindow"/>. The row's lights follow as the pilot's own press (the write path's
/// NoteOwnLamps), so the button's new text is the one thing heard ([CORE-7]).
/// </summary>
internal sealed class A350WindowRows : IA350WindowRows
{
    private readonly IniBuildsA350Definition _aircraft;
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;

    public A350WindowRows(IniBuildsA350Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _aircraft = aircraft;
        _simConnect = simConnect;
        _announcer = announcer;
    }

    public string? State(string row)
    {
        if (_aircraft.TryDescribeControlState(row, out var state) && state.Length > 0) return state;
        return _simConnect.GetCachedVariableValue(row) is double v && _aircraft.GetVariables().TryGetValue(row, out var def)
            && def.ValueDescriptions.TryGetValue(Math.Round(v), out var word) ? word : null;
    }

    public void Press(string row, double value = 1)
    {
        _aircraft.ArmWindowEcho(row);
        _aircraft.PressRow(row, _simConnect, _announcer, value);
    }

    public void Toggle(string row) => _aircraft.PressToggleFromWindow(row, _simConnect, _announcer);

    /// <summary>As MainForm's typed field (PanelBuilder's ApplyTyped): the number, or NaN for a field that refuses unparsable text
    /// (the A350's typed rows), else 0; the row's own plan validates it and says its refusal.</summary>
    public void Type(string row, string text)
    {
        if (!_aircraft.GetVariables().TryGetValue(row, out var def)) return;
        bool parsed = Utils.NumberParsing.TryParseTyped(text, out double value);
        _aircraft.PressRow(row, _simConnect, _announcer, parsed ? value : def.UnparseableTextAsNaN ? double.NaN : 0);
    }
}
