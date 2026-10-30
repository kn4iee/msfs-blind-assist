using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The approach minimums call-out, as the A380's (A380Minimums): PERF APPR's BARO and RADIO, read from the active flight
/// plan in the display reader's copy (live 2026-10-30: +748 and +752, 0 not set, -1 NO DH; entering one clears the other,
/// CLR is refused on both; INI_FMS_MDA stayed 0), said as set, changed or cleared, each behind its own Ctrl+M row. The
/// FMS's own clear after a flight, on the ground, is silent; a clear in the air (an approach change) is said.
/// </summary>
public class A350MinimumsTests
{
    private static void Fms(IniBuildsA350Definition def, SpeechCapture speech, int baro, int radio, int reads = 2)
    {
        for (int i = 0; i < reads; i++)
            def.ObserveMinimums(new A350Minimums(baro, radio), speech);
    }

    private static IniBuildsA350Definition Def(params string[] muted) => new() { IsMuted = key => muted.Contains(key) };

    [Fact]
    public void The_minimums_are_read_from_the_active_flight_plan_in_a_copy()
    {
        var facts = new A350MinimumsFacts(2000, 748, 752);
        var mem = new byte[8192];
        BitConverter.GetBytes(300).CopyTo(mem, 2748);
        BitConverter.GetBytes(-1).CopyTo(mem, 2752);
        var copy = new A350MemoryCopy();
        copy.Set(new[] { (0u, mem.Length) }, mem);

        Assert.Equal(new A350Minimums(300, -1), A350Minimums.Read(copy, facts));
        Assert.Null(A350Minimums.Read(copy, facts with { FlightPlan = 8000 }));   // outside the copy
        BitConverter.GetBytes(-7).CopyTo(mem, 2752);
        Assert.Null(A350Minimums.Read(copy, facts));   // below NO DH: a torn copy
        BitConverter.GetBytes(0).CopyTo(mem, 2752);
        BitConverter.GetBytes(1_000_000).CopyTo(mem, 2748);
        Assert.Null(A350Minimums.Read(copy, facts));
    }

    [Fact]
    public void Each_minimum_entered_changed_or_cleared_is_said_as_the_a380_says_it()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var speech = new SpeechCapture();
            var def = Def();
            Fms(def, speech, 0, 0);       // the baseline
            Fms(def, speech, 300, 0);
            Fms(def, speech, 1520, 0);
            Fms(def, speech, 0, 100);     // RADIO entered: the FMS clears BARO
            Fms(def, speech, 0, -1);
            Assert.Equal(new[] { "Baro minimum 300 feet", "Baro minimum 1520 feet", "Baro minimum cleared",
                "Decision height 100 feet", "No DH" }, speech.All);
            Assert.Empty(speech.Interrupts);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void A_single_read_is_never_believed_and_the_first_agreed_is_silent()
    {
        var speech = new SpeechCapture();
        var def = Def();
        Fms(def, speech, 300, 0);   // a flight loaded with a minimum set
        Fms(def, speech, 520, 0, reads: 1);
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Each_minimum_is_muted_by_its_own_row()
    {
        var speech = new SpeechCapture();
        var def = Def(A350MinimumsCallout.BaroKey);
        Fms(def, speech, 0, 0);
        Fms(def, speech, 300, 0);
        Fms(def, speech, 0, 100);
        Assert.Equal(new[] { "Decision height 100 feet" }, speech.All);

        var rows = MSFSBlindAssist.Forms.A350.A350MonitorManagerForm.BuildRows(def.GetVariables());
        Assert.Equal("Baro minimum", Assert.Single(rows, r => r.Key == A350MinimumsCallout.BaroKey).Label);
        Assert.Equal("Decision height", Assert.Single(rows, r => r.Key == A350MinimumsCallout.RadioKey).Label);
    }

    [Fact]
    public void A_clear_in_the_air_is_said_and_the_fms_reset_after_landing_is_not()
    {
        var speech = new SpeechCapture();
        var def = Def();
        Fms(def, speech, 0, 0);
        Fms(def, speech, 520, 0);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Fms(def, speech, 0, 0);       // the approach changed in the air
        Fms(def, speech, 0, 200);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 1, speech);   // landed
        Fms(def, speech, 0, 0);       // the FMS's reset
        Assert.Equal(new[] { "Baro minimum 520 feet", "Baro minimum cleared", "Decision height 200 feet" }, speech.All);

        Fms(def, speech, 400, 0);     // the next flight's, at the gate
        Fms(def, speech, 0, 0);       // and an approach change there: news
        Assert.Equal(new[] { "Baro minimum 520 feet", "Baro minimum cleared", "Decision height 200 feet",
            "Baro minimum 400 feet", "Baro minimum cleared" }, speech.All);
    }

    [Fact]
    public void A_flight_loaded_takes_its_minimums_as_a_new_baseline()
    {
        var speech = new SpeechCapture();
        var def = Def();
        Fms(def, speech, 0, 0);
        Fms(def, speech, 520, 0);
        speech.All.Clear();
        def.OnSimContextReset();
        Fms(def, speech, 0, 300);
        Assert.Empty(speech.All);
    }
}
