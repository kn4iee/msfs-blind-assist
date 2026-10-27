using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The A350's displays, read by running its own draw code on a copy of its memory
/// (docs/inibuilds-a350.md "The reader"). MainForm owns the service and hands it over while the A350 is
/// the active aircraft.</summary>
public partial class IniBuildsA350Definition
{
    /// <summary>The display reader, or null while it is not running.</summary>
    public A350DisplayService? Displays { get; set; }
}
