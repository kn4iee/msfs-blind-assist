using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Structural checks, across the whole definition, of the iniBuilds A330 (MSFS 2024) definition and its generated map.</summary>
public class IniA330DefinitionTests
{
    private static readonly IniA330Definition Def = new();
    private static readonly A330ControlMap Map = A330ControlMap.Embedded;

    [Fact]
    public void Every_panel_control_and_display_row_is_a_registered_variable()
    {
        var vars = Def.GetVariables();
        var missing = Def.GetPanelControls().SelectMany(p => p.Value.Select(k => $"{p.Key}: {k}"))
            .Concat(Def.GetPanelDisplayVariables().SelectMany(p => p.Value.Select(k => $"{p.Key} (display): {k}")))
            .Where(s => !vars.ContainsKey(s[(s.LastIndexOf(": ") + 2)..])).ToList();
        Assert.True(missing.Count == 0, string.Join("\n", missing));
    }

    [Fact]
    public void Every_panel_with_controls_is_in_the_section_tree()
    {
        var inTree = Def.GetPanelStructure().SelectMany(s => s.Value).ToHashSet();
        var orphans = Def.GetPanelControls().Keys.Concat(Def.GetPanelDisplayVariables().Keys).Where(p => !inTree.Contains(p)).Distinct().ToList();
        Assert.True(orphans.Count == 0, "panels not in any section: " + string.Join(", ", orphans));
    }

    [Fact]
    public void No_panel_has_two_rows_with_the_same_spoken_name()
    {
        var vars = Def.GetVariables();
        var dups = Def.GetPanelControls().Select(p => (panel: p.Key, keys: p.Value.Concat(
                Def.GetPanelDisplayVariables().TryGetValue(p.Key, out var d) ? d : new List<string>())))
            .SelectMany(p => p.keys.GroupBy(k => vars[k].DisplayName).Where(g => g.Count() > 1)
                .Select(g => $"{p.panel}: '{g.Key}' x{g.Count()}")).ToList();
        Assert.True(dups.Count == 0, string.Join("\n", dups));
    }

    [Fact]
    public void No_control_renders_as_a_text_entry()
    {
        // MainForm renders any key containing _SET as a TextBox + Set button
        var setKeys = Def.GetPanelControls().SelectMany(p => p.Value).Where(k => k.Contains("_SET")).ToList();
        Assert.Empty(setKeys);
    }

    [Fact]
    public void Monitored_variable_count_stays_inside_the_data_definition_budget()
    {
        int continuous = Def.GetVariables().Values.Count(v => v.UpdateFrequency == UpdateFrequency.Continuous && v.Type != SimVarType.InputEvent);
        Assert.InRange(continuous, 100, 850);
    }

    [Fact]
    public void Registers_the_bridge_probe_so_calculator_writes_can_be_verified()
        => Assert.True(Def.GetVariables().ContainsKey("MSFSBA_BRIDGE_PROBE"));

    [Fact]
    public void Switches_read_from_their_own_input_event_are_InputEvent_variables()
    {
        var vars = Def.GetVariables();
        Assert.Equal(SimVarType.InputEvent, vars["A330_PROBE_HEAT"].Type);
        Assert.Equal("AIRLINER_PROBE_HEAT", vars["A330_PROBE_HEAT"].Name);
        Assert.Equal(SimVarType.LVar, vars["A330_PACK1"].Type);
        Assert.Equal("INI_PACK1_BUTTON", vars["A330_PACK1"].Name);
    }
}

/// <summary>The A330's variables: Ctrl+M rows that do something, no batch name collisions, panel refresh.</summary>
public class A330VariableReviewTests
{
    [Fact]
    public void The_speedbrake_lever_has_its_ctrl_m_row()
    {
        // plan 04 Task 4a: ProcessPanelUpdate now says the lever in the A32NX's bands, so the row mutes something
        // ([VAR-9] no longer applies; A330SpeedbrakeTests.A_muted_row_says_nothing proves the mute).
        var rows = MonitorRowBuilder.Build(new IniA330Definition().GetVariables()).Select(r => r.Key).ToList();
        Assert.Contains("A330_SPDBRK", rows);
    }

    [Fact]
    public void Batched_vars_do_not_share_an_underlying_name()
    {
        // The check VarNameCollisionTests makes on every aircraft in ComboLabelCollapseTests.AllAircraft
        // ([VAR-7]), which the A330 is on. Kept so the A330's own failure names it; the count must not drop.
        var offenders = new IniA330Definition().GetVariables()
            .Where(kv => kv.Value.Type != SimVarType.Event
                         && kv.Value.UpdateFrequency == UpdateFrequency.Continuous
                         && !kv.Value.ExcludeFromBatch)
            .GroupBy(kv => kv.Value.Name)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + " (" + string.Join(", ", g.Select(kv => kv.Key)) + ")")
            .ToList();
        Assert.True(offenders.Count == 0, "Continuous batched vars sharing one Name: " + string.Join("; ", offenders));
    }

    [Fact]
    public void The_flap_combo_follows_its_detents_only()
    {
        var flaps = new IniA330Definition().GetVariables()["A330_FLAPS"];
        for (int i = 0; i < IniA330Definition.FlapDetents.Length; i++) Assert.True(flaps.RefreshesControlWhenDefHandled(i));
        Assert.False(flaps.RefreshesControlWhenDefHandled(-1));
        Assert.False(flaps.RefreshesControlWhenDefHandled(IniA330Definition.FlapDetents.Length));
        Assert.False(flaps.RefreshesControlWhenDefHandled(1.5));
        Assert.False(flaps.RefreshesControlWhenDefHandled(double.NaN));
    }

    [Fact]
    public void The_speedbrake_combo_follows_resting_positions_never_the_values_a_lever_sweeps_through()
    {
        var brk = new IniA330Definition().GetVariables()["A330_SPDBRK"];
        Assert.True(brk.RefreshesControlWhenDefHandled(0));
        Assert.True(brk.RefreshesControlWhenDefHandled(0.5));
        Assert.True(brk.RefreshesControlWhenDefHandled(1));
        Assert.True(brk.RefreshesControlWhenDefHandled(0.995));      // the sim's own rounding at a stop
        foreach (double mid in new[] { 0.1, 0.25, 0.3, 0.7, 0.8, 0.9 })
            Assert.False(brk.RefreshesControlWhenDefHandled(mid), $"{mid} is travel, not a position");
        Assert.False(brk.RefreshesControlWhenDefHandled(double.NaN));
    }

    [Fact]
    public void Guards_follow_the_values_they_name_and_sliders_follow_any_reading()
    {
        var vars = new IniA330Definition().GetVariables();
        var guards = vars.Where(kv => kv.Value.DisplayName.EndsWith(" guard", StringComparison.Ordinal)
                                      && kv.Value.ValueDescriptions.Count > 0).ToList();
        Assert.NotEmpty(guards);
        foreach (var (key, g) in guards)
        {
            foreach (double v in g.ValueDescriptions.Keys) Assert.True(g.RefreshesControlWhenDefHandled(v), $"{key} at {v}");
            Assert.False(g.RefreshesControlWhenDefHandled(double.NaN), key);
            Assert.False(g.RefreshesControlWhenDefHandled(987654), key);
        }
        var sliders = vars.Where(kv => kv.Value.RenderAsSlider).ToList();
        Assert.NotEmpty(sliders);
        foreach (var (key, s) in sliders)
        {
            Assert.True(s.RefreshesControlWhenDefHandled(s.SliderMin), key);
            Assert.True(s.RefreshesControlWhenDefHandled((s.SliderMin + s.SliderMax) / 2), key);
            Assert.False(s.RefreshesControlWhenDefHandled(double.NaN), key);
        }
    }

    [Fact]
    public void Every_panel_control_the_definition_handles_refreshes_while_its_panel_is_open()
    {
        // ProcessSimVarUpdate returning true skips MainForm's control refresh; without the predicate an
        // open combo or slider keeps the position it was built with (VAR-6, MainForm's def-handled branch).
        var def = new IniA330Definition();
        var vars = def.GetVariables();
        var stale = new List<string>();
        foreach (var key in def.GetPanelControls().SelectMany(p => p.Value).Distinct())
        {
            if (!vars.TryGetValue(key, out var v) || v.RenderAsButton || v.UpdateFrequency != UpdateFrequency.Continuous) continue;
            double probe = v.RenderAsSlider ? v.SliderMin : v.ValueDescriptions.Keys.Cast<double?>().FirstOrDefault() ?? 0;
            bool handled = new IniA330Definition().ProcessSimVarUpdate(key, probe, new SpeechCapture());
            if (handled && !v.RefreshesControlWhenDefHandled(probe)) stale.Add(key);
        }
        Assert.True(stale.Count == 0, "handled by the definition, never refreshed in an open panel: " + string.Join(", ", stale));
    }

    [Theory]
    [InlineData("A330_SPD_MACH")]
    [InlineData("A330_TRK_FPA")]
    [InlineData("INDICATED_ALTITUDE")]
    [InlineData("MON_ElevatorTrim")]
    public void The_copy_that_the_pilot_hears_stays_on_the_batch(string key)
    {
        var def = new IniA330Definition().GetVariables()[key];
        Assert.False(def.ExcludeFromBatch);
        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
    }
}
