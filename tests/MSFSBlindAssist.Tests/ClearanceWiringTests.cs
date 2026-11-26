// The Taxi Guidance window's step list reaches guidance through LoadRoute: the stops as lists, any number per
// taxiway, a stop with no taxiway of its own searched along the whole route, and back from it the progress the window
// trims its list with. The pure parts are tested where they live (ClearanceRowsTests, ClearanceProgressTests,
// ClearancePlannerTests, TurnPickApplierTests); TaxiGuidanceManager has no test harness [VP-1], so this pins from the
// source that the manager uses them.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class ClearanceWiringTests
{
    [Fact]
    public void Stops_are_placed_from_lists_and_search_the_whole_route()
    {
        string routing = RoutingSource();
        string manager = ManagerSource();

        Assert.DoesNotContain("Dictionary<int, string>? userRunwayHoldShorts", routing);
        Assert.DoesNotContain("Dictionary<int, string>? userTaxiwayHoldShorts", routing);
        Assert.DoesNotContain("Dictionary<int, Navigation.HoldingPointHoldShort>? userHoldingPointHoldShorts", routing);
        Assert.Contains("IReadOnlyList<KeyValuePair<int, string>>? userRunwayHoldShorts", routing);
        Assert.Contains("CheckClearance(", manager);
        Assert.Contains("GetClearanceProgress(", manager);
        Assert.Contains("ClearanceTrim.ViaStepsDone(", manager);

        // What a stop that finds nothing along the whole route says.
        Assert.Contains("$\"runway {runwayId} (the route does not cross it)\"", routing);
        Assert.Contains("$\"taxiway {rawTarget} (the route does not meet it)\"", routing);
        Assert.Contains("$\"{pick.Label} (the route does not pass it)\"", routing);
    }

    // [STR-13]: both public methods read the route under the lock. The progress is the pure ClearanceProgressState's
    // report, from the aircraft's last position, never a guess when there is none.
    [Fact]
    public void Progress_and_checks_are_read_under_the_lock()
    {
        string manager = ManagerSource();
        string check = Member(manager, "public IReadOnlyList<string> CheckClearance(");
        string progress = Member(manager, "public Navigation.ClearanceProgress GetClearanceProgress(");

        Assert.Contains("lock (_stateLock)", check);
        Assert.Contains("Navigation.ClearanceRouteChecks.Warnings(_route.Segments, plan, NodeTouchesTaxiway)", check);
        // The crossings the route clears, as applied (its runway events, after AdoptRoute's pass), in route order.
        string cleared = Member(manager, "public string DescribeClearedCrossings(");
        Assert.Contains("lock (_stateLock)", cleared);
        Assert.Contains("Navigation.ClearedCrossingCallouts.ClearedAtCalculate(_route.RunwayEvents)", cleared);
        Assert.Contains("lock (_stateLock)", progress);
        Assert.Contains("_positionInitialized", progress);
        Assert.Contains("FindRemainingSequenceByPosition(_lastLat, _lastLon,", progress);
        Assert.Contains("_clearanceProgress.Report(", progress);
        Assert.Contains("_state == TaxiGuidanceState.HoldShort", progress);
    }

    // [RTE-29]: the one new stored field is kept whole through a refused load and its rollback, and emptied by
    // StopGuidance and the rollout reset; a load starts it afresh with the crossings as entered.
    [Fact]
    public void The_progress_state_joins_every_reset_and_rollback()
    {
        string routing = RoutingSource();

        Assert.Contains("_clearanceProgress", FailSnapshot(routing));
        Assert.Contains("_clearanceProgress", Member(routing, "private LoadRouteRollback CaptureLoadRouteRollback("));
        Assert.Contains("_clearanceProgress =", Member(routing, "private void RestoreLoadRouteRollback("));
        Assert.Contains("_clearanceProgress = Navigation.ClearanceProgressState.Empty;", Member(ManagerSource(), "public void StopGuidance("));
        Assert.Contains("_clearanceProgress = Navigation.ClearanceProgressState.Empty;", Member(RolloutSource(), "private void EnterRunwayEndCountdown("));

        string load = Member(routing, "public string? LoadRoute(");
        Assert.Contains("_clearanceProgress = Navigation.ClearanceProgressState.For(clearedCrossings);", load);
    }

    // Ruling 2: the rows route loading leaves out are remembered, so progress is counted as the window numbered it.
    [Fact]
    public void Rows_left_out_are_remembered_for_the_numbering()
    {
        string load = Member(RoutingSource(), "public string? LoadRoute(");

        Assert.Contains("_clearanceProgress = _clearanceProgress.WithRowsLeftOut(droppedRowIndices);", load);
        // The stops as lists follow their rows; a stop on a skipped runway leg goes with it (and is said).
        Assert.Contains("ClearanceRows.Without(userRunwayHoldShorts, droppedRowIndices)", load);
        Assert.Contains("ClearanceRows.RowsOf(userRunwayHoldShorts)", load);
        Assert.DoesNotContain("userRunwayHoldShorts?.Keys", load);
    }

    // Final review, Important 1 [RTE-39]: a first taxiway left out as never driven keeps the stops entered after it, with
    // no row of their own (looked for along the whole route, as a crossing is), remembered as entered before they are
    // re-keyed; so no stop keeps that taxiway in the route, only a Turn pick does. A trimmed "Via A, Hold short of runway
    // 09, Via B" with the aircraft on B kept A, and the route went back along A behind the aircraft.
    [Fact]
    public void A_taxiway_left_out_keeps_its_stops_and_only_a_Turn_pick_keeps_it_in()
    {
        string load = Member(RoutingSource(), "public string? LoadRoute(");

        int gateStart = load.IndexOf("bool leaveOut = undriven > 0", StringComparison.Ordinal);
        Assert.True(gateStart >= 0, "the leave-out gate not found");
        string gate = load[gateStart..load.IndexOf(';', gateStart)];
        Assert.Contains("UndrivenClearanceStart.TurnPicksAllow(undriven, turnPicks?.Keys)", gate);
        Assert.DoesNotContain("RowsOf(", gate);
        Assert.DoesNotContain("userRunwayHoldShorts", gate);
        Assert.DoesNotContain("userTaxiwayHoldShorts", gate);
        Assert.DoesNotContain("userHoldingPointHoldShorts", gate);

        int remembered = load.IndexOf("_clearanceProgress = _clearanceProgress.WithRowsLeftOut(leftOutRows, Navigation.PlacedStop.Entered(" +
                                      "userRunwayHoldShorts, userTaxiwayHoldShorts, userHoldingPointHoldShorts));", StringComparison.Ordinal);
        int rekeyed = load.IndexOf("ClearanceRows.WithoutKeepingStops(userRunwayHoldShorts, leftOutRows)", StringComparison.Ordinal);
        Assert.True(remembered >= 0 && rekeyed > remembered, "the stops are remembered as entered before they lose their rows");
        Assert.Contains("ClearanceRows.WithoutKeepingStops(userTaxiwayHoldShorts, leftOutRows)", load);
        Assert.Contains("ClearanceRows.WithoutKeepingStops(userHoldingPointHoldShorts, leftOutRows)", load);
        Assert.DoesNotContain("ClearanceRows.Without(userRunwayHoldShorts, leftOutRows)", load);
        Assert.DoesNotContain("_clearanceProgress = _clearanceProgress.WithRowsLeftOut(leftOutRows);", load);

        // A passed stop is reported as the step it came from, numbered as entered (ClearanceRowsTests).
        string progress = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Navigation", "ClearanceProgress.cs"));
        Assert.Contains("=> new(numbering.AsEntered(this), new ClearanceStep(", progress);
    }

    // Used, put back, cleared again or passed: kept where guidance uses a crossing and where the Hold short key puts one
    // back, and settled before a recalculated route replaces the one they were judged on.
    [Fact]
    public void Crossings_used_and_put_back_are_recorded_where_they_happen()
    {
        string manager = ManagerSource();

        Assert.Contains("_clearanceProgress = _clearanceProgress.WithUsed(", Member(manager, "private void UseClearedCrossing("));
        Assert.Contains("_clearanceProgress = _clearanceProgress.WithUsed(", Member(manager, "private void UseNoStopCrossingsPassed("));
        Assert.Contains("_clearanceProgress = _clearanceProgress.WithPutBack(restoredCrossings, stopNodeId);",
            Member(manager, "private void PutClearedStopBack("));

        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");
        int settle = recalc.IndexOf("_clearanceProgress = _clearanceProgress.Settled(", StringComparison.Ordinal);
        int adopt = recalc.IndexOf("AdoptRoute(newRoute,", StringComparison.Ordinal);
        int placed = recalc.IndexOf("_clearanceProgress = _clearanceProgress.WithPlaced(recalcPlacedStops)", StringComparison.Ordinal);
        Assert.True(settle >= 0 && adopt > settle, "settled before the recalculated route is adopted");
        Assert.True(placed > adopt, "the recalculated route's stops recorded once it is adopted");
    }

    // A recalculation keeps a stop with no row of its own (-1) as -1, with no cleared taxiway left to follow too (every
    // row behind), but only while the stop it was placed at on the route being replaced is not passed
    // (ClearanceProgressState.CarriedOver, tested in ClearanceProgressTests); one on a row behind the aircraft goes.
    [Fact]
    public void A_recalculation_keeps_a_stop_with_no_row_only_while_it_is_not_passed()
    {
        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");

        Assert.Contains("int rowsBehind = sequenceRemains ? seqOffset : int.MaxValue;", recalc);
        Assert.Contains("var remappedRwy = ClearanceRows.After(_clearanceProgress.CarriedOver(_userRunwayHoldShorts, loadedRows,", recalc);
        Assert.Contains("var remappedTwy = ClearanceRows.After(_clearanceProgress.CarriedOver(_userTaxiwayHoldShorts, loadedRows,", recalc);
        Assert.Contains("_route?.Segments, _currentSegmentIndex, holdingNow), rowsBehind);", recalc);
    }

    // Fix round 1: the Via steps done are capped by the live route's own cursor (a row is done only once the next row's
    // run starts at or before it), from the row the live route was built from; the position decides alone only when
    // the route has no sequence of its own to go by.
    [Fact]
    public void Via_steps_done_are_capped_by_the_route_cursor()
    {
        string progress = Member(ManagerSource(), "public Navigation.ClearanceProgress GetClearanceProgress(");

        Assert.Contains("Navigation.ClearanceTrim.ViaStepsDoneOnRoute(firstRow, sequence.Count,", progress);
        Assert.Contains("FindTaxiwayRunStart(route, suffix, row - firstRow, sequence[row])", progress);
        Assert.Contains("_currentSegmentIndex)", progress);
        // Final review, Important 2: a route loaded from the whole sequence counts by its cursor alone ("A, C, A").
        Assert.Contains("Navigation.ClearanceTrim.ViaStepsDone(sequence, remaining, routeDone, routeFromWholeSequence: firstRow == 0)", progress);

        string routing = RoutingSource();
        Assert.Contains("_clearanceProgress = _clearanceProgress.WithPlaced(placedStops) with { RouteFirstRow = taxiwaySequence is { Count: > 0 } ? 0 : -1 };",
            Member(routing, "public string? LoadRoute("));
        Assert.Contains("_clearanceProgress = _clearanceProgress.WithPlaced(recalcPlacedStops) with { RouteFirstRow = sequenceRemains ? seqOffset : -1 };",
            Member(routing, "private void TryRecalculateRoute("));
    }

    // Fix round 1: the turns made on the route a recalculation replaces (Made at its cursor, or made by a corner cut)
    // are remembered before the cursor restarts, so the window never reads a made turn as one still to make [TURN-3].
    [Fact]
    public void Turns_made_are_remembered_across_a_recalculation()
    {
        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");

        // Read at the cursor as the aircraft stands: one back while holding short (HandleHoldShort has moved it past the
        // stop), so a turn just beyond a stop is not made while the aircraft holds short of it.
        Assert.Contains("turnsMade: TurnPickCarryOver.MadeRows(_route,", recalc);
        Assert.Contains("Navigation.ClearanceProgressState.CursorAsStanding(_currentSegmentIndex, holdingNow), cutPicks));", recalc);
        int settle = recalc.IndexOf("_clearanceProgress = _clearanceProgress.Settled(", StringComparison.Ordinal);
        int reset = recalc.IndexOf("_currentSegmentIndex = 0;", StringComparison.Ordinal);
        Assert.True(settle >= 0 && reset > settle, "the turns are read before the cursor restarts");
    }

    // Fix round 2: the live "turn made" reads the aircraft one segment back while holding short, as StopPassed does.
    [Fact]
    public void A_turn_is_judged_where_the_aircraft_stands_while_holding()
    {
        string progress = Member(ManagerSource(), "public Navigation.ClearanceProgress GetClearanceProgress(");

        Assert.Contains("bool holding = _state == TaxiGuidanceState.HoldShort;", progress);
        Assert.Contains("Navigation.ClearanceProgressState.IsOnTaxiwayPastStart(route.Segments, _currentSegmentIndex, holding, sequence[done])", progress);
        Assert.DoesNotContain("route.Segments[_currentSegmentIndex].TaxiwayName", progress);
    }

    // Ruling 3: a leg ending "hold short of taxiway" after a Cross taxiway step keeps the meeting beyond the crossing,
    // in the manager's meet scan and in a Turn pick's end re-resolution alike.
    [Fact]
    public void The_crossed_taxiways_reach_the_leg_end_scan_and_the_Turn_pick_end()
    {
        string routing = RoutingSource();
        string load = Member(routing, "public string? LoadRoute(");

        Assert.Contains("IReadOnlyList<string>? legEndCrossedTaxiways = null", load);
        Assert.Contains("ApplyLegEndSetback(route, taxiwaySequence, allowInsert: true, legEndCrossedTaxiways)", load);
        Assert.Contains("LegEndCrossedTaxiways: legEndCrossedTaxiways", load);
        Assert.Contains("ClearanceRouteChecks.LegEndMeeting(", Member(routing, "private string? ApplyLegEndSetback("));

        string applier = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Navigation", "TurnPickApplier.cs"));
        Assert.Contains("ResolveProgressiveEnd(ctx.Graph, v, ctx.Sequence[row], stepBlocked, ctx.Terminator, ctx.LegEndCrossedTaxiways)", applier);
    }

    // Owner, 2026-11-25 ("Warn at Calculate"): a leg end that skipped the last taxiway's first meeting with B because it
    // lies inside a runway's clear margin (the form's lookups, a Turn pick's end and the meet scan all skip it) says so
    // once, at Calculate: the route as cut is judged in ApplyLegEndSetback (ClearanceRouteChecksTests), and the sentence
    // rides in the summary box and the warnings the form speaks first ([HLD-17]). A local, never stored: a refused load
    // has nothing to put back [RTE-29]. A recalculation says nothing (it discards the result).
    [Fact]
    public void A_leg_end_that_skipped_a_meeting_beside_a_runway_says_so_at_Calculate()
    {
        string routing = RoutingSource();
        string load = Member(routing, "public string? LoadRoute(");
        string setback = Member(routing, "private string? ApplyLegEndSetback(");

        Assert.Contains("ClearanceRouteChecks.RunwayBesideFirstMeeting(route.Segments, runStart, target, NodeTouchesTaxiway,", setback);
        Assert.Contains("RouteRunwayCrossings.RunwayNameWithinClearMargin(graph.RunwayCenterlines, node.Latitude, node.Longitude)", setback);
        Assert.Contains("ClearanceRouteChecks.MetFirstBesideARunway(target, taxiwaySequence[^1], besideRunway)", setback);
        // Judged on the route as cut to its meeting, before the stop is set back from it.
        int cut = setback.IndexOf("route.Segments.RemoveRange(meet + 1,", StringComparison.Ordinal);
        int judged = setback.IndexOf("ClearanceRouteChecks.RunwayBesideFirstMeeting(", StringComparison.Ordinal);
        int placed = setback.IndexOf("PlaceTaxiwayHoldStop(", StringComparison.Ordinal);
        Assert.True(cut >= 0 && judged > cut && placed > judged, "judged after the cut and before the stop is placed");
        Assert.Contains("if (allowInsert && taxiwaySequence is { Count: > 0 })", setback);

        Assert.Contains("string? legEndBesideRunwayNote = ApplyLegEndSetback(route, taxiwaySequence, allowInsert: true, legEndCrossedTaxiways);", load);
        Assert.Contains("summary = legEndBesideRunwayNote + \" \" + summary;", load);
        Assert.Contains("mustHear.Add(legEndBesideRunwayNote);", load);
        Assert.DoesNotContain("legEndBesideRunwayNote", FailSnapshot(routing));
        Assert.Matches(@"if \(!keepsLoadedTail && _legEndAtTurnStart == null\)\s+ApplyLegEndSetback\(newRoute, _originalTaxiwaySequence, allowInsert: false\);", routing);
    }

    // Task 6: Calculate builds every route from ONE plan, refused for every destination type after the position is
    // refreshed [RTE-21] and before anything is looked up [RTE-29]; the row readers are gone.
    [Fact]
    public void Calculate_builds_every_route_from_the_plan()
    {
        string form = FormSource();
        string calculate = Member(form, "private void OnCalculateClicked(");

        int refresh = calculate.IndexOf("_simConnectManager?.LastKnownPosition is { } pos", StringComparison.Ordinal);
        int build = calculate.IndexOf("ClearancePlanner.Build(_steps, hasDestination, PlanDestinationRunway(hasDestination))", StringComparison.Ordinal);
        int refusal = calculate.IndexOf("RefuseCalculate(plan.Refusal);", StringComparison.Ordinal);
        int noDestination = calculate.IndexOf("CalculateLegWithNoDestination(plan);", StringComparison.Ordinal);
        int destination = calculate.IndexOf("\"Please select a destination.\"", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && build > refresh, "the plan is built after the position is refreshed");
        Assert.True(refusal > build && noDestination > refusal && destination > noDestination,
            "the plan's refusal is checked for every destination type before either path");

        foreach (string reader in new[] { "GetSelectedTaxiwayNames", "GetUserHoldShortIndices", "GetUserRunwayHoldShorts",
                     "GetUserTaxiwayHoldShorts", "GetUserHoldingPointHoldShorts", "GetUserTurnPicks", "CurrentPlan()" })
            Assert.DoesNotContain(reader, form);
        // Both LoadRoute calls take the plan's sequence, stops and picks, the wingspan set just before each.
        Assert.Equal(2, Regex.Matches(form, @"plan\.TaxiwaySequence\.Count > 0 \? plan\.TaxiwaySequence\.ToList\(\) : null").Count);
        Assert.Equal(2, Regex.Matches(form, @"userHoldingPointHoldShorts: PlanStops\(plan, ClearanceTargetKind\.HoldingPoint,").Count);
        Assert.Equal(2, Regex.Matches(form, @"_guidanceManager\.AircraftWingspanMetres = _aircraftWingspan \* 0\.3048;\r?\n\s+string\? error = _guidanceManager\.LoadRoute\(").Count);
    }

    // Rulings 1 to 3: a leg with no destination ends where the plan says. The Cross taxiway tie-breaker reaches the
    // form's junction choice and LoadRoute's own scan; a leg ending across a runway at a holding point is pinned
    // through it from the plan; the end lookups are the direction-aware ones (ClearanceLegEndTests); both stage-1
    // refusal sentences and "Could not find … Check your entry." stay, and every refusal keeps a pending import summary.
    [Fact]
    public void A_leg_with_no_destination_ends_where_the_plan_says()
    {
        string leg = Member(FormSource(), "private void CalculateLegWithNoDestination(");

        Assert.Contains("ClearanceRouteChecks.BeyondTheCrossing(candidates, Distances(), crossingNodes)", leg);
        Assert.Contains("legEndCrossedTaxiways: plan.LegEndCrossedTaxiways.Count > 0 ? plan.LegEndCrossedTaxiways : null", leg);
        Assert.Contains("holdingPointHoldNodeId: plan.CrossAtHoldingPointNodeId,", leg);
        Assert.Contains("holdingPointName: plan.CrossAtHoldingPointName,", leg);
        Assert.Contains("ClearanceLegEnd.NearestAhead(", leg);
        Assert.Contains("ClearanceLegEnd.EndOfTaxiway(", leg);
        Assert.Contains("ClearanceLegEnd.NodesOnTaxiway(", leg);
        Assert.Contains("graph.FindNearestNodeInDirection(", leg);
        Assert.DoesNotContain("FindTaxiwayEndNodeClearOfRunways(", leg);
        // Fix round 1: a limit said alone ends at the first near-side hold line reached; the end of the last taxiway is
        // entered ahead of the aircraft (ClearanceLegEndTests, HoldShortNodeResolverTests).
        Assert.Contains("HoldShortNodeResolver.NearSideHoldCandidates(", leg);
        Assert.Contains("ClearanceLegEnd.EntryOntoTaxiway(", leg);
        // A crossing already made ends just across THAT crossing, or is refused with the reason; one whose pin has no
        // side (made with no stop, pinned on the pavement) is refused, never sent back over the runway.
        Assert.Contains("ClearanceLegEnd.JudgeMadeCrossing(", leg);
        Assert.Contains("RunwayCrossingResolver.FindAcross(graph, crossRwy, stop.NodeId)", leg);
        Assert.Contains("ClearanceLegEnd.AlreadyAcross(runwayTarget)", leg);
        // Final review, minor: a pin with no side has a sentence of its own, never "already across" or "check your entry".
        Assert.Contains("RefuseCalculate(ClearanceLegEnd.NoSideToGoBy(runwayTarget));", leg);
        Assert.Contains("ProgressiveLegEnd.NoHoldShortClearOfARunway(taxiwayTarget, lastVia)", leg);
        Assert.Contains("ProgressiveLegEnd.NoEndClearOfARunway(lastVia)", leg);
        Assert.Contains("$\"Could not find {what} from {from}. Check your entry.\"", leg);
        Assert.DoesNotContain("_announcer.", leg);
        Assert.Contains("SpeakCalculateUtterance(plan);", leg);
    }

    // [HLD-17], [SI-22]: everything said at Calculate is ONE utterance, warnings first: the clearance's checks straight
    // after the route's own warnings, the start-hold sentence last. Both paths speak through it.
    [Fact]
    public void Calculate_speaks_once_with_the_clearance_checks_after_the_route_warnings()
    {
        string form = FormSource();
        string speak = Member(form, "private void SpeakCalculateUtterance(");

        int imported = speak.IndexOf("_importSummary?.Invoke(true)", StringComparison.Ordinal);
        int warnings = speak.IndexOf("parts.Add(_guidanceManager.LastRouteStartWarning);", StringComparison.Ordinal);
        int checks = speak.IndexOf("parts.AddRange(_guidanceManager.CheckClearance(plan));", StringComparison.Ordinal);
        // Final review, Important 3: each crossing a Cross step cleared, after the warnings: the summary naming it is cut.
        int cleared = speak.IndexOf("string clearedCrossings = _guidanceManager.DescribeClearedCrossings();", StringComparison.Ordinal);
        int clearedAdded = speak.IndexOf("if (clearedCrossings.Length > 0) parts.Add(clearedCrossings);", StringComparison.Ordinal);
        int unmapped = speak.IndexOf("ConsumeUnmappedStartWarning()", StringComparison.Ordinal);
        int turnCue = speak.IndexOf("ConsumeInitialTurnCue()", StringComparison.Ordinal);
        int startHold = speak.IndexOf("ConsumeStartHoldCue()", StringComparison.Ordinal);
        Assert.True(imported >= 0 && warnings > imported && checks > warnings && cleared > checks && clearedAdded > cleared
                    && unmapped > clearedAdded && turnCue > unmapped && startHold > turnCue, "the parts are in order");
        Assert.Single(Regex.Matches(speak, @"_announcer\.AnnounceImmediate\("));
        Assert.Single(Regex.Matches(Member(form, "private void OnCalculateClicked("), @"SpeakCalculateUtterance\("));
        Assert.DoesNotContain("progParts", form);
        Assert.DoesNotContain("standstillParts", form);
    }

    // Task 7: the window empties its step list when a leg is over and, while a route is live, shows only what is still
    // ahead (trimmed from the list as last calculated, never an already-trimmed one), and the list follows the Hold
    // short key. Spec 8.2 and 8.8; Review Focus 2, 7 and 8.
    [Fact]
    public void The_window_empties_and_trims_its_step_list()
    {
        string form = FormSource();

        Assert.Contains("StateChanged +=", form);
        Assert.Contains("ClearedCrossingsChanged +=", form);
        Assert.Contains("ClearanceTrim.Remaining(", form);
        Assert.Contains("TurnOntoFirstRemainingMade", form);
        Assert.Contains("FocusForOpening", form);
        Assert.Contains("plan.TurnPicks", form);
        Assert.DoesNotContain("GetUserTurnPicks", form);
        Assert.DoesNotContain("cmbFirstTurn", form);
        Assert.Contains("ClearSteps()", Member(form, "private void ResetRouteShapingControls("));
        // Final review, minor: where focus lands is set BEFORE Show(), so the window's activation lands there and the
        // screen reader names one control once; after Show() it is focused only when activation has not.
        string dialogs = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "MainForm.Dialogs.cs"));
        string open = Member(dialogs, "private void OpenTaxiForm(");
        int prepare = open.IndexOf("taxiAssistForm.PrepareForOpening();", StringComparison.Ordinal);
        int show = open.IndexOf("taxiAssistForm.Show();", StringComparison.Ordinal);
        int focus = open.IndexOf("taxiAssistForm.FocusForOpening();", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && show > prepare && focus > show, "OpenTaxiForm picks the focus before Show() and makes sure after it");
        string prepareBody = Member(form, "public void PrepareForOpening(");
        Assert.Contains("ActiveControl = control;", prepareBody);
        Assert.Contains("int opening = Visible ? _timesShown : _timesShown + 1;", prepareBody);
        Assert.Contains("if (control == null || control.Focused) return;", Member(form, "public void FocusForOpening("));

        // Trimmed from the list as last calculated, with GetClearanceProgress's lists passed straight in.
        string trim = Member(form, "private bool TrimStepsToWhatIsAhead(");
        Assert.Contains("ClearanceTrim.Remaining(_calculatedSteps, progress.ViaStepsDone, _calculatedWithDestination,", trim);
        Assert.Contains("progress.ClearedInForce, progress.ClearedUsed, progress.PutBack, progress.HoldsPassed,", trim);
        Assert.Contains("progress.TurnOntoFirstRemainingMade, out var origins)", trim);
        Assert.Contains("ClearanceTrim.KeepingEdits(", trim);
        Assert.Contains("_guidanceManager.HasLiveRoute", trim);
        Assert.Contains("TrimStepsToWhatIsAhead()", prepareBody);
        // Final review, minor: both successful Calculates record the list they use just BEFORE guidance starts, so a leg
        // that ends as guidance starts carries the new generation and is never ignored by EmptyStepsAfterLeg's guard.
        Assert.Equal(2, Regex.Matches(form, @"RecordCalculatedSteps\(hasDestination: (true|false)\);\r?\n\s+_guidanceManager\.StartGuidance\(").Count);
        Assert.DoesNotMatch(@"StartGuidance\([^;]*\);\r?\n\s+RecordCalculatedSteps\(", form);
    }

    // Fix round 1, ruling 1: Calculate trims the finished steps first. The window can stay open, or come back by Alt+Tab,
    // while the aircraft crosses 04L; built from the untrimmed list, "Cross runway 04L" cleared 04L again for the new
    // route (the KBOS route back over the runway) and "Left onto C" was judged again. The trim keeps a crossing still
    // ahead (spec 8.3), runs only with a route live and a list calculated (TrimStepsToWhatIsAhead's own guard), and says
    // nothing at Calculate: the one Calculate utterance stays the only thing said ([HLD-17], [SI-22]).
    [Fact]
    public void Calculate_trims_the_finished_steps_before_it_builds_the_plan()
    {
        string form = FormSource();
        string calculate = Member(form, "private void OnCalculateClicked(");

        int cancel = calculate.IndexOf("CancelPendingStepSpeech();", StringComparison.Ordinal);
        int trim = calculate.IndexOf("TrimStepsToWhatIsAhead();", StringComparison.Ordinal);
        int build = calculate.IndexOf("ClearancePlanner.Build(_steps, hasDestination, PlanDestinationRunway(hasDestination))", StringComparison.Ordinal);
        Assert.True(cancel >= 0 && trim > cancel && build > trim, "the list is trimmed before the plan is built from it");

        string trimBody = Member(form, "private bool TrimStepsToWhatIsAhead(");
        Assert.Contains("if (_calculatedSteps == null || !_guidanceManager.HasLiveRoute) return false;", trimBody);
        Assert.DoesNotContain("_announcer.", trimBody);
        Assert.DoesNotContain("SpeakStep", trimBody);
        // Only the window opening says a trim emptied the list.
        Assert.Contains("if (TrimStepsToWhatIsAhead()) SpeakStepAdded(ClearanceStepEntry.StepsCleared, afterFocusMove: true);",
            Member(form, "public void PrepareForOpening("));
    }

    // [A380C-6]: both guidance events arrive on the manager's thread under _stateLock; the window marshals with
    // BeginInvoke inside try/catch(InvalidOperationException), never Invoke, and reads the progress only after
    // marshalling. Subscribed once in the constructor, unsubscribed on dispose.
    [Fact]
    public void The_window_marshals_guidance_events_and_unsubscribes()
    {
        string form = FormSource();
        string safe = Member(form, "private void SafeBeginInvoke(");
        Assert.Contains("BeginInvoke(action)", safe);
        Assert.Contains("catch (InvalidOperationException)", safe);

        foreach (string handler in new[] { "private void OnGuidanceStateChanged(", "private void OnClearedCrossingsChanged(" })
        {
            string body = Member(form, handler);
            Assert.Contains("SafeBeginInvoke(", body);
            Assert.DoesNotContain(".Invoke(", body);
            Assert.DoesNotContain("GetClearanceProgress", body);
            // The generation is taken when the action is queued.
            Assert.Contains("Volatile.Read(ref _calculateGeneration)", body);
        }
        Assert.Contains("_guidanceManager.GetClearanceProgress()", Member(form, "private void FollowPutBackCrossings("));
        Assert.Contains("ClearanceTrim.PutBackSteps(", Member(form, "private void FollowPutBackCrossings("));

        string ctor = Member(form, "public TaxiAssistForm(");
        Assert.Contains("_guidanceManager.StateChanged += OnGuidanceStateChanged;", ctor);
        Assert.Contains("_guidanceManager.ClearedCrossingsChanged += OnClearedCrossingsChanged;", ctor);
        string dispose = Member(form, "protected override void Dispose(");
        Assert.Contains("_guidanceManager.StateChanged -= OnGuidanceStateChanged;", dispose);
        Assert.Contains("_guidanceManager.ClearedCrossingsChanged -= OnClearedCrossingsChanged;", dispose);
    }

    // Spec 8.2: emptied when guidance is stopped and when another airport is loaded, never by a same-airport reload (the
    // GSX gate list arriving); a removal or a trim that leaves the list empty is said; an append drops a crossing
    // already made (ruling 4); an emptying from the fill or an airport change drops a step sentence still waiting.
    [Fact]
    public void The_list_is_emptied_where_a_leg_ends_and_kept_on_a_same_airport_reload()
    {
        string form = FormSource();

        Assert.Contains("ClearSteps();", Member(form, "private void OnStopClicked("));
        string load = Member(form, "private async Task LoadAirportDataCoreAsync(");
        Assert.Contains("bool sameAirport = icao.Equals(_currentIcao, StringComparison.OrdinalIgnoreCase);", load);
        Assert.Contains("if (sameAirport) RefreshStepNameList();", load);
        Assert.Contains("else ClearSteps();", load);
        Assert.Contains("CancelPendingStepSpeech();", Member(form, "private void ClearSteps("));
        Assert.Contains("_calculatedSteps = null;", Member(form, "private void ClearSteps("));
        Assert.Contains("ClearanceTrim.CompletedCrossings(", Member(form, "private void DropCompletedCrossings("));
        Assert.Contains("DropCompletedCrossings();", Member(form, "private void AppendStep(ClearanceStep step, bool speak, bool speakAfterFocusMove)"));
        Assert.Contains("ClearanceStepEntry.RemovalSentence(", Member(form, "private void RemoveStep("));
        Assert.Contains("ClearanceStepEntry.StepsCleared", Member(form, "private void OnClearStepsClicked("));
        string empty = Member(form, "private void EmptyStepsAfterLeg(");
        Assert.Contains("LegEndEmptiesList(generation, _calculateGeneration, _calculatedSteps != null)", empty);
        // Final review, minor: steps typed since the last Calculate go too; in the focused window that is said once,
        // queued behind guidance's own leg-end sentence, never over it.
        int sentence = empty.IndexOf("ClearanceStepEntry.LegEndSentence(_stepOrigins, windowFocused: Visible && ContainsFocus)", StringComparison.Ordinal);
        int cleared = empty.IndexOf("ClearSteps();", StringComparison.Ordinal);
        int said = empty.IndexOf("if (sentence != null) _announcer.Announce(sentence);", StringComparison.Ordinal);
        Assert.True(sentence >= 0 && cleared > sentence && said > cleared, "judged before the list is emptied, said after");
        Assert.DoesNotContain("AnnounceImmediate", empty);
    }

    // Spec 8.2: where focus lands when the window opens.
    [Fact]
    public void Focus_on_opening_follows_whether_a_route_is_live()
    {
        Assert.Equal(Forms.TaxiAssistForm.OpeningFocusTarget.Airport, Forms.TaxiAssistForm.OpeningFocus(firstShow: true, routeLive: true));
        Assert.Equal(Forms.TaxiAssistForm.OpeningFocusTarget.Airport, Forms.TaxiAssistForm.OpeningFocus(firstShow: true, routeLive: false));
        Assert.Equal(Forms.TaxiAssistForm.OpeningFocusTarget.StepKind, Forms.TaxiAssistForm.OpeningFocus(firstShow: false, routeLive: true));
        Assert.Equal(Forms.TaxiAssistForm.OpeningFocusTarget.DestinationType, Forms.TaxiAssistForm.OpeningFocus(firstShow: false, routeLive: false));
    }

    // Spec 8.2: the list is emptied when the aircraft arrives, when guidance stops and when a leg with no destination
    // ends; never mid-leg. A landing ends it too (fix round 1, ruling 2): guidance stops at departure only through
    // take-off assist, so without it a departure list survived the flight and a departure "Cross runway 22R" cleared
    // 22R on the taxi-in at the same airport. A queued emptying is skipped when a Calculate has built a list since (its
    // generation), and when the list holds no leg calculated from it (steps entered after the last leg ended, or before
    // any Calculate: a taxi-in clearance typed during the landing rollout comes after the event and is for the leg to come).
    [Fact]
    public void A_leg_end_empties_only_the_list_it_was_calculated_from()
    {
        foreach (var state in new[] { Services.TaxiGuidanceState.Arrived, Services.TaxiGuidanceState.Inactive,
                     Services.TaxiGuidanceState.ProgressiveHold, Services.TaxiGuidanceState.LandingRollout })
            Assert.True(Forms.TaxiAssistForm.EndsTheLeg(state));
        foreach (var state in new[] { Services.TaxiGuidanceState.RouteLoaded, Services.TaxiGuidanceState.Taxiing,
                     Services.TaxiGuidanceState.HoldShort, Services.TaxiGuidanceState.LiningUp })
            Assert.False(Forms.TaxiAssistForm.EndsTheLeg(state));

        Assert.True(Forms.TaxiAssistForm.LegEndEmptiesList(3, 3, listHoldsCalculatedLeg: true));
        Assert.False(Forms.TaxiAssistForm.LegEndEmptiesList(3, 4, listHoldsCalculatedLeg: true));
        Assert.False(Forms.TaxiAssistForm.LegEndEmptiesList(3, 3, listHoldsCalculatedLeg: false));
    }

    // Stage 4, D1 and D2: Enter on a selected step in the Clearance list loads it into the step boxes, kind, then name,
    // then At, saying nothing: the name box's accessible name carries ", changing step N", which the screen reader says
    // as focus lands there ([CORE-7]). Delete still removes the step.
    [Fact]
    public void Enter_on_a_step_in_the_Clearance_list_starts_a_change()
    {
        string form = FormSource();
        Assert.Contains("lstClearance.KeyDown += OnClearanceListKeyDown;", form);
        string keys = Member(form, "private void OnClearanceListKeyDown(");
        Assert.Contains("RemoveStep();", keys);
        int enter = keys.IndexOf("e.KeyCode == Keys.Enter && lstClearance.SelectedIndex >= 0", StringComparison.Ordinal);
        int begin = keys.IndexOf("BeginStepChange(lstClearance.SelectedIndex);", StringComparison.Ordinal);
        Assert.True(enter >= 0 && begin > enter, "Enter with a step selected starts the change");

        string start = Member(form, "private void BeginStepChange(");
        int index = start.IndexOf("_changingStepIndex = index;", StringComparison.Ordinal);
        int kind = start.IndexOf("SetStepKindSilently(step.Kind);", StringComparison.Ordinal);
        int names = start.IndexOf("RefreshStepNameList();", StringComparison.Ordinal);
        int name = start.IndexOf("cmbStepName.SelectedIndex = IndexOfStepNameFor(step);", StringComparison.Ordinal);
        int at = start.IndexOf("cmbStepAt.SelectedIndex = IndexOfStepAtFor(step);", StringComparison.Ordinal);
        int focus = start.IndexOf("cmbStepName.Focus();", StringComparison.Ordinal);
        Assert.True(index >= 0 && kind > index && names > kind && name > names && at > name && focus > at,
            "the boxes are loaded kind, name, At, with the change open first, then focus moves to the name box");
        Assert.Contains("btnAddStep.Text = ClearanceStepEntry.ReplaceButtonText(index + 1);", start);
        Assert.Contains("btnAddStep.AccessibleName = ClearanceStepEntry.ReplaceButtonName(index + 1);", start);
        Assert.DoesNotContain("_announcer.", start);
        Assert.DoesNotContain("SpeakStep", start);

        // The name box says which step it is changing, whatever kind is picked while the change is open.
        Assert.Contains("ClearanceStepEntry.NameBoxAccessibleName(kind, _changingStepIndex + 1)",
            Member(form, "private void RefreshStepNameList("));
        // The name item and the At item are found by the pure matchers (ClearanceStepEntryTests): a holding point by its
        // node only, an At item only when picking it rebuilds the same place, or the step's own place (fix round 1).
        string nameFor = Member(form, "private int IndexOfStepNameFor(");
        Assert.Contains("IndexOfStepName(i => ClearanceStepEntry.NameItemMatches(i, step))", nameFor);
        Assert.DoesNotContain("string.Equals(i.Name", nameFor);
        string atFor = Member(form, "private int IndexOfStepAtFor(");
        Assert.Contains("if (label == _changingOwnAtLabel) return i;", atFor);
        Assert.Contains("ClearanceStepEntry.AtItemMatches(label, point, step)", atFor);
    }

    // Task 2 fix round 1 (Review Focus 3): a crossing made at a holding point now behind the aircraft is never offered its
    // place again, and the boxes rebuilt it with none: the pin went and the next Calculate cleared the crossing back. While
    // a runway Cross step is being changed and its own place is not offered, the At box lists that place (for the step's
    // runway only), the change loads with it selected, and picking it rebuilds the step with its old place, so Replace
    // keeps the old record. Replace and Calculate's refusal compare through ONE helper.
    [Fact]
    public void The_At_box_lists_the_own_place_of_the_step_being_changed()
    {
        string form = FormSource();
        string at = Member(form, "private void RefreshStepAtList(");
        int cleared = at.IndexOf("_changingOwnAtLabel = null;", StringComparison.Ordinal);
        int points = at.IndexOf("_crossAtHoldPoints.TryAdd(label, hp)", StringComparison.Ordinal);
        int own = at.IndexOf("ClearanceStepEntry.OwnPlaceLabel(changing, pickedRunway, placeOffered: IndexOfStepAtFor(changing) >= 0,", StringComparison.Ordinal);
        int added = at.IndexOf("_changingOwnAtLabel = ownLabel;", StringComparison.Ordinal);
        int kept = at.IndexOf("cmbStepAt.SelectedIndex = ClearanceStepEntry.AtIndexAfterRefill(", StringComparison.Ordinal);
        Assert.True(cleared >= 0 && points > cleared && own > points && added > own && kept > added,
            "the own place is judged after the offered places are listed, and kept by a refill");
        Assert.Contains("ClearanceStepEntry.PlaceIsHoldingPoint(changing,", at);
        Assert.Contains("_changingOwnAtLabel = null;", Member(form, "private void EndStepChange("));

        Assert.Contains("ClearanceStepEntry.BuildForChange(SelectedStepKind(), name, atLabel, atPoint, _steps[_changingStepIndex], _changingOwnAtLabel)",
            Member(form, "private ClearanceStep StepFromBoxes("));

        // One rebuild-and-compare for Replace and for Calculate's refusal.
        Assert.Single(Regex.Matches(form, @"ClearanceStepEntry\.SameAsEntered\("));
        Assert.Contains("ClearanceStepEntry.SameAsEntered(_steps[_changingStepIndex], rebuilt)",
            Member(form, "private (ClearanceStep? Rebuilt, bool Same) RebuildStepBeingChanged("));
    }

    // Final review, Important 1: every refill of the At box picks its item through ONE pure rule
    // (ClearanceStepEntry.AtIndexAfterRefill, ClearanceStepEntryTests), so a refill with no pick kept (the name box arrowed
    // away from the runway and back, a same-airport reload's re-pick) or the own place relabelled goes back to the place
    // of the crossing being changed, never to "(none)" with Enter then dropping the place and a made crossing's pin. The
    // own label is read before it is cleared, and the step's place is looked up after its own label is listed.
    [Fact]
    public void A_refilled_At_box_goes_back_to_the_place_of_the_step_being_changed()
    {
        string form = FormSource();
        Assert.Contains("cmbStepName.SelectedIndexChanged += (s, e) => { if (!_fillingStepName) RefreshStepAtList(); };", form);

        string at = Member(form, "private void RefreshStepAtList(");
        int captured = at.IndexOf("string? previousOwnLabel = _changingOwnAtLabel;", StringComparison.Ordinal);
        int cleared = at.IndexOf("_changingOwnAtLabel = null;", StringComparison.Ordinal);
        int added = at.IndexOf("_changingOwnAtLabel = ownLabel;", StringComparison.Ordinal);
        int ownPlace = at.IndexOf("ownPlace = IndexOfStepAtFor(changing);", StringComparison.Ordinal);
        int select = at.IndexOf("cmbStepAt.SelectedIndex = ClearanceStepEntry.AtIndexAfterRefill(", StringComparison.Ordinal);
        Assert.True(captured >= 0 && cleared > captured && added > cleared && ownPlace > added && select > ownPlace,
            "the old own label is read before it is cleared, the step's place found after its own label is listed");
        Assert.Contains("previous, previousOwnLabel, changing, pickedRunway, ownPlace);", at);
        Assert.Single(Regex.Matches(at, @"cmbStepAt\.SelectedIndex ="));
        Assert.DoesNotContain("kept >= 0 ? kept : 0", at);
    }

    // Stage 4, D2: while a step is being changed the name box lists from the steps BEFORE it (its step above, its last
    // Via), never from the whole list, and the first-step preselect never picks a name for it.
    [Fact]
    public void The_name_box_lists_from_the_steps_before_the_one_being_changed()
    {
        string form = FormSource();
        string items = Member(form, "private List<ClearanceNameLists.Item> StepNameItems(");
        Assert.Contains("var before = StepsBeforeEntry();", items);
        Assert.DoesNotContain(", _steps)", items);
        Assert.Equal(3, Regex.Matches(items, @", before\)").Count);
        string lastVia = Member(form, "private string? LastViaStepName(");
        Assert.Contains("StepsBeforeEntry()", lastVia);
        Assert.DoesNotContain("_steps", lastVia);

        string refresh = Member(form, "private void RefreshStepNameList(");
        Assert.Contains("_changingStepIndex < 0 && StepsBeforeEntry().Count == 0", refresh);
        Assert.DoesNotContain("_steps.Count == 0", refresh);
        Assert.Contains("_changingStepIndex", Member(form, "private IReadOnlyList<ClearanceStep> StepsBeforeEntry("));
    }

    // Stage 4, D3 and D4: Add Step (its button, Alt+D, Enter in a step box) replaces the step being changed and reads it
    // back with its own number. A replacement that says what the step said keeps the old record and its origin (a made
    // crossing's pin, a put-back hold short's place); anything else is a new step, its old origin kept out of every trim.
    // The button's own text is restored from a field, never a second "A&dd Step" literal (ClearanceStepEntryTests).
    [Fact]
    public void Add_Step_replaces_the_step_being_changed()
    {
        string form = FormSource();
        string add = Member(form, "private void AddStepFromInputs(");
        int open = add.IndexOf("if (_changingStepIndex >= 0)", StringComparison.Ordinal);
        int replace = add.IndexOf("ReplaceStepFromInputs();", StringComparison.Ordinal);
        int append = add.IndexOf("AppendStep(", StringComparison.Ordinal);
        Assert.True(open >= 0 && replace > open && append > replace, "an open change is replaced, never appended");

        string body = Member(form, "private void ReplaceStepFromInputs(");
        int refusal = body.IndexOf("ClearanceStepEntry.Refusal(cmbStepName.SelectedItem as ClearanceNameLists.Item)", StringComparison.Ordinal);
        int same = body.IndexOf("var (rebuilt, same) = RebuildStepBeingChanged();", StringComparison.Ordinal);
        Assert.Contains("if (!same && rebuilt != null)", body);
        int removed = body.IndexOf("if (_stepOrigins[index] >= 0) _removedOrigins.Add(_stepOrigins[index]);", StringComparison.Ordinal);
        int written = body.IndexOf("_steps[index] = rebuilt;", StringComparison.Ordinal);
        int origin = body.IndexOf("_stepOrigins[index] = -1;", StringComparison.Ordinal);
        int shown = body.IndexOf("lstClearance.Items[index] = rebuilt.Sentence();", StringComparison.Ordinal);
        int closed = body.IndexOf("EndStepChange();", StringComparison.Ordinal);
        int spoken = body.IndexOf("SpeakStepAdded(_steps[index].Spoken(index + 1), focusMoves);", StringComparison.Ordinal);
        Assert.True(refusal >= 0 && same > refusal && removed > same && written > removed && origin > written
                    && shown > origin && closed > shown && spoken > closed, "refused, compared, replaced, closed, then read back");
        Assert.Contains("SetStepKindSilently(ClearanceStepKind.Via);", body);
        Assert.Contains("cmbStepName.Focus();", body);
        // The index is the one the change was opened on, taken once; it is never written once the list has changed.
        Assert.Contains("int index = _changingStepIndex;", body);
        Assert.DoesNotContain("_steps[_changingStepIndex] =", form);

        Assert.Single(Regex.Matches(form, "\"A&dd Step\""));
        Assert.Contains("btnAddStep.Text = _addStepText;", Member(form, "private void EndStepChange("));
        Assert.Contains("_changingStepIndex = -1;", Member(form, "private void EndStepChange("));
    }

    // Stage 4, D6: Escape with a step being changed keeps the step as it was and goes back to it in the Clearance list
    // (the screen reader reads it); with no change open it closes the window, as it always has. The form sees the key
    // first (KeyPreview), so the check comes before Close().
    [Fact]
    public void Escape_cancels_an_open_change_and_closes_the_window_only_with_none_open()
    {
        string form = FormSource();
        Assert.Contains("this.KeyDown += OnFormKeyDown;", form);
        Assert.DoesNotContain("if (e.KeyCode == Keys.Escape) this.Close();", form);
        string keys = Member(form, "private void OnFormKeyDown(");
        int open = keys.IndexOf("if (_changingStepIndex >= 0)", StringComparison.Ordinal);
        int cancel = keys.IndexOf("CancelStepChange(true);", StringComparison.Ordinal);
        int stop = keys.IndexOf("return;", cancel < 0 ? 0 : cancel, StringComparison.Ordinal);
        int close = keys.IndexOf("Close();", StringComparison.Ordinal);
        Assert.True(open >= 0 && cancel > open && stop > cancel && close > stop, "a change is cancelled and the window stays open");

        string cancelBody = Member(form, "private void CancelStepChange(");
        int ended = cancelBody.IndexOf("EndStepChange();", StringComparison.Ordinal);
        int silent = cancelBody.IndexOf("if (!focusTheStep) return;", StringComparison.Ordinal);
        int select = cancelBody.IndexOf("lstClearance.SelectedIndex = index;", StringComparison.Ordinal);
        int focus = cancelBody.IndexOf("lstClearance.Focus();", StringComparison.Ordinal);
        Assert.True(ended >= 0 && silent > ended && select > silent && focus > select, "closed, then back to the step");
        Assert.DoesNotContain("_announcer.", cancelBody);
        Assert.DoesNotContain("_steps[", cancelBody);
    }

    // Final review, minor 1: Escape in an open drop-down list closes the list, as Enter there takes the highlighted item
    // (OnStepBoxKeyDown): it neither cancels an open change nor closes the window. The form sees the key first
    // (KeyPreview), so it leaves the key unhandled for the list before anything else.
    [Fact]
    public void Escape_in_an_open_drop_down_only_closes_the_list()
    {
        string keys = Member(FormSource(), "private void OnFormKeyDown(");
        int escape = keys.IndexOf("if (e.KeyCode != Keys.Escape) return;", StringComparison.Ordinal);
        int focused = keys.IndexOf("Control? focused = ActiveControl;", StringComparison.Ordinal);
        int walk = keys.IndexOf("while (focused is ContainerControl { ActiveControl: { } inner }) focused = inner;", StringComparison.Ordinal);
        int dropped = keys.IndexOf("if (focused is ComboBox { DroppedDown: true }) return;", StringComparison.Ordinal);
        int open = keys.IndexOf("if (_changingStepIndex >= 0)", StringComparison.Ordinal);
        int close = keys.IndexOf("Close();", StringComparison.Ordinal);
        Assert.True(escape >= 0 && focused > escape && walk > focused && dropped > walk && open > dropped && close > dropped,
            "an open list is let close before a change is cancelled or the window closed");
        string beforeTheList = keys[..dropped];
        Assert.DoesNotContain("e.Handled", beforeTheList);
        Assert.DoesNotContain("SuppressKeyPress", beforeTheList);
        Assert.DoesNotContain("CancelStepChange(", beforeTheList);
    }

    // Stage 4, D7 and Review Focus 2: anything else that changes the list closes an open change first, silently, so a
    // replacement never lands on a step that has moved or changed (a leg end, the fill, an airport change and Stop go
    // through ClearSteps; a trim through ShowSteps; the Hold short key through FollowPutBackCrossings), and so does the
    // window hiding.
    [Fact]
    public void Anything_else_that_changes_the_list_closes_an_open_change_first()
    {
        string form = FormSource();
        foreach (var (member, change) in new[]
                 {
                     ("private void ClearSteps(", "_steps.Clear();"),
                     ("private void ShowSteps(", "_steps.Clear();"),
                     ("private void RemoveStep(", "_steps.RemoveAt(index);"),
                     ("private void FollowPutBackCrossings(", "_steps[i] = steps[i];"),
                 })
        {
            string body = Member(form, member);
            int cancel = body.IndexOf("CancelStepChange(false);", StringComparison.Ordinal);
            int changed = body.IndexOf(change, StringComparison.Ordinal);
            Assert.True(cancel >= 0 && changed > cancel, $"{member} closes an open change before it changes the list");
        }

        string closing = Member(form, "protected override void OnFormClosing(");
        int cancelOnHide = closing.IndexOf("CancelStepChange(false);", StringComparison.Ordinal);
        int hide = closing.IndexOf("Hide();", StringComparison.Ordinal);
        Assert.True(cancelOnHide >= 0 && hide > cancelOnHide, "the window hiding closes an open change");

        // The paths into ClearSteps stay as pinned above (the leg end, the fill, an airport change, Stop).
        Assert.Contains("ClearSteps();", Member(form, "private void EmptyStepsAfterLeg("));
        Assert.Contains("ClearSteps()", Member(form, "private void ResetRouteShapingControls("));
    }

    // Stage 4, D8: Calculate with a step being changed goes ahead only when the boxes would make the same step (the
    // change is closed); otherwise it refuses with the step's number and changes nothing: before the trim, which would
    // otherwise close the change itself, and long before the plan is built.
    [Fact]
    public void Calculate_refuses_while_a_step_is_being_changed()
    {
        string calculate = Member(FormSource(), "private void OnCalculateClicked(");
        int open = calculate.IndexOf("if (_changingStepIndex >= 0)", StringComparison.Ordinal);
        int same = calculate.IndexOf("if (!RebuildStepBeingChanged().Same)", StringComparison.Ordinal);
        int refuse = calculate.IndexOf("RefuseCalculate(ClearanceStepEntry.StillChanging(_changingStepIndex + 1));", StringComparison.Ordinal);
        int closed = calculate.IndexOf("CancelStepChange(false);", StringComparison.Ordinal);
        int trim = calculate.IndexOf("TrimStepsToWhatIsAhead();", StringComparison.Ordinal);
        int build = calculate.IndexOf("ClearancePlanner.Build(_steps,", StringComparison.Ordinal);
        Assert.True(open >= 0 && same > open && refuse > same && closed > refuse && trim > closed && build > trim,
            "an unfinished change is refused, or closed when it changes nothing, before the trim and the plan");
    }

    private static string FormSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

    // LoadRoute's Fail snapshot: from the first prior capture to the end of the Fail local function.
    private static string FailSnapshot(string routing)
    {
        int start = routing.IndexOf("var priorDataProvider = _dataProvider;", StringComparison.Ordinal);
        Assert.True(start >= 0, "the Fail snapshot not found");
        int end = routing.IndexOf("return message;", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of Fail not found");
        return routing[start..end];
    }

    // A member's text: from its declaration to the next member at class level.
    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }

    private static string RoutingSource() => ServicesSource("TaxiGuidanceManager.Routing.cs");
    private static string ManagerSource() => ServicesSource("TaxiGuidanceManager.cs");
    private static string RolloutSource() => ServicesSource("TaxiGuidanceManager.Rollout.cs");

    private static string ServicesSource(string file)
        => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services", file));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
