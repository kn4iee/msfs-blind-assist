using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.A350;

// The ECAM Control Panel's status display (Ctrl+3, F5) reads the SD page shown, as the A380's
// "System Display Page" box does: the page row's line is the page's name and content.
public class A350SdDisplayTests
{
    [Fact]
    public void The_ecam_control_panel_lists_the_sd_page()
    {
        var def = new IniBuildsA350Definition();
        Assert.Contains(IniBuildsA350Definition.SdPageKey, def.GetPanelDisplayVariables()["ECAM Control Panel"]);
    }

    [Fact]
    public void The_sd_page_line_reads_the_page_shown()
    {
        var def = new IniBuildsA350Definition();
        Assert.True(def.TryGetDisplayOverride(IniBuildsA350Definition.SdPageKey, 8, out var text));
        Assert.Equal("Fuel page\r\nThe A350 displays are not available yet.", text);
    }
}
