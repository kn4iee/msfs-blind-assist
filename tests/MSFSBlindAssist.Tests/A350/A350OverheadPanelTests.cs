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


    private const string WiperPositions =
        "0=Intermittent slow, 1=Intermittent medium, 2=Intermittent fast, 3=Off, 4=Slow, 5=Fast";

    /// <summary>Live 2026-10-26: both selectors reach all six positions and the wipers sweep (WIPER_ANIMATION_CPT/_FO);
    /// each rain repellent holds 1 while pressed and 0 on release, which only the cockpit sound reads. Each side's on a
    /// panel of its own (owner 2026-10-27), though the A380 keeps both wipers on one.</summary>
    [Theory]
    [InlineData("Captain Wiper", "Captain")]
    [InlineData("First Officer Wiper", "First officer")]
    public void Wipers(string panel, string side)
    {
        Assert.Equal(new[]
        {
            ($"{side} wiper", A350RowAction.Set, WiperPositions),
            ($"{side} rain repellent", A350RowAction.Press, ""),
        }, Rows(panel));
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
    public void Calls_and_evacuation_panels()
    {
        AssertRows("Calls", new[]
        {
            ("Aft call", A350RowAction.Press, ""),
            ("All call", A350RowAction.Press, ""),
            ("Cabin crew rest", A350RowAction.Press, ""),
            ("Exit call", A350RowAction.Press, ""),
            ("Flight crew rest", A350RowAction.Press, ""),
            ("Fwd call", A350RowAction.Press, ""),
            ("Mech call", A350RowAction.Press, ""),
            ("Mid call", A350RowAction.Press, ""),
            ("Purser call", A350RowAction.Press, ""),
            ("EMER call", A350RowAction.Set, "0=Off, 1=On"),
        });
        AssertLights("Calls", new[] { "EMER call light" });
        AssertRows("Evacuation", new[]
        {
            ("EVAC command", A350RowAction.Set, "0=Off, 1=On"),
            ("EVAC horn off", A350RowAction.Press, ""),
            ("EVAC CAPT and PURS", A350RowAction.Set, "0=Captain only, 1=Captain and purser"),
        });
        AssertLights("Evacuation", new[] { "EVAC light" });
    }

    /// <summary>Live 2026-10-26: both pushbuttons reach both positions (MASK MAN ON drops the cabin masks; its set
    /// code ignores its guard).</summary>
    [Fact]
    public void Oxygen()
    {
        AssertRows("Oxygen", new[]
        {
            ("Crew oxygen supply", A350RowAction.Set, "0=Off, 1=On"),
            ("Mask man on", A350RowAction.Set, "0=Off, 1=On"),
        });
        Assert.Empty(Lights("Oxygen"));   // CREW SUPPLY's OFF legend only mirrors the button
    }

    /// <summary>Live 2026-10-26: the four recorder buttons press. RCDR GND CTL flips and the aircraft puts it straight
    /// back to off (GENERAL_SYSTEMS clears INI_GND_CTL with an engine at N2 52 % or more or off the ground, and in this
    /// state, both engines stopped on the APU, it still did): a mouse click runs the same set code, so MSFSBA saying
    /// "did not move" is what a sighted pilot sees.</summary>
    [Fact]
    public void Recorder()
    {
        AssertRows("Recorder and Misc", new[]
        {
            ("ACMS trigger", A350RowAction.Press, ""),
            ("DFDR event", A350RowAction.Press, ""),
            ("RCDR GND CTL", A350RowAction.Set, "0=Off, 1=On"),
            ("CVR test", A350RowAction.Press, ""),
            ("CVR erase", A350RowAction.Press, ""),
        });
    }

    /// <summary>Live 2026-10-26: ARMED and ON move with the guard either way; TEST/RESET only with the guard open
    /// (INI_ELT_SWITCH_GUARD 1), else the switch springs back to where it was. The guard's tooltip words are the click's
    /// action (OPEN at 0), not its state.</summary>
    [Fact]
    public void Elt()
    {
        AssertRows("Recorder and Misc", new[]
        {
            ("ELT", A350RowAction.Set, "0=Test/reset, 1=Armed, 2=On"),
            ("ELT guard", A350RowAction.Set, "0=Closed, 1=Open"),
        });
    }

    /// <summary>Live 2026-10-26: EMER GEN reaches both positions. RAT MAN ON is not pressed on a live pass: it deploys
    /// the RAT, which only maintenance stows.</summary>
    [Fact]
    public void Emergency_electrical_power()
    {
        AssertRows("ELEC", new[]
        {
            ("RAT MAN ON", A350RowAction.Press, ""),
            ("EMER GEN", A350RowAction.Set, "0=Off, 1=On"),
        });
        AssertLights("ELEC", new[] { "EMER GEN fault light" });
    }

    /// <summary>Live 2026-10-26: with INI_OPTION_FUEL_JETTISON 1 (OIS options), ARM and ACTIVE both move and ACTIVE opens
    /// the jettison valve (its OPEN legend lights) and closes it again. Without the option the cockpit hides the panel and
    /// the aircraft clears all three variables every frame (A350Options).</summary>
    [Fact]
    public void Fuel_jettison()
    {
        AssertRows("Fuel", new[]
        {
            ("Fuel jettison arm", A350RowAction.Set, "0=Off, 1=On"),
            ("Fuel jettison active", A350RowAction.Set, "0=Close, 1=Open"),
        });
        AssertLights("Fuel", new[] { "Fuel jettison arm fault light", "Fuel jettison active open light" });
    }

    /// <summary>Live 2026-10-26: each PRIM and SEC goes off and back on (their guards are not read by the set code).</summary>
    [Fact]
    public void Flight_controls()
    {
        AssertRows("Flight Control Computers", new[]
        {
            ("PRIM 1", A350RowAction.Set, "0=Off, 1=On"),
            ("PRIM 2", A350RowAction.Set, "0=Off, 1=On"),
            ("PRIM 3", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 1", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 2", A350RowAction.Set, "0=Off, 1=On"),
            ("SEC 3", A350RowAction.Set, "0=Off, 1=On"),
        });
        AssertLights("Flight Control Computers", new[] { "PRIM 1 fault light", "PRIM 2 fault light", "PRIM 3 fault light",
            "SEC 1 fault light", "SEC 2 fault light", "SEC 3 fault light" });
    }

    /// <summary>Live 2026-10-26: every IR and ADR goes off and on, its OFF legend following (lit only while its ADIRS
    /// runs); an IR off drops its IRS's alignment flag, back once on. ADIRS 3 went to ATT, NAV and OFF (OFF loses the
    /// alignment, which then restarts); ADIRS 1 and 2 to ATT and back. The ADR 2 and 3 FAULT legends read INI_ADF2/3_FAULT
    /// (iniBuilds' spelling): the status line reads what the cockpit lamp reads.</summary>
    [Fact]
    public void Adirs()
    {
        const string Modes = "0=Off, 1=Nav, 2=Att";
        AssertRows("ADIRS", new[]
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
        });
        AssertLights("ADIRS", new[] {
            "IR 1 fault light", "IR 1 off light", "IR 2 fault light", "IR 2 off light", "IR 3 fault light", "IR 3 off light",
            "ADR 1 fault light", "ADR 1 off light", "ADR 2 fault light", "ADR 2 off light", "ADR 3 fault light", "ADR 3 off light",
            "IRS on battery light",
        });
    }

    /// <summary>Live 2026-10-26: every position of every switch; beacon and wing follow on the sim's own light state.
    /// The nav lights are drawn from INI_LIGHTS_NAV by the model, so the stock LIGHT NAV stays on whatever the switch.</summary>
    [Fact]
    public void Exterior_lighting()
    {
        AssertRows("Exterior Lighting", new[]
        {
            ("Beacon light", A350RowAction.Set, "0=Off, 1=On"),
            ("Landing lights", A350RowAction.Set, "0=Off, 1=On"),
            ("Logo lights", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Nav lights", A350RowAction.Set, "0=1, 1=2, 2=Off"),
            ("Nose light", A350RowAction.Set, "0=T.O, 1=Taxi, 2=Off"),
            ("Strobe lights", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Wing lights", A350RowAction.Set, "0=Off, 1=On"),
        });
    }

    /// <summary>Live 2026-10-26: MASTER SW on, then START: the APU ran up and AVAIL lit within a minute.</summary>
    [Fact]
    public void Apu()
    {
        AssertRows("APU", new[]
        {
            ("APU master switch", A350RowAction.Set, "0=Off, 1=On"),
            ("APU start", A350RowAction.Press, ""),
        });
        AssertLights("APU", new[] { "APU master switch fault light", "APU start available light", "APU start on light" });
    }

    /// <summary>Live 2026-10-26: every position of every selector; NO SMKG and SEAT BELTS reach their signs
    /// (INI_NO_SMOKING_ON, INI_SEATBELTS_ON); the three occupant light knobs land on the value typed.</summary>
    [Fact]
    public void Interior_lighting_and_signs()
    {
        AssertRows("Signs", new[]
        {
            ("EMER exit light", A350RowAction.Set, "0=On, 1=Arm, 2=Off"),
            ("No mobile", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("No smoking and no PED", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
            ("Seat belts", A350RowAction.Set, "0=On, 1=Auto, 2=Off"),
        });
        AssertRows("Interior Lighting", new[]
        {
            ("Annunciator light", A350RowAction.Set, "0=Test, 1=Brt, 2=Dim"),
            ("Standby compass light", A350RowAction.Set, "0=Compass and eye reference, 1=Ice indicator, 2=Off"),
            ("Third occupant console light", A350RowAction.Set, ""),
            ("Third occupant reading light", A350RowAction.Set, ""),
            ("Fourth occupant reading light", A350RowAction.Set, ""),
            ("Coat stowage light", A350RowAction.Set, "0=Off, 1=On"),
        });
    }

    /// <summary>Live 2026-10-26: each anti-ice and the probe and window heat on and off; wing and engine 1 reach their
    /// system state.</summary>
    [Fact]
    public void Anti_ice()
    {
        AssertRows("Anti Ice", new[]
        {
            ("Wing anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 1 anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 2 anti ice", A350RowAction.Set, "0=Off, 1=On"),
            ("Probe and window heat", A350RowAction.Set, "0=Off, 1=On"),
        });
        AssertLights("Anti Ice", new[] { "Wing anti ice fault light", "ENG 1 anti ice fault light", "ENG 2 anti ice fault light" });
    }

    /// <summary>Live 2026-10-26: every position of every switch and selector. The two temperature selectors are
    /// 0-100 knobs; only the panel-state presets in the systems module write them, and what they select shows on the
    /// SD COND page (the display reader's).</summary>
    [Fact]
    public void Air_conditioning()
    {
        AssertRows("Air Conditioning", new[]
        {
            ("Windshield defog", A350RowAction.Set, "0=Off, 1=On"),
            ("Cockpit temperature", A350RowAction.Set, ""),
            ("Cabin temperature", A350RowAction.Set, ""),
            ("Ram air", A350RowAction.Set, "0=Off, 1=On"),
            ("Pack 1", A350RowAction.Set, "0=Off, 1=On"),
            ("Pack 2", A350RowAction.Set, "0=Off, 1=On"),
            ("Hot air 1", A350RowAction.Set, "0=Off, 1=On"),
            ("Hot air 2", A350RowAction.Set, "0=Off, 1=On"),
            ("Air flow", A350RowAction.Set, "0=Man, 1=Lo, 2=Norm, 3=Hi"),
        });
        AssertRows("Bleed Air", new[]
        {
            ("Crossbleed", A350RowAction.Set, "0=Close, 1=Auto, 2=Open"),
            ("ENG 1 bleed", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 2 bleed", A350RowAction.Set, "0=Off, 1=On"),
            ("APU bleed", A350RowAction.Set, "0=Off, 1=On"),
        });
    }

    /// <summary>Live 2026-10-26: both modes to MAN and back, DITCHING on and off; each target typed to its range ends
    /// (-2000 and 13500 ft, -1500 and +2500 ft/min), landed by the knob's own click (100 ft, 50 ft/min), read on the
    /// panel's status lines.</summary>
    [Fact]
    public void Cabin_pressure()
    {
        AssertRows("Pressurization", new[]
        {
            ("Cabin altitude target (feet)", A350RowAction.Typed, ""),
            ("Cabin altitude mode", A350RowAction.Set, "0=Auto, 1=Man"),
            ("Cabin V/S mode", A350RowAction.Set, "0=Auto, 1=Man"),
            ("Cabin V/S target (feet per minute)", A350RowAction.Typed, ""),
            ("Ditching", A350RowAction.Set, "0=Off, 1=On"),
        });
    }

    /// <summary>Live 2026-10-26: every switch off and on (a battery's FAULT legend lights a moment after it goes off and
    /// clears when it is back); with the OIS GPU connected, EXT 1 and EXT 2 each put external power on line (AVAIL out,
    /// ON lit) and off again. The DRIVE buttons were not pressed: they disconnect an IDG for good.</summary>
    [Fact]
    public void Electrical()
    {
        const string OffOn = "0=Off, 1=On";
        AssertRows("ELEC", new[]
        {
            ("BAT 1", A350RowAction.Set, OffOn), ("BAT 2", A350RowAction.Set, OffOn),
            ("BAT EMER 1", A350RowAction.Set, OffOn), ("BAT EMER 2", A350RowAction.Set, OffOn),
            ("Bus tie", A350RowAction.Set, OffOn),
            ("Commercial 1", A350RowAction.Set, OffOn), ("Commercial 2", A350RowAction.Set, OffOn),
            ("ELM", A350RowAction.Set, OffOn),
            ("EXT 1", A350RowAction.Press, ""), ("EXT 2", A350RowAction.Press, ""),
            ("Galley", A350RowAction.Set, OffOn),
            ("GEN 1A", A350RowAction.Set, OffOn), ("GEN 1B", A350RowAction.Set, OffOn),
            ("GEN 2A", A350RowAction.Set, OffOn), ("GEN 2B", A350RowAction.Set, OffOn),
            ("APU GEN", A350RowAction.Set, OffOn),
            ("DRIVE 1A", A350RowAction.Press, ""), ("DRIVE 1B", A350RowAction.Press, ""),
            ("DRIVE 2A", A350RowAction.Press, ""), ("DRIVE 2B", A350RowAction.Press, ""),
            ("PAX SYS", A350RowAction.Set, OffOn),
            ("Side 1 and 2 isolation", A350RowAction.Set, OffOn),
        });
        AssertLights("ELEC", new[] {
            "BAT 1 fault light", "BAT 2 fault light", "BAT EMER 1 fault light", "BAT EMER 2 fault light", "ELM fault light",
            "EXT 1 available light", "EXT 1 on light", "EXT 2 available light", "EXT 2 on light",
            "GEN 1A fault light", "GEN 1B fault light", "GEN 2A fault light", "GEN 2B fault light", "APU GEN fault light",
            "DRIVE 1A fault light", "DRIVE 1A off light", "DRIVE 1B fault light", "DRIVE 1B off light",
            "DRIVE 2A fault light", "DRIVE 2A off light", "DRIVE 2B fault light", "DRIVE 2B off light",
            "PAX SYS isolated light",
        });
    }

    /// <summary>Live 2026-10-26: every pump off and on, both crossfeeds (their OPEN legend follows the valve), both
    /// transfers and the center tank feed (their guards are not read by the set code). iniBuilds' variables cross the
    /// sides' inner and outer names (L TK MAIN is INI_OUTER_TANK_LEFT, R TK MAIN INI_INNER_TANK_RIGHT); the rows follow
    /// the buttons.</summary>
    [Fact]
    public void Fuel()
    {
        const string OffOn = "0=Off, 1=On";
        AssertRows("Fuel", new[]
        {
            ("Left main pump", A350RowAction.Set, OffOn), ("Left standby pump", A350RowAction.Set, OffOn),
            ("Right main pump", A350RowAction.Set, OffOn), ("Right standby pump", A350RowAction.Set, OffOn),
            ("Center left pump", A350RowAction.Set, OffOn), ("Center right pump", A350RowAction.Set, OffOn),
            ("Crossfeed A", A350RowAction.Set, OffOn), ("Crossfeed B", A350RowAction.Set, OffOn),
            ("Left fuel transfer", A350RowAction.Set, OffOn), ("Right fuel transfer", A350RowAction.Set, OffOn),
            ("Center tank feed", A350RowAction.Set, OffOn),
        });
        AssertLights("Fuel", new[] {
            "Left main pump fault light", "Left standby pump fault light", "Right main pump fault light",
            "Right standby pump fault light", "Center left pump fault light", "Center right pump fault light",
            "Crossfeed A open light", "Crossfeed B open light", "Left fuel transfer fault light", "Right fuel transfer fault light",
            "Center tank feed fault light",
        });
    }

    /// <summary>Live 2026-10-26: each engine pump off and on. The four SUPPLY buttons (fire shutoff valves) only ever
    /// set their variable to 1, as the released real button is reset on the ground: not pressed in a test pass.</summary>
    [Fact]
    public void Hydraulics()
    {
        AssertRows("Hydraulics", new[]
        {
            ("Green ENG 1 pump", A350RowAction.Set, "0=Off, 1=On"), ("Yellow ENG 1 pump", A350RowAction.Set, "0=Off, 1=On"),
            ("Green ENG 2 pump", A350RowAction.Set, "0=Off, 1=On"), ("Yellow ENG 2 pump", A350RowAction.Set, "0=Off, 1=On"),
            ("Green ENG 1 supply", A350RowAction.Press, ""), ("Yellow ENG 1 supply", A350RowAction.Press, ""),
            ("Green ENG 2 supply", A350RowAction.Press, ""), ("Yellow ENG 2 supply", A350RowAction.Press, ""),
        });
    }

    /// <summary>Live 2026-10-26: FIRE TEST held 3 s lights all thirteen fire legends (each fire button, each agent's
    /// SQUIB and DISCH). The fire buttons and agents latch (released, discharged): never pressed in a test pass.</summary>
    [Fact]
    public void Fire()
    {
        AssertRows("Fire", new[]
        {
            ("APU fire", A350RowAction.Press, ""), ("ENG 1 fire", A350RowAction.Press, ""), ("ENG 2 fire", A350RowAction.Press, ""),
            ("APU agent", A350RowAction.Press, ""),
            ("ENG 1 agent 1", A350RowAction.Press, ""), ("ENG 1 agent 2", A350RowAction.Press, ""),
            ("ENG 2 agent 1", A350RowAction.Press, ""), ("ENG 2 agent 2", A350RowAction.Press, ""),
            ("Fire test", A350RowAction.Press, ""),
        });
        Assert.Equal(14, Lights("Fire").Count());
        Assert.Equal("MLG bay fire light", Lights("Fire").Last());   // an indicator with no button of its own
    }

    /// <summary>Live 2026-10-26: every button on and off (the FADEC ground power buttons sit here, as on the A380).</summary>
    [Fact]
    public void Engine_fadec_and_manual_start()
    {
        AssertRows("Engine FADEC and Manual Start", new[]
        {
            ("ENG 1 FADEC GND PWR", A350RowAction.Set, "0=Off, 1=On"), ("ENG 2 FADEC GND PWR", A350RowAction.Set, "0=Off, 1=On"),
            ("ENG 1 man start", A350RowAction.Set, "0=Off, 1=On"), ("ENG 2 man start", A350RowAction.Set, "0=Off, 1=On"),
        });
    }

    /// <summary>Live 2026-10-26: every button off and on.</summary>
    [Fact]
    public void Ventilation()
    {
        AssertRows("Ventilation", new[]
        {
            ("Avionics extract", A350RowAction.Set, "0=Off, 1=On"),
            ("Cabin fans", A350RowAction.Set, "0=Off, 1=On"),
            ("Avionics cooling", A350RowAction.Set, "0=Off, 1=On"),
        });
        AssertLights("Ventilation", new[] { "Avionics extract fault light", "Avionics cooling fault light", "Avionics smoke light" });
    }

    private const string OffOn = "0=Off, 1=On";

    /// <summary>Live 2026-10-26: every button off and on; both temperature knobs land on the value typed. Each isolation
    /// valve's upper legend lights while its valve and its button disagree.</summary>
    [Fact]
    public void Cargo_air_conditioning()
    {
        AssertRows("Cargo Air", new[]
        {
            ("Aft isolation valves", A350RowAction.Set, OffOn), ("Forward isolation valves", A350RowAction.Set, OffOn),
            ("Bulk isolation valves", A350RowAction.Set, OffOn),
            ("Bulk temperature", A350RowAction.Set, ""), ("Forward temperature", A350RowAction.Set, ""),
            ("Cargo heater", A350RowAction.Set, OffOn),
        });
        AssertLights("Cargo Air", new[] { "Aft isolation valves fault light", "Forward isolation valves fault light",
            "Bulk isolation valves fault light", "Cargo heater fault light" });
    }

    /// <summary>The two agent buttons discharge a bottle (the aircraft sets DISCH): never pressed in a test pass.</summary>
    [Fact]
    public void Cargo_smoke()
    {
        Assert.Equal(new[] { ("Agent to forward", A350RowAction.Set, OffOn), ("Agent to aft", A350RowAction.Set, OffOn) },
            Rows("Cargo Smoke"));
        AssertLights("Cargo Smoke", new[] { "Agent to forward smoke light", "Agent to forward discharged light",
            "Agent to aft smoke light", "Agent to aft discharged light",
            "Bottle 1 discharged light", "Bottle 2 discharged light", "Bottle 3 discharged light" });
    }

    /// <summary>Live 2026-10-26: off and on.</summary>
    [Fact]
    public void Cabin_smoke()
    {
        AssertRows("Cabin Smoke", new[]
        { ("IFEC", A350RowAction.Set, OffOn) });
        AssertLights("Cabin Smoke", new[] { "IFEC smoke light" });
    }

    /// <summary>Live 2026-10-26: each electric pump off and on, each override on (its ON legend lit) and off.</summary>
    [Fact]
    public void Ground_hydraulics()
    {
        AssertRows("Hydraulics", new[]
        {
            ("Green electric pump", A350RowAction.Set, OffOn), ("Yellow electric pump", A350RowAction.Set, OffOn),
            ("Green electric pump override", A350RowAction.Set, OffOn), ("Yellow electric pump override", A350RowAction.Set, OffOn),
        });
        AssertLights("Hydraulics", new[] { "Green electric pump fault light", "Yellow electric pump fault light",
            "Green electric pump override on light", "Yellow electric pump override on light" });
    }

    /// <summary>Live 2026-10-26: both off and on.</summary>
    [Fact]
    public void Cabin()
    {
        Assert.Equal(new[] { ("Landscape camera", A350RowAction.Set, OffOn), ("Mobile communication", A350RowAction.Set, OffOn) },
            Rows("Cabin"));
        AssertLights("Cabin", new[] { "Lavatory occupied light" });
    }

    /// <summary>Live 2026-10-26: each off and on.</summary>
    [Fact]
    public void Ois()
    {
        AssertRows("OIS", new[]
        {
            ("Cabin data to OIS", A350RowAction.Set, OffOn), ("Gatelink", A350RowAction.Set, OffOn),
            ("OIS data to avionics", A350RowAction.Set, OffOn),
        });
    }

    /// <summary>Live 2026-10-26: both off and on.</summary>
    [Fact]
    public void Surveillance()
    {
        Assert.Equal(new[] { ("Takeoff surveillance", A350RowAction.Set, OffOn), ("ROW/ROP", A350RowAction.Set, OffOn) },
            Rows("Surveillance"));
    }

    /// <summary>Live 2026-10-26: every button on and off; the BAT selector through all five positions (it picks the
    /// battery the BAT voltage display shows).</summary>
    [Fact]
    public void Maintenance()
    {
        AssertRows("Maintenance", new[]
        {
            ("APU economy mode", A350RowAction.Set, OffOn), ("Cockpit door locking system", A350RowAction.Set, OffOn),
            ("COM ground connection", A350RowAction.Set, OffOn),
            ("BAT selector", A350RowAction.Set, "0=EMER 1, 1=EMER 2, 2=Off, 3=BAT 1, 4=BAT 2"),
            ("ELEC remote circuit breaker control", A350RowAction.Set, OffOn),
            ("Refuel", A350RowAction.Set, OffOn), ("Ground HF datalink", A350RowAction.Set, OffOn),
            ("Oxygen reset", A350RowAction.Set, OffOn), ("Service interphone override", A350RowAction.Set, OffOn),
            ("Towing on battery", A350RowAction.Set, OffOn),
        });
        AssertLights("Maintenance", new[] { "Refuel end light", "Oxygen reset fault light", "Oxygen reset on light" });
    }
}
