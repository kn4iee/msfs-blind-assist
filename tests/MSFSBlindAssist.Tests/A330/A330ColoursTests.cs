using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Tests;

public class A330ColoursTests
{
    [Theory]
    [InlineData(0.98f, 0.76f, 0.29f, "amber")]   // MCDU amber
    [InlineData(0.31f, 0.82f, 1.00f, "cyan")]    // MCDU cyan
    [InlineData(0.30f, 0.30f, 0.30f, "grey")]    // MCDU greyed prompt
    [InlineData(1.00f, 0.55f, 0.00f, "amber")]   // ECAM amber
    [InlineData(0.38f, 0.80f, 0.39f, "green")]   // ECAM green
    [InlineData(0.40f, 0.86f, 1.00f, "cyan")]    // ECAM cyan
    [InlineData(0.46f, 1.00f, 0.46f, "green")]   // ISIS green
    [InlineData(1.00f, 0.00f, 0.00f, "red")]     // ND flag red
    [InlineData(1.00f, 1.00f, 1.00f, "white")]
    [InlineData(0.80f, 0.80f, 0.80f, "white")]
    [InlineData(0.55f, 0.55f, 0.55f, "white")]
    [InlineData(0.45f, 0.45f, 0.45f, "grey")]
    [InlineData(1.00f, 1.00f, 0.00f, "yellow")]
    [InlineData(0.00f, 0.00f, 1.00f, "blue")]
    [InlineData(1.00f, 0.00f, 1.00f, "magenta")]
    public void Each_seen_colour_has_its_word(float r, float g, float b, string word)
    {
        Assert.Equal(word, A330Colours.Name(new A330Rgb(r, g, b)));
        Assert.Equal(word, TestCulture.Run("de-DE", () => A330Colours.Name(new A330Rgb(r, g, b))));
    }

    [Fact]
    public void A_text_with_no_colour_has_no_colour_word()
    {
        var none = new A330Text(0, 0, 0, "A", "F", 10, null, new float[] { 0 });
        var amber = none with { Colour = new A330Rgb(0.98f, 0.76f, 0.29f) };
        Assert.Null(A330Colours.Word(none));
        Assert.Equal("amber", A330Colours.Word(amber));
    }
}
