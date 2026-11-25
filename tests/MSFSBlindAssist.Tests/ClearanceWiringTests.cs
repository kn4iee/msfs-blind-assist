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
        Assert.Contains("_clearanceProgress = _clearanceProgress.WithRowsLeftOut(leftOutRows);", load);
        // The stops as lists follow their rows, and a stop with no row never keeps a first taxiway from being left out.
        Assert.Contains("ClearanceRows.Without(userRunwayHoldShorts, droppedRowIndices)", load);
        Assert.Contains("UndrivenClearanceStart.PicksAllow(", load);
        Assert.Contains("ClearanceRows.RowsOf(userRunwayHoldShorts)", load);
        Assert.DoesNotContain("userRunwayHoldShorts?.Keys", load);
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
        int placed = recalc.IndexOf("_clearanceProgress = _clearanceProgress.WithPlaced(recalcPlacedStops);", StringComparison.Ordinal);
        Assert.True(settle >= 0 && adopt > settle, "settled before the recalculated route is adopted");
        Assert.True(placed > adopt, "the recalculated route's stops recorded once it is adopted");
    }

    // A recalculation keeps a stop with no row of its own (-1) as -1, with no cleared taxiway left to follow too (every
    // row behind); one on a row behind the aircraft goes.
    [Fact]
    public void A_recalculation_keeps_a_stop_with_no_row()
    {
        string recalc = Member(RoutingSource(), "private void TryRecalculateRoute(");

        Assert.Contains("int rowsBehind = sequenceRemains ? seqOffset : int.MaxValue;", recalc);
        Assert.Contains("var remappedRwy = ClearanceRows.After(_userRunwayHoldShorts, rowsBehind);", recalc);
        Assert.Contains("var remappedTwy = ClearanceRows.After(_userTaxiwayHoldShorts, rowsBehind);", recalc);
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
        Assert.Contains("ClearanceRouteChecks.LegEndMeeting(", Member(routing, "private void ApplyLegEndSetback("));

        string applier = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Navigation", "TurnPickApplier.cs"));
        Assert.Contains("ResolveProgressiveEnd(ctx.Graph, v, ctx.Sequence[row], stepBlocked, ctx.Terminator, ctx.LegEndCrossedTaxiways)", applier);
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
