using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The one ordered list of runway entries (Full length, intersections, holding points).</summary>
public class RunwayEntryListTests
{
    private const double RunwayLength = 4000.0;
    private const double MetresPerDegLat = 111132.0;
    // A north-running runway from (50, 8) to RunwayLength metres further north.
    private const double StartLat = 50.0, Lon = 8.0;
    private static readonly double EndLat = StartLat + RunwayLength / MetresPerDegLat;

    private static string Dist(double m) => FormattableString.Invariant($"{m:0} metres");

    private static TaxiGraph.RunwayIntersection Entry(string name, double along, double remaining) =>
        new()
        {
            TaxiwayName = name,
            Latitude = StartLat + along / MetresPerDegLat,
            Longitude = Lon,
            AlongMetersFromThreshold = along,
            RemainingMeters = remaining,
        };

    private static IReadOnlyList<RunwayEntryItem> Build(
        IReadOnlyList<TaxiGraph.RunwayIntersection> intersections,
        IReadOnlyList<(TaxiGraph.RunwayIntersection Entry, string Kind, bool Partway)> holds,
        Func<double, string>? distance = null) =>
        RunwayEntryList.Build(intersections, holds, StartLat, Lon, EndLat, Lon, distance ?? Dist);

    [Fact]
    public void Full_length_comes_first_even_with_nothing_to_offer()
    {
        var items = Build([], []);
        var only = Assert.Single(items);
        Assert.Equal(RunwayEntryKind.FullLength, only.Kind);
        Assert.Equal("Full length", only.Label);
        Assert.Equal("Full length", only.Name);
        Assert.Null(only.Entry);
    }

    [Fact]
    public void Entries_are_merged_in_order_down_the_runway()
    {
        var b3 = Entry("B3", 650, 3350);
        var a1 = Entry("A1", 0, 4000);
        var a0 = Entry("A0", -40, 4040);
        var k2 = Entry("K2", 900, 3100);

        var items = Build(
            [b3],
            [(k2, "intermediate", true), (a1, "runway", false), (a0, "runway", false)]);

        Assert.Equal(
            new[]
            {
                "Full length",
                "A0 (holding point), 4040 metres remaining",
                "A1 (holding point), 4000 metres remaining",
                "B3, 3350 metres remaining",
                "K2 (holding point, intermediate hold), 3100 metres remaining",
            },
            items.Select(i => i.Label).ToArray());
        Assert.Equal(RunwayEntryKind.Intersection, items[3].Kind);
        Assert.Equal(RunwayEntryKind.HoldingPoint, items[4].Kind);
        Assert.True(items[4].Partway);
        Assert.Equal("intermediate", items[4].HoldKind);
        Assert.Same(k2, items[4].Entry);
        Assert.Equal("K2", items[4].Name);
    }

    [Fact]
    public void A_set_back_hold_keeps_its_kind()
    {
        var a1 = Entry("A1", 0, 3600);
        var a3 = Entry("A3", 0, 3600);

        var items = Build([], [(a1, "runway", false), (a3, "ils", false)]);

        Assert.Equal("A1 (holding point), 3600 metres remaining", items[1].Label);
        Assert.Equal("A3 (holding point, ILS hold), 3600 metres remaining", items[2].Label);
    }

    [Fact]
    public void An_unknown_kind_reads_as_a_plain_holding_point()
    {
        var items = Build([], [(Entry("C1", 100, 3900), "", false)]);
        Assert.Equal("C1 (holding point), 3900 metres remaining", items[1].Label);
    }

    [Fact]
    public void Ties_put_intersections_before_holding_points_then_label_order()
    {
        var items = Build(
            [Entry("Z9", 500, 3500)],
            [(Entry("B1", 500, 3500), "runway", false), (Entry("A1", 500, 3500), "runway", false)]);

        Assert.Equal(
            new[] { "Z9", "A1 (holding point)", "B1 (holding point)" },
            items.Skip(1).Select(i => i.Label.Split(',')[0]).ToArray());
    }

    [Fact]
    public void Two_meetings_of_one_taxiway_stay_two_items()
    {
        var items = Build([Entry("B", 600, 3400), Entry("B", 1800, 2200)], []);

        Assert.Equal(
            new[] { "Full length", "B, 3400 metres remaining", "B, 2200 metres remaining" },
            items.Select(i => i.Label).ToArray());
    }

    [Fact]
    public void A_label_seen_twice_keeps_the_first()
    {
        var first = Entry("B3", 650, 3350);
        var second = Entry("B3", 652, 3350);

        var items = Build([first, second], []);

        Assert.Equal(2, items.Count);
        Assert.Same(first, items[1].Entry);
    }

    [Fact]
    public void IndexToKeep_finds_a_kept_holding_point_after_a_rebuild_and_gives_minus_one_when_gone()
    {
        var before = Build([Entry("B3", 650, 3350)], [(Entry("A1", 0, 4000), "runway", false)]);
        var kept = before[1]; // A1

        var after = Build([Entry("B3", 650, 3350)], [(Entry("A1", 0, 3990), "runway", false)]);
        Assert.Equal(1, RunwayEntryList.IndexToKeep(after, kept));

        var gone = Build([Entry("B3", 650, 3350)], []);
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(gone, kept));
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(after, null));
        Assert.Equal(0, RunwayEntryList.IndexToKeep(after, after[0]));
    }

    [Fact]
    public void IndexToKeep_tells_a_pair_at_one_entrance_apart_by_hold_kind()
    {
        var items = Build([], [(Entry("A1", 0, 3600), "runway", false), (Entry("A1", 0, 3600), "ils", false)]);
        var ils = items.Single(i => i.HoldKind == "ils");
        Assert.Equal(items.ToList().IndexOf(ils), RunwayEntryList.IndexToKeep(items, ils));
    }

    [Fact]
    public void NoLongerOffered_says_it_once_and_names_the_fallback()
    {
        var gone = Build([Entry("B3", 650, 3350)], [])[1];
        Assert.Equal("B3 is no longer offered. Full length departure.", RunwayEntryList.NoLongerOffered(gone));
    }

    [Fact]
    public void Labels_use_the_invariant_culture()
    {
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUiCulture = CultureInfo.CurrentUICulture;
        var oldProvider = DistanceFormatter.UnitProvider;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;

            var items = RunwayEntryList.Build(
                [Entry("B3", 650, 3350.5)],
                [(Entry("A1", 0, 4000), "runway", false)],
                StartLat, Lon, EndLat, Lon, m => DistanceFormatter.FromMetres(m));

            foreach (var item in items)
            {
                Assert.DoesNotMatch(@"\d,\d", item.Label);
                Assert.DoesNotMatch(@"\d\.\d{3}", item.Label);
            }
            Assert.Contains("3350 metres", items.Single(i => i.Kind == RunwayEntryKind.Intersection).Label);
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUiCulture;
            DistanceFormatter.UnitProvider = oldProvider;
        }
    }
}
