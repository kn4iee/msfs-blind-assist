using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The pedestal's panels in the A380's order with the A380's words (ECAM keys, levers), the cockpit's own
/// legends elsewhere, and positions named from what the switch does where iniBuilds' tooltip names the click.</summary>
public class A350PedestalPanelTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());
    private const A350RowAction Set = A350RowAction.Set;
    private const A350RowAction Press = A350RowAction.Press;

    private static IEnumerable<(string Name, A350RowAction Action, string Positions)> Rows(string panel) =>
        Placement.RowsByPanel[panel].Select(r => (r.Name, r.Action,
            string.Join(", ", r.Positions.OrderBy(p => p.Key).Select(p =>
                $"{p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}={p.Value}"))));

    private static void AssertRows(string panel, params (string Name, A350RowAction Action, string Positions)[] expected) =>
        Assert.Equal(expected, Rows(panel));

    [Fact]
    public void The_pedestal_panels_follow_the_a380()
    {
        Assert.Equal(new[]
        {
            "Engines", "Thrust Levers", "Captain RMP", "First Officer RMP", "Emergency Descent", "Flaps and Brakes", "Speed Brake",
            "Trim", "ECAM Control Panel", "Captain ECAM Control Panel", "First Officer ECAM Control Panel", "Weather Radar",
            "Captain Weather Radar", "First Officer Weather Radar", "Transponder", "Cockpit", "Captain Display Transfer",
            "First Officer Display Transfer", "RMP 3",
        }, Placement.Structure["Pedestal"]);
    }

    // Review 2026-10-29 (panels I1): the guard's tooltip words name the click ({0 OPEN, 1 CLOSE}, ACTION "OPEN GUARD"), as
    // the ELT guard's and the gravity gear cover's do, so the row read "Open" with the guard shut, and every EMER CANCEL
    // press (whose release writes 0 to the guard: it shuts) was announced "ECAM emergency cancel guard: Open".
    [Fact]
    public void The_emergency_cancel_guard_reads_closed_at_zero()
    {
        var guard = Placement.RowsByPanel.Values.SelectMany(r => r).Single(r => r.Name == "ECAM emergency cancel guard");
        Assert.Equal(new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }, guard.Positions);
    }

    [Fact]
    public void The_kccu_wheels_wait_for_the_kccu_panel()
    {
        Assert.DoesNotContain(Placement.RowsByPanel.Values.SelectMany(r => r), r => r.Control?.Panel is "FMGS_1" or "FMGS_2");
    }

    // The three RMPs (manual pp. 23, 74): every key, page key, microphone key, volume knob and reception (the knob pulled
    // out), in the cockpit file's order, named as the manual names them (line select keys load a standby, activation keys
    // swap it in), each RMP's twin rows by their RMP first, as the EFIS panels' ("Captain LS"): Ctrl+M is one flat list,
    // where three "VHF1 reception" rows could not be told apart. iniBuilds names the VOICE key "VHF1" too: it is Voice.
    [Theory]
    [InlineData("Captain RMP", 1, "Captain")]
    [InlineData("First Officer RMP", 2, "First officer")]
    [InlineData("RMP 3", 3, "RMP 3")]
    public void Each_rmp_is_a_panel_of_its_own_keys(string panel, int n, string side)
    {
        var rows = Placement.RowsByPanel[panel];
        Assert.Equal(65, rows.Count);   // 53 keys and knobs, the 11 reception knobs' push-pull, and STBY RAD NAV
        Assert.All(rows, r => Assert.Matches($"_{n}(_BUTTON)?$", r.Control!.Id));
        Assert.Equal(rows.Count, rows.Select(r => r.Name).Distinct().Count());
        Assert.All(rows, r => Assert.StartsWith(side + " ", r.Name));
        var named = Rows(panel).ToList();
        foreach (var (name, action, positions) in new[]
        {
            ("key 1", Press, ""), ("key 0", Press, ""), ("up arrow", Press, ""), ("down arrow", Press, ""), ("clear", Press, ""),
            ("point", Press, ""), ("load", Press, ""), ("line select key 1", Press, ""), ("line select key 3", Press, ""),
            ("message clear", Press, ""), ("activation key 1", Press, ""), ("activation key 3", Press, ""),
            ("VHF page", Press, ""), ("HF page", Press, ""), ("TEL page", Press, ""), ("SQWK page", Press, ""),
            ("MENU page", Press, ""), ("NAV page", Press, ""), ("STBY RAD NAV", Set, "0=Off, 1=On"), ("VHF1 microphone", Press, ""), ("VHF1 volume", Set, ""),
            ("HF2 microphone", Press, ""), ("TEL2 volume", Set, ""), ("INT microphone", Press, ""), ("PA microphone", Press, ""),
            ("voice microphone", Press, ""), ("CAB volume", Set, ""), ("brightness", Set, ""),
            ("VHF1 reception", Set, "0=Off, 1=On"), ("voice reception", Set, "0=Off, 1=On"),
            ("RAD NAV", Set, "0=ADF 1, 1=ADF 2, 2=LS, 3=VOR 1, 4=VOR 2, 5=MKR"), ("INT/RAD", Set, "0=Radio, 1=Off, 2=Interphone"),
        })
            Assert.Contains((side + " " + name, action, positions), named);
    }

    [Fact]
    public void Engines()
    {
        // iniBuilds' master tooltip words are swapped (its OFF key reads ON): with both engines running the masters read 1.
        AssertRows("Engines",
            ("Engine mode selector", Set, "0=Crank, 1=Norm, 2=Ign start"),
            ("ENG 1 master", Set, "0=Off, 1=On"),
            ("ENG 2 master", Set, "0=Off, 1=On"));
    }

    [Fact]
    public void Thrust_levers()
    {
        const string detents = "-20=Reverse, -6=Reverse idle, 0=Idle, 12=50 percent N1, 25=Climb, 35=Flex/MCT, 45=TOGA";
        AssertRows("Thrust Levers",
            ("All thrust levers", Set, detents),
            ("Thrust lever 1", Set, detents),
            ("Left A/THR disconnect", Press, ""),
            ("Thrust lever 2", Set, detents),
            ("Right A/THR disconnect", Press, ""));
    }

    [Fact]
    public void Flaps_and_brakes()
    {
        // The cover's tooltip words are the click's action; the handle needs the cover and both guards open for DOWN.
        AssertRows("Flaps and Brakes",
            ("Flaps", Set, "0=Up, 1=1, 2=2, 3=3, 4=Full"),
            ("Parking brake", Set, "0=Off, 1=On"),
            ("Brake accumulator reinflate", Press, ""),
            ("Gravity gear extension cover", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension guard 1", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension guard 2", Set, "0=Closed, 1=Open"),
            ("Gravity gear extension", Set, "0=Reset, 1=Off, 2=Down"));
    }

    [Fact]
    public void Ecam_control_panel_takes_the_a380s_ecam_words()
    {
        var names = Rows("ECAM Control Panel").Select(r => r.Name).ToList();
        Assert.Equal(names.Distinct().Count(), names.Count);   // [VAR-1]
        foreach (var name in new[]
        {
            "ECAM ENG", "ECAM APU", "ECAM BLEED", "ECAM COND", "ECAM PRESS", "ECAM DOOR", "ECAM EL/AC", "ECAM EL/DC",
            "ECAM FUEL", "ECAM WHEEL", "ECAM HYD", "ECAM F/CTL", "ECAM all", "ECAM status", "ECAM video",
            "ECAM abnormal proc", "ECAM deferred proc", "ECAM dispatch", "ECAM more", "ECAM T.O config", "ECAM emergency cancel",
            "ECAM emergency cancel guard", "ECAM recall last", "ECAM recall all",
        })
            Assert.Contains(name, names);
    }

    /// <summary>Each pilot's ECP keys (CLEAR, CHECKLIST and the wheel at each end of the panel) on a panel of their own
    /// (owner 2026-10-27), after the shared keys.</summary>
    [Theory]
    [InlineData("Captain ECAM Control Panel", "Captain")]
    [InlineData("First Officer ECAM Control Panel", "First officer")]
    public void Each_pilots_ecam_keys(string panel, string side)
    {
        AssertRows(panel,
            ($"{side} ECAM checklist", Press, ""),
            ($"{side} ECAM wheel increase", A350RowAction.Increase, ""),
            ($"{side} ECAM wheel decrease", A350RowAction.Decrease, ""),
            ($"{side} ECAM clear", Press, ""));
    }

    [Fact]
    public void Weather_radar_and_transponder_split_the_surveillance_panel_as_the_a380()
    {
        Assert.Equal(new[] { "Weather radar system 1", "Weather radar system 2" }, Rows("Weather Radar").Select(r => r.Name));
        foreach (var (panel, side) in new[] { ("Captain Weather Radar", "Captain"), ("First Officer Weather Radar", "First officer") })
        {
            var radar = Rows(panel).Select(r => r.Name).ToList();
            foreach (var knob in new[] { "elevation", "gain", "VD azimuth" })
                Assert.Equal(new[] { "auto", "manual", "increase", "decrease" }.Select(a => $"{side} weather radar {knob} {a}"),
                    radar.Where(n => n.StartsWith($"{side} weather radar {knob} ", StringComparison.Ordinal)));   // push: AUTO MODE, pull: MANUAL
            Assert.Equal(12, radar.Count);
        }
        var xpdr = Rows("Transponder").Select(r => r.Name).ToList();
        Assert.Equal(new[] { "TAWS G/S mode", "TCAS above", "TCAS below", "TCAS TA only", "Transponder system 1", "Transponder system 2" },
            xpdr);   // the cockpit file's order
    }

    [Fact]
    public void Cockpit_door_and_lighting()
    {
        var cockpit = Rows("Cockpit").ToList();
        Assert.Contains(("Cockpit door switch", Set, "0=Lock, 1=Norm, 2=Unlock"), cockpit);   // the door itself is the Cockpit section's
        Assert.Contains(("DU master", Set, "0=Inhibit, 1=On"), cockpit);
        // DU MASTER BRT steps every display through the WASM (INI_CKPT_LT_DU_MASTER_BRT_INC/_DEC, consumed live), but
        // nothing keeps INI_CKPT_LT_DU_MASTER_BRT, which its tooltip reads: no value line that would always read 0.
        var brt = A350ControlMap.Load().Find("AIRLINER_CKPT_LT_DU_MASTER_BRT")!;
        Assert.False(A350Readouts.HasValueLine(brt));
        Assert.DoesNotContain(cockpit, r => r.Name.Contains("OIS on center", StringComparison.Ordinal));
    }

    /// <summary>Each side's OIS ON CTR and DISPLAY CYCLE (iniBuilds' OIS_DISPLAY_TRANSFER group) on a panel of their own
    /// (owner 2026-10-27), with their lights.</summary>
    [Theory]
    [InlineData("Captain Display Transfer", "Captain")]
    [InlineData("First Officer Display Transfer", "First officer")]
    public void Each_sides_display_transfer(string panel, string side)
    {
        AssertRows(panel, ($"{side} OIS on center", Set, "0=Off, 1=On"), ($"{side} display cycle", Set, "0=Off, 1=On"));
        var map = A350ControlMap.Load();
        Assert.Equal(new[] { $"{side} OIS on center INOP light", $"{side} display cycle light" },
            A350PanelLamps.Resolve(map, Placement, new[] { "Pedestal" }).Where(l => l.Panel == panel).Select(l => l.Name));
    }
}

/// <summary>The pedestal's lights, named by what they show.</summary>
public class A350PedestalLightTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly IReadOnlyList<A350Lamp> Lamps =
        A350PanelLamps.Resolve(Map, A350PanelLayout.Place(Map), new[] { "Pedestal" });

    private static string Name(string node) => Lamps.Single(l => l.Key == "A350_LT_" + node).Name;

    [Fact]
    public void An_engine_masters_upper_legend_is_its_fire_light()
    {
        Assert.Equal("ENG 1 master fire light", Name("ENG_MASTER_1_SEQ1_LIGHT"));
        Assert.Equal("ENG 2 master fire light", Name("ENG_MASTER_2_SEQ1_LIGHT"));
        Assert.Equal("ENG 1 master fault light", Name("ENG_MASTER_1_SEQ2_LIGHT"));
    }

    [Fact]
    public void Each_light_reads_what_lights_it()
    {
        Assert.Equal("Captain OIS on center INOP light", Name("OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_LEFT_SEQ1_LIGHT"));
        Assert.Equal("First officer OIS on center INOP light", Name("OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_RIGHT_SEQ1_LIGHT"));
        Assert.Equal("Cockpit door open light", Name("CKPT_DOOR_LT_SEQ1_LIGHT"));       // INI_COCKPIT_DOOR above 0.01
        Assert.Equal("Cockpit door fault light", Name("CKPT_DOOR_LT_SEQ2_LIGHT"));
        Assert.Equal("Emergency descent armed light", Name("EMER_DESCENT_SEQ2_LIGHT"));   // INI_EMER_DES_ARMED_LIGHT
    }
}
