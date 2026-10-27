using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Engine 2 started on the stand at VCBI (IGN START, master 2, APU bleed), 2026-10-27: the E/WD engine block and
// the SD ENG page with real numbers. A number with a decimal is drawn as two texts ("58." and a smaller "1",
// anchored 2 px apart), which must read as one.
public class A350EngineDisplayTests
{
    private static A350DrawCapture Fixture(string name) =>
        JsonSerializer.Deserialize<A350DrawCapture>(File.ReadAllText(Path.Combine(
            ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A350", "Fixtures", name + ".json")))!;

    [Fact]
    public void A_split_decimal_reads_as_one_number()
    {
        var texts = new[]
        {
            new DrawnText("58.", 645, 395, 645, 395, 1, new A350Rgba(0.15f, 0.85f, 0.1f, 1)),
            new DrawnText("1", 643, 395, 643, 395, 1, new A350Rgba(0.15f, 0.85f, 0.1f, 1)),
        };
        var row = Assert.Single(A350DrawLayout.Rows(texts));
        Assert.Equal("58.1", Assert.Single(row).Text);
    }

    [Fact]
    public void The_engine_block_names_thrust_n1_and_egt()
    {
        var block = A350SdPage.ReadEngineBlock(A350Ewd.Parse(Fixture("ctr_eng2_running")).Engine);
        Assert.Contains("Thrust limit mode: TOGA, cyan", block);
        Assert.Contains("Thrust limit: 100.0 %, green", block);
        Assert.Contains("Engine 2 THR: 2.5 %, green", block);
        Assert.Contains("Engine 2 N1: 20.1 %, green", block);
        Assert.Contains("Engine 2 EGT: 479 °C, green", block);
        Assert.Contains("Engine 1 N1: 0.0 %, green", block);
        Assert.Contains("Engine 1 EGT: 22 °C, green", block);
    }

    [Fact]
    public void Alt_e_reads_the_engine_block_by_name()
    {
        var lines = A350EwdText.Build(Fixture("ctr_eng2_running")).Split('\n');
        int engine = Array.IndexOf(lines, "Engine display:");
        Assert.Equal("Thrust limit mode: TOGA, cyan", lines[engine + 1]);
        Assert.Contains("Engine 2 N1: 20.1 %, green", lines);
    }

    [Fact]
    public void The_eng_page_reads_engine_2s_numbers()
    {
        var p = A350SdPage.Read(A350Ewd.Parse(Fixture("ctr_eng2_running")).SystemDisplay, 1);
        Assert.Contains("Engine 2 fuel flow: 1710 LBS/H, green", p);
        Assert.Contains("Engine 2 N2: 58.1 %, green", p);
        Assert.Contains("Engine 2 N3: 64.4 %, green", p);
        Assert.Contains("Engine 2 oil quantity: 17.1 QT, green", p);
        Assert.Contains("Engine 2 oil pressure: 122 PSI, green", p);
        Assert.Contains("Engine 1 oil pressure: 0 PSI, red", p);
        Assert.Contains("Engine 1 N2: 0.0 %, green", p);
    }

    [Fact]
    public void The_eng_page_reads_the_start_valve_and_sequence()
    {
        var starting = A350SdSymbols.Read(Fixture("ctr_eng2_starting").Shapes, 1);
        Assert.Contains("Engine 2 start valve: open, green", starting);
        Assert.Contains("Engine 2 start sequence: in progress", starting);
        Assert.Contains("Engine 1 start valve: closed, green", starting);
        var running = A350SdSymbols.Read(Fixture("ctr_eng2_running").Shapes, 1);
        Assert.Contains("Engine 2 start valve: closed, green", running);
        Assert.DoesNotContain(running, l => l.StartsWith("Engine 2 start sequence"));
    }
}
