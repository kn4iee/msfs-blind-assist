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
}
