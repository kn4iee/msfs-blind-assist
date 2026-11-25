// Tests for MSFSBlindAssist.Navigation.ClearanceProgressState: what guidance reports back to the Taxi Guidance
// window about the clearance it is flying (TaxiGuidanceManager.GetClearanceProgress), numbered as the window last
// calculated, in the shapes ClearanceTrim.Remaining takes.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceProgressTests
{
    /// <summary>A route through the given node ids.</summary>
    private static List<TaxiRouteSegment> Through(params int[] nodeIds)
    {
        var list = new List<TaxiRouteSegment>();
        for (int i = 1; i < nodeIds.Length; i++)
            list.Add(new TaxiRouteSegment
            {
                FromNode = new TaxiNode { NodeId = nodeIds[i - 1] },
                ToNode = new TaxiNode { NodeId = nodeIds[i] },
                TaxiwayName = "A",
            });
        return list;
    }

    private static readonly List<TaxiRouteSegment> Route = Through(10, 20, 30, 40, 50);

    // ---- when a stop is behind ----

    // A stop at the end of segment s is passed once the cursor is past it: the cursor stands on segment s + 1 while
    // the aircraft holds there, and that is not passed; once it rolls on, it is.
    [Fact]
    public void A_stop_is_passed_once_the_aircraft_has_rolled_past_its_line()
    {
        // Node 30 ends segment 1.
        Assert.False(ClearanceProgressState.StopPassed(Route, 30, cursor: 1, holding: false));
        Assert.False(ClearanceProgressState.StopPassed(Route, 30, cursor: 2, holding: true));
        Assert.True(ClearanceProgressState.StopPassed(Route, 30, cursor: 2, holding: false));
        Assert.True(ClearanceProgressState.StopPassed(Route, 30, cursor: 3, holding: true));
    }

    [Fact]
    public void A_start_hold_is_passed_once_the_aircraft_is_off_the_first_segment()
    {
        Assert.False(ClearanceProgressState.StopPassed(Route, 10, cursor: 0, holding: false));
        Assert.True(ClearanceProgressState.StopPassed(Route, 10, cursor: 1, holding: false));
    }

    [Fact]
    public void A_stop_not_on_the_route_is_never_passed()
    {
        Assert.False(ClearanceProgressState.StopPassed(Route, 99, cursor: 4, holding: false));
        Assert.False(ClearanceProgressState.StopPassed(Route, 0, cursor: 4, holding: false));
        Assert.False(ClearanceProgressState.StopPassed(null, 30, cursor: 4, holding: false));
    }

    // ---- Via steps behind, in the window's numbering ----

    [Fact]
    public void Via_steps_done_are_counted_as_entered_with_the_rows_left_out_before_the_aircraft()
    {
        // Entered A, 25 (a runway leg, skipped), B, C; the route's sequence is A, B, C and the aircraft is on C.
        var state = ClearanceProgressState.For(null).WithRowsLeftOut(new[] { 1 });

        var progress = state.Report(2, turnOntoItMade: true, Array.Empty<ClearedCrossing>(), Route, 2, holding: false);

        Assert.Equal(3, progress.ViaStepsDone);
        Assert.True(progress.TurnOntoFirstRemainingMade);
    }

    // Nothing known done is nothing done: a first taxiway left out as never driven stays in the list until the
    // aircraft is past a taxiway guidance does drive, and the turn judged on that taxiway is not the left-out one's.
    [Fact]
    public void Nothing_done_on_the_route_trims_nothing_even_with_leading_rows_left_out()
    {
        var state = ClearanceProgressState.For(null).WithRowsLeftOut(ClearanceRows.Leading(2));

        var progress = state.Report(0, turnOntoItMade: true, Array.Empty<ClearedCrossing>(), Route, 2, holding: false);

        Assert.Equal(0, progress.ViaStepsDone);
        Assert.False(progress.TurnOntoFirstRemainingMade);

        var plain = ClearanceProgressState.For(null).Report(0, turnOntoItMade: true, Array.Empty<ClearedCrossing>(), Route, 2, false);
        Assert.True(plain.TurnOntoFirstRemainingMade);
    }

    // ---- crossings: in force, used, put back ----

    [Fact]
    public void Only_crossings_the_pilot_entered_are_reported_and_each_is_numbered_as_entered()
    {
        var entered = new List<ClearedCrossing> { new("04L", SeqIndex: 0), new("22R", SeqIndex: 2) };
        var state = ClearanceProgressState.For(entered).WithRowsLeftOut(ClearanceRows.Leading(1));
        var inForce = new List<ClearedCrossing>
        {
            new("04L", StopNodeId: 30, SeqIndex: -1),          // its row was left out
            new("22R", StopNodeId: 40, SeqIndex: 1),
            new("09", StopNodeId: 50, Origin: ClearedBy.Continue),
        };

        var progress = state.Report(0, false, inForce, Route, 0, holding: false);

        Assert.Equal(new[] { new ClearedCrossing("04L", StopNodeId: 30, SeqIndex: 0), new ClearedCrossing("22R", StopNodeId: 40, SeqIndex: 2) },
            progress.ClearedInForce);
        Assert.Empty(progress.ClearedUsed);
        Assert.Empty(progress.PutBack);
    }

    // Used at its line, a crossing with no stop of its own included: kept as used until the next load.
    [Fact]
    public void Used_crossings_stay_reported_as_used()
    {
        var state = ClearanceProgressState.For(null)
            .WithUsed(new[] { new ClearedCrossing("04L", StopNodeId: 30, SeqIndex: 0) })
            .WithUsed(new[] { new ClearedCrossing("27", ReachNodeId: 40, SeqIndex: 1), new ClearedCrossing("09", StopNodeId: 50, Origin: ClearedBy.Continue) });

        var progress = state.Report(0, false, Array.Empty<ClearedCrossing>(), Route, 4, holding: false);

        Assert.Equal(new[] { new ClearedCrossing("04L", StopNodeId: 30, SeqIndex: 0), new ClearedCrossing("27", ReachNodeId: 40, SeqIndex: 1) },
            progress.ClearedUsed);
    }

    // The Hold short key puts a stop back: its crossing is put back, and no longer used even when its line was said
    // as passed (the segment jump says a line early). Then Continue clears that stop again (cleared, so in force), or
    // the aircraft passes it (the crossing is made, so used).
    [Fact]
    public void A_crossing_put_back_stays_put_back_until_cleared_again_or_passed()
    {
        var crossing = new ClearedCrossing("04L", StopNodeId: 40, SeqIndex: 1);
        var state = ClearanceProgressState.For(null)
            .WithUsed(new[] { crossing })
            .WithPutBack(new[] { crossing }, stopNodeId: 40);

        var held = state.Report(0, false, Array.Empty<ClearedCrossing>(), Route, 3, holding: true);
        Assert.Equal(new[] { crossing }, held.PutBack);
        Assert.Empty(held.ClearedUsed);
        Assert.Empty(held.ClearedInForce);

        var clearedAgain = state.Report(0, false,
            new[] { new ClearedCrossing("04L", StopNodeId: 40, Origin: ClearedBy.Continue) }, Route, 2, holding: false);
        Assert.Equal(new[] { crossing }, clearedAgain.ClearedInForce);
        Assert.Empty(clearedAgain.PutBack);

        var passed = state.Report(0, false, Array.Empty<ClearedCrossing>(), Route, 3, holding: false);
        Assert.Equal(new[] { crossing }, passed.ClearedUsed);
        Assert.Empty(passed.PutBack);
    }

    // ---- the pilot's own stops ----

    [Fact]
    public void A_stop_the_pilot_set_is_reported_passed_as_the_step_it_came_from()
    {
        var state = ClearanceProgressState.For(null).WithRowsLeftOut(new[] { 0 }).WithPlaced(new[]
        {
            new PlacedStop(0, ClearanceTargetKind.Runway, "22R", PointNodeId: 0, StopNodeId: 20),
            new PlacedStop(-1, ClearanceTargetKind.HoldingPoint, "L2", PointNodeId: 30, StopNodeId: 30),
            new PlacedStop(1, ClearanceTargetKind.Taxiway, "B", PointNodeId: 0, StopNodeId: 40),
        });

        var progress = state.Report(0, false, Array.Empty<ClearedCrossing>(), Route, 3, holding: true);

        Assert.Equal(new[]
        {
            new ClearanceHold(1, new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "22R")),
            new ClearanceHold(-1, new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint, "L2", NodeId: 30)),
        }, progress.HoldsPassed);
    }

    // A recalculation replaces the route: what was passed on the route being replaced is kept as passed, a crossing
    // put back and passed becomes used, and the stops placed on it go (the new route records its own).
    [Fact]
    public void Settling_before_a_new_route_keeps_what_was_passed()
    {
        var putBack = new ClearedCrossing("04L", StopNodeId: 30, SeqIndex: 0);
        var ahead = new ClearedCrossing("22R", StopNodeId: 50, SeqIndex: 1);
        var passedStop = new PlacedStop(0, ClearanceTargetKind.Taxiway, "B", 0, 20);
        var state = ClearanceProgressState.For(null)
            .WithPutBack(new[] { putBack, ahead }, stopNodeId: 30)
            .WithPlaced(new[] { passedStop, new PlacedStop(1, ClearanceTargetKind.Taxiway, "C", 0, 50) });

        var settled = state.Settled(Route, cursor: 3, holding: false);

        Assert.Equal(new[] { passedStop }, settled.Passed);
        Assert.Empty(settled.Placed);
        Assert.Equal(new[] { putBack }, settled.Used);
        Assert.Equal(new[] { ahead }, settled.PutBack);

        // On the new route none of those nodes need be: what was passed stays reported.
        var report = settled.Report(0, false, Array.Empty<ClearedCrossing>(), Through(70, 80), 0, holding: false);
        Assert.Equal(new[] { new ClearanceHold(0, new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "B")) },
            report.HoldsPassed);
        Assert.Equal(new[] { putBack }, report.ClearedUsed);
    }

    // ---- the whole loop: what Remaining makes of it ----

    // Via A, hold short of runway 22R, via B, cross runway 04L, via C; the aircraft on C has passed the 22R stop and
    // crossed 04L: only Via C is still ahead.
    [Fact]
    public void Remaining_trims_the_steps_guidance_reports_behind()
    {
        var steps = new List<ClearanceStep>
        {
            new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "A"),
            new(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "22R"),
            new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "B"),
            new(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "04L"),
            new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "C"),
        };
        var plan = ClearancePlanner.Build(steps, hasDestination: true);
        var state = ClearanceProgressState.For(plan.Crossings)
            .WithPlaced(new[] { new PlacedStop(plan.Holds[0].SeqIndex, ClearanceTargetKind.Runway, "22R", 0, 20) })
            .WithUsed(new[] { plan.Crossings[0] with { StopNodeId = 40 } });

        var p = state.Report(2, turnOntoItMade: false, Array.Empty<ClearedCrossing>(), Route, 4, holding: false);
        var remaining = ClearanceTrim.Remaining(steps, p.ViaStepsDone, hasDestination: true,
            p.ClearedInForce, p.ClearedUsed, p.PutBack, p.HoldsPassed, p.TurnOntoFirstRemainingMade);

        Assert.Equal(new[] { "Via C" }, remaining.Select(s => s.Sentence()));
    }

    // Ruling 5: a Turn pick is keyed by its own Via step's place in the list being calculated, so a finished Via step
    // the trim keeps in front of a stop (so the stop is never made the limit) keeps the pick on its own taxiway: Via A,
    // hold short of taxiway B, Left onto C, on a leg with no destination, the aircraft past A and short of the turn.
    [Fact]
    public void A_finished_Via_step_kept_in_front_of_a_stop_keeps_the_Turn_pick_on_its_own_taxiway()
    {
        var steps = new List<ClearanceStep>
        {
            new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "A"),
            new(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "B"),
            new(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Taxiway, "C"),
        };
        var before = ClearancePlanner.Build(steps, hasDestination: false);
        Assert.Equal(new[] { (1, TurnSide.Left) }, before.TurnPicks.Select(t => (t.Key, t.Value)));

        var p = ClearanceProgressState.For(before.Crossings)
            .Report(1, turnOntoItMade: false, Array.Empty<ClearedCrossing>(), Route, 1, holding: false);
        var trimmed = ClearanceTrim.Remaining(steps, p.ViaStepsDone, hasDestination: false,
            p.ClearedInForce, p.ClearedUsed, p.PutBack, p.HoldsPassed, p.TurnOntoFirstRemainingMade);
        Assert.Equal(new[] { "Via A", "Hold short of taxiway B", "Left onto C" }, trimmed.Select(s => s.Sentence()));

        var after = ClearancePlanner.Build(trimmed, hasDestination: false);
        Assert.Equal(new[] { "A", "C" }, after.TaxiwaySequence);
        Assert.Equal(new[] { (1, TurnSide.Left) }, after.TurnPicks.Select(t => (t.Key, t.Value)));
        Assert.Equal(0, Assert.Single(after.Holds).SeqIndex);
    }

    // ---- fix round 1: progress follows the route ----

    // "Via B, A, C", where C crosses B before the B to A turn. Passing that junction on B the position finds C within
    // 50 m and reads B and A as done; the route's own cursor has not reached A's run, so nothing is. Rows are done only
    // once the NEXT row's run starts at or before the cursor.
    [Fact]
    public void Via_steps_still_ahead_on_the_route_are_never_done()
    {
        string[] sequence = { "B", "A", "C" };
        // On the route: B from segment 0, A from 5, C from 7 (C's junction with B, at segment 2's end, is no run of C).
        int RunStart(int row) => row switch { 0 => 0, 1 => 5, 2 => 7, _ => -1 };
        int byPosition = ClearanceTrim.ViaStepsDone(sequence, new[] { "C" });
        Assert.Equal(2, byPosition);

        Assert.Equal(0, ClearanceTrim.ViaStepsDoneOnRoute(0, sequence.Length, RunStart, cursor: 3));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(sequence, new[] { "C" }, doneOnRoute: 0));
        Assert.Equal(1, ClearanceTrim.ViaStepsDoneOnRoute(0, sequence.Length, RunStart, cursor: 5));
        Assert.Equal(2, ClearanceTrim.ViaStepsDoneOnRoute(0, sequence.Length, RunStart, cursor: 7));
        // The cap never adds what the position does not know: a repeated taxiway still trims nothing.
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(new[] { "A", "C", "A" }, new[] { "A" }, doneOnRoute: 2));

        var steps = sequence.Select(n => new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, n)).ToList();
        var remaining = ClearanceTrim.Remaining(steps, ClearanceTrim.ViaStepsDone(sequence, new[] { "C" }, doneOnRoute: 0),
            hasDestination: true, Array.Empty<ClearedCrossing>(), Array.Empty<ClearedCrossing>(),
            Array.Empty<ClearedCrossing>(), Array.Empty<ClearanceHold>(), false);
        Assert.Equal(new[] { "Via B", "Via A", "Via C" }, remaining.Select(s => s.Sentence()));
    }

    // A recalculated route is built from the rows still ahead (its first row); rows before it were behind when it was
    // built. A row the route does not drive ends the count; with no sequence of its own the route gives nothing to go
    // by, and the position alone decides.
    [Fact]
    public void A_recalculated_route_counts_from_its_first_row()
    {
        int RunStart(int row) => row switch { 2 => 0, 3 => 4, _ => -1 };

        Assert.Equal(2, ClearanceTrim.ViaStepsDoneOnRoute(2, 4, RunStart, cursor: 1));
        Assert.Equal(3, ClearanceTrim.ViaStepsDoneOnRoute(2, 4, RunStart, cursor: 4));
        Assert.Equal(0, ClearanceTrim.ViaStepsDoneOnRoute(0, 4, RunStart, cursor: 9));
        Assert.Null(ClearanceTrim.ViaStepsDoneOnRoute(-1, 4, RunStart, cursor: 9));
        Assert.Null(ClearanceTrim.ViaStepsDoneOnRoute(4, 4, RunStart, cursor: 9));
        Assert.Equal(2, ClearanceTrim.ViaStepsDone(new[] { "A", "B", "C" }, new[] { "C" }, doneOnRoute: null));
    }

    // A recalculation restarts the cursor, so the turn onto a taxiway the aircraft is already on would read as a turn
    // still to make, and a Calculate would judge it again from the heading [TURN-3]. The turns made on the route being
    // replaced are remembered by row, through every later recalculation, until the next load.
    [Fact]
    public void A_turn_made_before_a_recalculation_stays_made()
    {
        var state = ClearanceProgressState.For(null).Settled(Route, cursor: 3, holding: false, turnsMade: new[] { 1 });

        Assert.True(state.Report(1, turnOntoItMade: false, Array.Empty<ClearedCrossing>(), Route, 0, false).TurnOntoFirstRemainingMade);
        Assert.False(state.Report(2, turnOntoItMade: false, Array.Empty<ClearedCrossing>(), Route, 0, false).TurnOntoFirstRemainingMade);

        var again = state.Settled(Route, cursor: 0, holding: false, turnsMade: new[] { 2 });
        Assert.Equal(new[] { 1, 2 }, again.TurnsMade);
        Assert.Empty(ClearanceProgressState.For(null).TurnsMade);
    }

    // A stop with no taxiway of its own is carried over a recalculation only while the stop it was placed at is not
    // passed, as a holding point is only while still ahead: one behind is never searched for again (an extra stop, or
    // "the route does not cross it" on every later "Route changed", [HLD-17]). A stop on a row of its own is left to
    // ClearanceRows.After.
    [Fact]
    public void A_stop_with_no_taxiway_is_carried_over_only_while_not_passed()
    {
        var stops = new List<KeyValuePair<int, string>>
        {
            new(-1, "22R"), new(1, "09"), new(-1, "04L"), new(3, "27"),
        };
        var state = ClearanceProgressState.For(null).WithPlaced(new[]
        {
            new PlacedStop(-1, ClearanceTargetKind.Runway, "22R", 0, 30),
            new PlacedStop(-1, ClearanceTargetKind.Taxiway, "04L", 0, 50),   // another kind: not this stop's
        });

        // Short of node 30: carried. Past it: dropped. "04L" was never placed as a runway stop: dropped. Row 3 is one
        // past a 3-row sequence: no taxiway of its own either.
        Assert.Equal(new[] { -1, 1 }, state.CarriedOver(stops, 3, ClearanceTargetKind.Runway, Route, 1, holding: false).Select(s => s.Key));
        Assert.Equal(new[] { 1 }, state.CarriedOver(stops, 3, ClearanceTargetKind.Runway, Route, 3, holding: false).Select(s => s.Key));
        Assert.Equal(new[] { -1, 1, 3 }, state.CarriedOver(stops, 4, ClearanceTargetKind.Runway, Route, 1, holding: false).Select(s => s.Key));
        // Held at it: not yet passed.
        Assert.Equal(new[] { -1, 1 }, state.CarriedOver(stops, 3, ClearanceTargetKind.Runway, Route, 2, holding: true).Select(s => s.Key));
    }

    [Fact]
    public void None_reports_nothing()
    {
        var none = ClearanceProgress.None;
        Assert.Equal(0, none.ViaStepsDone);
        Assert.Empty(none.ClearedInForce);
        Assert.Empty(none.ClearedUsed);
        Assert.Empty(none.PutBack);
        Assert.Empty(none.HoldsPassed);
        Assert.False(none.TurnOntoFirstRemainingMade);
    }
}
