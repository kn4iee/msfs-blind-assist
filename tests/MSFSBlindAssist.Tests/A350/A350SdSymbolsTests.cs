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
            "Forward cargo isolation valve: open, green", "Aft cargo isolation valve: open, green",
            "Bulk cargo isolation valve: open, green",
            "Cockpit air inlet: flow, green", "Forward cabin air inlet: flow, green",
            "Forward cargo air inlet: flow, green", "Aft cargo air inlet: flow, green", "Mixer outlet: flow, green",
            "Avionics overboard valve: 97 percent open, green", "Avionics air valve: open, green",
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

    // F/CTL's rudder (live 2026-10-28, its draw asks the sim's RUDDER POSITION, now answered): a bar growing from its
    // 3 px neutral line (x 409 to 412) toward the deflection, 55 px at full travel, which the sim reads as 35 degrees;
    // quarter and half travel grew 13.5 and 27 px.
    [Theory]
    [InlineData("ctr_fctl_rudder_left", "Rudder: 34 degrees right, green")]      // the bar to x 466
    [InlineData("ctr_fctl_rudder_halfright", "Rudder: 17 degrees left, green")]  // the bar from x 383
    [InlineData("ctr_fctl_rudder_neutral", "Rudder: centred, green")]
    public void Fctl_reads_the_rudders_deflection(string fixture, string rudder)
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", fixture + ".json")))!;
        Assert.Contains(rudder, A350SdSymbols.Read(cap.Shapes, 10));
    }

    // WHEEL: gear and gear doors are sprites (a door's line lies flat when closed), the accumulators are
    // pointers on an arc marked 1.6 and 3.5 (x 1000 PSI), the BRAKES gauge two bars on a scale stretched
    // below 1. Live: parking brake set, no hydraulics, gear down.
    [Fact]
    public void Wheel_reads_gear_accumulators_and_brakes()
    {
        var p = Apu(12);
        Assert.Equal(new[]
        {
            "Nose gear door, left: closed, green", "Nose gear door, right: closed, green", "Nose gear: down and locked, green",
            "Left main gear door: closed, green", "Left main gear: down and locked, green",
            "Right main gear door: closed, green", "Right main gear: down and locked, green",
            "Y accumulator pressure: 3.4 thousand PSI, green", "G accumulator pressure: 3.4 thousand PSI, green",
            "Left brake pressure: 3.5 thousand PSI, green", "Right brake pressure: 3.5 thousand PSI, green",
        }, p.Where(l => !l.Contains("spoiler")));
        Assert.Equal(14, p.Count(l => l.Contains("spoiler: retracted, amber") || l.Contains("spoiler") && l.EndsWith("retracted, amber")));
    }

    // Review 2026-10-29 (readers minor 13): the gear doors' rows of the atlas were matched on every page, so any uncatalogued
    // sprite there read as a gear door "open" wherever it was drawn. They are WHEEL's alone.
    [Fact]
    public void A_gear_door_is_read_on_the_wheel_page_alone()
    {
        Assert.Null(A350SdSymbols.SpriteAt(37, 522));   // a closed nose door's place: no catalogued sprite
        Assert.Equal(new SdSprite("gear door", "green", Word: "closed"), A350SdSymbols.SpriteFor(12, 37, 522));
        Assert.Equal(new SdSprite("gear door", "amber", Word: "open"), A350SdSymbols.SpriteFor(12, 200, 650));
        foreach (int page in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 13 })
            Assert.Null(A350SdSymbols.SpriteFor(page, 37, 522));
    }

    // Review 2026-10-29: the accumulator and brake pressures followed the PC's culture ("3,4 thousand PSI" under de-DE and
    // sv-SE), the only lines across all 126 fixtures that did.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Wheel_pressures_read_the_same_in_every_culture(string culture)
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            var p = Apu(12);
            Assert.Contains("Y accumulator pressure: 3.4 thousand PSI, green", p);
            Assert.Contains("Left brake pressure: 3.5 thousand PSI, green", p);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData(453, 0)]
    [InlineData(395, 1)]
    [InlineData(380, 2)]
    [InlineData(357, 3.5)]
    public void The_brakes_gauge_is_stretched_below_1(float top, double kpsi)
    {
        Assert.Equal(kpsi, A350SdSymbols.BrakeKpsi(top), 1);
    }

    // APU page: live, APU running; APU bleed on then off (the valve turns across its line).
    [Fact]
    public void Apu_reads_its_generator_and_bleed_valve()
    {
        Assert.Equal(new[] { "APU generator supply: supplying, green", "APU generator: white", "APU bleed valve: open, green" }, Apu(9));
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ctr_sdsym_apu_9_bleed_off.json")))!;
        Assert.Contains("APU bleed valve: closed, green", A350SdSymbols.Read(cap.Shapes, 9));
        var text = A350SdPage.Read(A350Ewd.Parse(cap).SystemDisplay, 9);
        Assert.Contains("APU generator load: 54 %, green", text);
        Assert.Contains("APU generator voltage: 233 V, green", text);
        Assert.Contains("APU generator frequency: 398 HZ, green", text);
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
        Assert.Equal(percent, A350SdSymbols.FlapOpenPercent(turn, 0));
    }

    // A flap in a slanted skin (COND's avionics overboard valve, skin at 37 degrees) is open by its angle to that skin.
    [Fact]
    public void A_flap_is_measured_against_its_own_skin()
    {
        Assert.Equal(97, A350SdSymbols.FlapOpenPercent(124, 37));
        Assert.Equal(0, A350SdSymbols.FlapOpenPercent(37, 37));
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
