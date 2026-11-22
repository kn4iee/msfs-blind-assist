using MSFSBlindAssist.Aircraft.A330.Mcdu;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330McduCharactersTests
{
    private static A330McduCell Cell(char c, bool small = false) => new(c, small, null);

    [Fact]
    public void A_box_shows_as_the_apps_box_character()
    {
        Assert.Equal("-", A330McduCharacters.Display(Cell('o')));
        Assert.Equal("-", A330McduCharacters.Display(Cell('o', small: true)));
        Assert.True(A330McduCharacters.IsBox('o'));
        Assert.False(A330McduCharacters.IsBox('d'));
        Assert.False(A330McduCharacters.IsBox('-'));
    }

    [Fact]
    public void The_degree_sign_and_the_arrows_show_as_themselves()
    {
        Assert.Equal("\u00B0", A330McduCharacters.Display(Cell('d')));
        Assert.Equal("\u2190", A330McduCharacters.Display(Cell('a')));
        Assert.Equal("\u2192", A330McduCharacters.Display(Cell('b')));
        Assert.Equal("\u2191", A330McduCharacters.Display(Cell('e')));
        Assert.Equal("\u2193", A330McduCharacters.Display(Cell('f')));
    }

    [Fact]
    public void An_unknown_code_shows_as_itself()
    {
        foreach (char c in "ABZ09*<>[]()+-./:=%x?\u00E9")
            Assert.Equal(c.ToString(), A330McduCharacters.Display(Cell(c)));
    }

    [Fact]
    public void Every_code_in_the_table_gives_exactly_one_character()
    {
        foreach (char c in "odabcef\"*<>[]()+-./:=%")
            Assert.Single(A330McduCharacters.Display(Cell(c)));
    }

    [Fact]
    public void The_overfly_mark_and_the_ditto_show_as_the_table_says()
    {
        Assert.Equal("\u0394", A330McduCharacters.Display(Cell('c')));
        Assert.Equal("\"", A330McduCharacters.Display(Cell('"')));
    }
}
