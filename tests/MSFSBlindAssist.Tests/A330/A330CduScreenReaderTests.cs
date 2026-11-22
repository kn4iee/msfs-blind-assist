using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CduColourTests
{
    [Theory]
    [InlineData(1, 1, 1, 220, "white")]
    [InlineData(1, 1, 1, 77, "grey")]
    [InlineData(1.55, 1.15, 0.30, 200, "amber")]
    [InlineData(0.55, 1.45, 1.0, 230, "cyan")]
    [InlineData(0.6, 1.7, 0.7, 200, "green")]
    [InlineData(1.45, 0.45, 1.1, 210, "magenta")]
    public void Classifies_the_MCDU_colours(double r, double g, double b, double peak, string colour)
        => Assert.Equal(colour, CduColour.Classify(r, g, b, peak));

    // the app normalises glyphs with the layout's letter height and the trainer with BASE_CAP;
    // 18.5 against 18.3 scaled every glyph 1 % off its templates and read "<TO DATA" as "<TO OATA"
    [Fact]
    public void Layout_letter_height_matches_the_trainer()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "tools", "a330-cdu-ocr", "ocr2.py"));
        var m = System.Text.RegularExpressions.Regex.Match(src, @"^BASE_CAP\s*=\s*([0-9.]+)", System.Text.RegularExpressions.RegexOptions.Multiline);
        Assert.True(m.Success, "BASE_CAP not found in tools/a330-cdu-ocr/ocr2.py");
        Assert.Equal(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), CduLayout.A330.BaseCap, 3);
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Grey_words_are_marked_unavailable_once_per_run()
    {
        var text = "<FM1 NAV B/UP>";
        var colours = text.Select((ch, i) => ch == ' ' ? "" : i < 4 ? CduColour.Green : CduColour.Grey).ToArray();
        Assert.Equal("<FM1 NAV B/UP> (unavailable)", CduLineText.Annotate(text, colours));
    }

    [Fact]
    public void A_line_without_grey_is_unchanged()
    {
        var text = "<MONITOR";
        Assert.Equal(text, CduLineText.Annotate(text, text.Select(_ => CduColour.White).ToArray()));
    }

    [Fact]
    public void The_A330_profile_types_punctuation_with_its_own_key_names()
    {
        Assert.Equal("DECIMAL", CduFormProfile.A330KeyForChar('.'));
        Assert.Equal("PLUS", CduFormProfile.A330KeyForChar('-'));
        Assert.Equal("SPACE", CduFormProfile.A330KeyForChar(' '));
        Assert.Equal("Q", CduFormProfile.A330KeyForChar('Q'));
    }

    // Key for key, as the FlyByWire A320 window ("UP" on Page Up) and the Fenix window ("ARROW_UP" on Page Up):
    // Page Up presses the MCDU's up-arrow key. The window had the two swapped until 2026-11-20.
    [Fact]
    public void The_A330_window_slews_key_for_key_like_the_other_Airbus_windows()
    {
        var profile = CduFormProfile.A330(null!);   // the glyph library is only used when a reader is created
        Assert.Equal("UARROW", profile.SlewUp);
        Assert.Equal("DOWN", profile.SlewDown);
        Assert.Contains(("Slew up", "UARROW", "Page Up"), profile.PageKeys);
        Assert.Contains(("Slew down", "DOWN", "Page Down"), profile.PageKeys);
    }
}

/// <summary>
/// End-to-end check of the pixel reader on the A330 MCDU captures committed with the trainer
/// (tools/a330-cdu-ocr/data/big: three F-PLN pages, the ones that taught it brackets and the ditto
/// mark). These are training pages, so this pins the reader and the embedded glyph library against
/// regression rather than measuring accuracy on unseen pages (the trainer's own evaluation does that).
/// </summary>
public class A330CduScreenReaderTests
{
    private static string DataDir => Path.Combine(CduColourTests.RepoRoot(), "tools", "a330-cdu-ocr", "data", "big");

    public static IEnumerable<object[]> Pages()
        => Directory.GetFiles(Path.Combine(CduColourTests.RepoRoot(), "tools", "a330-cdu-ocr", "data", "big"), "*.png")
            .Select(p => new object[] { Path.GetFileNameWithoutExtension(p) });

    private static string Collapse(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    [Theory]
    [MemberData(nameof(Pages))]
    public void Reads_every_line_of_a_committed_capture_exactly(string page)
    {
        var truth = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "truth.json")))
            .RootElement.GetProperty(page);
        var frame = A330CduFrame.FromFile(Path.Combine(DataDir, page + ".png"));
        var lines = new A330CduScreenReader(A330CduGlyphLibrary.A330, CduLayout.A330).Read(frame.Lum, frame.Width, frame.Height, frame.Rgb);
        var errors = new List<string>();
        for (int r = 0; r < A330CduScreenReader.Rows; r++)
        {
            string expected = truth.TryGetProperty(r.ToString(), out var e) ? e.GetString()! : "";
            string got = Collapse(lines[r].Text);
            if (got != Collapse(expected)) errors.Add($"row {r}: expected '{expected}' got '{got}'");
        }
        Assert.True(errors.Count == 0, $"{page}:\n" + string.Join("\n", errors));
    }
}
