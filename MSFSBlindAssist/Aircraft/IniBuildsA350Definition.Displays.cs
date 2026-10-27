using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The A350's displays, read by running its own draw code on a copy of its memory
/// (docs/inibuilds-a350.md "The reader"). MainForm owns the service and hands it over while the A350 is
/// the active aircraft.</summary>
public partial class IniBuildsA350Definition
{
    /// <summary>The display reader, or null while it is not running.</summary>
    /// <summary>The SD page row (System display page): its status line reads the page shown.</summary>
    internal const string SdPageKey = "A350_SD_PAGE";

    public A350DisplayService? Displays { get; set; }

    /// <summary>The background E/WD call-outs (and the Alt+E window's source), or null.</summary>
    public A350EcamMonitor? Ecam { get; set; }
}
