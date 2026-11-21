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
