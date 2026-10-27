using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The draw hooks keep NanoVG's per-context state as the draw code changes it: the fill colour and the
// 2-D transform, saved and restored as a stack. A text's screen position is its local position through
// that transform (the CTR draws the SD and WD translated, so raw positions overlap).
public class A350DrawStateTests
{
    private static void Near(float want, float got) => Assert.InRange(got, want - 0.001f, want + 0.001f);

    [Fact]
    public void A_text_is_placed_through_translate_and_scale()
    {
        var s = new A350DrawState();
        s.Translate(1, 822, 300);
        s.Scale(1, 2, 2);
        var (x, y) = s.Apply(1, 10, 5);
        Near(842, x); Near(310, y);
        var (x2, y2) = s.Apply(2, 10, 5);   // another context is untouched
        Near(10, x2); Near(5, y2);
    }

    [Fact]
    public void Rotate_turns_about_the_current_origin()
    {
        var s = new A350DrawState();
        s.Translate(1, 100, 0);
        s.Rotate(1, MathF.PI / 2);
        var (x, y) = s.Apply(1, 10, 0);
        Near(100, x); Near(10, y);
    }

    [Fact]
    public void Save_and_restore_bring_back_the_transform_and_the_fill()
    {
        var s = new A350DrawState();
        s.Fill(1, new A350Rgba(0, 1, 0, 1));
        s.Save(1);
        s.Translate(1, 50, 50);
        s.Fill(1, new A350Rgba(1, 0.6f, 0, 1));
        Assert.Equal(new A350Rgba(1, 0.6f, 0, 1), s.FillOf(1));
        s.Restore(1);
        Assert.Equal(new A350Rgba(0, 1, 0, 1), s.FillOf(1));
        var (x, _) = s.Apply(1, 1, 1);
        Near(1, x);
        s.Restore(1);   // an unbalanced restore is ignored
    }

    [Fact]
    public void Reset_clears_the_transform_and_clear_forgets_everything()
    {
        var s = new A350DrawState();
        s.Translate(1, 5, 5);
        s.Fill(1, new A350Rgba(1, 1, 1, 1));
        s.ResetTransform(1);
        Near(1, s.Apply(1, 1, 1).X);
        Assert.NotNull(s.FillOf(1));
        s.Clear();
        Assert.Null(s.FillOf(1));
    }

    [Theory]
    [InlineData(0f, 1f, 0f, "green")]
    [InlineData(1f, 0.6f, 0f, "amber")]
    [InlineData(1f, 0.65f, 0.05f, "amber")]
    [InlineData(0f, 1f, 1f, "cyan")]
    [InlineData(1f, 0f, 1f, "magenta")]
    [InlineData(1f, 1f, 1f, "white")]
    [InlineData(1f, 0f, 0f, "red")]
    [InlineData(1f, 1f, 0f, "yellow")]
    [InlineData(0.5f, 0.5f, 0.5f, "grey")]
    [InlineData(0f, 0f, 0f, "black")]
    public void Colours_are_named_by_the_nearest_ecam_colour(float r, float g, float b, string name)
    {
        Assert.Equal(name, A350Rgba.NameOf(new A350Rgba(r, g, b, 1)));
    }

    [Fact]
    public void No_colour_is_a_question_mark()
    {
        Assert.Equal("?", A350Rgba.NameOf(null));
    }
}
