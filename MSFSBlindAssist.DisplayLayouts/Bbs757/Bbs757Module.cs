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
        });
}
