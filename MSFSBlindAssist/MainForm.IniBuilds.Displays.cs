using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

public partial class MainForm
{
    /// <summary>Starts the A330's display reader with the aircraft; a switch away has already stopped the old
    /// definition's reader (<c>OnSwitchedAway</c>), so any other aircraft does nothing here.</summary>
    partial void StartIniA330Displays()
    {
        if (currentAircraft is IniA330Definition a330)
            a330.StartDisplays(announcer);
    }
}
