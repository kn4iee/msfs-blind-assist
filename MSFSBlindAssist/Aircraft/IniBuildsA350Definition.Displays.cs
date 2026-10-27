using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>The A350's displays, read by running its own draw code on a copy of its memory
/// (docs/inibuilds-a350.md "The reader"). MainForm owns the service and hands it over while the A350 is
/// the active aircraft.</summary>
public partial class IniBuildsA350Definition
{
    /// <summary>The SD page row (System display page): its status line reads the page shown.</summary>
    internal const string SdPageKey = "A350_SD_PAGE";

    /// <summary>The PFDs on demand, as the A380's: a Displays section in the Panels list, one panel per side,
    /// whose status display reads that side's PFD (A350Pfd).</summary>
    internal const string DisplaysSection = "Displays", PfdCaptainPanel = "Captain PFD", PfdFirstOfficerPanel = "First Officer PFD";
    internal const string PfdCaptainKey = "A350_PFD_CPT", PfdFirstOfficerKey = "A350_PFD_FO";

    /// <summary>The display reader, or null while it is not running.</summary>
    public A350DisplayService? Displays { get; set; }

    /// <summary>The background E/WD call-outs (and the Alt+E window's source), or null.</summary>
    public A350EcamMonitor? Ecam { get; set; }

    /// <summary>The background PFD reads (FMA call-outs, each side's latest draw), or null.</summary>
    public A350PfdMonitor? Pfd { get; set; }

    /// <summary>A side's PFD as its status display reads it: the side, then its areas (A350Pfd).</summary>
    internal string PfdText(int side)
    {
        var head = side == 0 ? PfdCaptainPanel : PfdFirstOfficerPanel;
        if (Pfd?.Latest(side) is not { } capture) return head + "\r\nThe A350 displays are not available yet.";
        var lines = A350Pfd.Read(capture, side);
        return lines.Count == 0 ? head + "\r\nThe PFD is off: no power." : head + "\r\n" + string.Join("\r\n", lines);
    }
}
