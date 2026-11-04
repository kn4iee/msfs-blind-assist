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

/// <summary>The A350 First Officer's crossings (review 2026-11-04, tests X1: the monitor had no test): the 10,000 ft lights on the
/// A350's own rows (the nose light 0 T.O, 2 Off), the latch while the setting is off, and the transition call said "set" only once the
/// write went out.</summary>
public class A350FlightPhaseMonitorTests
{
    /// <summary>An A350 whose every write is refused, as a row the aircraft does not take.</summary>
    private sealed class RefusingA350Definition : IniBuildsA350Definition
    {
        internal override A350WriteOutcome WriteRow(string varKey, double value, SimConnectManager simConnect, ScreenReaderAnnouncer announcer) =>
            A350WriteOutcome.Refused;
    }

    private static A350ActionExecutor ExecutorFor(IniBuildsA350Definition def)
    {
        def.CanLand = _ => true;
        var executor = new A350ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith());
        executor.SetDefinition(def);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return executor;
    }

    /// <summary>The executor paces its writes 200 ms apart: waits for <paramref name="writes"/> of them, at most 3 s, then a moment for
    /// any call said after them.</summary>
    private static async Task Settle(RecordingA350Definition? def = null, int writes = 0)
    {
        for (int waited = 0; def != null && def.Writes.Length < writes && waited < 3000; waited += 20)
            await Task.Delay(20);
        await Task.Delay(300);
    }

    [Fact]
    public async Task Climbing_through_ten_thousand_turns_the_landing_and_nose_lights_off()
    {
        var def = new RecordingA350Definition();
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(def.Executor(), speech);
        monitor.Update(9_000, 2_000, false);
        monitor.Update(10_400, 2_000, false);
        await Settle(def, 2);
        Assert.Contains(("A350_LIGHTS_EXT_LANDING", 0), def.Writes);
        Assert.Contains(("A350_LIGHTS_EXT_NOSE", 2), def.Writes);   // Off
        Assert.Equal(new[] { "Above ten thousand. Landing lights off." }, speech.All);
    }

    [Fact]
    public async Task Descending_through_ten_thousand_turns_them_on_with_the_nose_light_at_takeoff()
    {
        var def = new RecordingA350Definition();
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(def.Executor(), speech);
        monitor.Update(11_000, -1_500, false);
        monitor.Update(9_600, -1_500, false);
        await Settle(def, 2);
        Assert.Contains(("A350_LIGHTS_EXT_LANDING", 1), def.Writes);
        Assert.Contains(("A350_LIGHTS_EXT_NOSE", 0), def.Writes);   // T.O
        Assert.Equal(new[] { "Below ten thousand. Landing lights on." }, speech.All);
    }

    // Inside the 300 ft band nothing fires, and a crossing made while the setting was off is not made up for once it is on again.
    [Fact]
    public async Task The_band_holds_and_the_latch_tracks_while_the_setting_is_off()
    {
        var def = new RecordingA350Definition();
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(def.Executor(), speech);
        monitor.Update(9_000, 2_000, false);
        monitor.Update(10_200, 2_000, false);   // inside the band
        await Settle();
        Assert.Empty(def.Writes);
        monitor.AutoLights10kEnabled = false;
        monitor.Update(10_400, 2_000, false);   // the crossing, the setting off
        monitor.AutoLights10kEnabled = true;
        monitor.Update(12_000, 2_000, false);
        await Settle();
        Assert.Empty(def.Writes);
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task The_transition_altitude_says_standard_set_once_the_knobs_were_pressed()
    {
        var def = new RecordingA350Definition();
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(def.Executor(), speech);
        monitor.AutoLights10kEnabled = false;
        monitor.SetThresholds(5_000, 5_000);
        monitor.Update(4_000, 2_000, false);
        monitor.Update(5_400, 2_000, false);
        await Settle(def, 2);
        Assert.Equal(new[] { "A350_EFIS_QNH_PRESSURE_SELECTOR_LEFT_PUSH", "A350_EFIS_QNH_PRESSURE_SELECTOR_RIGHT_PUSH" }, def.Writes.Select(w => w.Key).ToArray());
        Assert.Equal(new[] { A350FlightPhaseMonitor.ClimbToStdDone }, speech.All);
        Assert.Equal("Transition altitude. Altimeters set to standard.", A350FlightPhaseMonitor.ClimbToStdDone);
    }

    [Fact]
    public async Task A_refused_transition_write_hands_the_captain_the_action()
    {
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(ExecutorFor(new RefusingA350Definition()), speech);
        monitor.AutoLights10kEnabled = false;
        monitor.SetThresholds(5_000, 5_000);
        monitor.Update(6_000, -1_500, false);
        monitor.Update(4_600, -1_500, false);
        await Settle();
        Assert.Equal(new[] { "Transition level. Set both altimeters to QNH and set local pressure." }, speech.All);
    }

    [Fact]
    public async Task With_the_altimeter_setting_off_the_crossing_is_a_reminder_and_nothing_is_written()
    {
        var def = new RecordingA350Definition();
        var speech = new SpeechCapture();
        var monitor = new A350FlightPhaseMonitor(def.Executor(), speech) { AutoAltimetersEnabled = false, AutoLights10kEnabled = false };
        monitor.SetThresholds(5_000, 5_000);
        monitor.Update(4_000, 2_000, false);
        monitor.Update(5_400, 2_000, false);
        await Settle();
        Assert.Empty(def.Writes);
        Assert.Single(speech.All);
        Assert.DoesNotContain("set to standard", speech.All[0]);
    }
}
