namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The PFD's window ("A350 PFD", output Alt+P; plan 26), as the PMDG 737's PFD window: the side's PFD as its status display reads it
/// (the FMA, the speeds, attitude, altitude, heading, the flags and messages, with their colours), then the controls that change it,
/// each the panel's own row: the FCU's FD, the side's EFIS LS and VV, its baro knob's STD push and QNH pull, its baro unit and the
/// typed QNH. A side box picks the captain's or the first officer's PFD and EFIS. A class of its own: windows are tracked one per type.
/// </summary>
public sealed class A350PfdWindow : A350DisplayWindow
{
    private static string Side(int side) => side == 0 ? "LEFT" : "RIGHT";

    /// <summary>The window's keys, in the EFIS panel's order after the FD: the label each shows and its row on each side. The FD is the
    /// FCU's one pushbutton for both sides, as the A350's FCU windows press it.</summary>
    internal static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
    {
        new("FD", _ => "A350_FCU_FD", Toggle: true),
        new("LS", s => $"A350_EFIS_LS_{Side(s)}", Toggle: true),
        new("VV", s => $"A350_EFIS_VV_{Side(s)}", Toggle: true),
        new("Baro STD", s => $"A350_EFIS_QNH_PRESSURE_SELECTOR_{Side(s)}_PUSH"),
        new("Baro QNH", s => $"A350_EFIS_QNH_PRESSURE_SELECTOR_{Side(s)}_PULL"),
        new("Baro unit", s => $"A350_EFIS_QNH_UNIT_SELECTOR_{Side(s)}"),
    };

    /// <summary>The side's typed QNH row, the EFIS panel's "QNH (in Capt unit)" and "QNH (in F/O unit)".</summary>
    internal static string QnhRow(int side) => side == 0 ? "A350_CAPT_QNH_SET" : "A350_FO_QNH_SET";

    public A350PfdWindow(A350DisplayFeed feed, IA350WindowRows rows) : base("PFD", feed, rows, sides: 2)
    {
        AddKeyRow(Keys);
        AddTypedBox("QNH", QnhRow);
    }
}
