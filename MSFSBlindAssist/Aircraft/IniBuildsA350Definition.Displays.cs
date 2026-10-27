using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One display on demand: its panel in the Displays section, the key of its status line, the gauge
/// (and index) the monitor draws, and how its draw reads.</summary>
public sealed record A350DisplayPanel(string Panel, string Key, string Gauge, int Index, Func<A350DrawCapture, IReadOnlyList<string>> Read);

/// <summary>The A350's displays, read by running its own draw code on a copy of its memory
/// (docs/inibuilds-a350.md "The reader"). MainForm owns the service and hands it over while the A350 is
/// the active aircraft.</summary>
public partial class IniBuildsA350Definition
{
    /// <summary>The SD page row (System display page): its status line reads the page shown.</summary>
    internal const string SdPageKey = "A350_SD_PAGE";

    /// <summary>The displays on demand, as the A380's: a Displays section in the Panels list, one panel per
    /// display, whose status display reads it.</summary>
    internal const string DisplaysSection = "Displays";
    internal const string PfdCaptainKey = "A350_DISP_PFD_CPT", PfdFirstOfficerKey = "A350_DISP_PFD_FO";

    public static readonly IReadOnlyList<A350DisplayPanel> DisplayPanels = new A350DisplayPanel[]
    {
        new("Captain PFD", PfdCaptainKey, "EFIS", 0, c => A350Pfd.Read(c, 0)),
        new("First Officer PFD", PfdFirstOfficerKey, "EFIS", 1, c => A350Pfd.Read(c, 1)),
        new("Captain ND", "A350_DISP_ND_CPT", "EFIS", 0, c => A350GroundDisplays.ReadNd(c, 0)),
        new("First Officer ND", "A350_DISP_ND_FO", "EFIS", 1, c => A350GroundDisplays.ReadNd(c, 1)),
        // The MFD as lines (A350MfdPage); its window operates them.
        new("Captain MFD", "A350_DISP_MFD_CPT", "MFD", 0, c => A350MfdPage.Read(c, 0).Select(l => l.Spoken).ToList()),
        new("First Officer MFD", "A350_DISP_MFD_FO", "MFD", 0, c => A350MfdPage.Read(c, 1).Select(l => l.Spoken).ToList()),
        new("FCU display", "A350_DISP_FCU", "FCU", 0, A350GroundDisplays.ReadFcu),
        new("RMP 1 display", "A350_DISP_RMP_1", "RMP", 0, A350GroundDisplays.ReadRmp),
        new("RMP 2 display", "A350_DISP_RMP_2", "RMP", 1, A350GroundDisplays.ReadRmp),
        new("RMP 3 display", "A350_DISP_RMP_3", "RMP", 2, A350GroundDisplays.ReadRmp),
        new("Standby flight display", "A350_DISP_SFD", "SFD", 0, A350GroundDisplays.ReadSfd),
        new("Standby navigation display", "A350_DISP_SND", "SND", 0, A350GroundDisplays.ReadSnd),
        new("BAT display", "A350_DISP_BAT", "BAT", 0, A350GroundDisplays.ReadBat),
    };

    /// <summary>The display reader, or null while it is not running.</summary>
    public A350DisplayService? Displays { get; set; }

    /// <summary>The background E/WD call-outs (and the Alt+E window's source), or null.</summary>
    public A350EcamMonitor? Ecam { get; set; }

    /// <summary>The background display reads (FMA call-outs, each display's latest draw), or null.</summary>
    public A350PfdMonitor? Pfd { get; set; }

    /// <summary>A display as its status display reads it: its name, then its lines.</summary>
    internal string DisplayText(A350DisplayPanel d)
    {
        if (Pfd?.Latest(d.Gauge, d.Index) is not { } capture) return d.Panel + "\r\nThe A350 displays are not available yet.";
        var lines = d.Read(capture);
        return lines.Count == 0 ? d.Panel + "\r\nOff: no power, or nothing shown." : d.Panel + "\r\n" + string.Join("\r\n", lines);
    }
}
