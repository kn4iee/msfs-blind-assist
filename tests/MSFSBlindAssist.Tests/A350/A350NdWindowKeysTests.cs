using System.Windows.Forms;
using MSFSBlindAssist.Forms.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The ND window's keys (review 2026-11-04, forms W3).</summary>
public class A350NdWindowKeysTests
{
    // In the text box (ARPT SEL's city names) Ctrl+Left and Ctrl+Right dragged the map instead of moving by word, and on Plan it stayed dragged.
    [Theory]
    [InlineData(Keys.Control | Keys.Left)]
    [InlineData(Keys.Control | Keys.Right)]
    [InlineData(Keys.Control | Keys.Up)]
    [InlineData(Keys.Control | Keys.Down)]
    public void Ctrl_arrows_drag_the_map_from_the_list_only(Keys key)
    {
        Assert.NotNull(A350NdWindow.DragFor(key, onList: true));
        Assert.Null(A350NdWindow.DragFor(key, onList: false));
    }

    [Fact]
    public void The_view_moves_the_way_the_arrow_says() =>
        Assert.Equal((180f, 0f), A350NdWindow.DragFor(Keys.Control | Keys.Left, onList: true));
}
