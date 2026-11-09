using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The upper EICAS layout record round-trips through the generic layout file format, byte-identical under any
/// culture (WASM-21), a file lacking its reader or one of its members is refused (WASM-26), and the module lists every
/// 757 reader in order.</summary>
public class Bbs757UpperEicasLayoutTests
{
    private static readonly ModuleDescription Module = Bbs757Module.Description;

    /// <summary>This build's values, as the plan's measurements recorded them (MaxTextLength is a stand-in).</summary>
    internal static Bbs757UpperEicasLayout Sample() => new(
        SourceFile: "",
        AnchorAddress: 0x636930,
        Anchor: new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
        ManagerBase: 0xf1334,
        ManagerSpan: 0x100,
        UpperEicasVptr: 0x133e0,
        ToComputer: 132,
        ToEngineLeft: 136,
        ToEngineRight: 140,
        ToLower: 272,
        ToPneumatic: 276,
        ToFms: 284,
        ToFire: 288,
        ToReverserLeft: 308,
        ToReverserRight: 312,
        ToTmc: 316,
        ToTat: 328,
        Powered: 4,
        Failed: 5,
        TimedBlank: 64,
        Compact: 416,
        EgtRed: 192,
        EgtAmber: 196,
        N1Red: 208,
        N1Amber: 220,
        MaxN1Left: 336,
        MaxN1Right: 340,
        MaxEgtLeft: 344,
        MaxEgtRight: 348,
        EnvelopeRows: 420,
        EnvelopeRowStride: 36,
        EnvelopeRowCount: 3,
        MainList: 0xf0a64,
        CommunicationList: 0xf0a84,
        EntryText: 0,
        EntryLength: 4,
        EntryId: 12,
        EntryCancelled: 15,
        EntryLevel: 48,
        MaxMessages: 278,
        MaxTextLength: 46,
        Levels: new Bbs757WordTable(new Dictionary<int, string>
        {
            [4] = "warning", [3] = "caution", [2] = "advisory", [1] = "communication", [0] = "communication",
        }),
        LinesPerPage: 10,
        CommunicationLinesBase: 11,
        ComputerPage: 344,
        ComputerRecallTime: 352,
        ComputerBlank: 364,
        Clock: 0x1244e8,
        RecallShowSeconds: 0.5,
        PageWord: "PAGE",
        RecallWord: "RECALL",
        EngineBlock: 3576,
        BlockEgt: 0,
        BlockN1: 8,
        BlockEpr: 56,
        EngineN1Alternate: 4104,
        EngineN1AlternateFlags: new[] { 6922, 6960, 6992 },
        EngineValid: 1944,
        EngineInvalidValue: 2,
        EngineType: 3768,
        EngineTypePw: 1,
        EngineCombustion: 3992,
        PwBlankBelow: 5.0,
        BlockN2Pw: 16,
        ThrustMode: 452,
        ThrustNumber: 456,
        Derate: 3092,
        DtoFlags: new[] { 3096, 3228 },
        DerateShown: 3016,
        ReferenceEpr: 480,
        ManualEprLeft: 2976,
        ManualEprRight: 2984,
        TmcFailed: 408,
        TmcChannelLeft: 440,
        TmcChannelRight: 441,
        ThrustWords: new Bbs757WordTable(new Dictionary<int, string>
        {
            [0] = "TO", [4] = "CLB", [5] = "CLB1", [6] = "CLB2", [7] = "CRZ", [8] = "CRZ", [9] = "CRZ",
            [10] = "CON", [11] = "G/A",
        }),
        DtoWord: "D-TO",
        ManualWord: "MAN",
        NumberedModes: new[] { 0, 7, 8, 9 },
        InhibitModes: new[] { 0, 1, 2, 3, 11 },
        TatValue: 344,
        ReverserState: 664,
        PneumaticComponents: 1932,
        ComponentStride: 184,
        ComponentId: 4,
        ComponentPosition: 72,
        ComponentState: 52,
        TaiLeftId: 16,
        TaiRightId: 22,
        TaiMinOpen: 7.0,
        TaiStates: new[] { 0, 1 },
        MaxComponents: 64,
        FmsOnGround: 1513,
        FireSwitchLeft: 400,
        FireSwitchRight: 401,
        LowerMode: 576,
        EnvelopeLowerMode: 1,
        EnvelopeTitle: "IN-FLT START",
        EnvelopeFl: "FL",
        EnvelopeKts: "KTS");

    private static LayoutFile FileWithAll(params string[] without) => new(
        GeneratorVersion: 1,
        CodeHash: new string('0', 64),
        DataRanges: new[] { new DataRange(0x400, 64) },
        DataHash: new string('1', 64),
        Searches: Array.Empty<DataSearch>(),
        Readers: new[]
        {
            new ReaderResult(Bbs757Module.Fma, Bbs757LayoutFileTests.SampleFmaLayout(), null),
            new ReaderResult(Bbs757Module.Efb, Bbs757LayoutFileTests.SampleEfbMemLayout(), null),
            new ReaderResult(Bbs757Module.LowerEicas, Bbs757LowerEicasLayoutTests.Sample(), null),
            new ReaderResult(Bbs757Module.UpperEicas, Sample(), null),
        }.Where(r => !without.Contains(r.Reader)).ToArray());

    [Fact]
    public void The_upper_eicas_layout_round_trips_byte_identical()
    {
        var expected = Sample() with { SourceFile = "" };
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        Assert.True(LayoutFileFormat.TryRead(Module, text, out var file, out string? failure), failure);
        var loaded = (Bbs757UpperEicasLayout)file!.Readers.Single(r => r.Reader == Bbs757Module.UpperEicas).Layout!;
        Assert.Equal(expected, loaded);
        Assert.Equal(expected.GetHashCode(), loaded.GetHashCode());
        Assert.Equal(text, LayoutFileFormat.Write(Module, file));
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(text, TestCulture.Run(culture, () => LayoutFileFormat.Write(Module, FileWithAll())));
            Assert.True(TestCulture.Run(culture, () =>
                LayoutFileFormat.TryRead(Module, text, out var f2, out _)
                && ((Bbs757UpperEicasLayout)f2!.Readers.Single(r => r.Reader == Bbs757Module.UpperEicas).Layout!).Equals(expected)),
                culture);
        }
        Assert.Equal(0.5, loaded.RecallShowSeconds);
        Assert.Equal(7.0, loaded.TaiMinOpen);
        Assert.Equal("warning", loaded.Levels.Lookup(4));
        Assert.Contains(Bbs757LayoutDescriptions.DescribeUpperEicas(loaded),
            line => line.Contains("recallShowSeconds 0.5", StringComparison.Ordinal));

        // Equality is content equality over every field but the source file.
        Assert.Equal(Sample(), Sample() with
        {
            SourceFile = "C:\\elsewhere.json",
            Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            DtoFlags = new[] { 3096, 3228 },
        });
        Assert.NotEqual(Sample(), Sample() with { DtoFlags = new[] { 3096, 3229 } });
        Assert.NotEqual(Sample(), Sample() with { Anchor = new byte[] { 1, 2, 3, 4, 5, 6, 7, 9 } });
        Assert.NotEqual(Sample(), Sample() with { TaiMinOpen = 7.5 });
        Assert.NotEqual(Sample(), Sample() with { EnvelopeKts = "KT" });
        Assert.NotEqual(Sample(), Sample() with
        {
            Levels = new Bbs757WordTable(new Dictionary<int, string> { [4] = "warning" }),
        });
    }

    [Fact]
    public void A_file_without_the_upper_eicas_reader_reads_as_missing()
    {
        // A file written before this reader has no entry for it: refused whole, never read with zeros (WASM-26).
        string text = LayoutFileFormat.Write(Module, FileWithAll());
        var root = JsonNode.Parse(text)!;
        Assert.True(root["readers"]!.AsObject().Remove(Bbs757Module.UpperEicas));
        string old = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        using (JsonDocument.Parse(old)) { }   // still JSON: refused for the missing reader, not its syntax
        Assert.False(LayoutFileFormat.TryRead(Module, old, out _, out string? failure));
        Assert.False(string.IsNullOrEmpty(failure));

        // A layout lacking one of its members is refused too.
        var root2 = JsonNode.Parse(text)!;
        Assert.True(root2["readers"]![Bbs757Module.UpperEicas]!["layout"]!.AsObject().Remove("toTmc"));
        Assert.False(LayoutFileFormat.TryRead(Module, root2.ToJsonString(), out _, out string? failure2));
        Assert.False(string.IsNullOrEmpty(failure2));
    }

    [Fact]
    public void Every_757_reader_is_in_the_module_in_order()
    {
        Assert.Equal(new[] { "fma", "efb", "lowerEicas", "upperEicas" }, Module.Readers.Select(r => r.Id).ToArray());
    }
}
