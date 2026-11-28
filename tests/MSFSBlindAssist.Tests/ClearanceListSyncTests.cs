// The Clearance list following guidance (TaxiAssistForm.ApplyFollowToList), on a real Win32 list box that is never
// shown: Visible is false and a parentless control's handle sits on WinForms' hidden parking window, so nothing appears
// on screen and no input is sent. A list box has a SELECTED item and a FOCUSED item (its caret); the screen reader names
// the focused one when focus comes back into the list, while Enter and Delete act on the selected one. Removing an item
// above the selection moves the selection up but leaves the caret where it was, so the two must be put back together.

using System.Runtime.InteropServices;
using System.Windows.Forms;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class ClearanceListSyncTests
{
    // Review fix round 1, Important 1 (measured): a step above the kept selection finishes. Before the fix the selection
    // read "step 3" and the caret "step 4", so focus back in the list named step 4 while Enter and Delete acted on step 3.
    // The selection did not move, so nothing says it changed.
    [Fact]
    public void A_step_finished_above_the_selection_keeps_the_selected_and_focused_step_together()
    {
        using var list = HiddenList("step 1", "step 2", "step 3", "step 4", "step 5", "step 6");
        list.SelectedIndex = 2;
        int changes = 0;
        list.SelectedIndexChanged += (s, e) => changes++;

        TaxiAssistForm.ApplyFollowToList(list, removed: new[] { 0 },
            new[] { "step 2", "step 3", "step 4", "step 5", "step 6" }, select: 1);

        Assert.Equal(new[] { "step 2", "step 3", "step 4", "step 5", "step 6" }, list.Items.Cast<string>());
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(1, CaretIndex(list));
        Assert.Equal("step 3", list.AccessibilityObject.GetSelected()?.Name);
        Assert.Equal("step 3", list.AccessibilityObject.GetFocused()?.Name);
        Assert.Equal(0, changes);
    }

    // Two steps above finish and the selected one reads differently now (a turn made reads as Via): still one step,
    // selected and focused.
    [Fact]
    public void Steps_finished_above_a_changed_selected_step_keep_it_selected_and_focused()
    {
        using var list = HiddenList("Via A", "Cross runway 4L", "Via B", "Left onto C", "Via D");
        list.SelectedIndex = 3;

        TaxiAssistForm.ApplyFollowToList(list, removed: new[] { 0, 1 }, new[] { "Via B", "Via C", "Via D" }, select: 1);

        Assert.Equal(new[] { "Via B", "Via C", "Via D" }, list.Items.Cast<string>());
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(1, CaretIndex(list));
        Assert.Equal("Via C", list.AccessibilityObject.GetFocused()?.Name);
    }

    // The selected step itself finishes with the focus in the list: its place goes to the step now there, selected and
    // focused alike, so the screen reader reads the step Enter and Delete act on.
    [Fact]
    public void The_selected_step_finishing_gives_its_place_to_the_step_now_there()
    {
        using var list = HiddenList("step 1", "step 2", "step 3", "step 4");
        list.SelectedIndex = 1;

        TaxiAssistForm.ApplyFollowToList(list, removed: new[] { 0, 1 }, new[] { "step 3", "step 4" }, select: 0);

        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, CaretIndex(list));
        Assert.Equal("step 3", list.AccessibilityObject.GetFocused()?.Name);
    }

    // A list that is not only the old one with steps taken out is rebuilt, the selection set where it is asked.
    [Fact]
    public void A_rebuilt_list_selects_and_focuses_the_kept_step()
    {
        using var list = HiddenList("step 1", "step 2", "step 3");
        list.SelectedIndex = 2;

        TaxiAssistForm.ApplyFollowToList(list, removed: null, new[] { "step 3", "step 1" }, select: 0);

        Assert.Equal(new[] { "step 3", "step 1" }, list.Items.Cast<string>());
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, CaretIndex(list));
    }

    // Nothing selected stays nothing selected, and nothing says it changed.
    [Fact]
    public void No_selection_stays_no_selection()
    {
        using var list = HiddenList("step 1", "step 2", "step 3");
        int changes = 0;
        list.SelectedIndexChanged += (s, e) => changes++;

        TaxiAssistForm.ApplyFollowToList(list, removed: new[] { 0 }, new[] { "step 2", "step 3" }, select: -1);

        Assert.Equal(-1, list.SelectedIndex);
        Assert.Equal(0, changes);
    }

    private static ListBox HiddenList(params string[] items)
    {
        var list = new ListBox { Visible = false };
        list.Items.AddRange(items);
        _ = list.Handle;   // a real Win32 list box, never shown
        return list;
    }

    private const int LB_GETCARETINDEX = 0x019F;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static int CaretIndex(ListBox list) => (int)SendMessage(list.Handle, LB_GETCARETINDEX, IntPtr.Zero, IntPtr.Zero);
}
