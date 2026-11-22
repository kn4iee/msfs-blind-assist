using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>Aircraft-menu handler for the iniBuilds A330 (MSFS 2024).</summary>
public partial class MainForm
{
    private void IniA330MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA330Definition());
    }

    /// <summary>Starts the A330's display readers; a no-op unless the current aircraft is the A330 (plan 02 implements it in
    /// MainForm.IniBuilds.Displays.cs; nothing happens until then).</summary>
    partial void StartIniA330Displays();
}
