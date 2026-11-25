// Tests for MSFSBlindAssist.Navigation.ClearancePlanner, ClearanceRouteChecks and ClearanceTrim: a list of
// clearance steps turned into what guidance takes (the taxiway sequence, the stops, the cleared crossings,
// the Turn picks and how a leg with no destination ends), the checks on the route built from it, and the
// list trimmed to what is still ahead. Every sentence is also checked under a comma-decimal culture.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearancePlannerTests
{
    private static ClearanceStep Via(string name) => new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, name);
    private static ClearanceStep Left(string name) => new(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Taxiway, name);
    private static ClearanceStep Right(string name) => new(ClearanceStepKind.RightOnto, ClearanceTargetKind.Taxiway, name);
    private static ClearanceStep CrossRwy(string name, string? at = null, int atNodeId = 0)
        => new(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, name, At: at, AtNodeId: atNodeId);
    private static ClearanceStep CrossTwy(string name) => new(ClearanceStepKind.Cross, ClearanceTargetKind.Taxiway, name);
    private static ClearanceStep HoldRwy(string name) => new(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, name);
    private static ClearanceStep HoldTwy(string name) => new(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, name);
    private static ClearanceStep HoldPoint(string name, int nodeId, bool fromSign = false)
        => new(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint, name, NodeId: nodeId, FromSign: fromSign);

    /// <summary>A leg with no destination.</summary>
    private static ClearancePlan Leg(params ClearanceStep[] steps) => ClearancePlanner.Build(steps, hasDestination: false);

    /// <summary>A clearance with a destination (a runway one when <paramref name="runway"/> is set).</summary>
    private static ClearancePlan To(string? runway, params ClearanceStep[] steps)
        => ClearancePlanner.Build(steps, hasDestination: true, destinationRunway: runway);

    private static (int, TurnSide)[] Picks(ClearancePlan plan)
        => plan.TurnPicks.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToArray();

    // ---- ClearancePlanner.Build ----

    [Fact]
    public void The_kcle_clearance()
    {
        var plan = Leg(Left("P"), CrossRwy("24L", at: "P"), HoldTwy("L"));
        Assert.Equal(new[] { "P" }, plan.TaxiwaySequence);
        Assert.Equal(new[] { (0, TurnSide.Left) }, Picks(plan));
        Assert.Equal(new[] { new ClearedCrossing("24L", At: "P", SeqIndex: 0) }, plan.Crossings);
        Assert.Empty(plan.Holds);
        Assert.Equal(ClearanceEndKind.HoldShortTaxiway, plan.End);
        Assert.Equal("L", plan.EndStep?.Name);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void The_kbos_clearance()
    {
        var plan = Leg(Right("C"), CrossRwy("04L"), HoldRwy("04R"));
        Assert.Equal(new[] { "C" }, plan.TaxiwaySequence);
        Assert.Equal(new[] { (0, TurnSide.Right) }, Picks(plan));
        Assert.Equal(new[] { new ClearedCrossing("04L", SeqIndex: 0) }, plan.Crossings);
        Assert.Equal(ClearanceEndKind.HoldShortRunway, plan.End);
        Assert.Equal("04R", plan.EndStep?.Name);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void A_turn_word_is_the_pick_of_its_via_step()
    {
        var plan = To("33L", Via("H"), CrossRwy("04R"), Left("C"));
        Assert.Equal(new[] { "H", "C" }, plan.TaxiwaySequence);
        // Keyed 1, C's place among the Via steps; never 2, its place in the list.
        Assert.Equal(new[] { (1, TurnSide.Left) }, Picks(plan));
        Assert.Equal(new[] { new ClearedCrossing("04R", SeqIndex: 0) }, plan.Crossings);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void Each_use_of_a_repeated_taxiway_keeps_its_own_turn()
    {
        var plan = To(null, Left("A"), Via("C"), Right("A"));
        Assert.Equal(new[] { "A", "C", "A" }, plan.TaxiwaySequence);
        Assert.Equal(new[] { (0, TurnSide.Left), (2, TurnSide.Right) }, Picks(plan));
    }

    [Fact]
    public void Via_steps_set_no_pick()
    {
        var plan = To(null, Via("A"), Via("B"));
        Assert.Empty(plan.TurnPicks);
    }

    [Fact]
    public void A_uk_clearance_with_the_limit_as_destination()
    {
        var plan = To("27", Via("C"), HoldRwy("23"));
        Assert.Equal(new[] { "C" }, plan.TaxiwaySequence);
        Assert.Equal(new[] { new ClearanceHold(0, HoldRwy("23")) }, plan.Holds);
        Assert.Equal(ClearanceEndKind.Destination, plan.End);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void A_limit_said_first_ends_the_leg()
    {
        var plan = Leg(HoldPoint("C5", 7), Via("A"));
        Assert.Equal(ClearanceEndKind.HoldAtNamedPoint, plan.End);
        Assert.Equal("C5", plan.EndStep?.Name);
        Assert.Equal(new[] { "A" }, plan.TaxiwaySequence);
        Assert.Empty(plan.Holds);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void A_limit_alone_is_limit_only()
    {
        var plan = Leg(HoldPoint("C5", 7));
        Assert.Equal(ClearanceEndKind.HoldAtNamedPoint, plan.End);
        Assert.Empty(plan.TaxiwaySequence);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void A_hold_short_said_last_is_a_stop_on_the_way_to_a_destination()
    {
        var plan = To(null, Via("A"), Via("B"), HoldRwy("22R"));
        Assert.Equal(new[] { new ClearanceHold(1, HoldRwy("22R")) }, plan.Holds);
        Assert.Equal(ClearanceEndKind.Destination, plan.End);
    }

    [Fact]
    public void A_hold_short_before_any_taxiway_with_a_destination_searches_the_whole_route()
    {
        var plan = To(null, HoldRwy("23"), Via("C"));
        Assert.Equal(new[] { new ClearanceHold(-1, HoldRwy("23")) }, plan.Holds);
    }

    [Fact]
    public void A_taxiway_used_twice_is_kept_twice()
    {
        Assert.Equal(new[] { "A", "C", "A" }, To(null, Via("A"), Via("C"), Via("A")).TaxiwaySequence);
        Assert.Equal(new[] { "A", "C", "A" }, Leg(Via("A"), Via("C"), Via("A")).TaxiwaySequence);
    }

    [Fact]
    public void A_leg_can_end_across_a_runway()
    {
        var plan = Leg(Via("C"), CrossRwy("04L"));
        Assert.Equal(ClearanceEndKind.AcrossRunway, plan.End);
        Assert.Equal("04L", plan.EndStep?.Name);
        Assert.Equal(new[] { new ClearedCrossing("04L", SeqIndex: 0) }, plan.Crossings);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void A_leg_can_end_at_the_end_of_a_taxiway()
    {
        var plan = Leg(Via("P"));
        Assert.Equal(ClearanceEndKind.EndOfTaxiway, plan.End);
        Assert.Equal("P", plan.EndStep?.Name);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void Holding_short_of_the_departure_runway_adds_nothing()
    {
        var same = To("33L", Via("A"), HoldRwy("33L"));
        Assert.Empty(same.Holds);
        Assert.Null(same.Refusal);
        var reciprocal = To("33L", Via("A"), HoldRwy("15R"));
        Assert.Empty(reciprocal.Holds);
        Assert.Null(reciprocal.Refusal);
    }

    [Fact]
    public void Nothing_to_route_is_refused()
    {
        const string expected = "Enter a step, or choose a destination.";
        Assert.Equal(expected, Leg().Refusal);
        Assert.Equal(expected, TestCulture.Run("de-DE", () => Leg().Refusal));
        // A destination alone is routed by the shortest way, as today.
        Assert.Null(To(null).Refusal);
    }

    [Fact]
    public void A_leg_cannot_end_on_cross_taxiway()
    {
        const string expected = "A leg cannot end on cross taxiway B. Add where to hold short, or the next taxiway.";
        Assert.Equal(expected, Leg(Via("A"), CrossTwy("B")).Refusal);
        Assert.Equal(expected, TestCulture.Run("de-DE", () => Leg(Via("A"), CrossTwy("B")).Refusal));
        // With a destination the leg does not end there: a check, not a refusal.
        Assert.Null(To(null, Via("A"), CrossTwy("B")).Refusal);
    }

    [Fact]
    public void One_runway_cannot_be_both_crossed_and_held_short_of()
    {
        const string expected = "Runway 24L is entered both as a crossing and as a hold short. Remove one.";
        Assert.Equal(expected, Leg(Via("P"), CrossRwy("24L"), HoldRwy("06R")).Refusal);
        Assert.Equal(expected, TestCulture.Run("de-DE", () => Leg(Via("P"), CrossRwy("24L"), HoldRwy("06R")).Refusal));
        Assert.Equal(expected, To(null, Via("P"), CrossRwy("24L"), HoldRwy("24L")).Refusal);
    }

    [Fact]
    public void A_sign_read_point_can_only_end_a_leg()
    {
        const string expected = "S1 is read from a sign and can only end a leg.";
        var onTheWay = Leg(Via("A"), HoldPoint("S1", 9, fromSign: true), Via("B"));
        Assert.Equal(expected, onTheWay.Refusal);
        Assert.Equal(expected,
            TestCulture.Run("de-DE", () => Leg(Via("A"), HoldPoint("S1", 9, fromSign: true), Via("B")).Refusal));
        Assert.Null(Leg(Via("A"), HoldPoint("S1", 9, fromSign: true)).Refusal);
        // [HLD-18]: with a destination no step ends the leg, so a sign-read point is always refused.
        Assert.Equal(expected, To(null, Via("A"), HoldPoint("S1", 9, fromSign: true)).Refusal);
    }

    // ---- The plan fields Tasks 3 and 6 consume (controller rulings) ----

    [Fact]
    public void A_leg_ending_across_a_runway_at_a_holding_point_pins_the_route_through_it()
    {
        // Today's "After crossing runway" at a holding point: LoadRoute's holdingPointHoldNodeId / holdingPointName.
        var plan = Leg(Via("P"), CrossRwy("24L", at: "B3", atNodeId: 42));
        Assert.Equal(ClearanceEndKind.AcrossRunway, plan.End);
        Assert.Equal(42, plan.CrossAtHoldingPointNodeId);
        Assert.Equal("B3", plan.CrossAtHoldingPointName);
        Assert.Equal(new[] { new ClearedCrossing("24L", At: "B3", AtNodeId: 42, SeqIndex: 0) }, plan.Crossings);
    }

    [Fact]
    public void A_crossing_at_a_holding_point_on_the_way_does_not_pin_the_route()
    {
        // Spec 8.1: an "at" does not bend the route, except at the end of a leg with no destination.
        var onTheWay = Leg(Via("P"), CrossRwy("24L", at: "B3", atNodeId: 42), HoldTwy("L"));
        Assert.Null(onTheWay.CrossAtHoldingPointNodeId);
        Assert.Null(onTheWay.CrossAtHoldingPointName);
        Assert.Equal(new[] { new ClearedCrossing("24L", At: "B3", AtNodeId: 42, SeqIndex: 0) }, onTheWay.Crossings);

        var withDestination = To("27", Via("P"), CrossRwy("24L", at: "B3", atNodeId: 42));
        Assert.Null(withDestination.CrossAtHoldingPointNodeId);
        Assert.Null(withDestination.CrossAtHoldingPointName);

        // An "at" taxiway has no node: nothing to pin.
        var atTaxiway = Leg(Via("P"), CrossRwy("24L", at: "P"));
        Assert.Null(atTaxiway.CrossAtHoldingPointNodeId);
        Assert.Null(atTaxiway.CrossAtHoldingPointName);
    }

    [Fact]
    public void Taxiways_crossed_after_the_last_via_step_are_kept_for_the_leg_end()
    {
        var plan = Leg(Via("A"), CrossTwy("B"), HoldTwy("C"));
        Assert.Equal(ClearanceEndKind.HoldShortTaxiway, plan.End);
        Assert.Equal(new[] { "B" }, plan.TaxiwaysToCross);
        Assert.Equal(new[] { "B" }, plan.LegEndCrossedTaxiways);

        // Only those after the last Via step, in list order; every one is still checked.
        var later = Leg(CrossTwy("B"), Via("A"), CrossTwy("D"), CrossRwy("04L"), CrossTwy("E"), HoldTwy("C"));
        Assert.Equal(new[] { "B", "D", "E" }, later.TaxiwaysToCross);
        Assert.Equal(new[] { "D", "E" }, later.LegEndCrossedTaxiways);

        // No Via step at all: every crossed taxiway lies before the end.
        Assert.Equal(new[] { "B" }, Leg(CrossTwy("B"), HoldTwy("C")).LegEndCrossedTaxiways);
    }

    [Fact]
    public void The_crossed_taxiways_break_a_tie_only_for_a_hold_short_of_a_taxiway_ending_a_leg()
    {
        Assert.Empty(To(null, Via("A"), CrossTwy("B"), HoldTwy("C")).LegEndCrossedTaxiways);
        Assert.Empty(Leg(Via("A"), CrossTwy("B"), HoldRwy("04R")).LegEndCrossedTaxiways);
        Assert.Empty(Leg(Via("A"), CrossTwy("B"), CrossRwy("04L")).LegEndCrossedTaxiways);
    }

    // ---- ClearanceRouteChecks ----

    private static TaxiRouteSegment Seg(string taxiway, int toNode)
        => new() { TaxiwayName = taxiway, FromNode = new TaxiNode { NodeId = toNode - 1 }, ToNode = new TaxiNode { NodeId = toNode } };

    [Fact]
    public void A_taxiway_the_route_never_meets_is_a_warning()
    {
        var plan = To(null, Via("A"), CrossTwy("B"));
        Assert.Equal(new[] { "B" }, plan.TaxiwaysToCross);
        var segments = new[] { Seg("A", 2), Seg("A", 3), Seg("C", 4) };
        const string expected = "The route does not cross taxiway B.";
        Assert.Equal(new[] { expected }, ClearanceRouteChecks.Warnings(segments, plan, (_, _) => false));
        Assert.Equal(new[] { expected },
            TestCulture.Run("de-DE", () => ClearanceRouteChecks.Warnings(segments, plan, (_, _) => false)));
    }

    [Fact]
    public void A_taxiway_the_route_meets_is_no_warning()
    {
        var plan = To(null, Via("A"), CrossTwy("B"));
        // Met by name (a segment on b, any case) ...
        Assert.Empty(ClearanceRouteChecks.Warnings(new[] { Seg("A", 2), Seg("b", 3) }, plan, (_, _) => false));
        // ... or at a node that touches it.
        Assert.Empty(ClearanceRouteChecks.Warnings(new[] { Seg("A", 2), Seg("A", 3) }, plan,
            (node, name) => node == 3 && name == "B"));
    }

    [Fact]
    public void A_crossed_taxiway_breaks_the_tie_for_the_end()
    {
        var candidates = new[] { 5, 9 };
        var distances = new Dictionary<int, double> { [5] = 120, [7] = 200, [9] = 300 };
        Assert.Equal(new[] { 9 }, ClearanceRouteChecks.BeyondTheCrossing(candidates, distances, new[] { 7 }));
        Assert.Equal(new[] { 5, 9 }, ClearanceRouteChecks.BeyondTheCrossing(candidates, distances, Array.Empty<int>()));
        distances[8] = 400;
        Assert.Equal(new[] { 5, 9 }, ClearanceRouteChecks.BeyondTheCrossing(candidates, distances, new[] { 8 }));
    }

    // ---- ClearanceTrim ----

    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static IReadOnlySet<string> Set(params string[] runways) => new HashSet<string>(runways);

    [Fact]
    public void Trim_drops_what_is_behind()
    {
        var steps = new[] { Via("A"), HoldRwy("09"), Via("B"), CrossRwy("27L"), HoldTwy("L") };
        var left = ClearanceTrim.Remaining(steps, viaStepsDone: 1, runwaysAhead: Set("27L"), clearedAhead: Set("27L"),
            turnOntoFirstRemainingMade: false);
        Assert.Equal(new[] { Via("B"), CrossRwy("27L"), HoldTwy("L") }, left);
    }

    [Fact]
    public void Trim_turns_a_crossing_that_was_taken_back_into_a_hold_short()
    {
        var steps = new[] { Via("C"), CrossRwy("04L") };
        var left = ClearanceTrim.Remaining(steps, viaStepsDone: 0, runwaysAhead: Set("04L"), clearedAhead: None,
            turnOntoFirstRemainingMade: false);
        Assert.Equal(new[] { Via("C"), HoldRwy("04L") }, left);
    }

    [Fact]
    public void Trim_drops_a_crossing_already_made()
    {
        var steps = new[] { Via("C"), CrossRwy("04L"), HoldRwy("04R") };
        var left = ClearanceTrim.Remaining(steps, viaStepsDone: 0, runwaysAhead: Set("04R"), clearedAhead: None,
            turnOntoFirstRemainingMade: false);
        Assert.Equal(new[] { Via("C"), HoldRwy("04R") }, left);
    }

    [Fact]
    public void Trim_counts_a_runway_by_either_end()
    {
        // "4l" ahead is 04L; a crossing named by its other end (22R) is the same runway.
        var steps = new[] { Via("C"), CrossRwy("22R"), HoldRwy("22L") };
        var left = ClearanceTrim.Remaining(steps, viaStepsDone: 0, runwaysAhead: Set("4l", "04R"), clearedAhead: Set("04L"),
            turnOntoFirstRemainingMade: false);
        Assert.Equal(new[] { Via("C"), CrossRwy("22R"), HoldRwy("22L") }, left);
    }

    [Fact]
    public void Trim_shows_a_turn_already_made_as_via()
    {
        var steps = new[] { Via("H"), Left("C"), HoldRwy("27") };
        var made = ClearanceTrim.Remaining(steps, viaStepsDone: 1, runwaysAhead: Set("27"), clearedAhead: None,
            turnOntoFirstRemainingMade: true);
        Assert.Equal(new[] { Via("C"), HoldRwy("27") }, made);
        var ahead = ClearanceTrim.Remaining(steps, viaStepsDone: 1, runwaysAhead: Set("27"), clearedAhead: None,
            turnOntoFirstRemainingMade: false);
        Assert.Equal(new[] { Left("C"), HoldRwy("27") }, ahead);
    }

    [Fact]
    public void Via_steps_done_counts_the_runs_behind()
    {
        var original = new[] { "A", "C", "B" };
        Assert.Equal(2, ClearanceTrim.ViaStepsDone(original, new[] { "B" }));
        Assert.Equal(1, ClearanceTrim.ViaStepsDone(original, new[] { "C", "B" }));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(original, new[] { "A", "C", "B" }));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(original, null));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(original, Array.Empty<string>()));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(original, new[] { "X", "A", "C", "B" }));
    }

    [Fact]
    public void Via_steps_done_is_zero_when_the_matched_taxiway_repeats()
    {
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(new[] { "A", "C", "A" }, new[] { "A" }));
        Assert.Equal(0, ClearanceTrim.ViaStepsDone(new[] { "A", "C", "A", "D" }, new[] { "a", "D" }));
    }
}
