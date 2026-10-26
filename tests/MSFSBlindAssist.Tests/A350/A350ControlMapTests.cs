using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The embedded A350 control map (tools/a350-gen) as the app reads it.</summary>
public class A350ControlMapTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();

    [Fact]
    public void The_embedded_map_holds_every_cockpit_control()
    {
        Assert.Equal(696, Map.Controls.Count);
        Assert.Equal("1.2.7", Map.PackageVersion);
    }

    [Fact]
    public void A_control_is_found_by_its_input_event_and_by_its_key()
    {
        var bat = Map.Find("AIRLINER_ELEC_BAT_1");
        Assert.NotNull(bat);
        Assert.Same(bat, Map.FindByKey("A350_ELEC_BAT_1"));
        Assert.Equal(A350Kinds.Toggle, bat!.Kind);
        Assert.Equal("L:INI_BATTERY_1_SWITCH", bat.StateVar);
        Assert.Equal("AIRLINER_ELEC_BAT_1_Set", bat.Event);
        Assert.Equal(("OVERHEAD", "ELEC"), (bat.Area, bat.Panel));
    }

    [Fact]
    public void Selector_positions_are_ordered_with_the_tooltip_words()
    {
        var belts = Map.Find("AIRLINER_SIGNS_SEAT_BELTS")!;
        Assert.Equal(new[] { (0.0, "ON"), (1.0, "AUTO"), (2.0, "OFF") }, belts.OrderedPositions());
        Assert.Equal(2.0, belts.PositionForState(2));
        Assert.Null(belts.PositionForState(5));
    }

    [Fact]
    public void The_lamps_come_with_the_map()
    {
        Assert.Equal(419, Map.Lamps.Count);
        var lamp = Map.Lamps.Single(l => l.Node == "AIR_APU_BLEED_SEQ1_LIGHT");
        Assert.Equal("(L:INI_AIR_BLEED_APU_FAULT) 1 ==", lamp.State);
        Assert.Equal(A350LightPower.Dc, lamp.Power);
    }

    [Fact]
    public void A_malformed_map_parses_to_an_empty_one()
    {
        Assert.Empty(A350ControlMap.Parse("{}").Controls);
    }
}
