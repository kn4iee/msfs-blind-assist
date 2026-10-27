using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFD read from the EFIS draw. Fixtures: the captain's EFIS on the stand at VCBI, IRs aligned, FDs on, and
// in a climb through 2940 ft (2026-10-27). The altitude window is at y 389 (the tape's labels, 0.35 px a foot,
// and the drum's tens, 21.75 px per 20 ft, agree there, in flight and on the stand); 219 degrees magnetic put
// the heading index at x 359, 7 px a degree.
public class A350PfdTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_captain_pfd_reads_by_area()
    {
        var lines = A350Pfd.Read(Fixture("efis0_fd_stand"), 0);
        foreach (var l in new[]
        {
            "FMA engagement: 1 FD 2, white", "Selected speed: 100, cyan", "Altitude: 43 feet",
            "Selected altitude: 100, cyan", "QNH: 1013, cyan", "Radio altitude: 0, amber", "Heading: 219",
            "Speed: 32 knots",   // on the stand the tape rests on its 30 kt floor
        })
            Assert.Contains(l, lines);
        Assert.Contains(lines, l => l.StartsWith("T.O THS FOR, white; 28.5"));
        Assert.DoesNotContain(lines, l => l.Contains("010") || l.Contains("80, green"));   // tape and drum labels
    }

    [Fact]
    public void The_first_officer_pfd_is_the_left_half()
    {
        var lines = A350Pfd.Read(Fixture("efis1_fd_stand"), 1);
        Assert.Contains("FMA engagement: 1 FD 2, white", lines);
        Assert.Contains("QNH: 1010, cyan", lines);
    }

    [Fact]
    public void A_flag_reads_with_its_colour()
    {
        var lines = A350Pfd.Read(Fixture("efis0_aligning"), 0);
        Assert.Contains("ATT, red", lines);
    }

    [Theory]
    [InlineData(0, 405f, 0, 436f, 43)]       // the stand
    [InlineData(30, 369f, 40, 390f, 2941)]   // the climb
    [InlineData(30, 369f, 80, 346f, 2940)]   // a tens label away from the window still lands in the right hundred
    public void The_altitude_reads_the_tape_and_the_drum(int tapeHundreds, float tapeY, int tens, float tensY, int feet)
    {
        Assert.Equal(feet, A350Pfd.Altitude(tapeHundreds, tapeY, tens, tensY));
    }

    [Fact]
    public void In_the_climb_the_pfd_reads_altitude_heading_and_vertical_speed()
    {
        var lines = A350Pfd.Read(Fixture("efis0_climb"), 0);
        Assert.Contains("Altitude: 2941 feet", lines);
        Assert.Contains("Heading: 31", lines);
        Assert.Contains("Vertical speed: 1400 feet per minute up, green", lines);
        Assert.Contains("FMA autothrust: SPEED, green", lines);
        Assert.Contains("FMA vertical: ALT CRZ*, green", lines);
        Assert.Contains("FMA lateral: HDG, green", lines);
        Assert.Contains("FMA engagement: 1 FD 2; A/THR, white", lines);
    }

    // Speed, pitch and bank are textures: the speed tape's offset (4.875 px a knot) and the horizon under the
    // attitude centre (x 359, y 389; 8.61 px a degree), turned by the bank. Live pairs with the sim: 240.2 kt
    // at -885.2; 3.61 and 6.63 degrees nose up; a left turn read 24.98 degrees against the horizon's 24.72.
    [Fact]
    public void Level_flight_reads_speed_pitch_and_wings_level()
    {
        var lines = A350Pfd.Read(Fixture("efis0_level6000"), 0);
        Assert.Contains("Speed: 240 knots", lines);
        Assert.Contains("Pitch: 3.6 degrees up", lines);
        Assert.Contains("Bank: wings level", lines);
    }

    // The EFIS draws its ND, then its PFD (live, both sides); an ND map label can land on the PFD's half (the
    // display clips it), so each display reads only its own part of the draw.
    [Fact]
    public void An_nd_map_label_never_reads_on_the_pfd()
    {
        var cap = Fixture("efis0_turn_left");
        var nd = new DrawnText("MTL", 1500, 300, 1500, 300, 1, new A350Rgba(0.4f, 0.86f, 1, 1));
        var texts = cap.Texts.ToList();
        texts.Insert(1, nd);
        var lines = A350Pfd.Read(cap with { Texts = texts }, 0);
        Assert.DoesNotContain(lines, l => l.Contains("MTL"));
        Assert.Contains("Heading: 18", lines);   // mid-turn towards 330
    }

    [Fact]
    public void A_turn_reads_its_bank()
    {
        var lines = A350Pfd.Read(Fixture("efis0_turn_left"), 0);
        Assert.Contains("Bank: 25 degrees left", lines);
        Assert.Contains("Speed: 239 knots", lines);
        Assert.Contains(lines, l => l.StartsWith("Pitch: 4."));
    }
}
