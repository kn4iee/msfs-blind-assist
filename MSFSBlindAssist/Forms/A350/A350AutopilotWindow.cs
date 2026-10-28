using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A380's Autopilot panel on the A350 (input Ctrl+P): AP 1 and AP 2, A/THR, LOC, APPR, the FD pushbutton and the
/// FCU's ALT (level off) pushbutton, each labelled with its light as the FCU panel shows it ("AP 1: On"); the A/THR
/// instinctive disconnects on the thrust levers. Every button is the panel's own row.
/// </summary>
public sealed class A350AutopilotWindow : A350FcuWindowBase
{
    public A350AutopilotWindow(IniBuildsA350Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "A350 Autopilot";
        Size = new Size(420, 330);
        var ap1 = RowButton("AP 1", "A350_FCU_AP_1", 20, 20, 175, 0);
        var ap2 = RowButton("AP 2", "A350_FCU_AP_2", 205, 20, 175, 1);
        var loc = RowButton("LOC", "A350_FCU_LOC", 20, 65, 175, 2);
        var appr = RowButton("APPR", "A350_FCU_APPR", 205, 65, 175, 3);
        var fd = ToggleRowButton("FD", "A350_FCU_FD", 20, 110, 175, 4);
        var athr = RowButton("A/THR", "A350_FCU_ATHR", 205, 110, 175, 5);
        var alt = RowButton("ALT (level off)", "A350_FCU_ALT", 20, 155, 175, 6);
        var athrDisc = RowButton("A/THR disconnect", "A350_ENG_THROTTLE_ATHR_DISC_LEFT", 205, 155, 175, 7);
        var close = new Button { Text = "Close", Location = new Point(140, 220), Size = new Size(140, 35), TabIndex = 8, AccessibleName = "Close" };
        close.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { ap1, ap2, loc, appr, fd, athr, alt, athrDisc, close });
        CancelButton = close;
    }

    protected override void OnShownForm() => Controls[0].Focus();
}
