// The Taxi Guidance window's step block as data: the kinds of step it offers, what the name box is called
// for each, the access keys that pick a kind, and the step a pick makes. Pure, so it is tested without the
// form. Design: docs/superpowers/specs/2026-11-14-taxi-clearance-steps-design.md, section 8.2.

namespace MSFSBlindAssist.Navigation;

public static class ClearanceStepEntry
{
    /// <summary>The step kind list's items, index for index with <see cref="ClearanceStepKind"/>. Each starts
    /// with its own letter, so one key picks it in the list.</summary>
    public static readonly IReadOnlyList<string> KindItems = new[]
    {
        "Via taxiway", "Left onto taxiway", "Right onto taxiway", "Cross", "Hold short of",
    };

    /// <summary>The "At" box's first item: no place named, so the crossing is the first the route makes.</summary>
    public const string NoAtPlace = "(none)";

    /// <summary>Spoken when a step is asked for with nothing chosen in the name box.</summary>
    public const string ChooseANameFirst = "Choose a name first.";

    /// <summary>Spoken when Remove Step (or Delete in the Clearance list) finds no step to remove.</summary>
    public const string NoStepToRemove = "No step to remove.";

    /// <summary>Spoken when Clear Steps is pressed by its button or access key (focus not in the Clearance list).</summary>
    public const string StepsCleared = "Steps cleared.";

    /// <summary>Spoken when Remove Step is pressed by its button or access key (focus not in the Clearance list):
    /// "Removed step 3: cross runway 24L." <paramref name="number"/> is the step's place before it was removed.</summary>
    public static string Removed(ClearanceStep step, int number)
    {
        ArgumentNullException.ThrowIfNull(step);
        string spoken = step.Spoken(number);
        return "Removed " + char.ToLowerInvariant(spoken[0]) + spoken.Substring(1);
    }

    /// <summary>
    /// What Remove Step says once ([CORE-7]): from the Clearance list (<paramref name="fromList"/>) nothing while a step
    /// remains, as the screen reader reads the step now in its place; from anywhere else which step went
    /// (<see cref="Removed"/>); and whenever the list is left empty (<paramref name="listNowEmpty"/>)
    /// <see cref="StepsCleared"/>, as an empty list gives the reader nothing to say. Null: say nothing.
    /// </summary>
    public static string? RemovalSentence(ClearanceStep removed, int number, bool fromList, bool listNowEmpty)
    {
        if (fromList) return listNowEmpty ? StepsCleared : null;
        string sentence = Removed(removed, number);
        return listNowEmpty ? sentence + " " + StepsCleared : sentence;
    }

    /// <summary>
    /// What the window says when the end of a leg empties its list ([CORE-7]): nothing as a rule (guidance says the leg's
    /// end, and the list held the leg just flown); <see cref="StepsCleared"/> once when steps the pilot added since the
    /// last Calculate go too (<paramref name="origins"/>: -1 for each) while the window has focus
    /// (<paramref name="windowFocused"/>), as they would vanish under the pilot's hands with nothing said. Null: say nothing.
    /// </summary>
    public static string? LegEndSentence(IReadOnlyList<int> origins, bool windowFocused)
        => windowFocused && origins != null && origins.Any(o => o < 0) ? StepsCleared : null;

    /// <summary>The kind at a list index; Via for an index the list does not have (nothing selected).</summary>
    public static ClearanceStepKind KindAt(int index)
        => index >= 0 && index < KindItems.Count ? (ClearanceStepKind)index : ClearanceStepKind.Via;

    /// <summary>The kind an Alt+letter picks from anywhere in the window: V via, L left onto, R right onto,
    /// X cross, H hold short of; null for anything else. Takes the KEY CODE of the letter, which is its upper-case
    /// character: never upper-cased here, as the codes of F3, F7, F9, NumPad8 and Separator read as the lower-case
    /// r, v, x, h and l.</summary>
    public static ClearanceStepKind? KindForAccessKey(char letter) => letter switch
    {
        'V' => ClearanceStepKind.Via,
        'L' => ClearanceStepKind.LeftOnto,
        'R' => ClearanceStepKind.RightOnto,
        'X' => ClearanceStepKind.Cross,
        'H' => ClearanceStepKind.HoldShort,
        _ => null,
    };

    /// <summary>The name box's label for a kind; its access key is always Alt+W.</summary>
    public static string NameBoxLabel(ClearanceStepKind kind) => kind switch
    {
        ClearanceStepKind.Cross => "Runway or taxi&way to cross:",
        ClearanceStepKind.HoldShort => "Hold short of runway or taxi&way:",
        _ => "Taxi&way:",
    };

    /// <summary>The name box's accessible name for a kind.</summary>
    public static string NameBoxAccessibleName(ClearanceStepKind kind) => kind switch
    {
        ClearanceStepKind.Cross => "Runway or taxiway to cross",
        ClearanceStepKind.HoldShort => "Hold short of runway, taxiway or holding point",
        _ => "Taxiway",
    };

    /// <summary>The name box's accessible name while step <paramref name="changingStep"/> is being changed:
    /// "Taxiway, changing step 3".</summary>
    public static string NameBoxAccessibleName(ClearanceStepKind kind, int changingStep)
        => NameBoxAccessibleName(kind) + ", changing step " + changingStep.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The Add Step button's text while step <paramref name="number"/> is being changed.</summary>
    public static string ReplaceButtonText(int number)
        => "Replace step " + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + " (&D)";

    /// <summary>The same button's accessible name (no access key marker).</summary>
    public static string ReplaceButtonName(int number)
        => "Replace step " + number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Spoken when Calculate is pressed with step <paramref name="number"/> still being changed and the boxes
    /// holding something other than that step (Calculate refuses and changes nothing).</summary>
    public static string StillChanging(int number)
        => "Step " + number.ToString(System.Globalization.CultureInfo.InvariantCulture)
           + " is still being changed. Press Enter to replace it, or Escape to keep it.";

    /// <summary>
    /// True when <paramref name="rebuilt"/>, the step the window's boxes make now, says the same as
    /// <paramref name="entered"/>, the step being changed: the same kind, target kind, name and, for a Cross step, "at"
    /// (ordinal, case ignored; null is empty), and the same node when the target is a holding point. Hidden data (an
    /// at-node, a passed-stop flag, the place a crossing put back as a hold short keeps) is not compared, as the boxes
    /// cannot show it: only a Cross step has an "At" box.
    /// </summary>
    public static bool SameAsEntered(ClearanceStep entered, ClearanceStep rebuilt)
    {
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(rebuilt);
        return entered.Kind == rebuilt.Kind
            && entered.TargetKind == rebuilt.TargetKind
            && string.Equals(entered.Name ?? "", rebuilt.Name ?? "", StringComparison.OrdinalIgnoreCase)
            && (entered.Kind != ClearanceStepKind.Cross
                || string.Equals(entered.At ?? "", rebuilt.At ?? "", StringComparison.OrdinalIgnoreCase))
            && (entered.TargetKind != ClearanceTargetKind.HoldingPoint || entered.NodeId == rebuilt.NodeId);
    }

    /// <summary>
    /// Whether a name box item is the name of <paramref name="step"/>, the step being changed: a holding point by its node
    /// only (a point of the same name at another node is another place, and a pick of it would move the hold with the
    /// same read-back); anything else by its kind and name (ordinal, case ignored).
    /// </summary>
    public static bool NameItemMatches(ClearanceNameLists.Item item, ClearanceStep step)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(step);
        if (item.Kind != step.TargetKind) return false;
        if (step.TargetKind == ClearanceTargetKind.HoldingPoint)
            return step.NodeId != 0 && item.HoldingPoint?.NodeId == step.NodeId;
        return string.Equals(item.Name, step.Name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether an "At" box item names the place of <paramref name="step"/>, a runway crossing being changed: only when
    /// a pick of it rebuilds the same place (<see cref="SameAsEntered"/>): a holding point (<paramref name="point"/>, the
    /// box's point for the label) by its name; a taxiway by its label, unless the step is pinned to a holding point's node
    /// (a taxiway of the same name is another place). Never <see cref="NoAtPlace"/>.
    /// </summary>
    public static bool AtItemMatches(string label, NamedHoldingPoint? point, ClearanceStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (string.IsNullOrEmpty(step.At) || string.IsNullOrEmpty(label) || label == NoAtPlace) return false;
        if (point != null) return string.Equals(point.Name, step.At, StringComparison.OrdinalIgnoreCase);
        return !PinnedToHoldingPoint(step) && string.Equals(label, step.At, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a runway crossing's place (<see cref="ClearanceStep.At"/>) is a holding point: pinned to one's node, or,
    /// for a crossing already made (its node then the stop it passed), named like one of the airport's holding points
    /// (<paramref name="holdingPointNames"/>) and like none of its taxiways (<paramref name="taxiwayNames"/>).
    /// </summary>
    public static bool PlaceIsHoldingPoint(ClearanceStep step, IEnumerable<string> taxiwayNames, IEnumerable<string> holdingPointNames)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (string.IsNullOrEmpty(step.At)) return false;
        if (PinnedToHoldingPoint(step)) return true;
        return !taxiwayNames.Any(n => string.Equals(n, step.At, StringComparison.OrdinalIgnoreCase))
            && holdingPointNames.Any(n => string.Equals(n, step.At, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The "At" box's item for the place of <paramref name="changing"/>, the runway crossing being changed, when no item
    /// offered names it (<paramref name="placeOffered"/> false): the box lists only the places on the aircraft's side,
    /// so a crossing made at a holding point now behind the aircraft is never offered its place again, and rebuilt with
    /// none it would lose its pin and clear the crossing back at the next Calculate. Labelled as the box labels a place:
    /// the taxiway's name, or the holding point's with <see cref="HoldShortTargetList.HoldingPointSuffix"/>. Null when
    /// nothing is added: not a runway crossing, no place, another runway chosen in the name box
    /// (<paramref name="chosenRunway"/>), or the place offered.
    /// </summary>
    public static string? OwnPlaceLabel(ClearanceStep changing, string? chosenRunway, bool placeOffered, bool placeIsHoldingPoint)
    {
        ArgumentNullException.ThrowIfNull(changing);
        if (placeOffered || changing is not { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway }
            || string.IsNullOrWhiteSpace(changing.At)
            || !string.Equals(chosenRunway, changing.Name, StringComparison.OrdinalIgnoreCase))
            return null;
        return placeIsHoldingPoint ? changing.At + HoldShortTargetList.HoldingPointSuffix : changing.At;
    }

    /// <summary>
    /// The step the boxes make while <paramref name="changing"/> is being changed: as <see cref="Build"/>, except that
    /// its own place (<paramref name="ownPlaceLabel"/>, from <see cref="OwnPlaceLabel"/>) picked in the "At" box with the
    /// same runway rebuilds the crossing with the old step's place (its At and AtNodeId), so <see cref="SameAsEntered"/>
    /// holds and the window keeps the old record, with its pins and origin.
    /// </summary>
    public static ClearanceStep BuildForChange(ClearanceStepKind kind, ClearanceNameLists.Item name, string? atLabel,
        NamedHoldingPoint? atPoint, ClearanceStep changing, string? ownPlaceLabel)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(changing);
        if (ownPlaceLabel != null && atLabel == ownPlaceLabel && kind == ClearanceStepKind.Cross
            && name.Kind == ClearanceTargetKind.Runway
            && changing is { Kind: ClearanceStepKind.Cross, TargetKind: ClearanceTargetKind.Runway }
            && string.Equals(name.Name, changing.Name, StringComparison.OrdinalIgnoreCase))
            return new ClearanceStep(ClearanceStepKind.Cross, ClearanceTargetKind.Runway, name.Name,
                At: changing.At, AtNodeId: changing.AtNodeId);
        return Build(kind, name, atLabel, atPoint);
    }

    // A crossing cleared at a holding point carries its node; a crossing already made carries the stop it passed (or
    // the reach) there instead.
    private static bool PinnedToHoldingPoint(ClearanceStep step)
        => step.AtNodeId != 0 && !step.AtNodeIsPassedStop && !step.AtNodeIsReach;

    /// <summary>The name box's accessible description for a kind.</summary>
    public static string NameBoxDescription(ClearanceStepKind kind) => kind switch
    {
        ClearanceStepKind.Cross =>
            "The runway or taxiway ATC cleared you to cross. Runways come first, those your last taxiway crosses at the top. Press Enter to add the step.",
        ClearanceStepKind.HoldShort =>
            "The runway, taxiway or holding point ATC told you to hold short of. Runways come first, then the taxiways and holding points together by name; a holding point is marked \"(holding point)\". Press Enter to add the step.",
        _ =>
            "The taxiway ATC named. After a taxiway, the taxiways it meets come first. Press a name's first letter to jump to it, then Enter to add the step.",
    };

    /// <summary>Why a step cannot be added from what is chosen: <see cref="ChooseANameFirst"/> with no name,
    /// so Enter never adds a name the pilot did not choose. Null when it can.</summary>
    public static string? Refusal(ClearanceNameLists.Item? name) => name == null ? ChooseANameFirst : null;

    /// <summary>
    /// The step a pick makes. Via, Left onto and Right onto name a taxiway. Cross names a runway or a taxiway; only a
    /// runway takes an "at": <paramref name="atPoint"/> (a holding point: its name and node) when one was picked, else
    /// <paramref name="atLabel"/> (a taxiway) unless it is <see cref="NoAtPlace"/>. Hold short of names a runway, a
    /// taxiway or a holding point (its node, its kind, and whether it was read from a sign).
    /// </summary>
    public static ClearanceStep Build(ClearanceStepKind kind, ClearanceNameLists.Item name,
        string? atLabel = null, NamedHoldingPoint? atPoint = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        switch (kind)
        {
            case ClearanceStepKind.Via:
            case ClearanceStepKind.LeftOnto:
            case ClearanceStepKind.RightOnto:
                return new ClearanceStep(kind, ClearanceTargetKind.Taxiway, name.Name);

            case ClearanceStepKind.Cross:
            {
                var target = name.Kind == ClearanceTargetKind.Runway ? ClearanceTargetKind.Runway : ClearanceTargetKind.Taxiway;
                if (target != ClearanceTargetKind.Runway)
                    return new ClearanceStep(kind, target, name.Name);
                if (atPoint != null)
                    return new ClearanceStep(kind, target, name.Name, At: atPoint.Name, AtNodeId: atPoint.NodeId);
                string? at = string.IsNullOrWhiteSpace(atLabel) || atLabel == NoAtPlace ? null : atLabel.Trim();
                return new ClearanceStep(kind, target, name.Name, At: at);
            }

            default:
            {
                if (name.Kind == ClearanceTargetKind.HoldingPoint && name.HoldingPoint is { } point)
                    return new ClearanceStep(ClearanceStepKind.HoldShort, ClearanceTargetKind.HoldingPoint, point.Name,
                        NodeId: point.NodeId, HoldingPointKind: point.Kind, FromSign: point.IsFromSign);
                return new ClearanceStep(ClearanceStepKind.HoldShort, name.Kind, name.Name);
            }
        }
    }
}
