namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The SD's window ("A350 SD", output Alt+S; plan 26), as the Fenix's: the page the SD shows with its values named, its symbols and the
/// permanent data (<c>A350SdText</c>), then the ECAM control panel's page keys, ENG to VIDEO, and the SD knob, each the panel's own row,
/// so a pilot can show a page and read it in the one window. They are buttons, never a combo box, which would press a key at every page
/// the reader arrows past (the Fenix SD window's reason). A class of its own: windows are tracked one per type.
/// </summary>
/// <remarks>The SD's draw names no page: its rows are read by the page the A350's variable says (INI_ECAM_ACTIVE_PAGE). After a key
/// the new page can be drawn before that variable arrives, and would be read by the old page's fields, so the window keeps the rows it
/// shows until the page has changed, at most <see cref="SettleMs"/>, and takes its fresh draw then (plan 26's review).</remarks>
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

    /// <summary>How often the page is looked at after a key, and how long at most the rows wait for it (a key of the page shown, or
    /// a variable that never comes, holds them no longer).</summary>
    internal const int PollMs = 100, SettleMs = 1500;

    private readonly Func<double?> _page;
    private double? _pageAtPress;
    private bool _settling;

    /// <param name="page">The page the SD shows, as the A350's variable says it, or null while unread.</param>
    public A350SdWindow(A350DisplayFeed feed, IA350WindowRows rows, Func<double?> page) : base("SD", feed, rows)
    {
        _page = page;
        AddKeyRow(Keys);
    }

    protected override void Pressed()
    {
        _pageAtPress = _page();
        _settling = true;
    }

    protected override async Task SettleAsync()
    {
        try
        {
            for (int waited = 0; waited < SettleMs && _page() == _pageAtPress; waited += PollMs)
                await Pause(PollMs, CancellationToken.None);
        }
        finally { _settling = false; }
    }

    protected override bool HoldsRows => _settling && _page() == _pageAtPress;
}
