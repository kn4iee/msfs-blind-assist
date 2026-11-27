using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The generated map and the panels (plan 04): the map, the input values, the autobrake and trim words.</summary>
public class A330ControlMapTests
{
    private static readonly IniA330Definition Def = new();
    private static readonly A330ControlMap Map = A330ControlMap.Embedded;

    [Fact]
    public void The_generated_map_is_embedded()
        => Assert.True(Map.Controls.Count > 200, $"only {Map.Controls.Count} controls in the embedded map");

    [Fact]
    public void Every_map_control_has_an_input_event_and_every_select_has_two_positions()
    {
        var bad = Map.Controls.Where(c => c.Kind != "status" && string.IsNullOrEmpty(c.Event)).Select(c => c.Key)
            .Concat(Map.Controls.Where(c => c.Kind == "select" && (c.Positions?.Count ?? 0) < 2).Select(c => c.Key + " (positions)"))
            .ToList();
        Assert.True(bad.Count == 0, string.Join(", ", bad));
    }

    [Fact]
    public void Momentary_keys_use_their_push_action_and_switches_set_a_value()
    {
        Assert.Equal("push", Map.Controls.Single(c => c.Key == "A330_ECAM_HYD").Mode);
        Assert.Equal("set", Map.Controls.Single(c => c.Key == "A330_STROBE").Mode);
    }

    [Theory]
    // a push-button: read 1 (on) is input 0 — measured on PACK 1
    [InlineData("A330_PACK1", 1, 0)]
    [InlineData("A330_PACK1", 0, 1)]
    // three-position switch numbered from the top: strobe ON is 0
    [InlineData("A330_STROBE", 0, 0)]
    [InlineData("A330_STROBE", 2, 2)]
    public void Picks_translate_to_the_measured_input_value(string key, double read, double input)
        => Assert.Equal(input, A330ControlMap.InputFor(Map.Controls.Single(c => c.Key == key), read));

    [Fact]
    public void Read_kinds_parse()
    {
        Assert.Equal(A330ControlMap.ReadKind.InputEvent, A330ControlMap.ParseReadKind("B:AIRLINER_X").kind);
        Assert.Equal(A330ControlMap.ReadKind.LVar, A330ControlMap.ParseReadKind("INI_X").kind);
        var sv = A330ControlMap.ParseReadKind("A:GENERAL ENG FUEL VALVE:1, bool");
        Assert.Equal((A330ControlMap.ReadKind.SimVar, "GENERAL ENG FUEL VALVE:1", "bool"), sv);
    }

    [Fact]
    public void Predictive_windshear_position_0_is_auto()
    {
        // measured in flight 2026-11-22: the switch reading 1 showed the E/WD memo PRED W/S OFF
        var pws = Map.Controls.Single(c => c.Key == "A330_WX_PWS");
        Assert.Equal("Auto", pws.Positions!.Single(p => p.Value == 0).Label);
        Assert.Equal("Off", pws.Positions!.Single(p => p.Value == 1).Label);
    }

    [Fact]
    public void Autobrake_levels_map_to_their_buttons()
    {
        Assert.Equal("AIRLINER_AUTOBRK_LO", IniA330Definition.AutobrakeEvent(1));
        Assert.Equal("AIRLINER_AUTOBRK_HI", IniA330Definition.AutobrakeEvent(3));
        Assert.Null(IniA330Definition.AutobrakeEvent(0));
    }

    [Theory]
    [InlineData(1.7, "1.7 up")]
    [InlineData(-0.4, "0.4 down")]
    [InlineData(0.01, "0.0")]
    public void Trim_reads_up_and_down(double deg, string text) => Assert.Equal(text, IniA330Definition.FormatTrim(deg));
}

/// <summary>The A330's panel words read the same on a comma-decimal PC.</summary>
public class A330ControlMapCultureTests
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
