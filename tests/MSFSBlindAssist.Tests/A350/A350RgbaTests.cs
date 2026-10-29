using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// A cell keeps only its colour's name; a reader that rebuilds a text from it gets the colour back. A name missing
// from that way back read as white (live: the MFD's yellow temporary revision read white).
public class A350RgbaTests
{
    [Theory]
    [InlineData("green")]
    [InlineData("amber")]
    [InlineData("cyan")]
    [InlineData("magenta")]
    [InlineData("white")]
    [InlineData("red")]
    [InlineData("yellow")]
    [InlineData("grey")]
    [InlineData("black")]
    public void Every_colour_name_maps_back_to_itself(string name)
    {
        Assert.Equal(name, A350Rgba.NameOf(A350Rgba.Of(name)));
    }

    // Review 2026-10-29: the vertical display's MSA and MORA are drawn in the A350's soft magenta (0.90, 0.53, 0.90), a
    // sighted pilot's pink, which the nearest pure colour named white (0.241 against magenta's 0.301). The LS block's
    // magenta (1, 0.47, 0.91) stays magenta.
    [Theory]
    [InlineData(0.901f, 0.529f, 0.901f)]
    [InlineData(1f, 0.467f, 0.914f)]
    public void The_a350s_soft_magentas_read_magenta(float r, float g, float b) =>
        Assert.Equal("magenta", A350Rgba.NameOf(new A350Rgba(r, g, b, 1)));
}
