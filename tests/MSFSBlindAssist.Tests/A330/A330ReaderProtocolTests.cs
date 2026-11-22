using MSFSBlindAssist.Aircraft.A330.Display;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330ReaderProtocolTests
{
    private const string FrameLine =
        "{\"frame\":{\"gauge\":\"McduCaptain\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":6,\"x\":22,\"baseline\":43,\"text\":\"          INIT\",\"font\":\"FMGSfont_A\",\"size\":29.5,\"colour\":[1,1,1],\"charX\":[22,39]}]}}";

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(
        ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A330", "Fixtures", name));

    [Fact]
    public void Each_request_is_one_line_of_the_documented_form()
    {
        Assert.Equal("{\"hello\":1}", A330ReaderProtocol.Hello());
        Assert.Equal("{\"want\":\"McduCaptain\",\"refreshMs\":250}", A330ReaderProtocol.Want(A330Gauge.McduCaptain, 250));
        Assert.Equal("{\"release\":\"McduCaptain\"}", A330ReaderProtocol.Release(A330Gauge.McduCaptain));
        Assert.Equal("{\"reset\":true}", A330ReaderProtocol.Reset());
        Assert.Equal("{\"quit\":true}", A330ReaderProtocol.Quit());
        foreach (var s in new[] { A330ReaderProtocol.Hello(), A330ReaderProtocol.Want(A330Gauge.Ewd, 100),
                     A330ReaderProtocol.Release(A330Gauge.Ewd), A330ReaderProtocol.Reset(), A330ReaderProtocol.Quit() })
            Assert.DoesNotContain('\n', s);
    }

    [Fact]
    public void A_frame_line_parses_into_a_frame()
    {
        var line = A330ReaderProtocol.Parse(FrameLine, 12345);
        Assert.NotNull(line);
        Assert.Null(line!.State);
        Assert.Null(line.HelloVersion);
        Assert.Null(line.Scratchpad);
        var f = line.Frame!;
        Assert.Equal(A330Gauge.McduCaptain, f.Gauge);
        Assert.True(f.Complete);
        Assert.True(f.Drawn);
        Assert.False(f.HiddenRowsLeftOut);
        Assert.Equal(0, f.Missing);
        Assert.Equal(12345, f.ReadTicks);
        var t = Assert.Single(f.Texts);
        Assert.Equal(6, t.Order);
        Assert.Equal(22f, t.X);
        Assert.Equal(43f, t.Baseline);
        Assert.Equal("          INIT", t.Text);
        Assert.Equal("FMGSfont_A", t.Font);
        Assert.Equal(29.5f, t.Size);
        Assert.Equal(new A330Rgb(1, 1, 1), t.Colour);
        Assert.Equal(new float[] { 22, 39 }, t.CharX);
    }

    [Fact]
    public void A_frame_line_for_an_undrawn_display_reads_drawn_false()
    {
        var line = A330ReaderProtocol.Parse(
            "{\"frame\":{\"gauge\":\"McduCaptain\",\"drawn\":false,\"complete\":false,\"missing\":0,\"texts\":[]}}", 7);
        var f = line!.Frame!;
        Assert.False(f.Drawn);
        Assert.False(f.Complete);
        Assert.Empty(f.Texts);
    }

    [Fact]
    public void A_frame_line_without_colour_parses()
    {
        var line = A330ReaderProtocol.Parse(
            "{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":1,\"x\":1,\"baseline\":2,\"text\":\"A\",\"font\":\"F\",\"size\":10,\"charX\":[1]}]}}", 1);
        Assert.Null(Assert.Single(line!.Frame!.Texts).Colour);
    }

    [Fact]
    public void A_frame_line_without_hiddenRowsLeftOut_reads_false()
    {
        var absent = A330ReaderProtocol.Parse(
            "{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[]}}", 1);
        Assert.False(absent!.Frame!.HiddenRowsLeftOut);
        var present = A330ReaderProtocol.Parse(
            "{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"hiddenRowsLeftOut\":true,\"texts\":[]}}", 1);
        Assert.True(present!.Frame!.HiddenRowsLeftOut);
    }

    [Theory]
    [InlineData("Starting", A330ReaderState.Starting)]
    [InlineData("Running", A330ReaderState.Running)]
    [InlineData("SimulatorNotFound", A330ReaderState.SimulatorNotFound)]
    [InlineData("NoAccess", A330ReaderState.NoAccess)]
    [InlineData("AircraftNotFound", A330ReaderState.AircraftNotFound)]
    [InlineData("NotRecognised", A330ReaderState.NotRecognised)]
    [InlineData("Stopped", A330ReaderState.Stopped)]
    public void A_state_line_parses(string name, A330ReaderState expected)
    {
        var line = A330ReaderProtocol.Parse("{\"state\":\"" + name + "\"}", 1);
        Assert.Equal(expected, line!.State);
        Assert.Null(line.Frame);
    }

    [Fact]
    public void A_scratchpad_line_parses()
    {
        var line = A330ReaderProtocol.Parse("{\"scratchpad\":{\"gauge\":\"McduCaptain\",\"text\":\"TQXZ\"}}", 1);
        Assert.Equal((A330Gauge.McduCaptain, "TQXZ"), line!.Scratchpad);
        Assert.Null(line.Frame);
    }

    [Fact]
    public void A_hello_with_its_version_parses()
    {
        var line = A330ReaderProtocol.Parse("{\"hello\":1,\"reader\":\"0.3.0\"}", 1);
        Assert.Equal(1, line!.HelloVersion);
        Assert.Equal("0.3.0", line.ReaderVersion);
        Assert.Null(line.State);
        var bare = A330ReaderProtocol.Parse("{\"hello\":1}", 1);
        Assert.Equal(1, bare!.HelloVersion);
        Assert.Null(bare.ReaderVersion);
    }

    [Theory]
    [InlineData("{\"state\":\"NotInstalled\"}")]
    [InlineData("{\"state\":\"OutOfDate\"}")]
    [InlineData("{\"state\":\"Failed\"}")]
    [InlineData("{\"state\":\"Bogus\"}")]
    [InlineData("{\"state\":\"running\"}")]
    [InlineData("{\"state\":\"2\"}")]
    [InlineData("{\"state\":\"Starting,Running\"}")]
    [InlineData("{\"state\":2}")]
    [InlineData("{\"scratchpad\":{\"gauge\":\"Nope\",\"text\":\"X\"}}")]
    [InlineData("{\"scratchpad\":{\"gauge\":\"mcducaptain\",\"text\":\"X\"}}")]
    [InlineData("{\"scratchpad\":{\"gauge\":\"0\",\"text\":\"X\"}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Nope\",\"complete\":true,\"missing\":0,\"texts\":[]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"McduCaptain,Ewd\",\"complete\":true,\"missing\":0,\"texts\":[]}}")]
    public void An_unknown_gauge_or_state_gives_null(string line)
    {
        Assert.Null(A330ReaderProtocol.Parse(line, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("{\"state\":\"Running\"} trailing")]
    [InlineData("[]")]
    [InlineData("[{\"state\":\"Running\"}]")]
    [InlineData("\"Running\"")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{\"hello\":\"one\"}")]
    [InlineData("{\"hello\":99999999999}")]
    [InlineData("{\"frame\":5}")]
    [InlineData("{\"frame\":{}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\"}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":99999999999,\"texts\":[]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":\"yes\",\"missing\":0,\"texts\":[]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":{}}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[5]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":99999999999,\"x\":1,\"baseline\":2,\"text\":\"A\",\"font\":\"F\",\"size\":1,\"charX\":[1]}]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":1,\"x\":\"a\",\"baseline\":2,\"text\":\"A\",\"font\":\"F\",\"size\":1,\"charX\":[1]}]}}")]
    [InlineData("{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":1,\"x\":1,\"baseline\":2,\"text\":\"A\",\"font\":\"F\",\"size\":1,\"colour\":[1,1],\"charX\":[1]}]}}")]
    [InlineData("{\"scratchpad\":{\"gauge\":\"Ewd\"}}")]
    [InlineData("{\"scratchpad\":\"x\"}")]
    public void A_broken_line_gives_null_and_never_throws(string line)
    {
        Assert.Null(A330ReaderProtocol.Parse(line, 1));
        Assert.Null(A330ReaderProtocol.ParseFrame(line, 1));
    }

    [Fact]
    public void A_null_string_gives_null_and_never_throws()
    {
        Assert.Null(A330ReaderProtocol.Parse(null!, 1));
        Assert.Null(A330ReaderProtocol.ParseFrame(null!, 1));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Numbers_parse_the_same_under_de_DE_and_sv_SE(string culture)
    {
        const string line = "{\"frame\":{\"gauge\":\"Ewd\",\"complete\":true,\"missing\":0,\"texts\":[{\"order\":3,\"x\":22.5,\"baseline\":43.25,\"text\":\"AB\",\"font\":\"F\",\"size\":29.5,\"colour\":[0.5,0.25,0.75],\"charX\":[22.5,39.75]}]}}";
        var inv = A330ReaderProtocol.Parse(line, 1)!.Frame!.Texts[0];
        var loc = TestCulture.Run(culture, () => A330ReaderProtocol.Parse(line, 1)!.Frame!.Texts[0]);
        Assert.Equal(22.5f, loc.X);
        Assert.Equal(43.25f, loc.Baseline);
        Assert.Equal(29.5f, loc.Size);
        Assert.Equal(new A330Rgb(0.5f, 0.25f, 0.75f), loc.Colour);
        Assert.Equal(new float[] { 22.5f, 39.75f }, loc.CharX);
        Assert.Equal(inv.CharX, loc.CharX);
    }

    [Theory]
    [InlineData("p02_mcdu_init.json", A330Gauge.McduCaptain, 18, 146, 127)]
    [InlineData("p02_ewd_ground.json", A330Gauge.Ewd, 18, 71, 64)]
    public void The_two_fixtures_parse(string file, A330Gauge gauge, int texts, int chars, int nonSpace)
    {
        var f = A330ReaderProtocol.ParseFrame(Fixture(file), 5);
        Assert.NotNull(f);
        Assert.Equal(gauge, f!.Gauge);
        Assert.True(f.Complete);
        Assert.Equal(0, f.Missing);
        Assert.Equal(texts, f.Texts.Count);
        Assert.Equal(chars, f.Texts.Sum(t => t.Text.Length));
        Assert.Equal(nonSpace, f.Texts.Sum(t => t.Text.Count(c => c != ' ')));
        Assert.All(f.Texts, t => Assert.Equal(t.Text.Length, t.CharX.Count));
    }
}
