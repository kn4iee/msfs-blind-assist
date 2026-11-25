// A Progressive Taxi leg with no destination ends the way a hold short does: the same countdown by
// speed, and a stop short of the point counts as arriving. The decision and the wording live in the
// pure ProgressiveLegEnd (ProgressiveLegEndTests); TaxiGuidanceManager has no test harness, so this
// pins from the source that the manager asks it, rather than keeping a second copy of either.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class LegEndWiringTests
{
    [Fact]
    public void The_manager_ends_a_no_destination_leg_through_ProgressiveLegEnd()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Contains("ProgressiveLegEnd.Arrived(", text);
        Assert.Contains("ProgressiveLegEnd.CountdownLead(", text);
        Assert.Contains("ProgressiveLegEnd.Countdown(", text);
    }

    // A leg end never ends a hold in progress. AdvanceToNearestSegment can enter a mid-route hold
    // earlier in the same position frame, which then reaches the arrival test with that hold's
    // "Slow down" latch still set: the arrival test and the past-the-node backstop both wait for Taxiing.
    [Fact]
    public void The_leg_end_is_judged_only_while_taxiing()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Contains("bool legEndTaxiing = legEnd != null && _state == TaxiGuidanceState.Taxiing;", text);
        Assert.Contains("legEndTaxiing && ProgressiveLegEnd.Arrived(", text);
        Assert.Contains("(!legEndTaxiing || ProgressiveLegEnd.HeldOffByRunway(legEnd.Type, nearARunway, onRunwayPavement))", text);
    }

    // The countdown of a leg end runs only while Taxiing too: the frame that enters a mid-route hold after
    // an overrun onto the last segment never adds the leg end's "... Stop." to the hold's own sentence.
    [Fact]
    public void The_leg_end_countdown_runs_only_while_taxiing()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));
        int method = text.IndexOf("private void CheckHoldShortCountdown(", StringComparison.Ordinal);
        Assert.True(method >= 0, "CheckHoldShortCountdown not found");
        int legEndAt = text.IndexOf("var legEnd = ", method, StringComparison.Ordinal);
        int choiceEnd = text.IndexOf("? _progressiveTerminator : null;", method, StringComparison.Ordinal);
        Assert.True(legEndAt > method && choiceEnd > legEndAt, "the countdown's legEnd choice not found");
        Assert.Contains("_state == TaxiGuidanceState.Taxiing", text[legEndAt..choiceEnd]);
    }

    // BA never asks for a stop on a runway: the countdown takes the frame's runway tests and asks
    // ProgressiveLegEnd.MaySpeak (unit-tested there) before it speaks or latches "Slow down" or "Stop".
    [Fact]
    public void The_countdown_asks_ProgressiveLegEnd_before_a_slow_down_or_stop()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Contains("CheckHoldShortCountdown(distToTarget, currentSeg, nearARunway, onRunwayPavement);", text);
        Assert.Contains("ProgressiveLegEnd.MaySpeak(legEnd.Type, CountdownTier.SlowDown, nearARunway, onRunwayPavement)", text);
        Assert.Contains("ProgressiveLegEnd.MaySpeak(legEnd.Type, CountdownTier.Stop, nearARunway, onRunwayPavement)", text);
        Assert.Contains("if (stopMaySpeak) _holdShortSlowDownAnnounced = true;", text);
        Assert.Contains("!_holdShortSlowDownAnnounced && slowDownMaySpeak)", text);
        Assert.Contains("!_holdShortStopAnnounced && stopMaySpeak)", text);
    }

    // No leg ends on runway pavement, whatever its ending (spec 8.5): the frame asks once, from the graph's
    // own runway geometry (RunwayShape, no margin), never building one [SUR-24], and hands the answer to
    // ProgressiveLegEnd.Arrived (unit-tested there).
    [Fact]
    public void The_arrival_test_is_given_the_frame_s_runway_pavement_test()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Contains("bool onRunwayPavement = nearARunway", text);
        Assert.Contains("Navigation.RouteRunwayCrossings.RunwayUnder(_graph?.RunwayCenterlines, lat, lon) != null;", text);
        Assert.Matches(@"ProgressiveLegEnd\.Arrived\(legEnd\.Type, distToTarget, groundSpeedKts,\s+_holdShortSlowDownAnnounced, nearARunway, onRunwayPavement\)", text);
    }

    // A hold short of a taxiway, mid-route or at the end of a leg, stops where the pure
    // TaxiwayHoldPlacement says (TaxiwayHoldPlacementTests), and a split re-indexes the Turn picks.
    // The leg end is cut back only while the route still ends at the junction: a recalculation that
    // keeps a Turn pick's tail ends at the stop placed at load and is never cut again (TURN-3).
    [Fact]
    public void Taxiway_holds_are_placed_through_TaxiwayHoldPlacement()
    {
        string text = RoutingSource();

        Assert.Contains("TaxiwayHoldPlacement.Choose(", text);
        Assert.Contains("TaxiwayHoldPlacement.ShiftPicksAfterSplit(", text);
        Assert.Contains("ApplyLegEndSetback(", text);
        Assert.Contains("Segments[^1].ToNode.NodeId == _destinationNodeId", text);
    }

    // The setback runs on the route the Turn picks leave (a pick can replace the route and move the
    // junction, TURN-1) and before the pilot's own hold-short picks; a recalculation cuts its route
    // before the sanity gate, so the gate compares two cut routes.
    [Fact]
    public void The_leg_end_setback_runs_after_the_Turn_picks_and_before_the_pilot_picks()
    {
        string text = RoutingSource();

        int turnPicks = text.IndexOf("TurnPickApplier.Apply(route", StringComparison.Ordinal);
        int loadSetback = text.IndexOf("ApplyLegEndSetback(route, taxiwaySequence, allowInsert: true, legEndCrossedTaxiways)", StringComparison.Ordinal);
        int pilotPicks = text.IndexOf("ApplyUserHoldShorts(route, taxiwaySequence, userHoldShortIndices)", StringComparison.Ordinal);
        int recalcSetback = text.IndexOf("ApplyLegEndSetback(newRoute, _originalTaxiwaySequence, allowInsert: false)", StringComparison.Ordinal);
        int sanityGate = text.IndexOf("bool lengthBlowUp", StringComparison.Ordinal);

        Assert.True(turnPicks >= 0, "TurnPickApplier.Apply(route not found");
        Assert.True(loadSetback >= 0, "the load's ApplyLegEndSetback call not found");
        Assert.True(pilotPicks >= 0, "ApplyUserHoldShorts(route, ...) not found");
        Assert.True(recalcSetback >= 0, "the recalculation's ApplyLegEndSetback call not found");
        Assert.True(sanityGate >= 0, "bool lengthBlowUp not found");
        Assert.True(turnPicks < loadSetback, "the setback must run after the Turn picks");
        Assert.True(loadSetback < pilotPicks, "the setback must run before the pilot's hold-short picks");
        Assert.True(recalcSetback < sanityGate, "the recalculation's setback must run before the sanity gate");
    }

    // A recalculation that keeps a Turn pick's tail ends at the stop placed at load and is never cut again
    // (TURN-3): the end-node test alone would cut a load-time Junction stop back a second time.
    [Fact]
    public void A_kept_Turn_pick_tail_is_never_cut_back_again()
    {
        string text = RecalculationSource();

        Assert.Contains("bool keepsLoadedTail = keptTailRoute != null && ReferenceEquals(newRoute, keptTailRoute);", text);
        Assert.Matches(@"if \(!keepsLoadedTail\)\s+ApplyLegEndSetback\(newRoute, _originalTaxiwaySequence, allowInsert: false\);", text);
        Assert.Single(Regex.Matches(text, @"ApplyLegEndSetback\("));
    }

    // A recalculation runs off the UI thread and never inserts a node [RTE-30]: both of its taxiway-hold
    // placements, the leg end's and the pilot's own "hold short of" picks, pass allowInsert: false.
    [Fact]
    public void A_recalculation_never_inserts_a_node()
    {
        string text = RecalculationSource();

        Assert.Matches(@"ApplyLegEndSetback\(newRoute, _originalTaxiwaySequence, allowInsert: false\)", text);
        Assert.Matches(@"ApplyUserTaxiwayHoldShorts\(\s*newRoute, holdSequence, remappedTwy, allowInsert: false, rowOffset: seqOffset,\s+placedStops: recalcPlacedStops\)", text);
        var calls = Regex.Matches(text, @"(ApplyLegEndSetback|ApplyUserTaxiwayHoldShorts)\([^;]*;");
        Assert.Equal(2, calls.Count);
        foreach (Match call in calls)
        {
            Assert.Contains("allowInsert: false", call.Value);
            Assert.DoesNotContain("allowInsert: true", call.Value);
        }
        Assert.DoesNotContain("allowInsert: true", text);
    }

    // A taxiway stop's approach is judged over the same 25 m that captures it, mid-route (the manager's
    // waypoint capture) and at a leg end (ProgressiveLegEnd): one value, so they cannot drift apart.
    [Fact]
    public void The_stop_approach_is_the_waypoint_capture_radius()
    {
        var field = typeof(MSFSBlindAssist.Services.TaxiGuidanceManager).GetField("WAYPOINT_CAPTURE_RADIUS_M",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal(MSFSBlindAssist.Navigation.ProgressiveLegEnd.CaptureRadiusM, (double)field!.GetRawConstantValue()!);
    }

    // TryRecalculateRoute's body: from its declaration to the next member at class level.
    private static string RecalculationSource()
    {
        string text = RoutingSource();
        int start = text.IndexOf("private void TryRecalculateRoute(", StringComparison.Ordinal);
        Assert.True(start >= 0, "TryRecalculateRoute not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        Assert.True(next.Success, "no member after TryRecalculateRoute");
        return text.Substring(start, next.Index + 1);
    }

    private static string RoutingSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.Routing.cs"));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
