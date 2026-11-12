using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 25 Task 4: Backspace and Delete in the MFD window as the A380's MFD window has them (`FBWA380MCDUForm.PerformClear`
/// and its Delete), on the A350's own keys: the window's scratchpad box first, then the FMS message (the KCCU's CLR), then the
/// selected field (clicked, the word CLR typed, ENTER: the FMS deletes a field for that input, live 2026-10-30).</summary>
public class A350MfdClearTests
{
    private static readonly MfdLine Field = new("field", "CI: 25, cyan", 300, 200);
    private static readonly MfdLine GreyField = new("field", "LEVEL: FL required, grey", 300, 240);
    private static readonly MfdLine Key = new("button", "INSERT * TMPY button, amber", 400, 500);

    public static TheoryData<string, bool, MfdLine?, bool, MfdClearAction, string?> Table => new()
    {
        // Backspace: the box's text first, whatever else.
        { "VCBI", true, Field, false, MfdClearAction.ClearScratchpadBox, "Scratchpad cleared" },
        { "VCBI", false, null, false, MfdClearAction.ClearScratchpadBox, "Scratchpad cleared" },
        // then the FMS message.
        { "", true, Field, false, MfdClearAction.ClearMessage, "Info cleared" },
        { "", true, null, false, MfdClearAction.ClearMessage, "Info cleared" },
        // then the selected line.
        { "", false, Field, false, MfdClearAction.ClearField, "Cleared" },
        { "", false, GreyField, false, MfdClearAction.Unavailable, "Unavailable" },
        { "", false, Key, false, MfdClearAction.CannotBeCleared, "Cannot be cleared" },
        { "", false, null, false, MfdClearAction.KccuClr, null },
        // A box of spaces is empty (Review Focus 5).
        { "   ", false, Field, false, MfdClearAction.ClearField, "Cleared" },
        { "   ", true, Field, false, MfdClearAction.ClearMessage, "Info cleared" },
        // Delete: the selected line alone, whatever the box and the message hold.
        { "VCBI", true, Field, true, MfdClearAction.ClearField, "Cleared" },
        { "", false, GreyField, true, MfdClearAction.Unavailable, "Unavailable" },
        { "", true, Key, true, MfdClearAction.CannotBeCleared, "Cannot be cleared" },
        { "VCBI", true, null, true, MfdClearAction.Nothing, null },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void A_clear_does_what_the_a380_window_does(string box, bool message, MfdLine? selected, bool delete, MfdClearAction action, string? say) =>
        Assert.Equal((action, say), A350MfdClear.Decide(box, message, selected, delete));

    // Backspace removes a message only when the MFD shows one: never for an empty scratchpad, a torn read or one caught without its box.
    [Theory]
    [InlineData("Scratchpad: CHECK DATABASE CYCLE, amber", true)]
    [InlineData("Scratchpad: empty", false)]
    [InlineData("Scratchpad: CHECK IRS/AIRPORT POS, black", false)]
    [InlineData("Scratchpad: unreadable, white", false)]
    public void A_message_is_shown_only_when_the_scratchpad_holds_one(string pad, bool shown) =>
        Assert.Equal(shown, MSFSBlindAssist.Forms.A350.A350MfdWindow.MessageShown(new[] { new MfdLine("title", "INIT", 0, 0), new MfdLine("scratchpad", pad, 0, 0) }));

    [Fact]
    public void No_page_read_shows_no_message() => Assert.False(MSFSBlindAssist.Forms.A350.A350MfdWindow.MessageShown(Array.Empty<MfdLine>()));

    // A dimmed key is no field: the A380 window asks "a field?" before "dimmed?".
    [Fact]
    public void A_dimmed_key_cannot_be_cleared() =>
        Assert.Equal((MfdClearAction.CannotBeCleared, "Cannot be cleared"),
            A350MfdClear.Decide("", false, new MfdLine("button", "DELETE ALL * button, grey", 0, 0), delete: false));
}
