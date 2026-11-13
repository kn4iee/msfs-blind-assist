namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The SND's window ("A350 SND", output Alt+Shift+N, the owner's key: the ND is Alt+N; plan 26): the standby navigation display as its
/// status display reads it, then every key of its panel: STD/CHANGE, LS/DIR TO, MENU, minus, MODE, plus and its knob. The SND is an OIS
/// option (STANDBY NAV DISPLAY): not fitted, the window's one row says so. A class of its own: windows are tracked one per type.
/// </summary>
public sealed class A350SndWindow : A350DisplayWindow
{
    /// <summary>The SND panel's keys, in its order: the label each shows and its row.</summary>
    internal static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
    {
        new("STD/CHANGE", _ => "A350_ISIS_KNOB_BUTTON_2"), new("LS/DIR TO", _ => "A350_ISIS_LS_2"), new("MENU", _ => "A350_ISIS_MENU_2"),
        new("Minus", _ => "A350_ISIS_MINUS_2"), new("MODE", _ => "A350_ISIS_MODE_2"), new("Plus", _ => "A350_ISIS_PLUS_2"),
        new("Knob up", _ => "A350_ISIS_KNOB_2#INC"), new("Knob down", _ => "A350_ISIS_KNOB_2#DEC"),
    };

    public A350SndWindow(A350DisplayFeed feed, IA350WindowRows rows) : base("SND", feed, rows)
    {
        AddKeyRow(Keys);
    }
}
