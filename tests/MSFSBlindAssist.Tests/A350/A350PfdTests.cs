using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The PFD read from the EFIS draw. Fixture: the captain's EFIS on the stand at VCBI, IRs aligned, FDs on,
// 2026-10-27; the sim then read 12.8 ft indicated and 219 degrees magnetic, which calibrate the altitude
// drum's window (y 422) and the heading index (x 359, 7 px a degree).
public class A350PfdTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_captain_pfd_reads_by_area()
    {
        var lines = A350Pfd.Read(Fixture("efis0_fd_stand"), 0);
        Assert.Equal(new[]
        {
            "FMA engagement: 1 FD 2, white", "Selected speed: 100, cyan", "Altitude: 13 feet",
            "Selected altitude: 100, cyan", "QNH: 1013, cyan", "Radio altitude: 0, amber", "Heading: 219",
        }, lines.Take(7));
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
    [InlineData(422f, 0, 0)]       // "00" in the window
    [InlineData(436f, 0, 13)]      // the fixture: "00" 14 px below the window
    [InlineData(414f, 2, 213)]     // "20" 8 px above the window, in the 200s
    public void The_altitude_drum_reads_its_window(float labelY, int hundreds, int feet)
    {
        int label = labelY == 414f ? 20 : 0;
        Assert.Equal(feet, A350Pfd.DrumAltitude(hundreds, label, labelY));
    }
}
