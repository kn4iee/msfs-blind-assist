using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The A350's take-off roll call-outs ("V1", "Rotate", "V2"), as the A380's (A380TakeoffCalloutsTests): the shared
/// TakeoffVSpeedCallouts machine fed by a per-frame airspeed and the FMS's own V-speeds, each call muted by its speed's
/// Ctrl+M row, the arm (never the speeds) dropped on a flight load and a reconnect ([TKO-5]). The speeds are the active
/// flight plan's, read from the display reader's memory copy (sim 2026-10-30: INI_V1, INI_VR and INI_V2 stay 0 with
/// the speeds on PERF, the PFD and in FlightPlan; nothing in 1.2.7 writes them), believed when two reads agree.
/// </summary>
public class A350TakeoffCalloutsTests
{
    private static Dictionary<string, SimVarDefinition> Vars => new IniBuildsA350Definition().GetVariables();

    /// <summary>The FMS read <paramref name="reads"/> times, as the display reader's ticks deliver it.</summary>
    private static void Fms(IniBuildsA350Definition def, SpeechCapture speech, int v1, int vr, int v2, int reads = 2)
    {
        for (int i = 0; i < reads; i++)
            def.ObserveTakeoffSpeeds(new A350TakeoffSpeeds(v1, vr, v2), speech);
    }

    /// <summary>A definition whose FMS had nothing, then the speeds entered on PERF, the aircraft parked: armed for a roll.</summary>
    private static IniBuildsA350Definition Parked(SpeechCapture speech, int v1 = 142, int vr = 143, int v2 = 148,
        params string[] muted)
    {
        var def = new IniBuildsA350Definition { IsMuted = key => muted.Contains(key) };
        Fms(def, speech, 0, 0, 0);
        Fms(def, speech, v1, vr, v2);
        def.ProcessSimVarUpdate(A350TakeoffCallouts.IasKey, 5, speech);
        return def;
    }

    private static void Roll(IniBuildsA350Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples)
            def.ProcessSimVarUpdate(A350TakeoffCallouts.IasKey, ias, speech);
    }

    [Fact]
    public void The_airspeed_feed_is_per_frame_silent_and_hidden_from_ctrl_m()
    {
        var ias = Vars[A350TakeoffCallouts.IasKey];
        Assert.Equal(("AIRSPEED INDICATED", "knots", SimVarType.SimVar, UpdateFrequency.Continuous),
            (ias.Name, ias.Units, ias.Type, ias.UpdateFrequency));
        Assert.True(ias.IsAnnounced);
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
    }

    // The FMS's own speeds, where FlightPlan::getV1Speed() and its siblings read them; a read the copy cannot answer, or
    // one no speed can be (a copy torn across frames), is no read.
    [Fact]
    public void The_speeds_are_read_from_the_active_flight_plan_in_a_copy()
    {
        var facts = new A350TakeoffSpeedFacts(2000, 964, 968, 972);
        var mem = new byte[8192];
        BitConverter.GetBytes(142).CopyTo(mem, 2964);
        BitConverter.GetBytes(143).CopyTo(mem, 2968);
        BitConverter.GetBytes(148).CopyTo(mem, 2972);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);

        Assert.Equal(new A350TakeoffSpeeds(142, 143, 148), A350TakeoffSpeeds.Read(copy, facts));
        Assert.Null(A350TakeoffSpeeds.Read(copy, facts with { FlightPlan = 8000 }));   // outside the copy
        BitConverter.GetBytes(-3).CopyTo(mem, 2968);
        Assert.Null(A350TakeoffSpeeds.Read(copy, facts));
        BitConverter.GetBytes(1_000_000).CopyTo(mem, 2968);
        Assert.Null(A350TakeoffSpeeds.Read(copy, facts));
    }

    [Theory]
    [InlineData("V1", "V1")]
    [InlineData("Rotate", "VR")]
    [InlineData("V2", "V2")]
    public void Each_callout_is_muted_by_its_v_speed_row(string callout, string label)
    {
        string key = A350TakeoffCallouts.Keys.MuteKeyFor(callout);
        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(Vars);
        Assert.Equal(label, Assert.Single(rows, r => r.Key == key).Label);
        Assert.False(Vars.ContainsKey(key));   // no sim variable: the dead INI_ speeds are never polled
    }

    [Fact]
    public void The_dead_ini_speeds_are_not_read()
    {
        Assert.DoesNotContain(Vars.Values, v => v.Name is "INI_V1" or "INI_VR" or "INI_V2");
    }

    [Fact]
    public void The_speeds_entered_are_said_once_two_reads_agree()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var speech = new SpeechCapture();
            var def = new IniBuildsA350Definition { IsMuted = _ => false };
            Fms(def, speech, 0, 0, 0);
            Fms(def, speech, 142, 143, 148, reads: 1);
            Assert.Empty(speech.All);   // one read may be a copy torn across frames

            Fms(def, speech, 142, 143, 148, reads: 1);
            Fms(def, speech, 142, 143, 148);
            Assert.Equal(new[] { "V1: 142 knots", "VR: 143 knots", "V2: 148 knots" }, speech.All);
            Assert.Empty(speech.Interrupts);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void The_first_agreed_read_is_a_silent_baseline_that_still_arms_the_roll()
    {
        var speech = new SpeechCapture();
        var def = new IniBuildsA350Definition { IsMuted = _ => false };
        Fms(def, speech, 142, 143, 148);   // a flight loaded with its speeds entered
        Assert.Empty(speech.All);

        Roll(def, speech, 5, 120, 142);
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }

    [Fact]
    public void A_speed_changed_alone_is_said_alone()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        speech.All.Clear();

        Fms(def, speech, 145, 143, 148);

        Assert.Equal(new[] { "V1: 145 knots" }, speech.All);
    }

    // [A320-10]'s A350 form: a clear before take-off (a runway change) is said; once the aircraft has flown since the
    // speeds were set, a clear is consumed silently (the FMS's reset after landing clears them as it goes to PREFLIGHT,
    // so a flight phase cannot tell the two apart: resetFMC() sets INI_flight_phase 1 as it clears them).
    [Fact]
    public void A_clear_before_take_off_is_said_and_one_after_the_flight_is_not()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        speech.All.Clear();

        Fms(def, speech, 0, 0, 0);
        Assert.Equal(new[] { "V1: not set", "VR: not set", "V2: not set" }, speech.All);

        Fms(def, speech, 142, 143, 148);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);
        speech.All.Clear();
        Fms(def, speech, 0, 0, 0);
        Assert.Empty(speech.All);

        Fms(def, speech, 150, 151, 156);   // the next flight's
        Assert.Equal(new[] { "V1: 150 knots", "VR: 151 knots", "V2: 156 knots" }, speech.All);
    }

    // A flight loaded in the air delivers SIM_ON_GROUND only as its first snapshot, which the definition never sees; the
    // cached value at the read says it flew.
    [Fact]
    public void A_flight_loaded_in_the_air_clears_its_speeds_silently_after_landing()
    {
        var speech = new SpeechCapture();
        var cache = new Dictionary<string, double> { ["SIM_ON_GROUND"] = 0 };
        var def = new IniBuildsA350Definition
        {
            IsMuted = _ => false,
            Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null,
        };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Fms(def, speech, 142, 143, 148);
        cache["SIM_ON_GROUND"] = 1;   // landed

        Fms(def, speech, 0, 0, 0);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_muted_row_mutes_its_entry_and_its_call()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, muted: A350TakeoffCallouts.V1Key);
        Assert.Equal(new[] { "VR: 143 knots", "V2: 148 knots" }, speech.All);

        Roll(def, speech, 100, 142, 143);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 149);

        Assert.Equal(new[] { "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void A_roll_calls_v1_rotate_and_v2_in_order_as_interruptions()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        Roll(def, speech, 60, 120, 141, 142);
        Roll(def, speech, 143);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 149, 160);

        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_feed_is_consumed()
    {
        Assert.True(new IniBuildsA350Definition().ProcessSimVarUpdate(A350TakeoffCallouts.IasKey, 5, new SpeechCapture()));
    }

    [Fact]
    public void V1_equal_to_vr_is_one_utterance()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, v1: 150, vr: 150, v2: 158);

        Roll(def, speech, 100, 149, 151);

        Assert.Equal(new[] { "V1, Rotate" }, speech.Interrupts);
    }

    [Fact]
    public void Speeds_not_yet_entered_leave_the_roll_silent()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, v1: 0, vr: 0, v2: 0);

        Roll(def, speech, 60, 143, 149, 156);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_context_reset_drops_the_arm_so_a_flight_loaded_into_the_cruise_calls_nothing()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        def.OnSimContextReset();
        Roll(def, speech, 280);

        Assert.Empty(speech.Interrupts);
    }

    // A flight loaded: its FMS's speeds are its baseline, never said as if entered.
    [Fact]
    public void A_flight_loaded_takes_its_speeds_as_a_new_baseline()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);
        speech.All.Clear();

        def.OnSimContextReset();
        Fms(def, speech, 150, 151, 156);
        Assert.Empty(speech.All);

        Roll(def, speech, 5, 150);
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }

    [Fact]
    public void A_reconnect_drops_the_arm_but_keeps_the_speeds()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        def.ResetAnnouncementBaselines();
        Roll(def, speech, 150);
        Assert.Empty(speech.Interrupts);

        Roll(def, speech, 5, 142);
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }

    [Fact]
    public void The_feed_runs_on_the_ground_or_armed_only()
    {
        var def = new IniBuildsA350Definition();
        Assert.Equal(A350TakeoffCallouts.IasKey, def.TakeoffCalloutFeedKey);
        Assert.True(def.TakeoffCalloutFeedNeeded);   // on the ground until told otherwise
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, new SpeechCapture());
        Assert.False(def.TakeoffCalloutFeedNeeded);  // airborne and never armed
    }
}
