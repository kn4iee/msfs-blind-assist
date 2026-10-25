using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using R = MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's full-flight readiness work (plan 01, task 10b) at the definition: what is registered and
/// where it shows, what is spoken on change and what is not, the Ctrl+M rows, the readouts and sets
/// with no simulator (each says it is unavailable, never nothing), and the autopilot window's table.
/// Whether the aircraft moves is checked in the sim (docs/b757/plans/01-prototype-insim-tests.md).
/// </summary>
public class Bbs757FlightReadinessTests
{
    private static readonly BBS757Definition Def = new();

    /// <summary>A definition whose BBS Nest check is settled and whose clock is a field.</summary>
    private sealed class Rig
    {
        public long Now { get; set; }
        public readonly HashSet<string> Muted = new(StringComparer.Ordinal);
        public readonly BBS757Definition Def;
        public readonly SpeechCapture Speech = new();

        public Rig()
        {
            Def = new BBS757Definition { NestRunning = () => true, NowMs = () => Now, IsMonitorMuted = Muted.Contains };
        }

        public void Deliver(string key, double value) => Def.ProcessSimVarUpdate(key, value, Speech);
    }

    // ---- Registration -------------------------------------------------------------------------

    [Theory]
    [InlineData(R.FlapsHandleKey, "FLAPS HANDLE INDEX", "Flaps")]
    [InlineData(R.SpeedbrakeKey, "B757_FC_SPOILER_LEVER", "Speedbrake")]
    [InlineData(R.ParkingBrakeKey, "B757_FC_PARK_BRAKE", "Parking Brake")]
    [InlineData(R.GearNoseKey, "GEAR CENTER POSITION", "Gear")]
    public void The_state_rows_stream_and_keep_a_monitor_row(string key, string name, string label)
    {
        var def = Def.GetVariables()[key];

        Assert.Equal(name, def.Name);
        Assert.Equal(label, def.DisplayName);
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced);
        Assert.False(def.ExcludeFromMonitorManager);
        Assert.True(def.RenderAsReadOnlyStatus);
    }

    [Fact]
    public void The_parking_brake_is_blueBirds_own_variable_never_the_stock_one()
    {
        // Measured: the stock BRAKE PARKING POSITION read 0 with BlueBird's brake set.
        Assert.DoesNotContain(Def.GetVariables().Values, v => v.Name == "BRAKE PARKING POSITION");
        Assert.Equal(SimVarType.LVar, Def.GetVariables()[R.ParkingBrakeKey].Type);
        Assert.Equal("Set", Def.GetVariables()[R.ParkingBrakeKey].ValueDescriptions[1]);
    }

    [Fact]
    public void The_flap_handle_row_names_the_detents()
    {
        Assert.Equal(new[] { "UP", "1", "5", "15", "20", "25", "30" },
            Def.GetVariables()[R.FlapsHandleKey].ValueDescriptions.OrderBy(d => d.Key).Select(d => d.Value));
    }

    [Fact]
    public void The_main_gear_legs_speak_under_the_one_gear_row()
    {
        var keys = MonitorRowBuilder.Build(Def.GetVariables()).Select(r => r.Key).ToList();

        Assert.Contains(R.GearNoseKey, keys);
        Assert.DoesNotContain(R.GearLeftKey, keys);
        Assert.DoesNotContain(R.GearRightKey, keys);
        // They speak the gear row's call-out, so MainForm leaves them out of its mute wrap ([VAR-8]).
        Assert.True(Def.IsMuteWrapExempt(R.GearNoseKey));
        Assert.True(Def.IsMuteWrapExempt(R.GearLeftKey));
        Assert.True(Def.IsMuteWrapExempt(R.GearRightKey));
        Assert.False(Def.IsMuteWrapExempt(R.FlapsHandleKey));
    }

    [Fact]
    public void Ctrl_M_lists_every_mcp_light_and_the_flight_controls()
    {
        var rows = MonitorRowBuilder.Build(Def.GetVariables()).ToList();
        var keys = rows.Select(r => r.Key).ToList();

        Assert.All(Bbs757McpLights.All, l => Assert.Contains(l.Key, keys));
        Assert.Contains(R.FlapsHandleKey, keys);
        Assert.Contains(R.SpeedbrakeKey, keys);
        Assert.Contains(R.ParkingBrakeKey, keys);
        // Silent rows earn no checkbox that mutes nothing ([VAR-9]).
        Assert.DoesNotContain(R.FlapsAngleKey, keys);
        Assert.All(R.EngineRows, e => Assert.DoesNotContain(e.Key, keys));
        Assert.DoesNotContain(Bbs757Squawk.CodeKey, keys);
        Assert.Contains(rows, r => r.Key == Bbs757McpLights.ForNode("B757_MCP_CMD_3_BUTTON")!.Key);
    }

    [Fact]
    public void The_mcp_lights_are_their_own_lvars_under_keys_that_are_not_the_buttons()
    {
        var vars = Def.GetVariables();
        Assert.All(Bbs757McpLights.All, light =>
        {
            var def = vars[light.Key];
            Assert.Equal(light.LightVar, def.Name);
            Assert.Equal(SimVarType.LVar, def.Type);
            Assert.Equal(light.Label, def.DisplayName);
            // The button keeps its own key and its press variable.
            Assert.Equal(light.LightVar + "_PRESS", vars[light.Node].Name);
        });
    }

    [Fact]
    public void Engine_rows_are_read_only_and_silent()
    {
        var vars = Def.GetVariables();
        Assert.All(R.EngineRows, row =>
        {
            var def = vars[row.Key];
            Assert.Equal(row.SimVar, def.Name);
            Assert.Equal(row.Units, def.Units);
            Assert.False(def.IsAnnounced);
            Assert.True(def.ExcludeFromMonitorManager);
            Assert.True(def.RenderAsReadOnlyStatus);
        });
    }

    [Fact]
    public void The_readouts_reads_are_on_request()
    {
        var vars = Def.GetVariables();
        foreach (string key in Bbs757Altimeters.KohlsmanKeys.Append(Bbs757Altimeters.DecisionHeightKey)
                     .Append(R.TotalWeightKey).Append(R.GearHandleKey))
        {
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.True(vars[key].ExcludeFromMonitorManager);
        }
        Assert.Equal("KOHLSMAN SETTING MB:3", vars[Bbs757Altimeters.Kohlsman3Key].Name);
        Assert.Equal("DECISION HEIGHT", vars[Bbs757Altimeters.DecisionHeightKey].Name);
        Assert.Equal("TOTAL WEIGHT", vars[R.TotalWeightKey].Name);
    }

    [Fact]
    public void The_squawk_field_is_a_typed_box_whose_empty_text_is_not_0000()
    {
        var field = Def.GetVariables()[Bbs757Squawk.SetKey];

        Assert.Contains("_SET", Bbs757Squawk.SetKey);
        Assert.Equal(SimVarType.Event, field.Type);   // no data definition
        Assert.True(field.UnparseableTextAsNaN);
        Assert.DoesNotMatch("^Set ", field.DisplayName);   // MainForm adds its own words
        Assert.Equal("TRANSPONDER CODE:1", Def.GetVariables()[Bbs757Squawk.CodeKey].Name);
        Assert.Equal("BCO16", Def.GetVariables()[Bbs757Squawk.CodeKey].Units);
    }

    // ---- Panels -------------------------------------------------------------------------------

    [Fact]
    public void The_engines_panel_shows_both_engines()
    {
        Assert.Equal(R.EngineRows.Select(r => r.Key), Def.GetPanelDisplayVariables()[BBS757Definition.EnginesPanel]);
    }

    [Fact]
    public void The_mcp_panel_shows_its_lights()
    {
        Assert.Equal(Bbs757McpLights.All.Select(l => l.Key), Def.GetPanelDisplayVariables()[BBS757Definition.McpPanel]);
    }

    [Fact]
    public void The_transponder_panel_has_the_squawk_field_and_shows_the_code()
    {
        Assert.Equal(Bbs757Squawk.SetKey, Def.GetPanelControls()[BBS757Definition.TransponderPanel].Last());
        Assert.Equal(new[] { Bbs757Squawk.CodeKey }, Def.GetPanelDisplayVariables()[BBS757Definition.TransponderPanel]);
    }

    // ---- Spoken on change -----------------------------------------------------------------------

    [Fact]
    public void Flaps_speak_a_new_detent_but_not_the_first_reading()
    {
        var rig = new Rig();

        rig.Deliver(R.FlapsHandleKey, 0);
        rig.Deliver(R.FlapsHandleKey, 2);
        rig.Deliver(R.FlapsHandleKey, 2);
        rig.Deliver(R.FlapsHandleKey, 3);

        Assert.Equal(new[] { "Flaps 5", "Flaps 15" }, rig.Speech.All);
    }

    [Fact]
    public void The_gear_speaks_in_transit_then_down_once_all_three_legs_are_down()
    {
        var rig = new Rig();
        foreach (string leg in new[] { R.GearNoseKey, R.GearLeftKey, R.GearRightKey }) rig.Deliver(leg, 0);

        rig.Deliver(R.GearNoseKey, 0.3);
        rig.Deliver(R.GearLeftKey, 0.4);
        rig.Deliver(R.GearNoseKey, 1);
        rig.Deliver(R.GearLeftKey, 1);
        rig.Deliver(R.GearRightKey, 1);

        Assert.Equal(new[] { "Gear in transit", "Gear down" }, rig.Speech.All);
    }

    [Fact]
    public void A_muted_gear_row_silences_all_three_legs()
    {
        var rig = new Rig();
        rig.Muted.Add(R.GearNoseKey);
        foreach (string leg in new[] { R.GearNoseKey, R.GearLeftKey, R.GearRightKey }) rig.Deliver(leg, 1);

        rig.Deliver(R.GearRightKey, 0.5);
        rig.Deliver(R.GearLeftKey, 0.5);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void Speedbrake_and_parking_brake_speak_their_changes()
    {
        var rig = new Rig();
        rig.Deliver(R.SpeedbrakeKey, 0);
        rig.Deliver(R.ParkingBrakeKey, 1);

        rig.Deliver(R.SpeedbrakeKey, 10);
        rig.Deliver(R.ParkingBrakeKey, 0);
        rig.Deliver(R.SpeedbrakeKey, 100);

        Assert.Equal(new[] { "Speedbrake armed", "Parking brake released", "Speedbrake up" }, rig.Speech.All);
    }

    [Fact]
    public void An_mcp_light_speaks_on_and_off()
    {
        var rig = new Rig();
        var cmdR = Bbs757McpLights.ForNode("B757_MCP_CMD_3_BUTTON")!;
        rig.Deliver(cmdR.Key, 0);

        rig.Deliver(cmdR.Key, 1);
        rig.Deliver(cmdR.Key, 0);

        Assert.Equal(new[] { "CMD R on", "CMD R off" }, rig.Speech.All);
    }

    [Fact]
    public void A_ctrl_m_muted_light_is_silenced_by_mainforms_wrap()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new GatedSpeechCapture();
        var light = Bbs757McpLights.ForNode("B757_MCP_LNAV_BUTTON")!;
        var settings = new UserSettings { BBS757DisabledMonitorVariables = new() { light.Key } };
        settings.RebuildDisabledMonitorVariableCaches();

        MuteWrap.Deliver(def, speech, settings, light.Key, 0);
        MuteWrap.Deliver(def, speech, settings, light.Key, 1);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 0);
        MuteWrap.Deliver(def, speech, settings, R.FlapsHandleKey, 1);

        Assert.Equal(new[] { "Flaps 1" }, speech.All);
    }

    [Fact]
    public void A_flight_load_makes_the_next_reading_a_baseline_again()
    {
        var rig = new Rig();
        rig.Deliver(R.FlapsHandleKey, 0);

        rig.Def.OnSimContextReset();
        rig.Deliver(R.FlapsHandleKey, 6);
        rig.Def.ResetAnnouncementBaselines();
        rig.Deliver(R.FlapsHandleKey, 0);

        Assert.Empty(rig.Speech.All);
    }

    [Fact]
    public void The_silent_rows_are_left_to_the_generic_path()
    {
        var rig = new Rig();

        Assert.False(rig.Def.ProcessSimVarUpdate(R.FlapsAngleKey, 5, rig.Speech));
        Assert.False(rig.Def.ProcessSimVarUpdate(R.EngineRows[0].Key, 20, rig.Speech));
        Assert.False(rig.Def.ProcessSimVarUpdate(Bbs757Squawk.CodeKey, 0x4743, rig.Speech));
        Assert.True(rig.Def.ProcessSimVarUpdate(R.FlapsHandleKey, 0, rig.Speech));
        Assert.Empty(rig.Speech.All);
    }

    // ---- What a press moves -----------------------------------------------------------------------

    [Theory]
    [InlineData("B757_MCP_CMD_3_BUTTON", "BBS757_MCP_LIGHT_CMD_3")]
    [InlineData("B757_MCP_HOLD_1_BUTTON", "BBS757_MCP_LIGHT_HOLD_1")]
    [InlineData("BBS757_SPOILERARMED_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_SPOILERUP_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_SPOILERDOWN_CLICK", R.SpeedbrakeKey)]
    [InlineData("BBS757_PEDESTAL_Park_Brake_Lever", R.ParkingBrakeKey)]
    [InlineData("ENGINE_Switch_ATDisc_1", null)]
    [InlineData("B757_OVHD_BATTERY_BUTTON", null)]
    public void A_press_reads_back_the_row_it_moves(string node, string? statusKey)
    {
        Assert.Equal(statusKey, BBS757Definition.LinkedStatusKey(node));
    }

    [Theory]
    [InlineData("BBS757_MCP_LIGHT_CMD_3", 1.0, "CMD R on")]
    [InlineData("BBS757_MCP_LIGHT_HOLD_2", 0.0, "ALT HOLD off")]
    [InlineData(R.SpeedbrakeKey, 10.0, "Speedbrake armed")]
    [InlineData(R.ParkingBrakeKey, 1.0, "Parking brake set")]
    public void The_read_back_says_the_state_not_pressed(string statusKey, double value, string phrase)
    {
        Assert.Equal(phrase, BBS757Definition.LinkedPhrase(statusKey, value));
    }

    // ---- Status display -----------------------------------------------------------------------

    [Fact]
    public void The_status_display_shows_words_not_raw_values()
    {
        var rig = new Rig();
        rig.Deliver(R.GearLeftKey, 1);
        rig.Deliver(R.GearRightKey, 1);

        Assert.True(rig.Def.TryGetDisplayOverride(R.GearNoseKey, 1, out string gear));
        Assert.Equal("Down", gear);
        Assert.True(rig.Def.TryGetDisplayOverride(R.SpeedbrakeKey, 10, out string speedbrake));
        Assert.Equal("Armed", speedbrake);
        Assert.True(rig.Def.TryGetDisplayOverride(Bbs757Squawk.CodeKey, 0x4743, out string squawk));
        Assert.Equal("4743", squawk);
        Assert.True(rig.Def.TryGetDisplayOverride(Bbs757McpLights.All[0].Key, 1, out string light));
        Assert.Equal("On", light);
        Assert.True(rig.Def.TryGetDisplayOverride(R.EngineRows[0].Key, 23.44, out string n1));
        Assert.Equal("23.4 percent", n1);
        Assert.True(rig.Def.TryGetDisplayOverride(R.FlapsAngleKey, 4.96, out string angle));
        Assert.Equal("5.0 degrees", angle);
        Assert.True(rig.Def.TryGetDisplayOverride(R.FlapsHandleKey, 3, out string flaps));
        Assert.Equal("15", flaps);
        Assert.False(rig.Def.TryGetDisplayOverride("B757_OVHD_BATTERY_BUTTON", 1, out _));
    }

    // ---- No simulator: every readout and set says so ---------------------------------------------

    [Theory]
    [InlineData(HotkeyAction.ReadFlaps, "Flaps unavailable")]
    [InlineData(HotkeyAction.ReadGear, "Gear unavailable")]
    [InlineData(HotkeyAction.ReadAltimeter, "Altimeters unavailable. DH unavailable")]
    [InlineData(HotkeyAction.ReadFuelQuantity, "Fuel unavailable")]
    [InlineData(HotkeyAction.ReadFuelInfo, "Fuel unavailable")]
    [InlineData(HotkeyAction.ReadWaypointInfo, "Gross weight unavailable")]
    [InlineData(HotkeyAction.ReadGrossWeightKg, "Gross weight unavailable")]
    public async Task A_readout_with_nothing_read_says_it_is_unavailable(HotkeyAction action, string said)
    {
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Reads(new Dictionary<string, double>()) };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(action, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadFlaps, "Flaps UP")]
    [InlineData(HotkeyAction.ReadAltimeter, "Altimeter: 1020, 30.12. DH 200")]
    [InlineData(HotkeyAction.ReadFuelQuantity, "Fuel 25480 pounds: left 12740, right 12740, center 0")]
    [InlineData(HotkeyAction.ReadFuelInfo, "Fuel 11560 kilograms: left 5780, right 5780, center 0")]
    [InlineData(HotkeyAction.ReadWaypointInfo, "Gross weight 208239 pounds")]
    [InlineData(HotkeyAction.ReadGrossWeightKg, "Gross weight 94456 kilograms")]
    [InlineData(HotkeyAction.ReadGear, "Gear down, lever up")]
    public async Task A_readout_speaks_what_it_reads_at_once(HotkeyAction action, string said)
    {
        var values = new Dictionary<string, double>
        {
            [R.FlapsHandleKey] = 0, [R.FlapsAngleKey] = 0,
            [Bbs757Altimeters.Kohlsman1Key] = 1020, [Bbs757Altimeters.Kohlsman2Key] = 1020,
            [Bbs757Altimeters.Kohlsman3Key] = 1020, [Bbs757Altimeters.DecisionHeightKey] = 200,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.LeftFuelKey] = 1902,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.RightFuelKey] = 1902,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.CenterFuelKey] = 0,
            [MSFSBlindAssist.SimConnect.BBS757.Bbs757Loadsheet.FuelDensityKey] = 6.7,
            [R.TotalWeightKey] = 208239.4,
            [R.GearHandleKey] = 0, [R.GearNoseKey] = 1, [R.GearLeftKey] = 1, [R.GearRightKey] = 1,
        };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Reads(values) };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(action, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Fact]
    public async Task A_readout_that_fails_still_says_something()
    {
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => new McpIo
            {
                Read = (_, _) => throw new InvalidOperationException("no link"),
                Write = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        var speech = new SpeechCapture();

        await def.ReadoutAsync(HotkeyAction.ReadAltimeter, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { "Altimeters unavailable. DH unavailable" }, speech.Interrupts);
    }

    private static McpIo Reads(IReadOnlyDictionary<string, double> values) => new()
    {
        Read = (key, _) => Task.FromResult(values.TryGetValue(key, out double v) ? v : (double?)null),
        Write = _ => throw new InvalidOperationException("a readout writes nothing"),
        Delay = (_, _) => Task.CompletedTask,
    };

    [Fact]
    public async Task An_altimeter_or_minimums_set_that_cannot_land_is_refused_aloud()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();
        var shown = new List<string>();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetAltimetersAsync(16320, sim, speech, shown.Add);
        await def.SetMinimumsAsync(200, sim, speech, shown.Add);

        Assert.Equal(new[] { "Altimeter unavailable", "DH unavailable" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, shown);   // the window's status box says the same
    }

    [Theory]
    [InlineData(double.NaN, Bbs757Squawk.EmptyMessage)]
    [InlineData(4783.0, Bbs757Squawk.InvalidMessage)]
    [InlineData(4743.0, "Squawk unavailable")]   // valid, but nothing can land
    public async Task A_squawk_that_cannot_be_set_says_why(double typed, string said)
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        await def.SetSquawkAsync(typed, new SimConnectManager(IntPtr.Zero), speech);

        Assert.Equal(new[] { said }, speech.Interrupts);
    }

    [Fact]
    public void The_squawk_field_is_claimed_by_the_definition()
    {
        var def = new BBS757Definition { NestRunning = () => true };

        Assert.True(def.HandleUIVariableSet(Bbs757Squawk.SetKey, double.NaN, def.GetVariables()[Bbs757Squawk.SetKey],
            new SimConnectManager(IntPtr.Zero), new SpeechCapture()));
    }

    // ---- The autopilot window's table ------------------------------------------------------------

    [Fact]
    public void The_autopilot_window_lists_the_cmd_buttons_switches_and_modes_in_order()
    {
        var map = Bbs757ControlMap.Load();
        var labels = Bbs757AutopilotLayout.Nodes.Select(n => map.Controls.Single(c => c.Node == n).Label);

        Assert.Equal(new[]
        {
            "CMD L", "CMD C", "CMD R", "A/T Arm", "Captain F/D", "F/O F/D",
            "THR", "SPD", "LNAV", "VNAV", "FLCH", "HDG HOLD", "ALT HOLD", "V/S", "LOC", "APP",
        }, labels);
    }

    [Fact]
    public void Every_autopilot_button_is_a_lit_button_or_a_two_position_switch()
    {
        var map = Bbs757ControlMap.Load();
        foreach (string node in Bbs757AutopilotLayout.Nodes)
        {
            var control = map.Controls.Single(c => c.Node == node);
            if (Bbs757AutopilotLayout.SwitchNodes.Contains(node))
            {
                Assert.Equal(Bbs757Kinds.Toggle, control.Kind);
                Assert.Equal(2, control.Positions.Count);
                Assert.Null(Bbs757McpLights.ForNode(node));
            }
            else
            {
                Assert.Equal(Bbs757Kinds.Button, control.Kind);
                Assert.NotNull(Bbs757McpLights.ForNode(node));
            }
        }
    }

    [Fact]
    public void A_switch_shows_its_position_and_flips_to_the_other()
    {
        var atArm = Bbs757ControlMap.Load().Controls.Single(c => c.Node == Bbs757AutopilotLayout.AutothrottleArmNode);

        // L:AT_ARM_SWITCH: 0 On, 10 Off (the map's positions).
        Assert.Equal("On", Bbs757AutopilotLayout.SwitchState(atArm, 0));
        Assert.Equal("Off", Bbs757AutopilotLayout.SwitchState(atArm, 10));
        Assert.Equal("unavailable", Bbs757AutopilotLayout.SwitchState(atArm, null));
        Assert.Equal("unavailable", Bbs757AutopilotLayout.SwitchState(atArm, 5));
        Assert.Equal(10, Bbs757AutopilotLayout.FlipTarget(atArm, 0));
        Assert.Equal(0, Bbs757AutopilotLayout.FlipTarget(atArm, 10));
        Assert.Null(Bbs757AutopilotLayout.FlipTarget(atArm, 5));      // a toggle's press flips: never on an unknown reading
        Assert.Null(Bbs757AutopilotLayout.FlipTarget(atArm, null));
    }
}
