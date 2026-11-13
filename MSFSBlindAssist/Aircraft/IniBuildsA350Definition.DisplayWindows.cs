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

    private (A350.Display.A350DrawCapture? Capture, A350.Display.A350EwdView? View) _ctrView;   // the UI thread's own

    /// <summary>The CTR's draw parsed once, whoever reads it (whether it is dark, the E/WD's text, the SD's): the last draw's view is kept
    /// by the draw's identity (plan 26's review: each window parsed it twice a tick). UI thread only.</summary>
    internal A350.Display.A350EwdView CtrView(A350.Display.A350DrawCapture capture)
    {
        if (ReferenceEquals(_ctrView.Capture, capture) && _ctrView.View is { } kept) return kept;
        var view = A350.Display.A350Ewd.Parse(capture);
        _ctrView = (capture, view);
        return view;
    }

    /// <summary>The page the SD shows (INI_ECAM_ACTIVE_PAGE), or null while unread.</summary>
    private double? SdPageShown() => _sim is { } sim ? Cached(sim, SdPageKey) : null;

    /// <summary>A CTR display's feed: the CTR's draws, dark while the CTR draws no power, and <paramref name="read"/>'s lines.</summary>
    private Forms.A350.A350DisplayFeed CtrFeed(Func<A350.Display.A350DrawCapture, string> read) => new(
        _ => CtrDraw(),
        async _ => Displays == null ? null : (await Displays.DrawAsync(("CTR", 0)))?.FirstOrDefault(),
        _ => CtrDraw() is { } ctr && !CtrView(ctr).Powered,
        (capture, _) => TextLines(read(capture)));

    /// <summary>The page to read a CTR draw's SD by: the page the draw shows, else the variable (<c>A350SdText.PageToRead</c>).</summary>
    private double? SdPageToRead(A350.Display.A350DrawCapture capture) =>
        A350.Display.A350SdText.PageToRead(CtrView(capture).SystemDisplay, SdPageShown());

    /// <summary>The E/WD window's feed: the CTR as <c>A350EwdText</c> reads it, its SD section by the page its draw shows.</summary>
    internal Forms.A350.A350DisplayFeed EwdFeed() => CtrFeed(c => A350.Display.A350EwdText.Build(c, CtrView(c), SdPageToRead(c)));

    /// <summary>The SD window's feed: the page its draw shows as <c>A350SdText</c> reads it; "not read yet" while neither the draw nor the
    /// variable names a page (never a guessed page).</summary>
    internal Forms.A350.A350DisplayFeed SdFeed() => CtrFeed(c => A350.Display.A350SdText.Build(c, CtrView(c), SdPageToRead(c) ?? 0)) with
    {
        Unavailable = _ => SdPageShown() is null && (CtrDraw() is not { } ctr || SdPageToRead(ctr) is null) ? "not read yet" : null,
    };

    /// <summary>The SND window's feed: the standby navigation display, and "not fitted" while its OIS option (STANDBY NAV DISPLAY) is
    /// off, as its panel's rows refuse then.</summary>
    internal Forms.A350.A350DisplayFeed SndFeed()
    {
        var option = A350.A350Options.For("AIRLINER_ISIS_MENU_2");
        return DisplayFeed("Standby navigation display") with
        {
            Unavailable = _ => option != null && _sim is { } sim && Cached(sim, option.Key) is double fitted && fitted < 0.5
                ? "not fitted (an OIS option)" : null,
        };
    }

    /// <summary>A reader's text as the window's rows: one a line, blank lines left out.</summary>
    private static IReadOnlyList<string> TextLines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();


    /// <summary>The windows list's entries (input Ctrl+Shift+F), as the Fenix's list holds its display windows with its other windows:
    /// every A350 window, the displays first, each naming the key that opens it too.</summary>
    internal IReadOnlyList<Forms.A350.A350WindowEntry> WindowEntries(SimConnectManager simConnect, ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm, HotkeyManager hotkeyManager)
    {
        Forms.A350.A350WindowEntry Display(string name, string description) =>
            new(name, description, () => ShowDisplayWindow(name, simConnect, announcer));
        return new[]
        {
            Display("PFD", "The captain's or the first officer's PFD, live, with the FD, LS, VV, baro and QNH. Also output Alt+P."),
            Display("ND", "The ND with every function of it: the airport map, BTV, mode and range, every EFIS button. Also output Alt+N."),
            Display("E/WD", "The engine and warning display, live, with the ECAM keys that act on it. Also output Alt+E."),
            Display("SD", "The system display, live, with the ECAM page keys and the SD knob. Also output Alt+S."),
            Display("ISIS", "The standby flight display, live, with its keys and QNH. Also output Alt+I."),
            Display("SND", "The standby navigation display, live, with its keys and knob. Also output Alt+Shift+N."),
            new Forms.A350.A350WindowEntry("MFD", "The MFD: the flight management pages, the checklists and the datalink. Also input Shift+M.",
                () => ShowMfdWindow(simConnect, announcer, hotkeyManager, page: null)),
            new Forms.A350.A350WindowEntry("RMP", "The radio management panels: the radios, the transponder. Also input Ctrl+Shift+R.",
                () => ShowRmpWindow(simConnect, announcer)),
            new Forms.A350.A350WindowEntry("Mailbox", "The ATC mailbox: the message it shows and its buttons. Also input Ctrl+Shift+D.",
                () => ShowMailboxWindow(simConnect, announcer)),
            new Forms.A350.A350WindowEntry("FAP", "The flight attendant panel: cabin lights, temperature, doors and slides, water and waste, Cabin Ready.",
                () => (parentForm as MainForm)?.ShowA350FapDialog()),
            new Forms.A350.A350WindowEntry("Printer", "What the printer last printed.",
                () => ShowTrackedWindow(() => new Forms.A350.A350PrinterWindow(A350.A350Printout.ReadAsync), w => w.ShowForm())),
        };
    }

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
            case "ISIS":
                ShowTrackedWindow(() => new Forms.A350.A350IsisWindow(DisplayFeed("Standby flight display"), rows), w => w.ShowForm());
                break;
            case "SND":
                ShowTrackedWindow(() => new Forms.A350.A350SndWindow(SndFeed(), rows), w => w.ShowForm());
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
                    w => w.ShowForm());
                break;
        }
    }
}
