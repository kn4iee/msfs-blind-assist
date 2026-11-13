using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// Plan 04 Task 4, from the autoland at VCBI on 2026-11-02 (1.2.8): the E/WD's engine block drew "REV" in green over each engine
// (x 256 and 504, y 120, "THR" between them at 408) with the thrust limit MAX, and nothing once stowed. The call a pilot monitoring
// makes: "Reverse green" once both read green; an engine whose REV stays amber past 3 s is named. The transit state was not caught
// at 2 Hz (it lasts under the reader's two-read agreement); the amber cases are built from the deployed capture.
public class A350ReverserCalloutTests
{
    private DateTime _now = new(2026, 11, 2, 8, 56, 50, DateTimeKind.Utc);
    private readonly List<string> _said = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);

    private A350EcamMonitor Monitor() => new(key => _muted.Contains(key), _said.Add, () => _now);

    private static A350DrawCapture Capture(string name) => A350Fixtures.Capture($"p04_ctr_rev_{name}.json");

    private static readonly A350Rgba Amber = new(1, 0.6f, 0, 1);

    /// <summary>The deployed capture with one engine's REV recoloured, or taken away (an engine shut down).</summary>
    private static A350DrawCapture Deployed(int? amber = null, int? missing = null)
    {
        var cap = Capture("deployed");
        bool Of(DrawnText t, int engine) => t.Text.Trim() == "REV" && (t.X < 408 ? 1 : 2) == engine;
        return cap with
        {
            Texts = cap.Texts.Where(t => missing is not int m || !Of(t, m))
                .Select(t => amber is int a && Of(t, a) ? t with { Colour = Amber } : t).ToList(),
        };
    }

    private void Observe(A350EcamMonitor m, A350DrawCapture cap, double afterSeconds = 0.5)
    {
        _now = _now.AddSeconds(afterSeconds);
        m.Observe(cap);
    }

    [Fact]
    public void The_engine_block_reads_each_reverser_with_its_colour()
    {
        Assert.Equal(new[] { (1, "green"), (2, "green") }, A350Ewd.Reversers(A350Ewd.Parse(Capture("deployed"))));
        Assert.Empty(A350Ewd.Reversers(A350Ewd.Parse(Capture("approach"))));
        Assert.Empty(A350Ewd.Reversers(A350Ewd.Parse(Capture("stowed"))));
    }

    [Fact]
    public void Both_reversers_green_say_reverse_green_once()
    {
        var m = Monitor();
        Observe(m, Capture("approach"));
        Observe(m, Capture("approach"));
        Observe(m, Capture("deployed"));
        Assert.DoesNotContain("Reverse green", _said);   // one read: not yet agreed
        Observe(m, Capture("deployed"));
        Observe(m, Capture("deployed"));
        Observe(m, Capture("stowed"));
        Observe(m, Capture("stowed"));
        Assert.Equal(1, _said.Count(s => s == "Reverse green"));
    }

    [Fact]
    public void One_amber_reverser_is_named_once_it_holds_3_s()
    {
        var m = Monitor();
        Observe(m, Deployed(amber: 2));
        Observe(m, Deployed(amber: 2), 1);
        Assert.DoesNotContain(_said, s => s.StartsWith("Reverse", StringComparison.Ordinal));   // in transit, perhaps
        Observe(m, Deployed(amber: 2), 2.5);
        Observe(m, Deployed(amber: 2), 1);
        Assert.Equal(new[] { "Reverse 2 amber" }, _said.Where(s => s.StartsWith("Reverse", StringComparison.Ordinal)));
    }

    // Whole-branch review (2026-11-13): an amber once said stayed remembered until no reverser showed at all, so a second amber
    // in the same deployment (a reverser hanging as it stows) was never said.
    [Fact]
    public void An_amber_that_comes_back_after_green_is_said_again()
    {
        var m = Monitor();
        Observe(m, Deployed(amber: 1));
        Observe(m, Deployed(amber: 1), 3.5);
        Observe(m, Deployed(), 1);
        Observe(m, Deployed(), 0.5);
        Observe(m, Deployed(amber: 1), 1);
        Observe(m, Deployed(amber: 1), 3.5);
        Assert.Equal(2, _said.Count(s => s == "Reverse 1 amber"));
    }

    [Fact]
    public void An_amber_that_turns_green_within_3_s_says_reverse_green_only()
    {
        var m = Monitor();
        Observe(m, Deployed(amber: 1));
        Observe(m, Deployed(amber: 1), 1);
        Observe(m, Deployed(), 1);
        Observe(m, Deployed(), 0.5);
        Assert.Equal(new[] { "Reverse green" }, _said.Where(s => s.StartsWith("Reverse", StringComparison.Ordinal)));
    }

    [Fact]
    public void With_one_engine_running_its_reverser_is_named()
    {
        var m = Monitor();
        Observe(m, Deployed(missing: 2));
        Observe(m, Deployed(missing: 2));
        Assert.Equal(new[] { "Reverse 1 green" }, _said.Where(s => s.StartsWith("Reverse", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_muted_reverser_row_says_nothing()
    {
        _muted.Add(A350EcamMonitor.ReverserKey);
        var m = Monitor();
        Observe(m, Deployed(amber: 2));
        Observe(m, Deployed(amber: 2), 4);
        Observe(m, Deployed());
        Observe(m, Deployed());
        Assert.DoesNotContain(_said, s => s.StartsWith("Reverse", StringComparison.Ordinal));
    }

    [Fact]
    public void The_reverser_row_is_in_the_ecam_call_out_rows()
    {
        Assert.Contains((A350EcamMonitor.ReverserKey, "Reverser call-outs"), A350CalloutRows.Ecam);
        Assert.Equal("A350_ECAM_REVERSERS", A350EcamMonitor.ReverserKey);
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Reverse_green_reads_the_same_under_sv_SE(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var m = Monitor();
            Observe(m, Deployed(amber: 2));
            Observe(m, Deployed(amber: 2), 4);
            Observe(m, Deployed());
            Observe(m, Deployed());
            Assert.Equal(new[] { "Reverse 2 amber", "Reverse green" }, _said.Where(s => s.StartsWith("Reverse", StringComparison.Ordinal)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
