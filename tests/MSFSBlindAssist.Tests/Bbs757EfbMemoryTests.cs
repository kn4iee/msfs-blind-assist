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
    // The fake's own vptrs for plan 21's kinds; a stack is no kind the layout names, so it reads "other".
    private const uint TextboxVptr = 0x30d00, MultilineVptr = 0x30500, PulldownVptr = 0x30700, ImageVptr = 0x30300,
        OverlayVptr = 0x28c00, ModalVptr = 0x27d00, StackVptr = 0x30d60;
    private const uint Outside = 0xA000, InnerVec = 0xA200, Inner = 0xA300;
    // A text box's label, a drop-down's face, list, stack and entries, the entry buttons and records.
    private const uint TextLabel = 0xD000, Face = 0xD200, List = 0xD400, ListVec = 0xD600, Stack = 0xD700, StackVec = 0xD900,
        EntriesVec = 0xDA00, Items = 0x10000, Records = 0x11000;
    // The top bar's app (key 1) and the dialog app (key 3), beside the page's app in the map.
    private const uint BarNode = 0xE000, BarAppVec = 0xE080, BarApp = 0xE100, BarRoots = 0xE400, BarGrid = 0x12000, BarGridVec = 0x12200,
        TimeLabel = 0x12400, ResetButton = 0x12600;
    private const uint DialogNode = 0xE800, DialogAppVec = 0xE880, DialogApp = 0xE900, DialogPage = 0x13000, DialogPageVec = 0x13400,
        DialogGrid = 0x13600, DialogGridVec = 0x13800, Prompt = 0x13A00, YesButton = 0x13C00;
    private const uint NoticeRecords = 0x14000, Heap = 0x18000, HeapEnd = 0x40000;
    // A text past the long cap (1 MiB and a byte); then a wide or deep tree of distinct controls.
    private const uint BigText = 0x40000, TreeBase = 0x150000;

    private static readonly Bbs757EfbMemLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 1 },
        ManagerBase: Manager, ManagerSpan: 0x100, EfbVptr: EfbVptr, EfbToBbos: 132, BbosToAppMap: 0x238,
        AppFront: 0x140, AppPage: 0x144, AppRoots: 0x17c, PageChildren: 0x4c,
        CtrlName: 0x1c, CtrlRect: 0x38, CtrlVisible: 0x8c, CtrlChildren: 0xf4, CtrlParent: 0xe8,
        CtrlLabelText: 0x128, CtrlButtonText: 0x144, ScrollboxOffset: 0x134, SliderKnob: 0x184,
        TextboxLabel: 0x128, MultilineText: 0x128, PulldownFace: 0x128, PulldownList: 0x130, PulldownEntries: 0x138,
        PulldownEntryText: 0x0c, PulldownOpen: 0x154, ButtonSelectable: 0x140, ButtonUnselected: 0x143,
        BbosModalOpen: 0x1d8, BbosNotices: 0x9334, NoticeStride: 0x3c, NoticeId: 0x00, NoticeTitle: 0x04,
        NoticeText: 0x10, NoticeTime: 0x1c, NoticeUnread: 0x34,
        OverlayPanelShown: 0x1fc, OverlayListOpen: 0x1fd, OverlayPanelRect: 0x234, OverlayPanelHeight: 0x240, OverlayFlightTimeText: 0x1b4,
        ClassVptrs: new[]
        {
            new Bbs757EfbClassVptr("button", ButtonVptr), new Bbs757EfbClassVptr("label", LabelVptr),
            new Bbs757EfbClassVptr("grid", GridVptr), new Bbs757EfbClassVptr("scrollbox", ScrollboxVptr),
            new Bbs757EfbClassVptr("slider", SliderVptr),
            new Bbs757EfbClassVptr("textbox", TextboxVptr), new Bbs757EfbClassVptr("multiline", MultilineVptr),
            new Bbs757EfbClassVptr("pulldown", PulldownVptr), new Bbs757EfbClassVptr("image", ImageVptr),
            new Bbs757EfbClassVptr("overlay", OverlayVptr), new Bbs757EfbClassVptr("modal", ModalVptr),
        });

    private readonly byte[] _b = new byte[0x1B0000];
    private uint _heap = Heap;
    private void Ptr(uint at, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(_b.AsSpan((int)at), v);
    private void F32(uint at, float v) => BinaryPrimitives.WriteSingleLittleEndian(_b.AsSpan((int)at), v);
    private void ShortStr(uint at, string s) { Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)at)); _b[at + 11] = (byte)s.Length; }

    /// <summary>A libc++ long-form string: its characters on the fake's heap, the pointer, the size and the capacity with the long flag.</summary>
    private void LongStr(uint at, string s)
    {
        uint data = _heap;
        Assert.True(data + (uint)s.Length + 1 <= HeapEnd, "the fake's heap is full");
        Encoding.ASCII.GetBytes(s).CopyTo(_b.AsSpan((int)data));
        _heap += (uint)s.Length + 1;
        LongHeader(at, data, s.Length);
    }

    /// <summary>A long-form string's 12 bytes alone: the pointer, the size word and the capacity with the long flag.</summary>
    private void LongHeader(uint at, uint data, int size) { Ptr(at, data); Ptr(at + 4, (uint)size); Ptr(at + 8, 0x80000000u | (uint)(size + 1)); }

    /// <summary>A std::string in the form libc++ keeps it: short up to 10 characters, long past that.</summary>
    private void Str(uint at, string s) { if (s.Length <= 10) ShortStr(at, s); else LongStr(at, s); }
    private void Rect(uint ctrl, float x, float y, float w, float h) { F32(ctrl + 0x38, x); F32(ctrl + 0x3c, y); F32(ctrl + 0x40, w); F32(ctrl + 0x44, h); }
    private void Control(uint ctrl, uint vptr, string name, bool visible) { Ptr(ctrl, vptr); Str(ctrl + 0x1c, name); _b[ctrl + 0x8c] = (byte)(visible ? 1 : 0); }

    /// <summary>A control's children vector (8-byte slots, as libc++ keeps BlueBird's).</summary>
    private void Children(uint ctrl, uint vec, params uint[] kids)
    {
        Ptr(ctrl + 0xf4, vec); Ptr(ctrl + 0xf8, vec + 8 * (uint)kids.Length);
        for (int i = 0; i < kids.Length; i++) Ptr(vec + 8 * (uint)i, kids[i]);
    }

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
        return new Bbs757EfbMemory(new ReadBudget(new ModuleEfbMemory(new ModuleMemory(src, B)), MaxReads), Layout);
    }

    /// <summary>More reads than any test's walk needs (the node budget's test needs about 300,000): a walk past it did not end.</summary>
    private const long MaxReads = 2_000_000;

    /// <summary>The fake's memory with a read budget, so a walk that would not end fails the test instead of hanging the run.</summary>
    private sealed class ReadBudget : IEfbMemory
    {
        private readonly IEfbMemory _m;
        private long _left;
        public ReadBudget(IEfbMemory m, long reads) { _m = m; _left = reads; }
        private void Spend() { if (--_left < 0) throw new InvalidOperationException("the walk read past its budget: it would not have ended"); }
        public bool TryReadInt(uint a, out int v) { Spend(); return _m.TryReadInt(a, out v); }
        public bool TryReadByte(uint a, out byte v) { Spend(); return _m.TryReadByte(a, out v); }
        public float ReadFloat(uint a) { Spend(); return _m.ReadFloat(a); }
        public bool TryReadPtr(uint a, out uint v) { Spend(); return _m.TryReadPtr(a, out v); }
        public string? ReadStdString(uint a, int maxLength = Bbs757EfbMemory.TextCap) { Spend(); return _m.ReadStdString(a, maxLength); }
    }

    private static int Count(IReadOnlyList<Bbs757EfbMemory.Node> ns) => ns.Count + ns.Sum(n => Count(n.Children));

    private static IEnumerable<Bbs757EfbMemory.Node> Flat(IEnumerable<Bbs757EfbMemory.Node> ns) =>
        ns.SelectMany(n => new[] { n }.Concat(Flat(n.Children)));

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

    // ---- text boxes, drop-downs and the selected entry (plan 21, measured 2026-11-07: docs/b757/evidence/efb-memory-classes.md) ----

    [Fact]
    public void A_text_box_reads_its_text()
    {
        // PERF calc's airport box: its text is its text_label's, a label in no children vector that the box points at; 2 of 6 boxes
        // had theirs allocated apart, so the label sits far from the box here. The box's own button-text field holds "FUEL".
        var box = FirstChild(() =>
        {
            Ptr(Button, TextboxVptr); Ptr(Button + (uint)Layout.TextboxLabel, TextLabel);
            Control(TextLabel, LabelVptr, "text_label", true); Rect(TextLabel, 1, 203, 248, 58);
            ShortStr(TextLabel + (uint)Layout.CtrlLabelText, "KPHX");
        });
        Assert.Equal("textbox", box.Kind);
        Assert.Equal("KPHX", box.Text);
        Assert.Empty(box.Children);

        // No label: no text, never the box's other fields.
        var bare = FirstChild(() => { Ptr(Button, TextboxVptr); Ptr(Button + (uint)Layout.TextboxLabel, 0); });
        Assert.Equal("", bare.Text);
    }

    [Fact]
    public void A_multiline_box_reads_every_line()
    {
        // The Operating FP: 91,869 characters with '\n' between lines, past the 4,096 every other string is capped at.
        string plan = string.Join("\n", Enumerable.Range(1, 150).Select(i => FormattableString.Invariant($"{i:000} WPT{i:000} N47 26.9 W122 18.5 FL350")));
        Assert.True(plan.Length > 4096);
        var fp = FirstChild(() => { Ptr(Button, MultilineVptr); LongStr(Button + (uint)Layout.MultilineText, plan); });
        Assert.Equal("multiline", fp.Kind);
        Assert.Equal(plan, fp.Text);

        // A label keeps the 4,096 cap: past it, no text.
        var label = FirstChild(() =>
        {
            Ptr(Button, LabelVptr); ShortStr(Button + (uint)Layout.CtrlButtonText, "");
            LongStr(Button + (uint)Layout.CtrlLabelText, plan);
        });
        Assert.Equal("", label.Text);
    }

    [Fact]
    public void A_multiline_box_reads_the_operating_fps_length()
    {
        // The Operating FP measured 91,869 characters: 100,000 read whole.
        string text = string.Concat(Enumerable.Repeat("KSEA DCT BTG DCT KPDX FL350 0:35 120000\n", 2500));
        Assert.Equal(100_000, text.Length);
        var fp = FirstChild(() => { Ptr(Button, MultilineVptr); LongStr(Button + (uint)Layout.MultilineText, text); });
        Assert.Equal(text, fp.Text);
    }

    [Fact]
    public void A_multiline_text_past_the_long_cap_reads_none()
    {
        // The cap is 1 MiB: a size word of exactly that reads whole; one more reads "", though every byte of it is there to read.
        // Numbers, not the constant, so a cap moved either way fails here.
        uint at = Button + (uint)Layout.MultilineText;
        Assert.Equal(1_048_576, Big(1_048_576).Length);
        Assert.Equal("", Big(1_048_577));

        string Big(int size) => FirstChild(() =>
        {
            Ptr(Button, MultilineVptr);
            _b.AsSpan((int)BigText, 1_048_577).Fill((byte)'A');
            LongHeader(at, BigText, size);
        }).Text;
    }

    /// <summary>The grid's first child made a drop-down, as PERF calc's runway one was read: its face (a button showing
    /// <paramref name="face"/>), its entry records, and its list (a scrollbox below the face holding a stack of one button per
    /// entry, 38 px apart while open, [0,0 50x38] while closed), scrolled by <paramref name="scroll"/>.</summary>
    private void DropDown(string face, string[] entries, bool open = false, float scroll = 0)
    {
        Ptr(Button, PulldownVptr); Rect(Button, 519.1f, 156, 150, 34);
        Ptr(Button + (uint)Layout.PulldownFace, Face);
        Control(Face, ButtonVptr, "main_button", true); Rect(Face, 519.1f, 156, 150, 34); Str(Face + (uint)Layout.CtrlButtonText, face);
        Ptr(Button + (uint)Layout.PulldownList, List);
        Control(List, ScrollboxVptr, "item_scrollbox", true); Rect(List, 519.1f, 189, 150, 180);
        F32(List + (uint)Layout.ScrollboxOffset, scroll);
        Children(List, ListVec, Stack);
        Control(Stack, StackVptr, "item_stack", true); Rect(Stack, 519.1f, 189, 150, 38 * entries.Length);
        var items = Enumerable.Range(0, entries.Length).Select(i => Items + (uint)i * 0x200).ToArray();
        Children(Stack, StackVec, items);
        uint vec = Button + (uint)Layout.PulldownEntries;
        Ptr(vec, EntriesVec); Ptr(vec + 4, EntriesVec + 8 * (uint)entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            Control(items[i], ButtonVptr, "item_btn_" + i, true);
            if (open) Rect(items[i], 519.1f, 189 + 38 * i, 150, 38); else Rect(items[i], 0, 0, 50, 38);
            Str(items[i] + (uint)Layout.CtrlButtonText, entries[i]);
            uint record = Records + (uint)i * 0x40;
            Ptr(EntriesVec + 8 * (uint)i, record);
            Str(record + (uint)Layout.PulldownEntryText, entries[i]);
        }
        _b[Button + (uint)Layout.PulldownOpen] = (byte)(open ? 1 : 0);
    }

    [Fact]
    public void A_drop_down_reads_its_entries_and_current_one()
    {
        // PERF calc's flap drop-down: no index field; the current entry is the one whose text is the face's.
        var flap = FirstChild(() => DropDown("Optimum", new[] { "15", "20", "5", "Optimum" }));
        Assert.Equal("pulldown", flap.Kind);
        Assert.Equal("Optimum", flap.Text);
        Assert.Equal("Optimum", flap.Value);
        Assert.Equal(new[] { "15", "20", "5", "Optimum" }, flap.Entries);
        Assert.False(flap.Open);
        Assert.Null(flap.Rows);
        Assert.Null(flap.OpenList);
        Assert.Empty(flap.Children); // the face and the list are in no children vector
    }

    [Fact]
    public void A_drop_down_whose_face_matches_no_entry_has_no_value()
    {
        // Systems' pb_menu1 always shows SELECT.
        var menu = FirstChild(() => DropDown("SELECT", new[] { "24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS", "36 - PNEUMATICS" }));
        Assert.Equal("SELECT", menu.Text);
        Assert.Equal("", menu.Value);
        Assert.Equal(new[] { "24 - ELECTRICAL", "28 - FUEL", "29 - HYDRAULICS", "36 - PNEUMATICS" }, menu.Entries);
    }

    [Fact]
    public void An_open_drop_down_reads_its_rows()
    {
        // The runway drop-down tapped open: its +0x154 byte 1, the entry buttons laid out 38 px apart from the list's top. The list
        // (180 px tall for six 38 px rows) is scrolled by one row here, so each row is drawn 38 px higher than its rectangle.
        string[] runways = { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        var runway = FirstChild(() => DropDown("RW07L", runways, open: true, scroll: 38));
        Assert.True(runway.Open);
        Assert.Equal("RW07L", runway.Value);
        var rows = runway.Rows!;
        Assert.Equal(runways, rows.Select(r => r.Text));
        Assert.All(rows, r => Assert.Equal("button", r.Kind));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => 189f + 38 * i - 38), rows.Select(r => r.Y));
        Assert.Equal((519.1f, 150f, 38f), (rows[0].X, rows[0].W, rows[0].H));
        Assert.Empty(runway.Children);

        // The list's own viewport, for a choice to tell a row in view from one scrolled out of it: within its 189 to 369, row 0
        // (drawn at 151) is scrolled out above and row 5 (341 to 379) runs past its bottom.
        var list = runway.OpenList!;
        Assert.Equal(("scrollbox", "item_scrollbox", 519.1f, 189f, 150f, 180f, 38f), (list.Kind, list.Name, list.X, list.Y, list.W, list.H, list.Scroll));
        Assert.Empty(list.Children); // its rows are Rows
    }

    [Fact]
    public void An_open_drop_down_under_a_scrolled_page_takes_the_pages_scroll_off_its_rows()
    {
        // Logic only, not measured: no drop-down read so far sits in a scrollbox. The list is drawn under its drop-down, so it is
        // read as any control inside the page's scrollbox: it and its rows drawn the page's offset higher, the rows its own too.
        string[] runways = { "RW07L", "RW07R", "RW08", "RW25L", "RW25R", "RW26" };
        var runway = FirstChild(() => { ScrolledGrid(100); DropDown("RW07L", runways, open: true, scroll: 38); });
        Assert.Equal(156f - 100, runway.Y);
        Assert.Equal((189f - 100, 38f), (runway.OpenList!.Y, runway.OpenList.Scroll));
        Assert.Equal(Enumerable.Range(0, 6).Select(i => 189f + 38 * i - 38 - 100), runway.Rows!.Select(r => r.Y));
    }

    private bool? Selection(byte selectable, byte unselected) => FirstChild(() =>
    {
        _b[Button + (uint)Layout.ButtonSelectable] = selectable; _b[Button + (uint)Layout.ButtonUnselected] = unselected;
    }).Selected;

    [Fact]
    public void A_selected_button_reads_selected()
    {
        // Side menus and tab rows: the current entry is the button whose +0x140 byte is 1 and +0x143 byte 0.
        Assert.True(Selection(selectable: 1, unselected: 0));
        Assert.False(Selection(selectable: 1, unselected: 1)); // another entry of the menu
        Assert.Null(Selection(selectable: 0, unselected: 0));  // a tile that takes no tap (Ground services' chocks_service_box)

        var label = FirstChild(() =>
        {
            Ptr(Button, LabelVptr);
            _b[Button + (uint)Layout.ButtonSelectable] = 1; _b[Button + (uint)Layout.ButtonUnselected] = 0;
        });
        Assert.Null(label.Selected); // only a button is selected or not
    }

    // ---- notifications ----

    /// <summary>BBOS's notification vector holding <paramref name="records"/>, each <c>NoticeStride</c> bytes, capacity 64.</summary>
    private void Notices(params (int Id, string Title, string Text, string Time, bool Unread)[] records)
    {
        uint vec = Bbos + (uint)Layout.BbosNotices, stride = (uint)Layout.NoticeStride;
        Ptr(vec, NoticeRecords); Ptr(vec + 4, NoticeRecords + stride * (uint)records.Length); Ptr(vec + 8, NoticeRecords + stride * 64);
        for (int i = 0; i < records.Length; i++)
        {
            uint r = NoticeRecords + stride * (uint)i;
            Ptr(r + (uint)Layout.NoticeId, (uint)records[i].Id);
            Str(r + (uint)Layout.NoticeTitle, records[i].Title);
            Str(r + (uint)Layout.NoticeText, records[i].Text);
            Str(r + (uint)Layout.NoticeTime, records[i].Time);
            _b[r + (uint)Layout.NoticeUnread] = (byte)(records[i].Unread ? 1 : 0);
        }
    }

    [Fact]
    public void The_notifications_read_text_and_time()
    {
        // Measured: id 45 read, the AFT cargo door's id 50 new and unread, an autobrake one with a title.
        var reader = Build(() => Notices((45, "", "CHOCKS REMOVED.", "21:37Z", false), (50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true),
            (52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", true)));
        Assert.Equal(new[]
        {
            new Bbs757EfbNoticeRecord(45, "", "CHOCKS REMOVED.", "21:37Z", false),
            new Bbs757EfbNoticeRecord(50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true),
            new Bbs757EfbNoticeRecord(52, "AUTOBRAKE 1", "TARGET 4.5 | ACTUAL 4.5 ft/s2", "03:20Z", true),
        }, reader.ReadNotices());
    }

    [Fact]
    public void No_notification_reads_an_empty_list()
    {
        // A whole vector with no record, and one never allocated (begin and end both 0, as a fresh BBOS starts): read, and empty.
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Bbs757EfbNoticeRecord>>(Build(() => Notices()).ReadNotices()));
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Bbs757EfbNoticeRecord>>(Build().ReadNotices()));
    }

    [Theory]
    [InlineData(NoticeRecords, NoticeRecords - 0x3c)]             // its end before its start
    [InlineData(NoticeRecords, NoticeRecords + 0x3c + 7)]         // not a whole record
    [InlineData(0u, 0x3cu)]                                      // no buffer
    [InlineData(NoticeRecords, NoticeRecords + 257 * 0x3c)]       // more records than any BlueBird keeps
    public void A_torn_notices_vector_is_not_read(uint begin, uint end)
    {
        // Not read (null), never an empty list: the window tells "BlueBird has none" from "none could be read".
        var reader = Build(() =>
        {
            Notices((45, "", "CHOCKS REMOVED.", "21:37Z", false), (50, "", "AFT CARGO DOOR OPENED.", "03:15Z", true));
            Ptr(Bbos + (uint)Layout.BbosNotices, begin); Ptr(Bbos + (uint)Layout.BbosNotices + 4, end);
        });
        Assert.Null(reader.ReadNotices());
    }

    [Fact]
    public void Notifications_are_not_read_while_the_module_is_not_reachable()
    {
        var reader = Build(() => Ptr(Efb, 0));   // the gauge manager's object is no EFB: BBOS not found
        Assert.Null(reader.ReadNotices());
    }

    // ---- the top bar and the dialog: apps found by their class ----

    /// <summary>The map gains the top bar's app (key 1, its front flag 0, as measured) and the dialog app (key 3, its front flag 18 and
    /// its page set, as BlueBird leaves them after a dialog closes), with BBOS's dialog byte <paramref name="dialogOpen"/>.</summary>
    private void TopBarAndDialog(bool dialogOpen)
    {
        Ptr(Bbos + 0x238 + 8, 3);
        Ptr(MapNode, BarNode); Ptr(MapNode + 4, DialogNode);
        Ptr(BarNode + 0x10, 1); Ptr(BarNode + 0x14, BarAppVec); Ptr(BarNode + 0x18, BarAppVec + 8); Ptr(BarAppVec, BarApp);
        Ptr(BarApp, OverlayVptr); Ptr(BarApp + 0x140, 0); Ptr(BarApp + 0x144, 0);
        Ptr(BarApp + 0x17c, BarRoots); Ptr(BarApp + 0x180, BarRoots + 16); Ptr(BarRoots, ResetButton); Ptr(BarRoots + 8, BarGrid);
        Control(ResetButton, ButtonVptr, "reset_points_button", false);
        Control(BarGrid, GridVptr, "overlay_grid", true); Rect(BarGrid, 0, 0, 1397, 32);
        Children(BarGrid, BarGridVec, TimeLabel);
        Control(TimeLabel, LabelVptr, "time_label", true); Rect(TimeLabel, 15, 0, 1382, 32);
        Str(TimeLabel + (uint)Layout.CtrlLabelText, "Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C");

        Ptr(DialogNode + 0x10, 3); Ptr(DialogNode + 0x14, DialogAppVec); Ptr(DialogNode + 0x18, DialogAppVec + 8); Ptr(DialogAppVec, DialogApp);
        Ptr(DialogApp, ModalVptr); Ptr(DialogApp + 0x140, 18); Ptr(DialogApp + 0x144, DialogPage);
        Ptr(DialogApp + 0x17c, 0); Ptr(DialogApp + 0x180, 0);
        Ptr(DialogPage + 0x4c, DialogPageVec); Ptr(DialogPage + 0x50, DialogPageVec + 8); Ptr(DialogPageVec, DialogGrid);
        Control(DialogGrid, GridVptr, "dialog_grid", true); Rect(DialogGrid, 334.5f, 431, 728, 218);
        Children(DialogGrid, DialogGridVec, Prompt, YesButton);
        Control(Prompt, LabelVptr, "text_label", true); Rect(Prompt, 435.5f, 436, 622, 148);
        Str(Prompt + (uint)Layout.CtrlLabelText, "Power off the EFB?");
        Control(YesButton, ButtonVptr, "yes_btn", true); Rect(YesButton, 334.5f, 589, 180, 60); Str(YesButton + (uint)Layout.CtrlButtonText, "YES");

        _b[Bbos + (uint)Layout.BbosModalOpen] = (byte)(dialogOpen ? 1 : 0);
    }

    [Fact]
    public void The_top_bar_app_is_read_though_not_front()
    {
        var apps = Build(() => TopBarAndDialog(dialogOpen: false)).ReadFrontApps();
        var bar = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass);
        Assert.Equal(1, bar.Key);
        var grid = Assert.Single(bar.Controls); // its hidden reset_points_button dropped
        Assert.Equal("overlay_grid", grid.Name);
        Assert.Equal("Sat, 7th Nov 2026 | 20:28L | 02:28Z | OAT 6 C", Assert.Single(grid.Children).Text);
    }

    [Fact]
    public void The_top_bar_app_reads_its_notification_panel()
    {
        // Measured 2026-11-07: under a new notification's pop-up the overlay's byte +0x1fc is 1 (+0x1fd, the full list, 0), its panel's x,
        // y and width the floats at +0x234 and its height the float at +0x240 (118). BlueBird draws the panel from these fields, not as a
        // control: only the top bar's app carries one.
        var apps = Build(() =>
        {
            TopBarAndDialog(dialogOpen: true);
            _b[BarApp + (uint)Layout.OverlayPanelShown] = 1;
            uint rect = BarApp + (uint)Layout.OverlayPanelRect;
            F32(rect, 1039.75f); F32(rect + 4, 38); F32(rect + 8, 349.25f); F32(BarApp + (uint)Layout.OverlayPanelHeight, 118);
        }).ReadFrontApps();
        var bar = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.TopBarClass);
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(true, false, 1039.75f, 38, 349.25f, 118), bar.Panel);
        Assert.All(apps.Where(a => a.Class != Bbs757EfbMemory.TopBarClass), a => Assert.Null(a.Panel));

        // The list open: both bytes 1, 480 tall (the fake's image is the same one, its rectangle kept). Closed: both 0, no height.
        var listed = Build(() =>
        {
            TopBarAndDialog(dialogOpen: false);
            _b[BarApp + (uint)Layout.OverlayListOpen] = 1;
            F32(BarApp + (uint)Layout.OverlayPanelHeight, 480);
        }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(true, true, 1039.75f, 38, 349.25f, 480), listed.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Panel);
        var closed = Build(() =>
        {
            TopBarAndDialog(dialogOpen: false);
            _b[BarApp + (uint)Layout.OverlayPanelShown] = 0;
            _b[BarApp + (uint)Layout.OverlayListOpen] = 0;
            F32(BarApp + (uint)Layout.OverlayPanelHeight, 0);
        }).ReadFrontApps();
        Assert.Equal(new Bbs757EfbMemory.NoticePanel(false, false, 1039.75f, 38, 349.25f, 0), closed.Single(a => a.Class == Bbs757EfbMemory.TopBarClass).Panel);
    }

    [Fact]
    public void A_closed_dialog_is_not_read()
    {
        // BlueBird left an answered defuel question "front" for hours, its page set; only BBOS's dialog byte said it was gone.
        var apps = Build(() => TopBarAndDialog(dialogOpen: false)).ReadFrontApps();
        Assert.DoesNotContain(apps, a => a.Class == Bbs757EfbMemory.DialogClass || a.Key == 3);
        Assert.Contains(apps, a => a.Key == 7); // the page's app as before
    }

    [Fact]
    public void An_open_dialog_is_read()
    {
        var apps = Build(() => TopBarAndDialog(dialogOpen: true)).ReadFrontApps();
        var dialog = Assert.Single(apps, a => a.Class == Bbs757EfbMemory.DialogClass);
        Assert.Equal(3, dialog.Key);
        var grid = Assert.Single(dialog.Controls);
        Assert.Equal(new[] { "Power off the EFB?", "YES" }, grid.Children.Select(c => c.Text));
        Assert.Equal(3, apps.Count);
    }

    // ---- a torn tree: each control read once, a node budget per reading, a depth guard ----

    [Fact]
    public void A_control_listing_itself_twice_is_read_once()
    {
        // The review's case: with only the depth guard, two back-edges made about 2^65 walks.
        var apps = Build(() => Children(Grid, GridChildrenVec, Grid, Grid)).ReadFrontApps();
        var grid = Assert.Single(Assert.Single(apps).Controls);
        Assert.Equal("page_grid", grid.Name);
        Assert.Empty(grid.Children);
    }

    [Fact]
    public void A_two_control_cycle_reads_each_once()
    {
        var apps = Build(() => { Children(Grid, GridChildrenVec, Button); Children(Button, InnerVec, Grid); }).ReadFrontApps();
        Assert.Equal(new[] { "page_grid", "btn_fuel" }, Flat(Assert.Single(apps).Controls).Select(n => n.Name));
    }

    [Fact]
    public void A_drop_down_whose_list_points_back_up_the_tree_reads_no_rows()
    {
        var apps = Build(() => { DropDown("A", new[] { "A" }, open: true); Ptr(Button + (uint)Layout.PulldownList, Grid); }).ReadFrontApps();
        var grid = Assert.Single(Assert.Single(apps).Controls);
        var dropDown = grid.Children[0];
        Assert.True(dropDown.Open);
        Assert.Empty(dropDown.Rows!);
        Assert.Null(dropDown.OpenList);
        Assert.Single(Flat(new[] { grid }), n => n.Name == "page_grid");
    }

    [Fact]
    public void A_reading_walks_at_most_its_node_budget()
    {
        // Eleven grids of 2,048 distinct controls each (a children vector's cap) under the page: 22,539 controls, past the budget.
        const int Parents = 11, Each = 2048;
        var apps = Build(() =>
        {
            uint wide = TreeBase, vecs = TreeBase + 0x18000, parents = TreeBase + 0x46000, page = TreeBase + 0x48000;
            // Controls 4 bytes apart in bytes of 0x01: each visible, of no known kind, with no name, text or children.
            _b.AsSpan((int)wide, Parents * Each * 4 + 0x150).Fill(1);
            for (int k = 0; k < Parents; k++)
            {
                uint parent = parents + (uint)k * 0x200, vec = vecs + (uint)k * 0x4000;
                Control(parent, GridVptr, "p" + k, true);
                Ptr(parent + 0xf4, vec); Ptr(parent + 0xf8, vec + 8 * Each);
                for (int j = 0; j < Each; j++) Ptr(vec + 8 * (uint)j, wide + 4 * (uint)(k * Each + j));
                Ptr(page + 8 * (uint)k, parent);
            }
            Ptr(Page + 0x4c, page); Ptr(Page + 0x50, page + 8 * Parents);
        }).ReadFrontApps();
        Assert.Equal(Bbs757EfbMemory.MaxNodes, Count(Assert.Single(apps).Controls));
    }

    [Fact]
    public void A_chain_deeper_than_the_depth_guard_stops_there()
    {
        // Seventy distinct controls each inside the one before: the walk reads the first MaxDepth + 1 levels.
        const int Links = 70;
        var apps = Build(() =>
        {
            for (int i = 0; i < Links; i++)
            {
                uint link = TreeBase + (uint)i * 0x200;
                Control(link, GridVptr, "c" + i, true);
                if (i + 1 < Links) Children(link, link + 0x100, link + 0x200);
            }
            Ptr(PageVec, TreeBase);
        }).ReadFrontApps();
        int depth = 0;
        for (var level = Assert.Single(apps).Controls; level.Count > 0; level = level[0].Children) depth++;
        Assert.Equal(Bbs757EfbMemory.MaxDepth + 1, depth);
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
