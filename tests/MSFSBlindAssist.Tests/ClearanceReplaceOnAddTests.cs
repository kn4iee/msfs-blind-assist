// Tests for the pure rules behind "a later instruction replaces an earlier one" in the Taxi Guidance window:
// which step a newly added runway Cross or Hold short step replaces, the sentence that says so, and which steps
// survive a Progressive leg's end.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceReplaceOnAddTests
{
    private static ClearanceStep R(ClearanceStepKind kind, string name, string? at = null, int node = 0,
        bool passed = false, bool reach = false)
        => new(kind, ClearanceTargetKind.Runway, name, At: at, AtNodeId: node, AtNodeIsPassedStop: passed, AtNodeIsReach: reach);

    private static ClearanceStep Hold(string name, string? at = null, int node = 0) => R(ClearanceStepKind.HoldShort, name, at, node);

    private static ClearanceStep Cross(string name, string? at = null, int node = 0) => R(ClearanceStepKind.Cross, name, at, node);

    private static ClearanceStep Via(string name) => new(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, name);

    [Fact]
    public void A_crossing_replaces_the_hold_short_of_the_same_runway()
        => Assert.Equal(2, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Via("B"), Via("N"), Hold("15R") }, Cross("15R"), null));

    [Fact]
    public void A_hold_short_replaces_the_crossing_of_the_same_runway()
        => Assert.Equal(1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Via("B"), Cross("15R"), Via("N") }, Hold("15R"), null));

    [Fact]
    public void Either_end_of_the_runway_matches()
        => Assert.Equal(0, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("33L") }, Cross("15R"), null));

    [Fact]
    public void Another_runway_is_never_replaced()
        => Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15L") }, Cross("15R"), null));

    [Fact]
    public void Two_places_that_differ_do_not_match_but_a_missing_place_does()
    {
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R", at: "N") }, Cross("15R", at: "K"), null));
        Assert.Equal(0, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R", at: "N") }, Cross("15R"), null));
    }

    [Fact]
    public void Two_holding_point_nodes_that_differ_do_not_match()
        => Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R", node: 10) }, Cross("15R", node: 20), null));

    [Fact]
    public void The_first_matching_step_is_replaced()
        => Assert.Equal(0, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R"), Via("X"), Hold("15R") }, Cross("15R"), null));

    [Fact]
    public void The_destination_runway_is_never_replaced()
    {
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("27") }, Cross("27"), "27"));
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Cross("27") }, Hold("27"), "27"));
    }

    [Fact]
    public void A_crossing_already_made_is_never_replaced()
    {
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(
            new[] { R(ClearanceStepKind.Cross, "15R", node: 5, passed: true) }, Hold("15R"), null));
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(
            new[] { R(ClearanceStepKind.Cross, "15R", node: 5, reach: true) }, Hold("15R"), null));
    }

    [Fact]
    public void The_same_kind_never_replaces()
    {
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R") }, Hold("15R"), null));
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Cross("15R") }, Cross("15R"), null));
    }

    [Fact]
    public void Taxiway_targets_and_via_steps_never_replace()
    {
        var holdTwy = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "N");
        var crossTwy = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Taxiway, "N");
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { holdTwy }, crossTwy, null));
        Assert.Equal(-1, ClearanceStepEntry.IndexReplacedOnAdd(new[] { Hold("15R") }, Via("N"), null));
    }

    [Fact]
    public void The_sentence_names_the_added_step_and_the_one_it_replaced()
    {
        Assert.Equal("Step 3: cross runway 15R, replacing hold short of runway 15R.",
            ClearanceStepEntry.ReplacedOnAdd(Cross("15R"), 3, Hold("15R")));
        Assert.Equal("Step 2: hold short of runway 15R, replacing cross runway 15R at N.",
            ClearanceStepEntry.ReplacedOnAdd(Hold("15R"), 2, Cross("15R", at: "N")));
    }

    [Fact]
    public void The_sentence_does_not_depend_on_the_culture()
        => Assert.Equal("Step 12: cross runway 15R, replacing hold short of runway 15R.",
            TestCulture.Run("de-DE", () => ClearanceStepEntry.ReplacedOnAdd(Cross("15R"), 12, Hold("15R"))));

    [Fact]
    public void A_progressive_leg_end_keeps_the_steps_typed_since_the_last_calculate()
    {
        Assert.Equal(new[] { 2, 3 }, ClearanceStepEntry.KeptAtProgressiveLegEnd(new[] { 0, 1, -1, -1 }));
        Assert.Equal(new[] { 0, 2 }, ClearanceStepEntry.KeptAtProgressiveLegEnd(new[] { -1, 0, -1 }));
        Assert.Empty(ClearanceStepEntry.KeptAtProgressiveLegEnd(new[] { 0, 1, 2 }));
        Assert.Empty(ClearanceStepEntry.KeptAtProgressiveLegEnd(Array.Empty<int>()));
    }
}
