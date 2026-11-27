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
    // an overrun onto the last segment never adds the leg end's "... Stop." to the hold's own sentence. It is
    // counted down along the route, as a hold short is (RouteAhead.NextCountdownStop, RouteAheadCountdownStopTests).
    [Fact]
    public void The_leg_end_countdown_runs_only_while_taxiing()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));
        string countdown = Member(text, "private void CheckHoldShortCountdown(");

        Assert.Contains("bool taxiing = _state == TaxiGuidanceState.Taxiing;", countdown);
        Assert.Matches(@"RouteAhead\.NextCountdownStop\(_route\.Segments, _currentSegmentIndex, distToTargetM, HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M,\s+legEnd: _progressiveTerminator != null && taxiing, cleared: taxiing\)", countdown);
        Assert.Contains("var legEnd = stop.Kind == RouteAhead.CountdownStopKind.LegEnd ? _progressiveTerminator : null;", countdown);
        Assert.DoesNotContain("RouteAhead.NextStop(", countdown);
    }

    // The frame's runway tests are asked whenever the leg end is the countdown's next stop, not only on its last segment:
    // on a leg whose last pieces are short its "Slow down" and "Stop" come before that segment, and BA never asks for a
    // stop on a runway (ProgressiveLegEnd.MaySpeak). The arrival reads the leg end's own "Slow down" only.
    [Fact]
    public void The_leg_end_s_runway_tests_and_slow_down_follow_the_along_route_countdown()
    {
        string text = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Matches(@"bool legEndInReach = _progressiveTerminator != null\s+&& \(onFinalSegment\s+\|\| RouteAhead\.NextCountdownStop\(_route\.Segments, _currentSegmentIndex, distToTarget,\s+HOLD_SHORT_COUNTDOWN_LOOKAHEAD_M, legEnd: true, cleared: false\)\s+is \{ Kind: RouteAhead\.CountdownStopKind\.LegEnd \}\);", text);
        Assert.Contains("bool nearARunway = legEndInReach", text);
        Assert.Contains("bool legEndSlowDownSaid = _holdShortSlowDownAnnounced && currentSeg.ToNode?.NodeId == _holdShortCountdownNodeId;", text);
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
        Assert.Matches(@"ProgressiveLegEnd\.Arrived\(legEnd\.Type, distToTarget, groundSpeedKts,\s+legEndSlowDownSaid, nearARunway, onRunwayPavement\)", text);
    }

    // A hold short of a taxiway, mid-route or at the end of a leg, stops where the pure
    // TaxiwayHoldPlacement says (TaxiwayHoldPlacementTests), a leg end never on a Turn pick's link, and a split
    // re-indexes the Turn picks. The leg end is cut back only while the route still ends at the junction: a
    // recalculation that keeps a Turn pick's tail ends at the stop placed at load and is never cut again (TURN-3).
    [Fact]
    public void Taxiway_holds_are_placed_through_TaxiwayHoldPlacement()
    {
        string text = RoutingSource();

        Assert.Contains("TaxiwayHoldPlacement.Choose(", text);
        Assert.Contains("TaxiwayHoldPlacement.PlaceLegEnd(", text);
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
        Assert.Matches(@"if \(!keepsLoadedTail && _legEndAtTurnStart == null\)\s+ApplyLegEndSetback\(newRoute, _originalTaxiwaySequence, allowInsert: false\);", text);
        Assert.Single(Regex.Matches(text, @"ApplyLegEndSetback\("));
    }

    // Fix round 2: a leg end the load placed at a turn's start (ChooseForLegEnd moved it there, the pick dropped) keeps
    // that end on a recalculation. The recalculation routes to its stop node, over the rows that lead to it
    // (TaxiwayHoldPlacement.RowsLeadingToTurnStart), and never sets it back again (above); a fresh route to the junction
    // with the pick gone could take the charted way and put the stop near B, after the turn. The end is stored at load
    // only, and joins LoadRoute's reset, its Fail snapshot and its rollback [RTE-29] and StopGuidance's reset.
    [Fact]
    public void A_leg_end_at_a_turn_start_is_kept_by_a_recalculation()
    {
        string text = RoutingSource();
        string recalc = RecalculationSource();
        string setback = Member(text, "private string? ApplyLegEndSetback(");
        string load = Member(text, "public string? LoadRoute(");
        string manager = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));

        Assert.Contains("int recalcDestinationNodeId = _legEndAtTurnStart?.StopNodeId ?? _destinationNodeId;", recalc);
        Assert.Contains("r.FindConstrainedPath(startNodeId, recalcDestinationNodeId, routeSequence,", recalc);
        Assert.Contains("r.FindShortestPath(startNodeId, recalcDestinationNodeId)", recalc);
        Assert.DoesNotContain("r.FindShortestPath(startNodeId, _destinationNodeId)", recalc);
        Assert.Contains("TaxiwayHoldPlacement.RowsLeadingToTurnStart(remainingSequence, _originalTaxiwaySequence.Count, keptEnd.RowsBefore)", recalc);

        Assert.Contains("if (allowInsert && movedTo is { } turn)", setback);
        Assert.Contains("_legEndAtTurnStart = (route.Segments[^1].ToNode.NodeId, turn.RowIndex);", setback);
        Assert.Contains("_legEndAtTurnStart = null;", load);
        Assert.Contains("_legEndAtTurnStart = priorLegEndAtTurnStart;", load);
        Assert.Contains("_legEndAtTurnStart", Member(text, "private LoadRouteRollback CaptureLoadRouteRollback("));
        Assert.Contains("_legEndAtTurnStart = r.LegEndAtTurnStart;", Member(text, "private void RestoreLoadRouteRollback("));
        Assert.Contains("private (int StopNodeId, int RowsBefore)? _legEndAtTurnStart;", manager);
        Assert.Contains("_legEndAtTurnStart = null;", Member(manager, "public void StopGuidance("));
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

    // Owner, 2026-11-25 ("Stop before the turn"): a taxiway stop no longer floors at a Turn pick's taxiway, so "Left
    // onto F" stops where "Via F" does. A mid-route stop is placed by plain Choose; a leg end by
    // TaxiwayHoldPlacement.ChooseForLegEnd with every pick on the route, any row's, which ends a leg that would end on
    // or at the far end of a pick's link where that turn starts (TaxiwayHoldPlacementTests). A leg end before a picked
    // turn drops the pick from the route, and the load note is composed from the picks still on the route: no
    // "Turning left onto taxiway F, as picked." for a turn the leg ends before. A mid-route stop keeps its pick (the
    // turn is made after Continue).
    [Fact]
    public void A_taxiway_stop_is_placed_around_a_picked_turn_and_the_note_names_only_turns_still_on_the_route()
    {
        string text = RoutingSource();
        string place = Member(text, "private int PlaceTaxiwayHoldStop(");
        string setback = Member(text, "private string? ApplyLegEndSetback(");
        string midRoute = Member(text, "private string? ApplyUserTaxiwayHoldShorts(");
        string load = Member(text, "public string? LoadRoute(");

        Assert.Contains("? TaxiwayHoldPlacement.PlaceLegEnd(route.Segments, junctionSegmentIndex, setbackM, allowInsert, clear, route.TurnPicks)", place);
        Assert.Contains(": new TaxiwayHoldPlacement.LegEndStop(TaxiwayHoldPlacement.Choose(route.Segments, junctionSegmentIndex, setbackM, allowInsert, clear), null, false);", place);
        Assert.Contains("if (placed.AtTurnStart) movedBeforeTurn?.Invoke(placed.OnLinkOf!);", place);
        Assert.DoesNotContain("firstUsableIndex", place);
        Assert.DoesNotContain("ChooseAroundTurn", text);
        Assert.Contains("endsLeg: true", setback);
        var midRouteCall = Regex.Match(midRoute, @"PlaceTaxiwayHoldStop\(route, holdSeg,[^;]*;");
        Assert.True(midRouteCall.Success, "the mid-route PlaceTaxiwayHoldStop call not found");
        Assert.DoesNotContain("endsLeg", midRouteCall.Value);

        // A pick is dropped only when the leg ends before its turn starts; one whose link the leg ends on (no usable node
        // before the turn) is kept, so its notes and an off-chart leg's sentence are said.
        Assert.Contains("route.TurnPicks = route.TurnPicks.Where(q => q.FirstSegmentAfterDeparture <= last).ToList();", setback);
        Assert.Matches(@"TurnPickMessages\.ComposeLoadNote\(\s*route\.TurnPicks, turnPickResult\.Refusals,", load);
        Assert.DoesNotContain("turnPickResult.Applied, turnPickResult.Refusals", load);
        int cut = load.IndexOf("ApplyLegEndSetback(route, taxiwaySequence, allowInsert: true, legEndCrossedTaxiways)", StringComparison.Ordinal);
        int note = load.IndexOf("TurnPickMessages.ComposeLoadNote(", StringComparison.Ordinal);
        Assert.True(cut >= 0 && note > cut, "the note is composed after the leg end has dropped the picks it ends before");
        // The mid-route stops keep their picks: only the leg end drops one.
        Assert.Single(Regex.Matches(text, @"route\.TurnPicks = route\.TurnPicks\.Where\("));
    }

    // Main's #369 (merged 2026-11-27): a runway route ending more than 400 m from its runway is spoken as the end of the
    // route ("End of route ahead.", "End of route in 35 metres. Stop.", "Stop. End of this route. It does not reach
    // Runway 20L. …"). A leg with no destination never is: the flag is set only for a runway destination with a lineup
    // target, on a load and on a recalculation, and the window loads a leg as no runway destination.
    [Fact]
    public void A_leg_never_ends_as_a_route_that_misses_its_runway()
    {
        string load = Member(RoutingSource(), "public string? LoadRoute(");
        string recalc = RecalculationSource();
        string form = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

        Assert.Contains("_isRunwayLineup = isRunwayDestination;", load);
        Assert.Matches(@"_routeEndsFarFromRunway = false;\s+_routeEndVerdict = RunwayReachVerdict\.Reaches;\s+if \(isRunwayDestination && _hasLineupTarget\)\s+\{[^}]*_routeEndsFarFromRunway = RunwayReachGate\.EndsFarFromRunway\(", load);
        Assert.Single(Regex.Matches(load, @"_routeEndsFarFromRunway = RunwayReachGate\.EndsFarFromRunway\("));
        Assert.Matches(@"if \(_isRunwayLineup\)\s+\{[^}]*_routeEndsFarFromRunway = RunwayReachGate\.EndsFarFromRunway\([^}]*\}\s+else\s+\{[^}]*_routeEndsFarFromRunway = false;", recalc);
        Assert.Single(Regex.Matches(recalc, @"_routeEndsFarFromRunway = RunwayReachGate\.EndsFarFromRunway\("));

        string leg = Member(form, "private void CalculateLegWithNoDestination(");
        Assert.Contains("isRunwayDestination: false,", leg);
        Assert.Contains("progressiveTerminator: term,", leg);
        // Only a leg is loaded with a terminator, so a runway route never takes a leg end's words.
        Assert.Single(Regex.Matches(form, @"progressiveTerminator: "));
    }

    // And each says its own end, once: a leg's countdown and arrival come first wherever both are asked, the far end's
    // only after them, and only for a runway route.
    [Fact]
    public void A_leg_end_and_a_route_end_far_from_its_runway_each_speak_their_own_words_once()
    {
        string manager = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.cs"));
        string announcements = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", "TaxiGuidanceManager.Announcements.cs"));

        // The approach: a leg has no "X ahead." at all; the far end's "End of route ahead." is past that return.
        string upcoming = Member(announcements, "private void CheckUpcomingAnnouncements(");
        int legReturn = upcoming.IndexOf("if (_progressiveTerminator != null)", StringComparison.Ordinal);
        int farAhead = upcoming.IndexOf("\"End of route ahead.\"", StringComparison.Ordinal);
        Assert.True(legReturn >= 0 && farAhead > legReturn, "a leg returns before the far end's approach line");

        // The countdown: one line per tier, the leg end's first, then a cleared crossing's, then `lead` ("End of route"
        // only on a runway route's last segment that misses its runway).
        string countdown = Member(manager, "private void CheckHoldShortCountdown(");
        Assert.Contains("string lead = _routeEndsFarFromRunway && stopIndex == _route.Segments.Count - 1", countdown);
        Assert.Equal(3, Regex.Matches(countdown, @"AnnounceInstruction\(legEnd != null\s+\? LegEndLine\(legEnd, CountdownTier\.\w+\)\s+: clearedCrossing \? ClearedLine\(\)\s+: \$""\{lead\} in ").Count);
        Assert.Contains("if (legEnd == null && !clearedCrossing && _holdShortSlowDownAnnounced && !_holdShortStopAnnounced", countdown);

        // The arrival: the leg's intercept first; the far end's needs a runway lineup.
        string arrival = Member(manager, "private void HandleArrival(");
        int legArrival = arrival.IndexOf("if (_progressiveTerminator is { } term)", StringComparison.Ordinal);
        int farArrival = arrival.IndexOf("if (_hasLineupTarget && _isRunwayLineup && _routeEndsFarFromRunway)", StringComparison.Ordinal);
        Assert.True(legArrival >= 0 && farArrival > legArrival, "a leg ends before the far end's branch is asked");

        // The status: the leg names its own end; "to the end of the route" only with the far-end flag.
        string status = Member(announcements, "public string GetStatusAnnouncement(");
        Assert.Contains("Navigation.RunwayReachGate.StatusDistanceTarget(_destinationName, _routeEndsFarFromRunway)", status);
        Assert.Contains("if (_state == TaxiGuidanceState.Arrived && _routeEndsFarFromRunway)", status);
    }

    // A member's text: from its declaration to the next member at class level.
    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
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
