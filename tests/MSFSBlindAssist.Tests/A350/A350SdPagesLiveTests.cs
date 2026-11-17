using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Plan 04 Task 5 (2026-11-02, 1.2.8, at the VCBI stand, both engines running): every SD page picked through the ECP's page row
// and captured live with its shapes (A350Probe draw live CTR, A350_SHAPES=1). Each reads as drawn; these pin one value or symbol
// of each, so a later change to the SD reader that loses a page shows here. Pages 13 and 15 are no page (the aircraft stays on 12); STATUS
// with the BRAKES PARK BRK ON caution on the E/WD drew the WHEEL page (the alert's), so it has no ground fixture here.
public class A350SdPagesLiveTests
{
    [Theory]
    [InlineData("engine", 1, "Engine page", "Engine 1 oil quantity: 16.7 QT, green")]
    [InlineData("hyd", 2, "Hyd page", "Green system pressure: 5000 PSI, green")]
    [InlineData("hyd", 2, "Hyd page", "Engine 1 green pump: running, green")]
    [InlineData("elecdc", 3, "Elec DC page", "BAT 1: charging, green")]
    [InlineData("elecac", 4, "Elec AC page", "AC 1A bus: 115, green")]
    [InlineData("bleed", 5, "Bleed page", "Crossbleed valve: closed, green")]
    [InlineData("cond", 6, "Cond page", "Ram air valve: closed, green")]
    [InlineData("press", 7, "Press page", "Cabin altitude mode: AUTO, green")]
    [InlineData("fuel", 8, "Fuel page", "Centre tank: 0 KG, green")]
    [InlineData("fuel", 8, "Fuel page", "Crossfeed A valve: closed, green")]
    [InlineData("apu", 9, "APU page", "APU bleed valve: closed, green")]
    [InlineData("fctl", 10, "F/Ctl page", "Rudder: centred, green")]
    [InlineData("door", 11, "Door page", "Door 1L: closed, green")]
    [InlineData("wheel", 12, "Wheel page", "Nose gear: down and locked, green")]
    public void Each_sd_page_on_the_ground_reads_as_drawn(string page, double number, string title, string line)
    {
        var text = A350SdText.Build(A350Fixtures.Capture($"p04_ctr_sd_{page}_ground.json"), number);
        var lines = text.Split("\r\n");
        Assert.Equal(title, lines[0]);
        Assert.Contains(line, lines);
    }

    [Theory]
    [InlineData("engine", 1, "Engine page", "Engine 1 N2: 8")]
    [InlineData("bleed", 5, "Bleed page", "Engine 1 HP valve: closed, amber")]
    [InlineData("press", 7, "Press page", "Delta P: 6.9 PSI, green")]
    [InlineData("fuel", 8, "Fuel page", "Left tank: 6")]
    [InlineData("wheel", 12, "Wheel page", "Brake 8 temperature: 35 °C, green")]
    [InlineData("cruise", 13, "Cruise page", "Cabin altitude: 2310 FT, green")]
    public void Each_sd_page_in_cruise_reads_as_drawn(string page, double number, string title, string start)
    {
        var lines = A350SdText.Build(A350Fixtures.Capture($"p04_ctr_sd_{page}_air.json"), number).Split("\r\n");
        Assert.Equal(title, lines[0]);
        Assert.Contains(lines, l => l.StartsWith(start, StringComparison.Ordinal));
    }

    // The A350-1000's WHEEL page (EcamWheel1000Static.png, live 2026-11-17 at OMDB, cold engines): six-wheel bogies, twelve brakes in three
    // rows (1 to 4, 5 to 8, 9 to 12, left to right) where the -900 draws eight in two; each temperature read as a bare number.
    [Fact]
    public void The_a350_1000s_wheel_page_names_all_twelve_brakes()
    {
        var lines = A350SdText.Build(A350Fixtures.Capture("ctr_sd_wheel_1000_ground.json"), 12).Split("\r\n");
        Assert.Equal("Wheel page", lines[0]);
        var expected = new[] { 35, 45, 45, 40, 35, 35, 40, 45, 35, 35, 35, 40 }
            .Select((t, i) => $"Brake {i + 1} temperature: {t} °C, green").ToArray();
        Assert.Equal(expected, lines.Where(l => l.StartsWith("Brake ", StringComparison.Ordinal)).ToArray());
        Assert.DoesNotContain(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+, green$"));
    }

    // With no gear door data (the stand, powered, on both variants) WHEEL draws an amber XX in each door's place: it read "XX XX, amber",
    // "XX, amber", "XX, amber", no door named, where every other page names its XX by its field.
    [Theory]
    [InlineData("ctr_sd_wheel_1000_ground.json")]
    [InlineData("ctr_sd_page_12.json")]
    public void The_wheel_pages_gear_doors_without_data_are_named(string fixture)
    {
        var lines = A350SdText.Build(A350Fixtures.Capture(fixture), 12).Split("\r\n");
        foreach (var door in new[] { "Nose gear door, left", "Nose gear door, right", "Left main gear door", "Right main gear door" })
            Assert.Contains($"{door}: XX, amber", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("XX", StringComparison.Ordinal));
    }

    // In flight the door page also draws CAB V/S (its label and unit in white and cyan, the value in green): it read as a bare "400".
    [Fact]
    public void The_door_page_in_flight_names_the_cabin_vertical_speed()
    {
        var lines = A350SdText.Build(A350Fixtures.Capture("p04_ctr_sd_door_air.json"), 11).Split("\r\n");
        Assert.Contains("Cabin vertical speed: 400 FT/MIN, green", lines);
        Assert.DoesNotContain("400, green", lines);
    }

    // The STATUS key with nothing for STATUS draws another page under the STATUS page number: in cruise the CRUISE page (it read as
    // fifteen bare numbers), and on the ground with BRAKES PARK BRK ON the WHEEL page (its brakes named as no page's). Said as shown.
    [Theory]
    [InlineData("p04_ctr_sd_status_air.json", "Status page, showing the Cruise page", "Cabin altitude: 2270 FT, green")]
    [InlineData("p04_ctr_sd_status_alert_ground.json", "Status page, showing the Wheel page", "Brake 1 temperature: 535 °C, amber")]
    public void A_status_key_showing_another_page_reads_that_page(string fixture, string title, string line)
    {
        var lines = A350SdText.Build(A350Fixtures.Capture(fixture), 14).Split("\r\n");
        Assert.Equal(title, lines[0]);
        Assert.Contains(line, lines);
    }

    [Fact]
    public void A_status_page_with_its_title_reads_as_status()
    {
        var capture = A350Fixtures.Capture("p04_ctr_sd_status_alert_ground.json");
        var titled = capture with
        {
            Texts = capture.Texts.Select(t => t.Y is > 380 and < 940 && t.X < 850 ? t with { Text = "" } : t)
                .Append(new DrawnText("STATUS", 400, 420, 0, 0, 0, new A350Rgba(1, 1, 1, 1))).ToList(),
        };
        Assert.Equal("Status page", A350SdText.Build(titled, 14).Split("\r\n")[0]);
    }

    // The brakes were hot from a slide on the parking brake: the temperatures read amber, as drawn.
    [Fact]
    public void Hot_brakes_read_amber_on_the_wheel_page()
    {
        var lines = A350SdText.Build(A350Fixtures.Capture("p04_ctr_sd_wheel_ground.json"), 12).Split("\r\n");
        Assert.Equal(8, lines.Count(l => l.StartsWith("Brake ", StringComparison.Ordinal) && l.EndsWith("°C, amber", StringComparison.Ordinal)));
    }
}
