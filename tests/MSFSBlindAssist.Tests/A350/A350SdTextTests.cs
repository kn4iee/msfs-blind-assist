using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The ECAM Control Panel's status display reads the SD page shown, as the A380's does: the page's name,
// then its values named (A350SdPage), then the permanent data line. Fixtures: SD pages drawn from a copy
// taken on the stand at VCBI (APU running, engines off), the page set in the copy.
public class A350SdTextTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void The_page_reads_its_name_then_its_rows_with_colours()
    {
        var lines = A350SdText.Build(Fixture("ctr_sd_elec_dc_stand"), 3).Split("\r\n");
        Assert.Equal("Elec DC page", lines[0]);
        Assert.Contains("BAT EMER 1, white", lines);
        Assert.Contains("BAT 1 voltage: 28 V, green", lines);
        Assert.Contains("Permanent data:", lines);
    }

    [Fact]
    public void The_fuel_page_reads_its_quantities()
    {
        var text = A350SdText.Build(Fixture("ctr_sd_fuel_stand"), 8);
        Assert.StartsWith("Fuel page\r\n", text);
        Assert.Contains("Left tank: 43100 LBS, green\r\nCentre tank: 137360 LBS, green\r\nRight tank: 43260 LBS, green", text);
    }

    [Fact]
    public void The_page_reads_its_symbols_after_its_values()
    {
        var lines = A350SdText.Build(Fixture("ctr_sdsym_2"), 2).Split("\r\n").ToList();
        int pressure = lines.IndexOf("Green system pressure: 0 PSI, amber");
        int pump = lines.IndexOf("Engine 1 green pump: LO, amber");
        Assert.True(pressure > 0 && pump > pressure);
        Assert.True(pump < lines.IndexOf("Permanent data:"));
        var ewd = A350EwdText.Build(Fixture("ctr_sdsym_8"), 8).Split('\n');
        Assert.Contains("Crossfeed A valve: closed, green", ewd);
    }

    [Fact]
    public void No_draw_and_no_power_are_said_after_the_page_name()
    {
        Assert.Equal("Fuel page\r\nThe A350 displays are not available yet.", A350SdText.Build(null, 8));
        var dark = new A350DrawCapture("CTR", Array.Empty<DrawnText>(), Array.Empty<DrawnFrame>(), Array.Empty<DrawnElement>(), null, 1);
        Assert.Equal("Fuel page\r\nThe SD is off: no power.", A350SdText.Build(dark, 8));
    }
}
