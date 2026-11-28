// The Clearance list in the taxi planner window following guidance live: when it may drop finished steps while
// the window is open, which steps went, and where the pilot's place in the list goes. Pure: no form, no manager.
// "Origins" are the form's _stepOrigins: each shown step's index in the list as last calculated, -1 for a step the
// pilot added or changed since (ClearanceTrim.KeepingEdits keeps every -1 step, in order).
//
// Identity of a step across two lists: a step with origin >= 0 is the same step when its origin is the same; a
// step with origin -1 is the k-th such step of its list (the k-th in the old list is the k-th in the new).

namespace MSFSBlindAssist.Navigation;

public static class ClearanceListFollow
{
    /// <summary>
    /// True when the list may drop finished steps now: the window is the active one, a route is live, the list
    /// holds a calculated leg, and the pilot is neither changing a step nor has a step-block list dropped down.
    /// Anything else waits, so the list never shifts under a pilot who is in the middle of editing it, nor in a
    /// window they are not looking at.
    /// </summary>
    public static bool FollowsNow(bool windowActive, bool routeLive, bool listHoldsCalculatedLeg,
                                  bool changingStep, bool stepListDroppedDown)
        => windowActive && routeLive && listHoldsCalculatedLeg && !changingStep && !stepListDroppedDown;

    /// <summary>
    /// The indices in <paramref name="oldOrigins"/> of the steps the new list no longer has, ascending, when the new
    /// list is the old one with steps taken out (every new step is an old step, in the same order). Null otherwise
    /// (a step the old list did not have, or a changed order): that is not a list that only lost finished steps, so
    /// the caller leaves the list alone rather than guess.
    /// </summary>
    public static IReadOnlyList<int>? RemovedIndices(IReadOnlyList<int> oldOrigins, IReadOnlyList<int> newOrigins)
    {
        var mapped = new bool[oldOrigins.Count];
        int previous = -1;
        for (int newIndex = 0; newIndex < newOrigins.Count; newIndex++)
        {
            int oldIndex = OldIndexOf(oldOrigins, newOrigins, newIndex);
            if (oldIndex <= previous) return null;
            mapped[oldIndex] = true;
            previous = oldIndex;
        }
        var removed = new List<int>();
        for (int i = 0; i < mapped.Length; i++)
            if (!mapped[i]) removed.Add(i);
        return removed;
    }

    /// <summary>
    /// The index to select in the new list. The step that was selected stays selected when it is kept (the
    /// pilot's place does not move). When it finished, and only when the list has the keyboard focus (the owner's
    /// "Update right away": the pilot is reading it now), the place goes to the first step after it that is kept,
    /// else the last step. With the focus elsewhere nothing is selected (-1): a background update never moves the
    /// pilot's place in a list they are not in, so the screen reader has nothing to announce. -1 too for no
    /// selection before, or an empty list after.
    /// </summary>
    public static int SelectionAfter(IReadOnlyList<int> oldOrigins, int oldSelected, IReadOnlyList<int> newOrigins,
                                     bool listFocused)
    {
        if (oldSelected < 0 || oldSelected >= oldOrigins.Count || newOrigins.Count == 0) return -1;

        int kept = NewIndexOf(oldOrigins, oldSelected, newOrigins);
        if (kept >= 0) return kept;
        if (!listFocused) return -1;

        for (int i = oldSelected + 1; i < oldOrigins.Count; i++)
        {
            int after = NewIndexOf(oldOrigins, i, newOrigins);
            if (after >= 0) return after;
        }
        return newOrigins.Count - 1;
    }

    // The old index of the step at newIndex in the new list; -1 when the old list has none.
    private static int OldIndexOf(IReadOnlyList<int> oldOrigins, IReadOnlyList<int> newOrigins, int newIndex)
        => IndexOfSame(newOrigins, newIndex, oldOrigins);

    // The new index of the step at oldIndex in the old list; -1 when the new list has none.
    private static int NewIndexOf(IReadOnlyList<int> oldOrigins, int oldIndex, IReadOnlyList<int> newOrigins)
        => IndexOfSame(oldOrigins, oldIndex, newOrigins);

    // The index in `to` of the step that is the step at `index` in `from`, by the identity above; -1 when none.
    private static int IndexOfSame(IReadOnlyList<int> from, int index, IReadOnlyList<int> to)
    {
        int origin = from[index];
        if (origin >= 0) return IndexOf(to, origin, 0);

        int k = 0;
        for (int i = 0; i < index; i++)
            if (from[i] < 0) k++;
        return IndexOf(to, -1, k);
    }

    // The index of the (skip+1)-th occurrence of value; -1 when there is none.
    private static int IndexOf(IReadOnlyList<int> list, int value, int skip)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == value && skip-- == 0) return i;
        return -1;
    }
}
