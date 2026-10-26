using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Each overhead subpanel's rows as live-tested on the A350-900 (package 1.2.7): names, what a row does, and the
/// positions it offers, by the aircraft's own state value. A panel's test changes only with a new live drive.
/// </summary>
public class A350OverheadPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"))));

    private const string WiperPositions =
        "0=Intermittent slow, 1=Intermittent medium, 2=Intermittent fast, 3=Off, 4=Slow, 5=Fast";

    /// <summary>Live 2026-10-26: both selectors reach all six positions and the wipers sweep (WIPER_ANIMATION_CPT/_FO);
    /// each rain repellent holds 1 while pressed and 0 on release, which only the cockpit sound reads.</summary>
    [Fact]
    public void Wipers()
    {
        Assert.Equal(new[]
        {
            ("Captain wiper", A350RowAction.Set, WiperPositions),
            ("First officer wiper", A350RowAction.Set, WiperPositions),
            ("Captain rain repellent", A350RowAction.Press, ""),
            ("First officer rain repellent", A350RowAction.Press, ""),
        }, Rows("Wipers"));
    }

    private static IEnumerable<string> Lights(string panel)
    {
        var map = A350ControlMap.Load();
        return A350PanelLamps.Resolve(map, Placement, new[] { "Overhead" }).Where(l => l.Panel == panel).Select(l => l.Name);
    }

    /// <summary>Live 2026-10-26 through MSFSBA's own path (tools/A350Probe): every call button presses; EMER, EVAC
    /// COMMAND and the CAPT and PURS selector reach both positions. EVAC COMMAND lights its EVAC legend
    /// (INI_EVAC_LIGHT) within seconds and puts it out when switched off. Only FWD, PURS and the EVAC variables are
    /// read by the systems; the other calls are chimes. CAPT and PURS: 0 lets only the captain order an evacuation.</summary>
    [Fact]
    public void Calls_and_evacuation()
    {
        Assert.Equal(new[]
        {
            ("Aft", A350RowAction.Press, ""),
            ("All", A350RowAction.Press, ""),
            ("Cabin crew rest", A350RowAction.Press, ""),
            ("Exit", A350RowAction.Press, ""),
            ("Flight crew rest", A350RowAction.Press, ""),
            ("Fwd", A350RowAction.Press, ""),
            ("Mech call", A350RowAction.Press, ""),
            ("Mid", A350RowAction.Press, ""),
            ("Purser", A350RowAction.Press, ""),
            ("EMER", A350RowAction.Set, "0=Off, 1=On"),
            ("EVAC command", A350RowAction.Set, "0=Off, 1=On"),
            ("EVAC horn off", A350RowAction.Press, ""),
            ("EVAC CAPT and PURS", A350RowAction.Set, "0=Captain only, 1=Captain and purser"),
        }, Rows("Calls and Evacuation"));
        Assert.Equal(new[] { "EMER call light", "EVAC light" }, Lights("Calls and Evacuation"));
    }

    /// <summary>Live 2026-10-26: both pushbuttons reach both positions (MASK MAN ON drops the cabin masks; its set
    /// code ignores its guard).</summary>
    [Fact]
    public void Oxygen()
    {
        Assert.Equal(new[]
        {
            ("Crew supply", A350RowAction.Set, "0=Off, 1=On"),
            ("Mask man on", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Oxygen"));
        Assert.Empty(Lights("Oxygen"));   // CREW SUPPLY's OFF legend only mirrors the button
    }

    /// <summary>Live 2026-10-26: the four recorder buttons press. RCDR GND CTL flips and the aircraft puts it straight
    /// back to off (GENERAL_SYSTEMS clears INI_GND_CTL with an engine at N2 52 % or more or off the ground, and in this
    /// state, both engines stopped on the APU, it still did): a mouse click runs the same set code, so MSFSBA saying
    /// "did not move" is what a sighted pilot sees.</summary>
    [Fact]
    public void Recorder()
    {
        Assert.Equal(new[]
        {
            ("ACMS trigger", A350RowAction.Press, ""),
            ("DFDR event", A350RowAction.Press, ""),
            ("RCDR GND CTL", A350RowAction.Set, "0=Off, 1=On"),
            ("CVR test", A350RowAction.Press, ""),
            ("CVR erase", A350RowAction.Press, ""),
        }, Rows("Recorder"));
    }

    /// <summary>Live 2026-10-26: ARMED and ON move with the guard either way; TEST/RESET only with the guard open
    /// (INI_ELT_SWITCH_GUARD 1), else the switch springs back to where it was. The guard's tooltip words are the click's
    /// action (OPEN at 0), not its state.</summary>
    [Fact]
    public void Elt()
    {
        Assert.Equal(new[]
        {
            ("ELT", A350RowAction.Set, "0=Test/reset, 1=Armed, 2=On"),
            ("ELT guard", A350RowAction.Set, "0=Closed, 1=Open"),
        }, Rows("ELT"));
    }

    /// <summary>Live 2026-10-26: EMER GEN reaches both positions. RAT MAN ON is not pressed on a live pass: it deploys
    /// the RAT, which only maintenance stows.</summary>
    [Fact]
    public void Emergency_electrical_power()
    {
        Assert.Equal(new[]
        {
            ("RAT MAN ON", A350RowAction.Press, ""),
            ("EMER GEN", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Emergency Electrical Power"));
        Assert.Equal(new[] { "EMER GEN fault light" }, Lights("Emergency Electrical Power"));
    }

    /// <summary>Live 2026-10-26: with INI_OPTION_FUEL_JETTISON 1 (OIS options), ARM and ACTIVE both move and ACTIVE opens
    /// the jettison valve (its OPEN legend lights) and closes it again. Without the option the cockpit hides the panel and
    /// the aircraft clears all three variables every frame (A350Options).</summary>
    [Fact]
    public void Fuel_jettison()
    {
        Assert.Equal(new[]
        {
            ("Fuel jettison arm", A350RowAction.Set, "0=Off, 1=On"),
            ("Fuel jettison active", A350RowAction.Set, "0=Close, 1=Open"),
        }, Rows("Fuel Jettison"));
        Assert.Equal(new[] { "Fuel jettison arm fault light", "Fuel jettison active open light" }, Lights("Fuel Jettison"));
    }

    /// <summary>Live 2026-10-26: each PRIM and SEC goes off and back on (their guards are not read by the set code).</summary>
    [Fact]
    public void Flight_controls()
    {
        Assert.Equal(new[]
        {
            ("PRIM 1", A350RowAction.Set, "0=Off, 1=On"),
            ("PRIM 2", A350RowAction.Set, "0=Off, 1=On"),
            ("PRIM 3", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 1", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 2", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 3", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Flight Controls"));
        Assert.Equal(new[] { "PRIM 1 fault light", "PRIM 2 fault light", "PRIM 3 fault light",
            "SEC 1 fault light", "SEC 2 fault light", "SEC 3 fault light" }, Lights("Flight Controls"));
    }

    /// <summary>Live 2026-10-26: every IR and ADR goes off and on, its OFF legend following (lit only while its ADIRS
    /// runs); an IR off drops its IRS's alignment flag, back once on. ADIRS 3 went to ATT, NAV and OFF (OFF loses the
    /// alignment, which then restarts); ADIRS 1 and 2 to ATT and back. The ADR 2 and 3 FAULT legends read INI_ADF2/3_FAULT
    /// (iniBuilds' spelling): the status line reads what the cockpit lamp reads.</summary>
    [Fact]
    public void Adirs()
    {
        const string Modes = "0=Off, 1=Nav, 2=Att";
        Assert.Equal(new[]
        {
            ("ADIRS 1", A350RowAction.Set, Modes),
            ("ADIRS 2", A350RowAction.Set, Modes),
            ("ADIRS 3", A350RowAction.Set, Modes),
            ("IR 1", A350RowAction.Set, "0=Off, 1=On"),
            ("IR 2", A350RowAction.Set, "0=Off, 1=On"),
            ("IR 3", A350RowAction.Set, "0=Off, 1=On"),
            ("ADR 1", A350RowAction.Set, "0=Off, 1=On"),
            ("ADR 2", A350RowAction.Set, "0=Off, 1=On"),
            ("ADR 3", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("ADIRS"));
        Assert.Equal(new[]
        {
            "IR 1 fault light", "IR 1 off light", "IR 2 fault light", "IR 2 off light", "IR 3 fault light", "IR 3 off light",
            "ADR 1 fault light", "ADR 1 off light", "ADR 2 fault light", "ADR 2 off light", "ADR 3 fault light", "ADR 3 off light",
        }, Lights("ADIRS"));
    }

    /// <summary>Live 2026-10-26: every position of every switch; beacon and wing follow on the sim's own light state.
    /// The nav lights are drawn from INI_LIGHTS_NAV by the model, so the stock LIGHT NAV stays on whatever the switch.</summary>
    [Fact]
    public void Exterior_lighting()
    {
        Assert.Equal(new[]
        {
            ("Beacon", A350RowAction.Set, "0=Off, 1=On"),
            ("Landing", A350RowAction.Set, "0=Off, 1=On"),
            ("Logo", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Nav", A350RowAction.Set, "0=1, 1=2, 2=Off"),
            ("Nose", A350RowAction.Set, "0=T.O, 1=Taxi, 2=Off"),
            ("Strobe", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Wing", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Exterior Lighting"));
    }

    /// <summary>Live 2026-10-26: MASTER SW on, then START: the APU ran up and AVAIL lit within a minute.</summary>
    [Fact]
    public void Apu()
    {
        Assert.Equal(new[]
        {
            ("Master switch", A350RowAction.Set, "0=Off, 1=On"),
            ("Start", A350RowAction.Press, ""),
        }, Rows("APU"));
        Assert.Equal(new[] { "Master switch fault light", "Start available light", "Start on light" }, Lights("APU"));
    }

    /// <summary>Live 2026-10-26: every position of every selector; NO SMKG and SEAT BELTS reach their signs
    /// (INI_NO_SMOKING_ON, INI_SEATBELTS_ON); the three occupant light knobs land on the value typed.</summary>
    [Fact]
    public void Interior_lighting_and_signs()
    {
        Assert.Equal(new[]
        {
            ("Annunciator light", A350RowAction.Set, "0=Test, 1=Brt, 2=Dim"),
            ("Standby compass light", A350RowAction.Set, "0=Compass and eye reference, 1=Ice indicator, 2=Off"),
            ("EMER exit light", A350RowAction.Set, "0=On, 1=Arm, 2=Off"),
            ("No mobile", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("No smoking and no PED", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Seat belts", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Third occupant console light", A350RowAction.Set, ""),
            ("Third occupant reading light", A350RowAction.Set, ""),
            ("Fourth occupant reading light", A350RowAction.Set, ""),
            ("Coat stowage light", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Interior Lighting and Signs"));
    }

    /// <summary>Live 2026-10-26: each anti-ice and the probe and window heat on and off; wing and engine 1 reach their
    /// system state.</summary>
    [Fact]
    public void Anti_ice()
    {
        Assert.Equal(new[]
        {
            ("Wing anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 1 anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 2 anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("Probe and window heat", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Anti-Ice"));
        Assert.Equal(new[] { "Wing anti ice fault light", "ENG 1 anti ice fault light", "ENG 2 anti ice fault light" }, Lights("Anti-Ice"));
    }

    /// <summary>Live 2026-10-26: every position of every switch and selector. The two temperature selectors are
    /// 0-100 knobs; only the panel-state presets in the systems module write them, and what they select shows on the
    /// SD COND page (the display reader's).</summary>
    [Fact]
    public void Air_conditioning()
    {
        Assert.Equal(new[]
        {
            ("Crossbleed", A350RowAction.Set, "0=Close, 1=Auto, 2=Open"),
            ("Windshield defog", A350RowAction.Set, "0=Off, 1=On"),
            ("Cockpit temperature", A350RowAction.Set, ""),
            ("Cabin temperature", A350RowAction.Set, ""),
            ("Ram air", A350RowAction.Set, "0=Off, 1=On"),
            ("Pack 1", A350RowAction.Set, "0=Off, 1=On"),
            ("Pack 2", A350RowAction.Set, "0=Off, 1=On"),
            ("Hot air 1", A350RowAction.Set, "0=Off, 1=On"),
            ("Hot air 2", A350RowAction.Set, "0=Off, 1=On"),
            ("Air flow", A350RowAction.Set, "0=Man, 1=Lo, 2=Norm, 3=Hi"),
            ("ENG 1 bleed", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 2 bleed", A350RowAction.Set, "0=Off, 1=On"),
            ("APU bleed", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Air Conditioning"));
    }

    /// <summary>Live 2026-10-26: both modes to MAN and back, DITCHING on and off; with a mode in MAN its target steps
    /// 100 ft (altitude) or 100 ft/min (V/S) a click, read on the panel's status lines.</summary>
    [Fact]
    public void Cabin_pressure()
    {
        Assert.Equal(new[]
        {
            ("Cabin altitude target increase", A350RowAction.Increase, ""),
            ("Cabin altitude target decrease", A350RowAction.Decrease, ""),
            ("Cabin altitude mode", A350RowAction.Set, "0=Auto, 1=Man"),
            ("Cabin V/S mode", A350RowAction.Set, "0=Auto, 1=Man"),
            ("Cabin V/S target increase", A350RowAction.Increase, ""),
            ("Cabin V/S target decrease", A350RowAction.Decrease, ""),
            ("Ditching", A350RowAction.Set, "0=Off, 1=On"),
        }, Rows("Cabin Pressure"));
    }
}
