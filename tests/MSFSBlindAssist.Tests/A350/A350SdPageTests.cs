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
        var cap = A350Fixtures.Capture($"{fixture}{page}.json");
        return A350SdPage.Read(A350Ewd.Parse(cap).SystemDisplay, page);
    }

    // Live, on external power with both emergency batteries off (2026-10-27): what the stand copy never drew.
    private static IReadOnlyList<string> Live(int page) => Page(page, "ctr_sd_live_");

    // Live, engine 1 starting with engine 2 running (2026-10-27): inside engine 1's start box, its igniter in use
    // (A or B) and, under each start valve, the start air pressure (PSI, the unit not drawn).
    [Fact]
    public void A_start_reads_the_igniter_and_the_start_air_pressure()
    {
        var lines = Page(1, "ctr_eng_start_");
        Assert.Contains("Engine 1 igniter: B, green", lines);
        Assert.Contains("Engine 1 start air pressure: 44 PSI, green", lines);
        Assert.Contains("Engine 2 start air pressure: 44 PSI, green", lines);
        Assert.Contains("Engine 2 N3: 64.6 %, green", lines);
    }

    [Fact]
    public void The_permanent_data_line_names_each_value()
    {
        var cap = A350Fixtures.Capture("ctr_sd_live_4.json");
        // "FLT" is drawn as text left of the flight time: the caption its field already names.
        Assert.Equal(new[]
        {
            "TAT: XX °C, amber", "Flight time: 00:00, green", "Gross weight: -- LBS, cyan",
            "SAT: XX °C, amber", "Gross weight CG: -- %, cyan", "UTC: 16:31:57, green", "Fuel on board: 243700 LBS, green",
        }, A350SdPage.ReadPermanentData(A350Ewd.Parse(cap).PermanentData));
    }

    // Live at FL400 (2026-10-28): the SD's CRUISE page, INI_ECAM_ACTIVE_PAGE 13 (it has no ECP key; STS with nothing
    // to show brings it up). Its labels are EcamCruiseStatic.png's: FF and FU per engine, (FOB + FU) - BLOCK, the
    // landing elevation, the cabin modes over CAB ALT and CAB V/S, delta P, cabin altitude and V/S, and COND's
    // temperatures (the cabin's "20 TO 21", the TO in the texture; the cockpit; the forward and aft cargo).
    [Fact]
    public void The_cruise_page_names_every_value()
    {
        Assert.Equal("Cruise", A350SdText.PageName(13));
        var p = Page(13, "ctr_sd_cruise_");
        foreach (var line in new[]
        {
            "Engine 1 fuel flow: 2880 KG/H, green", "Engine 2 fuel flow: 2880 KG/H, green",
            "Engine 1 fuel used: 7080 KG, green", "Engine 2 fuel used: 7100 KG, green",
            "FOB plus fuel used minus block: 0 KG, green", "Landing elevation: 50 FT, green",
            "Cabin altitude mode: AUTO, green", "Cabin vertical speed mode: AUTO, green",
            "Delta P: 9.0 PSI, green", "Cabin altitude: 5910 FT, green", "Cabin vertical speed: 0 FT/MIN, green",
            "Cabin temperature: 20 to 21 °C, green", "Cockpit temperature: 20 °C, green",
            "Forward cargo temperature: 12 °C, green", "Aft cargo temperature: 11 °C, green",
        })
            Assert.Contains(line, p);
    }

    // Live, refuelling on the stand once the ADIRS had aligned (2026-10-28): the ISA deviation drawn under SAT,
    // its "ISA" caption drawn as text at the left edge, was read as two lines ("ISA, white" then "+13, green").
    [Fact]
    public void The_isa_deviation_reads_as_one_named_value()
    {
        var cap = A350Fixtures.Capture("ctr_refuel_door_isa.json");
        var lines = A350SdPage.ReadPermanentData(A350Ewd.Parse(cap).PermanentData);
        Assert.Contains("ISA: +13 °C, green", lines);
        Assert.DoesNotContain("ISA, white", lines);
        Assert.DoesNotContain("+13, green", lines);
        Assert.True(lines.ToList().IndexOf("ISA: +13 °C, green") > lines.ToList().IndexOf("SAT: +27 °C, green"), "under SAT");
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
        // Each door's number is drawn in its symbol, coloured as the door: its state is the symbol's line
        // ("Door 1L: closed, green", A350SdSymbols), so the number alone is a caption, not a value.
        Assert.DoesNotContain(p, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^Door \d[LR]: \d, "));
        Assert.Equal(new[] { "CAPT, amber", "Captain oxygen pressure: 1880 PSI, green", "REGUL PR LO, amber",
            "F/O, amber", "First officer oxygen pressure: 1870 PSI, green", "REGUL PR LO, amber", "Rain repellent level: NORM, green" },
            p.SkipWhile(l => l != "CAPT, amber"));
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

    // STATUS: under the ALL PHASES and APPR & LDG headings, two columns, read column by column, each item with its
    // column's heading (a sighted pilot sees which column an item sits in; the read interleaved them by row).
    [Fact]
    public void Status_page_reads_each_item_with_its_column()
    {
        Assert.Equal(new[] { "STATUS, white", "INOP SYS, white", "ALL PHASES: APU, amber" }, Page(14));
    }

    // Review 2026-10-28: the columns ran on to the next centred white heading, so a line below them read as an item of
    // one ("APPR & LDG: PRESS STS FOR NEXT STATUS PAGE", the multi-page STATUS's footer). Only a line aligned with a
    // column's items is one of its items; anything else ends the columns and reads as drawn.
    [Theory]
    [InlineData(150f, "cyan", "PRESS STS FOR NEXT STATUS PAGE, cyan")]
    [InlineData(250f, "white", "PRESS STS FOR NEXT STATUS PAGE, white")]
    public void A_line_not_aligned_with_the_columns_ends_them(float x, string colour, string reads)
    {
        var cap = A350Fixtures.Capture("ctr_sd_status_inop_14.json");
        var rgba = colour == "cyan" ? new A350Rgba(0, 1, 1, 1) : new A350Rgba(1, 1, 1, 1);
        var texts = cap.Texts.ToList();
        texts.Add(new DrawnText("PRESS STS FOR NEXT STATUS PAGE", x, 760, x, 760, 1, rgba));
        var lines = A350SdPage.Read(A350Ewd.Parse(cap with { Texts = texts }).SystemDisplay, 14);
        Assert.Contains(reads, lines);
        Assert.Contains("ALL PHASES: R TK MAIN PMP, amber", lines);
    }

    // Live 2026-10-28 in the hold at IMPED with AC 2A 115V BUS, FLAPS SYS 1, R TK MAIN PMP and ENG 1 A-ICE
    // VALVE failed: INFO's lines, then INOP SYS's two columns.
    [Fact]
    public void Status_page_reads_info_then_both_columns()
    {
        Assert.Equal(new[]
        {
            "STATUS, white", "LIMITATIONS, cyan", "INFO, white", "FOR RNP AR : MINIMUM LIMITED TO RNP 0.30, green",
            "APPR 1 ONLY, green", "INOP SYS, white",
            "ALL PHASES: PART GALLEY, amber", "ALL PHASES: FWD CARGO VENT, amber", "ALL PHASES: BULK CARGO VENT, amber",
            "ALL PHASES: R TK MAIN PMP, amber", "APPR & LDG: LS 2, amber", "APPR & LDG: FLAP SYS 1, amber",
        }, Page(14, "ctr_sd_status_inop_").Take(12));
    }
}
