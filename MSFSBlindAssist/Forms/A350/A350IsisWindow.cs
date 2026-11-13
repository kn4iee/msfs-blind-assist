namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The ISIS's window ("A350 ISIS", output Alt+I; plan 26), as the Fenix's ISIS window: the standby flight display as its status display
/// reads it, then the ISIS panel's keys and its typed QNH, every row of that panel: STD/CHANGE, LS/DIR TO, MENU, minus, MODE, plus, so
/// a pilot can press one and read what it did in the one window. Its own STD is its STD/CHANGE key's alone ([A350-14]); the typed QNH
/// takes the panel's "ISIS QNH" row. A class of its own: windows are tracked one per type.
/// </summary>
public sealed class A350IsisWindow : A350DisplayWindow
{
    /// <summary>The ISIS panel's keys, in its order: the label each shows (the bezel's word, plus and minus spelled out) and its row.</summary>
    internal static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
    {
        new("STD/CHANGE", _ => "A350_ISIS_KNOB_BUTTON_1"), new("LS/DIR TO", _ => "A350_ISIS_LS_1"), new("MENU", _ => "A350_ISIS_MENU_1"),
        new("Minus", _ => "A350_ISIS_MINUS_1"), new("MODE", _ => "A350_ISIS_MODE_1"), new("Plus", _ => "A350_ISIS_PLUS_1"),
    };

    /// <summary>The ISIS panel's typed QNH row.</summary>
    internal const string QnhRow = "A350_ISIS_QNH_SET";

    public A350IsisWindow(A350DisplayFeed feed, IA350WindowRows rows) : base("ISIS", feed, rows)
    {
        AddKeyRow(Keys);
        AddTypedBox("ISIS QNH", _ => QnhRow);
    }
}
