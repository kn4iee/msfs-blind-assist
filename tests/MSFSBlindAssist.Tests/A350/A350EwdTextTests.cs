using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The Alt+E window's text: the whole CTR as the pilot sees it, every line with its colours, the ECAM
// cursor and action box said where they are.
public class A350EwdTextTests
{
    private static A350DrawCapture Fixture(string name) =>
        A350Fixtures.Capture(name + ".json");

    [Fact]
    public void The_window_reads_the_warning_display_with_cursor_and_box()
    {
        var lines = A350EwdText.Build(Fixture("ctr_apu_emer_shutdown")).Split('\n');
        int wd = Array.IndexOf(lines, "Warning display:");
        Assert.True(wd >= 0);
        Assert.Equal("APU EMER SHUTDOWN, amber", lines[wd + 1]);
        Assert.Equal("CLEAR, cyan (selected, to do)", lines[wd + 3]);
        Assert.Contains("Engine display:", lines);
        Assert.Contains("System display:", lines);
        Assert.Contains("Permanent data:", lines);
        Assert.True(Array.IndexOf(lines, "Engine display:") < wd);
    }

    [Fact]
    public void The_system_display_section_names_the_page_and_its_values()
    {
        var lines = A350EwdText.Build(Fixture("ctr_sd_page_8"), 8).Split('\n');
        Assert.Contains("System display: Fuel page", lines);
        Assert.Contains("Centre tank: 137360 LBS, green", lines);
    }

    // Review 2026-11-04 (display R1): the STATUS key with nothing for STATUS shows another page under its number; Alt+E said
    // "Status page" over the CRUISE page's values, read as STATUS fields.
    [Theory]
    [InlineData("p04_ctr_sd_status_air", "System display: Status page, showing the Cruise page", "Cabin altitude: 2270 FT, green")]
    [InlineData("p04_ctr_sd_status_alert_ground", "System display: Status page, showing the Wheel page", "Brake 1 temperature: 535 °C, amber")]
    public void A_status_key_showing_another_page_is_read_as_that_page(string fixture, string title, string line)
    {
        var lines = A350EwdText.Build(Fixture(fixture), 14).Split('\n');
        Assert.Contains(title, lines);
        Assert.Contains(line, lines);
    }

    [Fact]
    public void No_messages_and_no_power_are_said()
    {
        var safety = A350EwdText.Build(Fixture("ctr_powerup_safety_test")).Split('\n');
        Assert.Equal("No messages", safety[Array.IndexOf(safety, "Warning display:") + 1]);
        var dark = new A350DrawCapture("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);
        Assert.Equal("The E/WD and SD are off: no power.", A350EwdText.Build(dark));
        Assert.Equal("The A350 displays are not available yet.", A350EwdText.Build(null));
    }
}
