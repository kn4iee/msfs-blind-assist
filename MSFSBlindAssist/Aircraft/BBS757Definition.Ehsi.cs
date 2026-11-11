using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 26's seam (S16): the 757's Captain EHSI read from BBS752.wasm's memory. It registers the monitor (the display's two
/// position inputs come from it through the registry, so the definition streams each once, hidden where nothing carries it
/// already, [VAR-7]), the "EHSI messages" Ctrl+M row, Alt+N, which opens the one "Captain EHSI" window while the reader can
/// run and falls back to the AI read of the screen when it cannot (S15), and Ctrl+W, which says the active waypoint at once.
/// Opening the window says nothing itself: its title and list are what the screen reader says (CORE-7).
/// </summary>
public partial class BBS757Definition
{
    private Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading>? _ehsi;

    /// <summary>The EHSI's display; its <see cref="Bbs757EhsiDisplay.FullWanted"/> is whether the window is showing.</summary>
    internal Bbs757EhsiDisplay EhsiDisplay { get; init; } = new();

    /// <summary>The EHSI's monitor, on <see cref="EhsiDisplay"/>; a test may start it on a scripted sampler.</summary>
    internal Bbs757DisplayMonitor<Bbs757EhsiLayout, Bbs757EhsiReading> Ehsi
    {
        get => _ehsi ??= new(EhsiDisplay);
        init => _ehsi = value;
    }

    // Made on the first Alt+N that finds the reader running; owned by the registry, so a switch away closes it for good.
    private BBS757DisplayForm? _ehsiWindow;

    /// <summary>
    /// What Ctrl+W says: the monitor's latest reading, whether or not the window is open. While there is none the line
    /// says why: "display dark" while the screen is dark, else the monitor's own reason (its status line's words), and
    /// "no reading yet" only when it has none to give (<see cref="Bbs757DisplayMonitor{TLayout, TReading}.NoReadingReason"/>).
    /// </summary>
    internal string EhsiWaypointLine()
    {
        var latest = Ehsi.Latest;
        return EhsiDisplay.WaypointLine(latest, latest is null ? Ehsi.NoReadingReason : null);
    }

    partial void RegisterEhsi(Bbs757DisplayRegistry r)
    {
        // The route is read only while the window shows; a window hidden by Escape reads light again at once.
        EhsiDisplay.FullWanted = () => _ehsiWindow is { IsDisposed: false, Visible: true };
        r.Add(Ehsi);
        r.AddMonitorRow(new MonitorRow(Bbs757EhsiDisplay.MuteKey, "EHSI messages"));
        r.OnKey(HotkeyAction.ReadDisplayND, context =>
        {
            if (Ehsi.KeyFallsBackToAi()) return context.AiRead();
            context.Hotkeys.ExitOutputHotkeyMode();
            if (_ehsiWindow is not { IsDisposed: false } window)
            {
                window = _ehsiWindow = new BBS757DisplayForm(Bbs757EhsiDisplay.Name, "Captain EHSI", Ehsi.View);
                r.OwnWindow(window);
            }
            window.ShowForm();
            return true;
        });
        // A key press is always answered, so no Ctrl+M row mutes it; it interrupts, as a pilot's own request does.
        r.OnKey(HotkeyAction.ReadNDWaypoint, context =>
        {
            context.Announcer.AnnounceImmediate(EhsiWaypointLine());
            return true;
        });
    }
}
