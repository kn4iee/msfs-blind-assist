using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The SD's symbols read from what is drawn: each a sprite of EcamElements.png, found by its place on the page
// and told apart by which sprite it is (and how it is turned). Fixtures: every SD page drawn with shapes from
// a copy taken at VCBI on external power, engines and APU off, all pumps off (2026-10-27).
public class A350SdSymbolsTests
{
    private static IReadOnlyList<string> Page(int page)
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", $"ctr_sdsym_{page}.json")))!;
        return A350SdSymbols.Read(cap.Shapes, page);
    }

    [Fact]
    public void Hyd_reads_pumps_valves_and_pressure()
    {
        Assert.Equal(new[]
        {
            "Green system pressure: low, amber", "Yellow system pressure: low, amber",
            "Engine 1 green pump: LO, amber", "Engine 1 green supply valve: open, green",
            "Engine 1 yellow pump: LO, amber", "Engine 1 yellow supply valve: open, green",
            "Engine 2 green pump: LO, amber", "Engine 2 green supply valve: open, green",
            "Engine 2 yellow pump: LO, amber", "Engine 2 yellow supply valve: open, green",
            "Green electric pump: not running, white", "Yellow electric pump: not running, white",
            "Engine 1: amber", "Engine 2: amber",
            "Green reservoir level: 71 percent, green", "Yellow reservoir level: 71 percent, green",
        }, Page(2));
    }

    [Fact]
    public void Fuel_reads_pumps_and_valves()
    {
        Assert.Equal(new[]
        {
            "Left main pump: off, amber", "Left standby pump: off, amber",
            "Center left pump: off, amber", "Center right pump: off, amber",
            "Right main pump: off, amber", "Right standby pump: off, amber",
            "Crossfeed A valve: closed, green", "Crossfeed B valve: closed, green",
            "Engine 1 LP valve: closed, amber", "Engine 2 LP valve: closed, amber",
            "Engine 1: amber", "Engine 2: amber",
        }, Page(8));
    }

    // Live: all six pumps switched on and crossfeed A opened on the stand: the main pumps run, the standby and
    // center pumps wait on standby (A350 logic), the crossfeed valve turns in line.
    [Fact]
    public void Fuel_follows_the_pumps_and_crossfeed_switched_on()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ctr_sdsym_8_pumps_on.json")))!;
        var p = A350SdSymbols.Read(cap.Shapes, 8);
        Assert.Contains("Left main pump: running, green", p);
        Assert.Contains("Left standby pump: standby, green", p);
        Assert.Contains("Center right pump: standby, green", p);
        Assert.Contains("Crossfeed A valve: open, green", p);
        Assert.Contains("Crossfeed B valve: closed, green", p);
    }

    // A valve is open when its bar lies along the flow line, whatever way the sprite is turned.
    [Theory]
    [InlineData(842, 72, 0, 90, "open")]       // bar vertical, flow vertical
    [InlineData(842, 72, 0, 0, "closed")]      // bar vertical, flow horizontal
    [InlineData(898, 72, 0, 0, "open")]        // bar horizontal, flow horizontal
    [InlineData(898, 72, 90, 90, "open")]      // bar horizontal turned 90: vertical
    [InlineData(898, 72, 45, 45, "open")]
    [InlineData(898, 72, 45, -45, "closed")]
    public void A_valve_is_open_with_its_bar_along_the_flow(float sx, float sy, float turn, int flow, string word)
    {
        Assert.Equal(word, A350SdSymbols.ValveWord(A350SdSymbols.SpriteAt(sx, sy)!, turn, flow));
    }

    [Fact]
    public void No_shapes_or_an_unknown_page_read_nothing()
    {
        Assert.Empty(A350SdSymbols.Read(null, 2));
        Assert.Empty(A350SdSymbols.Read(Array.Empty<DrawnShape>(), 99));
    }
}
