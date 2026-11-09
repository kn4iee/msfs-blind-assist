using MSFSBlindAssist.Services.BBS757;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>
/// BlueBird Simulations' 757-200 module: its file (BBS752.wasm, in the one 757 package) and its display readers
/// in anchor-search order. Unlike the PMDG 737, this build has no name section; the readers reach their objects
/// through the gauge manager and C++ RTTI (see the layout records). The readers grow as their recipes land.
/// </summary>
public static class Bbs757Module
{
    public const string Fma = "fma";
    public const string Efb = "efb";
    public const string LowerEicas = "lowerEicas";
    public const string UpperEicas = "upperEicas";

    public static ModuleDescription Description { get; } = new(
        "bbs757",
        "BBS752.wasm",
        "^bluebirdsim-aircraft-757-200$",
        new[] { "SimObjects", "Airplanes", "bbs-757-200-base", "panel" },
        new LayoutReader[]
        {
            new LayoutReader<Bbs757FmaLayout>(Fma, "FMA",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.Describe),
            new LayoutReader<Bbs757EfbMemLayout>(Efb, "EFB",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeEfb),
            new LayoutReader<Bbs757LowerEicasLayout>(LowerEicas, "lower EICAS",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeLowerEicas, LowerEicasFailures),
            new LayoutReader<Bbs757UpperEicasLayout>(UpperEicas, "upper EICAS",
                l => new ModuleAnchor(l.AnchorAddress, l.Anchor), (l, s) => l with { SourceFile = s },
                Bbs757LayoutDescriptions.DescribeUpperEicas),
        });

    /// <summary>The lower EICAS pages whose part could not be derived, with the generator's reason (each page is an
    /// optional part of the layout): the generator and the store log them.</summary>
    private static IReadOnlyList<(string Part, string Reason)> LowerEicasFailures(Bbs757LowerEicasLayout l)
    {
        var failures = new List<(string, string)>();
        if (l.EngineMissing is { } engine) failures.Add(("engine page", engine));
        if (l.StatusMissing is { } status) failures.Add(("STATUS page", status));
        return failures;
    }
}
