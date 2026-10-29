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

    // Live in the climb on BASU1D (2026-10-28): the ND's TO waypoint block, top right, is the name (white), the track
    // to it (green, x 759), its distance a line under ("6." and a smaller "2", then a cyan NM) and the ETA under that.
    // The distance read as a line of its own split at its point ("6., green; 2, green; NM, cyan"), the ETA as another.
    [Fact]
    public void The_nd_reads_its_to_waypoint_block_as_one_line()
    {
        var nd = A350GroundDisplays.ReadNd(JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "efis0_climb_bi614.json")))!, 0);
        Assert.Contains("To waypoint: BI614, white; track 296°, green; 6.2 NM, green; ETA 07:20, green", nd);
        Assert.DoesNotContain(nd, l => l.StartsWith("6.", StringComparison.Ordinal) || l.StartsWith("07:20", StringComparison.Ordinal));
    }

    // The vertical display (live 2026-10-28): the selected altitude beyond its altitude range is a cyan value at its top
    // left (x 105, y 786; "FL 400" in STD) or bottom left (y 1010). It read inside the range line ("40000 0 2.5 5 7.5 10")
    // or as the lowest altitude label, or bare; the scale's "FL" (white, x 58) read as a line of its own, and the safe
    // altitude (MSA or MORA, white, its value above it) as two.
    [Theory]
    [InlineData("efis0_takeoff_ready", "Vertical display selected altitude: 40000, cyan", "Vertical display range: 0 2.5 5 7.5 10, cyan", "Vertical display altitudes: 3000 2000 1000 0, white")]
    [InlineData("efis0_climb_bi614", "Vertical display selected altitude: FL 400, cyan", "Vertical display range: 0 2.5 5 7.5 10, cyan", "Vertical display altitudes: FL 190 180 170 160, white")]
    [InlineData("efis0_ls_downwind_omdb", "Vertical display selected altitude: 4000, cyan", "Vertical display range: 0 1.25 2.5 3.75 5, cyan", "Vertical display altitudes: 6000 5500 5000 4500, white")]
    public void The_vertical_display_reads_its_selected_altitude_apart(string fixture, string selected, string range, string altitudes)
    {
        var nd = A350GroundDisplays.ReadNd(JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", fixture + ".json")))!, 0);
        Assert.Contains(selected, nd);
        Assert.Contains(range, nd);
        Assert.Contains(altitudes, nd);
        Assert.DoesNotContain(nd, l => l is "FL, white" or "FL 400, cyan" or "4000, cyan");
    }

    [Theory]
    [InlineData("efis0_ls_downwind_omdb", "MSA: 38, magenta")]
    [InlineData("pfd_climb_std_fl400", "MORA: 16, magenta")]
    public void The_vertical_displays_safe_altitude_reads_with_its_name(string fixture, string safe)
    {
        var nd = A350GroundDisplays.ReadNd(JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", fixture + ".json")))!, 0);
        Assert.Contains(safe, nd);
        Assert.DoesNotContain(nd, l => l is "MSA, white" or "MORA, white" or "3 8, white" or "1 6, white");
    }

    [Fact]
    public void The_fcu_reads_its_windows_and_both_baros()
    {
        var fcu = A350GroundDisplays.ReadFcu(Fixture("FCU"));
        Assert.Equal(new[]
        {
            "Speed: 100 KT, white", "Heading: 218, white", "Altitude: 100, white", "Vertical speed: ----, white",
            "Captain baro: QNH 1013, white", "First officer baro: QNH 1013, white",
        }, fcu);
    }

    // The FCU's altitude window draws five digits ("00100") and the V/S window its hundreds' zeros small, as "oo" (live
    // 2026-10-28: "+00oo"): they read as the numbers they show, as the FCU windows say them ("V/S +1200", "0").
    [Theory]
    [InlineData("+12oo", "Vertical speed: +1200, white")]
    [InlineData("-07oo", "Vertical speed: -700, white")]
    [InlineData("+00oo", "Vertical speed: 0, white")]
    public void The_fcu_reads_its_vertical_speed_as_a_number(string drawn, string read)
    {
        var cap = Fixture("FCU");
        var fcu = A350GroundDisplays.ReadFcu(cap with { Texts = cap.Texts.Select(t => t.Text == " ----" ? t with { Text = drawn } : t).ToList() });
        Assert.Contains(read, fcu);
        Assert.Contains("Altitude: 100, white", fcu);
    }

    // sv-SE's negative sign is U+2212: the V/S reads with a plain minus, as the FCU windows say it.
    [Fact]
    public void The_fcu_vertical_speed_is_invariant_under_a_swedish_culture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var cap = Fixture("FCU");
            Assert.Contains("Vertical speed: -700, white",
                A350GroundDisplays.ReadFcu(cap with { Texts = cap.Texts.Select(t => t.Text == " ----" ? t with { Text = "-07oo" } : t).ToList() }));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
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
