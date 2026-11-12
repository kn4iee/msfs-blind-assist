namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>What Backspace or Delete does in the MFD window (<see cref="A350MfdClear.Decide"/>).</summary>
public enum MfdClearAction { ClearScratchpadBox, ClearMessage, ClearField, CannotBeCleared, Unavailable, KccuClr, Nothing }

/// <summary>
/// Backspace and Delete in the MFD window as the A380's MFD window has them (plan 25; `FBWA380MCDUForm.PerformClear` and its
/// Delete), on the A350's own keys. Backspace empties the window's scratchpad box when it holds text, else removes the FMS
/// message (the KCCU's CLR, `docs/inibuilds-a350.md`), else clears the selected field (a click, the word CLR typed, ENTER:
/// <see cref="A350MfdInput.ClearAsync"/>), else, with no line, presses the KCCU's CLR. Delete clears the selected field alone. A
/// line that is no field "cannot be cleared"; a dimmed field is "unavailable" (the A380 asks "a field?" before "dimmed?").
/// </summary>
public static class A350MfdClear
{
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
        if (A350MfdWording.IsDimmed(selected)) return (MfdClearAction.Unavailable, "Unavailable");
        return (MfdClearAction.ClearField, "Cleared");
    }
}
