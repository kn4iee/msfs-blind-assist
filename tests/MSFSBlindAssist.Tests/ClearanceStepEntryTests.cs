// Tests for MSFSBlindAssist.Navigation.ClearanceStepEntry: the Taxi Guidance window's step block as data (the step
// kinds, the name box's label, the Alt+letter kinds, the refusal with nothing chosen, and the step a pick makes), and
// the window's access keys, read from its source.

using System.Text.RegularExpressions;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceStepEntryTests
{
    private static ClearanceNameLists.Item Taxiway(string name) => new(name, ClearanceTargetKind.Taxiway, name);

    private static ClearanceNameLists.Item Runway(string name) => new(name, ClearanceTargetKind.Runway, name);

    private static NamedHoldingPoint Point(string name, int node, string kind = "runway")
        => new() { Name = name, NodeId = node, Kind = kind };

    [Fact]
    public void The_kinds_are_listed_in_order_each_with_its_own_first_letter()
    {
        Assert.Equal(new[] { "Via taxiway", "Left onto taxiway", "Right onto taxiway", "Cross", "Hold short of" },
            ClearanceStepEntry.KindItems);
        Assert.Equal(ClearanceStepEntry.KindItems.Count,
            ClearanceStepEntry.KindItems.Select(k => char.ToUpperInvariant(k[0])).Distinct().Count());
        // Index for index with the kinds the planner reads.
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(0));
        Assert.Equal(ClearanceStepKind.LeftOnto, ClearanceStepEntry.KindAt(1));
        Assert.Equal(ClearanceStepKind.RightOnto, ClearanceStepEntry.KindAt(2));
        Assert.Equal(ClearanceStepKind.Cross, ClearanceStepEntry.KindAt(3));
        Assert.Equal(ClearanceStepKind.HoldShort, ClearanceStepEntry.KindAt(4));
    }

    [Fact]
    public void SameAsEntered_ignores_hidden_data()
    {
        var entered = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", At: "P",
            AtNodeId: 5, AtNodeIsPassedStop: true);
        Assert.True(ClearanceStepEntry.SameAsEntered(entered,
            ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "P")));
        Assert.False(ClearanceStepEntry.SameAsEntered(entered,
            ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "Q")));
        Assert.True(ClearanceStepEntry.SameAsEntered(
            ClearanceStepEntry.Build(ClearanceStepKind.Via, Taxiway("C")),
            ClearanceStepEntry.Build(ClearanceStepKind.Via, Taxiway("c"))));
        var seven = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item("L2", ClearanceTargetKind.HoldingPoint, "L2", Point("L2", 7)));
        var eight = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item("L2", ClearanceTargetKind.HoldingPoint, "L2", Point("L2", 8)));
        Assert.False(ClearanceStepEntry.SameAsEntered(seven, eight));
    }

    // Review Focus 3: a crossing put back by the Hold short key reads "Hold short of runway 24L at P" and keeps the
    // crossing's place (its At and node, ClearanceTrim.AsHoldShort). The Hold short of runway step has an At box now, so
    // "replacing" it with no place is another step, and with its own place (BuildForChange) the same one: the window keeps
    // the record, with its node.
    [Fact]
    public void SameAsEntered_compares_a_put_back_hold_shorts_place()
    {
        var putBack = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "24L", At: "P", AtNodeId: 9);
        Assert.False(ClearanceStepEntry.SameAsEntered(putBack,
            ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("24L"))));
        Assert.True(ClearanceStepEntry.SameAsEntered(putBack,
            ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("24L"), "P")));
        Assert.False(ClearanceStepEntry.SameAsEntered(putBack,
            ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("24R"))));
        Assert.False(ClearanceStepEntry.SameAsEntered(putBack,
            ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "P")));
    }

    // A crossing already made, pinned by the trim (ClearanceTrim's PinnedToMadeCrossing): its place, holding point B3, its
    // node the stop it passed.
    private static readonly ClearanceStep MadeAtB3 = new(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L",
        At: "B3", AtNodeId: 9, AtNodeIsPassedStop: true);

    // Task 2 fix round 1 (Review Focus 3): once across, the At box lists only holding points on the aircraft's side, so a
    // crossing made at B3 is never offered B3 again; without its own place the boxes rebuilt "Cross runway 24L" with no
    // place, the pin was lost, and the next Calculate cleared the crossing back. While the step is being changed and the
    // name box still holds its runway, the box lists the step's own place, labelled as the box labels it.
    [Fact]
    public void OwnPlaceLabel_lists_the_place_of_the_step_being_changed_when_it_is_not_offered()
    {
        Assert.Equal("B3 (holding point)", ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, "24L", placeOffered: false, placeIsHoldingPoint: true));
        Assert.Equal("B3", ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, "24l", placeOffered: false, placeIsHoldingPoint: false));
        // Offered already, another runway chosen, no place, not a runway crossing: nothing added.
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, "24L", placeOffered: true, placeIsHoldingPoint: true));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, "06", placeOffered: false, placeIsHoldingPoint: true));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, null, placeOffered: false, placeIsHoldingPoint: true));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(MadeAtB3 with { At = null }, "24L", placeOffered: false, placeIsHoldingPoint: false));
        // A Hold short of taxiway carries no place: nothing added (a Hold short of runway does, see below).
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "24L",
            At: "B3", AtNodeId: 9), "24L", placeOffered: false, placeIsHoldingPoint: true));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Taxiway, "24L"),
            "24L", placeOffered: false, placeIsHoldingPoint: false));
    }

    [Fact]
    public void Picking_the_steps_own_place_rebuilds_it_with_its_place_so_the_record_is_kept()
    {
        string own = ClearanceStepEntry.OwnPlaceLabel(MadeAtB3, "24L", placeOffered: false, placeIsHoldingPoint: true)!;
        var rebuilt = ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("24L"), own, null, MadeAtB3, own);
        Assert.Equal("B3", rebuilt.At);
        Assert.Equal(9, rebuilt.AtNodeId);
        Assert.True(ClearanceStepEntry.SameAsEntered(MadeAtB3, rebuilt));
        // The plain build of the same picks, what the window did before: the place is lost.
        Assert.False(ClearanceStepEntry.SameAsEntered(MadeAtB3, ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), null)));

        // Any other pick builds as Add Step does.
        Assert.Equal(ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "C"),
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("24L"), "C", null, MadeAtB3, own));
        Assert.Equal(ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), ClearanceStepEntry.NoAtPlace),
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("24L"), ClearanceStepEntry.NoAtPlace, null, MadeAtB3, own));
        Assert.Equal(ClearanceStepEntry.Build(ClearanceStepKind.Via, Taxiway("D")),
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.Via, Taxiway("D"), null, null, MadeAtB3, own));
        // Another runway never keeps the old step's place (the box offers that place only for the step's own runway).
        var otherRunway = ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("06"), own, null, MadeAtB3, own);
        Assert.Equal("06", otherRunway.Name);
        Assert.Equal(0, otherRunway.AtNodeId);
        // No own place listed: as Add Step.
        Assert.Equal(ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), null),
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("24L"), null, null, MadeAtB3, null));
    }

    [Fact]
    public void A_crossings_place_is_a_holding_point_when_pinned_to_one_or_named_only_like_one()
    {
        string[] taxiways = { "B", "B3", "C" };
        string[] points = { "B3", "K2" };
        // Pinned to a holding point's node.
        Assert.True(ClearanceStepEntry.PlaceIsHoldingPoint(
            new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", At: "B3", AtNodeId: 4), taxiways, points));
        // A crossing made: its node is the stop passed, so the name decides; a taxiway's name stays a taxiway.
        Assert.True(ClearanceStepEntry.PlaceIsHoldingPoint(MadeAtB3 with { At = "k2" }, taxiways, points));
        Assert.False(ClearanceStepEntry.PlaceIsHoldingPoint(MadeAtB3, taxiways, points));
        Assert.False(ClearanceStepEntry.PlaceIsHoldingPoint(MadeAtB3 with { At = "C" }, taxiways, points));
        Assert.False(ClearanceStepEntry.PlaceIsHoldingPoint(MadeAtB3 with { At = null }, taxiways, points));
    }

    // Task 2 fix round 1 (spec): a name no longer offered is left unselected. A holding point is found by its node only,
    // never by its name: a point of the same name at another node would move the hold with the same read-back.
    [Fact]
    public void A_steps_name_item_is_a_holding_point_by_its_node_only()
    {
        var atNode7 = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item("L2 (holding point)", ClearanceTargetKind.HoldingPoint, "L2", Point("L2", 7)));
        Assert.True(ClearanceStepEntry.NameItemMatches(
            new ClearanceNameLists.Item("L2 (holding point)", ClearanceTargetKind.HoldingPoint, "L2", Point("L2", 7)), atNode7));
        Assert.False(ClearanceStepEntry.NameItemMatches(
            new ClearanceNameLists.Item("L2 (holding point)", ClearanceTargetKind.HoldingPoint, "L2", Point("L2", 8)), atNode7));
        Assert.True(ClearanceStepEntry.NameItemMatches(
            new ClearanceNameLists.Item("L2A (holding point)", ClearanceTargetKind.HoldingPoint, "L2A", Point("L2A", 7)), atNode7));
        Assert.False(ClearanceStepEntry.NameItemMatches(Taxiway("L2"), atNode7));

        var viaC = ClearanceStepEntry.Build(ClearanceStepKind.Via, Taxiway("C"));
        Assert.True(ClearanceStepEntry.NameItemMatches(Taxiway("c"), viaC));
        Assert.False(ClearanceStepEntry.NameItemMatches(Runway("C"), viaC));
        Assert.True(ClearanceStepEntry.NameItemMatches(Runway("24L"),
            ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("24L"))));
    }

    // An "At" item names the step's place only when picking it rebuilds the same place (SameAsEntered holds): a holding
    // point by its name, a taxiway by its label unless the step is pinned to a holding point's node.
    [Fact]
    public void An_At_item_names_the_steps_place_only_when_picking_it_rebuilds_the_same_place()
    {
        var atTaxiwayB3 = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "B3");
        var atPointB3 = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), "B3 (holding point)", Point("B3", 4));
        Assert.True(ClearanceStepEntry.AtItemMatches("b3", null, atTaxiwayB3));
        Assert.False(ClearanceStepEntry.AtItemMatches("B3", null, atPointB3));
        Assert.True(ClearanceStepEntry.AtItemMatches("B3 (holding point)", Point("B3", 4), atPointB3));
        // The same node renamed is another place: picking it would rebuild another name.
        Assert.False(ClearanceStepEntry.AtItemMatches("B3A (holding point)", Point("B3A", 4), atPointB3));
        // A crossing made at B3: its node is the stop passed, so a taxiway B3 still names it.
        Assert.True(ClearanceStepEntry.AtItemMatches("B3", null, MadeAtB3));
        Assert.True(ClearanceStepEntry.AtItemMatches("B3 (holding point)", Point("B3", 12), MadeAtB3));
        Assert.False(ClearanceStepEntry.AtItemMatches(ClearanceStepEntry.NoAtPlace, null, MadeAtB3));
    }

    // Final review, Important 1: the At box set the step's place only as the change opened. Every later refill with no
    // pick to keep landed on "(none)": the name box arrowed away from 24L and back (how a screen reader browses a closed
    // list), a same-airport reload's re-pick, or the step's own place relabelled. Enter then rebuilt "Cross runway 24L"
    // with no place, a new step replaced the old one, and a made crossing lost its pin. With the step's own runway chosen
    // the box goes back to the step's own place; another runway starts on "(none)".
    private static readonly string[] AtBoxFor24L = { ClearanceStepEntry.NoAtPlace, "B", "C", "B3 (holding point)" };

    [Fact]
    public void A_refilled_At_box_goes_back_to_the_place_of_the_crossing_being_changed()
    {
        // The same runway picked again, no pick kept: the step's own place (listed for the change at 3).
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, previous: null, previousOwnLabel: "B3 (holding point)",
            MadeAtB3, chosenRunway: "24L", ownPlaceIndex: 3));
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, MadeAtB3, "24l", 3));
        // A crossing with no place: its place is "(none)".
        var noPlace = MadeAtB3 with { At = null, AtNodeId = 0, AtNodeIsPassedStop = false };
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, noPlace, "24L", 0));
        // Nothing names the place: nothing selected, as when the change opens.
        Assert.Equal(-1, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, MadeAtB3, "24L", -1));
        // An empty box selects nothing.
        Assert.Equal(-1, ClearanceStepEntry.AtIndexAfterRefill(Array.Empty<string>(), null, null, MadeAtB3, "24L", 3));
    }

    [Fact]
    public void Another_runway_or_no_runway_crossing_being_changed_starts_the_At_box_on_none()
    {
        string[] for06 = { ClearanceStepEntry.NoAtPlace, "B3", "D" };
        // Another runway: "(none)", even where a taxiway there shares the step's place name.
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(for06, null, "B3 (holding point)", MadeAtB3, "06", ownPlaceIndex: 1));
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(for06, null, null, MadeAtB3, null, 1));
        // No change open, or the step being changed is no runway crossing: "(none)", as Add Step always did.
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, null, "24L", -1));
        var viaC = ClearanceStepEntry.Build(ClearanceStepKind.Via, Taxiway("C"));
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, viaC, "24L", 2));
        var holdShortTaxiway = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "24L", At: "C", AtNodeId: 9);
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, holdShortTaxiway, "24L", 2));
        // A pick still listed stays, whatever runway.
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(for06, "D", null, MadeAtB3, "06", 1));
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, "C", null, null, "24L", -1));
    }

    // The step's own place listed as "B3" before the holding points were known reads "B3 (holding point)" once they land
    // (the box's refresh as focus arrives or its list opens): the pick follows the place to its new label.
    [Fact]
    public void The_steps_own_place_picked_follows_it_to_a_new_label()
    {
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, previous: "B3", previousOwnLabel: "B3",
            MadeAtB3, "24L", ownPlaceIndex: 3));
        // Now offered as one of the box's own holding points: that item.
        string[] offered = { ClearanceStepEntry.NoAtPlace, "B", "B3 (holding point)", "K2 (holding point)" };
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(offered, "B3", "B3", MadeAtB3, "24L", 2));
        // Even where its old label is still listed as something else: the pick was the step's own place.
        string[] oldLabelListed = { ClearanceStepEntry.NoAtPlace, "B3", "B3 (holding point)" };
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(oldLabelListed, "B3", "B3", MadeAtB3, "24L", 2));
    }

    // A pick the pilot made in the At box and still listed stays, "(none)" chosen on purpose included; one no longer
    // listed gives way to the step's own place.
    [Fact]
    public void A_pick_the_pilot_made_in_the_At_box_is_kept_while_it_is_listed()
    {
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, ClearanceStepEntry.NoAtPlace, "B3 (holding point)",
            MadeAtB3, "24L", ownPlaceIndex: 3));
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, "C", "B3 (holding point)", MadeAtB3, "24L", 3));
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, "B3 (holding point)", "B3 (holding point)",
            MadeAtB3, "24L", 3));
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, "D", "B3 (holding point)", MadeAtB3, "24L", 3));
    }

    [Fact]
    public void Replace_texts()
    {
        Assert.Equal("Replace step 3 (&D)", ClearanceStepEntry.ReplaceButtonText(3));
        Assert.Equal("Replace step 3", ClearanceStepEntry.ReplaceButtonName(3));
        Assert.Equal("Taxiway, changing step 3", ClearanceStepEntry.NameBoxAccessibleName(ClearanceStepKind.Via, 3));
        Assert.Equal("Step 3 is still being changed. Press Enter to replace it, or Escape to keep it.",
            ClearanceStepEntry.StillChanging(3));
    }

    [Fact]
    public void Nothing_selected_in_the_kind_list_reads_as_via()
    {
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(-1));
        Assert.Equal(ClearanceStepKind.Via, ClearanceStepEntry.KindAt(5));
    }

    [Theory]
    [InlineData('V', ClearanceStepKind.Via)]
    [InlineData('v', ClearanceStepKind.Via)]
    [InlineData('L', ClearanceStepKind.LeftOnto)]
    [InlineData('l', ClearanceStepKind.LeftOnto)]
    [InlineData('R', ClearanceStepKind.RightOnto)]
    [InlineData('r', ClearanceStepKind.RightOnto)]
    [InlineData('X', ClearanceStepKind.Cross)]
    [InlineData('x', ClearanceStepKind.Cross)]
    [InlineData('H', ClearanceStepKind.HoldShort)]
    [InlineData('h', ClearanceStepKind.HoldShort)]
    public void Each_kind_has_its_own_access_key(char letter, ClearanceStepKind kind)
        => Assert.Equal(kind, ClearanceStepEntry.KindForMnemonic(letter));

    // A function or number-pad key makes no character, so the access-key message never carries one:
    // there is no key-code case left to pin.
    [Theory]
    [InlineData('k')]
    [InlineData('W')]
    [InlineData('d')]
    [InlineData('C')]
    [InlineData('3')]
    public void Other_letters_pick_no_kind(char letter)
        => Assert.Null(ClearanceStepEntry.KindForMnemonic(letter));

    [Theory]
    [InlineData(ClearanceStepKind.Via, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.LeftOnto, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.RightOnto, "Taxi&way:", "Taxiway")]
    [InlineData(ClearanceStepKind.Cross, "Runway or taxi&way to cross:", "Runway or taxiway to cross")]
    [InlineData(ClearanceStepKind.HoldShort, "Hold short of runway or taxi&way:", "Hold short of runway, taxiway or holding point")]
    public void The_name_box_follows_the_kind_and_keeps_Alt_W(ClearanceStepKind kind, string label, string accessibleName)
    {
        Assert.Equal(label, ClearanceStepEntry.NameBoxLabel(kind));
        Assert.Equal(accessibleName, ClearanceStepEntry.NameBoxAccessibleName(kind));
        Assert.Equal(1, label.Count(c => c == '&'));
        Assert.False(string.IsNullOrWhiteSpace(ClearanceStepEntry.NameBoxDescription(kind)));
    }

    // Review focus 1: Enter in the name box with nothing chosen adds no step, and says why.
    [Fact]
    public void Enter_with_nothing_chosen_is_refused_with_the_reason()
    {
        Assert.Equal("Choose a name first.", ClearanceStepEntry.Refusal(null));
        Assert.Null(ClearanceStepEntry.Refusal(Taxiway("B")));
    }

    // Remove Step on an empty list is refused out loud.
    [Fact]
    public void Remove_step_on_an_empty_list_says_so()
        => Assert.Equal("No step to remove.", ClearanceStepEntry.NoStepToRemove);

    // Remove Step and Clear Steps pressed by their buttons or access keys (focus not in the Clearance list) confirm once:
    // the button's access key clicks it without moving focus, so the screen reader says nothing of the change.
    [Fact]
    public void A_removed_step_is_said_with_its_number_and_sentence()
    {
        var cross = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L");
        var at = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", At: "P");
        var left = new ClearanceStep(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Taxiway, "C");

        Assert.Equal("Removed step 3: cross runway 24L.", TestCulture.Run("de-DE", () => ClearanceStepEntry.Removed(cross, 3)));
        Assert.Equal("Removed step 12: cross runway 24L at P.", TestCulture.Run("sv-SE", () => ClearanceStepEntry.Removed(at, 12)));
        Assert.Equal("Removed step 1: left onto C.", TestCulture.Run("de-DE", () => ClearanceStepEntry.Removed(left, 1)));
    }

    [Fact]
    public void Clearing_the_steps_is_said()
        => Assert.Equal("Steps cleared.", ClearanceStepEntry.StepsCleared);

    // Task 7 ruling 5 ([CORE-7]): a removal that leaves the list empty says "Steps cleared." whatever the focus, as an
    // empty list gives the screen reader nothing to say; from the Clearance list a removal that leaves a step says
    // nothing (the reader reads the step now in its place); from anywhere else it says which step went.
    [Fact]
    public void A_removal_says_once_what_the_screen_reader_cannot()
    {
        var cross = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", At: "P");

        Assert.Null(ClearanceStepEntry.RemovalSentence(cross, 2, fromList: true, listNowEmpty: false));
        Assert.Equal("Steps cleared.",
            TestCulture.Run("de-DE", () => ClearanceStepEntry.RemovalSentence(cross, 1, fromList: true, listNowEmpty: true)));
        Assert.Equal("Removed step 2: cross runway 24L at P.",
            TestCulture.Run("sv-SE", () => ClearanceStepEntry.RemovalSentence(cross, 2, fromList: false, listNowEmpty: false)));
        Assert.Equal("Removed step 1: cross runway 24L at P. Steps cleared.",
            TestCulture.Run("de-DE", () => ClearanceStepEntry.RemovalSentence(cross, 1, fromList: false, listNowEmpty: true)));
    }

    // Final review, minor ([CORE-7]): the end of a leg empties the list silently (guidance says the leg's end), but steps
    // the pilot typed since the last Calculate (origin -1) go with it, and with the window focused they would vanish
    // under the pilot's hands unsaid: "Steps cleared." once. With the window elsewhere, or only steps of the leg flown,
    // nothing.
    [Fact]
    public void A_leg_end_says_steps_cleared_only_for_steps_typed_since_the_last_Calculate_in_the_focused_window()
    {
        Assert.Equal("Steps cleared.", TestCulture.Run("de-DE",
            () => ClearanceStepEntry.LegEndSentence(new[] { 0, 1, -1 }, windowFocused: true)));
        Assert.Null(ClearanceStepEntry.LegEndSentence(new[] { 0, 1, -1 }, windowFocused: false));
        Assert.Null(ClearanceStepEntry.LegEndSentence(new[] { 0, 1, 2 }, windowFocused: true));
        Assert.Null(ClearanceStepEntry.LegEndSentence(Array.Empty<int>(), windowFocused: true));
    }

    [Theory]
    [InlineData(ClearanceStepKind.Via, "Via B")]
    [InlineData(ClearanceStepKind.LeftOnto, "Left onto B")]
    [InlineData(ClearanceStepKind.RightOnto, "Right onto B")]
    public void A_via_kind_names_a_taxiway(ClearanceStepKind kind, string sentence)
    {
        var step = ClearanceStepEntry.Build(kind, Taxiway("B"));
        Assert.Equal(kind, step.Kind);
        Assert.Equal(ClearanceTargetKind.Taxiway, step.TargetKind);
        Assert.Equal("B", step.Name);
        Assert.Equal(sentence, step.Sentence());
    }

    [Fact]
    public void A_crossing_at_a_taxiway_is_said_with_its_place()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), atLabel: "P");

        Assert.Equal(ClearanceTargetKind.Runway, step.TargetKind);
        Assert.Equal("P", step.At);
        Assert.Equal(0, step.AtNodeId);
        Assert.Equal("Step 2: cross runway 24L at P.", TestCulture.Run("de-DE", () => step.Spoken(2)));
        Assert.Equal("Step 2: cross runway 24L at P.", TestCulture.Run("sv-SE", () => step.Spoken(2)));
    }

    // ---- Hold short of runway Y at Z ----

    [Fact]
    public void Build_hold_short_of_runway_takes_its_At()
    {
        var taxiway = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "B");
        Assert.Equal(ClearanceTargetKind.Runway, taxiway.TargetKind);
        Assert.Equal("B", taxiway.At);
        Assert.Equal(0, taxiway.AtNodeId);

        var point = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"),
            "A2" + HoldShortTargetList.HoldingPointSuffix, Point("A2", 7));
        Assert.Equal("A2", point.At);
        Assert.Equal(7, point.AtNodeId);

        Assert.Null(ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), ClearanceStepEntry.NoAtPlace).At);
        Assert.Null(ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L")).At);
        // A taxiway or a holding point held short of has no At box.
        Assert.Null(ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Taxiway("B"), "C").At);
    }

    [Fact]
    public void Hold_short_of_runway_reads_its_place()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "B");
        Assert.Equal("Hold short of runway 27L at B", step.Sentence());
        Assert.Equal("Step 2: hold short of runway 27L at B.", TestCulture.Run("de-DE", () => step.Spoken(2)));
        Assert.Equal("Hold short of runway 27L at A2", ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"),
            "A2", Point("A2", 7)).Sentence());
        Assert.Equal("Hold short of runway 27L", ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L")).Sentence());
    }

    [Fact]
    public void SameAsEntered_compares_a_runway_hold_shorts_place()
    {
        var atB = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "B");
        Assert.False(ClearanceStepEntry.SameAsEntered(atB, ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "C")));
        Assert.True(ClearanceStepEntry.SameAsEntered(atB, ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "b")));

        // Review Focus 3: a put-back crossing changed with its own place is the same step, hidden node kept.
        var putBack = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "04L", At: "C", AtNodeId: 5);
        string own = ClearanceStepEntry.OwnPlaceLabel(putBack, "04L", placeOffered: false, placeIsHoldingPoint: true)!;
        Assert.Equal("C (holding point)", own);
        var rebuilt = ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort, Runway("04L"), own, null, putBack, own);
        Assert.Equal(putBack, rebuilt);
        Assert.True(ClearanceStepEntry.SameAsEntered(putBack, rebuilt));
        // A Cross rebuilt from a hold short's own place is not it (kind differs).
        Assert.NotEqual(ClearanceStepKind.Cross, rebuilt.Kind);
        Assert.Equal(ClearanceStepKind.Cross, ClearanceStepEntry.BuildForChange(ClearanceStepKind.Cross, Runway("04L"), own, null, putBack, own).Kind);
    }

    [Fact]
    public void Own_place_and_refill_apply_to_a_runway_hold_short_as_to_a_crossing()
    {
        var held = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "24L", At: "B3", AtNodeId: 9);
        Assert.Equal("B3 (holding point)", ClearanceStepEntry.OwnPlaceLabel(held, "24L", false, true));
        Assert.Equal("B3", ClearanceStepEntry.OwnPlaceLabel(held, "24l", false, false));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(held, "06", false, true));
        Assert.Equal(3, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, held, "24L", 3));
        Assert.Equal(0, ClearanceStepEntry.AtIndexAfterRefill(AtBoxFor24L, null, null, held, "06", 3));
    }

    // Task 3: the window shows its At box for Hold short of with a runway chosen, as for Cross, and for nothing else: a
    // taxiway or a holding point held short of, a taxiway crossed, a taxiway followed, nothing chosen.
    [Fact]
    public void The_At_box_shows_for_a_runway_crossed_or_held_short_of_only()
    {
        Assert.True(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.Cross, ClearanceTargetKind.Runway));
        Assert.True(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.Cross, ClearanceTargetKind.Taxiway));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.LeftOnto, ClearanceTargetKind.Runway));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.Cross, null));
        Assert.False(ClearanceStepEntry.ShowsAtBox(ClearanceStepKind.HoldShort, null));
    }

    // The At box's description says what the place is for the kind chosen: Cross's, as it always was, and Hold short of's.
    [Fact]
    public void The_At_box_description_follows_the_kind()
    {
        Assert.Equal("Optional: where ATC cleared you to cross the runway, a taxiway or a holding point such as P1. Lists only those that cross the runway chosen in the name box. Leave at (none) to cross where the route first meets the runway.",
            ClearanceStepEntry.AtBoxDescription(ClearanceStepKind.Cross));
        Assert.Equal("Optional: where ATC told you to hold short of the runway, a taxiway or a holding point such as A2. Lists only those that cross the runway chosen in the name box. Leave at (none) to hold short where the route first meets the runway.",
            ClearanceStepEntry.AtBoxDescription(ClearanceStepKind.HoldShort));
    }

    // Task 3: Enter on "Hold short of runway 27L at B" in the Clearance list loads B into the At box (the box shows for
    // it, and B is the item a pick of which rebuilds the same place), a refill keeps it, and Enter again keeps the step.
    [Fact]
    public void A_change_of_a_hold_short_of_runway_at_B_loads_B_and_keeps_it()
    {
        var atB = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "B");
        Assert.True(ClearanceStepEntry.ShowsAtBox(atB.Kind, atB.TargetKind));
        string[] items = { ClearanceStepEntry.NoAtPlace, "A", "B", "C", "A2 (holding point)" };
        Assert.True(ClearanceStepEntry.AtItemMatches("B", null, atB));
        Assert.False(ClearanceStepEntry.AtItemMatches("A2 (holding point)", Point("A2", 7), atB));
        // B is offered, so no own place is added.
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(atB, "27L", placeOffered: true, placeIsHoldingPoint: false));
        // The change opens with B selected (BeginStepChange's refill keeps no pick), and a refill keeping the pick keeps B.
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(items, null, null, atB, "27L", ownPlaceIndex: 2));
        Assert.Equal(2, ClearanceStepEntry.AtIndexAfterRefill(items, "B", null, atB, "27L", ownPlaceIndex: 2));
        var rebuilt = ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort, Runway("27L"), items[2], null, atB, null);
        Assert.Equal(atB, rebuilt);
        Assert.True(ClearanceStepEntry.SameAsEntered(atB, rebuilt));
        // Another place picked is another step.
        Assert.False(ClearanceStepEntry.SameAsEntered(atB,
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort, Runway("27L"), "C", null, atB, null)));

        // At a holding point: its labelled item, by the point's name.
        var atA2 = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "A2 (holding point)", Point("A2", 7));
        Assert.True(ClearanceStepEntry.AtItemMatches("A2 (holding point)", Point("A2", 7), atA2));
        Assert.False(ClearanceStepEntry.AtItemMatches("A2", null, atA2));
        Assert.True(ClearanceStepEntry.SameAsEntered(atA2, ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort,
            Runway("27L"), "A2 (holding point)", Point("A2", 7), atA2, null)));
    }

    // Review Focus 3, carried from Task 1: a crossing put back by Input Alt+Y reads "Hold short of runway 04L at C" with
    // the crossing's hidden data. Changed with Enter and then Enter again, the box shows for it, lists its own place when
    // C is behind the aircraft (not offered), opens on that place, and rebuilds the very record: nothing is lost.
    [Theory]
    [InlineData("C", 0, "C")]                       // cleared at taxiway C
    [InlineData("C3", 41, "C3 (holding point)")]    // cleared at holding point C3, its node kept
    public void A_put_back_crossing_changed_with_Enter_and_Enter_again_keeps_its_place_and_hidden_data(
        string place, int node, string ownLabel)
    {
        var entered = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "04L", At: place, AtNodeId: node);
        var putBack = ClearanceTrim.AsHoldShort(entered);
        Assert.Equal("Hold short of runway 04L at " + place, putBack.Sentence());
        Assert.True(ClearanceStepEntry.ShowsAtBox(putBack.Kind, putBack.TargetKind));

        // Its place not offered (behind the aircraft): listed as the step's own place, the change opening on it.
        var items = new List<string> { ClearanceStepEntry.NoAtPlace, "A", "B" };
        string own = ClearanceStepEntry.OwnPlaceLabel(putBack, "04L", placeOffered: false,
            ClearanceStepEntry.PlaceIsHoldingPoint(putBack, new[] { "A", "B", "C" }, new[] { "C3" }))!;
        Assert.Equal(ownLabel, own);
        items.Add(own);
        int opened = ClearanceStepEntry.AtIndexAfterRefill(items, null, null, putBack, "04L", ownPlaceIndex: items.Count - 1);
        Assert.Equal(own, items[opened]);
        var rebuilt = ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort, Runway("04L"), items[opened], null, putBack, own);
        Assert.Equal(putBack, rebuilt);
        Assert.True(ClearanceStepEntry.SameAsEntered(putBack, rebuilt));
        // Without the box (as before this task) the place was lost and the step replaced.
        Assert.False(ClearanceStepEntry.SameAsEntered(putBack, ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("04L"))));
    }

    // The same put-back crossing with its taxiway offered: picking it says the same, so the window keeps the old record
    // (ReplaceStepFromInputs), hidden data and all.
    [Fact]
    public void A_put_back_crossing_at_an_offered_taxiway_is_kept_by_picking_it()
    {
        var putBack = ClearanceTrim.AsHoldShort(new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "04L", At: "C"));
        Assert.True(ClearanceStepEntry.AtItemMatches("C", null, putBack));
        Assert.True(ClearanceStepEntry.SameAsEntered(putBack,
            ClearanceStepEntry.BuildForChange(ClearanceStepKind.HoldShort, Runway("04L"), "C", null, putBack, null)));
    }

    // Task 1's deferred minor: a blank At is no place, wherever a step's place is read (the sentence, the window's
    // comparison and item match, the planner's runway pick), as Build already makes one.
    [Fact]
    public void A_blank_At_is_no_place_everywhere()
    {
        var hold = new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Runway, "27L", At: "  ");
        var cross = new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, "24L", At: " ");
        Assert.Equal("Hold short of runway 27L", hold.Sentence());
        Assert.Equal("Cross runway 24L", cross.Sentence());
        Assert.True(ClearanceStepEntry.SameAsEntered(hold, ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"))));
        Assert.True(ClearanceStepEntry.SameAsEntered(cross, ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("24L"), ClearanceStepEntry.NoAtPlace)));
        Assert.False(ClearanceStepEntry.AtItemMatches("  ", null, hold));
        Assert.False(ClearanceStepEntry.PlaceIsHoldingPoint(hold, Array.Empty<string>(), new[] { "  " }));
        Assert.Null(ClearanceStepEntry.OwnPlaceLabel(hold, "27L", placeOffered: false, placeIsHoldingPoint: false));
        var plan = ClearancePlanner.Build(new[] { new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "A"), hold },
            hasDestination: true);
        Assert.Null(plan.RunwayHoldPicks()![0].Value.At);
    }

    // Task 3: at Calculate the window writes the steps and the destination to the taxi_router log, so a report can be
    // read from the logs: the destination type as listed, the destination when there is one, the steps as they read.
    [Fact]
    public void The_log_line_names_the_destination_and_the_steps_as_they_read()
    {
        var steps = new[]
        {
            new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "5"),
            new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.Taxiway, "AK"),
        };
        Assert.Equal("Calculate: Progressive Taxi (no destination); steps: Via 5 | Hold short of taxiway AK",
            TestCulture.Run("de-DE", () => ClearanceStepEntry.LogLine(steps, "Progressive Taxi (no destination)", null)));
        Assert.Equal("Calculate: Runway, destination Runway 09; steps: Via A | Hold short of runway 27L at B",
            ClearanceStepEntry.LogLine(new[]
            {
                new ClearanceStep(ClearanceStepKind.Via, ClearanceTargetKind.Taxiway, "A"),
                ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("27L"), "B"),
            }, "Runway", "Runway 09"));
        Assert.Equal("Calculate: Runway, destination Runway 09; steps: (none)",
            ClearanceStepEntry.LogLine(Array.Empty<ClearanceStep>(), "Runway", "Runway 09"));
        Assert.Equal("Calculate: Gate, stand or parking; steps: (none)",
            ClearanceStepEntry.LogLine(null!, "Gate, stand or parking", "  "));
    }

    [Fact]
    public void A_crossing_with_no_place_named_has_no_at()
    {
        var none = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("04L"), atLabel: ClearanceStepEntry.NoAtPlace);
        var hidden = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("04L"));

        Assert.Null(none.At);
        Assert.Null(hidden.At);
        Assert.Equal("Cross runway 04L", none.Sentence());
    }

    [Fact]
    public void A_crossing_at_a_holding_point_takes_its_name_and_node()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Runway("23R"),
            atLabel: "P1" + HoldShortTargetList.HoldingPointSuffix, atPoint: Point("P1", 4711));

        Assert.Equal("P1", step.At);
        Assert.Equal(4711, step.AtNodeId);
        Assert.False(step.AtNodeIsPassedStop);
        Assert.Equal("Step 3: cross runway 23R at P1.", TestCulture.Run("de-DE", () => step.Spoken(3)));
    }

    [Fact]
    public void Crossing_a_taxiway_takes_no_place()
    {
        var step = ClearanceStepEntry.Build(ClearanceStepKind.Cross, Taxiway("B"), atLabel: "P", atPoint: Point("P1", 9));

        Assert.Equal(ClearanceTargetKind.Taxiway, step.TargetKind);
        Assert.Null(step.At);
        Assert.Equal(0, step.AtNodeId);
        Assert.Equal("Cross taxiway B", step.Sentence());
    }

    [Fact]
    public void Hold_short_names_a_runway_or_a_taxiway()
    {
        var runway = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Runway("04R"));
        var taxiway = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort, Taxiway("L"));

        Assert.Equal("Step 4: hold short of runway 04R.", TestCulture.Run("sv-SE", () => runway.Spoken(4)));
        Assert.Equal("Step 1: hold short of taxiway L.", TestCulture.Run("de-DE", () => taxiway.Spoken(1)));
    }

    [Fact]
    public void Hold_short_of_a_holding_point_carries_its_node_kind_and_source()
    {
        var painted = Point("L2", 77, "intermediate");
        var fromSign = Point("KK", 88, NamedHoldingPointResolver.SignKind);

        var a = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item(HoldShortTargetList.HoldingPointLabel(painted), ClearanceTargetKind.HoldingPoint, "L2", painted));
        var b = ClearanceStepEntry.Build(ClearanceStepKind.HoldShort,
            new ClearanceNameLists.Item(HoldShortTargetList.HoldingPointLabel(fromSign), ClearanceTargetKind.HoldingPoint, "KK", fromSign));

        Assert.Equal((ClearanceTargetKind.HoldingPoint, "L2", 77, "intermediate", false),
            (a.TargetKind, a.Name, a.NodeId, a.HoldingPointKind, a.FromSign));
        Assert.True(b.FromSign);
        Assert.Equal(88, b.NodeId);
        Assert.Equal("Step 2: hold short of L2 (holding point).", TestCulture.Run("de-DE", () => a.Spoken(2)));
    }

    // ---- The window's access keys (read from its source) -------------------------------------

    private static string FormSource()
        => File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Forms", "TaxiAssistForm.cs"));

    // Every access key a control's Text gives in the window ("Show &fitting only" -> 'F'), with the name box's own.
    private static List<char> AccessKeys()
    {
        var keys = new List<char>();
        foreach (Match m in Regex.Matches(FormSource(), "Text = \"([^\"\\n]*)\""))
        {
            string text = m.Groups[1].Value;
            int amp = text.IndexOf('&');
            if (amp >= 0 && amp + 1 < text.Length) keys.Add(char.ToUpperInvariant(text[amp + 1]));
        }
        keys.Add('W');   // the name box's label, ClearanceStepEntry.NameBoxLabel
        return keys;
    }

    // Alt+V, Alt+L, Alt+R, Alt+X and Alt+H pick a step kind from anywhere in the window (ProcessCmdKey): no
    // control may take one of those letters.
    [Fact]
    public void No_control_takes_a_step_kind_letter()
    {
        var keys = AccessKeys();
        foreach (char letter in new[] { 'V', 'L', 'R', 'X', 'H' })
            Assert.DoesNotContain(letter, keys);
    }

    [Fact]
    public void The_step_block_keys_and_the_CAT_III_box_each_have_one_control()
    {
        var keys = AccessKeys();
        foreach (char letter in new[] { 'K', 'W', 'D', 'U', 'M', 'Z', '3' })
            Assert.Equal(1, keys.Count(k => k == letter));

        string form = FormSource();
        Assert.Contains("Text = \"CAT &3 / low-visibility hold (LVP)\"", form);
        Assert.Contains("AccessibleName = \"CAT three, low visibility hold\"", form);
    }

    // Stage 4, D9: the Runway entry list's label holds I, where "Intersection departure" did; P, which "Depart from
    // named holding point" held, is free.
    [Fact]
    public void The_runway_entry_list_has_I_and_no_control_has_P()
    {
        var keys = AccessKeys();
        Assert.Equal(1, keys.Count(k => k == 'I'));
        Assert.DoesNotContain('P', keys);
        Assert.Contains("Text = \"Runway entry (&I):\"", FormSource());
    }

    // A shared letter does not cycle between its controls: Windows Forms takes the first match after the focused
    // control, so "Last route summary" sharing S with Stop Guidance (always enabled) stopped guidance from almost
    // anywhere. Every access key in the window belongs to one control, Stop Guidance alone has S, the summary N.
    [Fact]
    public void Every_access_key_in_the_window_belongs_to_one_control()
    {
        var shared = AccessKeys().GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(shared);

        string form = FormSource();
        Assert.Contains("Text = \"&Stop Guidance\"", form);
        Assert.Contains("Text = \"Last route summary (&N):\"", form);
    }
}
