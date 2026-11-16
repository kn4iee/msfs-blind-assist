using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The Alt+E window's text: the upper display (E/WD) as the pilot sees it, the engine block and the warning display, every
// line with its colours, the ECAM cursor and action box said where they are.
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
        Assert.True(Array.IndexOf(lines, "Engine display:") < wd);
    }

    // Gus, 2026-11-16: "The engine display still seems to have the lower display stuff inside of it". The SD page, its
    // symbols and the permanent data line are the lower display's (the SD window, Alt+S): Alt+E reads the upper display
    // alone (the engine block, the mailbox above the warning display, the warning display).
    [Theory]
    [InlineData("ctr_sd_page_8", 8, "Centre tank: 137360 LBS, green")]
    [InlineData("p04_ctr_sd_status_air", 14, "Cabin altitude: 2270 FT, green")]
    [InlineData("ctr_sdsym_8", 8, "Crossfeed A valve: closed, green")]
    public void The_window_reads_the_upper_display_alone(string fixture, double page, string sdLine)
    {
        var capture = Fixture(fixture);
        var lines = A350EwdText.Build(capture).Split('\n');
        Assert.DoesNotContain(sdLine, lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("System display", StringComparison.Ordinal) || l == "Permanent data:");
        Assert.Equal("Warning display:", lines.Last(l => l.EndsWith(" display:", StringComparison.Ordinal)));
        Assert.Contains(sdLine, A350SdText.Build(capture, page).Split("\r\n"));   // still read, in the SD window
    }

    // Review 2026-11-04 (display R1): the STATUS key with nothing for STATUS shows another page under its number; it was
    // said "Status page" over the CRUISE page's values, read as STATUS fields. The SD window names the page shown.
    [Theory]
    [InlineData("p04_ctr_sd_status_air", "Status page, showing the Cruise page", "Cabin altitude: 2270 FT, green")]
    [InlineData("p04_ctr_sd_status_alert_ground", "Status page, showing the Wheel page", "Brake 1 temperature: 535 °C, amber")]
    public void A_status_key_showing_another_page_is_read_as_that_page(string fixture, string title, string line)
    {
        var lines = A350SdText.Build(Fixture(fixture), 14).Split("\r\n");
        Assert.Equal(title, lines[0]);
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
