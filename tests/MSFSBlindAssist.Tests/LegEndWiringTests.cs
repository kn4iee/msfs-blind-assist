// A Progressive Taxi leg with no destination ends the way a hold short does: the same countdown by
// speed, and a stop short of the point counts as arriving. The decision and the wording live in the
// pure ProgressiveLegEnd (ProgressiveLegEndTests); TaxiGuidanceManager has no test harness, so this
// pins from the source that the manager asks it, rather than keeping a second copy of either.

using System;
using System.IO;

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
        Assert.Contains("(!legEndTaxiing || (!ProgressiveLegEnd.EndsAtAHold(", text);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
