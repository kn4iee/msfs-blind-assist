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
    private static ClearanceStep HoldRwyAt(string name, string at, int atNodeId = 0)
        => new(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, name, At: at, AtNodeId: atNodeId);
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

    // Final review, minor: the refusal names both steps, so the pilot knows which two to look at in the list (by
    // number, lower first), and both names when they are the runway's two ends.
    [Fact]
    public void One_runway_cannot_be_both_crossed_and_held_short_of()
    {
        const string ends = "Steps 2 and 3 both name runway 24L/06R, one as a crossing and one as a hold short. Remove one.";
        Assert.Equal(ends, Leg(Via("P"), CrossRwy("24L"), HoldRwy("06R")).Refusal);
        Assert.Equal(ends, TestCulture.Run("de-DE", () => Leg(Via("P"), CrossRwy("24L"), HoldRwy("06R")).Refusal));
        Assert.Equal("Steps 2 and 3 both name runway 24L, one as a crossing and one as a hold short. Remove one.",
            TestCulture.Run("de-DE", () => To(null, Via("P"), CrossRwy("24L"), HoldRwy("24L")).Refusal));
        Assert.Equal("Steps 2 and 4 both name runway 04L, one as a crossing and one as a hold short. Remove one.",
            TestCulture.Run("sv-SE", () => To(null, Via("P"), CrossRwy("04L"), HoldTwy("L"), HoldRwy("04L")).Refusal));
    }

    [Fact]
    public void A_crossing_said_before_any_taxiway_is_the_same_crossing_as_one_after_the_first()
    {
        // Before any Via step the whole route is searched; after the first, from its start: the same crossing.
        const string expected = "Steps 1 and 3 both name runway 04L, one as a crossing and one as a hold short. Remove one.";
        Assert.Equal(expected, To(null, HoldRwy("04L"), Via("A"), CrossRwy("04L")).Refusal);
        Assert.Equal(expected, Leg(CrossRwy("04L"), Via("A"), HoldRwy("04L")).Refusal);
        Assert.Equal(expected, Leg(HoldRwy("04L"), Via("A"), CrossRwy("04L")).Refusal);
        // After two different taxiways, two crossings.
        Assert.Null(Leg(Via("P"), HoldRwy("04L"), Via("Q"), CrossRwy("04L"), HoldTwy("L")).Refusal);
    }

    [Fact]
    public void A_limit_said_first_lets_a_leg_end_after_a_crossed_taxiway()
    {
        // Spec 8.1: "taxi to holding point C5 via A, cross B" ends at C5, never on the crossing.
        var plan = Leg(HoldPoint("C5", 7), Via("A"), CrossTwy("B"));
        Assert.Null(plan.Refusal);
        Assert.Equal(ClearanceEndKind.HoldAtNamedPoint, plan.End);
        Assert.Equal("C5", plan.EndStep?.Name);
        Assert.Equal(new[] { "B" }, plan.TaxiwaysToCross);

        var taxiwayLimit = Leg(HoldTwy("C"), Via("A"), CrossTwy("B"));
        Assert.Null(taxiwayLimit.Refusal);
        Assert.Equal(ClearanceEndKind.HoldShortTaxiway, taxiwayLimit.End);
        Assert.Equal(new[] { "B" }, taxiwayLimit.LegEndCrossedTaxiways);
    }

    [Fact]
    public void Holding_short_of_the_destination_runway_is_no_second_crossing()
    {
        // Spec 8.1: a Hold short of the destination runway is accepted and adds nothing, even when the clearance
        // also crosses that runway on the way.
        var plan = To("33L", Via("A"), CrossRwy("33L"), Via("B"), HoldRwy("15R"));
        Assert.Null(plan.Refusal);
        Assert.Empty(plan.Holds);
        Assert.Equal(new[] { new ClearedCrossing("33L", SeqIndex: 0) }, plan.Crossings);
    }

    [Fact]
    public void A_crossing_and_a_hold_short_at_different_places_are_different_crossings()
    {
        // A crossing put back as a stop keeps its place (hold short of runway 24L at P); the same runway crossed at
        // Q is another crossing.
        Assert.Null(Leg(Via("P"), HoldRwyAt("24L", "P"), Via("Q"), CrossRwy("24L", at: "Q"), HoldTwy("L")).Refusal);
        Assert.Null(Leg(Via("P"), HoldRwyAt("24L", "B3", 42), Via("Q"), CrossRwy("24L", at: "B4", atNodeId: 43),
                        HoldTwy("L")).Refusal);
        // The same place, or a step with no place, is the same crossing.
        const string expected = "Steps 2 and 3 both name runway 24L, one as a crossing and one as a hold short. Remove one.";
        Assert.Equal(expected, Leg(Via("P"), HoldRwyAt("24L", "P"), CrossRwy("24L", at: "p"), HoldTwy("L")).Refusal);
        Assert.Equal(expected, Leg(Via("P"), HoldRwyAt("24L", "P"), CrossRwy("24L"), HoldTwy("L")).Refusal);
    }

    // [HLD-18] since main's 2026-11-24 reversal, merged into the step list 2026-11-26: a holding point read from a sign
    // is a stop like a painted one anywhere in the list, with a destination too, and still ends a leg as its last step.
    // Until then Calculate refused one anywhere but the end of a leg with no destination ("KK is read from a sign and
    // can only end a leg.").
    [Fact]
    public void A_sign_read_point_is_a_stop_anywhere_in_the_list()
    {
        var kk = HoldPoint("KK", 9, fromSign: true);

        var onTheWay = Leg(Via("K"), kk, Via("B"));
        Assert.Null(onTheWay.Refusal);
        Assert.Equal(new[] { new ClearanceHold(0, kk) }, onTheWay.Holds);
        Assert.Equal(ClearanceEndKind.EndOfTaxiway, onTheWay.End);

        var withDestination = To(null, Via("K"), kk);
        Assert.Null(withDestination.Refusal);
        Assert.Equal(new[] { new ClearanceHold(0, kk) }, withDestination.Holds);

        var toARunway = To("30R", Via("K"), kk, Via("N"));
        Assert.Null(toARunway.Refusal);
        Assert.Equal(new[] { new ClearanceHold(0, kk) }, toARunway.Holds);

        var legEnd = Leg(Via("K"), kk);
        Assert.Null(legEnd.Refusal);
        Assert.Equal(ClearanceEndKind.HoldAtNamedPoint, legEnd.End);
        Assert.Equal(kk, legEnd.EndStep);
        Assert.Empty(legEnd.Holds);
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

    // ---- LegEndMeeting: where the route meets the taxiway a leg ends short of ----

    // Along A: node 3 meets C, node 4 meets B (the taxiway crossed), node 6 meets C again.
    private static readonly TaxiRouteSegment[] AlongA = { Seg("A", 2), Seg("A", 3), Seg("A", 4), Seg("A", 5), Seg("A", 6), Seg("A", 7) };

    private static bool Touches(int node, string name)
        => (name == "C" && (node == 3 || node == 6)) || (name == "B" && node == 4);

    [Fact]
    public void A_leg_end_is_the_first_meeting_along_the_route()
    {
        Assert.Equal(1, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "C", null, Touches, _ => true, out bool beyond));
        Assert.False(beyond);
        // From a later run, the first meeting from there.
        Assert.Equal(4, ClearanceRouteChecks.LegEndMeeting(AlongA, 2, "C", null, Touches, _ => true, out _));
        // One inside a runway's margin is never a leg end.
        Assert.Equal(4, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "C", null, Touches, n => n.NodeId != 3, out _));
        // None at all.
        Assert.Equal(-1, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "D", null, Touches, _ => true, out _));
    }

    // "Via A, cross B, hold short of C": the meeting before B is not the one ATC meant (spec 8.1), so the scan keeps
    // the tie-breaker the form applied (BeyondTheCrossing) instead of cutting the leg back to the first meeting.
    [Fact]
    public void A_crossed_taxiway_keeps_the_leg_end_beyond_the_crossing()
    {
        Assert.Equal(4, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "C", new[] { "B" }, Touches, _ => true, out bool beyond));
        Assert.True(beyond);
        // Crossed on the route itself: a segment on B.
        var onB = new[] { Seg("A", 2), Seg("A", 3), Seg("B", 10), Seg("A", 6) };
        Assert.Equal(3, ClearanceRouteChecks.LegEndMeeting(onB, 0, "C", new[] { "b" }, Touches, _ => true, out beyond));
        Assert.True(beyond);
    }

    // The tie-breaker never leaves a leg with no end: no meeting beyond the crossing, or a route that never crosses it,
    // keeps the first meeting. A meeting AT the crossing node is not beyond it.
    [Fact]
    public void With_no_meeting_beyond_the_crossing_the_first_meeting_stands()
    {
        Assert.Equal(1, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "C", new[] { "E" }, Touches, _ => true, out bool beyond));
        Assert.False(beyond);
        Assert.Equal(1, ClearanceRouteChecks.LegEndMeeting(AlongA.Take(4).ToArray(), 0, "C", new[] { "B" }, Touches, _ => true, out beyond));
        Assert.False(beyond);
        bool AtTheCrossing(int node, string name) => (name == "C" && node == 3) || (name == "B" && node == 3);
        Assert.Equal(1, ClearanceRouteChecks.LegEndMeeting(AlongA, 0, "C", new[] { "B" }, AtTheCrossing, _ => true, out beyond));
        Assert.False(beyond);
    }

    // A segment already ON the taxiway ends the scan: "hold short of C" from a route driving C is a mismatch.
    [Fact]
    public void The_scan_stops_on_the_taxiway_itself()
    {
        var ontoC = new[] { Seg("A", 2), Seg("C", 3), Seg("A", 6) };
        Assert.Equal(-1, ClearanceRouteChecks.LegEndMeeting(ontoC, 0, "c", null, Touches, _ => true, out _));
    }

    // ---- ClearanceTrim ----

    /// <summary>
    /// <see cref="ClearanceTrim.Remaining"/> with the manager's records (what Tasks 3 and 7 supply); none by default:
    /// every runway step's state unknown.
    /// </summary>
    private static List<ClearanceStep> Trim(ClearanceStep[] steps, int done = 0, bool destination = false,
        ClearedCrossing[]? inForce = null, ClearedCrossing[]? used = null, ClearedCrossing[]? putBack = null,
        ClearanceHold[]? passed = null, bool turnMade = false)
        => ClearanceTrim.Remaining(steps, done, destination,
            inForce ?? Array.Empty<ClearedCrossing>(), used ?? Array.Empty<ClearedCrossing>(),
            putBack ?? Array.Empty<ClearedCrossing>(), passed ?? Array.Empty<ClearanceHold>(), turnMade);

    private static ClearedCrossing Rec(string runway, int seqIndex, string? at = null, int atNodeId = 0,
        ClearedBy origin = ClearedBy.Clearance, int stopNodeId = 0)
        => new(runway, At: at, AtNodeId: atNodeId, StopNodeId: stopNodeId, SeqIndex: seqIndex, Origin: origin);

    [Fact]
    public void Trim_drops_the_finished_via_steps_and_what_is_known_to_be_behind()
    {
        var steps = new[] { Via("A"), HoldRwy("09"), Via("B"), CrossRwy("27L"), HoldTwy("L") };
        // The 09 stop is known passed; the 27L crossing is still cleared ahead.
        Assert.Equal(new[] { Via("B"), CrossRwy("27L"), HoldTwy("L") },
            Trim(steps, done: 1, inForce: new[] { Rec("27L", 1) }, passed: new[] { new ClearanceHold(0, HoldRwy("09")) }));
        // Nothing known of the 09 stop: it stays (spec 8.2: only completed steps are dropped), after the taxiway the
        // aircraft is on, never in front of it where it would read as the limit.
        Assert.Equal(new[] { Via("B"), HoldRwy("09"), CrossRwy("27L"), HoldTwy("L") },
            Trim(steps, done: 1, inForce: new[] { Rec("27L", 1) }));
    }

    [Fact]
    public void Trim_never_makes_a_kept_stop_the_limit()
    {
        // The two repros: each kept stop lands after the first taxiway still ahead, and the leg still ends at L.
        var first = Trim(new[] { Via("A"), HoldRwy("09"), Via("B"), CrossRwy("27L"), HoldTwy("L") }, done: 1);
        Assert.Equal(new[] { Via("B"), HoldRwy("09"), CrossRwy("27L"), HoldTwy("L") }, first);
        var firstPlan = ClearancePlanner.Build(first, hasDestination: false);
        Assert.Equal(ClearanceEndKind.HoldShortTaxiway, firstPlan.End);
        Assert.Equal("L", firstPlan.EndStep?.Name);

        var second = Trim(new[] { Via("A"), Via("B"), HoldRwy("27"), Via("C"), HoldTwy("L") }, done: 2);
        Assert.Equal(new[] { Via("C"), HoldRwy("27"), HoldTwy("L") }, second);
        var secondPlan = ClearancePlanner.Build(second, hasDestination: false);
        Assert.Equal(ClearanceEndKind.HoldShortTaxiway, secondPlan.End);
        Assert.Equal("L", secondPlan.EndStep?.Name);

        // A crossing kept ahead moves with it.
        Assert.Equal(new[] { Via("B"), CrossRwy("04L"), HoldTwy("L") },
            Trim(new[] { Via("A"), CrossRwy("04L"), Via("B"), HoldTwy("L") }, done: 1, inForce: new[] { Rec("04L", 0) }));
        // With a destination there is no limit: nothing moves.
        Assert.Equal(new[] { HoldRwy("09"), Via("B") },
            Trim(new[] { Via("A"), HoldRwy("09"), Via("B") }, done: 1, destination: true));
    }

    [Fact]
    public void Trim_keeps_the_last_finished_via_step_in_front_of_a_stop_it_cannot_move()
    {
        // The first taxiway still ahead ends the leg: a stop moved after it would end the leg instead.
        var steps = new[] { Via("A"), HoldRwy("09"), Via("B") };
        var kept = Trim(steps, done: 1);
        Assert.Equal(steps, kept);
        var plan = ClearancePlanner.Build(kept, hasDestination: false);
        Assert.Equal(ClearanceEndKind.EndOfTaxiway, plan.End);
        Assert.Equal("B", plan.EndStep?.Name);

        // No taxiway left at all.
        var noVia = new[] { Via("A"), HoldRwy("09"), HoldTwy("L") };
        Assert.Equal(noVia, Trim(noVia, done: 1));

        // The taxiway kept is behind the aircraft: a turn onto it is already made, so it reads as Via.
        Assert.Equal(new[] { Via("A"), HoldRwy("09"), Via("B") }, Trim(new[] { Left("A"), HoldRwy("09"), Via("B") }, done: 1));
    }

    [Fact]
    public void Trim_keeps_a_limit_said_first()
    {
        var steps = new[] { HoldPoint("C5", 7), Via("A"), Via("B") };
        Assert.Equal(new[] { HoldPoint("C5", 7), Via("B") }, Trim(steps, done: 1));
        // The limit ends the leg: never dropped, whatever is reported.
        Assert.Equal(new[] { HoldPoint("C5", 7), Via("B") },
            Trim(steps, done: 1, passed: new[] { new ClearanceHold(-1, HoldPoint("C5", 7)) }));
        // A stop kept on the way goes after the taxiway still ahead; the limit stays the end.
        var withStop = Trim(new[] { HoldPoint("C5", 7), Via("A"), HoldRwy("09"), Via("B") }, done: 1);
        Assert.Equal(new[] { HoldPoint("C5", 7), Via("B"), HoldRwy("09") }, withStop);
        Assert.Equal("C5", ClearancePlanner.Build(withStop, hasDestination: false).EndStep?.Name);
    }

    [Fact]
    public void Trim_keeps_a_hold_short_said_first_on_the_way_to_a_destination()
    {
        var steps = new[] { HoldPoint("C5", 7), Via("A"), Via("B") };
        Assert.Equal(new[] { HoldPoint("C5", 7), Via("B") }, Trim(steps, done: 1, destination: true));
        // A stop on the way, so it goes once it is known passed.
        Assert.Equal(new[] { Via("B") },
            Trim(steps, done: 1, destination: true, passed: new[] { new ClearanceHold(-1, HoldPoint("C5", 7)) }));
    }

    [Fact]
    public void Trim_keeps_a_crossing_between_finished_via_steps_that_is_still_ahead()
    {
        // Cross 04L was entered after A, but the route crosses 04L on C: still cleared, still ahead.
        var steps = new[] { Via("A"), CrossRwy("04L"), Via("B"), Via("C") };
        Assert.Equal(new[] { CrossRwy("04L"), Via("C") }, Trim(steps, done: 2, inForce: new[] { Rec("04L", 0) }));
    }

    [Fact]
    public void Trim_flips_only_the_crossing_put_back_of_a_runway_cleared_at_two_places()
    {
        var steps = new[] { Via("P"), CrossRwy("24L", at: "P"), Via("Q"), CrossRwy("24L", at: "Q"), HoldTwy("L") };
        var atP = Trim(steps, inForce: new[] { Rec("24L", 1, at: "Q") }, putBack: new[] { Rec("24L", 0, at: "P") });
        Assert.Equal(new[] { Via("P"), HoldRwyAt("24L", "P"), Via("Q"), CrossRwy("24L", at: "Q"), HoldTwy("L") }, atP);
        // The list stays a clearance Calculate takes: two places, two crossings.
        Assert.Null(ClearancePlanner.Build(atP, hasDestination: false).Refusal);

        var atQ = Trim(steps, inForce: new[] { Rec("24L", 0, at: "P") }, putBack: new[] { Rec("24L", 1, at: "Q") });
        Assert.Equal(new[] { Via("P"), CrossRwy("24L", at: "P"), Via("Q"), HoldRwyAt("24L", "Q"), HoldTwy("L") }, atQ);
    }

    [Fact]
    public void Trim_drops_the_first_crossing_of_a_runway_crossed_twice_and_keeps_the_second()
    {
        var steps = new[] { Via("P"), CrossRwy("04L"), Via("Q"), CrossRwy("04L"), HoldTwy("L") };
        Assert.Equal(new[] { Via("P"), Via("Q"), CrossRwy("04L"), HoldTwy("L") },
            Trim(steps, used: new[] { Rec("04L", 0) }, inForce: new[] { Rec("04L", 1) }));

        // One Cross step, its crossing used; the route crosses 04L again later: no phantom hold short.
        var once = new[] { Via("P"), CrossRwy("04L", at: "P"), Via("Q") };
        Assert.Equal(new[] { Via("P"), Via("Q") }, Trim(once, used: new[] { Rec("04L", 0, at: "P") }));
    }

    [Fact]
    public void Trim_never_matches_a_crossing_continue_cleared()
    {
        var steps = new[] { Via("C"), CrossRwy("04L"), HoldRwy("04R") };
        var continued = Rec("04L", 0, origin: ClearedBy.Continue);
        Assert.Equal(steps, Trim(steps, used: new[] { continued }));
        Assert.Equal(steps, Trim(steps, putBack: new[] { continued }));
    }

    [Fact]
    public void Trim_matches_a_record_by_either_end_of_the_runway_and_by_its_via_step()
    {
        var steps = new[] { Via("C"), CrossRwy("22R"), HoldRwy("04R") };
        Assert.Equal(new[] { Via("C"), HoldRwy("04R") }, Trim(steps, used: new[] { Rec("4l", 0) }));
        // Entered after another taxiway: another crossing, state unknown, kept.
        Assert.Equal(steps, Trim(steps, used: new[] { Rec("04L", 1) }));
        // At another place: another crossing.
        var atP = new[] { Via("C"), CrossRwy("22R", at: "P"), HoldRwy("04R") };
        Assert.Equal(atP, Trim(atP, used: new[] { Rec("04L", 0, at: "Q") }));
    }

    [Fact]
    public void Trim_keeps_a_step_whose_state_is_unknown()
    {
        var steps = new[] { Via("A"), CrossTwy("B"), HoldRwy("09"), Via("C"), CrossRwy("27L"), HoldTwy("L") };
        Assert.Equal(steps, Trim(steps));
    }

    [Fact]
    public void Trim_drops_a_passed_hold_by_its_place_not_its_runway()
    {
        // The same runway held short of twice: only the stop passed goes.
        var steps = new[] { Via("A"), HoldRwy("09"), Via("B"), HoldRwy("27"), Via("C") };
        Assert.Equal(new[] { Via("B"), HoldRwy("27"), Via("C") },
            Trim(steps, done: 1, passed: new[] { new ClearanceHold(0, HoldRwy("09")) }));
    }

    [Fact]
    public void Trim_never_drops_the_step_that_ends_the_leg()
    {
        // Across 04L, still on the way to the far side: the leg's end stays, pinned to the crossing it made.
        var steps = new[] { Via("C"), CrossRwy("04L") };
        var kept = Trim(steps, used: new[] { Rec("04L", 0, stopNodeId: 77) });
        Assert.Equal(new[] { Via("C"), CrossRwy("04L") with { AtNodeId = 77, AtNodeIsPassedStop = true } }, kept);
        // With a destination it is a crossing on the way, and goes once used.
        Assert.Equal(new[] { Via("C") }, Trim(steps, destination: true, used: new[] { Rec("04L", 0, stopNodeId: 77) }));
        // A crossing put back still turns into the stop the pilot asked for.
        Assert.Equal(new[] { Via("C"), HoldRwy("04L") }, Trim(steps, putBack: new[] { Rec("04L", 0) }));
    }

    [Fact]
    public void A_used_end_crossing_stays_pinned_to_its_place()
    {
        // The leg still ends just across the crossing already made (its stop node); a fresh Calculate clears nothing for
        // it, so never a later crossing of 04L either.
        var kept = Trim(new[] { Via("C"), CrossRwy("04L") }, used: new[] { Rec("04L", 0, at: "C", stopNodeId: 77) });
        Assert.Equal(new[] { Via("C"), CrossRwy("04L", at: "C") with { AtNodeId = 77, AtNodeIsPassedStop = true } }, kept);
        var plan = ClearancePlanner.Build(kept, hasDestination: false);
        Assert.Equal(ClearanceEndKind.AcrossRunway, plan.End);
        Assert.Empty(plan.Crossings);
        // The stop node is behind the aircraft: never a holding point to pin the route through.
        Assert.Null(plan.CrossAtHoldingPointNodeId);
        Assert.Null(plan.CrossAtHoldingPointName);
        // "Cross runway 24L at B3" made: the pin replaces the holding point, which is behind too.
        var atPoint = Trim(new[] { Via("P"), CrossRwy("24L", at: "B3", atNodeId: 42) },
                           used: new[] { Rec("24L", 0, at: "B3", atNodeId: 42, stopNodeId: 41) });
        Assert.Equal(41, atPoint[1].AtNodeId);
        Assert.Null(ClearancePlanner.Build(atPoint, hasDestination: false).CrossAtHoldingPointNodeId);
    }

    // A crossing made with no stop of its own (#355's decision 3) is pinned by its NEAR side, the node before the
    // runway: where the route reached the runway can be the first node beyond it when one edge jumps across, and a pin
    // there read the far side as the near one and sent the leg back over. A fresh Calculate never takes the stale step
    // for a second crossing of that runway.
    [Fact]
    public void A_used_end_crossing_with_no_stop_is_pinned_on_its_near_side()
    {
        var steps = new[] { Via("P"), CrossRwy("24L", at: "P") };
        var kept = Trim(steps, used: new[] { Rec("24L", 0, at: "P") with { ReachNodeId = 58, NearNodeId = 57 } });

        Assert.Equal(new[] { Via("P"), CrossRwy("24L", at: "P") with { AtNodeId = 57, AtNodeIsPassedStop = true } }, kept);
        var plan = ClearancePlanner.Build(kept, hasDestination: false);
        Assert.Null(plan.CrossAtHoldingPointNodeId);
        Assert.Empty(plan.Crossings);
        Assert.False(kept[1].AtNodeIsReach);
        // No node before the runway on the route (it started on the pavement, or the aircraft itself was the last clear
        // point and the route's first node lies beyond the runway): the reach, marked as one. Its side is never trusted.
        var reachOnly = Trim(steps, used: new[] { Rec("24L", 0, at: "P") with { ReachNodeId = 58 } });
        Assert.Equal(CrossRwy("24L", at: "P") with { AtNodeId = 58, AtNodeIsPassedStop = true, AtNodeIsReach = true },
            reachOnly[1]);
        // Neither: nothing to pin it by, it stays as entered.
        Assert.Equal(steps, Trim(steps, used: new[] { Rec("24L", 0, at: "P") }));
    }

    // Ruling 4 (Task 6 re-review): a crossing already made only says where the leg ends. It is never cleared again, by
    // its stop, its near side or its reach, so a fresh Calculate can never clear a crossing of that runway the new
    // route makes ("crossed a second time"); a crossing still to make next to it is cleared as entered.
    [Fact]
    public void A_crossing_already_made_is_never_cleared_again()
    {
        var byStop = CrossRwy("04L", at: "C") with { AtNodeId = 77, AtNodeIsPassedStop = true };
        var byReach = CrossRwy("24L") with { AtNodeId = 58, AtNodeIsPassedStop = true, AtNodeIsReach = true };
        var reachOnlyFlag = CrossRwy("24L") with { AtNodeId = 58, AtNodeIsReach = true };

        Assert.Empty(Leg(Via("C"), byStop).Crossings);
        Assert.Empty(Leg(Via("P"), byReach).Crossings);
        Assert.Empty(Leg(Via("P"), reachOnlyFlag).Crossings);
        Assert.Equal(ClearanceEndKind.AcrossRunway, Leg(Via("C"), byStop).End);
        Assert.Empty(To(null, Via("C"), byStop, Via("D")).Crossings);

        var mixed = To(null, Via("C"), byStop, Via("D"), CrossRwy("09"));
        Assert.Equal(new[] { new ClearedCrossing("09", SeqIndex: 1) }, mixed.Crossings);
    }

    // Ruling 4: once the pilot adds a step after it, a crossing already made no longer ends the list: it is completed
    // and goes. Only a made crossing that some step follows; one still ending the list, or a crossing still to make, stays.
    [Fact]
    public void A_made_crossing_some_step_follows_is_completed()
    {
        var made = CrossRwy("04L") with { AtNodeId = 77, AtNodeIsPassedStop = true };
        var reach = CrossRwy("24L") with { AtNodeId = 58, AtNodeIsPassedStop = true, AtNodeIsReach = true };

        Assert.Empty(ClearanceTrim.CompletedCrossings(new[] { Via("C"), made }));
        Assert.Equal(new[] { 1 }, ClearanceTrim.CompletedCrossings(new[] { Via("C"), made, HoldRwy("04R") }));
        Assert.Equal(new[] { 1 }, ClearanceTrim.CompletedCrossings(new[] { Via("P"), reach, HoldTwy("L") }));
        Assert.Empty(ClearanceTrim.CompletedCrossings(new[] { Via("C"), CrossRwy("04L"), HoldRwy("04R") }));
        Assert.Empty(ClearanceTrim.CompletedCrossings(Array.Empty<ClearanceStep>()));
    }

    [Fact]
    public void A_crossing_put_back_and_a_later_crossing_of_the_same_runway_are_two_crossings()
    {
        var steps = new[] { Via("P"), CrossRwy("04L"), Via("Q"), CrossRwy("04L"), HoldTwy("L") };
        var kept = Trim(steps, putBack: new[] { Rec("04L", 0) }, inForce: new[] { Rec("04L", 1) });
        Assert.Equal(new[] { Via("P"), HoldRwy("04L"), Via("Q"), CrossRwy("04L"), HoldTwy("L") }, kept);
        // Neither names a place, and they follow different taxiways: two crossings, so Calculate takes it.
        Assert.Null(ClearancePlanner.Build(kept, hasDestination: false).Refusal);
    }

    [Fact]
    public void Trim_matches_an_exact_place_before_a_wildcard()
    {
        // The plain step is the crossing in force; the one "at B4" is the one used.
        var steps = new[] { Via("P"), CrossRwy("24L"), CrossRwy("24L", at: "B4") };
        Assert.Equal(new[] { Via("P"), CrossRwy("24L") },
            Trim(steps, destination: true, used: new[] { Rec("24L", 0, at: "B4") }, inForce: new[] { Rec("24L", 0) }));
        var leg = new[] { Via("P"), CrossRwy("24L"), CrossRwy("24L", at: "B4"), HoldTwy("L") };
        Assert.Equal(new[] { Via("P"), CrossRwy("24L"), HoldTwy("L") },
            Trim(leg, used: new[] { Rec("24L", 0, at: "B4") }, inForce: new[] { Rec("24L", 0) }));
    }

    [Fact]
    public void Trim_shows_a_turn_already_made_as_via()
    {
        var steps = new[] { Via("H"), Left("C"), HoldRwy("27") };
        Assert.Equal(new[] { Via("C"), HoldRwy("27") }, Trim(steps, done: 1, turnMade: true));
        Assert.Equal(new[] { Left("C"), HoldRwy("27") }, Trim(steps, done: 1, turnMade: false));
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

    // ---- The window's list: trimmed from the list as last calculated, keeping the pilot's edits (Task 7) ----

    [Fact]
    public void Trim_says_where_each_step_still_ahead_came_from()
    {
        var steps = new[] { Via("A"), HoldRwy("09"), Via("B"), CrossRwy("27L"), HoldTwy("L") };
        var kept = ClearanceTrim.Remaining(steps, 1, false, new[] { Rec("27L", 1) }, Array.Empty<ClearedCrossing>(),
            Array.Empty<ClearedCrossing>(), Array.Empty<ClearanceHold>(), false, out var origins);
        // The 09 stop moved after B (never the limit): each step keeps its place in the list as calculated.
        Assert.Equal(new[] { Via("B"), HoldRwy("09"), CrossRwy("27L"), HoldTwy("L") }, kept);
        Assert.Equal(new[] { 2, 1, 3, 4 }, origins);

        // The last finished Via step kept in front of a stop it cannot move, read as Via: its own place.
        var kept2 = ClearanceTrim.Remaining(new[] { Left("A"), HoldRwy("09"), Via("B") }, 1, false,
            Array.Empty<ClearedCrossing>(), Array.Empty<ClearedCrossing>(), Array.Empty<ClearedCrossing>(),
            Array.Empty<ClearanceHold>(), false, out var origins2);
        Assert.Equal(new[] { Via("A"), HoldRwy("09"), Via("B") }, kept2);
        Assert.Equal(new[] { 0, 1, 2 }, origins2);
    }

    [Fact]
    public void A_trim_keeps_the_steps_added_since_and_never_brings_back_one_removed()
    {
        var trimmed = new[] { Via("B"), HoldRwy("09"), CrossRwy("27L"), HoldTwy("L") };
        var trimmedOrigins = new[] { 2, 1, 3, 4 };
        // Shown: the list as calculated, then a step the pilot added (-1).
        var shown = new[] { Via("A"), HoldRwy("09"), Via("B"), CrossRwy("27L"), HoldTwy("L"), Via("M") };
        var shownOrigins = new[] { 0, 1, 2, 3, 4, -1 };

        var (steps, origins) = ClearanceTrim.KeepingEdits(trimmed, trimmedOrigins, shown, shownOrigins, new HashSet<int>());
        Assert.Equal(new[] { Via("B"), HoldRwy("09"), CrossRwy("27L"), HoldTwy("L"), Via("M") }, steps);
        Assert.Equal(new[] { 2, 1, 3, 4, -1 }, origins);

        // The pilot removed the 27L crossing since: it stays removed, whatever the trim keeps.
        var (without, withoutOrigins) = ClearanceTrim.KeepingEdits(trimmed, trimmedOrigins,
            new[] { Via("A"), HoldRwy("09"), Via("B"), HoldTwy("L") }, new[] { 0, 1, 2, 4 }, new HashSet<int> { 3 });
        Assert.Equal(new[] { Via("B"), HoldRwy("09"), HoldTwy("L") }, without);
        Assert.Equal(new[] { 2, 1, 4 }, withoutOrigins);

        // A step the trim changed (a turn made, a crossing put back) is shown as the trim has it.
        var (turned, _) = ClearanceTrim.KeepingEdits(new[] { Via("C"), HoldRwy("27") }, new[] { 1, 2 },
            new[] { Via("H"), Left("C"), HoldRwy("27") }, new[] { 0, 1, 2 }, new HashSet<int>());
        Assert.Equal(new[] { Via("C"), HoldRwy("27") }, turned);
    }

    // A changed step is a new step (origin -1) in the middle of the list, its old origin (1) removed.
    private static (List<ClearanceStep> Steps, List<int> Origins) ChangedStepTrim(ClearanceStep[] trimmed, int[] trimmedOrigins)
        => ClearanceTrim.KeepingEdits(trimmed, trimmedOrigins,
            new[] { Via("A"), Via("B"), Via("D"), HoldRwy("27") }, new[] { 0, -1, 2, 3 }, new HashSet<int> { 1 });

    [Fact]
    public void KeepingEdits_keeps_a_changed_step_where_it_was()
    {
        var (steps, origins) = ChangedStepTrim(new[] { Via("A"), Via("C"), Via("D"), HoldRwy("27") }, new[] { 0, 1, 2, 3 });
        Assert.Equal(new[] { Via("A"), Via("B"), Via("D"), HoldRwy("27") }, steps);
        Assert.Equal(new[] { 0, -1, 2, 3 }, origins);
    }

    [Fact]
    public void KeepingEdits_keeps_a_changed_step_after_the_step_before_it_is_finished()
    {
        var (steps, origins) = ChangedStepTrim(new[] { Via("C"), Via("D"), HoldRwy("27") }, new[] { 1, 2, 3 });
        Assert.Equal(new[] { Via("B"), Via("D"), HoldRwy("27") }, steps);
        Assert.Equal(new[] { -1, 2, 3 }, origins);
    }

    [Fact]
    public void KeepingEdits_puts_a_changed_step_before_the_next_step_that_survives()
    {
        var (steps, origins) = ChangedStepTrim(new[] { HoldRwy("27") }, new[] { 3 });
        Assert.Equal(new[] { Via("B"), HoldRwy("27") }, steps);
        Assert.Equal(new[] { -1, 3 }, origins);
    }

    [Fact]
    public void KeepingEdits_puts_a_changed_step_last_when_no_later_step_survives()
    {
        var (steps, origins) = ChangedStepTrim(Array.Empty<ClearanceStep>(), Array.Empty<int>());
        Assert.Equal(new[] { Via("B") }, steps);
        Assert.Equal(new[] { -1 }, origins);
    }

    [Fact]
    public void A_crossing_put_back_is_found_by_its_record_numbered_as_entered()
    {
        // The window's list: route loading left out A (never driven), so guidance numbers P's row 0, the window 1.
        var steps = new[] { Via("A"), Via("P"), CrossRwy("04L"), HoldTwy("L") };
        var restored = new[] { Rec("04L", 0, stopNodeId: 41) };
        var progress = Progress(putBack: new[] { Rec("04L", 1, stopNodeId: 41) });
        Assert.Equal(new[] { 2 }, ClearanceTrim.PutBackSteps(steps, restored, progress));

        // Matched by runway name alone it would be the wrong step: the record says which.
        var twoPlaces = new[] { Via("P"), CrossRwy("24L", at: "P"), Via("Q"), CrossRwy("24L", at: "Q"), HoldTwy("L") };
        var atQ = Rec("24L", 1, at: "Q", stopNodeId: 51);
        var both = Progress(putBack: new[] { atQ }, inForce: new[] { Rec("24L", 0, at: "P", stopNodeId: 41) });
        Assert.Equal(new[] { 3 }, ClearanceTrim.PutBackSteps(twoPlaces, new[] { atQ }, both));
    }

    [Fact]
    public void Only_the_crossings_this_press_put_back_are_followed()
    {
        var steps = new[] { Via("P"), CrossRwy("24L", at: "P"), Via("Q"), CrossRwy("09"), HoldTwy("L") };
        var earlier = Rec("24L", 0, at: "P", stopNodeId: 41);
        var now = Rec("09", 1, stopNodeId: 51);
        var progress = Progress(putBack: new[] { earlier, now });
        Assert.Equal(new[] { 3 }, ClearanceTrim.PutBackSteps(steps, new[] { now }, progress));

        // A Continue's crossing stands for no step, even at a stop shared with one the pilot entered.
        var continued = Rec("27", -1, origin: ClearedBy.Continue, stopNodeId: 51);
        Assert.Equal(new[] { 3 }, ClearanceTrim.PutBackSteps(steps, new[] { continued, now }, progress));
        Assert.Empty(ClearanceTrim.PutBackSteps(steps, new[] { continued }, progress));

        // Gone from the put-back list by the time the window looks (passed, or cleared again): nothing to follow.
        Assert.Empty(ClearanceTrim.PutBackSteps(steps, new[] { now }, Progress(used: new[] { now })));
    }

    [Fact]
    public void A_crossing_put_back_becomes_a_hold_short_at_its_place()
    {
        var shown = new[] { Via("P"), CrossRwy("24L", at: "P"), Via("Q"), CrossRwy("24L", at: "Q"), Via("M") };
        var origins = new[] { 0, 1, 2, 3, -1 };
        Assert.Equal(new[] { Via("P"), HoldRwyAt("24L", "P"), Via("Q"), CrossRwy("24L", at: "Q"), Via("M") },
            ClearanceTrim.FollowingPutBack(shown, origins, new[] { 1 }));
        // Only a Cross runway step still shown is changed: one the pilot removed, or already changed, is left alone.
        Assert.Equal(shown, ClearanceTrim.FollowingPutBack(shown, origins, new[] { 0, 4 }));
        Assert.Equal(new[] { Via("P"), HoldRwyAt("24L", "P") },
            ClearanceTrim.FollowingPutBack(new[] { Via("P"), HoldRwyAt("24L", "P") }, new[] { 0, 1 }, new[] { 1 }));
    }

    private static ClearanceProgress Progress(ClearedCrossing[]? inForce = null, ClearedCrossing[]? used = null,
        ClearedCrossing[]? putBack = null)
        => new(0, inForce ?? Array.Empty<ClearedCrossing>(), used ?? Array.Empty<ClearedCrossing>(),
            putBack ?? Array.Empty<ClearedCrossing>(), Array.Empty<ClearanceHold>(), false);
}
