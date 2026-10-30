using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>An A350 definition that records every write the First Officer's executor asks of it, as the A380's does.</summary>
internal sealed class RecordingA350Definition : IniBuildsA350Definition
{
    private readonly object _gate = new();
    private readonly List<(string Key, double Value)> _writes = new();

    public (string Key, double Value)[] Writes
    {
        get { lock (_gate) return _writes.ToArray(); }
    }

    internal override A350WriteOutcome WriteRow(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!GetVariables().ContainsKey(varKey))
            return A350WriteOutcome.NoSuchRow;
        lock (_gate) _writes.Add((varKey, value));
        return A350WriteOutcome.Sent;
    }

    internal override bool HoldForFirstOfficer(string key, bool down, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        lock (_gate) _writes.Add((key, down ? 1 : 0));
        return true;
    }

    public A350ActionExecutor Executor(params (string Name, double Value)[] seed)
    {
        CanLand = _ => true;
        var variables = GetVariables();
        var executor = new A350ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed
            .Select(s => (variables.ContainsKey(s.Name) ? s.Name : KeyForInput(s.Name), s.Value)).ToArray()));
        executor.SetDefinition(this);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return executor;
    }
}

public class A350ActionExecutorTests
{
    [Fact]
    public async Task A_row_is_written_through_the_definition()
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor().Set("A350_LIGHTS_EXT_BEACON", 1));
        Assert.Equal(new[] { ("A350_LIGHTS_EXT_BEACON", 1.0) }, def.Writes);
    }

    // Never a silent success over a control nobody moved (FOA-4's lesson): the definition claims only its rows.
    [Fact]
    public async Task A_key_that_is_no_row_is_refused()
    {
        var def = new RecordingA350Definition();
        Assert.False(await def.Executor().Set("NOT_A_ROW", 1));
        Assert.Empty(def.Writes);
    }

    // The buttons flip what they switch: pressed only on a definite mismatch.
    [Theory]
    [InlineData("A350_LDG_AUTO_BRK", "INI_AUTOBRAKE_ARMED", 0, 1, true)]    // disarmed, wanted armed
    [InlineData("A350_LDG_AUTO_BRK", "INI_AUTOBRAKE_ARMED", 1, 1, false)]   // already armed: a second press would disarm
    [InlineData("A350_LDG_AUTO_BRK", "INI_AUTOBRAKE_ARMED", 1, 0, true)]
    [InlineData("A350_LDG_AUTO_BRK", "INI_AUTOBRAKE_ARMED", 0, 0, false)]
    [InlineData("A350_APU_START", "INI_APU_START_BUTTON", 0, 1, true)]
    [InlineData("A350_APU_START", "INI_APU_START_BUTTON", 1, 1, false)]
    [InlineData("A350_EFIS_BSK_2_LEFT", "INI_WXR_ON", 0, 1, true)]
    [InlineData("A350_EFIS_BSK_2_LEFT", "INI_WXR_ON", 1, 1, false)]
    [InlineData("A350_EFIS_BSK_2_RIGHT", "INI_WXR2_ON", 0, 1, true)]
    public async Task A_flipping_button_is_pressed_only_on_a_definite_mismatch(string key, string state, double now, int target, bool pressed)
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor((state, now)).Set(key, target));
        Assert.Equal(pressed, def.Writes.Contains((key, 1.0)));
        Assert.Equal(pressed ? 1 : 0, def.Writes.Length);
    }

    [Fact]
    public async Task A_flipping_button_with_its_state_unread_is_refused_and_never_pressed()
    {
        var def = new RecordingA350Definition();
        Assert.False(await def.Executor().Set("A350_LDG_AUTO_BRK", 1));
        Assert.Empty(def.Writes);
    }

    [Theory]
    [InlineData(0, 0, 1, new[] { "A350_ELEC_EXT_1", "A350_ELEC_EXT_2" })]
    [InlineData(1, 0, 1, new[] { "A350_ELEC_EXT_2" })]
    [InlineData(1, 1, 1, new string[0])]
    [InlineData(1, 1, 0, new[] { "A350_ELEC_EXT_1", "A350_ELEC_EXT_2" })]
    [InlineData(0, 1, 0, new[] { "A350_ELEC_EXT_2" })]
    public async Task External_power_presses_each_button_that_differs(double a, double b, int target, string[] pressed)
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor(("INI_GEN_EXT_A_ONLINE", a), ("INI_GEN_EXT_B_ONLINE", b)).Set(A350ActionExecutor.ExternalPowerKey, target));
        Assert.Equal(pressed, def.Writes.Select(w => w.Key).ToArray());
    }

    [Fact]
    public async Task External_power_unread_is_refused()
    {
        var def = new RecordingA350Definition();
        Assert.False(await def.Executor(("INI_GEN_EXT_A_ONLINE", 0)).Set(A350ActionExecutor.ExternalPowerKey, 1));
        Assert.Empty(def.Writes);
    }

    // Live 2026-10-30: the baro knob's PUSH is STD and its PULL QNH, each on either state and pressed again changing nothing.
    [Theory]
    [InlineData(true, "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH")]
    [InlineData(false, "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PULL", "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PULL")]
    public async Task The_baro_presses_both_knobs(bool std, string left, string right)
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor().SetBaroStd(std));
        Assert.Equal(new[] { left, right }, def.Writes.Select(w => w.Key).ToArray());
    }

    [Theory]
    [InlineData(0, 0, new[] { "A350_EFIS_QNH_UNIT_SELECTOR_LEFT", "A350_EFIS_QNH_UNIT_SELECTOR_RIGHT" })]
    [InlineData(1, 0, new[] { "A350_EFIS_QNH_UNIT_SELECTOR_RIGHT" })]
    [InlineData(1, 1, new string[0])]
    public async Task The_baro_unit_presses_only_a_selector_at_inches(double left, double right, string[] pressed)
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor(("XMLVAR_Baro_Selector_HPA_1", left), ("XMLVAR_Baro_Selector_HPA_2", right)).Set(A350ActionExecutor.BaroUnitHectopascalsKey, 1));
        Assert.Equal(pressed, def.Writes.Select(w => w.Key).ToArray());
    }

    [Fact]
    public async Task The_seat_belt_automation_sets_on_or_auto_never_off()
    {
        var def = new RecordingA350Definition();
        var executor = def.Executor();
        await executor.SetSeatbeltSign(true);
        await executor.SetSeatbeltSign(false);
        Assert.Equal(new[] { ("A350_SIGNS_SEAT_BELTS", 0.0), ("A350_SIGNS_SEAT_BELTS", 1.0) }, def.Writes);
    }

    [Fact]
    public async Task A_held_test_presses_then_lets_go()
    {
        var def = new RecordingA350Definition();
        Assert.True(await def.Executor().HoldAsync("A350_ECAM_TO_CONFIG", 10));
        Assert.Equal(new[] { ("A350_ECAM_TO_CONFIG", 1.0), ("A350_ECAM_TO_CONFIG", 0.0) }, def.Writes);
    }

    // The window closing mid-hold lets go at once (HeldPresses), not on the hold's own timer.
    [Fact]
    public async Task A_window_closing_mid_hold_lets_go_at_once()
    {
        var def = new RecordingA350Definition();
        var executor = def.Executor();
        var hold = executor.HoldAsync("A350_FIRE_TEST", 60_000);
        for (int n = 0; n < 200 && def.Writes.Length == 0; n++) await Task.Delay(10);
        Assert.Single(def.Writes);
        executor.ReleaseHeldPresses();
        Assert.Equal(2, def.Writes.Length);
    }

    [Fact]
    public void A_detached_executor_is_not_available()
    {
        var executor = new A350ActionExecutor();
        Assert.False(executor.IsAvailable);
    }
}
