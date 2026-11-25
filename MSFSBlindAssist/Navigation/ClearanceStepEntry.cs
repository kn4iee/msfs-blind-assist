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

    /// <summary>The kind at a list index; Via for an index the list does not have (nothing selected).</summary>
    public static ClearanceStepKind KindAt(int index)
        => index >= 0 && index < KindItems.Count ? (ClearanceStepKind)index : ClearanceStepKind.Via;

    /// <summary>The kind an Alt+letter picks from anywhere in the window: V via, L left onto, R right onto,
    /// X cross, H hold short of. Null for any other letter.</summary>
    public static ClearanceStepKind? KindForAccessKey(char letter) => char.ToUpperInvariant(letter) switch
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
