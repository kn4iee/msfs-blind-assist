using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>What the A350 says on its own, and the Ctrl+M rows that mute it: the A380's convention (every switch combo
/// spoken when something else moves it, one row each) and lights spoken as the cockpit lights them.</summary>
public class A350AnnouncementTests
{
    private static readonly IniBuildsA350Definition Def = new();

    private static IReadOnlySet<string> MonitorKeys() =>
        MonitorRowBuilder.Build(Def.GetVariables()).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void The_a350_is_muted_by_its_own_list()
    {
        var s = new UserSettings { A350DisabledMonitorVariables = new() { "a350" } };
        s.RebuildDisabledMonitorVariableCaches();
        var set = DefAnnounceMuteSets.For(IniBuildsA350Definition.Code, s);
        Assert.NotNull(set);
        Assert.Equal(new[] { "a350" }, set!);
    }

    [Fact]
    public void Ctrl_m_lists_every_switch_and_light_and_nothing_that_says_nothing()
    {
        var keys = MonitorKeys();
        Assert.Contains("A350_ELEC_BAT_1", keys);                        // a switch
        Assert.Contains("A350_SIGNS_SEAT_BELTS", keys);                  // a selector
        Assert.Contains("A350_LT_ELEC_BAT_1_SEQ1_LIGHT", keys);          // a light
        Assert.DoesNotContain("A350_APU_START", keys);                   // a button: its press is the pilot's own
        Assert.DoesNotContain("A350_CABIN_ALT_TARGET_SET", keys);        // a typed field
        Assert.DoesNotContain("A350_OIS_AVNCS_EFB_LEFT", keys);          // a spring: its return is the pilot's own press
        Assert.DoesNotContain(A350PanelLamps.AcPowerKey, keys);          // a light's input, consumed silently
        Assert.DoesNotContain(keys, k => k.StartsWith("A350_IN_", StringComparison.Ordinal));
    }

    [Fact]
    public void A_switch_row_is_left_to_mainforms_generic_announce()
    {
        var bat = Def.GetVariables()["A350_ELEC_BAT_1"];
        Assert.True(bat.IsAnnounced);
        Assert.Equal(UpdateFrequency.Continuous, bat.UpdateFrequency);
        Assert.False(bat.ExcludeFromMonitorManager);
        Assert.False(Def.ProcessSimVarUpdate("A350_ELEC_BAT_1", 1, new SpeechCapture()));
    }

    [Fact]
    public void A_spring_switch_is_consumed_silently()
    {
        var spring = Def.GetVariables()["A350_OIS_AVNCS_EFB_LEFT"];
        Assert.True(spring.ExcludeFromMonitorManager);
        Assert.True(Def.ProcessSimVarUpdate("A350_OIS_AVNCS_EFB_LEFT", 1, new SpeechCapture()));
    }

    // Review 2026-10-29 (panels minor 1, speech 7): both tiller disconnects are switch rows on one INI_TILLER_DISC, so a
    // change made elsewhere was said twice. The first row on a variable speaks; its twin follows silently, no Ctrl+M row.
    [Fact]
    public void Two_switch_rows_on_one_variable_speak_once()
    {
        Assert.Equal(new Dictionary<string, string> { ["A350_FO_TILLER_DISCO"] = "A350_CPT_TILLER_DISCO" }, Def.SetTwins);
        var keys = MonitorKeys();
        Assert.Contains("A350_CPT_TILLER_DISCO", keys);
        Assert.DoesNotContain("A350_FO_TILLER_DISCO", keys);
        Assert.False(Def.ProcessSimVarUpdate("A350_CPT_TILLER_DISCO", 1, new SpeechCapture()));   // MainForm's announce says it
        Assert.True(Def.ProcessSimVarUpdate("A350_FO_TILLER_DISCO", 1, new SpeechCapture()));
    }

    [Fact]
    public void Ctrl_m_lists_one_row_per_master_light_pair_side()
    {
        var keys = MonitorKeys();
        Assert.Contains("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT", keys);
        Assert.Contains("A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT", keys);
        Assert.DoesNotContain("A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ1_LIGHT", keys);   // the same light, said once
        Assert.DoesNotContain("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ2_LIGHT", keys);
        Assert.Equal("Master caution", Def.GetVariables()["A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT"].DisplayName);
    }
}

/// <summary>The definition speaking its lights: deliveries feed the light board, a batch end speaks the sentence.</summary>
public class A350LampAnnouncementTests
{
    private long _now = 100_000;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly IniBuildsA350Definition _def;
    private static readonly IReadOnlyList<A350Lamp> Lamps = A350PanelLamps.Resolve(A350ControlMap.Load(),
        A350PanelLayout.Place(A350ControlMap.Load()), new[] { "Overhead", "Glareshield", "Instrument" });

    public A350LampAnnouncementTests()
    {
        _def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Cached = (_, _) => null,
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(new SimConnectManager(IntPtr.Zero));
    }

    private static A350Lamp Lamp(string key) => Lamps.Single(l => l.Key == key);

    /// <summary>Delivers every input of a lamp at <paramref name="value"/>, its own key at <paramref name="first"/>.</summary>
    private void Baseline(A350Lamp lamp, double first)
    {
        foreach (var key in lamp.InputKeys)
            _def.ProcessSimVarUpdate(key, key == lamp.Key ? first : 1, _speech);
    }

    private void BatchAfter(int ms)
    {
        _now += ms;
        _def.OnContinuousBatchDelivered(0);
    }

    [Fact]
    public void A_fault_light_is_spoken_at_a_batch_end_once_gathered()
    {
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        Baseline(bat, 0);
        BatchAfter(5000);
        Assert.Empty(_speech.All);   // the baseline says nothing
        Assert.True(_def.ProcessSimVarUpdate(bat.Key, 1, _speech));
        BatchAfter(100);
        Assert.Empty(_speech.All);   // still gathering
        BatchAfter(A350LampSpeech.GatherMs);
        Assert.Equal(new[] { bat.Name + " on" }, _speech.All);
    }

    // ANN LT TEST lights every annunciator at once: said once, never light by light; back to BRT the lights return to
    // their states unspoken (the switch's new position is the screen reader's).
    [Fact]
    public void The_annunciator_test_is_said_once_not_light_by_light()
    {
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        var gen = Lamp("A350_LT_ELEC_GEN_1A_SEQ1_LIGHT");
        Baseline(bat, 0);
        Baseline(gen, 0);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(A350PanelLamps.AnnLtKey, 0, _speech);   // TEST
        BatchAfter(5000);
        Assert.Equal(new[] { "Annunciator light test: all lights on" }, _speech.All);
        _def.ProcessSimVarUpdate(A350PanelLamps.AnnLtKey, 1, _speech);   // BRT
        BatchAfter(5000);
        Assert.Single(_speech.All);
    }

    /// <summary>Delivers every input of a lamp but the ANN LT switch (a light is decided without it, as not tested).</summary>
    private void BaselineWithoutAnnLt(IniBuildsA350Definition def, A350Lamp lamp)
    {
        foreach (var key in lamp.InputKeys.Where(k => k != A350PanelLamps.AnnLtKey))
            def.ProcessSimVarUpdate(key, key == lamp.Key ? 0 : 1, _speech);
    }

    // Review 2026-10-28 (minor 6): ANN LT's first value arriving at TEST after the lights were decided (its own
    // subscription can land after the batch; a flight load or reconnect mid-test) turned every light on through the
    // normal path, one by one: the flood the test's one sentence exists to prevent. A first value is a baseline.
    [Fact]
    public void A_first_ann_lt_value_at_test_speaks_no_light()
    {
        BaselineWithoutAnnLt(_def, Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT"));
        BaselineWithoutAnnLt(_def, Lamp("A350_LT_ELEC_GEN_1A_SEQ1_LIGHT"));
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(A350PanelLamps.AnnLtKey, 0, _speech);   // its first value: TEST
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(A350PanelLamps.AnnLtKey, 1, _speech);   // BRT: the lights return, unspoken
        BatchAfter(5000);
        Assert.Empty(_speech.All);
    }

    // The same after a flight load: ANN LT seeded from the cache at TEST left the lights decided before it dark in the
    // board, so each was said "on" at its next delivery.
    [Fact]
    public void Ann_lt_seeded_at_test_takes_the_lights_it_lights_as_their_baseline()
    {
        var cache = new Dictionary<string, double>(StringComparer.Ordinal) { [A350PanelLamps.AnnLtKey] = 0 };
        var def = new IniBuildsA350Definition
        {
            Clock = () => _now, Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null, IsMuted = _ => false,
            ActiveBatches = _ => new[] { 0 },
        };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        BaselineWithoutAnnLt(def, bat);
        for (int i = 0; i <= Md11SeedGate.QuietCycles; i++)
            def.OnContinuousBatchDelivered(0);   // the deliveries settled: the seed runs
        def.ProcessSimVarUpdate(bat.Key, 0, _speech);   // its fault input again, unchanged
        _now += 5000;
        def.OnContinuousBatchDelivered(0);
        Assert.Empty(_speech.All);
    }

    private static readonly Lazy<IReadOnlyList<A350Lamp>> Everywhere = new(() =>
    {
        var map = A350ControlMap.Load();
        var placement = A350PanelLayout.Place(map);
        return A350PanelLamps.Resolve(map, placement, placement.Structure.Keys);
    });

    private static A350Lamp AnyLamp(string key) => Everywhere.Value.Single(l => l.Key == key);

    // Review 2026-10-29 (speech 4): ECAM CLR, ATC MSG and AUTOLAND light on both sides from one rule each, and both sides
    // spoke ("Captain ECAM clear light on. First officer ECAM clear light on."). Lamps sharing a rule are one light: the
    // first speaks, without its side; the others are its echoes, with no Ctrl+M row.
    [Theory]
    [InlineData("A350_LT_ECAM_CLEAR_LEFT_SEQ1_LIGHT", "A350_LT_ECAM_CLEAR_RIGHT_SEQ1_LIGHT")]
    [InlineData("A350_LT_FCU_ATC_MSG_LEFT_SEQ1_LIGHT", "A350_LT_FCU_ATC_MSG_RIGHT_SEQ1_LIGHT")]
    [InlineData("A350_LT_FCU_AUTO_LAND_LEFT_SEQ1_LIGHT", "A350_LT_FCU_AUTO_LAND_RIGHT_SEQ1_LIGHT")]
    public void Lights_both_sides_show_from_one_rule_are_one_light(string captain, string firstOfficer)
    {
        var (cpt, fo) = (AnyLamp(captain), AnyLamp(firstOfficer));
        Assert.StartsWith("Captain ", cpt.Name, StringComparison.Ordinal);
        string spoken = char.ToUpperInvariant(cpt.Name["Captain ".Length]) + cpt.Name[("Captain ".Length + 1)..];
        Baseline(cpt, 0);
        Baseline(fo, 0);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(cpt.Key, 1, _speech);
        _def.ProcessSimVarUpdate(fo.Key, 1, _speech);
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Equal(new[] { spoken + " on" }, _speech.All);
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).ToList();
        Assert.Equal(spoken, Assert.Single(rows, r => r.Key == captain).Label);
        Assert.DoesNotContain(rows, r => r.Key == firstOfficer);
    }

    // A press of the first officer's CLR puts the one light out: the pilot's own press, never "ECAM clear light off".
    [Fact]
    public void A_press_of_either_twin_is_the_pilots_own()
    {
        var (cpt, fo) = (AnyLamp("A350_LT_ECAM_CLEAR_LEFT_SEQ1_LIGHT"), AnyLamp("A350_LT_ECAM_CLEAR_RIGHT_SEQ1_LIGHT"));
        Baseline(cpt, 1);
        Baseline(fo, 1);
        BatchAfter(5000);
        string foRow = A350PanelLayout.Place(A350ControlMap.Load()).RowsByPanel.Values.SelectMany(r => r)
            .First(r => r.Control?.Id == fo.ControlId && r.Action == A350RowAction.Press).Key;
        _def.HandleUIVariableSet(foRow, 1, _def.GetVariables()[foRow], new SimConnectManager(IntPtr.Zero), _speech);
        _speech.All.Clear();
        _def.ProcessSimVarUpdate(cpt.Key, 0, _speech);
        _def.ProcessSimVarUpdate(fo.Key, 0, _speech);
        BatchAfter(A350LampSpeech.OffHoldMs + 100);   // an off is said once it has stayed off this long
        Assert.Empty(_speech.All);
    }

    // A pick on the first officer's tiller row moves the captain's too: the pilot's own pick, which the screen reader said,
    // is never said back by the captain's row; a later change to another position is.
    [Fact]
    public void A_pick_on_a_twin_row_is_not_said_back_by_the_row_that_speaks()
    {
        var def = new IniBuildsA350Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Cached = (_, key) => key.EndsWith("_TILLER_DISCO", StringComparison.Ordinal) ? 0 : null,   // both connected
            IsMuted = _ => false,
        };
        var sim = new SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        def.HandleUIVariableSet("A350_FO_TILLER_DISCO", 1, def.GetVariables()["A350_FO_TILLER_DISCO"], sim, _speech);
        Assert.True(def.ProcessSimVarUpdate("A350_FO_TILLER_DISCO", 1, _speech));
        Assert.True(def.ProcessSimVarUpdate("A350_CPT_TILLER_DISCO", 1, _speech));    // the pick: consumed
        Assert.False(def.ProcessSimVarUpdate("A350_CPT_TILLER_DISCO", 0, _speech));   // not the pick: MainForm says it
    }

    // Review 2026-10-29 (speech 5): a light whose rule reads only another switch row's variable repeats that row: each RMP
    // reception light (its reception selector's), the cockpit door's OPEN light (the door row's), each display-cycle light
    // (the other side's OIS on center). The row speaks the change; the light never does, and has no Ctrl+M row.
    [Theory]
    [InlineData("A350_LT_RMP_VHF1_VOL_1_LIGHT")]
    [InlineData("A350_LT_CKPT_DOOR_LT_SEQ1_LIGHT")]
    [InlineData("A350_LT_OIS_DISPLAY_TRANSFER_DISPLAY_CYCLE_LEFT_SEQ1_LIGHT")]
    public void A_light_that_mirrors_a_switch_row_never_speaks(string key)
    {
        var lamp = AnyLamp(key);
        Baseline(lamp, 0);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(lamp.Key, 1, _speech);
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Empty(_speech.All);
        Assert.DoesNotContain(MonitorRowBuilder.Build(_def.GetVariables()), r => r.Key == key);
    }

    [Fact]
    public void A_muted_light_says_nothing()
    {
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        _muted.Add(bat.Key);
        Baseline(bat, 0);
        _def.ProcessSimVarUpdate(bat.Key, 1, _speech);
        BatchAfter(5000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_pressed_buttons_own_light_is_left_to_its_label()
    {
        var abrk = Lamp("A350_LT_LDG_AUTO_BRK_SEQ1_LIGHT");
        Baseline(abrk, 0);
        _def.HandleUIVariableSet("A350_LDG_AUTO_BRK", 1, _def.GetVariables()["A350_LDG_AUTO_BRK"], new SimConnectManager(IntPtr.Zero), _speech);
        _def.ProcessSimVarUpdate(abrk.Key, 1, _speech);
        BatchAfter(5000);
        Assert.Empty(_speech.All);
        _now += IniBuildsA350Definition.PressedLampMs;
        _def.ProcessSimVarUpdate(abrk.Key, 0, _speech);   // disarmed by the aircraft later: spoken
        BatchAfter(A350LampSpeech.OffHoldMs + 100);
        Assert.Equal(new[] { abrk.Name + " off" }, _speech.All);
    }

    [Fact]
    public void A_master_light_is_spoken_once_and_only_as_it_comes_on()
    {
        var caution = Lamp("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT");
        var foCaution = Lamp("A350_LT_FCU_MASTER_CAUTION_RIGHT_SEQ1_LIGHT");
        Baseline(caution, 0);
        Baseline(foCaution, 0);
        _def.ProcessSimVarUpdate(caution.Key, 1, _speech);
        _def.ProcessSimVarUpdate(foCaution.Key, 1, _speech);
        BatchAfter(5000);
        Assert.Equal(new[] { "Master caution on" }, _speech.All);
        _def.ProcessSimVarUpdate(caution.Key, 0, _speech);
        BatchAfter(10000);
        Assert.Single(_speech.All);   // going out is the pilot's own press of it
    }

    // Review 2026-10-29 (speech 9): a master light lit again within 3 s of its acknowledgement was never said: pressed
    // through MSFSBA, its own press's window quieted it; pressed in the cockpit, its held off took it for a flash. Both
    // lamps are steady (MASTER WARN's rule holds while its warning is active): lit again, it is the next caution or warning.
    [Fact]
    public void A_master_caution_lit_again_right_after_its_press_through_msfsba_is_said()
    {
        var caution = Lamp("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT");
        Baseline(caution, 0);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(caution.Key, 1, _speech);
        BatchAfter(A350LampSpeech.GatherMs + 100);
        _def.HandleUIVariableSet("A350_FCU_MASTER_CAUTION_LEFT", 1, _def.GetVariables()["A350_FCU_MASTER_CAUTION_LEFT"],
            new SimConnectManager(IntPtr.Zero), _speech);
        _def.ProcessSimVarUpdate(caution.Key, 0, _speech);   // out: the press
        _now += 1000;
        _def.ProcessSimVarUpdate(caution.Key, 1, _speech);   // the next caution, inside the press's own window
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Equal(new[] { "Master caution on", "Master caution on" }, _speech.All);
    }

    [Fact]
    public void A_master_caution_lit_again_right_after_a_cockpit_press_is_said()
    {
        var caution = Lamp("A350_LT_FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT");
        Baseline(caution, 0);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(caution.Key, 1, _speech);
        BatchAfter(A350LampSpeech.GatherMs + 100);
        _def.ProcessSimVarUpdate(caution.Key, 0, _speech);   // pressed in the cockpit
        _now += 1000;
        _def.ProcessSimVarUpdate(caution.Key, 1, _speech);   // the next caution, inside the off's hold
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Equal(new[] { "Master caution on", "Master caution on" }, _speech.All);
    }

    [Fact]
    public void A_master_warning_raised_again_right_after_a_cockpit_press_is_said()
    {
        var warning = Lamp("A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT");
        string active = warning.KeyFor("INI_MASTER_WARNING_ACTIVE"), bottom = warning.KeyFor("INI_MASTER_WARNING_CAPT_BOTTOM");
        string apOff = warning.KeyFor("INI_AUTOPILOT_DISCONNECT_LIGHT");
        foreach (var key in warning.InputKeys)
            _def.ProcessSimVarUpdate(key, key == warning.Key || key == bottom || key == active || key == apOff ? 0 : 1, _speech);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(active, 1, _speech);   // a warning
        BatchAfter(A350LampSpeech.GatherMs + 100);
        _def.ProcessSimVarUpdate(active, 0, _speech);   // pressed in the cockpit
        _now += 1000;
        _def.ProcessSimVarUpdate(active, 1, _speech);   // the next warning, inside the off's hold
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Equal(new[] { "Master warning on", "Master warning on" }, _speech.All);
    }

    [Fact]
    public void A_flashing_master_warning_is_spoken_once_whatever_phase_a_delivery_catches()
    {
        // Live 2026-10-28, the fire test: MASTER WARN flashes, about 0.5 s lit and 0.5 s dark, while the warning lasts,
        // and the 1 Hz light delivery catches either phase. A dark phase dropped the pending "on": nothing was said.
        // INI_MASTER_WARNING_ACTIVE holds 1 for the whole warning, until the pilot's press.
        var warning = Lamp("A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT");
        string active = warning.KeyFor("INI_MASTER_WARNING_ACTIVE"), bottom = warning.KeyFor("INI_MASTER_WARNING_CAPT_BOTTOM");
        void Legends(double lit)
        {
            _def.ProcessSimVarUpdate(warning.Key, lit, _speech);   // the top legend, the light's own key
            _def.ProcessSimVarUpdate(bottom, lit, _speech);
        }
        string apOff = warning.KeyFor("INI_AUTOPILOT_DISCONNECT_LIGHT");
        foreach (var key in warning.InputKeys)
            _def.ProcessSimVarUpdate(key, key == warning.Key || key == bottom || key == active || key == apOff ? 0 : 1, _speech);
        _def.ProcessSimVarUpdate(active, 1, _speech);
        Legends(1);
        _now += 1000;
        Legends(0);   // caught dark before the gather ended
        BatchAfter(A350LampSpeech.GatherMs);
        Assert.Equal(new[] { "Master warning on" }, _speech.All);
        for (int i = 0; i < 5; i++)
        {
            Legends(i % 2 == 0 ? 1 : 0);
            BatchAfter(1000);
        }
        Assert.Single(_speech.All);   // still flashing: said once
        _def.ProcessSimVarUpdate(active, 0, _speech);   // the pilot's press
        Legends(0);
        BatchAfter(A350LampSpeech.OffHoldMs + 100);
        Assert.Single(_speech.All);   // out: never said
        _def.ProcessSimVarUpdate(active, 1, _speech);   // the next warning
        BatchAfter(A350LampSpeech.GatherMs + 100);
        Assert.Equal(new[] { "Master warning on", "Master warning on" }, _speech.All);
    }

    // Live 2026-10-30 after an autoland: AUTO FLT AP OFF flashed MASTER WARN with INI_MASTER_WARNING_ACTIVE at 0 and was said
    // "Master warning on" four times in 21 s; INI_AUTOPILOT_DISCONNECT_LIGHT held 1 until the press.
    [Fact]
    public void A_flashing_autopilot_disconnect_warning_is_spoken_once()
    {
        var warning = Lamp("A350_LT_FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT");
        string active = warning.KeyFor("INI_MASTER_WARNING_ACTIVE"), bottom = warning.KeyFor("INI_MASTER_WARNING_CAPT_BOTTOM");
        string apOff = warning.KeyFor("INI_AUTOPILOT_DISCONNECT_LIGHT");
        void Legends(double lit)
        {
            _def.ProcessSimVarUpdate(warning.Key, lit, _speech);
            _def.ProcessSimVarUpdate(bottom, lit, _speech);
        }
        foreach (var key in warning.InputKeys)
            _def.ProcessSimVarUpdate(key, key == warning.Key || key == bottom || key == active || key == apOff ? 0 : 1, _speech);
        BatchAfter(5000);
        _def.ProcessSimVarUpdate(apOff, 1, _speech);
        Legends(1);
        BatchAfter(A350LampSpeech.GatherMs);
        for (int i = 0; i < 12; i++)   // dark for runs of deliveries, as the 1 Hz delivery caught it
        {
            Legends(i % 4 == 3 ? 1 : 0);
            BatchAfter(1000);
        }
        Assert.Equal(new[] { "Master warning on" }, _speech.All);
        _def.ProcessSimVarUpdate(apOff, 0, _speech);   // the pilot's MASTER WARN press
        Legends(0);
        BatchAfter(A350LampSpeech.OffHoldMs + 100);
        Assert.Single(_speech.All);
    }

    [Fact]
    public void After_a_reset_lights_that_did_not_change_are_seeded_from_the_cache()
    {
        // A flight load re-delivers only changed values: without a seed, a light's first real change would be taken
        // for its baseline and never spoken.
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        var cache = bat.InputKeys.ToDictionary(k => k, k => k == bat.Key ? 1.0 : 1.0);   // lit
        _def.Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null;
        _def.ActiveBatches = _ => new[] { 0 };
        _def.OnSimContextReset();
        BatchAfter(1000);                       // the first full cycle
        BatchAfter(Md11SeedGate.CeilingMs);     // nothing of the A350's own moved (the load changed nothing): seeded at the ceiling
        _def.ProcessSimVarUpdate(bat.Key, 0, _speech);
        BatchAfter(A350LampSpeech.OffHoldMs + 100);
        Assert.Equal(new[] { bat.Name + " off" }, _speech.All);
    }

    // Review 2026-10-29 (speech 2): the seed ran three batch ends (about a second) after a flight load, while the stream still
    // repeated the last flight's values (an aircraft publishes seconds after AircraftLoaded: the MD-11 measured 8.47 s), so
    // those became the baselines and each of the new flight's differences was spoken. The seed now waits for evidence, as
    // the MD-11's (Md11SeedGate): every batch delivered, a value of the A350's own changed, then quiet cycles.
    [Fact]
    public void After_a_load_the_seed_waits_for_the_a350_to_publish()
    {
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        var gen = Lamp("A350_LT_ELEC_GEN_1A_SEQ1_LIGHT");
        var cache = bat.InputKeys.Concat(gen.InputKeys).Distinct().ToDictionary(k => k, k => k == bat.Key || k == gen.Key ? 0.0 : 1.0);
        _def.Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null;   // the last flight's: both dark
        _def.ActiveBatches = _ => new[] { 0 };
        _def.OnSimContextReset();
        for (int i = 0; i < 3; i++)
            BatchAfter(1000);                                 // the stream still repeats the last flight's values
        _def.ProcessSimVarUpdate(bat.Key, 1, _speech);        // the new flight publishes: its BAT 1 fault lit
        for (int i = 0; i <= Md11SeedGate.QuietCycles; i++)
            BatchAfter(1000);
        Assert.Empty(_speech.All);                            // the new flight's state is its baseline, never news
    }

    [Fact]
    public void A_flight_load_resets_the_baselines()
    {
        var bat = Lamp("A350_LT_ELEC_BAT_1_SEQ1_LIGHT");
        Baseline(bat, 0);
        _def.OnSimContextReset();
        Baseline(bat, 1);      // the load's first values: a new baseline, not a change
        BatchAfter(5000);
        Assert.Empty(_speech.All);
    }
}

/// <summary>The pedestal's levers speak as the A380's (A350LeverSpeech), each sentence under its own Ctrl+M row.</summary>
public class A350LeverAnnouncementTests
{
    private sealed class Pedestal : IniBuildsA350Definition
    {
        public Pedestal() : base(new[] { "Pedestal" }) { }
    }

    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new(StringComparer.Ordinal);
    private readonly Pedestal _def;

    public A350LeverAnnouncementTests()
    {
        _def = new Pedestal
        {
            CanLand = _ => true,
            Send = (_, _) => { },
            ReRead = (_, _) => { },
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            IsMuted = key => _muted.Contains(key),
            Clock = () => _now,
        };
        _def.Attach(new SimConnectManager(IntPtr.Zero));
    }

    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private long _now;

    private bool Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    /// <summary>A batch end once a lever's gather is over.</summary>
    private void Later()
    {
        _now += A350LeverSpeech.GatherMs;
        _def.OnContinuousBatchDelivered(0);
    }

    private void Baseline()
    {
        Deliver(A350LeverSpeech.Lever1, 0);
        Deliver(A350LeverSpeech.Lever2, 0);
    }

    // Review 2026-10-29: the All row shares lever 1's angle (L:INI_AUTOTHRUST_TLA:1) and, placed first, took its batch
    // slot [VAR-7]: lever 1 streamed on its own 1 Hz subscription, up to a second apart from lever 2, so a lone lever's
    // sentence came before "All thrust levers". The levers ride one batch, one message; the All row, silent on its own
    // delivery, is the copy that streams apart.
    [Fact]
    public void Both_thrust_levers_ride_one_batch_and_the_all_row_streams_apart()
    {
        var vars = new IniBuildsA350Definition().GetVariables();
        var batches = MSFSBlindAssist.SimConnect.ContinuousBatchLayout.BatchNumbers(vars);
        Assert.True(batches.ContainsKey(A350LeverSpeech.Lever1), "lever 1 rides a batch");
        Assert.Equal(batches[A350LeverSpeech.Lever1], batches[A350LeverSpeech.Lever2]);
        Assert.True(vars[A350LeverSpeech.AllLevers].ExcludeFromBatch);
    }

    [Fact]
    public void The_thrust_levers_speak_their_detents_after_a_silent_baseline()
    {
        Baseline();
        Assert.True(Deliver(A350LeverSpeech.Lever1, 25));
        Assert.True(Deliver(A350LeverSpeech.Lever2, 25));
        Assert.True(Deliver(A350LeverSpeech.AllLevers, 25));   // the All row's own delivery: the levers spoke
        Later();
        Assert.Equal(new[] { "All thrust levers Climb" }, _speech.All);   // never lever 1's lone "Thrust lever 1 Climb" first
        Deliver(A350LeverSpeech.Lever2, 45);
        Later();
        Assert.Equal(new[] { "All thrust levers Climb", "Thrust lever 2 TOGA" }, _speech.All);
    }

    [Fact]
    public void Each_sentence_obeys_its_own_row()
    {
        _muted.Add(A350LeverSpeech.AllLevers);
        Baseline();
        Deliver(A350LeverSpeech.Lever1, 25);
        Deliver(A350LeverSpeech.Lever2, 25);
        Later();
        Assert.Empty(_speech.All);
        _muted.Add(A350LeverSpeech.Lever2);
        Deliver(A350LeverSpeech.Lever2, 45);
        Deliver(A350LeverSpeech.Lever1, 0);
        Later();
        Assert.Equal(new[] { "Thrust lever 1 Idle" }, _speech.All);
        Assert.True(_def.IsMuteWrapExempt(A350LeverSpeech.Lever1));   // its delivery may speak the All row's sentence
        Assert.True(_def.IsMuteWrapExempt(A350LeverSpeech.Lever2));
    }

    [Fact]
    public void The_speed_brake_speaks_in_tenths_and_keeps_its_ctrl_m_row()
    {
        Assert.True(Deliver("A350_FCTL_SPD_BRK_LEVER", 0));
        Assert.True(Deliver("A350_FCTL_SPD_BRK_LEVER", 0.366));
        Assert.Equal(new[] { "Spoilers 40 percent" }, _speech.All);
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("A350_FCTL_SPD_BRK_LEVER", rows);
        Assert.Contains(A350LeverSpeech.Lever1, rows);
        Assert.Contains(A350LeverSpeech.AllLevers, rows);
        Assert.DoesNotContain("A350_STAT_SPEED_BRAKE", rows);
    }

    [Fact]
    public void Levers_unseen_after_a_load_are_seeded_from_the_cache_so_their_first_move_speaks()
    {
        _cache[A350LeverSpeech.Lever1] = 25;
        _cache[A350LeverSpeech.Lever2] = 25;
        _cache["A350_FCTL_SPD_BRK_LEVER"] = 0;
        _def.ActiveBatches = _ => new[] { 0 };
        _def.OnContinuousBatchDelivered(0);   // the first full cycle
        _now += Md11SeedGate.CeilingMs;
        _def.OnContinuousBatchDelivered(0);   // nothing of the A350's own moved: seeded at the ceiling
        Deliver(A350LeverSpeech.Lever1, 0);
        Later();
        Deliver("A350_FCTL_SPD_BRK_LEVER", 1);
        Assert.Equal(new[] { "Thrust lever 1 Idle", "Spoilers full" }, _speech.All);
    }

    [Fact]
    public void The_page_keys_lights_are_the_sd_page_rows_echoes()
    {
        var rows = MonitorRowBuilder.Build(_def.GetVariables()).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("A350_SD_PAGE", rows);                                // the page speaks, as on the A380
        Assert.DoesNotContain("A350_LT_ECAM_ENG_SEQ1_LIGHT", rows);           // its key's light shows the same page
        Assert.Contains("A350_LT_ECAM_ABN_SEQ1_LIGHT", rows);                 // a light of its own still speaks
    }

    [Fact]
    public void A_flight_load_takes_a_fresh_baseline()
    {
        Baseline();
        _def.OnSimContextReset();
        Deliver(A350LeverSpeech.Lever1, 45);
        Deliver(A350LeverSpeech.Lever2, 45);
        Assert.Empty(_speech.All);
    }
}
