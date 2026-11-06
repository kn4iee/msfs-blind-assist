using System.Buffers.Binary;
using System.Text;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The EFB memory reader walks a synthetic BBOS image — gauge manager, EFB, BBOS app map, one front app
/// with a page — and returns the visible control tree with libc++ std::string text and rectangles.</summary>
public class Bbs757EfbMemoryTests
{
    private const ulong B = 0x40000000;
    private const uint Manager = 0x1000, Efb = 0x2000, Bbos = 0x3000, MapNode = 0x3400, AppVec = 0x3480,
        App = 0x4000, RootsVec = 0x4400, Page = 0x5000, PageVec = 0x5400,
        Grid = 0x6000, GridChildrenVec = 0x6400, Button = 0x7000, ValueLabel = 0x8000, HiddenBtn = 0x9000;
    private const uint EfbVptr = 0x13128, ButtonVptr = 0x30164, LabelVptr = 0x30cd4, GridVptr = 0x30320;

    private static readonly Bbs757EfbMemLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 1 },
        ManagerBase: Manager, ManagerSpan: 0x100, EfbVptr: EfbVptr, EfbToBbos: 132, BbosToAppMap: 0x238,
        AppFront: 0x140, AppPage: 0x144, AppRoots: 0x17c, PageChildren: 0x4c,
        CtrlName: 0x1c, CtrlRect: 0x38, CtrlVisible: 0x8c, CtrlChildren: 0xf4, CtrlParent: 0xe8,
        CtrlLabelText: 0x128, CtrlButtonText: 0x144,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("button", ButtonVptr), new Bbs757EfbClassVptr("label", LabelVptr),
            new Bbs757EfbClassVptr("grid", GridVptr),
        });

    private readonly byte[] _b = new byte[0x10000];
    private void Ptr(uint at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_b.AsSpan((int)at), v);
    private void F32(uint at, float v) => BinaryPrimitives.WriteSingleLittleEndian(_b.AsSpan((int)at), v);
    private void ShortStr(uint at, string s) { Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)at)); _b[at + 11] = (byte)s.Length; }
    private void Rect(uint ctrl, float x, float y, float w, float h) { F32(ctrl + 0x38, x); F32(ctrl + 0x3c, y); F32(ctrl + 0x40, w); F32(ctrl + 0x44, h); }
    private void Control(uint ctrl, uint vptr, string name, bool visible) { Ptr(ctrl, vptr); ShortStr(ctrl + 0x1c, name); _b[ctrl + 0x8c] = (byte)(visible ? 1 : 0); }

    private Bbs757EfbMemory Build()
    {
        Ptr(Manager + 0x70, Efb);           // efb in the manager
        Ptr(Efb, EfbVptr);
        Ptr(Efb + 132, Bbos);               // efb -> bbos
        // app map header at bbos+0x238: [begin, root, size]
        Ptr(Bbos + 0x238 + 4, MapNode);
        Ptr(Bbos + 0x238 + 8, 1);
        Ptr(MapNode + 0x10, 7);             // key
        Ptr(MapNode + 0x14, AppVec); Ptr(MapNode + 0x18, AppVec + 8); // value vector [one app*]
        Ptr(AppVec, App);
        Ptr(App, GridVptr);                 // app "class" (any vptr; mapped to grid here, harmless)
        Ptr(App + 0x140, 1);                // front
        Ptr(App + 0x144, Page);             // current page
        Ptr(App + 0x17c, RootsVec); Ptr(App + 0x180, RootsVec); // empty roots vector
        Ptr(Page + 0x4c, PageVec); Ptr(Page + 0x50, PageVec + 8); // page children [grid]
        Ptr(PageVec, Grid);
        Control(Grid, GridVptr, "page_grid", true); Rect(Grid, 0, 32, 1397, 1016);
        Ptr(Grid + 0xf4, GridChildrenVec); Ptr(Grid + 0xf8, GridChildrenVec + 24); // 3 children
        Ptr(GridChildrenVec, Button); Ptr(GridChildrenVec + 8, ValueLabel); Ptr(GridChildrenVec + 16, HiddenBtn);
        Control(Button, ButtonVptr, "btn_fuel", true); Rect(Button, 0, 202, 250, 60); ShortStr(Button + 0x144, "FUEL");
        Control(ValueLabel, LabelVptr, "tiller_value", true); Rect(ValueLabel, 1197, 422, 200, 60); ShortStr(ValueLabel + 0x128, "RUDDER");
        Control(HiddenBtn, ButtonVptr, "btn_hidden", false); Rect(HiddenBtn, 0, 300, 250, 60); ShortStr(HiddenBtn + 0x144, "HIDDEN");

        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)_b.Length, true, true, true), _b);
        return new Bbs757EfbMemory(new ModuleEfbMemory(new ModuleMemory(src, B)), Layout);
    }

    [Fact]
    public void Walks_the_front_app_page_tree_with_text_and_rects()
    {
        var apps = Build().ReadFrontApps();
        var app = Assert.Single(apps);
        Assert.Equal(7, app.Key);
        var grid = Assert.Single(app.Controls);
        Assert.Equal("page_grid", grid.Name);
        // the hidden button is dropped; the button and value label remain, with their text
        Assert.Equal(2, grid.Children.Count);
        var fuel = grid.Children[0];
        Assert.Equal("button", fuel.Kind);
        Assert.Equal("FUEL", fuel.Text);
        Assert.Equal((0f, 202f, 250f, 60f), (fuel.X, fuel.Y, fuel.W, fuel.H));
        var value = grid.Children[1];
        Assert.Equal("label", value.Kind);
        Assert.Equal("RUDDER", value.Text);
    }

    [Fact]
    public void A_missing_efb_vptr_reads_no_apps()
    {
        var reader = Build();
        _b[Efb] = 0; _b[Efb + 1] = 0; _b[Efb + 2] = 0; _b[Efb + 3] = 0; // clobber the efb vptr
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)_b.Length, true, true, true), _b);
        var r = new Bbs757EfbMemory(new ModuleEfbMemory(new ModuleMemory(src, B)), Layout);
        Assert.False(r.TryEfb(out _));
        Assert.Empty(r.ReadFrontApps());
    }
}
