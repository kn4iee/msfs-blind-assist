// Tests for MSFSBlindAssist.Navigation.ClearanceRows: the pilot's per-row picks follow the rows that
// remain when route loading removes rows from a clearance (a runway used as a taxi leg, a first
// taxiway the route never drives [RTE-39]).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceRowsTests
{
    [Fact]
    public void A_list_of_rows_moves_down_past_each_removed_row()
    {
        // Rows 0..4, row 1 removed: row 0 stays, rows 2..4 become 1..3.
        Assert.Equal(new List<int> { 0, 1, 3 }, ClearanceRows.Without(new List<int> { 0, 2, 4 }, new[] { 1 }));
    }

    [Fact]
    public void A_pick_on_a_removed_row_goes_with_it()
    {
        Assert.Equal(new List<int> { 1 }, ClearanceRows.Without(new List<int> { 1, 2 }, new[] { 1 }));
    }

    [Fact]
    public void Removing_the_leading_rows_moves_every_pick_down_by_that_many()
    {
        Assert.Equal(new List<int> { 0, 3 }, ClearanceRows.Without(new List<int> { 2, 5 }, ClearanceRows.Leading(2)));

        var picks = ClearanceRows.Without(new Dictionary<int, string> { [5] = "07C" }, ClearanceRows.Leading(2));
        Assert.Equal("07C", Assert.Single(picks!).Value);
        Assert.Equal(3, picks!.Keys.Single());
    }

    [Fact]
    public void Picks_of_any_kind_move_with_their_rows()
    {
        var picks = ClearanceRows.Without(
            new Dictionary<int, TurnSide> { [0] = TurnSide.Right, [2] = TurnSide.Left, [4] = TurnSide.Right }, new[] { 1, 3 });

        Assert.Equal(TurnSide.Right, picks![0]);
        Assert.Equal(TurnSide.Left, picks[1]);
        Assert.Equal(TurnSide.Right, picks[2]);
        Assert.Equal(3, picks.Count);
    }

    // The runway-leg pass removes rows walking from the end, so its list comes in descending order.
    [Fact]
    public void The_order_rows_were_removed_in_does_not_matter()
    {
        Assert.Equal(new List<int> { 0, 2 }, ClearanceRows.Without(new List<int> { 0, 4 }, new[] { 3, 1 }));
    }

    [Fact]
    public void Nothing_removed_changes_nothing()
    {
        Assert.Equal(new List<int> { 0, 2 }, ClearanceRows.Without(new List<int> { 0, 2 }, Array.Empty<int>()));
    }

    [Fact]
    public void Null_stays_null()
    {
        Assert.Null(ClearanceRows.Without((List<int>?)null, new[] { 0 }));
        Assert.Null(ClearanceRows.Without((Dictionary<int, string>?)null, new[] { 0 }));
    }

    // A pick on a removed row goes with it, so route loading must be able to say so.
    [Fact]
    public void CountOn_counts_the_picks_sitting_on_removed_rows_across_every_list()
    {
        Assert.Equal(3, ClearanceRows.CountOn(new[] { 1, 3 }, new[] { 1, 2 }, null, new[] { 3 }, new[] { 1 }));
        Assert.Equal(0, ClearanceRows.CountOn(new[] { 1 }, new[] { 0, 2 }, null));
        Assert.Equal(0, ClearanceRows.CountOn(Array.Empty<int>(), new[] { 0, 1 }));
    }

    [Fact]
    public void DroppedHoldShortsNote_says_a_hold_short_went_with_a_skipped_runway_leg()
    {
        Assert.Null(ClearanceRows.DroppedHoldShortsNote(0));
        Assert.Equal("The hold short set on a skipped runway leg was dropped.", ClearanceRows.DroppedHoldShortsNote(1));
        Assert.Equal("The hold shorts set on skipped runway legs were dropped.", ClearanceRows.DroppedHoldShortsNote(2));
    }

    // A crossing the pilot was cleared for is a clearance, not a stop: it stays when its row goes, with no row
    // of its own (SeqIndex -1), so it still clears the first crossing of its runway in route order. A later one
    // moves down with its row, and one entered with no row keeps none.
    [Fact]
    public void A_cleared_crossing_loses_only_its_row_when_that_row_goes()
    {
        var requests = new List<ClearedCrossing>
        {
            new("09", SeqIndex: 1),
            new("27R", At: "B", SeqIndex: 4),
            new("04L", AtNodeId: 77),
        };

        var rekeyed = ClearanceRows.Without(requests, new[] { 3, 1 });

        Assert.Equal(new ClearedCrossing("09", SeqIndex: -1), rekeyed![0]);
        Assert.Equal(new ClearedCrossing("27R", At: "B", SeqIndex: 2), rekeyed[1]);
        Assert.Equal(new ClearedCrossing("04L", AtNodeId: 77), rekeyed[2]);
        Assert.Equal(3, rekeyed.Count);

        var afterLeading = ClearanceRows.Without(requests, ClearanceRows.Leading(2));
        Assert.Equal(new[] { -1, 2, -1 }, afterLeading!.Select(c => c.SeqIndex));
        Assert.Null(ClearanceRows.Without((List<ClearedCrossing>?)null, new[] { 0 }));
    }

    [Fact]
    public void Leading_names_the_first_rows()
    {
        Assert.Equal(new[] { 0, 1, 2 }, ClearanceRows.Leading(3));
        Assert.Empty(ClearanceRows.Leading(0));
    }

    // ---- stops as lists: any number per taxiway, and stops with no taxiway of their own ----

    private static List<KeyValuePair<int, string>> Stops(params (int Row, string Name)[] stops)
        => stops.Select(s => new KeyValuePair<int, string>(s.Row, s.Name)).ToList();

    // Two stops after the same taxiway both move with it; a stop with no taxiway of its own (-1) never moves and never
    // goes; one on a removed row goes with it.
    [Fact]
    public void Stops_in_a_list_move_with_their_rows_and_a_stop_with_no_row_stays()
    {
        var stops = Stops((2, "22R"), (2, "B"), (-1, "04L"), (1, "09"), (4, "C"));

        var kept = ClearanceRows.Without(stops, new[] { 1 });

        Assert.Equal(Stops((1, "22R"), (1, "B"), (-1, "04L"), (3, "C")), kept);
        Assert.Null(ClearanceRows.Without((List<KeyValuePair<int, string>>?)null, new[] { 0 }));
    }

    [Fact]
    public void RowsOf_names_the_rows_stops_sit_on_never_a_stop_with_no_row()
    {
        Assert.Equal(new[] { 2, 2, 0 }, ClearanceRows.RowsOf(Stops((2, "22R"), (-1, "04L"), (2, "B"), (0, "C"))));
        Assert.Null(ClearanceRows.RowsOf((List<KeyValuePair<int, string>>?)null));

        // A stop with no row neither goes with a removed row nor keeps a first taxiway from being left out.
        Assert.Equal(1, ClearanceRows.CountOn(new[] { 0 }, ClearanceRows.RowsOf(Stops((-1, "04L"), (0, "C")))));
        Assert.True(UndrivenClearanceStart.PicksAllow(1, ClearanceRows.RowsOf(Stops((-1, "04L"), (1, "C")))));
    }

    // [RTE-39]: a first taxiway left out as never driven keeps the stops entered after it. "Via A, Hold short of runway
    // 09, Via B" trimmed with the aircraft on B keeps the finished A in front of the stop; the 09 stop, keyed to A, kept
    // A in the route, which went back along A behind the aircraft and started with a U-turn (final review, Important 1).
    // Left out, A's stops stay with no row of their own (-1), looked for along the whole route as a crossing is; a later
    // stop moves down with its row, and one with no row keeps none.
    [Fact]
    public void A_stop_on_a_taxiway_left_out_stays_with_no_row()
    {
        var stops = Stops((0, "09"), (0, "B"), (-1, "04L"), (1, "22R"), (2, "C"));

        var kept = ClearanceRows.WithoutKeepingStops(stops, ClearanceRows.Leading(1));

        Assert.Equal(Stops((-1, "09"), (-1, "B"), (-1, "04L"), (0, "22R"), (1, "C")), kept);
        Assert.Null(ClearanceRows.WithoutKeepingStops((List<KeyValuePair<int, string>>?)null, new[] { 0 }));
        // So a stop never keeps its taxiway in the route: only a Turn pick does (TurnPicksAllow, a row up to the count).
        Assert.False(UndrivenClearanceStart.TurnPicksAllow(1, new[] { 1 }));
        Assert.True(UndrivenClearanceStart.TurnPicksAllow(1, new[] { 2 }));
    }

    // A stop left with no row is numbered as entered again from the stops as they stood when its row was left out (the
    // trim matches a passed stop by its Via step), unless two alike say different rows: one entered with no row of its
    // own and one on the row left out cannot be told apart, and the stop keeps none (the trim then keeps the step).
    [Fact]
    public void A_stop_left_with_no_row_is_numbered_as_entered()
    {
        // Entered: A (row 0), 25 (a runway leg, row 1, skipped first), B, C. Stops: 09 after A, K after B, 04L before A.
        var asKeyed = PlacedStop.Entered(Stops((0, "09"), (-1, "04L")), Stops((1, "K")), null);
        Assert.Equal(new[]
        {
            new PlacedStop(0, ClearanceTargetKind.Runway, "09", 0, 0),
            new PlacedStop(-1, ClearanceTargetKind.Runway, "04L", 0, 0),
            new PlacedStop(1, ClearanceTargetKind.Taxiway, "K", 0, 0),
        }, asKeyed);

        var numbering = ClearanceNumbering.For(null).Removing(new[] { 1 }).Removing(ClearanceRows.Leading(1), asKeyed);

        Assert.Equal(0, numbering.AsEntered(new PlacedStop(-1, ClearanceTargetKind.Runway, "09", 0, 30)));
        Assert.Equal(-1, numbering.AsEntered(new PlacedStop(-1, ClearanceTargetKind.Runway, "04L", 0, 40)));
        Assert.Equal(2, numbering.AsEntered(new PlacedStop(0, ClearanceTargetKind.Taxiway, "K", 0, 50)));
        // Another kind, or a holding point's other node, is another stop.
        Assert.Equal(-1, numbering.AsEntered(new PlacedStop(-1, ClearanceTargetKind.Taxiway, "09", 0, 30)));
        var points = ClearanceNumbering.For(null).Removing(ClearanceRows.Leading(1),
            PlacedStop.Entered(null, null, new List<KeyValuePair<int, HoldingPointHoldShort>> { new(0, new HoldingPointHoldShort("L2", 77)) }));
        Assert.Equal(0, points.AsEntered(new PlacedStop(-1, ClearanceTargetKind.HoldingPoint, "L2", 77, 77)));
        Assert.Equal(-1, points.AsEntered(new PlacedStop(-1, ClearanceTargetKind.HoldingPoint, "L2", 78, 78)));

        // 09 entered twice: before any Via step and after A, which was left out.
        var twice = ClearanceNumbering.For(null).Removing(ClearanceRows.Leading(1),
            PlacedStop.Entered(Stops((-1, "09"), (0, "09")), null, null));
        Assert.Equal(-1, twice.AsEntered(new PlacedStop(-1, ClearanceTargetKind.Runway, "09", 0, 30)));
        // Nothing left out that way: a stop with no row keeps none.
        Assert.Equal(-1, ClearanceNumbering.For(null).AsEntered(new PlacedStop(-1, ClearanceTargetKind.Runway, "09", 0, 30)));
    }

    // A recalculation numbers the stops on the remaining rows from the first of them: one behind the aircraft goes,
    // one with no row of its own stays with none, and several on one row all stay.
    [Fact]
    public void After_keeps_the_stops_on_rows_still_ahead_renumbered_and_every_stop_with_no_row()
    {
        var stops = Stops((0, "A1"), (-1, "04L"), (2, "22R"), (2, "B"), (3, "C"));

        Assert.Equal(Stops((-1, "04L"), (0, "22R"), (0, "B"), (1, "C")), ClearanceRows.After(stops, 2));
        Assert.Equal(stops, ClearanceRows.After(stops, 0));
    }

    // ---- the numbering the window calculated in ----

    [Fact]
    public void AsEntered_puts_the_rows_route_loading_left_out_back_into_the_numbering()
    {
        // Rows 1 and 3 of 0..4 left out: rows 0, 1, 2 now are rows 0, 2, 4 as entered.
        var numbering = ClearanceNumbering.For(null).Removing(new[] { 3, 1 });

        Assert.Equal(new[] { 0, 2, 4 }, new[] { 0, 1, 2 }.Select(numbering.AsEntered));
        Assert.Equal(-1, numbering.AsEntered(-1));
        Assert.Equal(new[] { 1, 3 }, numbering.RemovedRows);
    }

    // A runway leg is skipped first (in the numbering as entered), then the first taxiways never driven (in the
    // numbering left after that): both are put back as entered.
    [Fact]
    public void Removals_in_two_passes_are_both_numbered_as_entered()
    {
        // Entered: E7, 25 (a runway leg), E8, D6, C. After the runway leg: E7, E8, D6, C; after two undriven: D6, C.
        var numbering = ClearanceNumbering.For(null).Removing(new[] { 1 }).Removing(ClearanceRows.Leading(2));

        Assert.Equal(new[] { 0, 1, 2 }, numbering.RemovedRows);
        Assert.Equal(3, numbering.AsEntered(0));
        Assert.Equal(4, numbering.AsEntered(1));
    }

    // A crossing keeps the Via step it was entered after; one whose row was left out, which routing holds with no row
    // (-1), gets that row back from the crossings as entered, unless two entered alike say different rows.
    [Fact]
    public void A_crossing_is_numbered_as_entered_even_when_its_row_was_left_out()
    {
        var entered = new List<ClearedCrossing>
        {
            new("09", SeqIndex: 0),
            new("27R", At: "B", SeqIndex: 3),
            new("04L"),
        };
        var numbering = ClearanceNumbering.For(entered).Removing(ClearanceRows.Leading(1));

        Assert.Equal(new ClearedCrossing("09", StopNodeId: 5, SeqIndex: 0),
            numbering.AsEntered(new ClearedCrossing("09", StopNodeId: 5, SeqIndex: -1)));
        Assert.Equal(new ClearedCrossing("27R", At: "B", SeqIndex: 3),
            numbering.AsEntered(new ClearedCrossing("27R", At: "B", SeqIndex: 2)));
        Assert.Equal(new ClearedCrossing("04L", StopNodeId: 8),
            numbering.AsEntered(new ClearedCrossing("04L", StopNodeId: 8)));
        // A Continue's crossing stands for no step: left as it is.
        var byContinue = new ClearedCrossing("09", StopNodeId: 5, Origin: ClearedBy.Continue);
        Assert.Equal(byContinue, numbering.AsEntered(byContinue));

        // Entered twice alike, once with no row and once on the row left out: which is which cannot be told.
        var twice = ClearanceNumbering.For(new List<ClearedCrossing> { new("09"), new("09", SeqIndex: 0) })
            .Removing(ClearanceRows.Leading(1));
        Assert.Equal(-1, twice.AsEntered(new ClearedCrossing("09", SeqIndex: -1)).SeqIndex);
    }
}
