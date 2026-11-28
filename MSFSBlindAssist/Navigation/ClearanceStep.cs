// One step of an ATC taxi clearance, in ATC's own words: via, left onto, right onto, cross,
// hold short of. The Taxi Guidance window keeps an ordered list of these and reads each one
// back as a sentence.

namespace MSFSBlindAssist.Navigation;

public enum ClearanceStepKind { Via, LeftOnto, RightOnto, Cross, HoldShort }

public enum ClearanceTargetKind { Taxiway, Runway, HoldingPoint }

// AtNodeId is a holding point's node, the place a crossing is cleared at, which ClearancePlanner also pins a leg
// ending across the runway through; AtNodeIsPassedStop marks it instead as the stop node of a crossing already made, or,
// for one made with no stop, the node before the runway (ClearanceTrim keeps a used end crossing pinned to its place):
// matched, never pinned through. AtNodeIsReach says that pin is where the route reached the runway, there being no node
// before it on the route: it can lie on either side, so its side is never trusted (the window refuses that leg).
public sealed record ClearanceStep(
    ClearanceStepKind Kind, ClearanceTargetKind TargetKind, string Name,
    int NodeId = 0, string HoldingPointKind = "", bool FromSign = false,
    string? At = null, int AtNodeId = 0, bool AtNodeIsPassedStop = false, bool AtNodeIsReach = false)
{
    /// <summary>True for the steps that name a taxiway to follow (Via, Left onto, Right onto).</summary>
    public bool IsVia => Kind is ClearanceStepKind.Via or ClearanceStepKind.LeftOnto or ClearanceStepKind.RightOnto;

    /// <summary>The step as ATC would say it, e.g. "Cross runway 24L at P".</summary>
    public string Sentence()
    {
        switch (Kind)
        {
            case ClearanceStepKind.Via:
                return "Via " + Name;
            case ClearanceStepKind.LeftOnto:
                return "Left onto " + Name;
            case ClearanceStepKind.RightOnto:
                return "Right onto " + Name;
            case ClearanceStepKind.Cross:
            {
                string target = TargetKind == ClearanceTargetKind.Taxiway ? "taxiway " : "runway ";
                string text = "Cross " + target + Name;
                // A blank "at" is no place, as everywhere a step's place is read (ClearanceRunways.HasPlace).
                return string.IsNullOrWhiteSpace(At) ? text : text + " at " + At;
            }
            default:
            {
                return TargetKind switch
                {
                    ClearanceTargetKind.Runway => string.IsNullOrWhiteSpace(At)
                        ? "Hold short of runway " + Name
                        : "Hold short of runway " + Name + " at " + At,
                    ClearanceTargetKind.Taxiway => "Hold short of taxiway " + Name,
                    _ => "Hold short of " + Name
                        + (FromSign ? HoldShortTargetList.SignHoldingPointSuffix : HoldShortTargetList.HoldingPointSuffix),
                };
            }
        }
    }

    /// <summary>The sentence numbered for speech: "Step 2: cross runway 24L at P."</summary>
    public string Spoken(int number)
    {
        string sentence = Sentence();
        if (sentence.Length > 0)
            sentence = char.ToLowerInvariant(sentence[0]) + sentence.Substring(1);
        return "Step " + number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + sentence + ".";
    }
}
