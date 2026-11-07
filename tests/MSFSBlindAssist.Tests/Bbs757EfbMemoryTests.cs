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
    private const uint EfbVptr = 0x13128, ButtonVptr = 0x30164, LabelVptr = 0x30cd4, GridVptr = 0x30320, ScrollboxVptr = 0x30400,
        SliderVptr = 0x30c48;
    private const uint Outside = 0xA000, InnerVec = 0xA200, Inner = 0xA300;

    private static readonly Bbs757EfbMemLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 1 },
        ManagerBase: Manager, ManagerSpan: 0x100, EfbVptr: EfbVptr, EfbToBbos: 132, BbosToAppMap: 0x238,
        AppFront: 0x140, AppPage: 0x144, AppRoots: 0x17c, PageChildren: 0x4c,
        CtrlName: 0x1c, CtrlRect: 0x38, CtrlVisible: 0x8c, CtrlChildren: 0xf4, CtrlParent: 0xe8,
        CtrlLabelText: 0x128, CtrlButtonText: 0x144, ScrollboxOffset: 0x134, SliderKnob: 0x184,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("button", ButtonVptr), new Bbs757EfbClassVptr("label", LabelVptr),
            new Bbs757EfbClassVptr("grid", GridVptr), new Bbs757EfbClassVptr("scrollbox", ScrollboxVptr),
            new Bbs757EfbClassVptr("slider", SliderVptr),
        });

    private readonly byte[] _b = new byte[0x10000];
    private void Ptr(uint at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_b.AsSpan((int)at), v);
    private void F32(uint at, float v) => BinaryPrimitives.WriteSingleLittleEndian(_b.AsSpan((int)at), v);
    private void ShortStr(uint at, string s) { Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)at)); _b[at + 11] = (byte)s.Length; }
    private void Rect(uint ctrl, float x, float y, float w, float h) { F32(ctrl + 0x38, x); F32(ctrl + 0x3c, y); F32(ctrl + 0x40, w); F32(ctrl + 0x44, h); }
    private void Control(uint ctrl, uint vptr, string name, bool visible) { Ptr(ctrl, vptr); ShortStr(ctrl + 0x1c, name); _b[ctrl + 0x8c] = (byte)(visible ? 1 : 0); }

    private Bbs757EfbMemory Build(Action? mutate = null)
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
        mutate?.Invoke();

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

    /// <summary>The grid made a scrollbox scrolled by <paramref name="offset"/>, written where the layout keeps a scrollbox's content
    /// offset (B757-26: never the scrollbar thumb).</summary>
    private void ScrolledGrid(float offset)
    {
        Ptr(Grid, ScrollboxVptr); F32(Grid + (uint)Layout.ScrollboxOffset, offset);
        Rect(Button, 0, 1202, 250, 60);
    }

    /// <summary>Settings, Aircraft at the end of its scroll: its stackbox's 1,180 less its scrollbox's 966 (measured at +0x134,
    /// 2026-11-07).</summary>
    private const float SettingsAircraftEnd = 214f;

    [Fact]
    public void A_scrolled_scrollbox_moves_its_controls_up_by_its_offset()
    {
        var apps = Build(() =>
        {
            ScrolledGrid(SettingsAircraftEnd);
            // a second page child, outside the scrollbox
            Ptr(Page + 0x50, PageVec + 16); Ptr(PageVec + 8, Outside);
            Control(Outside, LabelVptr, "outside", true); Rect(Outside, 0, 1202, 100, 20); ShortStr(Outside + 0x128, "TOP");
        }).ReadFrontApps();
        var controls = Assert.Single(apps).Controls;
        var grid = controls[0];
        Assert.Equal("scrollbox", grid.Kind);
        Assert.Equal(SettingsAircraftEnd, grid.Scroll);
        Assert.Equal(32f, grid.Y);                                          // the scrollbox itself is not moved
        Assert.Equal(1202f - SettingsAircraftEnd, grid.Children[0].Y, 0.01); // the row reads where it is drawn
        Assert.Equal(1202f, controls[1].Y);                          // outside the scrollbox: unmoved
        Assert.Equal(0f, controls[1].Scroll);
    }

    [Fact]
    public void A_nested_scrollbox_adds_its_offset()
    {
        var apps = Build(() =>
        {
            ScrolledGrid(100f);
            Ptr(Button, ScrollboxVptr); F32(Button + (uint)Layout.ScrollboxOffset, 50f);
            Ptr(Button + 0xf4, InnerVec); Ptr(Button + 0xf8, InnerVec + 8); Ptr(InnerVec, Inner);
            Control(Inner, LabelVptr, "inner", true); Rect(Inner, 0, 500, 100, 20); ShortStr(Inner + 0x128, "IN");
        }).ReadFrontApps();
        var button = Assert.Single(apps).Controls[0].Children[0];
        Assert.Equal(1102f, button.Y);
        Assert.Equal(350f, Assert.Single(button.Children).Y);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(-5f, 0f)]
    [InlineData(1e9f, 100000f)]
    public void An_unreadable_offset_reads_as_zero(float stored, float expected)
    {
        var grid = Assert.Single(Assert.Single(Build(() => ScrolledGrid(stored)).ReadFrontApps()).Controls);
        Assert.Equal(expected, grid.Scroll);
        Assert.Equal(1202f - expected, grid.Children[0].Y);
    }

    // ---- a slider's knob ----

    /// <summary>The grid's first child made a slider at <paramref name="x"/>,<paramref name="y"/> <paramref name="w"/>x<paramref name="h"/>
    /// with its knob's rectangle written where the layout keeps it, in the slider rectangle's own coordinates.</summary>
    private void Slider(float x, float y, float w, float h, float kx, float ky, float kw, float kh)
    {
        Ptr(Button, SliderVptr); Rect(Button, x, y, w, h);
        uint knob = Button + (uint)Layout.SliderKnob;
        F32(knob, kx); F32(knob + 4, ky); F32(knob + 8, kw); F32(knob + 12, kh);
    }

    private Bbs757EfbMemory.Node FirstChild(Action mutate) =>
        Assert.Single(Assert.Single(Build(mutate).ReadFrontApps()).Controls).Children[0];

    [Fact]
    public void An_across_sliders_place_is_its_knobs_left_along_the_track()
    {
        // Weight & balance's forward cargo, measured 2026-11-07: 59.8 % of the way, the 12 px knob's left at X + 0.598 (W - 12). Its y
        // is off the slider's middle, so a place taken from y (an across slider read as upright) would differ.
        var cargo = FirstChild(() => Slider(1162.5f, 462, 188, 18, 1162.5f + 0.598f * 176, 463, 12, 12));

        Assert.Equal("slider", cargo.Kind);
        Assert.Equal(0.598f, cargo.Knob!.Value, 3);
        Assert.Equal(12f, cargo.KnobSpan);
    }

    [Fact]
    public void An_upright_sliders_place_counts_from_its_bottom()
    {
        // The left hydraulic reservoir, measured 2026-11-07: 78.1 %, the knob's top at Y + (1 - 0.781) (H - 12); bottom is 0.
        var reservoir = FirstChild(() => Slider(423, 279, 24, 190, 429, 279 + 0.219f * 178, 12, 12));

        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);
        Assert.Equal(0f, FirstChild(() => Slider(423, 279, 24, 190, 429, 279 + 178, 12, 12)).Knob);   // at the bottom
        Assert.Equal(1f, FirstChild(() => Slider(423, 279, 24, 190, 429, 279, 12, 12)).Knob);         // at the top
    }

    [Fact]
    public void An_upright_sliders_span_is_its_knobs_height()
    {
        // A knob wider than tall on an upright slider: its height is what runs along the track (a width taken as the span reads 0.77).
        var reservoir = FirstChild(() => Slider(423, 279, 24, 190, 425, 279 + 0.219f * 178, 20, 12));
        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);

        // A knob as tall as its upright slider has no track to run along.
        var tall = FirstChild(() => Slider(423, 279, 24, 190, 429, 279, 12, 190));
        Assert.Null(tall.Knob);
        Assert.Equal(0f, tall.KnobSpan);
    }

    [Fact]
    public void A_scroll_moves_a_slider_but_not_its_place()
    {
        // Settings, Aircraft scrolled, measured 2026-11-07: Boarding & load rate's knob y 864.5 is its rectangle's 857 + 7.5 while the
        // slider is drawn 113 px higher. The knob is in the rectangle's own coordinates, so its place is the same. The knob's y here is
        // off the slider's middle (861): an across slider's place never comes from it.
        var rate = FirstChild(() =>
        {
            ScrolledGrid(113);
            Slider(897, 857, 300, 30, 897 + 285, 861, 15, 15);
        });

        Assert.Equal(857f - 113f, rate.Y);
        Assert.Equal(1f, rate.Knob);
        Assert.Equal(15f, rate.KnobSpan);
    }

    [Fact]
    public void A_scroll_moves_an_upright_slider_but_not_its_place()
    {
        // The review's I1: the left reservoir under a scroll of 113. Its place comes from the knob against the rectangle as stored (279),
        // not as drawn (166): against the drawn one it would read 0.146, inside the range, and list as 15.
        var reservoir = FirstChild(() =>
        {
            ScrolledGrid(113);
            Slider(423, 279, 24, 190, 429, 279 + 0.219f * 178, 12, 12);
        });

        Assert.Equal(166f, reservoir.Y);
        Assert.Equal(0.781f, reservoir.Knob!.Value, 3);
        Assert.Equal(12f, reservoir.KnobSpan);
    }

    [Theory]
    [InlineData(float.NaN, 465f, 12f, 12f)]                  // a float that is no number
    [InlineData(1200f, float.PositiveInfinity, 12f, 12f)]
    [InlineData(1200f, 465f, 188f, 12f)]                     // the knob as wide as the slider along its track
    [InlineData(1200f, 465f, -12f, 12f)]                     // a knob of negative size
    [InlineData(1162.5f + 1.02f * 176, 465f, 12f, 12f)]      // past the track's end
    [InlineData(1162.5f - 0.02f * 176, 465f, 12f, 12f)]      // before its start
    public void An_unreadable_knob_gives_no_place(float kx, float ky, float kw, float kh)
    {
        var cargo = FirstChild(() => Slider(1162.5f, 462, 188, 18, kx, ky, kw, kh));
        Assert.Null(cargo.Knob);
        Assert.Equal(0f, cargo.KnobSpan);
    }

    [Theory]
    [InlineData(1.005f, 1f)]
    [InlineData(-0.005f, 0f)]
    public void A_place_just_past_an_end_is_that_end(float place, float expected)
    {
        Assert.Equal(expected, FirstChild(() => Slider(1162.5f, 462, 188, 18, 1162.5f + place * 176, 465, 12, 12)).Knob);
    }

    [Fact]
    public void Only_a_slider_has_a_knob()
    {
        var button = FirstChild(() =>
        {
            uint knob = Button + (uint)Layout.SliderKnob;
            F32(knob, 100); F32(knob + 4, 210); F32(knob + 8, 12); F32(knob + 12, 12);
        });
        Assert.Equal("button", button.Kind);
        Assert.Null(button.Knob);
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
