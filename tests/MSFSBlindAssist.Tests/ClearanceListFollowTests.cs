// When the Clearance list may drop finished steps while the window is open, and where the pilot's place goes
// when it does (ClearanceListFollow, pure). Origins are the form's _stepOrigins: each shown step's index in the
// list as last calculated, -1 for a step the pilot added or changed since.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceListFollowTests
{
    [Fact]
    public void Follows_only_when_active_live_calculated_and_not_editing()
    {
        Assert.True(ClearanceListFollow.FollowsNow(true, true, true, false, false));
    }

    [Theory]
    [InlineData(false, true, true, false, false)]   // window not active
    [InlineData(true, false, true, false, false)]   // no live route
    [InlineData(true, true, false, false, false)]   // the list holds no calculated leg
    [InlineData(true, true, true, true, false)]     // a step is being changed
    [InlineData(true, true, true, false, true)]     // a step-block list is dropped down
    public void Waits_otherwise(bool active, bool live, bool calculated, bool changing, bool droppedDown)
        => Assert.False(ClearanceListFollow.FollowsNow(active, live, calculated, changing, droppedDown));

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, new[] { 1, 2 }, new[] { 0 })]
    [InlineData(new[] { 0, -1, 1 }, new[] { -1, 1 }, new[] { 0 })]
    [InlineData(new[] { 0, -1, -1 }, new[] { -1, -1 }, new[] { 0 })]
    [InlineData(new[] { 0, 1, 2 }, new[] { 0, 1, 2 }, new int[0])]
    [InlineData(new[] { 0, 1, 2 }, new int[0], new[] { 0, 1, 2 })]
    public void Removed_indices_are_the_old_steps_not_kept(int[] oldO, int[] newO, int[] expected)
        => Assert.Equal(expected, ClearanceListFollow.RemovedIndices(oldO, newO)!);

    [Theory]
    [InlineData(new[] { 0, 1 }, new[] { 1, 0 })]    // reordered
    [InlineData(new[] { 0, 1 }, new[] { 0, 3 })]    // a step the old list did not have
    [InlineData(new[] { 0 }, new[] { 0, -1 })]      // a pilot's step the old list did not have
    public void Not_a_list_with_steps_taken_out_gives_null(int[] oldO, int[] newO)
        => Assert.Null(ClearanceListFollow.RemovedIndices(oldO, newO));

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, 2, new[] { 1, 2 }, true, 1)]     // the selected step stays selected
    [InlineData(new[] { 0, 1, 2 }, 2, new[] { 1, 2 }, false, 1)]
    [InlineData(new[] { 0, 1, 2 }, 0, new[] { 1, 2 }, true, 0)]     // finished: the step now in its place
    [InlineData(new[] { 0, 1, 2 }, 1, new[] { 0, 2 }, true, 1)]
    [InlineData(new[] { 0, 1, 2 }, 2, new[] { 0, 1 }, true, 1)]     // none after it: the last step
    [InlineData(new[] { 0, 1, 2 }, 0, new[] { 1, 2 }, false, -1)]   // focus elsewhere: nothing selected
    [InlineData(new[] { 0, 1 }, 0, new int[0], true, -1)]           // the list emptied
    [InlineData(new[] { 0, 1 }, -1, new[] { 1 }, true, -1)]         // nothing was selected
    [InlineData(new[] { 0, -1 }, 1, new[] { -1 }, true, 0)]         // a pilot's step keeps its selection
    public void Selection_follows_the_pilots_place(int[] oldO, int oldSel, int[] newO, bool focused, int expected)
        => Assert.Equal(expected, ClearanceListFollow.SelectionAfter(oldO, oldSel, newO, focused));
}
