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

    // ---- where the aircraft joins the last taxiway, and which way it goes on it ----

    // A comes up from the south to P at east 0; P runs from east -300 to 600, with nodes at 0, 300 and 600.
    private static TaxiGraph AOntoP() => Build(Path("A", 0, -300, 0, 0),
        Path("P", -300, 0, 0, 0), Path("P", 0, 0, 300, 0), Path("P", 300, 0, 600, 0));

    private static ClearanceLegEnd.TaxiwayEntry Entry(TaxiGraph g, int start, double eastM, double northM, double heading)
    {
        var distances = new TaxiRouter(g).ComputeGraphDistancesFrom(start);
        return ClearanceLegEnd.EntryOntoTaxiway(g, "P", previousTaxiway: "A", g.Nodes[start].ComponentId, distances,
            id => ClearanceLegEnd.NeedsReversal(g, start, id, heading),
            RunwayFixture.Lat(northM), RunwayFixture.Lon(eastM), heading);
    }

    // The review's repro: "Via A, Via P" still listed, the aircraft already on P going east, the A/P junction behind.
    // The junction was the entry, the bearing back to it the way on, and the leg ran back to P's west end.
    [Fact]
    public void On_the_last_taxiway_with_the_junction_from_the_one_before_behind_its_end_is_ahead()
    {
        var g = AOntoP();
        double acLat = RunwayFixture.Lat(0), acLon = RunwayFixture.Lon(150);

        var entry = Entry(g, NodeAt(g, 300, 0), 150, 0, heading: 90);

        Assert.Equal(NodeAt(g, 300, 0), entry.NodeId);
        Assert.Equal(90.0, entry.TravelBearingTrue);
        Assert.True(entry.AircraftOnTaxiway);
        Assert.Equal(NodeAt(g, 600, 0), ClearanceLegEnd.EndOfTaxiway(g, entry.NodeId, "P", entry.TravelBearingTrue, acLat, acLon).EndNodeId);
        // What it did: from the junction behind, "straight on" back west.
        Assert.Equal(NodeAt(g, -300, 0), ClearanceLegEnd.EndOfTaxiway(g, NodeAt(g, 0, 0), "P", 270, acLat, acLon).EndNodeId);
    }

    [Fact]
    public void Joining_from_the_taxiway_before_the_entry_is_their_junction_ahead_and_the_way_the_aircraft_comes()
    {
        var g = AOntoP();

        var entry = Entry(g, NodeAt(g, 0, 0), 0, -200, heading: 0);

        Assert.Equal(NodeAt(g, 0, 0), entry.NodeId);
        Assert.Equal(0.0, entry.TravelBearingTrue, 1);
        Assert.False(entry.AircraftOnTaxiway);
        Assert.Equal(NodeAt(g, 600, 0), ClearanceLegEnd.EndOfTaxiway(g, entry.NodeId, "P", entry.TravelBearingTrue,
            RunwayFixture.Lat(-200), RunwayFixture.Lon(0)).EndNodeId);
    }

    [Fact]
    public void A_junction_the_aircraft_would_turn_round_for_is_never_the_entry()
    {
        // A goes on north past P; the aircraft is on A 150 m past the junction, still going north, not on P.
        var g = Build(Path("A", 0, -300, 0, 0), Path("A", 0, 0, 0, 300),
            Path("P", -300, 0, 0, 0), Path("P", 0, 0, 300, 0), Path("P", 300, 0, 600, 0));

        var entry = Entry(g, NodeAt(g, 0, 300), 0, 150, heading: 0);

        // Every node of P is behind: the nearest of them, and the aircraft's own heading as the way; a join, not yet on P.
        Assert.Equal(NodeAt(g, 0, 0), entry.NodeId);
        Assert.Equal(0.0, entry.TravelBearingTrue);
        Assert.False(entry.AircraftOnTaxiway);
    }

    [Fact]
    public void On_a_taxiway_is_within_its_pavement_tolerance_of_one_of_its_edges()
    {
        var g = AOntoP();

        Assert.True(ClearanceLegEnd.IsOnTaxiway(g, "P", RunwayFixture.Lat(10), RunwayFixture.Lon(150)));
        Assert.False(ClearanceLegEnd.IsOnTaxiway(g, "P", RunwayFixture.Lat(-200), RunwayFixture.Lon(0)));
        Assert.False(ClearanceLegEnd.IsOnTaxiway(g, "Z", RunwayFixture.Lat(0), RunwayFixture.Lon(150)));
    }

    // ---- a crossing already made ----

    // 09/27 along north 0 (pavement 30 m either side). Metres north of its centreline, at east 500.
    [Theory]
    // Pinned to its stop, 100 m south:
    [InlineData(-100.0, 150.0, true, ClearanceLegEnd.MadeCrossingEnd.AlreadyAcross)]   // across, clear of it
    [InlineData(-100.0, 20.0, false, ClearanceLegEnd.MadeCrossingEnd.FarSide)]         // still crossing: on across
    [InlineData(-100.0, -50.0, true, ClearanceLegEnd.MadeCrossingEnd.FarSide)]         // still on the near side
    // The review's jump: one edge from 60 m south to 60 m north, no stop, the aircraft now 150 m north and clear. Pinned
    // on its near side (the node before the runway), it is across; the far-side reach as a pin read north as the near
    // side and sent the leg back over.
    [InlineData(-60.0, 150.0, true, ClearanceLegEnd.MadeCrossingEnd.AlreadyAcross)]
    // A pin on the pavement (the route started on it, so its reach is the only node): no side to go by, even one 29 m
    // north that a runway table's half-width could read as off it.
    [InlineData(29.0, 35.0, false, ClearanceLegEnd.MadeCrossingEnd.Unknown)]
    [InlineData(0.0, 10.0, false, ClearanceLegEnd.MadeCrossingEnd.Unknown)]            // exactly on the centreline
    [InlineData(0.0, 150.0, true, ClearanceLegEnd.MadeCrossingEnd.AlreadyAcross)]      // and the aircraft clear: refused
    public void A_made_crossing_is_judged_from_the_side_of_its_pin_and_never_sent_back_over(
        double pinNorthM, double aircraftNorthM, bool aircraftClear, ClearanceLegEnd.MadeCrossingEnd end)
    {
        var shape = RunwayShape.For(RunwayFixture.EastWest());

        var judged = ClearanceLegEnd.JudgeMadeCrossing(shape, RunwayFixture.Lat(pinNorthM), RunwayFixture.Lon(500),
            RunwayFixture.Lat(aircraftNorthM), RunwayFixture.Lon(500), aircraftClear, pinIsReach: false);

        Assert.Equal(end, judged);
    }

    // The re-review's case: the aircraft itself was the last clear point before 09/27 (35 m south, inside the margin)
    // and the route's first node lies 60 m north of it, so the crossing has no stop and no node before it on the route:
    // its pin is the reach, BEYOND the runway. Read as a side, it sent the leg back over; a reach-only pin is trusted
    // only on the pavement, so Calculate refuses.
    [Fact]
    public void A_crossing_jumped_from_the_aircraft_is_refused_never_sent_back()
    {
        var route = RunwayFixture.RouteOf(RunwayFixture.Node(1, 500, 60), RunwayFixture.Node(2, 500, 150));
        var ev = Assert.Single(RunwayFixture.Pass(route, new[] { RunwayFixture.EastWest() },
            aircraft: new RouteRunwayCrossings.AircraftPosition(RunwayFixture.Lat(-35), RunwayFixture.Lon(500))));
        Assert.False(ev.Held);
        Assert.Equal(0, ev.ReachNodeIndex);
        var applied = Assert.Single(ClearedCrossingHolds.Apply(route, new[] { new ClearedCrossing("09", SeqIndex: 0) },
            recalculation: false).Applied);
        Assert.Equal(1, applied.ReachNodeId);
        Assert.Equal(0, applied.NearNodeId);

        var steps = new[]
        {
            new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "P"),
            new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "09"),
        };
        var pin = ClearanceTrim.Remaining(steps, 0, hasDestination: false, Array.Empty<ClearedCrossing>(),
            new[] { applied }, Array.Empty<ClearedCrossing>(), Array.Empty<ClearanceHold>(), false)[1];
        Assert.Equal(1, pin.AtNodeId);
        Assert.True(pin.AtNodeIsPassedStop);
        Assert.True(pin.AtNodeIsReach);

        var shape = RunwayShape.For(RunwayFixture.EastWest());
        double pinLat = RunwayFixture.Lat(60), lon = RunwayFixture.Lon(500);
        Assert.Equal(ClearanceLegEnd.MadeCrossingEnd.AlreadyAcross, ClearanceLegEnd.JudgeMadeCrossing(shape, pinLat, lon,
            RunwayFixture.Lat(150), lon, aircraftClearOfRunways: true, pinIsReach: true));
        Assert.Equal(ClearanceLegEnd.MadeCrossingEnd.Unknown, ClearanceLegEnd.JudgeMadeCrossing(shape, pinLat, lon,
            RunwayFixture.Lat(35), lon, aircraftClearOfRunways: false, pinIsReach: true));
        // Trusted as a side, the far-side reach read north as the near side.
        Assert.Equal(ClearanceLegEnd.MadeCrossingEnd.FarSide, ClearanceLegEnd.JudgeMadeCrossing(shape, pinLat, lon,
            RunwayFixture.Lat(35), lon, aircraftClearOfRunways: false, pinIsReach: false));
    }

    // The review's other reversal: the node nearest ahead is the taxiway's own last node, so its one arm walks back.
    // Only with the aircraft on the taxiway, where its heading is the real way it travels.
    [Fact]
    public void At_a_taxiway_end_the_aircraft_is_heading_off_the_leg_ends_there()
    {
        var g = Build(Path("P", 0, 0, 300, 0), Path("P", 300, 0, 600, 0));
        int east = NodeAt(g, 600, 0);
        double acLat = RunwayFixture.Lat(0), acLon = RunwayFixture.Lon(550);

        var end = ClearanceLegEnd.EndOfTaxiway(g, east, "P", 90, acLat, acLon, aircraftOnTaxiway: true);

        Assert.Equal(east, end.EndNodeId);
        // Heading back along it, the lone arm is the way on.
        Assert.Equal(NodeAt(g, 0, 0),
            ClearanceLegEnd.EndOfTaxiway(g, east, "P", 270, acLat, acLon, aircraftOnTaxiway: true).EndNodeId);
    }

    // The re-review's case: "via A, B" from an apron north of A, B starting at A and running north. The way the
    // aircraft comes is only the bearing to the A/B junction (about 200 deg), B leaves it at 0 deg: a turn, never a
    // reversal, so the leg runs to B's end.
    [Fact]
    public void A_turn_at_a_junction_is_never_read_as_a_reversal()
    {
        var g = Build(Path("A", -300, 0, 0, 0), Path("A", 0, 0, 100, 0), Path("A", 100, 0, 300, 0),
            Path("S", 100, 300, 100, 0), Path("B", 0, 0, 0, 600));
        int start = NodeAt(g, 100, 300);
        double acLat = RunwayFixture.Lat(290), acLon = RunwayFixture.Lon(100);
        var distances = new TaxiRouter(g).ComputeGraphDistancesFrom(start);

        var entry = ClearanceLegEnd.EntryOntoTaxiway(g, "B", previousTaxiway: "A", g.Nodes[start].ComponentId, distances,
            id => ClearanceLegEnd.NeedsReversal(g, start, id, 180), acLat, acLon, headingTrue: 180);

        Assert.Equal(NodeAt(g, 0, 0), entry.NodeId);
        Assert.False(entry.AircraftOnTaxiway);
        Assert.InRange(entry.TravelBearingTrue, 190.0, 210.0);
        Assert.Equal(NodeAt(g, 0, 600), ClearanceLegEnd.EndOfTaxiway(g, entry.NodeId, "B", entry.TravelBearingTrue,
            acLat, acLon, entry.AircraftOnTaxiway).EndNodeId);
        // Read as the aircraft's own heading on B, it ended the leg at the junction and B was never taxied.
        Assert.Equal(NodeAt(g, 0, 0), ClearanceLegEnd.EndOfTaxiway(g, entry.NodeId, "B", entry.TravelBearingTrue,
            acLat, acLon, aircraftOnTaxiway: true).EndNodeId);
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
