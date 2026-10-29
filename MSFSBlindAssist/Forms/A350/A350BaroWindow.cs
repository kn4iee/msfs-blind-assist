using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A380's Altimeter window on the A350 (input Ctrl+B): a QNH typed once sets both sides and the ISIS (each side's
/// typed QNH row, in that side's unit), STD on both sides (the baro knob's push) or QNH (its pull), and each side's unit
/// (its EFIS panel's unit button, a toggle labelled with the unit it shows: the A380's unit, parity task 7); both sides'
/// baro and the ISIS setting stay current in read-only boxes.
/// </summary>
public sealed class A350BaroWindow : A350FcuWindowBase
{
    private readonly TextBox _qnh, _captain, _firstOfficer, _isis;

    public A350BaroWindow(IniBuildsA350Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "A350 Altimeter";
        Size = new Size(420, 380);
        var label = new Label { Text = "QNH (both sides):", Location = new Point(20, 25), Size = new Size(130, 20) };
        _qnh = new TextBox { Location = new Point(160, 22), Size = new Size(110, 25), TabIndex = 0, AccessibleName = "QNH value",
            AccessibleDescription = "Hectopascals (1013) or inches (29.92), in each side's unit. Enter sets both sides and the ISIS." };
        _qnh.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            if (!Utils.NumberParsing.TryParseTyped(_qnh.Text.Trim(), out double v)) { announcer.AnnounceImmediate("Invalid number"); _qnh.SelectAll(); return; }
            aircraft.PressRow("A350_CAPT_QNH_SET", simConnect, announcer, v);
            aircraft.PressRow("A350_FO_QNH_SET", simConnect, announcer, v);
            aircraft.PressRow("A350_ISIS_QNH_SET", simConnect, announcer, v);
            _qnh.SelectAll();
        };
        _captain = StatusBox("Captain baro", 20, 60, 360, 1);
        _firstOfficer = StatusBox("First officer baro", 20, 90, 360, 2);
        _isis = StatusBox("ISIS QNH setting", 20, 120, 360, 3);
        var std = new Button { Text = "STD (both sides)", Location = new Point(20, 160), Size = new Size(175, 35), TabIndex = 4, AccessibleName = "STD, both sides" };
        std.Click += (s, e) =>
        {
            aircraft.PressRow("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", simConnect, announcer);
            aircraft.PressRow("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH", simConnect, announcer);
        };
        var qnh = new Button { Text = "QNH (both sides)", Location = new Point(205, 160), Size = new Size(175, 35), TabIndex = 5, AccessibleName = "QNH, both sides" };
        qnh.Click += (s, e) =>
        {
            aircraft.PressRow("A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", simConnect, announcer);
            aircraft.PressRow("A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL", simConnect, announcer);
        };
        // Each side's unit apart: one button for both would flip two sides that differ the wrong way round.
        var captainUnit = RowButton("Captain baro unit", "A350_EFIS_QNH_UNIT_SELECTOR_LEFT", 20, 205, 175, 6);
        var firstOfficerUnit = RowButton("First officer baro unit", "A350_EFIS_QNH_UNIT_SELECTOR_RIGHT", 205, 205, 175, 7);
        var close = new Button { Text = "Close", Location = new Point(140, 270), Size = new Size(140, 35), TabIndex = 8, AccessibleName = "Close" };
        close.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { label, _qnh, _captain, _firstOfficer, _isis, std, qnh, captainUnit, firstOfficerUnit, close });
        CancelButton = close;
    }

    protected override void OnShownForm() => _qnh.Focus();

    protected override void RefreshStates()
    {
        base.RefreshStates();
        Set(_captain, "Captain baro: ", aircraft.FcuWindowText("Captain baro", simConnect));
        Set(_firstOfficer, "First officer baro: ", aircraft.FcuWindowText("First officer baro", simConnect));
        Set(_isis, "ISIS: ", aircraft.FcuWindowText("ISIS QNH setting", simConnect));
    }

    private static void Set(TextBox box, string name, string? shown)
    {
        string text = name + (shown ?? "not available");
        if (box.Text != text) box.Text = text;
    }
}
