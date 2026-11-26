// The Taxi Guidance window's Runway entry list (stage 4, D9-D15): one list in place of the "Intersection departure" and
// "Depart from named holding point" tick boxes. The list itself is pure and tested in RunwayEntryListTests; the window
// has no test harness, so this pins from its source where the list is filled, kept, read and reset.

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class RunwayEntryListWiringTests
{
    // D9: the two tick boxes, their lists and the maps behind them are gone, and with them the two "nothing to offer"
    // sentences (D14): with nothing to offer, the list holds Full length alone.
    [Fact]
    public void The_two_tick_boxes_and_their_lists_are_gone()
    {
        string form = FormSource();
        foreach (string gone in new[]
                 {
                     "chkIntersection", "cmbIntersection", "chkHoldingPoint", "cmbHoldingPoint",
                     "_intersectionMap", "_holdingPointMap", "PopulateIntersections", "PopulateHoldingPoints",
                     "OnIntersectionToggled", "OnHoldingPointToggled", "RestoredIntersectionIndex",
                     "No runway intersections available", "No named holding points available",
                 })
            Assert.DoesNotContain(gone, form);
    }

    // D9: one list, shown for a Runway destination only, where the boxes were: its label sits right before it in the
    // tab order, right after the destination.
    [Fact]
    public void One_runway_entry_list_follows_the_destination()
    {
        string form = FormSource();
        Assert.Contains("Text = \"Runway entry (&I):\"", form);
        Assert.Contains("AccessibleName = \"Runway entry\"", form);

        int dest = form.IndexOf("cmbDestination.TabIndex = tabIdx++;", StringComparison.Ordinal);
        int label = form.IndexOf("lblRunwayEntry.TabIndex = tabIdx++;", StringComparison.Ordinal);
        int list = form.IndexOf("cmbRunwayEntry.TabIndex = tabIdx++;", StringComparison.Ordinal);
        int cat = form.IndexOf("chkCatIiiHold.TabIndex = tabIdx++;", StringComparison.Ordinal);
        Assert.True(dest >= 0 && label > dest && list > label && cat > list,
            "Destination, then the Runway entry label and list, then the CAT III box");

        string typeChanged = Member(form, "private void OnDestTypeChanged(");
        Assert.Contains("lblRunwayEntry.Visible = isRunway;", typeChanged);
        Assert.Contains("cmbRunwayEntry.Visible = isRunway;", typeChanged);
    }

    // D10, D12: the list is built by RunwayEntryList from the same two graph calls the old lists made; a refill keeps
    // the choice by IndexToKeep and, when it is gone, falls back to Full length and says so once.
    [Fact]
    public void The_list_is_built_by_RunwayEntryList_and_a_refill_keeps_the_choice()
    {
        string form = FormSource();
        string build = Member(form, "private IReadOnlyList<RunwayEntryItem> BuildRunwayEntries(");
        Assert.Contains("_graph.GetRunwayIntersections(", build);
        Assert.Contains("ResolveHoldingPointsForRunway(destName)", build);
        Assert.Contains("RunwayEntryList.Build(", build);

        string populate = Member(form, "private void PopulateRunwayEntries(bool keepSelection)");
        Assert.Contains("BuildRunwayEntries()", populate);
        Assert.Contains("RunwayEntryList.IndexToKeep(", populate);
        Assert.Contains("_announcer.Announce(RunwayEntryList.NoLongerOffered(", populate);
    }

    // D12: picking a runway, a destination type change and an airport load rebuild the list at Full length; it is
    // refilled, keeping the choice, when it gets focus or drops down.
    [Fact]
    public void The_list_is_reset_on_a_runway_pick_and_refilled_on_focus_and_drop_down()
    {
        string form = FormSource();
        Assert.Contains("cmbRunwayEntry.Enter += (s, e) => PopulateRunwayEntries(keepSelection: true);", form);
        Assert.Contains("cmbRunwayEntry.DropDown += (s, e) => PopulateRunwayEntries(keepSelection: true);", form);

        int destChanged = form.IndexOf("cmbDestination.SelectedIndexChanged += (s, e) =>", StringComparison.Ordinal);
        Assert.True(destChanged >= 0, "the destination's SelectedIndexChanged handler not found");
        string handler = form[destChanged..form.IndexOf("};", destChanged, StringComparison.Ordinal)];
        int reset = handler.IndexOf("PopulateRunwayEntries(keepSelection: false);", StringComparison.Ordinal);
        int callout = handler.IndexOf("AnnounceDefaultHoldingPoint();", StringComparison.Ordinal);
        Assert.True(reset >= 0 && callout > reset, "the list is back at Full length before the default call-out");

        Assert.Contains("PopulateRunwayEntries(keepSelection: false);", Member(form, "private void OnDestTypeChanged("));
        Assert.Contains("PopulateRunwayEntries(keepSelection: false);", Member(form, "private async Task LoadAirportDataCoreAsync("));
    }

    // D13: Calculate maps the choice to today's two branches, with today's LoadRoute arguments and sentences;
    // backtrack keeps its precedence.
    [Fact]
    public void Calculate_maps_the_choice_to_the_intersection_and_holding_point_branches()
    {
        string calculate = Member(FormSource(), "private void OnCalculateClicked(");
        Assert.Contains("SelectedRunwayEntry", calculate);

        int ix = calculate.IndexOf("RunwayEntryKind.Intersection", StringComparison.Ordinal);
        int hp = calculate.IndexOf("RunwayEntryKind.HoldingPoint", StringComparison.Ordinal);
        Assert.True(ix >= 0 && hp > ix, "the intersection branch, then the holding point branch");
        string ixBranch = calculate[ix..hp];
        Assert.Contains("!backtrackActive", ixBranch);
        Assert.Contains("destNodeId = ix.NodeId;", ixBranch);
        Assert.Contains("thresholdLat = ix.Latitude;", ixBranch);
        int hpIf = calculate.LastIndexOf("if (", hp, StringComparison.Ordinal);
        string hpBranch = calculate[hpIf..calculate.IndexOf("_guidanceManager.LoadRoute(", hp, StringComparison.Ordinal)];
        Assert.Contains("!backtrackActive && intersection == null", hpBranch);
        Assert.Contains("destNodeId = hp.NodeId;", hpBranch);
        Assert.Contains("Partway", hpBranch);
        Assert.Contains("thresholdLat = hp.Latitude;", hpBranch);

        Assert.Contains("holdingPointHoldNodeId: holdingPointEntry?.HoldNodeId,", calculate);
        Assert.Contains("holdingPointName: holdingPointEntry?.TaxiwayName,", calculate);
        Assert.Contains("intersectionDeparture: intersection != null,", calculate);
        Assert.Contains("$\"Intersection {intersection.TaxiwayName} departure, {rwyLabel}. \"", calculate);
        Assert.Contains("$\"Holding point {holdingPointEntry.TaxiwayName}, {rwyLabel}. \"", calculate);
    }

    // D14: the default and CAT III call-outs speak only while the choice is Full length.
    [Fact]
    public void The_default_call_out_is_silent_for_any_other_entry()
    {
        string callout = Member(FormSource(), "private void AnnounceDefaultHoldingPoint(");
        Assert.Contains("SelectedRunwayEntry is { Kind: not RunwayEntryKind.FullLength }", callout);
    }

    // D15 [SI-26], [SI-23]: the reset before a fill selects Full length; a failed probe saves and puts back the runway
    // entry choice (a holding point too), directly and silently.
    [Fact]
    public void SayIntentions_resets_and_restores_the_runway_entry_choice()
    {
        string form = FormSource();
        Assert.Contains("SelectFullLengthRunwayEntry();", Member(form, "private void ResetRouteShapingControls("));

        string probe = Member(form, "public bool TryResolveExternalDestination(");
        Assert.Contains("RunwayEntryItem? priorRunwayEntry = SelectedRunwayEntry;", probe);
        Assert.Contains("priorRunwayEntry, priorCatIiiHold);", probe);

        Assert.Contains("RestoreRunwayEntryState(priorRunwayEntry);", Member(form, "private void RestoreDestinationState("));
        string restore = Member(form, "private void RestoreRunwayEntryState(");
        Assert.Contains("RestoredRunwayEntryIndex(", restore);
        Assert.DoesNotContain("_announcer", restore);
        Assert.DoesNotContain("Focus()", restore);
    }

    private static string FormSource()
        => File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

    // A member's text: from its declaration to the next member at class level.
    private static string Member(string text, string declaration)
    {
        int start = text.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{declaration} not found");
        var next = Regex.Match(text[(start + 1)..], @"\n    (private|public|internal|protected) ");
        return next.Success ? text.Substring(start, next.Index + 1) : text[start..];
    }
}
