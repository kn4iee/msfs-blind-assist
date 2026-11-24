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

    // The form's "After crossing runway" clears that runway at the place the pilot named, and only there.
    [Fact]
    public void The_after_crossing_leg_asks_for_its_crossing_at_its_place()
    {
        string form = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

        Assert.Contains("crossAtTaxiwayName = crossAt;", form);
        Assert.Contains("new ClearedCrossing(runwayTarget, At: crossAtTaxiwayName, AtNodeId: crossAtHold?.NodeId ?? 0)", form);
        Assert.Matches(@"userTurnPicks: progTurnPicks\.Count > 0 \? progTurnPicks : null,\s+(//[^\n]*\n\s+)*clearedCrossings: ", form);
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
