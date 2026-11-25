// Where a leg with no destination ends (ClearanceLegEnd): the lookups the Taxi Guidance window makes from the step
// list's plan before LoadRoute. A leg-end junction is the nearest by taxi distance that the aircraft does not have to
// turn round for; the end of a taxiway is found along that taxiway's own edges, the way the aircraft travels on it,
// never the node farthest from it in a straight line; a limit "hold short of taxiway L" said with no taxiway step
// ends at any node of L clear of a runway; and a crossing already made is never routed back over. Graphs are built
// with TaxiGraph.Build from plain TaxiPath lists, as TaxiwayJunctionLookupTests does; coordinates are metres east
// and north of RunwayFixture's base point.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceLegEndTests
{
    private static TaxiPath Path(string name, double e1, double n1, double e2, double n2) => new()
    {
        StartLat = RunwayFixture.Lat(n1), StartLon = RunwayFixture.Lon(e1),
        EndLat = RunwayFixture.Lat(n2), EndLon = RunwayFixture.Lon(e2),
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75,
    };

    private static TaxiGraph Build(params TaxiPath[] paths)
        => TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static int NodeAt(TaxiGraph g, double eastM, double northM)
    {
        double lat = RunwayFixture.Lat(northM), lon = RunwayFixture.Lon(eastM);
        int best = -1;
        double bestD = double.MaxValue;
        foreach (var n in g.Nodes.Values)
        {
            double d = TaxiGraph.FastDistanceMeters(lat, lon, n.Latitude, n.Longitude);
            if (d < bestD) { bestD = d; best = n.NodeId; }
        }
        Assert.True(bestD < 1.0, $"no node at ({eastM}, {northM})");
        return best;
    }

    // Taxiway A along north 0 from east -500 to 600, with nodes at -500, 0, 200, 400 and 600.
    private static TaxiGraph LongA() => Build(
        Path("A", -500, 0, 0, 0), Path("A", 0, 0, 200, 0), Path("A", 200, 0, 400, 0), Path("A", 400, 0, 600, 0));

    // ---- choosing among leg-end candidates ----

    [Fact]
    public void The_nearest_candidate_the_aircraft_need_not_turn_round_for_is_chosen()
    {
        var distances = new Dictionary<int, double> { [1] = 10, [2] = 50, [3] = 80 };
        var asked = new List<int>();
        bool Behind(int id) { asked.Add(id); return id == 1; }

        Assert.Equal(2, ClearanceLegEnd.NearestAhead(new[] { 3, 1, 2 }, distances, Behind));
        // Asked nearest first, and no further than the first one ahead.
        Assert.Equal(new[] { 1, 2 }, asked);
    }

    [Fact]
    public void With_every_candidate_behind_the_nearest_is_chosen()
    {
        var distances = new Dictionary<int, double> { [1] = 10, [2] = 50 };

        Assert.Equal(1, ClearanceLegEnd.NearestAhead(new[] { 2, 1 }, distances, _ => true));
    }

    [Fact]
    public void An_unreachable_candidate_never_beats_a_reachable_one_and_is_never_asked_about()
    {
        var distances = new Dictionary<int, double> { [7] = 500 };
        var asked = new List<int>();

        Assert.Equal(7, ClearanceLegEnd.NearestAhead(new[] { 4, 7 }, distances, id => { asked.Add(id); return true; }));
        Assert.Equal(new[] { 7 }, asked);
        // Only unreachable ones: NearestByTaxiDistance's choice (the smaller id); none: -1.
        Assert.Equal(4, ClearanceLegEnd.NearestAhead(new[] { 9, 4 }, new Dictionary<int, double>(), _ => false));
        Assert.Equal(-1, ClearanceLegEnd.NearestAhead(Array.Empty<int>(), distances, _ => false));
    }

    [Fact]
    public void A_way_that_begins_back_past_the_aircraft_needs_a_reversal()
    {
        var g = LongA();
        int start = NodeAt(g, 200, 0);

        Assert.False(ClearanceLegEnd.NeedsReversal(g, start, NodeAt(g, 400, 0), headingTrue: 90));
        Assert.True(ClearanceLegEnd.NeedsReversal(g, start, NodeAt(g, 0, 0), headingTrue: 90));
        Assert.False(ClearanceLegEnd.NeedsReversal(g, start, start, headingTrue: 90));
        // Facing west, the other way round.
        Assert.True(ClearanceLegEnd.NeedsReversal(g, start, NodeAt(g, 400, 0), headingTrue: 270));
    }

    // ---- a limit said with no taxiway step ----

    [Fact]
    public void Hold_short_of_a_taxiway_with_no_taxiway_step_takes_its_nodes_in_the_component_clear_of_a_runway()
    {
        var g = Build(
            Path("C", 0, -300, 0, 0),
            Path("L", -200, 0, 0, 0), Path("L", 0, 0, 300, 0),
            // Another piece of L, not joined to this one.
            Path("L", 0, 3000, 300, 3000));
        // A north-south runway 10 m east of L's east end: that node is on its pavement.
        g.RunwayCenterlines.Add(RunwayFixture.NorthSouth("18", "36", eastM: 310, fromNorthM: 500, toNorthM: -500));
        int component = g.Nodes[NodeAt(g, 0, -300)].ComponentId;

        var nodes = ClearanceLegEnd.NodesOnTaxiway(g, "L", component);

        Assert.Equal(new[] { NodeAt(g, -200, 0), NodeAt(g, 0, 0) }.OrderBy(id => id), nodes.OrderBy(id => id));
        Assert.Empty(ClearanceLegEnd.NodesOnTaxiway(g, "Z", component));
    }

    // ---- the end of the last taxiway ----

    [Fact]
    public void The_end_of_the_taxiway_the_aircraft_is_on_lies_the_way_it_is_going()
    {
        var g = LongA();
        int entry = NodeAt(g, 200, 0);
        double acLat = RunwayFixture.Lat(0), acLon = RunwayFixture.Lon(190);

        Assert.Equal(NodeAt(g, 600, 0), ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 90, acLat, acLon).EndNodeId);
        Assert.Equal(NodeAt(g, -500, 0), ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 270, acLat, acLon).EndNodeId);
        // The straight-line farthest node is the west end, behind an aircraft going east.
        Assert.Equal(NodeAt(g, -500, 0), g.FindTaxiwayEndNodeClearOfRunways(entry, "A"));
    }

    [Fact]
    public void Joined_square_to_the_taxiway_the_end_is_the_arm_reaching_farthest_from_the_aircraft()
    {
        // C comes up from the south onto A at east 0; A runs 100 m west and 600 m east.
        var g = Build(Path("C", 0, -300, 0, 0), Path("A", -100, 0, 0, 0), Path("A", 0, 0, 600, 0));
        int entry = NodeAt(g, 0, 0);

        var end = ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 0, RunwayFixture.Lat(-300), RunwayFixture.Lon(0));

        Assert.Equal(NodeAt(g, 600, 0), end.EndNodeId);
    }

    [Fact]
    public void A_taxiway_going_straight_on_from_the_way_the_aircraft_comes_is_followed_straight_on()
    {
        // C comes up from the south to east 0, where A goes on north 600 m and also 1,000 m west.
        var g = Build(Path("C", 0, -300, 0, 0), Path("A", 0, 0, 0, 300), Path("A", 0, 300, 0, 600),
            Path("A", -1000, 0, 0, 0));
        int entry = NodeAt(g, 0, 0);

        var end = ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 0, RunwayFixture.Lat(-300), RunwayFixture.Lon(0));

        Assert.Equal(NodeAt(g, 0, 600), end.EndNodeId);
        // The straight-line farthest node is the west end.
        Assert.Equal(NodeAt(g, -1000, 0), g.FindTaxiwayEndNodeClearOfRunways(NodeAt(g, 0, -300), "A"));
    }

    [Fact]
    public void A_fork_of_the_taxiway_is_followed_straight_on()
    {
        var g = Build(Path("A", 0, 0, 300, 0), Path("A", 300, 0, 600, 0), Path("A", 300, 0, 500, 300));
        int entry = NodeAt(g, 0, 0);

        var end = ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 90, RunwayFixture.Lat(0), RunwayFixture.Lon(-10));

        Assert.Equal(NodeAt(g, 600, 0), end.EndNodeId);
    }

    [Fact]
    public void A_taxiway_that_runs_onto_a_runway_ends_at_its_last_node_clear_of_it()
    {
        var g = Build(Path("A", 0, 0, 150, 0), Path("A", 150, 0, 270, 0), Path("A", 270, 0, 300, 0));
        g.RunwayCenterlines.Add(RunwayFixture.NorthSouth("18", "36", eastM: 300, fromNorthM: 500, toNorthM: -500));
        int entry = NodeAt(g, 0, 0);

        var end = ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 90, RunwayFixture.Lat(0), RunwayFixture.Lon(-10));

        Assert.Equal(NodeAt(g, 150, 0), end.EndNodeId);
        Assert.Equal(NodeAt(g, 300, 0), end.ArmEndNodeId);
    }

    [Fact]
    public void A_taxiway_with_no_node_clear_of_a_runway_ahead_has_no_end_but_an_arm()
    {
        // The aircraft at A's west end, 60 m off an east-west runway; everything east of it lies inside the runway's
        // clear margin (20 m off its centreline).
        var g = Build(Path("A", 0, 60, 100, 20), Path("A", 100, 20, 300, 20));
        g.RunwayCenterlines.Add(RunwayFixture.EastWest(northM: 0));
        int entry = NodeAt(g, 0, 60);

        var end = ClearanceLegEnd.EndOfTaxiway(g, entry, "A", 90, RunwayFixture.Lat(60), RunwayFixture.Lon(-10));

        Assert.Equal(-1, end.EndNodeId);
        Assert.Equal(NodeAt(g, 300, 20), end.ArmEndNodeId);
        // A taxiway the entry is not on: nothing at all.
        var none = ClearanceLegEnd.EndOfTaxiway(g, entry, "Z", 90, RunwayFixture.Lat(60), RunwayFixture.Lon(-10));
        Assert.Equal(-1, none.EndNodeId);
        Assert.Equal(-1, none.ArmEndNodeId);
    }

    // ---- a crossing already made ----

    [Theory]
    [InlineData(100.0, -150.0, true, true)]    // across, clear of the runway: the leg's end is reached
    [InlineData(100.0, -20.0, false, false)]   // still inside the runway's margin: route on across
    [InlineData(100.0, 50.0, true, false)]     // still on the side it crossed from
    [InlineData(0.0, -150.0, true, false)]     // a stop with no side: never judged across
    public void An_aircraft_is_already_across_only_on_the_far_side_of_the_stop_and_clear_of_the_runway(
        double stopCrossTrack, double aircraftCrossTrack, bool aircraftClear, bool expected)
    {
        Assert.Equal(expected, ClearanceLegEnd.IsAlreadyAcross(stopCrossTrack, aircraftCrossTrack, aircraftClear));
    }

    [Fact]
    public void Already_across_is_said_as_written()
    {
        const string expected = "You are already across runway 24L. Add the next step.";
        Assert.Equal(expected, ClearanceLegEnd.AlreadyAcross("24L"));
        Assert.Equal(expected, TestCulture.Run("de-DE", () => ClearanceLegEnd.AlreadyAcross("24L")));
        Assert.Equal(expected, TestCulture.Run("sv-SE", () => ClearanceLegEnd.AlreadyAcross(" 24L ")));
    }
}
