using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The A350's take-off roll call-outs ("V1", "Rotate", "V2"), as the A380's (A380TakeoffCalloutsTests): the shared
/// TakeoffVSpeedCallouts machine fed by a per-frame airspeed and the A350 FMS's own V-speeds (INI_V1, INI_VR, INI_V2 in
/// its var table, 0 before PERF has them), each call muted by its speed's Ctrl+M row, the arm (never the speeds)
/// dropped on a flight load and a reconnect ([TKO-5]).
/// </summary>
public class A350TakeoffCalloutsTests
{
    private static Dictionary<string, SimVarDefinition> Vars => new IniBuildsA350Definition().GetVariables();

    /// <summary>A definition with the FMS speeds entered and the aircraft parked: armed for a roll.</summary>
    private static IniBuildsA350Definition Parked(SpeechCapture speech, double v1 = 142, double vr = 143, double v2 = 148,
        params string[] muted)
    {
        var def = new IniBuildsA350Definition { IsMuted = key => muted.Contains(key) };
        def.ProcessSimVarUpdate(A350TakeoffCallouts.V1Key, v1, speech);
        def.ProcessSimVarUpdate(A350TakeoffCallouts.VrKey, vr, speech);
        def.ProcessSimVarUpdate(A350TakeoffCallouts.V2Key, v2, speech);
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

    [Theory]
    [InlineData("V1", "INI_V1", "V1")]
    [InlineData("Rotate", "INI_VR", "VR")]
    [InlineData("V2", "INI_V2", "V2")]
    public void Each_callout_is_muted_by_its_v_speed_row(string callout, string name, string spoken)
    {
        string key = A350TakeoffCallouts.Keys.MuteKeyFor(callout);
        var row = Vars[key];
        Assert.Equal(name, row.Name);   // the A350 FMS's own
        // Read raw, as the A350's other INI_ speeds: a velocity unit would convert an L:var its writer stored as a number.
        Assert.Equal((SimVarType.LVar, "number", UpdateFrequency.Continuous), (row.Type, row.Units, row.UpdateFrequency));
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);   // said as the speed is entered, its own Ctrl+M row
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var sim = new SimConnectManager(IntPtr.Zero);
            Assert.Equal($"{spoken}: 142 knots", sim.FormatVariableValue(key, row, 142));
            Assert.Equal($"{spoken}: not set", sim.FormatVariableValue(key, row, 0));   // 0 before PERF has it
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
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
    public void The_v_speeds_still_reach_the_monitor_and_the_feed_is_consumed()
    {
        var def = new IniBuildsA350Definition();
        Assert.False(def.ProcessSimVarUpdate(A350TakeoffCallouts.V1Key, 142, new SpeechCapture()));
        Assert.True(def.ProcessSimVarUpdate(A350TakeoffCallouts.IasKey, 5, new SpeechCapture()));
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
    public void A_muted_v1_row_leaves_rotate_and_v2()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, muted: A350TakeoffCallouts.V1Key);

        Roll(def, speech, 100, 142, 143);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 149);

        Assert.Equal(new[] { "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void Speeds_not_yet_entered_leave_the_roll_silent()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, v1: 0, vr: 0, v2: 0);

        Roll(def, speech, 60, 143, 149, 156);

        Assert.Empty(speech.Interrupts);
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
