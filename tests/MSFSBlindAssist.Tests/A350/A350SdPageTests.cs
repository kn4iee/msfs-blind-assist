using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Each SD page read with every value named, as a sighted pilot reads it against the page's static labels
// (the package's Data/Textures/ECAM/*Static.png, drawn at the same coordinates as the values). Fixtures:
// every SD page drawn from one copy taken on the stand at VCBI, APU running, engines off (2026-10-27).
public class A350SdPageTests
{
    private static IReadOnlyList<string> Page(int page, string fixture = "ctr_sd_page_")
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", $"{fixture}{page}.json")))!;
        return A350SdPage.Read(A350Ewd.Parse(cap).SystemDisplay, page);
    }

    // Live, on external power with both emergency batteries off (2026-10-27): what the stand copy never drew.
    private static IReadOnlyList<string> Live(int page) => Page(page, "ctr_sd_live_");

    [Fact]
    public void The_permanent_data_line_names_each_value()
    {
        var cap = JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", "ctr_sd_live_4.json")))!;
        Assert.Equal(new[]
        {
            "TAT: XX °C, amber", "FLT, white", "Flight time: 00:00, green", "Gross weight: -- LBS, cyan",
            "SAT: XX °C, amber", "Gross weight CG: -- %, cyan", "UTC: 16:31:57, green", "Fuel on board: 243700 LBS, green",
        }, A350SdPage.ReadPermanentData(A350Ewd.Parse(cap).PermanentData));
    }

    [Fact]
    public void Live_dc_page_names_an_emergency_battery_that_is_off()
    {
        var p = Live(3);
        Assert.Contains("BAT EMER 1: OFF, white", p);
        Assert.Contains("BAT EMER 2: OFF, white", p);
        Assert.DoesNotContain("OFF, white", p);
    }

    [Fact]
    public void Live_ac_page_names_external_power()
    {
        var p = Live(4);
        Assert.Contains("EXT 1 voltage: 115 V, green", p);
        Assert.Contains("EXT 1 frequency: 400 HZ, green", p);
        Assert.Contains("EXT 2 voltage: 115 V, green", p);
        Assert.Contains("EXT 2 frequency: 400 HZ, green", p);
    }

    [Fact]
    public void Live_door_page_reads_the_doors_then_the_oxygen_column()
    {
        var p = Live(11).ToList();
        Assert.Contains("Door 1L: 1, green", p);
        int lastDoor = p.IndexOf("Door 4R: 4, green");
        Assert.True(lastDoor >= 0);
        Assert.True(p.IndexOf("CAPT, amber") > lastDoor);
        Assert.Equal(new[] { "CAPT, amber", "Captain oxygen pressure: 1880 PSI, green", "REGUL PR LO, amber",
            "F/O, amber", "First officer oxygen pressure: 1870 PSI, green", "REGUL PR LO, amber", "Rain repellent level: NORM, green" },
            p.Skip(lastDoor + 1));
    }

    [Fact]
    public void Engine_page_names_both_engines_values_with_units()
    {
        var p = Page(1);
        Assert.Contains("Engine 1 fuel flow: XX LBS/H, amber", p);
        Assert.Contains("Engine 2 N2: XX %, amber", p);
        Assert.Contains("Engine 1 oil quantity: XX QT, amber", p);
        Assert.Contains("Engine 2 oil temperature: XX °C, amber", p);
        Assert.Contains("Engine 1 oil pressure: XX PSI, amber", p);
        Assert.Contains("Engine 2 vibration N3: XX, amber", p);
        Assert.DoesNotContain(p, l => l.StartsWith("LBS/H"));   // the unit caption is read with the values
    }

    [Fact]
    public void Hyd_page_names_system_pressures()
    {
        var p = Page(2);
        Assert.Contains("Green system pressure: 0 PSI, amber", p);
        Assert.Contains("Yellow system pressure: 0 PSI, amber", p);
        Assert.Contains("GREEN, amber", p);
        Assert.Contains("ELEC, white", p);
    }

    [Fact]
    public void Dc_page_names_each_battery_and_tr()
    {
        var p = Page(3);
        Assert.Contains("BAT 1, white", p);
        Assert.Contains("BAT EMER 1, white", p);
        Assert.Contains("BAT 1 voltage: 28 V, green", p);
        Assert.Contains("BAT EMER 1 current: 16 A, amber", p);
        Assert.Contains("BAT 2 current: 1 A, amber", p);
        Assert.Contains("TR EMER 2 voltage: 0 V, amber", p);
        Assert.Contains("DC 1, amber", p);
        Assert.Contains("DC EMER 1, green", p);
    }

    [Fact]
    public void Ac_page_names_buses_and_generators()
    {
        var p = Page(4);
        Assert.Contains("AC EMER 1 bus: 115, amber", p);
        Assert.Contains("AC 2A bus: 230, amber", p);
        Assert.Contains("Generator 1B load: 0 %, green", p);
        Assert.Contains("Generator 2A voltage: 0 V, amber", p);
        Assert.Contains("GALLEY PARTLY SHED, white", p);
    }

    [Fact]
    public void Bleed_page_names_packs_and_engine_bleeds()
    {
        var p = Page(5);
        Assert.Contains("Pack 1 outlet temperature: 28 °C, green", p);
        Assert.Contains("Pack 2 compressor outlet temperature: 28 °C, green", p);
        Assert.Contains("Pack 1 controllers: 1 2, amber", p);
        Assert.Contains("Engine 1 bleed pressure: 0 PSI, amber", p);
        Assert.Contains("Engine 2 bleed temperature: 30 °C, green", p);
    }

    [Fact]
    public void Cond_page_names_zones()
    {
        var p = Page(6);
        Assert.Contains("Cockpit temperature: 26 °C, green", p);
        Assert.Contains("Cabin temperature: 26, green; TO, white; 27 °C, green", p);
        Assert.Contains("Forward cargo temperature: 25 °C, green", p);
        Assert.Contains("Bulk cargo temperature: 25 °C, green", p);
        Assert.Contains("Avionics: VENT, amber", p);
        Assert.Contains("MORE, grey", p);
    }

    [Fact]
    public void Cab_press_page_names_its_gauges()
    {
        var p = Page(7);
        Assert.Contains("Landing elevation: 0 FT, green", p);
        Assert.Contains("Delta P: 0.0 PSI, green", p);
        Assert.Contains("Cabin altitude: -30 FT, green", p);
        Assert.Contains("Cabin vertical speed: 0 FT/MIN, green", p);
        Assert.Contains("Pressurization system: SYS 1, green", p);
    }

    [Fact]
    public void Fuel_page_names_quantities_in_the_drawn_unit()
    {
        var p = Page(8);
        Assert.Contains("FOB plus fuel used minus block: XX LBS, amber", p);
        Assert.Contains("Engine 1 fuel used: 8250 LBS, green", p);
        Assert.Contains("Total fuel used: 16430 LBS, green", p);
        Assert.Contains("Centre tank: 137360 LBS, green", p);
        Assert.Contains("Right tank temperature: 25 °C, green", p);
        Assert.Contains("All engines fuel flow: XX LBS/MIN, amber", p);
    }

    [Fact]
    public void Apu_page_names_its_values()
    {
        var p = Page(9);
        Assert.Equal(new[] { "APU bleed pressure: 0 PSI, green", "APU fuel used: 0 LBS, green", "APU N: 0 %, green", "APU EGT: 0 °C, green" }, p);
    }

    [Fact]
    public void Fctl_page_names_trims_and_surfaces()
    {
        var p = Page(10);
        Assert.Contains("Rudder trim: L 0.0, amber", p);
        Assert.Contains("Pitch trim: 2.9 UP, amber", p);
        Assert.Contains("Left spoiler 7: X, amber", p);
        Assert.Contains("Right spoiler 2: X, amber", p);
    }

    [Fact]
    public void Door_page_names_doors_slides_and_oxygen()
    {
        var p = Page(11);
        Assert.Contains("Door 1L slide: XX, amber", p);
        Assert.Contains("Door 4R: X, amber", p);
        Assert.Contains("Captain oxygen pressure: 1890 PSI, green", p);
        Assert.Contains("First officer oxygen pressure: 1890 PSI, green", p);
        Assert.Contains("Rain repellent level: NORM, green", p);
    }

    [Fact]
    public void Wheel_page_names_brake_temperatures()
    {
        var p = Page(12);
        Assert.Contains("Brake 1 temperature: 45 °C, green", p);
        Assert.Contains("Brake 3 temperature: 35 °C, green", p);
        Assert.Contains("Brake 8 temperature: 40 °C, green", p);
        Assert.Contains("Y ACCU, white", p);
    }

    [Fact]
    public void Status_page_reads_as_drawn()
    {
        Assert.Equal(new[] { "STATUS, white", "INOP SYS, white", "ALL PHASES, white", "APPR & LDG, white", "APU, amber" }, Page(14));
    }
}
