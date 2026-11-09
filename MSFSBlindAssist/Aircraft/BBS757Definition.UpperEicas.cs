using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The 757's upper EICAS (plan 24): its monitor (the gate variables come from the reader through the registry, never
/// registered by hand), its three Ctrl+M rows, and Alt+E, which opens the "Upper EICAS" window or, when the reader cannot
/// run, falls back to the AI read (S15). The key speaks nothing itself: the window's title and list are what the screen
/// reader says (CORE-7).
/// </summary>
public partial class BBS757Definition
{
    /// <summary>The upper EICAS: spoken as its messages change, and the window Alt+E opens.</summary>
    internal Bbs757DisplayMonitor<Bbs757UpperEicasLayout, Bbs757UpperEicasReading> UpperEicas { get; } =
        new(new Bbs757UpperEicasDisplay());

    private BBS757DisplayForm? _upperEicasForm;

    partial void RegisterUpperEicas(Bbs757DisplayRegistry r)
    {
        r.Add(UpperEicas);
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.MessagesKey, Bbs757UpperEicasDisplay.MessagesLabel));
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.ThrustKey, Bbs757UpperEicasDisplay.ThrustLabel));
        r.AddMonitorRow(new MonitorRow(Bbs757UpperEicasDisplay.ColorsKey, Bbs757UpperEicasDisplay.ColorsLabel));
        r.OnKey(HotkeyAction.ReadDisplayUpperECAM, context =>
        {
            if (UpperEicas.KeyFallsBackToAi()) return context.AiRead();
            context.Hotkeys.ExitOutputHotkeyMode();
            if (_upperEicasForm is not { IsDisposed: false } form)
            {
                form = _upperEicasForm = new BBS757DisplayForm("Upper EICAS", "Upper EICAS", UpperEicas.View);
                // The seam runs inside the first GetVariables, so the handler, not the seam, makes and owns the form.
                Displays.OwnWindow(form);
            }
            form.ShowForm();
            return true;
        });
    }
}
