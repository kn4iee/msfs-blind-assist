namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>What Backspace or Delete does in the MFD window (<see cref="A350MfdClear.Decide"/>).</summary>
public enum MfdClearAction { ClearScratchpadBox, ClearMessage, ClearField, CannotBeCleared, KccuClr, Nothing }

/// <summary>
/// Backspace and Delete in the MFD window as the A380's MFD window has them (plan 25; `FBWA380MCDUForm.PerformClear` and its
/// Delete), on the A350's own keys. Backspace empties the window's scratchpad box when it holds text, else removes the FMS
/// message (the KCCU's CLR, `docs/inibuilds-a350.md`), else clears the selected field (a click, the word CLR typed, ENTER:
/// <see cref="A350MfdInput.ClearAsync"/>), else, with no line, presses the KCCU's CLR. Delete clears the selected field alone. A
/// line that is no field "cannot be cleared". A grey field is cleared all the same: on the A350 grey is a colour the reader infers,
/// not the A380's disabled flag, so the FMS says if it refuses (final review; never block).
/// </summary>
public static class A350MfdClear
{
    /// <summary>Whether an outcome presses the MFD, and so waits on an action already running (the window says so aloud); emptying
    /// the window's box and saying what cannot be cleared never wait.</summary>
    public static bool NeedsMfd(MfdClearAction action) =>
        action is MfdClearAction.ClearMessage or MfdClearAction.ClearField or MfdClearAction.KccuClr;

    /// <param name="scratchpadBox">The window's scratchpad box (only spaces is empty).</param>
    /// <param name="messageShown">Whether the MFD shows an FMS message in its scratchpad.</param>
    /// <param name="selected">The list's selected line, or null.</param>
    /// <param name="delete">Delete rather than Backspace.</param>
    public static (MfdClearAction Action, string? Say) Decide(string scratchpadBox, bool messageShown, MfdLine? selected, bool delete)
    {
        if (!delete)
        {
            if (!string.IsNullOrWhiteSpace(scratchpadBox)) return (MfdClearAction.ClearScratchpadBox, "Scratchpad cleared");
            if (messageShown) return (MfdClearAction.ClearMessage, "Info cleared");
        }
        if (selected == null) return delete ? (MfdClearAction.Nothing, null) : (MfdClearAction.KccuClr, null);
        if (selected.Kind != "field") return (MfdClearAction.CannotBeCleared, "Cannot be cleared");
        return (MfdClearAction.ClearField, "Cleared");
    }
}
