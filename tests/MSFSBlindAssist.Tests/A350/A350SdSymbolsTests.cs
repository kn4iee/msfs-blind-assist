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

    // Live, on the stand at VCBI: APU running, APU bleed on, both packs on, EXT 1 and 2 on (2026-10-27).
    private static IReadOnlyList<string> Apu(int page)
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", $"ctr_sdsym_apu_{page}.json")))!;
        return A350SdSymbols.Read(cap.Shapes, page);
    }

    [Fact]
    public void Bleed_reads_the_apu_feeding_both_packs()
    {
        Assert.Equal(new[]
        {
            "Pack 1 outlet: flow, green", "Pack 2 outlet: flow, green",
            "Pack 1 valve: open, green", "Pack 2 valve: open, green",
            "Ram air valve: closed, green", "Crossbleed valve: open, green", "APU bleed valve: open, green",
            "Engine 1 IP valve: closed, amber", "Engine 1 HP valve: closed, amber",
            "Engine 2 IP valve: closed, amber", "Engine 2 HP valve: closed, amber",
            "Engine 1: amber", "Engine 2: amber",
        }, Apu(5));
    }

    [Fact]
    public void Cond_reads_hot_air_and_cargo_valves()
    {
        Assert.Equal(new[]
        {
            "Pack 1 outlet: flow, green", "Pack 2 outlet: flow, green",
            "Hot air 1 valve: open, green", "Hot air 2 valve: open, green", "Ram air valve: closed, green",
            "Hot air 1 outlet: flow, green", "Hot air 2 outlet: flow, green",
            "Forward cargo isolation valve: open, white", "Aft cargo isolation valve: open, white",
            "Bulk cargo isolation valve: open, white",
        }, Apu(6));
    }

    [Fact]
    public void Press_reads_the_outflow_valves_open_on_the_ground()
    {
        Assert.Equal(new[]
        {
            "Avionics extract overboard valve: 99 percent open, green",
            "Forward outflow valve: 100 percent open, green", "Aft outflow valve: 100 percent open, green",
            "Pack 1 inlet: flow, green", "Pack 2 inlet: flow, green",
        }, Apu(7));
    }

    [Fact]
    public void Elec_pages_read_who_supplies_and_the_batteries()
    {
        var dc = Apu(3);
        Assert.Contains("BAT 1: charging, green", dc);
        Assert.Contains("BAT EMER 2: charging, green", dc);
        Assert.Contains("TR 1 input: normal, white", dc);
        var ac = Apu(4);
        Assert.Contains("EXT 1: not supplying, white", ac);
        Assert.Contains("EXT 2: supplying, green", ac);
        Assert.Contains("APU GEN: supplying, green", ac);
        Assert.Contains("RAT: not supplying, white", ac);
        Assert.Contains("AC 1A bus indicator: green", ac);
    }

    [Fact]
    public void Door_reads_every_door_closed()
    {
        var p = Apu(11);
        Assert.Equal(13, p.Count);
        Assert.All(p, l => Assert.EndsWith(": closed, green", l));
        Assert.Contains("Door 4R: closed, green", p);
        Assert.Contains("Bulk cargo door: closed, green", p);
    }

    // F/CTL draws its surfaces as plain bars from a neutral band: ailerons and elevators 3 px a degree, spoilers
    // 1 px a degree up (A350Probe sdmap: a deflection of 1 grew the bar by that much). Live: no hydraulics, so
    // the ailerons and elevators droop and the spoilers are amber.
    [Fact]
    public void Fctl_reads_each_surface_deflection_and_colour()
    {
        var p = Apu(10);
        Assert.Equal(new[]
        {
            "Left outer aileron: 19 degrees down, amber", "Left inner aileron: 19 degrees down, amber",
            "Right inner aileron: 19 degrees down, amber", "Right outer aileron: 19 degrees down, amber",
            "Left elevator: 19 degrees down, green", "Right elevator: 19 degrees down, green",
            "Rudder: centred, amber",
            "Left spoiler 1: retracted, amber", "Left spoiler 2: retracted, amber", "Left spoiler 3: retracted, amber",
            "Left spoiler 4: retracted, amber", "Left spoiler 5: retracted, amber", "Left spoiler 6: retracted, amber",
            "Left spoiler 7: retracted, amber",
            "Right spoiler 1: retracted, amber", "Right spoiler 2: retracted, amber", "Right spoiler 3: retracted, amber",
            "Right spoiler 4: retracted, amber", "Right spoiler 5: retracted, amber", "Right spoiler 6: retracted, amber",
            "Right spoiler 7: retracted, amber",
        }, p.Where(l => l.Contains("aileron") || l.Contains("elevator") || l.Contains("spoiler") || l.StartsWith("Rudder")));
    }

    [Fact]
    public void An_opened_door_reads_open_in_amber()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ctr_sdsym_door_l1_open.json")))!;
        var p = A350SdSymbols.Read(cap.Shapes, 11);
        Assert.Contains("Door 1L: open, amber", p);
        Assert.Contains("Door 1R: closed, green", p);
        Assert.Contains("Nose hatch, left: closed, green", p);
    }

    [Theory]
    [InlineData(776, 779, "neutral")]
    [InlineData(776, 782, "1 degrees down")]
    [InlineData(770, 779, "2 degrees up")]
    public void A_bar_reads_its_deflection_from_the_neutral_band(float y0, float y1, string word)
    {
        Assert.Equal(word, A350SdSymbols.BarWord(new SdBar("x", 56, 71, 776, 779, 3), y0, y1));
    }

    [Theory]
    [InlineData(90, 100)]
    [InlineData(-90, 100)]
    [InlineData(0, 0)]
    [InlineData(180, 0)]
    [InlineData(45, 50)]
    public void A_flap_is_open_by_how_far_it_stands_up(float turn, double percent)
    {
        Assert.Equal(percent, A350SdSymbols.FlapOpenPercent(turn));
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
