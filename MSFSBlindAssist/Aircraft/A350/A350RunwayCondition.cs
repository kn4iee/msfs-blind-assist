namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// The RWY COND/BRAKING ACTION knob's runway condition, picked as one choice. The WASM keeps the pick to itself (an index
/// in its own memory, <c>GEAR::Update</c>, 1.2.7): 0 is code 6 DRY down to 5, code 1 ICE. It moves only while the knob's
/// menu is open on the SD (a pull), one step per click, a click up toward DRY and stopping there, a click down toward ICE
/// and stopping there. So a pick needs no reading: pull, five clicks up land on DRY whatever was picked before, then one
/// click down per code below 6, then a push closes the menu. Each click is a command flag the WASM consumes once a frame,
/// so the clicks are paced (live 2026-10-26: a DEC from DRY moved the index 0 to 1, an INC back to 0). The words are the
/// SD menu's, read with the display reader: the runway state, then the braking action.
/// </summary>
public static class A350RunwayCondition
{
    public const string KnobId = "AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR";

    /// <summary>The row's MSFSBA variable key (a choice, never read).</summary>
    public const string Key = "A350_LDG_RWY_COND";

    public const string Name = "Runway condition";

    /// <summary>Between two clicks: live 2026-10-26, at 150 ms one click of five was lost to a long frame.</summary>
    public const int ClickGapMs = 300;

    /// <summary>For the menu to open after the pull, and the last click to land before the push.</summary>
    public const int MenuMs = 300;

    public static readonly IReadOnlyDictionary<double, string> Codes = new Dictionary<double, string>
    {
        [1] = "1, ice, poor",
        [2] = "2, standing water or slush, medium to poor",
        [3] = "3, snow or slippery when wet, medium",
        [4] = "4, compacted snow, good to medium",
        [5] = "5, wet, good",
        [6] = "6, dry",
    };

    /// <summary>The clicks that pick <paramref name="code"/> (6 DRY to 1 ICE) with the knob <paramref name="knob"/>.</summary>
    public static A350Plan Plan(A350Control knob, double code)
    {
        if (!Codes.ContainsKey(code))
            return A350Plan.Refused(A350WritePlan.NotAPositionRefusal);
        var steps = new List<A350Step> { Event(knob.Id + "_PULL", 1), new A350DelayStep(MenuMs) };
        for (int i = 0; i < 5; i++)
        {
            steps.Add(Event(knob.Id, 1));
            steps.Add(new A350DelayStep(ClickGapMs));
        }
        for (int i = 0; i < 6 - (int)code; i++)
        {
            steps.Add(Event(knob.Id, -1));
            steps.Add(new A350DelayStep(ClickGapMs));
        }
        steps.Add(new A350DelayStep(MenuMs));
        steps.Add(Event(knob.Id + "_PUSH", 1));
        return new A350Plan(steps, null);
    }

    private static A350CalcStep Event(string id, int value) => new($"{value} (>B:{id}_Set)");
}
