using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using S = MSFSBlindAssist.Aircraft.BBS757.Bbs757SpringControls;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's spring-loaded cockpit switches, held from the panels for one second (plan 12, task 3): the
/// rudder trim, the aileron trim, the alternate stab trim and the manual cabin altitude switch. The values
/// are measured (docs/b757/evidence/flight-12.md) and read from BlueBird's code: nothing in the aircraft
/// returns a trim switch written 0 or 2 to rest (a lost release ran the rudder trim to its stop twice), so
/// the release always runs and is read back, and re-sent while the switch is off rest. The first half is
/// the pure part (the codes, the phrases, the decisions), CI runs under en-US, so the de-DE and sv-SE cases
/// are the ones that catch a culture leak. The second half is the definition's press, over the shared fake sim.
/// </summary>
public class Bbs757SpringControlsTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static Bbs757SpringControl Control(string key) => S.All.Single(c => c.Key == key);

    // ---- The table (measured, flight-12.md) --------------------------------------------------------------

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", "B757_PEDESTAL_RUDDER_TRIM_KNOB", 1, 0, 2, "Pedestal", "Stabilizer trim", "Rudder trim")]
    [InlineData("BBS757_AILERON_TRIM", "BBS_AILERON_TRIM_SWITCH", 1, 0, 2, "Pedestal", "Stabilizer trim", "Aileron trim")]
    [InlineData("BBS757_ALTN_STAB_TRIM", "BBS_ELEV_TRIM_ALTERNATE", 1, 0, 2, "Pedestal", "Stabilizer trim", "Stab trim")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", "B757_OVHD_CABINALT_SWITCH", 4, 0, 7, "Overhead", "Pressurization", "Cabin altitude")]
    public void The_four_switches_are_the_measured_ones(string key, string lvar, double rest, double negative, double positive,
        string section, string panel, string name)
    {
        var c = Control(key);

        Assert.Equal((lvar, rest, negative, positive, section, panel, name),
            (c.LVar, c.Rest, c.Negative, c.Positive, c.Section, c.Panel, c.Name));
        Assert.Equal(key == "BBS757_CABIN_ALT_MANUAL", c.NeedsManualMode);
    }

    [Fact]
    public void Each_switch_has_its_readout()
    {
        Assert.Equal("B757_PED_RUDDER_TRIM_INDICATOR", S.RudderTrim.ReadoutKey);
        Assert.Equal("B757_FC_YOKE", S.AileronTrim.ReadoutKey);
        Assert.Equal("MON_ElevatorTrim", S.AltnStabTrim.ReadoutKey);   // the trim call-out's own row (stock ELEVATOR TRIM POSITION)
        Assert.Equal("BBS_CABIN_VALVE", S.CabinAltManual.ReadoutKey);
        Assert.Equal(4, S.All.Count);
    }

    [Fact]
    public void The_timings_are_the_planned_ones()
    {
        Assert.Equal(1000, S.HoldMs);
        Assert.Equal(500, S.HoldRewriteMs);
        Assert.Equal(500, S.ReadoutDelayMs);
        Assert.Equal(3, S.MaxReleaseWrites);
        Assert.Equal(250, S.ReleaseReadBackMs);
        Assert.Equal(2000, S.ReleaseRetryIntervalMs);   // after a lost release: every 2 s ...
        Assert.Equal(60000, S.ReleaseRetryForMs);       // ... for up to 60 s
    }

    [Fact]
    public void The_buttons_are_named_and_ordered_as_the_panels_list_them()
    {
        // The side first ([VAR-1]: where rows share a phrase, the discriminator comes first).
        Assert.Equal(new[]
        {
            ("BBS757_RUDDER_TRIM_LEFT", "Left Rudder Trim 1 s", "BBS757_RUDDER_TRIM", false),
            ("BBS757_RUDDER_TRIM_RIGHT", "Right Rudder Trim 1 s", "BBS757_RUDDER_TRIM", true),
            ("BBS757_AILERON_TRIM_LEFT", "Left Aileron Trim 1 s", "BBS757_AILERON_TRIM", false),
            ("BBS757_AILERON_TRIM_RIGHT", "Right Aileron Trim 1 s", "BBS757_AILERON_TRIM", true),
            ("BBS757_ALTN_STAB_TRIM_NOSE_DOWN", "Nose Down Stab Trim 1 s", "BBS757_ALTN_STAB_TRIM", false),
            ("BBS757_ALTN_STAB_TRIM_NOSE_UP", "Nose Up Stab Trim 1 s", "BBS757_ALTN_STAB_TRIM", true),
            ("BBS757_CABIN_ALT_MANUAL_CLIMB", "Climb Cabin 1 s", "BBS757_CABIN_ALT_MANUAL", true),
            ("BBS757_CABIN_ALT_MANUAL_DESCEND", "Descend Cabin 1 s", "BBS757_CABIN_ALT_MANUAL", false),
        }, S.Buttons.Select(b => (b.Key, b.Label, b.Control.Key, b.Positive)));

        foreach (var b in S.Buttons) Assert.Same(b, S.ForButtonKey(b.Key));
        Assert.Null(S.ForButtonKey("B757_OVHD_MODE_SELECT_SWITCH"));
        Assert.Null(S.ForButtonKey(S.RudderTrim.Key));   // a control's key is no button
    }

    [Fact]
    public void A_button_key_is_never_a_typed_field()
    {
        // MainForm renders a key containing "_SET" as a text box.
        Assert.All(S.Buttons, b => Assert.DoesNotContain("_SET", b.Key, StringComparison.Ordinal));
    }

    // ---- Codes ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", false, "0 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)", "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)")]
    [InlineData("BBS757_RUDDER_TRIM", true, "2 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)", "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)")]
    [InlineData("BBS757_AILERON_TRIM", false, "0 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)")]
    [InlineData("BBS757_AILERON_TRIM", true, "2 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)")]
    [InlineData("BBS757_ALTN_STAB_TRIM", false, "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", "1 (>L:BBS_ELEV_TRIM_ALTERNATE)")]
    [InlineData("BBS757_ALTN_STAB_TRIM", true, "2 (>L:BBS_ELEV_TRIM_ALTERNATE)", "1 (>L:BBS_ELEV_TRIM_ALTERNATE)")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", false, "0 (>L:B757_OVHD_CABINALT_SWITCH)", "4 (>L:B757_OVHD_CABINALT_SWITCH)")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", true, "7 (>L:B757_OVHD_CABINALT_SWITCH)", "4 (>L:B757_OVHD_CABINALT_SWITCH)")]
    public void Codes_hold_one_side_and_release_to_rest(string key, bool positive, string hold, string release)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal((hold, release), S.Codes(Control(key), positive)));
    }

    // ---- Decisions --------------------------------------------------------------------------------------

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", 1.0, true)]
    [InlineData("BBS757_RUDDER_TRIM", 1.0004, true)]   // the read-back slack
    [InlineData("BBS757_RUDDER_TRIM", 2.0, false)]
    [InlineData("BBS757_RUDDER_TRIM", 0.0, false)]
    [InlineData("BBS757_RUDDER_TRIM", null, false)]    // unread: not known to rest
    [InlineData("BBS757_RUDDER_TRIM", double.NaN, false)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 4.0, true)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 7.0, false)]
    [InlineData("BBS757_CABIN_ALT_MANUAL", 1.0, false)]
    public void At_rest_only_on_the_rest_value(string key, double? reading, bool atRest)
    {
        Assert.Equal(atRest, S.AtRest(Control(key), reading));
    }

    [Theory]
    [InlineData(2.0, null)]
    [InlineData(0.0, "Set the pressurization mode to MAN first")]
    [InlineData(1.0, "Set the pressurization mode to MAN first")]
    [InlineData(null, "Climb Cabin 1 s unavailable")]
    [InlineData(double.NaN, "Climb Cabin 1 s unavailable")]
    public void The_manual_cabin_altitude_works_only_in_man(double? modeSelect, string? refusal)
    {
        Assert.Equal(refusal, S.ManualModeRefusal(modeSelect, "Climb Cabin 1 s"));
        Assert.Equal("B757_OVHD_MODE_SELECT_SWITCH", S.ModeSelectNode);
    }

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM", "Rudder trim switch did not return; the trim may keep running", "Rudder trim switch returned")]
    [InlineData("BBS757_AILERON_TRIM", "Aileron trim switch did not return; the trim may keep running", "Aileron trim switch returned")]
    [InlineData("BBS757_ALTN_STAB_TRIM", "Stab trim switch did not return; the trim may keep running", "Stab trim switch returned")]
    [InlineData("BBS757_CABIN_ALT_MANUAL", "Cabin altitude switch did not return; check the outflow valve", "Cabin altitude switch returned")]
    public void A_switch_that_stays_off_rest_is_said(string key, string phrase, string returned)
    {
        Assert.Equal(phrase, S.DidNotReturnPhrase(Control(key)));
        Assert.Equal(returned, S.ReturnedPhrase(Control(key)));
    }

    // ---- Readout phrases --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.85, "Rudder trim 0.9 units right")]
    [InlineData(-0.85, "Rudder trim 0.9 units left")]
    [InlineData(12.34, "Rudder trim 12.3 units right")]
    [InlineData(-17.0, "Rudder trim 17.0 units left")]
    [InlineData(0.04, "Rudder trim centered")]       // under 0.05 units
    [InlineData(-0.049, "Rudder trim centered")]
    [InlineData(0.0, "Rudder trim centered")]
    [InlineData(0.05, "Rudder trim 0.1 units right")]
    [InlineData(null, "Rudder trim unavailable")]
    [InlineData(double.NaN, "Rudder trim unavailable")]
    public void Rudder_trim_phrase(double? units, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.RudderTrimPhrase(units)));
    }

    [Theory]
    [InlineData(1.5, "Aileron trim centered")]
    [InlineData(1.505, "Aileron trim centered")]    // within 0.01 of the yoke's neutral 1.5
    [InlineData(1.495, "Aileron trim centered")]
    [InlineData(1.52, "Aileron trim right")]
    [InlineData(2.4, "Aileron trim right")]
    [InlineData(1.489, "Aileron trim left")]        // the measured residual after the put-back
    [InlineData(0.865, "Aileron trim left")]
    [InlineData(null, "Aileron trim unavailable")]
    [InlineData(double.PositiveInfinity, "Aileron trim unavailable")]
    public void Aileron_trim_phrase(double? yoke, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.AileronTrimPhrase(yoke)));
    }

    [Theory]
    [InlineData(2.3, "Trim 2.9")]     // 2.95 units at load: the strip's own formula
    [InlineData(5.5, "Trim 5.5")]
    [InlineData(-2.0, "Trim 0.4")]    // the strip's floor
    [InlineData(null, "Stab trim unavailable")]
    [InlineData(double.NaN, "Stab trim unavailable")]
    public void Stab_trim_phrase_is_the_trim_call_outs(double? degrees, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () =>
            {
                Assert.Equal(phrase, S.StabTrimPhrase(degrees));
                if (degrees is double d && double.IsFinite(d))
                    Assert.Equal(StabTrimUnitsCallout.Phrase(Math.Round(Bbs757Readouts.StabTrimUnits(d), 1)), S.StabTrimPhrase(d));
            });
    }

    [Theory]
    [InlineData(0.869, "Outflow valve 87 percent open")]
    [InlineData(1.0, "Outflow valve 100 percent open")]
    [InlineData(0.0, "Outflow valve 0 percent open")]
    [InlineData(0.004, "Outflow valve 0 percent open")]
    [InlineData(0.996, "Outflow valve 100 percent open")]
    [InlineData(1.2, "Outflow valve 100 percent open")]   // never past fully open
    [InlineData(null, "Outflow valve unavailable")]
    [InlineData(double.NaN, "Outflow valve unavailable")]
    public void Outflow_valve_phrase(double? open, string phrase)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(phrase, S.OutflowValvePhrase(open)));
    }

    [Theory]
    [InlineData(2.3, 2.3, false, "Stab trim did not move")]
    [InlineData(2.3, 2.33, false, "Stab trim did not move")]   // 0.024 units: under 0.05
    [InlineData(2.3, 2.33, true, "Stab trim did not move")]
    [InlineData(2.3, 2.6, false, null)]                        // moved: the trim call-out speaks it
    [InlineData(2.6, 2.3, false, null)]
    [InlineData(2.3, 2.6, true, "Trim 3.2")]                   // moved, the call-out silent: the read-back
    [InlineData(null, 2.6, false, "Trim 3.2")]                 // whether it moved cannot be told: the read-back
    [InlineData(2.3, null, false, "Stab trim unavailable")]
    [InlineData(2.3, double.NaN, true, "Stab trim unavailable")]
    public void Stab_trim_speech_after_a_press(double? before, double? after, bool calloutSilent, string? said)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
            InCulture(culture, () => Assert.Equal(said, S.StabTrimSpeech(before, after, calloutSilent)));
    }

    [Fact]
    public void Each_control_speaks_its_own_readout()
    {
        Assert.Equal("Rudder trim 0.9 units right", S.ReadoutPhrase(S.RudderTrim, 0.85));
        Assert.Equal("Aileron trim left", S.ReadoutPhrase(S.AileronTrim, 1.4));
        Assert.Equal("Trim 2.9", S.ReadoutPhrase(S.AltnStabTrim, 2.3));
        Assert.Equal("Outflow valve 87 percent open", S.ReadoutPhrase(S.CabinAltManual, 0.869));
    }

    // ---- The definition: variables and panels -----------------------------------------------------------

    private const string ModeSelectNode = "B757_OVHD_MODE_SELECT_SWITCH";
    private const string StabTrimPanel = "Stabilizer trim";

    private static Bbs757Control ModeSelect() => new()
    {
        Node = ModeSelectNode, Section = "Overhead", Panel = "Pressurization", Label = "Mode Select",
        Kind = Bbs757Kinds.Switch, StateVar = ModeSelectNode,
        Positions = { new(0, "AUTO 1"), new(1, "AUTO 2"), new(2, "MAN") },
    };

    private static Bbs757Control StabCutout() => new()
    {
        Node = "B757_Stab_trim_C_toggle_switch_CLICK", Section = "Pedestal", Panel = StabTrimPanel, Label = "Stab Trim C Cutout",
        Kind = Bbs757Kinds.Toggle, StateVar = "B757_Stab_trim_C_toggle_switch_CLICK",
        Positions = { new(0, "NORM"), new(1, "CUTOUT") },
    };

    private static Bbs757ControlMap MapOf(params Bbs757Control[] controls)
    {
        var map = new Bbs757ControlMap();
        foreach (var control in controls) map.Controls.Add(control);
        return map;
    }

    private static readonly string[] TrimButtons =
    {
        "BBS757_RUDDER_TRIM_LEFT", "BBS757_RUDDER_TRIM_RIGHT", "BBS757_AILERON_TRIM_LEFT", "BBS757_AILERON_TRIM_RIGHT",
        "BBS757_ALTN_STAB_TRIM_NOSE_DOWN", "BBS757_ALTN_STAB_TRIM_NOSE_UP",
    };

    private static readonly string[] CabinButtons = { "BBS757_CABIN_ALT_MANUAL_CLIMB", "BBS757_CABIN_ALT_MANUAL_DESCEND" };

    private static readonly string[] ReadKeys =
    {
        "B757_PEDESTAL_RUDDER_TRIM_KNOB", "BBS_AILERON_TRIM_SWITCH", "BBS_ELEV_TRIM_ALTERNATE", "B757_OVHD_CABINALT_SWITCH",
        "B757_PED_RUDDER_TRIM_INDICATOR", "B757_FC_YOKE", "BBS_CABIN_VALVE",
    };

    [Fact]
    public void The_shipped_stabilizer_trim_panel_starts_with_the_six_trim_buttons()
    {
        var def = new BBS757Definition();
        var panel = def.GetPanelControls()[StabTrimPanel];
        var vars = def.GetVariables();

        Assert.Equal(TrimButtons, panel.Take(6));
        Assert.Equal(new[]
        {
            "Left Rudder Trim 1 s", "Right Rudder Trim 1 s", "Left Aileron Trim 1 s", "Right Aileron Trim 1 s",
            "Nose Down Stab Trim 1 s", "Nose Up Stab Trim 1 s",
        }, panel.Take(6).Select(k => vars[k].DisplayName));
        Assert.Contains("B757_Stab_trim_C_toggle_switch_CLICK", panel.Skip(6));   // the map's controls follow
    }

    [Fact]
    public void The_shipped_pressurization_panel_ends_with_the_cabin_buttons_and_keeps_the_field_first()
    {
        var def = new BBS757Definition();
        var panel = def.GetPanelControls()["Pressurization"];

        Assert.Equal(Bbs757LandingAltitude.SetKey, panel[0]);
        Assert.Equal(CabinButtons, panel.TakeLast(2));
        Assert.Contains(ModeSelectNode, panel);
        Assert.Equal(new[] { "Climb Cabin 1 s", "Descend Cabin 1 s" }, panel.TakeLast(2).Select(k => def.GetVariables()[k].DisplayName));
    }

    [Fact]
    public void An_in_memory_map_places_them_the_same_way()
    {
        var def = new BBS757Definition(MapOf(ModeSelect(), StabCutout())) { NestRunning = () => true };

        Assert.Equal(TrimButtons.Append("B757_Stab_trim_C_toggle_switch_CLICK"), def.GetPanelControls()[StabTrimPanel]);
        Assert.Equal(new[] { Bbs757LandingAltitude.SetKey, ModeSelectNode }.Concat(CabinButtons), def.GetPanelControls()["Pressurization"]);
        // Listed once, first under its section (plan 04's COM radios panel follows it).
        Assert.Equal(StabTrimPanel, def.GetPanelStructure()["Pedestal"][0]);
        Assert.Single(def.GetPanelStructure().SelectMany(p => p.Value), StabTrimPanel);
        Assert.Equal(new[] { "Pressurization" }, def.GetPanelStructure()["Overhead"]);
    }

    [Fact]
    public void A_map_without_the_panels_still_gets_the_buttons()
    {
        var def = new BBS757Definition(new Bbs757ControlMap()) { NestRunning = () => true };

        Assert.Equal(TrimButtons, def.GetPanelControls()[StabTrimPanel]);
        Assert.Equal(new[] { Bbs757LandingAltitude.SetKey }.Concat(CabinButtons), def.GetPanelControls()["Pressurization"]);
        Assert.Contains(StabTrimPanel, def.GetPanelStructure()["Pedestal"]);
        Assert.Equal(new[] { "Pressurization" }, def.GetPanelStructure()["Overhead"]);
    }

    [Fact]
    public void Registers_eight_buttons_that_send_nothing_generic()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (var b in S.Buttons)
        {
            var def = vars[b.Key];
            Assert.Equal(b.Label, def.DisplayName);
            Assert.True(def.RenderAsButton);
            Assert.True(def.SuppressRestingButtonState);
            // An event registration takes no data definition, and a dotted private name changes nothing if ever sent.
            Assert.Equal(SimVarType.Event, def.Type);
            Assert.StartsWith("BBS757.", def.Name, StringComparison.Ordinal);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
            Assert.False(def.IsAnnounced);
        }
        Assert.Equal(8, S.Buttons.Select(b => vars[b.Key].Name).Distinct().Count());
    }

    [Fact]
    public void Registers_the_seven_reads_quiet_and_on_no_panel()
    {
        var def = new BBS757Definition();
        var vars = def.GetVariables();
        var onPanels = def.GetPanelControls().SelectMany(p => p.Value).Concat(def.GetPanelDisplayVariables().SelectMany(p => p.Value))
            .ToHashSet(StringComparer.Ordinal);

        foreach (string key in ReadKeys)
        {
            var read = vars[key];
            Assert.Equal(key, read.Name);
            Assert.Equal(SimVarType.LVar, read.Type);
            Assert.Equal(UpdateFrequency.OnRequest, read.UpdateFrequency);
            Assert.False(read.IsAnnounced);
            Assert.True(read.ExcludeFromMonitorManager);   // no Ctrl+M row that mutes nothing ([VAR-9])
            Assert.False(read.RenderAsButton);
            Assert.DoesNotContain(key, onPanels);
        }
        // The stab trim and Mode Select readouts are rows the profile already registers.
        Assert.Equal("ELEVATOR TRIM POSITION", vars[S.AltnStabTrim.ReadoutKey].Name);
        Assert.Equal(UpdateFrequency.OnRequest, vars[ModeSelectNode].UpdateFrequency);
    }

    [Fact]
    public void A_key_already_present_is_never_shadowed()
    {
        // A map control on one of the read variables keeps its own definition.
        var yoke = new Bbs757Control
        {
            Node = "B757_FC_YOKE", Section = "Pedestal", Panel = "Control stand", Label = "Yoke",
            Kind = Bbs757Kinds.Switch, StateVar = "B757_FC_YOKE", Positions = { new(0, "A"), new(1, "B") },
        };
        var vars = new BBS757Definition(MapOf(yoke)).GetVariables();

        Assert.Equal("Yoke", vars["B757_FC_YOKE"].DisplayName);
        Assert.Equal(new Dictionary<double, string> { [0] = "A", [1] = "B" }, vars["B757_FC_YOKE"].ValueDescriptions);
    }

    [Fact]
    public void The_profile_gains_fifteen_variables_and_stays_under_the_ceiling()
    {
        var vars = new BBS757Definition().GetVariables();
        var mine = S.Buttons.Select(b => b.Key).Concat(ReadKeys).ToList();

        Assert.Equal(15, mine.Distinct(StringComparer.Ordinal).Count());
        Assert.All(mine, k => Assert.True(vars.ContainsKey(k), k));
        Assert.True(vars.Count < 900, $"{vars.Count} variables");
    }

    // ---- The definition: the press ----------------------------------------------------------------------

    /// <summary>A 757 definition over in-memory controls, the shared fake sim behind its writes and a speech capture.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly SpeechCapture Speech = new();
        public readonly SimConnectManager Manager = new(IntPtr.Zero);
        public readonly BBS757Definition Def;

        /// <summary>The Ctrl+M rows the pilot muted.</summary>
        public readonly HashSet<string> Muted = new(StringComparer.Ordinal);

        public Rig()
        {
            Def = new BBS757Definition(MapOf(ModeSelect(), StabCutout()))
            {
                NestRunning = () => true, WriteIo = _ => Sim.Io, IsMonitorMuted = key => Muted.Contains(key),
            };
        }

        /// <summary>
        /// Every switch reads what was last written to it, as BlueBird's do; <paramref name="also"/> runs after each
        /// write too (a trim that follows its switch).
        /// </summary>
        public Rig FollowingWrites(Action<string>? also = null)
        {
            Sim.OnWrite = rpn =>
            {
                var m = Regex.Match(rpn, @"^(-?[0-9.]+) \(>L:([A-Za-z0-9_]+)\)$");
                if (m.Success) Sim.Fresh[m.Groups[2].Value] = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                also?.Invoke(rpn);
            };
            return this;
        }

        public Task Press(string key) => Def.PressSpringAsync(key, Manager, Speech);

        /// <summary>The panel button's click: MainForm's call into the definition.</summary>
        public bool Click(string key) => Def.HandleUIVariableSet(key, 1, Def.GetVariables()[key], Manager, Speech);
    }

    private const string RudderKnob = "B757_PEDESTAL_RUDDER_TRIM_KNOB";
    private const string RudderRight = "2 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string RudderLeft = "0 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string RudderRelease = "1 (>L:B757_PEDESTAL_RUDDER_TRIM_KNOB)";
    private const string CabinClimb = "7 (>L:B757_OVHD_CABINALT_SWITCH)";
    private const string CabinDescend = "0 (>L:B757_OVHD_CABINALT_SWITCH)";
    private const string CabinRelease = "4 (>L:B757_OVHD_CABINALT_SWITCH)";

    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_press_holds_rewrites_releases_then_says_the_readout_once()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[]
        {
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),   // the hold, written again at 500 ms
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250), // released at 1000 ms, read back at rest 250 ms later
            Bbs757FakeIo.DelayEvent(500),                                         // the readout lags the release
        }, rig.Sim.Events);
        // The screen reader said the button; the app says only the read-back (CORE-7).
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);
        Assert.Empty(rig.Sim.Resyncs);
    }

    [Fact]
    public async Task The_hold_is_written_again_at_500_ms()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the first half of the hold
        rig.Sim.Fresh["B757_FC_YOKE"] = 1.44;

        var press = rig.Press("BBS757_AILERON_TRIM_LEFT");
        Assert.Equal(new[] { "0 (>L:BBS_AILERON_TRIM_SWITCH)" }, rig.Sim.Writes);
        Assert.Equal(new[] { 500 }, rig.Sim.Delays);

        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { "0 (>L:BBS_AILERON_TRIM_SWITCH)", "0 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)" },
            rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 500 }, rig.Sim.Delays);   // the hold's halves, the release's read-back, the readout
        Assert.Equal(new[] { "Aileron trim left" }, rig.Speech.All);
    }

    [Fact]
    public async Task Release_always_runs()
    {
        // The pilot switches to another profile during the hold: the lifetime is canceled, the hold is not
        // written again, but the release goes and is read back (nothing else returns the switch to rest),
        // and nothing is said into the other aircraft's session.
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderRight }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(0, rig.Sim.CanceledDelays);   // the hold's waits are never canceled
        Assert.Equal(new[] { 500, 500, 250 }, rig.Sim.Delays);   // the release's read-back still waits; no readout wait after the switch
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task Release_always_runs_when_the_switch_comes_in_the_second_half()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 1;

        var press = rig.Press("BBS757_RUDDER_TRIM_LEFT");
        Assert.Equal(new[] { RudderLeft, RudderLeft }, rig.Sim.Writes);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(1);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_switch_stuck_after_a_switch_away_still_gets_every_release_and_says_nothing()
    {
        var rig = new Rig();   // the knob keeps reading its hold value
        rig.Sim.Hold = ordinal => ordinal == 0;
        rig.Sim.Fresh[RudderKnob] = 2;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        rig.Def.OnSwitchedAway();
        rig.Sim.Release(0);
        await press.WaitAsync(Ceiling);

        // The press's three releases, then the background's: one every 2 s for 60 s (a profile switch does not stop it).
        Assert.Equal(new[] { RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 250, 250 }.Concat(Enumerable.Repeat(2000, 30)), rig.Sim.Delays);
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_press_after_a_switch_away_sends_and_says_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Def.OnSwitchedAway();

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.True(rig.Click("BBS757_CABIN_ALT_MANUAL_CLIMB"));

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Empty(rig.Speech.All);
    }

    private const string RudderDidNotReturn = "Rudder trim switch did not return; the trim may keep running";
    private const string RudderReturned = "Rudder trim switch returned";

    /// <summary>The 0-based ordinal of a rudder press's first background wait: the hold's two, then the three release read-backs.</summary>
    private const int FirstBackgroundWait = 5;

    [Fact]
    public async Task Release_is_resent_until_the_switch_rests()
    {
        // The knob still reads its hold value after every release: three releases in all, then the pilot is told,
        // and the release goes on in the background (its first wait held here).
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease, RudderRelease, RudderRelease }, rig.Sim.Writes);
        // Each release is read back 250 ms after it is written, so a read that has not yet seen it cannot count as lost.
        Assert.Equal(new[]
        {
            Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500), Bbs757FakeIo.WriteEvent(RudderRight), Bbs757FakeIo.DelayEvent(500),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.WriteEvent(RudderRelease), Bbs757FakeIo.DelayEvent(250),
            Bbs757FakeIo.DelayEvent(2000),   // the background release's first wait (held)
            Bbs757FakeIo.DelayEvent(500),    // the readout, said meanwhile
        }, rig.Sim.Events);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);
        Assert.Contains(RudderDidNotReturn, rig.Speech.Interrupts);
        Assert.False(press.IsCompleted);   // still releasing

        // The switch is back by the first background check: nothing more is written, and that is said once.
        rig.Sim.Fresh[RudderKnob] = 1;
        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(5, rig.Sim.Writes.Count);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right", RudderReturned }, rig.Speech.All);
    }

    [Fact]
    public async Task A_lost_release_is_retried_every_2_s_until_the_switch_returns()
    {
        // The switch comes back on the third background release: 3 + 3 release writes, "returned" said once.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 6) rig.Sim.Fresh[RudderKnob] = 1;
        };

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);

        // While it retries, the switch's gate stays taken: neither of its buttons starts a hold.
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(5, rig.Sim.Writes.Count);

        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 3)), rig.Sim.Writes);
        // Each background check waits 2 s, reads the switch and, while it is off rest, writes the release again.
        Assert.Equal(new[] { 500, 500, 250, 250, 250, 2000, 500, 2000, 2000, 2000 }, rig.Sim.Delays);
        Assert.Single(rig.Speech.All, RudderReturned);
        Assert.Equal(RudderReturned, rig.Speech.All[^1]);
        Assert.Equal(0, rig.Sim.CanceledDelays);

        // The gate is free again.
        rig.FollowingWrites();
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.TakeLast(3));
    }

    [Fact]
    public async Task A_switch_that_never_returns_is_released_for_60_s_then_left_quietly()
    {
        var rig = new Rig();   // the knob keeps reading its hold value
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), rig.Sim.Writes);
        var background = rig.Sim.Delays.Where(d => d == 2000).ToList();
        Assert.Equal(30, background.Count);
        Assert.Equal(60000, background.Sum());   // the loop ends at 60 s
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);   // nothing more is said

        // The gate is freed: the next press holds the switch again.
        rig.FollowingWrites();
        int before = rig.Sim.Writes.Count;
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.Skip(before));
    }

    [Fact]
    public async Task A_switch_away_during_the_retries_keeps_releasing_and_says_nothing()
    {
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 5.0;
        rig.Sim.Hold = ordinal => ordinal == FirstBackgroundWait;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 6) rig.Sim.Fresh[RudderKnob] = 1;
        };

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);

        rig.Def.OnSwitchedAway();
        rig.Sim.Release(FirstBackgroundWait);
        await press.WaitAsync(Ceiling);

        // The writes continue (the release is the safety); its return is not said into another aircraft's session.
        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 3)), rig.Sim.Writes);
        Assert.Equal(0, rig.Sim.CanceledDelays);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim 5.0 units right" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_release_read_back_waits_for_the_write_to_land()
    {
        // The knob reads its old value until the release's wait has passed: one release, no false alarm.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;
        rig.Sim.Hold = ordinal => ordinal == 2;   // the release's read-back wait
        rig.Sim.OnWrite = rpn => { if (rpn == RudderRelease) rig.Sim.Fresh[RudderKnob] = 2; };   // not landed yet

        var press = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease }, rig.Sim.Writes);

        rig.Sim.Fresh[RudderKnob] = 1;   // the write lands within the wait
        rig.Sim.Release(2);
        await press.WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Rudder trim 0.9 units right" }, rig.Speech.All);
    }

    [Fact]
    public async Task Release_stops_once_the_switch_reads_rest()
    {
        // At rest after the second release: exactly two release writes, nothing but the readout said.
        var rig = new Rig();
        rig.Sim.Fresh[RudderKnob] = 2;
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = -0.85;
        int releases = 0;
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == RudderRelease && ++releases == 2) rig.Sim.Fresh[RudderKnob] = 1;
        };

        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight, RudderRelease, RudderRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { 500, 500, 250, 250, 500 }, rig.Sim.Delays);
        Assert.Equal(new[] { "Rudder trim 0.9 units left" }, rig.Speech.All);
    }

    [Fact]
    public async Task An_unreadable_switch_keeps_being_released_and_is_said()
    {
        var rig = new Rig();   // the knob is never read
        rig.Sim.Fresh["BBS_CABIN_VALVE"] = 0.5;
        rig.Sim.Fresh[ModeSelectNode] = 2;

        await rig.Press("BBS757_CABIN_ALT_MANUAL_DESCEND").WaitAsync(Ceiling);

        // Three releases, then one every 2 s for 60 s: an unread switch is never known to rest.
        Assert.Equal(new[] { CabinDescend, CabinDescend }.Concat(Enumerable.Repeat(CabinRelease, 3 + 30)), rig.Sim.Writes);
        Assert.Equal(new[] { "Cabin altitude switch did not return; check the outflow valve", "Outflow valve 50 percent open" }, rig.Speech.All);
    }

    [Theory]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", 0.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", 1.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", 0.0, "Set the pressurization mode to MAN first")]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", null, "Climb Cabin 1 s unavailable")]      // Mode Select unread
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", null, "Descend Cabin 1 s unavailable")]
    public async Task Cabin_buttons_refused_outside_man(string key, double? modeSelect, string said)
    {
        var rig = new Rig().FollowingWrites();
        if (modeSelect is double mode) rig.Sim.Fresh[ModeSelectNode] = mode;

        await rig.Press(key).WaitAsync(Ceiling);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(new[] { said }, rig.Speech.All);
    }

    [Theory]
    [InlineData("BBS757_CABIN_ALT_MANUAL_CLIMB", CabinClimb)]
    [InlineData("BBS757_CABIN_ALT_MANUAL_DESCEND", CabinDescend)]
    public async Task Cabin_buttons_hold_in_man(string key, string hold)
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh[ModeSelectNode] = 2;
        rig.Sim.Fresh["BBS_CABIN_VALVE"] = 0.869;

        await rig.Press(key).WaitAsync(Ceiling);

        Assert.Equal(new[] { hold, hold, CabinRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Outflow valve 87 percent open" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_press_that_cannot_land_is_refused_aloud_and_sends_nothing()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.CanWrite = false;
        rig.Sim.Fresh[ModeSelectNode] = 2;

        foreach (var b in S.Buttons) await rig.Press(b.Key).WaitAsync(Ceiling);

        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
        Assert.Equal(S.Buttons.Select(b => $"{b.Label} unavailable"), rig.Speech.All);
    }

    [Fact]
    public async Task A_press_while_that_control_holds_is_ignored()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Hold = ordinal => ordinal == 0;   // the rudder's first wait
        rig.Sim.Fresh["B757_PED_RUDDER_TRIM_INDICATOR"] = 0.85;
        rig.Sim.Fresh["B757_FC_YOKE"] = 1.5;

        var first = rig.Press("BBS757_RUDDER_TRIM_RIGHT");
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);    // the same switch: ignored, silently
        await rig.Press("BBS757_RUDDER_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderRight }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);

        // Another switch holds on its own.
        await rig.Press("BBS757_AILERON_TRIM_RIGHT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderRight, "2 (>L:BBS_AILERON_TRIM_SWITCH)", "2 (>L:BBS_AILERON_TRIM_SWITCH)", "1 (>L:BBS_AILERON_TRIM_SWITCH)" },
            rig.Sim.Writes);

        rig.Sim.Release(0);
        await first.WaitAsync(Ceiling);
        Assert.Equal(new[] { "Aileron trim centered", "Rudder trim 0.9 units right" }, rig.Speech.All);

        // Once released, the rudder holds again.
        await rig.Press("BBS757_RUDDER_TRIM_LEFT").WaitAsync(Ceiling);
        Assert.Equal(new[] { RudderLeft, RudderLeft, RudderRelease }, rig.Sim.Writes.TakeLast(3));
    }

    [Theory]
    [InlineData("BBS757_RUDDER_TRIM_LEFT", "B757_PED_RUDDER_TRIM_INDICATOR", -3.4, "Rudder trim 3.4 units left")]
    [InlineData("BBS757_RUDDER_TRIM_RIGHT", "B757_PED_RUDDER_TRIM_INDICATOR", 0.02, "Rudder trim centered")]
    [InlineData("BBS757_AILERON_TRIM_LEFT", "B757_FC_YOKE", 1.44, "Aileron trim left")]
    [InlineData("BBS757_AILERON_TRIM_RIGHT", "B757_FC_YOKE", 1.56, "Aileron trim right")]
    [InlineData("BBS757_ALTN_STAB_TRIM_NOSE_UP", "NOTHING_READ", 0.0, "Stab trim unavailable")]   // the trim unread
    public async Task Each_button_says_its_readout(string key, string readoutKey, double value, string said)
    {
        foreach (string culture in new[] { "en-US", "de-DE" })
        {
            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var rig = new Rig().FollowingWrites();
                rig.Sim.Fresh[readoutKey] = value;

                await rig.Press(key).WaitAsync(Ceiling);

                Assert.Equal(new[] { said }, rig.Speech.All);
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }
    }

    // ---- The stab trim: the trim call-out speaks its move (CORE-7) -----------------------------------------

    private const string StabNoseUp = "2 (>L:BBS_ELEV_TRIM_ALTERNATE)";
    private const string StabRelease = "1 (>L:BBS_ELEV_TRIM_ALTERNATE)";

    /// <summary>A rig whose stabilizer goes from 2.3 to <paramref name="after"/> degrees while the nose-up switch holds.</summary>
    private static Rig StabRig(double after)
    {
        var rig = new Rig();
        rig.Sim.Fresh["MON_ElevatorTrim"] = 2.3;
        rig.FollowingWrites(rpn => { if (rpn == StabNoseUp) rig.Sim.Fresh["MON_ElevatorTrim"] = after; });
        return rig;
    }

    [Fact]
    public async Task A_stab_trim_press_that_did_not_move_it_says_so()
    {
        // No hydraulic pressure, or a cutout in CUTOUT: the switch holds and the trim stays where it was.
        var rig = StabRig(after: 2.3);

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { StabNoseUp, StabNoseUp, StabRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Stab trim did not move" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_is_left_to_the_trim_call_out()
    {
        // The trim call-out ("Trim 3.2") speaks while the stabilizer moves: the press says nothing more.
        var rig = StabRig(after: 2.6);

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { StabNoseUp, StabNoseUp, StabRelease }, rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_says_the_trim_when_its_call_out_is_muted()
    {
        var rig = StabRig(after: 2.6);
        rig.Muted.Add("MON_ElevatorTrim");   // the trim call-out's Ctrl+M row

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { "Trim 3.2" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_that_moved_it_says_the_trim_when_trim_announcements_are_off()
    {
        var rig = StabRig(after: 2.6);
        Assert.True(rig.Def.HandleHotkeyAction(MSFSBlindAssist.Hotkeys.HotkeyAction.ToggleTrimAnnouncements, rig.Manager, rig.Speech, null!, null!));

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_UP").WaitAsync(Ceiling);

        Assert.Equal(new[] { "Trim announcements off", "Trim 3.2" }, rig.Speech.All);
    }

    [Fact]
    public async Task A_stab_trim_press_reads_the_trim_before_it_holds()
    {
        // The trim before is read fresh, not from a stale cache: a cached value that differs changes nothing.
        var rig = StabRig(after: 2.3);
        rig.Sim.Cache["MON_ElevatorTrim"] = 1.0;

        await rig.Press("BBS757_ALTN_STAB_TRIM_NOSE_DOWN").WaitAsync(Ceiling);

        Assert.Equal(new[] { "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", "0 (>L:BBS_ELEV_TRIM_ALTERNATE)", StabRelease }, rig.Sim.Writes);
        Assert.Equal(new[] { "Stab trim did not move" }, rig.Speech.All);
    }

    [Fact]
    public void Every_button_click_is_claimed_and_runs_the_hold()
    {
        var rig = new Rig().FollowingWrites();
        rig.Sim.Fresh[ModeSelectNode] = 2;

        foreach (var b in S.Buttons)
        {
            int before = rig.Sim.Writes.Count;
            Assert.True(rig.Click(b.Key));   // the definition claims it: the generic path sends nothing
            var (hold, release) = S.Codes(b.Control, b.Positive);
            Assert.Equal(new[] { hold, hold, release }, rig.Sim.Writes.Skip(before));
        }
    }

    [Fact]
    public void Other_keys_are_not_claimed_by_the_spring_buttons()
    {
        var rig = new Rig();

        Assert.False(rig.Def.HandleUIVariableSet("SOME_OTHER_BUTTON", 1, new SimVarDefinition(), rig.Manager, rig.Speech));
        // The landing altitude field is still claimed and sends nothing.
        Assert.True(rig.Click(Bbs757LandingAltitude.SetKey));
        Assert.Empty(rig.Sim.Writes);
        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public async Task A_read_that_throws_never_escapes_and_the_release_still_goes()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_RUDDER_TRIM_RIGHT", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Equal(new[] { RudderRight, RudderRight }.Concat(Enumerable.Repeat(RudderRelease, 3 + 30)), sim.Writes);
        Assert.Equal(new[] { RudderDidNotReturn, "Rudder trim unavailable" }, speech.All);
    }

    [Fact]
    public async Task A_mode_select_read_that_throws_refuses_the_cabin_button()
    {
        var sim = new Bbs757FakeIo();
        var io = sim.Io;
        var failing = new Bbs757SimIo
        {
            CanWrite = io.CanWrite, Cached = io.Cached, Write = io.Write, RequestRead = io.RequestRead, Delay = io.Delay,
            ReadFresh = (_, _) => throw new InvalidOperationException("no link"),
        };
        var def = new BBS757Definition(MapOf(ModeSelect())) { NestRunning = () => true, WriteIo = _ => failing };
        var speech = new SpeechCapture();

        await def.PressSpringAsync("BBS757_CABIN_ALT_MANUAL_CLIMB", new SimConnectManager(IntPtr.Zero), speech).WaitAsync(Ceiling);

        Assert.Empty(sim.Writes);
        Assert.Equal(new[] { "Climb Cabin 1 s unavailable" }, speech.All);
    }
}
