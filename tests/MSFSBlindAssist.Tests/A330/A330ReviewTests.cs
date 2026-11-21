using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>Regression tests for the A330 FCU value dialogs and the Ctrl+P autopilot window.</summary>
public class A330ReviewTests
{
    [Fact]
    public void Autopilot_window_rows_have_distinct_access_keys()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var keys = def.AutopilotRows(sim, null!)
            .Select(r => r.Label)
            .Where(l => l.Contains('&'))
            .Select(l => char.ToUpperInvariant(l[l.IndexOf('&') + 1]))
            .ToList();
        var dups = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dups.Count == 0, "shared access keys: " + string.Join(", ", dups));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void Typed_numbers_read_the_same_on_every_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            // FPA: "-0,5" is half a degree, never -5.0
            Assert.True(IniA330Definition.TryParseVs("-0,5", true, out double fpa));
            Assert.Equal(-0.5, fpa);
            Assert.True(IniA330Definition.TryParseVs("-2.5", true, out fpa));
            Assert.Equal(-2.5, fpa);
            // Mach with a comma
            Assert.True(IniA330Definition.TryParseSpeed("0,82", out double mach));
            Assert.Equal(0.82, mach);
            Assert.True(IniA330Definition.TryParseSpeed("0.82", out mach));
            Assert.Equal(0.82, mach);
            // inHg with a comma is 29.92 inHg, not 2992
            Assert.True(IniA330Definition.TryParseBaro("29,92", out double hpa));
            Assert.Equal(29.92 * 33.8639, hpa, 6);
            Assert.True(IniA330Definition.TryParseBaro("1013", out hpa));
            Assert.Equal(1013, hpa);
            // whole numbers
            Assert.True(IniA330Definition.TryParseHeading("270", out int h));
            Assert.Equal(270, h);
            Assert.True(IniA330Definition.TryParseAltitude("35000", out int a));
            Assert.Equal(35000, a);
            return 0;
        });
    }

    [Fact]
    public void Standard_button_leaves_its_verdict_to_ToggleStandard()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var std = Assert.Single(def.BaroToggles(sim, null!));
        Assert.True(std.SuppressStateAnnounce?.Invoke() == true);
    }

    [Fact]
    public void Knob_buttons_with_a_constant_state_do_not_read_back_but_stateful_ones_do()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var all = def.HeadingToggles(sim, null!).Concat(def.SpeedToggles(sim, null!))
            .Concat(def.AltitudeToggles(sim, null!)).Concat(def.VsToggles(sim, null!)).ToList();
        string Plain(ToggleButtonDef t) => t.Label.Replace("&", "");
        var silent = all.Where(t => t.SuppressStateAnnounce?.Invoke() == true).Select(Plain).OrderBy(s => s).ToList();
        var expectedSilent = new[]
        {
            "Heading pull (selected)", "Heading push (NAV)",
            "Altitude pull (open climb or descent)", "Altitude push (managed)", "ALT (hold the current altitude)",
            "V/S pull (engage)", "V/S push (level off)",
        }.OrderBy(s => s).ToList();
        Assert.Equal(expectedSilent, silent);
        // every other button keeps its read-back, so a button with a real state is never silenced
        Assert.Equal(all.Count - expectedSilent.Count, all.Count(t => t.SuppressStateAnnounce is null));
    }

    [Fact]
    public void Vs_dialog_checks_and_writes_for_the_mode_at_commit_time()
    {
        bool fpa = false;   // the dialog opened in HDG-V/S
        Func<bool> isFpa = () => fpa;
        Assert.True(IniA330Definition.VsValidator("-1500", isFpa).isValid);
        var vs = IniA330Definition.VsSet("-1500", isFpa);
        Assert.Equal(("INI_VVI_DIAL", "A330_FCU_VS", -1500.0), (vs!.Lvar, vs.CacheKey, vs.Target));

        fpa = true;         // the pilot pressed TRK/FPA in the dialog
        Assert.False(IniA330Definition.VsValidator("-1500", isFpa).isValid);
        Assert.True(IniA330Definition.VsValidator("-3", isFpa).isValid);
        var angle = IniA330Definition.VsSet("-3", isFpa);
        Assert.Equal(("INI_FPA_DIAL", "A330_FCU_FPA", -3.0), (angle!.Lvar, angle.CacheKey, angle.Target));
        Assert.Null(IniA330Definition.VsSet("-1500", isFpa));
    }

    [Fact]
    public void Heading_dialog_writes_the_dial_of_the_mode_at_commit_time()
    {
        bool trk = false;
        Func<bool> isTrk = () => trk;
        Assert.Equal("INI_HEADING_DIAL", IniA330Definition.HeadingSet("270", isTrk)!.Lvar);
        trk = true;
        var set = IniA330Definition.HeadingSet("270", isTrk)!;
        Assert.Equal(("INI_TRK_DIAL", "A330_FCU_TRK", 270.0, true), (set.Lvar, set.CacheKey, set.Target, set.Alt));
        Assert.Equal(0, IniA330Definition.HeadingSet("360", isTrk)!.Target);
    }

    [Fact]
    public void Ambiguous_or_out_of_range_input_is_refused()
    {
        Assert.False(IniA330Definition.TryParseVs("-10", true, out _));
        Assert.False(IniA330Definition.TryParseVs("6,001", false, out _));   // a thousands group, refused
        Assert.False(IniA330Definition.TryParseSpeed("NaN", out _));
        Assert.False(IniA330Definition.TryParseHeading("abc", out _));
        Assert.False(IniA330Definition.TryParseHeading("361", out _));
        Assert.False(IniA330Definition.TryParseAltitude("99", out _));
    }
}

/// <summary>The A330's take-off call-outs follow the shared contract ([TKO-5]): one keys record, a feed
/// that pauses once nothing can be called, and a mute row per speed.</summary>
[Collection("SettingsManagerGlobalState")]
public class A330TakeoffCalloutTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A330TakeoffCalloutTests()
    {
        _savedMutes = SettingsManager.Current.A330DisabledMonitorVariables;
        Mute();
    }

    public void Dispose()
    {
        SettingsManager.Current.A330DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.A330DisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static IniA330Definition Parked(SpeechCapture speech)
    {
        var def = new IniA330Definition();
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffV1Key, 142, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffVrKey, 148, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffV2Key, 155, speech);
        def.ProcessSimVarUpdate(IniA330Definition.TakeoffIasKey, 5, speech);
        return def;
    }

    private static void Roll(IniA330Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples) def.ProcessSimVarUpdate(IniA330Definition.TakeoffIasKey, ias, speech);
    }

    [Fact]
    public void The_airspeed_feed_is_named_and_pauses_once_nothing_can_be_called()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Assert.Equal(IniA330Definition.TakeoffIasKey, def.TakeoffCalloutFeedKey);
        Assert.True(def.TakeoffCalloutFeedNeeded);                       // on the ground: a roll can start any moment

        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Assert.True(def.TakeoffCalloutFeedNeeded);                       // airborne but V2 not yet called
        Roll(def, speech, 156);
        Assert.False(def.TakeoffCalloutFeedNeeded);                      // V2 called: the feed pauses until touchdown
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        Assert.True(def.TakeoffCalloutFeedNeeded);
    }

    [Fact]
    public void The_feed_is_kept_out_of_the_mute_wrap()
    {
        var def = new IniA330Definition();
        Assert.True(def.IsMuteWrapExempt(IniA330Definition.TakeoffIasKey));
        Assert.False(def.IsMuteWrapExempt(IniA330Definition.TakeoffV1Key));
    }

    [Fact]
    public void A_roll_calls_v1_rotate_and_v2_in_order()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 141, 143, 147, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Theory]
    [InlineData(IniA330Definition.TakeoffV1Key, "Rotate", "V2")]
    [InlineData(IniA330Definition.TakeoffVrKey, "V1", "V2")]
    [InlineData(IniA330Definition.TakeoffV2Key, "V1", "Rotate")]
    public void A_muted_row_silences_only_its_own_call(string mutedKey, string first, string second)
    {
        Mute(mutedKey);
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Equal(new[] { first, second }, speech.Interrupts);
    }

    [Fact]
    public void All_three_rows_muted_say_nothing()
    {
        Mute(IniA330Definition.TakeoffV1Key, IniA330Definition.TakeoffVrKey, IniA330Definition.TakeoffV2Key);
        var speech = new SpeechCapture();
        var def = Parked(speech);
        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void The_mute_rows_exist_in_ctrl_m_and_the_feed_does_not()
    {
        var rows = MonitorRowBuilder.Build(new IniA330Definition().GetVariables()).Select(r => r.Key).ToList();
        Assert.Contains(IniA330Definition.TakeoffV1Key, rows);
        Assert.Contains(IniA330Definition.TakeoffVrKey, rows);
        Assert.Contains(IniA330Definition.TakeoffV2Key, rows);
        Assert.DoesNotContain(IniA330Definition.TakeoffIasKey, rows);
    }
}

/// <summary>Every number the A330 speaks or shows from its definition reads the same on a comma-decimal PC
/// (CI runs under en-US, so a test that sets no culture cannot catch a regression).</summary>
public class A330CultureTests
{
    public static TheoryData<string> Cultures() => new() { "sv-SE", "de-DE", "en-US" };

    private static (IniA330Definition def, SimConnectManager sim) Shown(params (string Key, double Value)[] cache)
    {
        var sim = Tests.FirstOfficer.SeededSimConnectCache.With(cache);
        var def = new IniA330Definition();
        def.GetVariables();
        def.OnDisplayPanelShown("test", sim);   // hands the definition its cache, as MainForm does
        return (def, sim);
    }

    private static string Display(IniA330Definition def, string key, double value)
    {
        Assert.True(def.TryGetDisplayOverride(key, value, out var text));
        return text;
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_altimeter_reads_hectopascals_and_inches_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            Assert.Equal("Altimeter 1013 hectopascals, 29.92 inches", IniA330Definition.DescribeAltimeter(1013.2, 0));
            Assert.Equal("Altimeter 1020 hectopascals, 30.12 inches", IniA330Definition.DescribeAltimeter(1020, 1));
            Assert.Equal("Altimeter standard", IniA330Definition.DescribeAltimeter(1013.2, 3));
            Assert.Equal("Altimeter not available", IniA330Definition.DescribeAltimeter(null, 0));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_trim_reads_a_decimal_point_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            Assert.Equal("1.7 up", IniA330Definition.FormatTrim(1.7));
            Assert.Equal("0.4 down", IniA330Definition.FormatTrim(-0.4));
            Assert.Equal("0.0", IniA330Definition.FormatTrim(0.01));
            var (def, _) = Shown();
            Assert.Equal("2.5 down", Display(def, "A330_TRIM", -2.5));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void Distances_to_go_read_whole_miles_and_minutes_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            Assert.Equal("Destination 123 miles, 16 minutes", IniA330Definition.DescribeFmsDistance(123.4, 450, false));
            Assert.Equal("1235 miles to top of descent, 2 hours 45 minutes", IniA330Definition.DescribeFmsDistance(1234.5, 450, true));
            Assert.Equal("Destination 99 miles", IniA330Definition.DescribeFmsDistance(99.4, 0, false));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_fuel_summary_has_no_group_or_decimal_separator_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var sim = Tests.FirstOfficer.SeededSimConnectCache.With(
                ("A330_IS_METRIC", 1), ("A330_FUEL_LO", 12345.6), ("A330_FUEL_LI", 20000.4), ("A330_FUEL_TOTAL_LB", 100000));
            var def = new IniA330Definition();
            Assert.Equal("Fuel 45359 kilograms. left outer 12346 kilograms, left inner 20000 kilograms", def.FuelSummary(sim));
            var lb = Tests.FirstOfficer.SeededSimConnectCache.With(("A330_IS_METRIC", 0), ("A330_FUEL_LO", 12345.6), ("A330_FUEL_TOTAL_LB", 100000));
            Assert.Equal("Fuel 100000 pounds. left outer 27217 pounds", def.FuelSummary(lb));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_speed_window_reads_mach_with_a_decimal_point_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var (mach, _) = Shown(("A330_M_MACH", 1));
            Assert.Equal("Mach 0.82", Display(mach, "A330_FCU_SPD", 0.82));
            var (knots, _) = Shown(("A330_M_MACH", 0));
            Assert.Equal("250 knots", Display(knots, "A330_FCU_SPD", 250.4));
            var (managed, _) = Shown(("A330_M_SPD_MANAGED", 1));
            Assert.Equal("Managed", Display(managed, "A330_FCU_SPD", 250));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_vertical_window_reads_flight_path_angle_with_a_decimal_point_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            var (fpa, _) = Shown(("A330_M_TRKFPA", 1), ("A330_FCU_FPA", -2.5));
            Assert.Equal("flight path angle -2.5 degrees", Display(fpa, "A330_FCU_VS", 0));
            var (vs, _) = Shown(("A330_M_TRKFPA", 0));
            string text = Display(vs, "A330_FCU_VS", 1500);
            Assert.Equal("1500 feet per minute", text);
            // sv-SE writes its own minus sign (U+2212) for a whole negative number: a sign a screen reader
            // says as "minus", not a separator, so only the digits and the absence of a group mark are pinned
            Assert.Equal("-1500 feet per minute", Display(vs, "A330_FCU_VS", -1500).Replace('−', '-'));
            return 0;
        });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void The_hotkey_readouts_read_mach_and_flight_path_angle_on_every_culture(string culture) =>
        _ = TestCulture.Run(culture, () =>
        {
            string Read(Hotkeys.HotkeyAction action, params (string, double)[] cache)
            {
                var sim = Tests.FirstOfficer.SeededSimConnectCache.With(cache);
                var speech = new SpeechCapture();
                Assert.True(new IniA330Definition().HandleHotkeyAction(action, sim, speech, null!, null!));
                return Assert.Single(speech.Interrupts);
            }
            Assert.StartsWith("FCU Mach 0.78", Read(Hotkeys.HotkeyAction.ReadSpeed, ("A330_M_MACH", 1), ("A330_FCU_SPD", 0.78)));
            Assert.StartsWith("FCU flight path angle -3.0 degrees",
                Read(Hotkeys.HotkeyAction.ReadFCUVerticalSpeedFPA, ("A330_M_TRKFPA", 1), ("A330_FCU_FPA", -3)));
            Assert.StartsWith("FCU flight path angle 0.5 degrees",
                Read(Hotkeys.HotkeyAction.ReadFCUVerticalSpeedFPA, ("A330_M_TRKFPA", 1), ("A330_FCU_FPA", 0.5)));
            return 0;
        });
}

/// <summary>The A330's variables: Ctrl+M rows that do something, no batch name collisions, panel refresh.</summary>
public class A330VariableReviewTests
{
    [Fact]
    public void The_speedbrake_lever_cache_has_no_ctrl_m_row()
    {
        // ProcessSimVarUpdate consumes it silently: a row would be a checkbox that mutes nothing ([VAR-9]).
        var rows = MonitorRowBuilder.Build(new IniA330Definition().GetVariables()).Select(r => r.Key).ToList();
        Assert.DoesNotContain("A330_SPDBRK", rows);
    }

    [Fact]
    public void Batched_vars_do_not_share_an_underlying_name()
    {
        // The check VarNameCollisionTests makes on every aircraft in ComboLabelCollapseTests.AllAircraft
        // ([VAR-7]); the A330 joins that list in a later task.
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

/// <summary>The landing elevation read-out is a silent display row: no lamp positions, so no spoken change and no Ctrl+M row.</summary>
public class A330LandingElevationReadOutTests
{
    [Fact]
    public void Landing_elevation_read_out_is_not_announced_and_has_no_ctrl_m_row()
    {
        var v = new IniA330Definition().GetVariables()["A330_LDG_ELEV_FT"];
        Assert.False(v.IsAnnounced);
        Assert.True(v.ExcludeFromMonitorManager);
        Assert.Empty(v.ValueDescriptions);
    }
}
