using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The one ordered list of runway entries (Full length, intersections, holding points).
/// Labels_use_the_invariant_culture sets DistanceFormatter.UnitProvider, which is process-global, hence the shared
/// collection (DistanceUnitGlobalStateCollection).</summary>
[Collection("DistanceUnitGlobalState")]
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

    // Two meetings of one taxiway share kind, name and hold kind: a refill keeps the one whose runway remaining is
    // nearest the kept one's, never the first, which would move the departure to the other meeting.
    [Fact]
    public void IndexToKeep_keeps_the_second_meeting_of_a_taxiway_after_a_rebuild()
    {
        var before = Build([Entry("B", 600, 3400), Entry("B", 1800, 2200)], []);
        var second = before[2];
        var first = before[1];

        var after = Build([Entry("B", 605, 3390), Entry("B", 1805, 2190)], []);
        Assert.Equal(2, RunwayEntryList.IndexToKeep(after, second));
        Assert.Equal(1, RunwayEntryList.IndexToKeep(after, first));
    }

    // Review Focus 5: a meeting that is gone is gone, never replaced by the other meeting of its taxiway. With only
    // the second meeting offered, the first is not kept (the departure would move 1,210 m down the runway, the unsafe
    // direction): the list goes to Full length and says so. The second is still kept.
    [Fact]
    public void IndexToKeep_never_replaces_a_lost_meeting_with_the_other()
    {
        var before = Build([Entry("B", 600, 3400), Entry("B", 1800, 2200)], []);
        var first = before[1];
        var second = before[2];

        var onlySecond = Build([Entry("B", 1805, 2190)], []);
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(onlySecond, first));
        Assert.Equal(1, RunwayEntryList.IndexToKeep(onlySecond, second));

        var onlyFirst = Build([Entry("B", 605, 3390)], []);
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(onlyFirst, second));
    }

    // [ENT-2]: a refill moves one entry's remaining runway by at most 300 m (its pavement entry on the aircraft's
    // side), so the same entry moved by less is kept, and a match further away is another entry.
    [Fact]
    public void IndexToKeep_keeps_an_entry_moved_by_under_300_metres_and_no_further()
    {
        var kept = Build([Entry("B3", 650, 3350)], [])[1];

        Assert.Equal(1, RunwayEntryList.IndexToKeep(Build([Entry("B3", 900, 3100)], []), kept));
        Assert.Equal(1, RunwayEntryList.IndexToKeep(Build([Entry("B3", 950, 3050)], []), kept));
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(Build([Entry("B3", 951, 3049)], []), kept));
        Assert.Equal(300.0, RunwayEntryList.MaxRefillMoveMetres);
    }

    // A remaining runway that is not a number cannot show that a match is the same meeting: never kept.
    [Fact]
    public void IndexToKeep_never_keeps_by_a_remaining_that_is_not_a_number()
    {
        var kept = Build([Entry("B3", 650, double.NaN)], [], m => "?")[1];
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(Build([Entry("B3", 650, 3350)], []), kept));

        var sound = Build([Entry("B3", 650, 3350)], [])[1];
        Assert.Equal(-1, RunwayEntryList.IndexToKeep(Build([Entry("B3", 650, double.NaN)], [], m => "?"), sound));

        // Full length has no entry and is always kept.
        var items = Build([Entry("B3", 650, 3350)], []);
        Assert.Equal(0, RunwayEntryList.IndexToKeep(items, items[0]));
    }

    // ---- Refill: what a refill of the shown list does (Review Focus 5) ----

    // The chosen entry is gone: the list goes to Full length and says so once.
    [Fact]
    public void A_refill_that_loses_the_chosen_entry_goes_to_full_length_and_says_so()
    {
        var shown = Build([Entry("B3", 650, 3350)], []);
        var rebuilt = Build([], []);

        var refill = RunwayEntryList.Refill(shown, 1, rebuilt, keepSelection: true);

        Assert.True(refill.Rebuild);
        Assert.Equal(0, refill.SelectIndex);
        Assert.Equal("B3 is no longer offered. Full length departure.", refill.Speech);
    }

    // The chosen entry is still offered at another place in the list: it stays chosen, nothing said.
    [Fact]
    public void A_refill_keeps_a_chosen_entry_that_moved_without_a_word()
    {
        var shown = Build([Entry("B3", 650, 3350)], [(Entry("A1", 0, 4000), "runway", false)]);
        int chosen = shown.ToList().FindIndex(i => i.Name == "B3");
        var rebuilt = Build([Entry("A2", 300, 3700), Entry("B3", 660, 3340)], [(Entry("A1", 0, 4000), "runway", false)]);

        var refill = RunwayEntryList.Refill(shown, chosen, rebuilt, keepSelection: true);

        Assert.True(refill.Rebuild);
        Assert.Equal(rebuilt.ToList().FindIndex(i => i.Name == "B3"), refill.SelectIndex);
        Assert.NotEqual(chosen, refill.SelectIndex);
        Assert.Null(refill.Speech);
    }

    // The same lines: the list is left as it is, the choice with it.
    [Fact]
    public void A_refill_with_the_same_lines_leaves_the_list_alone()
    {
        var shown = Build([Entry("B3", 650, 3350)], [(Entry("A1", 0, 4000), "runway", false)]);
        var rebuilt = Build([Entry("B3", 650, 3350.2)], [(Entry("A1", 0, 4000), "runway", false)]);

        var refill = RunwayEntryList.Refill(shown, 2, rebuilt, keepSelection: true);

        Assert.False(refill.Rebuild);
        Assert.Equal(2, refill.SelectIndex);
        Assert.Null(refill.Speech);
    }

    // A rebuild that does not keep the choice (a runway picked, a type change, an airport load, a restore) starts at
    // Full length without a word, whatever was chosen.
    [Fact]
    public void A_rebuild_without_keeping_starts_at_full_length_silently()
    {
        var shown = Build([Entry("B3", 650, 3350)], []);

        var same = RunwayEntryList.Refill(shown, 1, Build([Entry("B3", 650, 3350)], []), keepSelection: false);
        Assert.False(same.Rebuild);
        Assert.Equal(0, same.SelectIndex);
        Assert.Null(same.Speech);

        var gone = RunwayEntryList.Refill(shown, 1, Build([], []), keepSelection: false);
        Assert.True(gone.Rebuild);
        Assert.Equal(0, gone.SelectIndex);
        Assert.Null(gone.Speech);
    }

    // Full length chosen is never "no longer offered"; an empty list (never built) selects nothing.
    [Fact]
    public void A_refill_from_full_length_or_from_nothing_says_nothing()
    {
        var shown = Build([Entry("B3", 650, 3350)], []);
        var fromFull = RunwayEntryList.Refill(shown, 0, Build([], []), keepSelection: true);
        Assert.Equal(0, fromFull.SelectIndex);
        Assert.Null(fromFull.Speech);

        var first = RunwayEntryList.Refill(Array.Empty<RunwayEntryItem>(), -1, Build([], []), keepSelection: true);
        Assert.True(first.Rebuild);
        Assert.Equal(0, first.SelectIndex);
        Assert.Null(first.Speech);

        var none = RunwayEntryList.Refill(shown, 1, Array.Empty<RunwayEntryItem>(), keepSelection: true);
        Assert.Equal(-1, none.SelectIndex);
    }

    // The hold kind is kept as the label reads it (trimmed, lower case), so the key a refill matches on is the label's.
    [Fact]
    public void The_hold_kind_is_normalised_where_the_item_is_made()
    {
        var items = Build([], [(Entry("A3", 0, 3600), " ILS ", false), (Entry("A1", 0, 3600), "Runway", false)]);

        var a3 = items.Single(i => i.Name == "A3");
        Assert.Equal("ils", a3.HoldKind);
        Assert.Equal("A3 (holding point, ILS hold), 3600 metres remaining", a3.Label);
        Assert.Equal("runway", items.Single(i => i.Name == "A1").HoldKind);

        var made = new RunwayEntryItem(RunwayEntryKind.HoldingPoint, "A3", a3.Entry, false, "Ils");
        Assert.Equal("ils", made.HoldKind);
        Assert.Equal(items.ToList().IndexOf(a3), RunwayEntryList.IndexToKeep(items, made));
        Assert.Equal("", new RunwayEntryItem(RunwayEntryKind.FullLength, "Full length", null, false, null!).HoldKind);
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
