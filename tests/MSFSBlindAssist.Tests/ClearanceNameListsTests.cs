// Tests for MSFSBlindAssist.Navigation.ClearanceNameLists: the name box's contents for each kind of step.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceNameListsTests
{
    private static readonly string[] All = { "A", "B", "C", "K", "N", "P" };

    private static ClearanceStep Via(string name)
        => new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, name);

    private static ClearanceStep CrossRwy(string name)
        => new(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, name);

    private static IReadOnlyList<string> Crossing(string runway) => runway switch
    {
        "04L" or "04R" => new[] { "C" },
        "09" => new[] { "K", "C2" },
        _ => Array.Empty<string>(),
    };

    private static NamedHoldingPoint Point(string name, string kind = "intermediate")
        => new() { Name = name, Kind = kind, NodeId = 5 };

    private static string[] Labels(IEnumerable<ClearanceNameLists.Item> items)
        => items.Select(i => i.Label).ToArray();

    [Fact]
    public void The_first_step_lists_the_nearest_taxiways_first()
    {
        var items = ClearanceNameLists.Taxiways(new[] { "P", "N" }, Array.Empty<string>(), All, Array.Empty<ClearanceStep>());
        Assert.Equal(new[] { "P", "N" }, Labels(items));
        Assert.All(items, i => Assert.Equal(ClearanceTargetKind.Taxiway, i.Kind));
    }

    [Fact]
    public void After_a_via_step_the_connected_taxiways_come_first()
    {
        var items = ClearanceNameLists.Taxiways(new[] { "P" }, new[] { "K", "A" }, All, new[] { Via("C") });
        Assert.Equal(new[] { "K", "A", "B", "N", "P" }, Labels(items));
    }

    [Fact]
    public void The_previous_taxiway_is_left_out_after_a_via_step_and_offered_after_a_crossing()
    {
        var afterVia = ClearanceNameLists.Taxiways(new string[0], new[] { "K" }, All, new[] { Via("N") });
        Assert.DoesNotContain("N", Labels(afterVia));

        var afterCross = ClearanceNameLists.Taxiways(new string[0], new[] { "K" }, All, new[] { Via("N"), CrossRwy("15R") });
        Assert.Contains("N", Labels(afterCross));
    }

    [Fact]
    public void Runways_the_last_taxiway_crosses_come_first_in_the_cross_list()
    {
        var runways = new[] { "04L", "04R", "09", "15R" };

        var viaC = ClearanceNameLists.CrossTargets(runways, Crossing, new[] { "B" }, All, new[] { Via("C") });
        Assert.Equal(new[] { "04L", "04R", "09", "15R" }, Labels(viaC).Take(4).ToArray());
        Assert.All(viaC.Take(4), i => Assert.Equal(ClearanceTargetKind.Runway, i.Kind));
        Assert.Equal("B", viaC[4].Label);
        Assert.Equal(ClearanceTargetKind.Taxiway, viaC[4].Kind);

        var viaK = ClearanceNameLists.CrossTargets(runways, Crossing, new[] { "B" }, All, new[] { Via("K") });
        Assert.Equal(new[] { "09", "04L", "04R", "15R" }, Labels(viaK).Take(4).ToArray());
    }

    [Fact]
    public void A_crossing_entered_first_lists_the_runways_then_every_taxiway_alphabetically()
    {
        var items = ClearanceNameLists.CrossTargets(new[] { "09", "15R" }, Crossing, Array.Empty<string>(), new[] { "P", "A", "C" }, Array.Empty<ClearanceStep>());
        Assert.Equal(new[] { "09", "15R", "A", "C", "P" }, Labels(items));
        Assert.Equal(ClearanceTargetKind.Taxiway, items[2].Kind);
    }

    [Fact]
    public void Holding_points_are_merged_with_taxiways_and_marked()
    {
        var items = ClearanceNameLists.HoldTargets(new[] { "09" }, Crossing, new[] { "L", "L3" },
            new[] { Point("L2") }, false, new[] { Via("L") });
        Assert.Equal(new[] { "09", "L", "L2 (holding point)", "L3" }, Labels(items));
        Assert.Equal(ClearanceTargetKind.Runway, items[0].Kind);
        Assert.Equal(ClearanceTargetKind.Taxiway, items[1].Kind);
        Assert.Equal(ClearanceTargetKind.HoldingPoint, items[2].Kind);
        Assert.Equal("L2", items[2].Name);
        Assert.NotNull(items[2].HoldingPoint);
    }

    [Fact]
    public void Sign_read_points_are_listed_only_when_asked_for()
    {
        var points = new[] { Point("L2"), Point("S1", NamedHoldingPointResolver.SignKind) };
        var without = ClearanceNameLists.HoldTargets(new string[0], Crossing, new[] { "L" }, points, false, new ClearanceStep[0]);
        Assert.DoesNotContain(without, i => i.Name == "S1");

        var with = ClearanceNameLists.HoldTargets(new string[0], Crossing, new[] { "L" }, points, true, new ClearanceStep[0]);
        Assert.Contains(with, i => i.Name == "S1" && i.Kind == ClearanceTargetKind.HoldingPoint);
    }
}
