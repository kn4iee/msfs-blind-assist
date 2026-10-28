using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>One display on demand: its panel in the Displays section, the key of its status line, the gauge
/// (and index) the monitor draws, how its draw reads, and the variable that lights its screen (read below 0.5 the
/// screen is dark; null for one with no such variable).</summary>
public sealed record A350DisplayPanel(string Panel, string Key, string Gauge, int Index, Func<A350DrawCapture, IReadOnlyList<string>> Read,
    string? Power = null);

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

    // Each screen's power as its backlight reads it (the cockpit model's emissive codes): the variable follows the
    // screen's switch and its bus (live 2026-10-28: the captain's outer display switched off read 0 with its brightness
    // at 100), the RMPs and the FCU their brightness knob, whose 0 is OFF. The A350 keeps drawing a dark screen in
    // memory, so without this a dark PFD was read as if lit.
    public static readonly IReadOnlyList<A350DisplayPanel> DisplayPanels = new A350DisplayPanel[]
    {
        new("Captain PFD", PfdCaptainKey, "EFIS", 0, c => A350Pfd.Read(c, 0), "INI_LEFT_EFIS_POWERED"),
        new("First Officer PFD", PfdFirstOfficerKey, "EFIS", 1, c => A350Pfd.Read(c, 1), "INI_RIGHT_EFIS_POWERED"),
        new("Captain ND", "A350_DISP_ND_CPT", "EFIS", 0, c => A350GroundDisplays.ReadNd(c, 0), "INI_LEFT_EFIS_POWERED"),
        new("First Officer ND", "A350_DISP_ND_FO", "EFIS", 1, c => A350GroundDisplays.ReadNd(c, 1), "INI_RIGHT_EFIS_POWERED"),
        // The MFD as lines (A350MfdPage); its window operates them.
        new("Captain MFD", "A350_DISP_MFD_CPT", "MFD", 0, c => A350MfdPage.Read(c, 0).Select(l => l.Spoken).ToList(), "INI_FMS_POWERED"),
        new("First Officer MFD", "A350_DISP_MFD_FO", "MFD", 0, c => A350MfdPage.Read(c, 1).Select(l => l.Spoken).ToList(), "INI_FMS_POWERED"),
        new("FCU display", "A350_DISP_FCU", "FCU", 0, A350GroundDisplays.ReadFcu, "INI_FCU_DISPLAY_BRT"),
        new("RMP 1 display", "A350_DISP_RMP_1", "RMP", 0, A350GroundDisplays.ReadRmp, "INI_POTENTIOMETER_93"),
        new("RMP 2 display", "A350_DISP_RMP_2", "RMP", 1, A350GroundDisplays.ReadRmp, "INI_POTENTIOMETER_94"),
        new("RMP 3 display", "A350_DISP_RMP_3", "RMP", 2, A350GroundDisplays.ReadRmp, "INI_POTENTIOMETER_95"),
        new("Standby flight display", "A350_DISP_SFD", "SFD", 0, A350GroundDisplays.ReadSfd, "INI_ISIS1_POWERED"),
        new("Standby navigation display", "A350_DISP_SND", "SND", 0, A350GroundDisplays.ReadSnd, "INI_ISIS2_POWERED"),
        new("BAT display", "A350_DISP_BAT", "BAT", 0, A350GroundDisplays.ReadBat),
    };

    /// <summary>What a dark screen reads, as a sighted pilot sees it.</summary>
    internal const string DarkScreen = "Dark: its screen is off or has no power.";

    /// <summary>Whether a display's screen is dark: its power variable read below 0.5. Unread, it is taken as lit.</summary>
    internal bool IsDark(A350DisplayPanel d) =>
        d.Power != null && _sim is { } sim && Cached(sim, KeyForInput(d.Power)) is double lit && lit < 0.5;

    /// <summary>Whether a display panel's screen is dark (<see cref="IsDark"/>), for a window that shows it.</summary>
    internal bool IsScreenDark(string panel) => DisplayPanels.FirstOrDefault(d => d.Panel == panel) is { } d && IsDark(d);

    /// <summary>The display reader, or null while it is not running.</summary>
    public A350DisplayService? Displays { get; set; }

    /// <summary>The background E/WD call-outs (and the Alt+E window's source), or null.</summary>
    public A350EcamMonitor? Ecam { get; set; }

    /// <summary>The background display reads (FMA call-outs, each display's latest draw), or null.</summary>
    public A350PfdMonitor? Pfd { get; set; }

    /// <summary>A display as its status display reads it: its name, then its lines.</summary>
    internal string DisplayText(A350DisplayPanel d)
    {
        if (IsDark(d)) return d.Panel + "\r\n" + DarkScreen;
        if (Pfd?.Latest(d.Gauge, d.Index) is not { } capture) return d.Panel + "\r\nThe A350 displays are not available yet.";
        var lines = d.Read(capture);
        return lines.Count == 0 ? d.Panel + "\r\nOff: no power, or nothing shown." : d.Panel + "\r\n" + string.Join("\r\n", lines);
    }
}
