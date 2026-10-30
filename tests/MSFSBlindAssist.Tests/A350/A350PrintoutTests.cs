using MSFSBlindAssist.Aircraft.A350;
using Xunit;

namespace MSFSBlindAssist.Tests.A350;

public class A350PrintoutTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n \n")]
    public void No_paper_is_said(string? raw) => Assert.Equal(A350Printout.Empty, A350Printout.Text(raw));

    [Fact]
    public void Lines_keep_their_columns_and_end_as_a_text_box_wants()
    {
        Assert.Equal("FM ATC FLIGHT PLAN\r\n    DATE: 30   \tOCT\r\n\r\nEND",
            A350Printout.Text("\nFM ATC FLIGHT PLAN  \n    DATE: 30   \tOCT\r\n\nEND\n\n"));
    }

    [Fact]
    public void A_lone_carriage_return_breaks_a_line()
    {
        Assert.Equal("A\r\nB", A350Printout.Text("A\rB"));
    }
}
