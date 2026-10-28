using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>One FCU window's controls: its typed value row, its knob's push and pull rows, the window it reads, and the
/// pushbuttons beside it the A380's window offers (SPD/MACH; HDG-V/S / TRK-FPA; 100/1000 and metric).</summary>
public sealed record A350FcuWindowSpec(string Title, string ValueLabel, string Hint, string TypedRow, string PushRow, string PullRow,
    string Window, IReadOnlyList<(string Name, string Row, bool Toggle)> Extras);

/// <summary>
/// The A380's FCU Set Values windows on the A350 (input Ctrl+S speed, Ctrl+H heading, Ctrl+A altitude, Ctrl+V V/S or FPA):
/// type a value and press Enter (the typed row: the aircraft's own knob lands it and the window it then shows is said),
/// push or pull the knob, and press the pushbuttons beside it. The window's line stays current in a read-only box.
/// </summary>
public sealed class A350FcuValueWindow : A350FcuWindowBase
{
    private readonly A350FcuWindowSpec _spec;
    private readonly TextBox _value;
    private readonly TextBox _shown;

    public static A350FcuWindowSpec For(HotkeyAction action) => action switch
    {
        HotkeyAction.FCUSetSpeed => new("Set FCU Speed", "Speed", "100 to 399 knots, or a Mach number such as 0.85",
            "A350_FCU_SPEED_SET", "A350_FCU_SPD_KNOB_PUSH", "A350_FCU_SPD_KNOB_PULL", "Speed window",
            new[] { ("SPD/MACH", "A350_FCU_SPD_MACH", false) }),
        HotkeyAction.FCUSetHeading => new("Set FCU Heading", "Heading", "0 to 359 degrees",
            "A350_FCU_HEADING_SET", "A350_FCU_HDG_KNOB_PUSH", "A350_FCU_HDG_KNOB_PULL", "Heading window",
            new[] { ("HDG-V/S / TRK-FPA", "A350_FCU_TRK_FPA", false) }),
        HotkeyAction.FCUSetAltitude => new("Set FCU Altitude", "Altitude", "in feet, 100 to 43000",
            "A350_FCU_ALTITUDE_SET", "A350_FCU_ALT_KNOB_PUSH", "A350_FCU_ALT_KNOB_PULL", "Altitude window",
            new[] { ("100/1000", "A350_FCU_ALT_SELECTOR", false), ("Metric altitude", "A350_FCU_METER", true), ("ALT (level off)", "A350_FCU_ALT", false) }),
        _ => new("Set FCU V/S or FPA", "V/S or FPA", "feet per minute (-1500), or a flight path angle in TRK-FPA (-3.0)",
            "A350_FCU_VS_SET", "A350_FCU_VS_KNOB_PUSH", "A350_FCU_VS_KNOB_PULL", "V/S window",
            new[] { ("HDG-V/S / TRK-FPA", "A350_FCU_TRK_FPA", false) }),
    };

    public A350FcuValueWindow(IniBuildsA350Definition aircraft, A350FcuWindowSpec spec, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        _spec = spec;
        Text = spec.Title;
        Size = new Size(420, 210 + 45 * ((spec.Extras.Count + 1) / 2));

        var label = new Label { Text = spec.ValueLabel + ":", Location = new Point(20, 25), Size = new Size(130, 20) };
        _value = new TextBox { Location = new Point(160, 22), Size = new Size(110, 25), TabIndex = 0, AccessibleName = spec.ValueLabel + " value",
            AccessibleDescription = spec.Hint + ". Enter sets it." };
        _value.KeyDown += (s, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            if (!Utils.NumberParsing.TryParseTyped(_value.Text.Trim(), out double v)) { announcer.AnnounceImmediate("Invalid number"); _value.SelectAll(); return; }
            aircraft.PressRow(spec.TypedRow, simConnect, announcer, v);   // the typed row plans the knob and says the window
            _value.SelectAll();
        };
        _shown = StatusBox(spec.Window, 20, 60, 360, 1);
        var push = RowButton(spec.ValueLabel + " knob push", spec.PushRow, 20, 95, 175, 2);
        var pull = RowButton(spec.ValueLabel + " knob pull", spec.PullRow, 205, 95, 175, 3);
        Controls.AddRange(new Control[] { label, _value, _shown, push, pull });

        int tab = 4;
        for (int i = 0; i < spec.Extras.Count; i++)
        {
            var (name, row, toggle) = spec.Extras[i];
            int x = i % 2 == 0 ? 20 : 205, y = 140 + 45 * (i / 2);
            Controls.Add(toggle ? ToggleRowButton(name, row, x, y, 175, tab++) : RowButton(name, row, x, y, 175, tab++));
        }
        var close = new Button { Text = "Close", Location = new Point(140, Size.Height - 85), Size = new Size(140, 35), TabIndex = tab, AccessibleName = "Close" };
        close.Click += (s, e) => Close();
        Controls.Add(close);
        CancelButton = close;
    }

    protected override void OnShownForm() => _value.Focus();

    protected override void RefreshStates()
    {
        base.RefreshStates();
        string line = aircraft.FcuWindowText(_spec.Window, simConnect) is { } shown
            ? MSFSBlindAssist.Aircraft.A350.A350Hotkeys.FcuReadout(_spec.Window, shown) : "FCU not available";
        if (_shown.Text != line) _shown.Text = line;
    }
}
