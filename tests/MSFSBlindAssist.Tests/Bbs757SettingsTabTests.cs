using System.Windows.Forms;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The pure parts of the 757 EFB window's Settings page: what leaving a number or text box with a change not saved says
/// (<c>BBS757EfbForm.UnsavedEditText</c>), where a key moves the random failure frequency's slider
/// (<c>Bbs757LockableSlider.Target</c>) and which keys with Control it takes and ignores
/// (<c>Bbs757LockableSlider.IsSliderKeyWithControl</c>). The page itself is checked in the simulator, by ear.
/// </summary>
public class Bbs757SettingsTabTests
{
    private static Bbs757SettingRow Row(string path, string text, Bbs757RowState state = Bbs757RowState.Value) =>
        new(Bbs757EfbSettings.ByPath(path)!, state, -1, text);

    // ---- a box left with a change not saved ---------------------------------------------------------

    [Fact]
    public void Leaving_a_box_with_a_change_not_saved_says_so()
    {
        Assert.Equal("TOD pause distance (NM) not saved", BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), "25"));
        Assert.Equal("Hoppie logon code not saved", BBS757EfbForm.UnsavedEditText(Row("hoppie_logon_code", ""), "ABC"));
        // An emptied box is a change too: it would have switched the distance off, or cleared the ID.
        Assert.Equal("SimBrief ID number not saved", BBS757EfbForm.UnsavedEditText(Row("simbrief_id", "1234567"), ""));
    }

    [Fact]
    public void Leaving_a_box_that_holds_the_stored_value_says_nothing()
    {
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), "1"));
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), " 1 "));
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", ""), ""));
        // A box showing a state, not a value, is read-only: there is nothing of the pilot's to drop.
        Assert.Null(BBS757EfbForm.UnsavedEditText(
            Row("display_brightness", Bbs757SettingsRows.AutomaticText, Bbs757RowState.Automatic), "29"));
        Assert.Null(BBS757EfbForm.UnsavedEditText(
            Row("simbrief_id", Bbs757SettingsRows.UnreadableText, Bbs757RowState.Unreadable), "1"));
        // Before the first read there is no stored value to compare with.
        Assert.Null(BBS757EfbForm.UnsavedEditText(null, "25"));
    }

    // ---- the slider's keys ---------------------------------------------------------------------------

    [Theory]
    [InlineData(Keys.Up, 4, 5)]
    [InlineData(Keys.Right, 4, 5)]
    [InlineData(Keys.Down, 4, 3)]
    [InlineData(Keys.Left, 4, 3)]
    [InlineData(Keys.PageUp, 4, 14)]
    [InlineData(Keys.PageDown, 14, 4)]
    [InlineData(Keys.Home, 14, 0)]
    [InlineData(Keys.End, 4, 30)]
    [InlineData(Keys.Up | Keys.Shift, 4, 5)]
    // Kept at its ends.
    [InlineData(Keys.Up, 30, 30)]
    [InlineData(Keys.PageUp, 25, 30)]
    [InlineData(Keys.Down, 0, 0)]
    [InlineData(Keys.PageDown, 5, 0)]
    public void Up_and_page_up_move_the_slider_higher(Keys key, int from, int to)
    {
        Assert.Equal(to, Bbs757LockableSlider.Target(key, from, 0, 30, 10));
    }

    [Theory]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Tab | Keys.Shift)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Return)]
    [InlineData(Keys.Escape)]
    [InlineData(Keys.A)]
    [InlineData(Keys.Up | Keys.Alt)]
    [InlineData(Keys.Right | Keys.Control)]
    [InlineData(Keys.Tab | Keys.Control)]
    public void Other_keys_and_the_window_s_shortcuts_leave_the_slider_alone(Keys key)
    {
        Assert.Null(Bbs757LockableSlider.Target(key, 4, 0, 30, 10));
    }

    [Theory]
    // With Control, an arrow, Home or End moves nothing: the native trackbar would act on it, Up and Down the other way.
    [InlineData(Keys.Up | Keys.Control, true)]
    [InlineData(Keys.Down | Keys.Control, true)]
    [InlineData(Keys.Left | Keys.Control, true)]
    [InlineData(Keys.Right | Keys.Control, true)]
    [InlineData(Keys.Home | Keys.Control, true)]
    [InlineData(Keys.End | Keys.Control, true)]
    [InlineData(Keys.Up | Keys.Control | Keys.Shift, true)]
    // The tab control's own keys, Alt (the native trackbar ignores it), and the plain keys are not taken here.
    [InlineData(Keys.PageUp | Keys.Control, false)]
    [InlineData(Keys.PageDown | Keys.Control, false)]
    [InlineData(Keys.Tab | Keys.Control, false)]
    [InlineData(Keys.Up | Keys.Control | Keys.Alt, false)]
    [InlineData(Keys.Up | Keys.Alt, false)]
    [InlineData(Keys.Up, false)]
    public void An_arrow_home_or_end_with_control_is_taken_and_moves_nothing(Keys key, bool taken)
    {
        Assert.Equal(taken, Bbs757LockableSlider.IsSliderKeyWithControl(key));
    }
}
