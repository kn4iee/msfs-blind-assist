using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Plan 25's seam (S16): the 757's lower EICAS read from BBS752.wasm's memory. It registers the monitor (whose gate
/// variables, <c>TURB ENG N2:1</c>, <c>TURB ENG N2:2</c> and <c>L:BBS_ECAM_Mode</c>, the registry takes and puts on the
/// batch), the "EICAS status messages" Ctrl+M row, and Alt+S, which opens the one "Lower EICAS" window while the reader
/// can run and falls back to the AI read of the screen when it cannot (S15).
/// </summary>
public partial class BBS757Definition
{
    // Made on the first Alt+S that finds the reader running; owned by the registry, so a switch away closes it for good.
    private BBS757DisplayForm? _lowerEicasWindow;

    partial void RegisterLowerEicas(Bbs757DisplayRegistry r)
    {
        var monitor = new Bbs757DisplayMonitor<Bbs757LowerEicasLayout, Bbs757LowerEicasReading>(new Bbs757LowerEicasDisplay());
        r.Add(monitor);
        r.AddMonitorRow(new MonitorRow(Bbs757LowerEicasDisplay.MuteKey, Bbs757LowerEicasDisplay.MonitorRowLabel));
        r.OnKey(HotkeyAction.ReadDisplayLowerECAM, ctx =>
        {
            if (monitor.KeyFallsBackToAi()) return ctx.AiRead();
            if (_lowerEicasWindow is not { IsDisposed: false } window)
            {
                window = _lowerEicasWindow = new BBS757DisplayForm(Bbs757LowerEicasDisplay.Name, Bbs757LowerEicasDisplay.Name,
                    monitor.View);
                r.OwnWindow(window);
            }
            window.ShowForm();
            return true;
        });
    }
}
