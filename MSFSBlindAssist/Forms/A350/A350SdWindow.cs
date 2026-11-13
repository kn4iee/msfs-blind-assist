namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The SD's window ("A350 SD", output Alt+S; plan 26), as the Fenix's: the page the SD shows with its values named, its symbols and the
/// permanent data (<c>A350SdText</c>), then the ECAM control panel's page keys, ENG to VIDEO, and the SD knob, each the panel's own row,
/// so a pilot can show a page and read it in the one window. They are buttons, never a combo box, which would press a key at every page
/// the reader arrows past (the Fenix SD window's reason). A class of its own: windows are tracked one per type.
/// </summary>
public sealed class A350SdWindow : A350DisplayWindow
{
    /// <summary>The page keys in the panel's order, then the SD knob: the label each shows and its row.</summary>
    internal static readonly IReadOnlyList<A350WindowKey> Keys = new A350WindowKey[]
    {
        new("ENG", _ => "A350_ECAM_ENG"), new("APU", _ => "A350_ECAM_APU"), new("BLEED", _ => "A350_ECAM_BLEED"),
        new("COND", _ => "A350_ECAM_COND"), new("PRESS", _ => "A350_ECAM_PRESS"), new("DOOR", _ => "A350_ECAM_DOOR"),
        new("EL/AC", _ => "A350_ECAM_ELAC"), new("EL/DC", _ => "A350_ECAM_ELDC"), new("FUEL", _ => "A350_ECAM_FUEL"),
        new("WHEEL", _ => "A350_ECAM_WHEEL"), new("HYD", _ => "A350_ECAM_HYD"), new("F/CTL", _ => "A350_ECAM_FCTL"),
        new("ALL", _ => "A350_ECAM_ALL"), new("STS", _ => "A350_ECAM_STS"), new("VIDEO", _ => "A350_ECAM_VIDEO"),
        new("SD knob push", _ => "A350_ECAM_SD_KNOB_PUSH"),
        new("SD knob up", _ => "A350_ECAM_SD_KNOB#INC"),
        new("SD knob down", _ => "A350_ECAM_SD_KNOB#DEC"),
    };

    public A350SdWindow(A350DisplayFeed feed, IA350WindowRows rows) : base("SD", feed, rows)
    {
        AddKeyRow(Keys, "SD page keys");
    }
}
