using System.Globalization;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The bbs757 layout records serialise and load back unchanged through the generic layout file format,
/// byte-identical under any culture (WASM-21), and a damaged file is refused (WASM-26).</summary>
public class Bbs757LayoutFileTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;
    private const string Hash0 = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Hash1 = "1111111111111111111111111111111111111111111111111111111111111111";

    private static Bbs757FmaLayout SampleLayout() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        PfdVptr: 0x1406c,
        PfdToAfds: 612,
        PfdToTmc: 616,
        Pitch: 11924,
        PitchArmed: 11944,
        Roll: 11932,
        RollArmed: 11940,
        AtMode: 444,
        AtWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "SPD", [2] = "EPR", [3] = "THR HLD", [4] = "IDLE", [5] = "FLCH", [6] = "GA",
            [7] = "FLAP LIM", [8] = "ALPHA", [9] = "SPD LIM",
        }),
        PitchWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "TO", [2] = "ALT HOLD", [3] = "V/S", [4] = "SPD", [6] = "VNAV PTH", [7] = "VNAV SPD",
            [9] = "G/S", [10] = "FLARE", [11] = "ALT CAP", [12] = "GA", [13] = "SPD LIM", [14] = "FLAP LIM",
        }),
        PitchArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [5] = "VNAV", [9] = "G/S", [10] = "FLARE" }),
        RollWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [1] = "HDG HOLD", [2] = "HDG SEL", [3] = "LOC", [4] = "ROLLOUT", [5] = "LNAV", [6] = "B/CRS",
            [8] = "TO", [9] = "GA",
        }),
        RollArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [3] = "LNAV", [4] = "LOC", [5] = "B/CRS", [6] = "ROLLOUT" }));

    private static Bbs757EfbMemLayout SampleEfbLayout() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        EfbVptr: 0x13128,
        EfbToBbos: 132,
        BbosToAppMap: 0x238,
        AppFront: 0x140,
        AppPage: 0x144,
        AppRoots: 0x17c,
        PageChildren: 0x4c,
        CtrlName: 0x1c,
        CtrlRect: 0x38,
        CtrlVisible: 0x8c,
        CtrlChildren: 0xf4,
        CtrlParent: 0xe8,
        CtrlLabelText: 0x128,
        CtrlButtonText: 0x144,
        ScrollboxOffset: 0x134,
        SliderKnob: 0x184,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("scrollbox", 0x30400),
            new Bbs757EfbClassVptr("button", 0x30164), new Bbs757EfbClassVptr("label", 0x30cd4),
            new Bbs757EfbClassVptr("toggle", 0x30e7c), new Bbs757EfbClassVptr("slider", 0x30c48),
            new Bbs757EfbClassVptr("grid", 0x30320), new Bbs757EfbClassVptr("HOME", 0x27bdc),
            new Bbs757EfbClassVptr("SETTINGS", 0x2c860),
        });

    private static LayoutFile FileWith(Bbs757FmaLayout layout) => FileWith(layout, SampleEfbLayout());

    private static LayoutFile FileWith(Bbs757FmaLayout fma, Bbs757EfbMemLayout efb) => new(
        GeneratorVersion: 1,
        CodeHash: Hash0,
        DataRanges: new[] { new DataRange(0x400, 64) },
        DataHash: Hash1,
        Searches: Array.Empty<DataSearch>(),
        Readers: new[] { new ReaderResult(Bbs757Module.Fma, fma, null), new ReaderResult(Bbs757Module.Efb, efb, null) });

    [Fact]
    public void A_fma_layout_round_trips_through_the_format()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757FmaLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Fma).Layout!;
        Assert.Equal(SampleLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
    }

    [Fact]
    public void An_efb_layout_round_trips_through_the_format()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(SampleEfbLayout() with { SourceFile = "" }, loaded);
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
    }

    [Fact]
    public void The_efb_layout_with_the_scroll_offset_round_trips()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        // The content offset (+0x134, B757-26), never the scrollbar thumb at +0x158.
        Assert.Contains("\"scrollboxOffset\": 308", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(0x134, loaded.ScrollboxOffset);
        Assert.Equal("scrollbox", loaded.KindFor(0x30400));
    }

    [Fact]
    public void A_layout_without_the_scroll_offset_reads_as_missing()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string damaged = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"scrollboxOffset\": 308,?", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void The_efb_layout_with_the_slider_knob_round_trips()
    {
        // A slider keeps its knob's rectangle at +0x184 (four floats, measured live 2026-11-07).
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        Assert.Contains("\"sliderKnob\": 388", text);
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757EfbMemLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.Efb).Layout!;
        Assert.Equal(0x184, loaded.SliderKnob);
        Assert.NotEqual(SampleEfbLayout(), SampleEfbLayout() with { SliderKnob = 0x188 });
        Assert.Contains(Bbs757LayoutDescriptions.DescribeEfb(loaded), line => line.Contains("sliderKnob 388", StringComparison.Ordinal));
    }

    [Fact]
    public void A_layout_without_the_slider_knob_reads_as_missing()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        string damaged = System.Text.RegularExpressions.Regex.Replace(text, "\\s*\"sliderKnob\": 388,?", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }

    [Fact]
    public void The_text_is_the_same_under_sv_SE_and_de_DE()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "sv-SE", "de-DE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(text, LayoutFileFormat.Write(Module, FileWith(SampleLayout())));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void A_missing_member_is_refused()
    {
        string text = LayoutFileFormat.Write(Module, FileWith(SampleLayout()));
        // Drop the atMode field: the loader must refuse rather than read it as 0 (WASM-26).
        string damaged = text.Replace("\"atMode\": 444,\n", "");
        Assert.NotEqual(text, damaged);
        Assert.False(LayoutFileFormat.TryRead(Module, damaged, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));
    }
}
