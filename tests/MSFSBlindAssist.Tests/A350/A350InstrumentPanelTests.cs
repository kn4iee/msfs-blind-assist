using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Each instrument-panel subpanel's rows as live-tested on the A350-900 (package 1.2.7, 2026-10-26, through MSFSBA's
/// own path with tools/A350Probe): names, what a row does, and the positions it offers.
/// </summary>
public class A350InstrumentPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))));

    private static IEnumerable<string> Lights(string panel) =>
        A350PanelLamps.Resolve(A350ControlMap.Load(), Placement, new[] { "Instrument" }).Where(l => l.Panel == panel).Select(l => l.Name);

    private const string OffOn = "0=Off, 1=On";

    /// <summary>The gear lever refuses UP on the ground (the aircraft's own interlock: "L/G lever did not move"); anti-skid
    /// off and on; the brake fans run (INI_Brake_Fans_ANIM turns). Named as the A380's Gear panel names them.</summary>
    [Fact]
    public void Gear()
    {
        Assert.Equal(new[]
        {
            ("Gear lever", A350RowAction.Set, "0=Up, 1=Down"),
            ("Anti-skid", A350RowAction.Set, OffOn),
            ("Brake fans", A350RowAction.Set, OffOn),
        }, Rows("Gear"));
        Assert.Equal(new[] { "Brakes hot light", "Gear lever red arrow light" }, Lights("Gear"));
    }

    /// <summary>A/BRK arms (INI_AUTOBRAKE_ARMED, level 4 = RTO on the ground) and disarms, its light following. The
    /// runway condition is an index the WASM keeps to itself (GEAR::Update, 0 DRY to 5 ICE), changed only while the
    /// knob's menu is open on the SD (pull); a click up moves toward DRY and stops there, a click down toward ICE. Its
    /// words are the SD menu's (read with the display reader).</summary>
    [Fact]
    public void Autobrake()
    {
        Assert.Equal(new[]
        {
            ("Runway condition knob push", A350RowAction.Press, ""),
            ("Runway condition knob pull", A350RowAction.Press, ""),
            ("Runway condition", A350RowAction.Custom,
                "1=1, ice, poor, 2=2, standing water or slush, medium to poor, 3=3, snow or slippery when wet, medium, "
                + "4=4, compacted snow, good to medium, 5=5, wet, good, 6=6, dry"),
            ("Runway condition or braking action", A350RowAction.Set, "0=Runway condition, 1=Braking action"),
            ("A/BRK", A350RowAction.Press, ""),
        }, Rows("Autobrake"));
        Assert.Equal(new[] { "A/BRK light" }, Lights("Autobrake"));
    }

    [Fact]
    public void The_runway_condition_is_set_by_the_knobs_own_clicks_from_dry()
    {
        var knob = A350ControlMap.Load().Find("AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR")!;
        var plan = A350RunwayCondition.Plan(knob, 4);
        var rpns = plan.Steps.OfType<A350CalcStep>().Select(s => s.Rpn).ToList();
        Assert.Equal("1 (>B:AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR_PULL_Set)", rpns[0]);
        Assert.Equal(Enumerable.Repeat("1 (>B:AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR_Set)", 5), rpns.Skip(1).Take(5));
        Assert.Equal(Enumerable.Repeat("-1 (>B:AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR_Set)", 2), rpns.Skip(6).Take(2));
        Assert.Equal("1 (>B:AIRLINER_LDG_RWY_COND_BRK_ACTION_SELECTOR_PUSH_Set)", rpns[^1]);
        Assert.Equal(9, rpns.Count);
        // One click a frame: the WASM consumes a command flag once per frame, so two in one frame are one.
        Assert.All(plan.Steps.Zip(plan.Steps.Skip(1)), pair =>
            Assert.True(pair.First is A350DelayStep || pair.Second is A350DelayStep));
        Assert.Equal(A350WritePlan.NotAPositionRefusal, A350RunwayCondition.Plan(knob, 7).Refusal);
    }

    /// <summary>The ISIS, the standby flight display (INI_ISIS1_POWERED; the manual's cockpit layout item 22): each key is
    /// a hold (its set code acts only on 2) and the ISIS consumes each command; what it then shows is the ISIS gauge's own
    /// (the emulated draw answers sim variables with defaults, so it is read with the display reader). Its baro is the
    /// sim's KOHLSMAN SETTING MB:3: a typed QNH. Named by the unit, not by a side: it is no captain's instrument.</summary>
    [Fact]
    public void Isis()
    {
        Assert.Equal(new[]
        {
            ("ISIS STD/CHANGE", A350RowAction.Press, ""), ("ISIS LS/DIR TO", A350RowAction.Press, ""),
            ("ISIS menu", A350RowAction.Press, ""), ("ISIS minus", A350RowAction.Press, ""),
            ("ISIS mode", A350RowAction.Press, ""), ("ISIS plus", A350RowAction.Press, ""),
            // Not "QNH": the EFIS's "QNH (in Capt unit)" reads the same in a flat list (gap 84).
            ("ISIS QNH", A350RowAction.Typed, ""),
        }, Rows("ISIS"));
        Assert.Null(A350Options.For("AIRLINER_ISIS_MENU_1"));
    }

    /// <summary>The second ISIS unit is the standby navigation display (INI_ISIS2_POWERED; item 25 of the manual's cockpit
    /// layout), the OIS EQUIPMENT option STANDBY NAV DISPLAY, which iniBuilds' variable calls the first officer's ISIS
    /// (INI_OPTION_FO_ISIS): the A350 runs one ISIS unit as the SFD and the other as the SND. Its own panel, its keys by
    /// their unit.</summary>
    [Fact]
    public void Standby_navigation_display()
    {
        Assert.Equal(new[]
        {
            ("SND STD/CHANGE", A350RowAction.Press, ""), ("SND LS/DIR TO", A350RowAction.Press, ""),
            ("SND menu", A350RowAction.Press, ""), ("SND minus", A350RowAction.Press, ""),
            ("SND mode", A350RowAction.Press, ""), ("SND plus", A350RowAction.Press, ""),
            ("SND set/sel knob increase", A350RowAction.Increase, ""),
            ("SND set/sel knob decrease", A350RowAction.Decrease, ""),
        }, Rows("SND"));
        var option = A350Options.For("AIRLINER_ISIS_MENU_2")!;
        Assert.Equal(("INI_OPTION_FO_ISIS", "Standby navigation display"), (option.Var, option.Name));
        Assert.Equal("Standby navigation display is not fitted. It is an option on the OIS options page.", option.NotFitted);
    }

    /// <summary>Every position of both selectors.</summary>
    [Fact]
    public void Source_switching()
    {
        Assert.Equal(new[]
        {
            ("FMS source", A350RowAction.Set, "0=Both on 2, 1=Norm, 2=Both on 1"),
            ("Air data source", A350RowAction.Set, "0=Captain on backup, 1=Auto, 2=First officer on backup"),
        }, Rows("Source Switching"));
    }

    /// <summary>Each display off and on, named by what went dark: the OIS displays' INI_EFB1/EFB2_STATE, the captain's
    /// EFIS draw (PFD and ND), the MFD draw and INI_MFD_STATE, and the centre draw (E/WD and SD). Each side's two on a
    /// panel of their own (owner 2026-10-27), after the centre's.</summary>
    [Fact]
    public void Display_power()
    {
        Assert.Equal(new[]
        {
            ("Center lower display", A350RowAction.Set, OffOn), ("Center upper display", A350RowAction.Set, OffOn),
        }, Rows("Display Power"));
        Assert.Equal(new[]
        {
            ("Captain OIS display", A350RowAction.Set, OffOn), ("Captain outer display", A350RowAction.Set, OffOn),
        }, Rows("Captain Display Power"));
        Assert.Equal(new[]
        {
            ("First officer OIS display", A350RowAction.Set, OffOn), ("First officer outer display", A350RowAction.Set, OffOn),
        }, Rows("First Officer Display Power"));
    }

    /// <summary>The pedal light and every knob; the OIS switch springs back to NEUTRAL and only the cockpit sound reads
    /// it; OIS VIEW OFFSIDE shows the other side's OIS, one side at a time (the other side's button reads INOP).</summary>
    [Theory]
    [InlineData("Captain Side Panel", "Captain")]
    [InlineData("First Officer Side Panel", "First officer")]
    public void Side_panels(string panel, string side)
    {
        // Each side's name carries its side, as the glareshield's twins do: Ctrl+M lists both sides in one list.
        Assert.Equal(new[]
        {
            ($"{side} pedal light", A350RowAction.Set, OffOn),
            ($"{side} OIS switch", A350RowAction.Set, "0=EFB, 1=Neutral, 2=AVNCS"),
            ($"{side} OIS view offside", A350RowAction.Press, ""),
            ($"{side} console light", A350RowAction.Set, ""),
            ($"{side} map light", A350RowAction.Set, ""),
        }, Rows(panel));
        Assert.Equal(new[] { $"{side} OIS view offside INOP light", $"{side} OIS view offside on light" }, Lights(panel));
    }

    [Theory]
    [InlineData("Captain Individual Panel", "Captain")]
    [InlineData("First Officer Individual Panel", "First officer")]
    public void Individual_panels(string panel, string side)
    {
        Assert.Equal(new[]
        {
            ($"{side} lateral vent and defog", A350RowAction.Set, ""),
            ($"{side} foot vent", A350RowAction.Set, ""),
            ($"{side} heater", A350RowAction.Set, "0=High, 1=Low, 2=Off"),
        }, Rows(panel));
    }
}
