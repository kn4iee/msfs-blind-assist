using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A350's display windows (plan 26), the Fenix's and the PMDG 737's: each display key opens a live window of its display with the
/// controls that change it (Forms/A350/A350DisplayWindow.cs). This part gives each window its feed, the background monitor's draws and
/// the reader its status display uses, and opens it on its key (<see cref="A350.A350Hotkeys.DisplayWindows"/>).
/// </summary>
public partial class IniBuildsA350Definition
{
    /// <summary>A display's feed from its status display entries (<see cref="DisplayPanels"/>), one per side in order: the monitor's
    /// latest draw of the entry's gauge (which keeps it drawn while the window reads it), a fresh draw, its screen's own power, and its
    /// status display's reader.</summary>
    internal Forms.A350.A350DisplayFeed DisplayFeed(params string[] panels)
    {
        var sides = panels.Select(p => DisplayPanels.Single(d => d.Panel == p)).ToArray();
        A350DisplayPanel Of(int side) => sides[Math.Clamp(side, 0, sides.Length - 1)];
        return new Forms.A350.A350DisplayFeed(
            side => Pfd?.Latest(Of(side).Gauge, Of(side).Index),
            async side => Displays == null ? null : (await Displays.DrawAsync((Of(side).Gauge, Of(side).Index)))?.FirstOrDefault(),
            side => IsDark(Of(side)),
            (capture, side) => ReadDisplay(Of(side), capture));
    }

    /// <summary>The CTR's latest draw (the E/WD, the SD and the mailbox), drawn every tick.</summary>
    private A350.Display.A350DrawCapture? CtrDraw() => Pfd?.Latest("CTR", 0);

    /// <summary>The page the SD shows (INI_ECAM_ACTIVE_PAGE), or null while unread.</summary>
    private double? SdPageShown() => _sim is { } sim ? Cached(sim, SdPageKey) : null;

    /// <summary>A CTR display's feed: the CTR's draws, dark while the CTR draws no power, and <paramref name="read"/>'s lines.</summary>
    private Forms.A350.A350DisplayFeed CtrFeed(Func<A350.Display.A350DrawCapture, string> read, bool needsPage) => new(
        _ => needsPage && SdPageShown() is null ? null : CtrDraw(),
        async _ => Displays == null ? null : (await Displays.DrawAsync(("CTR", 0)))?.FirstOrDefault(),
        _ => CtrDraw() is { } ctr && !A350.Display.A350Ewd.Parse(ctr).Powered,
        (capture, _) => TextLines(read(capture)));

    /// <summary>The E/WD window's feed: the CTR as <c>A350EwdText</c> reads it, with the SD's page when known (the Alt+E text before).</summary>
    internal Forms.A350.A350DisplayFeed EwdFeed() => CtrFeed(c => A350.Display.A350EwdText.Build(c, SdPageShown()), needsPage: false);

    /// <summary>The SD window's feed: the page the SD shows as <c>A350SdText</c> reads it; not read while the page is unread.</summary>
    internal Forms.A350.A350DisplayFeed SdFeed() => CtrFeed(c => A350.Display.A350SdText.Build(c, SdPageShown() ?? 0), needsPage: true);

    /// <summary>A reader's text as the window's rows: one a line, blank lines left out.</summary>
    private static IReadOnlyList<string> TextLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();


    /// <summary>Opens, or brings forward, the window of <paramref name="display"/> ("PFD" and the rest).</summary>
    private void ShowDisplayWindow(string display, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        var rows = new Forms.A350.A350WindowRows(this, simConnect, announcer);
        switch (display)
        {
            case "PFD":
                ShowTrackedWindow(() => new Forms.A350.A350PfdWindow(DisplayFeed("Captain PFD", "First Officer PFD"), rows), w => w.ShowForm());
                break;
            case "E/WD":
                ShowTrackedWindow(() => new Forms.A350.A350EwdWindow(EwdFeed(), rows), w => w.ShowForm());
                break;
            case "SD":
                ShowTrackedWindow(() => new Forms.A350.A350SdWindow(SdFeed(), rows), w => w.ShowForm());
                break;
            // The ND with every function of it (the owner: "the nd plus all its functions in one place"): its airport map, BTV, mode
            // and range, every EFIS button. Ctrl+Shift+B, the A380's airport map key, no longer opens it.
            case "ND":
                ShowTrackedWindow(
                    () => new Forms.A350.A350NdWindow(side => Pfd?.Latest("EFIS", side),
                        async side => Displays == null ? null : (await Displays.DrawAsync(("EFIS", side)))?.FirstOrDefault(),
                        side => IsScreenDark(side == 0 ? "Captain ND" : "First Officer ND"),
                        new A350.Display.A350NdInput(simConnect.ExecuteCalculatorCodeUnique), announcer,
                        (side, mode, position) =>
                        {
                            ArmWindowEcho(Forms.A350.A350NdWindow.KnobRow(side, mode));
                            PressRow(Forms.A350.A350NdWindow.KnobRow(side, mode), simConnect, announcer, position);
                        },
                        (side, mode) => ToggleState(Forms.A350.A350NdWindow.KnobRow(side, mode), simConnect),
                        // BTV's arming (plan 08): the A/BRK pushbutton, the Autobrake panel's own row.
                        () => PressRow("A350_LDG_AUTO_BRK", simConnect, announcer, 1),
                        rows),
                    w => { w.Show(); w.BringToFront(); w.Activate(); });
                break;
        }
    }
}
