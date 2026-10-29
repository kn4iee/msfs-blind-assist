using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The FCU's four windows as the FCU shows them, from the A350's own variables (live 2026-10-26).</summary>
public sealed class A350FcuWindowsTests : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

    public A350FcuWindowsTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE");

    public void Dispose() => CultureInfo.CurrentCulture = _previous;

    private static string? Read(A350FcuWindow window, params (string Var, double Value)[] values) =>
        window.Compose(v => values.Where(p => p.Var == v).Select(p => (double?)p.Value).FirstOrDefault());

    private static A350FcuWindow W(string name) => A350FcuWindows.All.Single(w => w.Name == name);

    // The Altitude window's hint said 100 to 43000 while the entry takes 100 to 49000, the A350's own clamp (review
    // 2026-10-29): the hint names the range the entry checks.
    [Fact]
    public void The_altitude_window_hint_names_the_range_the_entry_takes()
    {
        Assert.Contains("100 to 49000", MSFSBlindAssist.Forms.A350.A350FcuValueWindow.For(MSFSBlindAssist.Hotkeys.HotkeyAction.FCUSetAltitude).Hint);
        Assert.True(MSFSBlindAssist.Aircraft.FcuValueEntry.TryAltitude(49000, out _, out _));
        Assert.False(MSFSBlindAssist.Aircraft.FcuValueEntry.TryAltitude(49100, out _, out _));
    }

    [Fact]
    public void The_four_windows_are_on_the_fcu_panel_in_cockpit_order() =>
        Assert.Equal(new[] { "Speed window", "Heading window", "Altitude window", "V/S window" },
            A350FcuWindows.All.Where(w => w.Panel == "FCU").Select(w => w.Name));

    [Fact]
    public void Speed_reads_knots_or_mach_from_one_variable()
    {
        Assert.Equal("250 knots", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 250), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0), ("INI_MANAGED_SPEED", 0), ("INI_PRESET_DIAL", 0), ("INI_PRESET_DIAL_IS_MACH", 0)));
        Assert.Equal("Mach 0.78", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 0.78), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0), ("INI_MANAGED_SPEED", 0), ("INI_PRESET_DIAL", 0), ("INI_PRESET_DIAL_IS_MACH", 0)));
    }

    /// <summary>Live in flight 2026-10-26: a turn while the speed is managed shows the preset (INI_Preset_Dial) for 45 s, the
    /// managed target staying in INI_AIRSPEED_DIAL; the WASM drops the dashes exactly then (FCU::Draw).</summary>
    [Fact]
    public void A_managed_speed_turned_shows_its_preset()
    {
        Assert.Equal("250 knots, preset", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 229), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0),
            ("INI_MANAGED_SPEED", 1), ("INI_PRESET_DIAL", 250), ("INI_PRESET_DIAL_IS_MACH", 0)));
        Assert.Equal("Mach 0.80, preset", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 0.78), ("INI_FCU_SPD_DASHED", 0), ("INI_FCU_SPD_DOT", 0),
            ("INI_MANAGED_SPEED", 1), ("INI_PRESET_DIAL", 0.80), ("INI_PRESET_DIAL_IS_MACH", 1)));
        Assert.Equal("Dashes, managed", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 229), ("INI_FCU_SPD_DASHED", 1), ("INI_FCU_SPD_DOT", 1),
            ("INI_MANAGED_SPEED", 1), ("INI_PRESET_DIAL", 250), ("INI_PRESET_DIAL_IS_MACH", 0)));
    }

    /// <summary>Live in flight 2026-10-26: a turn in NAV (INI_ROLL_MODE 1) shows the preset heading; HDG is 10.</summary>
    [Fact]
    public void A_heading_turned_in_nav_is_a_preset()
    {
        Assert.Equal("Heading 300, preset", Read(W("Heading window"), ("INI_HEADING_DIAL", 300), ("INI_TRK_DIAL", 5), ("INI_TRACK_FPA_STATE", 0),
            ("INI_FCU_HDG_DASHED", 0), ("INI_FCU_HDG_DOT", 0), ("INI_ROLL_MODE", 1)));
    }

    [Fact]
    public void A_dashed_window_reads_dashes_and_its_managed_dot()
    {
        Assert.Equal("Dashes, managed", Read(W("Speed window"), ("INI_AIRSPEED_DIAL", 250), ("INI_FCU_SPD_DASHED", 1), ("INI_FCU_SPD_DOT", 1), ("INI_MANAGED_SPEED", 0), ("INI_PRESET_DIAL", 0), ("INI_PRESET_DIAL_IS_MACH", 0)));
        Assert.Equal("Dashes", Read(W("V/S window"), ("INI_VVI_DIAL", 0), ("INI_FPA_DIAL", 0), ("INI_FCU_VS_DASHED", 1), ("INI_TRACK_FPA_STATE", 0)));
    }

    [Fact]
    public void Heading_reads_heading_or_track_by_the_fcu_mode()
    {
        Assert.Equal("Heading 218", Read(W("Heading window"), ("INI_HEADING_DIAL", 218.9), ("INI_TRK_DIAL", 5), ("INI_TRACK_FPA_STATE", 0),
            ("INI_FCU_HDG_DASHED", 0), ("INI_FCU_HDG_DOT", 0), ("INI_ROLL_MODE", 10)));
        Assert.Equal("Track 005", Read(W("Heading window"), ("INI_HEADING_DIAL", 218.9), ("INI_TRK_DIAL", 5), ("INI_TRACK_FPA_STATE", 1),
            ("INI_FCU_HDG_DASHED", 0), ("INI_FCU_HDG_DOT", 0), ("INI_ROLL_MODE", 10)));
    }

    [Fact]
    public void Altitude_reads_feet_and_its_managed_dot()
    {
        Assert.Equal("10000 feet, managed", Read(W("Altitude window"), ("INI_ALTITUDE_DIAL", 10000), ("INI_FCU_ALT_DOT", 1)));
    }

    [Fact]
    public void Vertical_reads_vs_or_fpa_by_the_fcu_mode()
    {
        Assert.Equal("V/S -1200 feet per minute", Read(W("V/S window"), ("INI_VVI_DIAL", -1200), ("INI_FPA_DIAL", -3), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 0)));
        Assert.Equal("V/S +500 feet per minute", Read(W("V/S window"), ("INI_VVI_DIAL", 500), ("INI_FPA_DIAL", 0), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 0)));
        Assert.Equal("FPA -3.0 degrees", Read(W("V/S window"), ("INI_VVI_DIAL", -1200), ("INI_FPA_DIAL", -3), ("INI_FCU_VS_DASHED", 0), ("INI_TRACK_FPA_STATE", 1)));
    }

    [Fact]
    public void Each_side_has_its_baro_on_its_efis_panel()
    {
        Assert.Equal("EFIS Captain", W("Captain baro").Panel);
        Assert.Equal("EFIS First Officer", W("First officer baro").Panel);
    }

    [Fact]
    public void The_baro_reads_qnh_in_its_unit_or_std()
    {
        var baro = W("Captain baro");
        Assert.Equal("QNH 1009 hectopascals", Read(baro, ("KOHLSMAN SETTING MB:1", 1009.125), ("XMLVAR_Baro1_Mode", 0), ("XMLVAR_Baro_Selector_HPA_1", 1), ("INI_BARO_PRESET_CPT", 0), ("INI_BARO_PRESET_CPT_SHOW", 0)));
        Assert.Equal("QNH 29.80 inches", Read(baro, ("KOHLSMAN SETTING MB:1", 1009.125), ("XMLVAR_Baro1_Mode", 0), ("XMLVAR_Baro_Selector_HPA_1", 0), ("INI_BARO_PRESET_CPT", 0), ("INI_BARO_PRESET_CPT_SHOW", 0)));
        Assert.Equal("STD", Read(baro, ("KOHLSMAN SETTING MB:1", 1013.25), ("XMLVAR_Baro1_Mode", 3), ("XMLVAR_Baro_Selector_HPA_1", 1), ("INI_BARO_PRESET_CPT", 0), ("INI_BARO_PRESET_CPT_SHOW", 0)));
    }

    /// <summary>The cockpit floors hectopascals (the baro knob's tooltip: flr '%dhPa'): a pull from a preset stepped from a
    /// fractional setting left 1015.81, which the cockpit reads 1015 (live in flight 2026-10-26).</summary>
    [Fact]
    public void Hectopascals_read_floored_as_the_cockpit_reads_them() =>
        Assert.Equal("QNH 1015 hectopascals", Read(W("Captain baro"), ("KOHLSMAN SETTING MB:1", 1015.8125), ("XMLVAR_Baro1_Mode", 0),
            ("XMLVAR_Baro_Selector_HPA_1", 1), ("INI_BARO_PRESET_CPT", 0), ("INI_BARO_PRESET_CPT_SHOW", 0)));

    /// <summary>Live in flight 2026-10-26: on STD a knob turn shows a preset QNH (INI_BARO_PRESET_*, whole hectopascals or
    /// hundredths of an inch, while INI_BARO_PRESET_*_SHOW), which the pull then sets.</summary>
    [Fact]
    public void A_std_baro_reads_its_preset_while_shown()
    {
        Assert.Equal("STD, preset 1013 hectopascals", Read(W("Captain baro"), ("KOHLSMAN SETTING MB:1", 1013.25), ("XMLVAR_Baro1_Mode", 3),
            ("XMLVAR_Baro_Selector_HPA_1", 1), ("INI_BARO_PRESET_CPT", 1013), ("INI_BARO_PRESET_CPT_SHOW", 1)));
        Assert.Equal("STD, preset 29.92 inches", Read(W("First officer baro"), ("KOHLSMAN SETTING MB:2", 1013.25), ("XMLVAR_Baro2_Mode", 3),
            ("XMLVAR_Baro_Selector_HPA_2", 0), ("INI_BARO_PRESET_FO", 2992), ("INI_BARO_PRESET_FO_SHOW", 1)));
    }

    [Fact]
    public void A_sim_variable_input_is_named_with_its_unit() =>
        Assert.Equal("millibars", A350FcuWindows.StockUnits["KOHLSMAN SETTING MB:1"]);

    /// <summary>The ISIS keeps its unit and STD to itself, so its line is the setting the sim holds, in both units.</summary>
    [Fact]
    public void The_isis_line_is_its_qnh_setting_in_both_units()
    {
        var isis = W("ISIS QNH setting");
        Assert.Equal("ISIS", isis.Panel);
        Assert.Equal("1009 hectopascals, 29.80 inches", Read(isis, ("KOHLSMAN SETTING MB:3", 1009.14)));
    }

    [Fact]
    public void An_unread_input_composes_nothing() =>
        Assert.Null(Read(W("Altitude window"), ("INI_ALTITUDE_DIAL", 10000)));
}

/// <summary>Parity task 7 (2026-10-29): the A380's Altimeter window sets the baro unit; the A350's offers each side's unit
/// button (the EFIS panel's own row, a toggle), labelled with the unit it shows, after STD and QNH.</summary>
public class A350BaroWindowUnitTests
{
    [Fact]
    public void Each_sides_unit_button_is_offered_after_std_and_qnh()
    {
        var def = new MSFSBlindAssist.Aircraft.IniBuildsA350Definition();
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        using var window = new MSFSBlindAssist.Forms.A350.A350BaroWindow(def, sim, new SpeechCapture());
        var buttons = window.Controls.OfType<System.Windows.Forms.Button>().OrderBy(b => b.TabIndex).Select(b => b.AccessibleName).ToList();
        Assert.Equal(new[] { "STD, both sides", "QNH, both sides", "Captain baro unit", "First officer baro unit", "Close" }, buttons);
    }
}
