// Characterization tests for the two Progressive Taxi leg-end lookups on TaxiGraph:
// FindTaxiwayIntersectionNodes (every junction of the last taxiway with the hold-short target,
// none inside a runway's clear margin) and FindTaxiwayEndNodeClearOfRunways (the far end of
// the last taxiway, never on or beside a runway). Graphs are built with TaxiGraph.Build from
// plain TaxiPath lists, as HoldShortNodeResolverTests does; coordinates are metres east and
// north of RunwayFixture's base point.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiwayJunctionLookupTests
{
    private static TaxiPath Path(string name, double e1, double n1, double e2, double n2) => new()
    {
        StartLat = RunwayFixture.Lat(n1), StartLon = RunwayFixture.Lon(e1),
        EndLat = RunwayFixture.Lat(n2), EndLon = RunwayFixture.Lon(e2),
        Name = name, Type = "T", StartType = "N", EndType = "N", Width = 75,
    };

    private static TaxiGraph Build(List<TaxiPath> paths)
        => TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

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

    /// Taxiway A runs east along north = northM from east 0 to 600; taxiway B leaves it at east 200,
    /// runs 200 m south, east to 400 and back north, meeting A again at east 400.
    private static void AddLoop(List<TaxiPath> paths, double northM)
    {
        paths.Add(Path("A", 0, northM, 200, northM));
        paths.Add(Path("A", 200, northM, 400, northM));
        paths.Add(Path("A", 400, northM, 600, northM));
        paths.Add(Path("B", 200, northM, 200, northM - 200));
        paths.Add(Path("B", 200, northM - 200, 400, northM - 200));
        paths.Add(Path("B", 400, northM - 200, 400, northM));
    }

    [Fact]
    public void Every_junction_of_two_taxiways_is_returned()
    {
        var paths = new List<TaxiPath>();
        AddLoop(paths, 0);
        var g = Build(paths);
        int west = NodeAt(g, 200, 0), east = NodeAt(g, 400, 0);

        var junctions = g.FindTaxiwayIntersectionNodes("A", "B", g.Nodes[west].ComponentId);

        Assert.Equal(new[] { west, east }.OrderBy(id => id), junctions.OrderBy(id => id));
    }

    [Fact]
    public void Junctions_in_another_component_are_left_out()
    {
        var paths = new List<TaxiPath>();
        AddLoop(paths, 0);
        AddLoop(paths, 2000);
        var g = Build(paths);
        int west = NodeAt(g, 200, 0), east = NodeAt(g, 400, 0);
        int otherWest = NodeAt(g, 200, 2000);
        Assert.NotEqual(g.Nodes[west].ComponentId, g.Nodes[otherWest].ComponentId);

        var junctions = g.FindTaxiwayIntersectionNodes("A", "B", g.Nodes[west].ComponentId);

        Assert.Equal(new[] { west, east }.OrderBy(id => id), junctions.OrderBy(id => id));
    }

    [Fact]
    public void A_junction_inside_a_runway_margin_is_left_out()
    {
        var paths = new List<TaxiPath>();
        AddLoop(paths, 0);
        var g = Build(paths);
        // A north-south runway 20 m east of the east junction: that junction is on its pavement.
        g.RunwayCenterlines.Add(RunwayFixture.NorthSouth("18", "36", eastM: 420, fromNorthM: 500, toNorthM: -500));
        int west = NodeAt(g, 200, 0);

        var junctions = g.FindTaxiwayIntersectionNodes("A", "B", g.Nodes[west].ComponentId);

        Assert.Equal(new[] { west }, junctions);
    }

    [Fact]
    public void The_end_of_a_taxiway_that_runs_onto_a_runway_is_the_last_node_clear_of_it()
    {
        // Taxiway A runs east onto a north-south runway at east 300: the node at 270 is 30 m from the
        // centreline (inside the clear margin), the node at 300 on it. The last node clear is at 150.
        var g = Build(new List<TaxiPath>
        {
            Path("A", 0, 0, 150, 0),
            Path("A", 150, 0, 270, 0),
            Path("A", 270, 0, 300, 0),
        });
        g.RunwayCenterlines.Add(RunwayFixture.NorthSouth("18", "36", eastM: 300, fromNorthM: 500, toNorthM: -500));
        int start = NodeAt(g, 0, 0);

        Assert.Equal(NodeAt(g, 300, 0), g.FindTaxiwayEndNode(start, "A"));
        Assert.Equal(NodeAt(g, 150, 0), g.FindTaxiwayEndNodeClearOfRunways(start, "A"));
    }

    [Fact]
    public void A_taxiway_wholly_inside_a_runway_margin_has_no_end()
    {
        // The aircraft is on C, 200 m south; taxiway A lies along an east-west runway's centreline.
        var g = Build(new List<TaxiPath>
        {
            Path("C", 0, -200, 0, 0),
            Path("A", 0, 0, 150, 0),
            Path("A", 150, 0, 300, 0),
        });
        g.RunwayCenterlines.Add(RunwayFixture.EastWest(lengthM: 1000.0));
        int start = NodeAt(g, 0, -200);

        Assert.Equal(-1, g.FindTaxiwayEndNodeClearOfRunways(start, "A"));
    }
}
