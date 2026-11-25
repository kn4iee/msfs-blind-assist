// A crossing the pilot was cleared for is cleared at one place, on the route as picked, by the pure
// ClearedCrossingHolds (ClearedCrossingHoldsTests). TaxiGuidanceManager has no test harness [VP-1], so this
// pins from the source that the manager clears through it inside the one pass that places runway stops, keeps
// the crossings cleared on the route being flown whole through a refused load, a rollback, a stop and the
// rollout reset, and that the form's "After crossing runway" leg asks for its crossing this way.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class ClearedCrossingWiringTests
{
    [Fact]
    public void Guidance_clears_crossings_through_the_one_pass()
    {
        string routing = RoutingSource();

        Assert.Contains("ClearedCrossingHolds.Apply(", routing);
        Assert.DoesNotContain("StripClearedCrossing", routing);
        Assert.Contains("_clearedCrossings", FailSnapshot(routing));
        Assert.Contains("_clearedCrossings", Member(routing, "private LoadRouteRollback CaptureLoadRouteRollback("));
        Assert.Contains("_clearedCrossings =", Member(routing, "private void RestoreLoadRouteRollback("));
        Assert.Contains("_clearedCrossings", Member(ManagerSource(), "public void StopGuidance("));
        Assert.Contains("_clearedCrossings", Member(RolloutSource(), "private void EnterRunwayEndCountdown("));
    }

    // [HLD-4] the pass still places every stop; the clearing follows it, its merge and its log, inside the pass
    // AdoptRoute runs, so it sees the route as picked [TURN-1] and before the deferred runway picks are retried.
    [Fact]
    public void The_crossing_is_cleared_after_the_automatic_pass_and_before_the_deferred_picks()
    {
        string routing = RoutingSource();
        string pass = Member(routing, "private void ApplyAutoHoldShortPasses(");

        int insert = pass.IndexOf("RouteRunwayCrossings.InsertRunwayHoldShorts(", StringComparison.Ordinal);
        int merge = pass.IndexOf("RouteRunwayCrossings.MergeUserPickEvents(", StringComparison.Ordinal);
        int log = pass.IndexOf("RouteRunwayCrossings.DescribeForLog(", StringComparison.Ordinal);
        int apply = pass.IndexOf("ClearedCrossingHolds.Apply(", StringComparison.Ordinal);
        Assert.True(insert >= 0 && merge > insert && log > merge && apply > log,
            "the clearing must follow the automatic pass, the merge and the log");

        string load = Member(routing, "public string? LoadRoute(");
        int turnPicks = load.IndexOf("TurnPickApplier.Apply(route", StringComparison.Ordinal);
        int adopt = load.IndexOf("AdoptRoute(", StringComparison.Ordinal);
        int retry = load.IndexOf("RetryDeferredRunwayPicks(", StringComparison.Ordinal);
        Assert.True(turnPicks >= 0 && adopt > turnPicks && retry > adopt,
            "LoadRoute: Turn picks, then AdoptRoute (the pass and the clearing), then the deferred picks");
    }

    // The pilot's Calculate clears what was entered; a recalculation clears again only the crossings cleared on the
    // route being flown, at their stop nodes, and says a stop that came back.
    [Fact]
    public void A_recalculation_clears_again_only_what_the_route_being_flown_had_cleared()
    {
        string pass = Member(RoutingSource(), "private void ApplyAutoHoldShortPasses(");

        Assert.Contains("var requests = recalculation ? _clearedCrossings : _clearRequests;", pass);
        Assert.Contains("_clearedCrossings = outcome.Applied.ToList();", pass);
        Assert.Contains("if (!recalculation) LastClearedCrossingWarning = ClearedCrossingHolds.WarningFor(outcome);", pass);
        Assert.Contains("ClearedCrossingHolds.DroppedOnRecalculation(route, before, outcome)", pass);
        Assert.Contains("_guidanceLog.Info(ClearedCrossingHolds.DescribeForLog(outcome));", pass);
    }

    // The requests are keyed by clearance row, like every per-row pick: both places LoadRoute removes rows re-key them.
    [Fact]
    public void The_requests_follow_the_rows_at_both_row_removals()
    {
        string load = Member(RoutingSource(), "public string? LoadRoute(");

        Assert.Contains("_clearRequests = ClearanceRows.Without(_clearRequests, droppedRowIndices)", load);
        Assert.Contains("_clearRequests = ClearanceRows.Without(_clearRequests, leftOutRows)", load);
    }

    // What could not be cleared is heard: in the summary and in the standstill utterance [HLD-17], beside the Turn
    // pick note and after every route-start note. A stop a recalculation put back rides in "Route changed", before
    // the hold-short and Turn pick notes.
    [Fact]
    public void What_could_not_be_cleared_and_a_stop_put_back_are_said()
    {
        string load = Member(RoutingSource(), "public string? LoadRoute(");

        int turnPickPrepend = load.IndexOf("summary = turnPickNote + \" \" + summary;", StringComparison.Ordinal);
        int warningPrepend = load.IndexOf("summary = LastClearedCrossingWarning + \" \" + summary;", StringComparison.Ordinal);
        int undrivenPrepend = load.IndexOf("summary = undrivenStartNote + \" \" + summary;", StringComparison.Ordinal);
        Assert.True(turnPickPrepend >= 0 && warningPrepend > turnPickPrepend && undrivenPrepend > warningPrepend,
            "the summary carries the warning just ahead of the Turn pick note, after the route-start notes");

        int turnBackHeard = load.IndexOf("mustHear.Add(turnBackNote);", StringComparison.Ordinal);
        int warningHeard = load.IndexOf("mustHear.Add(LastClearedCrossingWarning);", StringComparison.Ordinal);
        int turnPickHeard = load.IndexOf("mustHear.Add(turnPickNote);", StringComparison.Ordinal);
        Assert.True(turnBackHeard >= 0 && warningHeard > turnBackHeard && turnPickHeard > warningHeard,
            "the must-hear list carries the warning after the route-start notes, just ahead of the Turn pick note");

        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");
        int compose = recalc.IndexOf("RouteChangedCallout.Compose(", StringComparison.Ordinal);
        int dropNote = recalc.IndexOf("callout = callout + \" \" + _clearedCrossingDropNote;", StringComparison.Ordinal);
        int holdWarning = recalc.IndexOf("callout = callout + \" \" + recalcHoldWarning;", StringComparison.Ordinal);
        Assert.True(compose >= 0 && dropNote > compose && holdWarning > dropNote,
            "\"Route changed\" says a stop put back right after its own sentence");
    }

    // [RTE-22] discards a same-named recalculation; one that no longer stops where a crossing still ahead was cleared
    // is adopted, or the old route would keep that stop cleared where the new route does not stop.
    [Fact]
    public void A_same_named_route_that_moves_a_cleared_stop_is_not_a_no_op()
    {
        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");

        Assert.Contains("bool clearedStopMoves = ", recalc);
        Assert.Matches(@"!oldRouteWrongForTurnPick && !clearedStopMoves\)\s+return;", recalc);
    }

    // The form's "After crossing runway" clears that runway at the place the pilot named, and only there: a taxiway by
    // its name, a holding point by its node and its name, so a stop whose label names the point matches and one the
    // route does not cross there is said by name.
    [Fact]
    public void The_after_crossing_leg_asks_for_its_crossing_at_its_place()
    {
        string form = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

        Assert.Contains("crossAtTaxiwayName = crossAt;", form);
        Assert.Contains("new ClearedCrossing(runwayTarget, At: crossAtHold?.Name ?? crossAtTaxiwayName, AtNodeId: crossAtHold?.NodeId ?? 0)", form);
        Assert.Matches(@"userTurnPicks: progTurnPicks\.Count > 0 \? progTurnPicks : null,\s+(//[^\n]*\n\s+)*clearedCrossings: ", form);
    }

    // A runway is never crossed in silence: a cleared crossing is counted down like a stop and said at its line.
    [Fact]
    public void A_cleared_crossing_is_counted_down_and_spoken_at_the_line()
    {
        string manager = ManagerSource() + RoutingSource();

        Assert.Contains("ClearedCrossingCallouts.Approach(", manager);
        Assert.Contains("ClearedCrossingCallouts.AtTheLine(", manager);
    }

    // The hold-short countdown counts a fully cleared stop down at the same distances by speed and on the same three
    // latches, the distance from DistanceFormatter (the pilot's unit option), "cleared to cross" on the first call
    // only; no "Slow down", no "Stop", no stopped-between-tiers line, and never held by the start grace window
    // [RTE-27]. A leg end on the same segment wins, and nothing is counted down while holding
    // (ClearedCrossingHolds.CountsDownAsCleared, unit-tested).
    [Fact]
    public void The_countdown_speaks_a_cleared_crossing_in_its_own_words()
    {
        string countdown = Member(ManagerSource(), "private void CheckHoldShortCountdown(");

        Assert.Contains("bool clearedCrossing = ClearedCrossingHolds.CountsDownAsCleared(currentSeg, legEnd != null, _state == TaxiGuidanceState.Taxiing);", countdown);
        Assert.Contains("if (!currentSeg.IsHoldShortPoint && legEnd == null && !clearedCrossing)", countdown);
        Assert.Contains("ClearedCrossingCallouts.Approach(", countdown);
        Assert.Contains("DistanceFormatter.FromFeet(distFeet), firstCall)", countdown);
        // Each of the three tiers speaks the cleared line in place of the hold short's, after the leg end's.
        Assert.Equal(3, Regex.Matches(countdown, @"\? LegEndLine\(legEnd, CountdownTier\.\w+\)\s+: clearedCrossing \? ClearedLine\(\)\s+: \$""Hold short ").Count);
        Assert.Contains("if (legEnd == null && !clearedCrossing && _holdShortSlowDownAnnounced && !_holdShortStopAnnounced", countdown);
        Assert.DoesNotContain("_startChatterSuppressUntil", countdown);
        // "Cleared to cross" goes with the first call spoken: judged before this frame latches any tier.
        int first = countdown.IndexOf("bool firstCall = ", StringComparison.Ordinal);
        int latch = countdown.IndexOf("if (distFeet < stopDistFt)", StringComparison.Ordinal);
        Assert.True(first >= 0 && latch > first, "firstCall must be read before the tiers are latched");
        // The outer call's distance is the ONE formula the Hold short key judges its restore by.
        Assert.Contains("double outerDistFt    = ClearedCrossingHolds.OuterCountdownFt(_lastGroundSpeedKts);", countdown);
    }

    // [STR-5] The advance never passes a cleared line in silence: AdvanceSegment (the waypoint capture and the
    // pin-breaker) and AdvanceToNearestSegment's jump, which checks every segment it passes BEFORE the hold-short
    // test, since a cleared stop is no longer a hold short.
    [Fact]
    public void The_advance_never_passes_a_cleared_line_in_silence()
    {
        string routing = RoutingSource();

        string advance = Member(routing, "private void AdvanceSegment(");
        int said = advance.IndexOf("if (ClearedCrossingHolds.IsFullyCleared(completedSeg)) PassClearedLine(completedSeg);", StringComparison.Ordinal);
        Assert.True(said >= 0 && said < advance.LastIndexOf("_currentSegmentIndex++;", StringComparison.Ordinal),
            "AdvanceSegment says a cleared line before it moves past it");

        string nearest = Member(routing, "private void AdvanceToNearestSegment(");
        int loop = nearest.IndexOf("for (int i = _currentSegmentIndex; i < bestIdx; i++)", StringComparison.Ordinal);
        int jumpSaid = nearest.IndexOf("if (ClearedCrossingHolds.IsFullyCleared(_route.Segments[i])) PassClearedLine(_route.Segments[i]);", StringComparison.Ordinal);
        int skip = nearest.IndexOf("if (!_route.Segments[i].IsHoldShortPoint) continue;", StringComparison.Ordinal);
        Assert.True(loop >= 0 && jumpSaid > loop && skip > jumpSaid, "the jump says each cleared line it passes, before the hold-short test");
    }

    // At the line: said once (SpeakNow, the frame's one utterance [STR-22]); the incursion watch's own "Crossing …" for
    // that node latched off; the crossing used once, in a NEW list; remembered for the Hold short key; and the three
    // countdown latches reset, so a stale "Slow down" latch never reaches a leg end's arrival test
    // (ProgressiveLegEnd.Arrived) and the next countdown speaks.
    [Fact]
    public void At_the_line_the_crossing_is_said_once_used_once_and_the_countdown_starts_fresh()
    {
        string pass = Member(ManagerSource(), "private void PassClearedLine(string label, int stopNodeId)");

        Assert.Contains("SpeakNow(ClearedCrossingCallouts.AtTheLine(label));", pass);
        Assert.Contains("UseClearedCrossing(label, stopNodeId);", pass);
        Assert.Contains("_holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;", pass);

        string use = Member(ManagerSource(), "private void UseClearedCrossing(string label, int stopNodeId)");
        Assert.Contains("_lastIncursionWarnedNodeId = stopNodeId;", use);
        Assert.Contains("_clearedCrossings = ClearedCrossingHolds.WithoutStop(_clearedCrossings, stopNodeId);", use);
        Assert.Contains("_passedClearedLabel = label;", use);
        Assert.Contains("_passedClearedAtUtc = MSFSBlindAssist.Utils.SimClock.UtcNow;", use);
        Assert.Contains("PassClearedLine(ClearedCrossingHolds.LineLabel(segment.PlacedHoldLabel, segment.ClearedRunways), segment.ToNode.NodeId)",
            Member(ManagerSource(), "private void PassClearedLine(TaxiRouteSegment segment)"));

        // Ended with the route: cleared by StopGuidance and the rollout reset.
        Assert.Contains("_passedClearedLabel = null;", Member(ManagerSource(), "public void StopGuidance("));
        Assert.Contains("_passedClearedLabel = null;", Member(RolloutSource(), "private void EnterRunwayEndCountdown("));
    }

    // A shared stop cleared for one runway still holds for the other, its hold sentence and [HLD-11] staging as they
    // were. The Continue that RELEASES it (after any staging press has returned) lets the aircraft across EVERY runway
    // at it, so that press names them all: when a cleared runway is crossed there (ClearedPartLabel), the whole placed
    // label (LineLabel) as "Crossing …", straight after "Continuing." (ClearedCrossingCallouts.Continuing), in the
    // order things happen, before the taxiway, the turn cue and any paired-Continue offer. Then the crossing is used
    // as at a cleared line (UseClearedCrossing: used once, remembered, the watch's own line latched off, correctly,
    // since nothing at that node is left unnamed). The start hold too.
    [Fact]
    public void The_Continue_that_releases_a_shared_stop_names_every_runway_at_it_first()
    {
        string manager = ManagerSource();
        string cont = Member(manager, "public void ContinuePastHoldShort(");

        int staged = cont.IndexOf("RunwayHoldStages.ComposeAdvance(cleared, next)", StringComparison.Ordinal);
        int released = cont.IndexOf("var releasedCrossing = ClearedPartOfReleasedStop();", StringComparison.Ordinal);
        int startSpent = cont.IndexOf("_route.StartHoldRunway = null;", StringComparison.Ordinal);
        Assert.True(staged >= 0 && released > staged && startSpent > released,
            "the released stop is read after the staging press returns and before the start hold is spent");
        Assert.Contains("string continuing = ClearedCrossingCallouts.Continuing(releasedCrossing is { } rc ? ClearedCrossingCallouts.AtTheLine(rc.Label) : null);", cont);
        // Every resume sentence opens with it, and none writes its own "Continuing." any more.
        Assert.Contains("? $\"{continuing}{startTurnCue} {pairedOffer}\".TrimEnd()", cont);
        Assert.Contains(": $\"{continuing}{taxiway}{pairedOffer}\");", cont);
        Assert.Contains("AnnounceInstruction($\"{continuing}{pairedOffer}\");", cont);
        Assert.DoesNotContain("$\"Continuing. ", cont);
        int said = cont.LastIndexOf("{continuing}", StringComparison.Ordinal);
        int used = cont.IndexOf("if (releasedCrossing is { } usedCrossing) UseClearedCrossing(usedCrossing.Label, usedCrossing.StopNodeId);", StringComparison.Ordinal);
        Assert.True(said >= 0 && used > said, "the crossing is used up after the sentence that names it");

        string part = Member(manager, "private (string Label, int StopNodeId)? ClearedPartOfReleasedStop(");
        Assert.Contains("if (_route == null || _holdShortAtDestination) return null;", part);
        Assert.Contains("ClearedCrossingHolds.ClearedPartLabel(_route.PlacedStartHoldLabel, _route.StartHoldClearedRunways) != null", part);
        Assert.Contains("ClearedCrossingHolds.LineLabel(_route.PlacedStartHoldLabel, _route.StartHoldClearedRunways)", part);
        Assert.Contains("ClearedCrossingHolds.ClearedPartLabel(held.PlacedHoldLabel, held.ClearedRunways) != null", part);
        Assert.Contains("ClearedCrossingHolds.LineLabel(held.PlacedHoldLabel, held.ClearedRunways)", part);

        // The start hold released without a Continue (the adoption's start-hold skip) names its whole line as it starts
        // across, never drops it in silence.
        string start = Member(manager, "private void AnnounceClearedStartCrossing(");
        Assert.DoesNotContain("ClearedPartLabel", start);
        Assert.DoesNotContain("_clearedCrossings = ", start);
    }

    // While a cleared stop is the current segment its countdown owns its node, as for a hold short: the incursion
    // watch's "Crossing …" is not said on the approach, and the line latches it off after.
    [Fact]
    public void The_incursion_watch_leaves_a_cleared_stop_to_its_countdown()
    {
        string watch = Member(ManagerSource(), "private void CheckRunwayIncursion(");

        Assert.Matches(@"_route\.Segments\[_currentSegmentIndex\]\.IsHoldShortPoint\s+\|\| ClearedCrossingHolds\.IsFullyCleared\(_route\.Segments\[_currentSegmentIndex\]\)", watch);
    }

    // A cleared START hold: the aircraft is at the line when the route starts, so there is no approach; the crossing is
    // said once as it starts across, before the segment cursor moves in that frame, and used up like any other.
    [Fact]
    public void A_cleared_start_hold_is_said_once_as_the_aircraft_starts_across()
    {
        string manager = ManagerSource();
        string frame = Member(manager, "private void UpdatePositionFrame(");
        int start = frame.IndexOf("AnnounceClearedStartCrossing(lat, lon, groundSpeedKts);", StringComparison.Ordinal);
        int advance = frame.IndexOf("AdvanceToNearestSegment(lat, lon, headingTrue);", StringComparison.Ordinal);
        Assert.True(start >= 0 && advance > start, "the cleared start is judged before the segment cursor moves");

        string method = Member(manager, "private void AnnounceClearedStartCrossing(");
        Assert.Contains("ClearedCrossingHolds.HasStop(_clearedCrossings, start.NodeId)", method);
        Assert.Contains("ClearedCrossingHolds.HasStartedAcross(groundSpeedKts,", method);
        Assert.Contains("Navigation.RouteRunwayCrossings.RouteProgressMeters(_route.Segments, lat, lon)", method);
        Assert.Contains("PassClearedLine(ClearedCrossingHolds.LineLabel(_route.PlacedStartHoldLabel, _route.StartHoldClearedRunways), start.NodeId);", method);
    }

    // _clearedCrossings is REPLACED, never mutated in place: LoadRoute's Fail snapshot and the load rollback hold the
    // reference, so an in-place change would rewrite the snapshot a refused load restores [RTE-29].
    [Fact]
    public void The_cleared_crossings_list_is_replaced_never_mutated()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Services"), "TaxiGuidanceManager*.cs"))
            Assert.DoesNotMatch(@"_clearedCrossings\s*\.\s*(Add|AddRange|Insert|InsertRange|Remove|RemoveAll|RemoveAt|RemoveRange|Clear|Sort|Reverse)\s*\(",
                File.ReadAllText(file));

        Assert.Matches(@"//[^\n]*[Rr]eplaced, never mutated in place[^\n]*\n\s*private List<Navigation\.ClearedCrossing> _clearedCrossings = ", ManagerSource());
    }

    // Continue before the line (spec 8.4): while taxiing, once the countdown has named the stop ahead (any of its three
    // tiers spoken), a press clears the first runway its label names at that one stop, records it as an entered
    // crossing (a NEW list), and says so. A stop left with no runway keeps the three latches set, so its approach calls
    // never repeat what was just said; its line is still said (PassClearedLine). Tried after the paired double press and
    // before the HoldShort guard, so a press in HoldShort is as it was; never while a paired offer waits for its window
    // to open, a key bounce or a quick double press that was ignored before and still is.
    [Fact]
    public void A_Continue_before_the_line_clears_a_runway_the_countdown_has_named()
    {
        string cont = Member(ManagerSource(), "public void ContinuePastHoldShort(");

        int paired = cont.IndexOf("Also cleared across {label}. No stop between the runways.", StringComparison.Ordinal);
        int early = cont.IndexOf("ClearedCrossingHolds.CanClearEarly(", StringComparison.Ordinal);
        int guard = cont.IndexOf("if (_state != TaxiGuidanceState.HoldShort || _route == null) return;", StringComparison.Ordinal);
        Assert.True(paired >= 0 && early > paired && guard > early,
            "the early Continue is tried after the paired double press and before the HoldShort guard");

        string branch = cont[paired..guard];
        Assert.Contains("if (_state == TaxiGuidanceState.Taxiing && _route != null", branch);
        Assert.Contains("&& !(_pairedCrossingSegIdx >= 0 && MSFSBlindAssist.Utils.SimClock.UtcNow < _pairedCrossingWindowFrom))", branch);
        Assert.Contains("_holdShortOuterAnnounced || _holdShortSlowDownAnnounced || _holdShortStopAnnounced,", branch);
        Assert.Contains("seg.IsHoldShortPoint, seg.HoldShortRunway, _currentSegmentIndex >= _route.Segments.Count - 1)", branch);
        Assert.Contains("RouteRunwayCrossings.ExtractRunwayDesignator(seg.HoldShortRunway) is string runway", branch);
        // Recorded as a Continue's, never as a crossing the pilot entered (stage 3 maps only those to a Cross step).
        Assert.Contains("_clearedCrossings = ClearedCrossingHolds.WithCrossing(_clearedCrossings,", branch);
        Assert.Contains("new ClearedCrossing(runway, StopNodeId: seg.ToNode.NodeId, Origin: ClearedBy.Continue));", branch);
        Assert.Contains("AnnounceInstruction(ClearedCrossingCallouts.EarlyContinue(runway, seg.IsHoldShortPoint ? seg.HoldShortRunway : null));", branch);
        Assert.Contains("if (ClearedCrossingHolds.IsFullyCleared(seg)) _holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = true;", branch);
        Assert.DoesNotContain("_holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;", branch);
    }

    // ClearAt can refuse even a stop whose label names the runway (one shared with the pilot's own taxiway or holding-point
    // hold). Both Continue paths act on its answer: nothing is recorded, changed or said unless it cleared. The early press
    // then does what it always did (nothing); the paired press is never offered such a stop, and should ClearAt still
    // refuse, the stop stays and nothing is said: the paired path never takes a stop off by hand.
    [Fact]
    public void Both_Continue_paths_act_only_on_what_ClearAt_cleared()
    {
        string cont = Member(ManagerSource(), "public void ContinuePastHoldShort(");

        // Never called for its side effect alone, its answer dropped.
        Assert.DoesNotMatch(@"(?m)^\s*(Navigation\.)?ClearedCrossingHolds\.ClearAt\(", cont);

        int earlyClear = cont.IndexOf("&& ClearedCrossingHolds.ClearAt(_route, _currentSegmentIndex + 1, runway))", StringComparison.Ordinal);
        int earlyRecord = cont.IndexOf("new ClearedCrossing(runway, StopNodeId: seg.ToNode.NodeId, Origin: ClearedBy.Continue)", StringComparison.Ordinal);
        int earlySaid = cont.IndexOf("ClearedCrossingCallouts.EarlyContinue(", StringComparison.Ordinal);
        Assert.True(earlyClear >= 0 && earlyRecord > earlyClear && earlySaid > earlyClear,
            "the early press records and speaks only inside ClearAt's yes");

        int pairedClear = cont.IndexOf("&& ClearedCrossingHolds.ClearAt(_route, segIdx + 1, pairedRunway))", StringComparison.Ordinal);
        int pairedRecord = cont.IndexOf("new ClearedCrossing(pairedRunway, StopNodeId: pairedSeg.ToNode.NodeId, Origin: ClearedBy.Continue)", StringComparison.Ordinal);
        Assert.True(pairedClear >= 0 && pairedRecord > pairedClear, "the paired press records only inside ClearAt's yes");
        // The paired sentence is said inside that yes block too (no closing brace at the block's own indentation before it).
        Assert.Matches(@"ClearAt\(_route, segIdx \+ 1, pairedRunway\)\)\s*\{(?:(?!\n {16}\})[\s\S])*AnnounceInstruction\(\$""Also cleared across \{label\}\. No stop between the runways\.""\);", cont);
        // Never blanked by hand: a stop ClearAt will not clear stays a stop, and nothing is said for it.
        Assert.DoesNotContain("pairedSeg.IsHoldShortPoint = false;", cont);
        Assert.DoesNotContain("pairedSeg.HoldShortRunway = \"\";", cont);
        int pairedEnd = cont.IndexOf("return;", pairedClear, StringComparison.Ordinal);
        Assert.True(pairedEnd > pairedClear, "the end of the paired branch not found");
        string pairedTail = cont[pairedClear..pairedEnd];
        Assert.DoesNotMatch(@"pairedSeg\.\w+\s*=[^=]", pairedTail);
        Assert.Single(Regex.Matches(pairedTail, @"AnnounceInstruction\(|SpeakNow\("));
    }

    // The paired offer is never made for a stop the press could not clear: after the two-runway refusal, the offer scan
    // breaks on a stop ClearAt would refuse (ClearedCrossingHolds.CanClearLabel: one shared with the pilot's own taxiway,
    // holding-point or end-of-taxiway hold), before it records the stop or opens the window.
    [Fact]
    public void The_paired_offer_skips_a_stop_shared_with_the_pilots_own_hold()
    {
        string cont = Member(ManagerSource(), "public void ContinuePastHoldShort(");

        int twoRunways = cont.IndexOf("if (Navigation.RouteRunwayCrossings.ExtractRunwayDesignators(s.HoldShortRunway).Count != 1)", StringComparison.Ordinal);
        int refused = cont.IndexOf("if (!ClearedCrossingHolds.CanClearLabel(s.HoldShortRunway, Navigation.RouteRunwayCrossings.ExtractRunwayDesignator(s.HoldShortRunway) ?? \"\"))", StringComparison.Ordinal);
        int offered = cont.IndexOf("_pairedCrossingSegIdx = i;", StringComparison.Ordinal);
        Assert.True(twoRunways >= 0 && refused > twoRunways && offered > refused,
            "the scan refuses a stop the press cannot clear, after the two-runway refusal and before the offer");
        Assert.Matches(@"CanClearLabel\(s\.HoldShortRunway, [^\n]*\)\)\s+break;", cont);
    }

    // The paired-runway double Continue clears its stop the same way: the one runway the offer named, at that stop,
    // recorded, so it is counted down and said at its line and the Hold short key can put it back. Its sentence is unchanged.
    [Fact]
    public void The_paired_double_Continue_clears_its_stop_the_same_way()
    {
        string cont = Member(ManagerSource(), "public void ContinuePastHoldShort(");

        Assert.Contains("RouteRunwayCrossings.ExtractRunwayDesignator(pairedSeg.HoldShortRunway) is string pairedRunway", cont);
        Assert.Contains("_clearedCrossings = ClearedCrossingHolds.WithCrossing(_clearedCrossings,", cont);
        Assert.Contains("AnnounceInstruction($\"Also cleared across {label}. No stop between the runways.\");", cont);
        Assert.Single(Regex.Matches(cont, @"Also cleared across "));
        // The offer is still made only for a stop that names one runway, never on the route's last segment.
        Assert.Contains("for (int i = _currentSegmentIndex; i < _route.Segments.Count - 1; i++)", cont);
        Assert.Contains("if (Navigation.RouteRunwayCrossings.ExtractRunwayDesignators(s.HoldShortRunway).Count != 1)", cont);
    }

    // The Hold short key (spec 8.8), under the state lock [STR-13]: the pure ClearedCrossingHolds.DecideHoldShortKey
    // (ClearedCrossingHoldShortKeyTests) decides from the route, the state and the aircraft, and the manager acts on it.
    // It never clears a runway (no ClearAt, no WithCrossing); it speaks through AnnounceInstruction and SpeakNow only
    // [STR-22]; every answer it reports is spoken, each worded by ClearedCrossingCallouts.
    [Fact]
    public void The_hold_short_key_decides_purely_and_never_clears_a_runway()
    {
        string manager = ManagerSource();
        string key = Member(manager, "public void HoldShortOfNextClearedRunway(");

        Assert.Matches(@"public void HoldShortOfNextClearedRunway\(\)\s*\{\s*lock \(_stateLock\)", key);
        Assert.Contains("ClearedCrossingHolds.DecideHoldShortKey(_route,", key);
        Assert.Contains("TaxiGuidanceState.RouteLoaded or TaxiGuidanceState.Taxiing or TaxiGuidanceState.HoldShort", key);
        Assert.Contains("AnnounceInstruction(ClearedCrossingCallouts.NoRoute);", key);
        Assert.Contains("SpeakNow(ClearedCrossingCallouts.PastTheLine(decision.Label));", key);
        Assert.Contains("SpeakNow(ClearedCrossingCallouts.NextStop(decision.Label, FormatDistance(decision.Metres)));", key);
        Assert.Contains("SpeakNow(ClearedCrossingCallouts.NoRunwayAhead);", key);
        Assert.Contains("PutClearedStopBack(decision);", key);
        // The minute "Past the hold line" is said for, from the line's own time on the sim clock.
        Assert.Contains("(MSFSBlindAssist.Utils.SimClock.UtcNow - _passedClearedAtUtc).TotalSeconds", key);

        string back = Member(manager, "private void PutClearedStopBack(");
        string both = key + back;
        Assert.DoesNotContain("ClearAt(", both);
        Assert.DoesNotContain("WithCrossing(", both);
        Assert.DoesNotContain("_announcer.", both);
    }

    // Putting a stop back: the start hold through RestoreStartHold, a segment's through RestoreAt; the crossing leaves
    // _clearedCrossings as a NEW list (WithoutStop), the watch's latch for that node is lifted, the line is no longer
    // "passed", and the window hears ClearedCrossingsChanged after the sentence. A stop the aircraft holds at asks for
    // every runway again (its hold stages composed afresh, [HLD-11]); a line the segment jump said early puts the cursor
    // back onto its segment, so its countdown and hold run as for any stop. Only the restored stop's OWN countdown starts
    // fresh (it is the current segment, after any cursor move, or the aircraft holds there): resetting the latches of
    // another stop the aircraft is counting down to would repeat that stop's last call and cut the key's sentence off.
    [Fact]
    public void Putting_a_stop_back_restores_it_and_tells_the_window()
    {
        string manager = ManagerSource();
        string back = Member(manager, "private void PutClearedStopBack(");

        Assert.Contains("ClearedCrossingHolds.RestoreStartHold(_route)", back);
        Assert.Contains("ClearedCrossingHolds.RestoreAt(_route, decision.SegmentIndex)", back);
        Assert.Contains("_clearedCrossings = ClearedCrossingHolds.WithoutStop(_clearedCrossings, stopNodeId);", back);
        Assert.Matches(@"if \(heldHere \|\| ownSegment\)\s+_holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;", back);
        Assert.Single(Regex.Matches(back, @"_holdShortOuterAnnounced = _holdShortSlowDownAnnounced = _holdShortStopAnnounced = false;"));
        Assert.Contains("bool ownSegment = !startHold && decision.SegmentIndex == _currentSegmentIndex;", back);
        int moved = back.IndexOf("_currentSegmentIndex = decision.CursorIndex;", StringComparison.Ordinal);
        int own = back.IndexOf("bool ownSegment = ", StringComparison.Ordinal);
        Assert.True(moved >= 0 && own > moved, "the restored stop's own segment is judged after the cursor moves");
        // While taxiing the key's sentence stands for the outer call only within that call's distance; further out the
        // outer call still comes as the heads-up (the first call is never "Slow down").
        Assert.Matches(@"else if \(ownSegment && _state == TaxiGuidanceState\.Taxiing\s+&& ClearedCrossingHolds\.RestoreSaysOuterCall\(decision\.Metres, _lastGroundSpeedKts\)\)\s+\{\s+(//[^\n]*\n\s+)*_holdShortOuterAnnounced = true;", back);
        Assert.Contains("ComposeHoldEntry(", back);
        Assert.Contains("if (_lastIncursionWarnedNodeId == stopNodeId) _lastIncursionWarnedNodeId = -1;", back);
        Assert.Contains("if (_passedClearedNodeId == stopNodeId) ForgetPassedClearedLine();", back);
        int said = back.IndexOf("AnnounceInstruction(ClearedCrossingCallouts.HoldRestored(decision.Label, FormatDistance(decision.Metres)));", StringComparison.Ordinal);
        int told = back.IndexOf("ClearedCrossingsChanged?.Invoke(this, new ClearedCrossingsChangedEventArgs(restoredCrossings));", StringComparison.Ordinal);
        Assert.True(said >= 0 && told > said, "the window is told after the sentence");
        Assert.Contains("public event EventHandler<ClearedCrossingsChangedEventArgs>? ClearedCrossingsChanged;", manager);

        // It is told WHICH crossings came back (each with its stop node and who cleared it), read before the stop's
        // entries leave the list and before the passed line is forgotten.
        int restored = back.IndexOf("var restoredCrossings = ClearedCrossingHolds.RestoredCrossings(_clearedCrossings, stopNodeId, _passedClearedNodeId, _passedClearedCrossings);", StringComparison.Ordinal);
        int removed = back.IndexOf("_clearedCrossings = ClearedCrossingHolds.WithoutStop(_clearedCrossings, stopNodeId);", StringComparison.Ordinal);
        int forgotten = back.IndexOf("if (_passedClearedNodeId == stopNodeId) ForgetPassedClearedLine();", StringComparison.Ordinal);
        Assert.True(restored >= 0 && removed > restored && forgotten > restored, "the restored crossings are read before they are removed or forgotten");

        // Raised under the state lock: its doc says so, and that a subscriber marshals to its own thread without blocking.
        int eventAt = manager.IndexOf("public event EventHandler<ClearedCrossingsChangedEventArgs>? ClearedCrossingsChanged;", StringComparison.Ordinal);
        string doc = manager[manager.LastIndexOf("/// <summary>", eventAt, StringComparison.Ordinal)..eventAt];
        Assert.Contains("_stateLock", doc);
        Assert.Contains("marshal", doc);
    }

    // The crossings used at a line are remembered with it, so the key can name them if it puts that stop back; they go
    // wherever the line is forgotten. A crossing with no stop is used once the cursor passes where the route reaches its
    // runway, checked each taxiing frame after the cursor moves (no allocation while there is none).
    [Fact]
    public void The_crossings_used_at_a_line_are_remembered_and_one_with_no_stop_is_used_once_passed()
    {
        string manager = ManagerSource();

        string use = Member(manager, "private void UseClearedCrossing(string label, int stopNodeId)");
        int kept = use.IndexOf("_passedClearedCrossings = ClearedCrossingHolds.AtStop(_clearedCrossings, stopNodeId);", StringComparison.Ordinal);
        int dropped = use.IndexOf("_clearedCrossings = ClearedCrossingHolds.WithoutStop(_clearedCrossings, stopNodeId);", StringComparison.Ordinal);
        Assert.True(kept >= 0 && dropped > kept, "the crossings used at the line are kept before they leave the list");
        Assert.Contains("_passedClearedCrossings = ", Member(manager, "private void ForgetPassedClearedLine("));
        Assert.Contains("_passedClearedCrossings = ", Member(manager, "public void StopGuidance("));
        Assert.Contains("_passedClearedCrossings = ", Member(RolloutSource(), "private void EnterRunwayEndCountdown("));

        string frame = Member(manager, "private void UpdatePositionFrame(");
        int advance = frame.IndexOf("AdvanceToNearestSegment(lat, lon, headingTrue);", StringComparison.Ordinal);
        int noStop = frame.IndexOf("UseNoStopCrossingsPassed();", StringComparison.Ordinal);
        Assert.True(advance >= 0 && noStop > advance, "a crossing with no stop is judged after the cursor moves");
        string method = Member(manager, "private void UseNoStopCrossingsPassed(");
        Assert.Contains("ClearedCrossingHolds.WithoutPassedNoStop(_clearedCrossings, _route, _currentSegmentIndex) is { } kept", method);
    }

    // The line just passed is remembered with its node, so the key can judge it from the route geometry; it ends with
    // the route (StopGuidance, the rollout reset) and with a successful new load (it used to survive a new Calculate),
    // cleared only once nothing can refuse the load any more, so a refused load leaves it alone [RTE-29].
    [Fact]
    public void The_line_passed_is_remembered_with_its_node_and_ends_with_the_route()
    {
        string manager = ManagerSource();

        Assert.Contains("_passedClearedNodeId = stopNodeId;", Member(manager, "private void UseClearedCrossing(string label, int stopNodeId)"));
        Assert.Contains("_passedClearedNodeId = 0;", Member(manager, "public void StopGuidance("));
        Assert.Contains("_passedClearedNodeId = 0;", Member(RolloutSource(), "private void EnterRunwayEndCountdown("));
        string forget = Member(manager, "private void ForgetPassedClearedLine(");
        Assert.Contains("_passedClearedLabel = null;", forget);
        Assert.Contains("_passedClearedNodeId = 0;", forget);
        Assert.Contains("_passedClearedAtUtc = DateTime.MinValue;", forget);

        string load = Member(RoutingSource(), "public string? LoadRoute(");
        Assert.Matches(@"ForgetPassedClearedLine\(\);\s+return null;\s+\}\s+catch \(Exception ex\)", load);
        Assert.Single(Regex.Matches(load, @"ForgetPassedClearedLine\(\);"));
    }

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

    // The taxi status readout (Output Y) names the next stop of any kind through the Hold short key's own finder, the
    // route's last segment left out (the sentence's end names it), worded by ClearedCrossingCallouts.StatusClause.
    [Fact]
    public void The_taxi_status_names_the_next_stop_through_the_keys_finder()
    {
        string announcements = ServicesSource("TaxiGuidanceManager.Announcements.cs");
        string status = Member(announcements, "public string GetStatusAnnouncement(");

        Assert.Contains("ClearedCrossingCallouts.StatusClause(", status);
        Assert.Contains("ClearedCrossingHolds.FindNextStop(_route,", status);
        Assert.Contains("leaveOutLast: true", status);
        Assert.Contains("FormatDistance(nextStop.Metres)", status);
        Assert.Contains("return $\"{taxiway}{nextTurn}{stopClause}, {distStr} to {_destinationName}.\";", status);
        // The key's "Next stop" answer is the same finder's (the key itself calls DecideHoldShortKey).
        Assert.Contains("FindNextStop(route, cursor, aircraft,", Member(ClearedHoldsSource(), "public static HoldShortKeyDecision DecideHoldShortKey("));
    }

    private static string ClearedHoldsSource()
        => File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Navigation", "ClearedCrossingHolds.cs"));

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
