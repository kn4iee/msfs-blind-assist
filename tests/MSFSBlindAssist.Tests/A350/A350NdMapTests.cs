using System.Text.Json;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The A350's airport map (ANF) on the ND, at a zoom range on the ground at OMDB (live 2026-10-28). Its names draw as
// text once the draw code's PLANE LATITUDE/LONGITUDE are answered: taxiways grey, runway idents white, buildings cyan,
// the aircraft's position box yellow, the airport's name top right, the zoom range cyan. Fixtures: the captain's ND
// stopped on 12L (ARC, 2 NM) and at stand A5 (ARC, 0.5 NM); the first officer's ND at A5 (ARC, 0.5 NM).
public class A350NdMapTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    private static string Line(IReadOnlyList<string> lines, string label) => Assert.Single(lines, l => l.StartsWith(label + ": "));

    [Fact]
    public void Stopped_on_a_runway_the_map_reads_by_kind()
    {
        var lines = A350GroundDisplays.ReadNd(Fixture("efis0_stopped_omdb_12l"), 0);
        Assert.Equal("Airport: DUBAI INTL OMDB DXB", Line(lines, "Airport"));
        Assert.Equal("Position: 12L-30R, yellow", Line(lines, "Position"));
        Assert.Equal("Map range: 2 NM, cyan", Line(lines, "Map range"));
        var runways = Line(lines, "Runways");
        foreach (var r in new[] { "12L", "30R", "12R", "30L" })
            Assert.Contains(r, runways);
        Assert.EndsWith(", white", runways);
        var taxiways = Line(lines, "Taxiways");
        Assert.EndsWith(", grey", taxiways);
        Assert.Single(Regex.Matches(taxiways, @"\bM13\b"));   // drawn several times along the taxiway: named once
        Assert.Contains("M14A", taxiways);
        Assert.Contains("N10", taxiways);
    }

    // Nearest first: stopped about 3,775 m down 12L (590 m before its end), the 30R ident ahead is nearer than 12L's behind.
    [Fact]
    public void Names_are_nearest_the_aircraft_first()
    {
        var runways = Line(A350GroundDisplays.ReadNd(Fixture("efis0_stopped_omdb_12l"), 0), "Runways");
        Assert.True(runways.IndexOf("30R", StringComparison.Ordinal) < runways.IndexOf("12L", StringComparison.Ordinal), runways);
    }

    // The compass labels around the aircraft are a scale, never text; the messages under the map still read.
    [Fact]
    public void The_compass_labels_are_not_read_as_text()
    {
        var lines = A350GroundDisplays.ReadNd(Fixture("efis0_stopped_omdb_12l"), 0);
        Assert.DoesNotContain(lines, l => Regex.IsMatch(l, @"^(\d+(, white)?\s*)+$") || l.StartsWith("7 8 9") || l.Contains("10 11 12"));
        Assert.Contains(lines, l => l.Contains("NAV PRIMARY"));
    }

    [Fact]
    public void At_the_stand_the_first_officer_reads_the_stand_and_the_buildings()
    {
        var lines = A350GroundDisplays.ReadNd(Fixture("efis1_stand_omdb_a5"), 1);
        Assert.Equal("Position: A5, yellow", Line(lines, "Position"));
        Assert.Equal("Buildings: CONCOURSE A, cyan", Line(lines, "Buildings"));
        Assert.Equal("Map range: 0.5 NM, cyan", Line(lines, "Map range"));
        Assert.Contains("Ground speed: 0, green", lines);
    }

    // Without the map (no airport map names drawn), the ND reads as before: no map lines.
    [Fact]
    public void Without_the_map_there_are_no_map_lines()
    {
        var lines = A350GroundDisplays.ReadNd(Fixture("efis0_fd_stand"), 0);
        Assert.DoesNotContain(lines, l => l.StartsWith("Airport: ") || l.StartsWith("Taxiways: ") || l.StartsWith("Map range: "));
    }
}
