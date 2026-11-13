namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The E/WD's window ("A350 E/WD", output Alt+E; plan 26), as the Fenix's: the CTR as the pilot sees it, the engine block, the warning
/// display with its cursor and action boxes, the SD and the permanent data, each line with its colours (<c>A350EwdText</c>), then the
/// ECAM control panel's keys that act on the warning display, each the panel's own row: T.O CONFIG, each pilot's CLEAR, RCL LAST and
/// ALL, EMER CANC with its guard, ABN and DEFRD PROC, each pilot's C/L, MORE, DISPATCH and each pilot's wheel. The SD's page keys are
/// the SD window's (<see cref="A350SdWindow"/>). A class of its own: windows are tracked one per type.
/// </summary>
public sealed class A350EwdWindow : A350DisplayWindow
{
    /// <summary>The keys, in the ECAM control panel's order of use: the label each shows and its row (both pilots' keys are the panel's,
    /// side-free).</summary>
    internal static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
    {
        new("T.O CONFIG", _ => "A350_ECAM_TO_CONFIG"),
        new("CLEAR left", _ => "A350_ECAM_CLEAR_LEFT"),
        new("CLEAR right", _ => "A350_ECAM_CLEAR_RIGHT"),
        new("RCL LAST", _ => "A350_ECAM_RCL_LAST"),
        new("RCL ALL", _ => "A350_ECAM_RCL_ALL"),
        new("EMER CANC guard", _ => "A350_ECAM_EMER_CANCEL_GUARD", Toggle: true),
        new("EMER CANC", _ => "A350_ECAM_EMER_CANCEL"),
        new("ABN PROC", _ => "A350_ECAM_ABN"),
        new("DEFRD PROC", _ => "A350_ECAM_DEFRD"),
        new("C/L left", _ => "A350_ECAM_CHECKLIST_LEFT"),
        new("C/L right", _ => "A350_ECAM_CHECKLIST_RIGHT"),
        new("MORE", _ => "A350_ECAM_MORE"),
        new("DISPATCH", _ => "A350_ECAM_DISPCH"),
        new("Left wheel up", _ => "A350_ECAM_SCROLL_L#INC"),
        new("Left wheel down", _ => "A350_ECAM_SCROLL_L#DEC"),
        new("Right wheel up", _ => "A350_ECAM_SCROLL_R#INC"),
        new("Right wheel down", _ => "A350_ECAM_SCROLL_R#DEC"),
    };

    public A350EwdWindow(A350DisplayFeed feed, IA350WindowRows rows) : base("E/WD", feed, rows)
    {
        AddKeyRow(Keys, "ECAM keys");
    }
}
