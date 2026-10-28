using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Each glareshield panel's rows as live-tested on the A350-900 (package 1.2.7).</summary>
public class A350GlareshieldPanelTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly A350Placement Placement = A350PanelLayout.Place(Map);

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))));

    /// <summary>These rows, in this order, among the panel's rows (a panel the A380 merges holds several groups' rows).</summary>
    private static void AssertRows(string panel, (string Name, A350RowAction Action, string Positions)[] expected)
    {
        var names = expected.Select(e => e.Name).ToHashSet();
        Assert.Equal(expected, Rows(panel).Where(r => names.Contains(r.Name)));
    }

    /// <summary>These lights, in this order, among the panel's lights.</summary>
    private static void AssertLights(string panel, string[] expected)
    {
        var names = expected.ToHashSet();
        Assert.Equal(expected, Lights(panel).Where(names.Contains));
    }


    private static IEnumerable<string> Lights(string panel) =>
        A350PanelLamps.Resolve(Map, Placement, new[] { "Glareshield" }).Where(l => l.Panel == panel).Select(l => l.Name);

    private const string OffOn = "0=Off, 1=On";
    private const A350RowAction Press = A350RowAction.Press;
    private const A350RowAction Set = A350RowAction.Set;

    /// <summary>Live 2026-10-26 through MSFSBA's path: every button presses (SPD/MACH and HDG-V/S / TRK-FPA swap their
    /// windows' units and flip back on a second press); each knob pushes and pulls, and its value is typed (knots and
    /// Mach, heading and track, 100 and 1000 ft increments, V/S and FPA, and every refusal); AUTOLAND is a light test
    /// the cockpit returns.</summary>
    [Fact]
    public void Fcu()
    {
        AssertRows("FCU", new[]
        {
            ("AP 1", Press, ""), ("AP 2", Press, ""), ("A/THR", Press, ""), ("FD", Set, OffOn), ("LOC", Press, ""),
            ("APPR", Press, ""), ("ALT", Press, ""), ("SPD/MACH", Press, ""), ("HDG-V/S / TRK-FPA", Press, ""),
            ("Metric altitude", Set, "0=Off, 1=On"), ("TRUE/MAG", Set, "0=Magnetic, 1=True"),
            ("Speed knob push", Press, ""), ("Speed knob pull", Press, ""),
            ("Speed", A350RowAction.Typed, ""),
            ("Heading knob push", Press, ""), ("Heading knob pull", Press, ""),
            ("Heading", A350RowAction.Typed, ""),
            ("Altitude knob push", Press, ""), ("Altitude knob pull", Press, ""),
            ("Altitude", A350RowAction.Typed, ""),
            ("V/S knob push", Press, ""), ("V/S knob pull", Press, ""),
            ("VS/FPA", A350RowAction.Typed, ""),
            ("Altitude 100/1000 selector", Press, ""),
            ("FCU display brightness", Set, ""), ("FCU integral lighting", Set, ""),
        });
        AssertLights("FCU", new[] { "AP 1 light", "AP 2 light", "A/THR light", "LOC light", "APPR light", "ALT light" });
        AssertRows("Warnings", new[]
        {
            ("Captain chrono", Press, ""), ("First officer chrono", Press, ""),
            ("Captain master caution", Press, ""), ("First officer master caution", Press, ""),
            ("Captain master warning", Press, ""), ("First officer master warning", Press, ""),
            ("Captain autoland", Press, ""), ("First officer autoland", Press, ""),
            ("Captain ATC message", Press, ""), ("First officer ATC message", Press, ""),
        });
        AssertLights("Warnings", new[] {
            "Captain master caution light", "Captain master caution lower light",
            "First officer master caution light", "First officer master caution lower light",
            // Each MASTER WARN reads its steady warning state, both legends one light (A350PanelLamps.SteadyRules).
            "Captain master warning light", "First officer master warning light",
            "Captain autoland light", "First officer autoland light",
            "Captain ATC message light", "First officer ATC message light",
        });
        AssertRows("HUD", new[]
        {
            ("Captain HUD brightness", Set, ""), ("First officer HUD brightness", Set, ""),
            ("Captain HUD crosswind mode", Set, "0=Norm, 1=X-wind"), ("First officer HUD crosswind mode", Set, "0=Norm, 1=X-wind"),
            ("Captain HUD declutter", Set, OffOn), ("First officer HUD declutter", Set, OffOn),
        });
        AssertRows("Loudspeakers and Table Lights", new[]
        {
            ("Captain loudspeaker", Set, ""), ("First officer loudspeaker", Set, ""),
            ("Captain sliding table light", Set, ""), ("First officer sliding table light", Set, ""),
        });
    }

    /// <summary>The HUD is an option (OIS OPTIONS, <c>INI_IS_HUD</c>): without it the cockpit hides its controls. A
    /// declutter key's input event only animates it; its click code flips the declutter, so the app writes what the
    /// click code writes.</summary>
    [Fact]
    public void Hud_is_an_option_and_its_declutter_is_switched_as_its_click_code_does()
    {
        foreach (var id in new[]
        {
            "AIRLINER_FCU_HUD_BRIGHTNESS_LEFT", "AIRLINER_FCU_HUD_BRIGHTNESS_RIGHT", "AIRLINER_FCU_HUD_XWIND_LEFT",
            "AIRLINER_FCU_HUD_XWIND_RIGHT", "INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID", "INSTRUMENT_FCU_HUD_DECLUTTER_RIGHT_IE_ID",
            "AIRLINER_HUD_LEFT", "AIRLINER_HUD_RIGHT", "AIRLINER_HUD_HANDLE_LEFT", "AIRLINER_HUD_HANDLE_RIGHT",
        })
            Assert.Equal(("INI_IS_HUD", "HUD"), (A350Options.For(id)!.Var, A350Options.For(id)!.Name));
        var declutter = Map.Controls.Single(c => c.Id == "INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID");
        Assert.Equal(new A350Step[]
        {
            new A350CalcStep("0 (>L:INI_FCU_HUD_DECLUTTER_LEFT_ANIM) 2 (>L:INI_FCU_HUD_DECLUTTER_LEFT_TIMER) 1 (>L:INI_FCU_HUD_DECLUTTER_LEFT)"),
        }, A350WritePlan.ForSet(declutter, 1, 0).Steps);
        Assert.Equal(new A350Step[]
        {
            new A350CalcStep("0 (>L:INI_FCU_HUD_DECLUTTER_LEFT_ANIM) 2 (>L:INI_FCU_HUD_DECLUTTER_LEFT_TIMER) 0 (>L:INI_FCU_HUD_DECLUTTER_LEFT)"),
        }, A350WritePlan.ForSet(declutter, 0, 1).Steps);
        Assert.True(A350WritePlan.ForSet(declutter, 1, 1).IsEmpty);
    }

    /// <summary>Live 2026-10-26 through MSFSBA's path: every position of the ND mode and range selectors; baro PUSH
    /// selects STD (XMLVAR_Baro1_Mode 3) and PULL QNH (0); the unit button swaps hPa and inHg; each filter button toggles
    /// its light (WPT, VORD and NDB exclude each other); LS, TAXI and VV on and off. The ZOOM range positions step from
    /// 0.2 to 5 NM (the manual: "from a maximum of 5nm down to a minimum of 0.2nm"; the middle three are inferred).</summary>
    [Fact]
    public void Efis_captain()
    {
        AssertRows("EFIS Captain", new[]
        {
            ("Captain ND mode", Set, "0=Rose LS, 1=Rose VOR, 2=Rose NAV, 3=Arc, 4=Plan"),
            ("Captain ND range", Set, "0=Zoom 0.2 NM, 1=Zoom 0.5 NM, 2=Zoom 1 NM, 3=Zoom 2 NM, 4=Zoom 5 NM, 5=10 NM, 6=20 NM, "
                + "7=40 NM, 8=80 NM, 9=160 NM, 10=320 NM, 11=640 NM"),
            // Each baro knob row says what it does, from iniBuilds' tooltips (PUSH "STD VALUE", PULL "SET QNH"): the A320
            // habit (pull for STD) set nothing in flight, live 2026-10-28.
            ("Captain baro STD (push)", Press, ""), ("Captain baro QNH (pull)", Press, ""),
            ("QNH (in Capt unit)", A350RowAction.Typed, ""),
            ("Captain baro unit", Press, ""),
            ("Captain CSTR", Press, ""), ("Captain WPT", Press, ""), ("Captain VORD", Press, ""), ("Captain NDB", Press, ""),
            ("Captain ARPT", Press, ""),
            ("Captain VOR 1", Press, ""), ("Captain WX", Press, ""), ("Captain TERR", Press, ""), ("Captain TRAF", Press, ""),
            ("Captain VOR 2", Press, ""),
            ("Captain LS", Set, OffOn), ("Captain taxi", Set, OffOn), ("Captain VV", Set, OffOn),
        });
        AssertLights("EFIS Captain", new[] { "Captain CSTR light", "Captain WPT light", "Captain VORD light", "Captain NDB light",
            "Captain ARPT light", "Captain VOR 1 light", "Captain WX light", "Captain TERR light", "Captain TRAF light",
            "Captain VOR 2 light" });
    }

    /// <summary>Live 2026-10-26: the first officer's panel, as the captain's (its baro is XMLVAR_Baro2_Mode, unit flag
    /// XMLVAR_Baro_Selector_HPA_2, setting KOHLSMAN SETTING MB:2).</summary>
    [Fact]
    public void Efis_first_officer()
    {
        AssertRows("EFIS First Officer", new[]
        {
            ("First officer ND mode", Set, "0=Rose LS, 1=Rose VOR, 2=Rose NAV, 3=Arc, 4=Plan"),
            ("First officer ND range", Set, "0=Zoom 0.2 NM, 1=Zoom 0.5 NM, 2=Zoom 1 NM, 3=Zoom 2 NM, 4=Zoom 5 NM, 5=10 NM, 6=20 NM, "
                + "7=40 NM, 8=80 NM, 9=160 NM, 10=320 NM, 11=640 NM"),
            ("First officer baro STD (push)", Press, ""), ("First officer baro QNH (pull)", Press, ""),
            ("QNH (in F/O unit)", A350RowAction.Typed, ""),
            ("First officer baro unit", Press, ""),
            ("First officer CSTR", Press, ""), ("First officer WPT", Press, ""), ("First officer VORD", Press, ""),
            ("First officer NDB", Press, ""), ("First officer ARPT", Press, ""),
            ("First officer VOR 1", Press, ""), ("First officer WX", Press, ""), ("First officer TERR", Press, ""),
            ("First officer TRAF", Press, ""), ("First officer VOR 2", Press, ""),
            ("First officer LS", Set, OffOn), ("First officer taxi", Set, OffOn), ("First officer VV", Set, OffOn),
        });
        AssertLights("EFIS First Officer", new[] { "First officer CSTR light", "First officer WPT light", "First officer VORD light",
            "First officer NDB light", "First officer ARPT light", "First officer VOR 1 light", "First officer WX light",
            "First officer TERR light", "First officer TRAF light", "First officer VOR 2 light" });
    }
}
