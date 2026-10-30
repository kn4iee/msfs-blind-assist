using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The Exterior section: the walkaround's service panels, hatches, gear bays and cowlings (the exterior models'
/// clickspots), each a row on its L:var. Live 2026-10-30, every one set and read back.</summary>
public class A350ExteriorPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static List<string> Names(string panel) => Placement.RowsByPanel[panel].Select(r => r.Name).ToList();

    private static A350PlacedRow Row(string panel, string name) => Placement.RowsByPanel[panel].Single(r => r.Name == name);

    private static string Words(A350PlacedRow row) =>
        string.Join(", ", row.Positions.OrderBy(p => p.Key).Select(p => p.Value));

    [Fact]
    public void The_exterior_panels_follow_the_cockpit()
    {
        Assert.Equal(new[] { "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit", "Exterior" }, Placement.Structure.Keys);
        Assert.Equal(new[] { "Ground Services", "Service Hatches", "Gear Bays", "Engine Cowlings and Covers" }, Placement.Structure["Exterior"]);
    }

    [Fact]
    public void Ground_services_are_the_air_power_water_and_fuel_panels()
    {
        Assert.Equal(new[]
        {
            "HP air (air start unit)", "LP air (air conditioning unit)", "External power", "Potable water panel", "Waste water panel",
            "Refuel panel door", "Refuel mode selector",
        }, Names("Ground Services"));
        Assert.Equal("Not connected, Connected", Words(Row("Ground Services", "HP air (air start unit)")));
        Assert.Equal("Closed, Open", Words(Row("Ground Services", "Refuel panel door")));
        Assert.Equal("Off, On", Words(Row("Ground Services", "Refuel mode selector")));
    }

    [Fact]
    public void Hatches_and_gear_bays_are_named_by_place()
    {
        Assert.Equal(new[]
        {
            "Green hydraulic panel", "Yellow hydraulic panel", "APU service door", "Avionics bay door", "Forward cargo upper panel",
            "Forward cargo lower panel", "Aft cargo upper panel", "Aft cargo lower panel",
        }, Names("Service Hatches"));
        Assert.Equal(new[]
        {
            "Main gear door hatch", "Left main gear door", "Right main gear door", "Nose gear door hatch", "Nose gear door",
            "Nose wheel steering panel", "Nose gear interphone panel",
        }, Names("Gear Bays"));
    }

    [Fact]
    public void Cowlings_and_covers()
    {
        Assert.Equal(new[] { "Left engine cowling", "Right engine cowling", "Engine and pitot covers" }, Names("Engine Cowlings and Covers"));
        Assert.Equal("Removed, Fitted", Words(Row("Engine Cowlings and Covers", "Engine and pitot covers")));
    }

    // Each input event only flips its L:var ("(L:INI_ASU_AVAIL) ! (>L:INI_ASU_AVAIL)"): a pick writes the state asked for.
    [Theory]
    [InlineData("Ground Services", "HP air (air start unit)", "INI_ASU_AVAIL")]
    [InlineData("Ground Services", "LP air (air conditioning unit)", "INI_ACU_AVAIL")]
    [InlineData("Ground Services", "External power", "INI_GPU_AVAIL")]
    [InlineData("Ground Services", "Refuel panel door", "INI_Refuel_Door")]
    [InlineData("Gear Bays", "Left main gear door", "INI_Gear_GND_Opening_Left")]
    [InlineData("Gear Bays", "Nose wheel steering panel", "INI_NWS_NG")]
    [InlineData("Engine Cowlings and Covers", "Right engine cowling", "INI_Engine_Cowl_Right")]
    [InlineData("Engine Cowlings and Covers", "Engine and pitot covers", "INI_COVERS_ENABLED")]
    public void A_pick_writes_its_state(string panel, string name, string lvar)
    {
        var control = Row(panel, name).Control!;
        Assert.Equal("L:" + lvar, control.StateVar);
        Assert.Equal($"1 (>L:{lvar})", A350Levers.WriteFor(control, 1));
        Assert.Equal($"0 (>L:{lvar})", A350Levers.WriteFor(control, 0));
    }

    // The cowling opened, the sight gauge shows the oil (the model's ENG1_OIL_INDICATOR reads INI_ENG1_OIL_QUANTITY).
    [Fact]
    public void The_oil_level_is_a_status_line_beside_each_cowling()
    {
        var left = A350StatusLines.All.Single(l => l.Key == "A350_STAT_ENG1_OIL");
        Assert.Equal(Row("Engine Cowlings and Covers", "Left engine cowling").Key, left.Beside);
        Assert.Equal("INI_ENG1_OIL_QUANTITY", left.Var);
        Assert.Equal("16.3 quarts", left.Format(16.297));
        Assert.Equal(Row("Engine Cowlings and Covers", "Right engine cowling").Key, A350StatusLines.All.Single(l => l.Key == "A350_STAT_ENG2_OIL").Beside);
    }
}
