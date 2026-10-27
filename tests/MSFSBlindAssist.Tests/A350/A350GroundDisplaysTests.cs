using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The other displays read by name, from live draws on the stand at VCBI (2026-10-27): engines and APU running,
// IRs aligned, no flight plan; heading 219, 12.8 ft indicated.
public class A350GroundDisplaysTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ground_" + name + ".json")))!;

    [Fact]
    public void The_nd_reads_its_corners_messages_and_vertical_display()
    {
        var nd = A350GroundDisplays.ReadNd(Fixture("EFIS_0"), 0);
        Assert.Contains("Ground speed: 0, green", nd);
        Assert.Contains("True airspeed: ---, green", nd);
        Assert.Contains("Wind: --- ---, green", nd);
        Assert.Contains("To waypoint: PPOS, white", nd);
        Assert.Contains("TCAS STBY, white", nd);
        Assert.Contains("NAV PRIMARY, white", nd);
        Assert.Contains("Vertical display range: 0 1.25 2.5 3.75 5, cyan", nd);
        Assert.Contains("Vertical display altitudes: 1500 1000 500 0, white", nd);
        Assert.DoesNotContain(nd, l => l.Contains("MTL"));   // drawn off the screen
    }

    [Fact]
    public void The_fcu_reads_its_windows_and_both_baros()
    {
        var fcu = A350GroundDisplays.ReadFcu(Fixture("FCU"));
        Assert.Equal(new[]
        {
            "Speed: 100 KT, white", "Heading: 218, white", "Altitude: 00100, white", "Vertical speed: ----, white",
            "Captain baro: QNH 1013, white", "First officer baro: QNH 1013, white",
        }, fcu);
    }

    [Fact]
    public void An_rmp_reads_each_radio_active_and_standby()
    {
        var rmp = A350GroundDisplays.ReadRmp(Fixture("RMP_0"));
        Assert.Contains("VHF1: active 122.800, white; standby 123.450, cyan", rmp);
        Assert.Contains("VHF2: active 121.500, white; standby 124.850, white", rmp);
        Assert.Contains("VHF3: active DATA, white; standby 121.500, white", rmp);
        Assert.Contains("SQUAWK : 2000 - STBY, white", rmp);
    }

    // The overhead BAT display: seven-segment digits with the decimal point drawn apart (live: BAT 1 selected).
    [Fact]
    public void The_bat_display_reads_the_selected_battery_voltage()
    {
        Assert.Equal(new[] { "Battery voltage: 28.2 V, grey" }, A350GroundDisplays.ReadBat(Fixture("BAT")));
        Assert.Equal(new[] { "Battery voltage display: blank" },
            A350GroundDisplays.ReadBat(new A350DrawCapture("BAT", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1)));
    }

    [Fact]
    public void The_standby_displays_read_baro_heading_and_position()
    {
        var sfd = A350GroundDisplays.ReadSfd(Fixture("SFD"));
        Assert.Contains("Baro: 1013, cyan", sfd);
        Assert.Contains("Heading: 220", sfd);
        Assert.Contains("Position: N07 10.6 E079 53.1, green", sfd);
        Assert.Contains("Position source: GPIR3, white", sfd);
        var snd = A350GroundDisplays.ReadSnd(Fixture("SND"));
        Assert.Contains("Ground speed: 0, green", snd);
        Assert.Contains("Position: N07 10.6 E079 53.1, green", snd);
        Assert.Contains("Heading source: IR3, white", snd);
    }
}
