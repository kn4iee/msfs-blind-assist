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
        int unmapped = speak.IndexOf("ConsumeUnmappedStartWarning()", StringComparison.Ordinal);
        int turnCue = speak.IndexOf("ConsumeInitialTurnCue()", StringComparison.Ordinal);
        int startHold = speak.IndexOf("ConsumeStartHoldCue()", StringComparison.Ordinal);
        Assert.True(imported >= 0 && warnings > imported && checks > warnings && unmapped > checks
                    && turnCue > unmapped && startHold > turnCue, "the parts are in order");
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
        string dialogs = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "MainForm.Dialogs.cs"));
        string open = Member(dialogs, "private void OpenTaxiForm(");
        int show = open.IndexOf("taxiAssistForm.Show();", StringComparison.Ordinal);
        int focus = open.IndexOf("taxiAssistForm.FocusForOpening();", StringComparison.Ordinal);
        Assert.True(show >= 0 && focus > show, "OpenTaxiForm sets the focus after Show()");

        // Trimmed from the list as last calculated, with GetClearanceProgress's lists passed straight in.
        string trim = Member(form, "private bool TrimStepsToWhatIsAhead(");
        Assert.Contains("ClearanceTrim.Remaining(_calculatedSteps, progress.ViaStepsDone, _calculatedWithDestination,", trim);
        Assert.Contains("progress.ClearedInForce, progress.ClearedUsed, progress.PutBack, progress.HoldsPassed,", trim);
        Assert.Contains("progress.TurnOntoFirstRemainingMade, out var origins)", trim);
        Assert.Contains("ClearanceTrim.KeepingEdits(", trim);
        Assert.Contains("_guidanceManager.HasLiveRoute", trim);
        Assert.Contains("TrimStepsToWhatIsAhead()", Member(form, "public void FocusForOpening("));
        // Both successful Calculates record the list they used, after guidance has started.
        Assert.Equal(2, Regex.Matches(form, @"StartGuidance\([^;]*\);\r?\n\s+RecordCalculatedSteps\(hasDestination: (true|false)\);").Count);
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
            Member(form, "public void FocusForOpening("));
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
        Assert.Contains("LegEndEmptiesList(generation, _calculateGeneration, _calculatedSteps != null)",
            Member(form, "private void EmptyStepsAfterLeg("));
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
