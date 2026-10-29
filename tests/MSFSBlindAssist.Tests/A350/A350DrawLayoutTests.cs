using System.Text.Json;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

// The layout turns a gauge's drawn texts into rows in reading order and cuts a gauge into the displays
// it draws. Fixture: the CTR (E/WD + SD) drawn live on the stand at VCBI, APU running, engines off
// (A350Probe draw with A350_DRAW_JSON). Expected rows are the prototype's toLines over the same texts.
public class A350DrawLayoutTests
{
    private static A350DrawCapture Ctr() =>
        A350Fixtures.Capture("ctr_parked_apu.json");

    private static string Show(IReadOnlyList<DrawnCell> row) => string.Join(" | ", row.Select(c => c.Text + "{" + c.Colour + "}"));

    // Live 2026-10-27 (IRs aligning): the captain's EFIS draws the ND on its left half and the PFD on its right
    // ("ATT" flag at x 1163); the first officer's the other way round ("ATT" at x 327).
    [Fact]
    public void Each_side_draws_its_pfd_on_its_own_half()
    {
        var cpt = A350Fixtures.Capture("efis0_aligning.json");
        Assert.Contains(A350DrawLayout.Region(cpt.Texts, A350DrawLayout.EfisPfd(0)), t => t.Text == "ATT");
        Assert.Contains(A350DrawLayout.Region(cpt.Texts, A350DrawLayout.EfisNd(0)), t => t.Text == "MAP NOT AVAIL");
        Assert.True(A350DrawLayout.EfisPfd(1).Contains(327, 399));
        Assert.True(A350DrawLayout.EfisNd(1).Contains(1098, 332));
    }

    [Fact]
    public void The_warning_display_reads_in_rows_with_colours()
    {
        var wd = A350DrawLayout.Region(Ctr().Texts, A350DrawLayout.CtrWarningDisplay);
        Assert.Equal(new[]
        {
            "MAILBOX NOT AVAIL{amber}", "SURV SYS 1+2 FAULT{amber}", "XPDR MODE A ONLY{cyan}", "CLEAR{cyan}",
            "F/CTL SLAT SYS 1+2 FAULT{amber}", "...{white}", "AIR XBLEED FAULT{amber}", "...{white}",
            "SURV TCAS STBY{amber}", "FWS FWS 2 FAULT{amber}",
        }, A350DrawLayout.Rows(wd).Select(Show));
    }

    [Fact]
    public void Cells_of_one_row_read_left_to_right()
    {
        var rows = A350DrawLayout.Rows(A350DrawLayout.Region(Ctr().Texts, A350DrawLayout.CtrPermanentData)).Select(Show).ToList();
        Assert.Equal("+28{green} | FLT{white} | 00{green} | :{green} | 06{green} | --{cyan} | LBS{cyan}", rows[0]);
        // ISA +14 (y 1015) and the fuel on board (y 1013) are one visual line, read together.
        Assert.Equal("ISA{white} | +14{green} | 223700{green} | LBS{cyan}", rows[^1]);
    }

    [Fact]
    public void The_engine_display_and_the_sd_are_cut_apart()
    {
        var texts = Ctr().Texts;
        var eng = A350DrawLayout.Rows(A350DrawLayout.Region(texts, A350DrawLayout.CtrEngineDisplay)).Select(Show).ToList();
        Assert.Equal("THR XX{amber} | THR XX{amber}", eng[1]);
        Assert.DoesNotContain(eng, r => r.Contains("ACCU"));
        var sd = A350DrawLayout.Rows(A350DrawLayout.Region(texts, A350DrawLayout.CtrSystemDisplay)).Select(Show).ToList();
        Assert.Contains("Y ACCU{white}", sd);
        Assert.Contains("45{green} | 45{green} | 35{green} | 45{green}", sd);
    }

    [Fact]
    public void Blank_texts_are_dropped_and_text_is_trimmed()
    {
        var rows = A350DrawLayout.Rows(new[]
        {
            new DrawnText("  ", 10, 10, 10, 10, 1, null),
            new DrawnText(" PARK BRK ", 50, 11, 50, 11, 1, new A350Rgba(0, 1, 0, 1)),
        });
        Assert.Equal("PARK BRK{green}", Show(Assert.Single(rows)));
    }

    [Fact]
    public void A_row_is_spoken_with_its_colours_by_run()
    {
        var row = new List<DrawnCell>
        {
            new("PARK", "green", 0, 0), new("BRK", "green", 10, 0), new("ON", "amber", 20, 0),
        };
        Assert.Equal("PARK BRK, green; ON, amber", A350DrawLayout.Speak(row));
        Assert.Equal("SURV SYS 1+2 FAULT, amber", A350DrawLayout.Speak(new[] { new DrawnCell("SURV SYS 1+2 FAULT", "amber", 0, 0) }));
    }
}
