using System.Globalization;
using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

// Plan 07 Task 6 (the owner's answer to question 15, 2026-10-31): Shift+1 to 3 read green dot, S and F as the captain's speed tape
// shows them, which on the ground is not at all ("not shown" in the test flights with the V-speeds set). The A350 publishes the three
// (FMGEC_GD_SPD, FCPC_S_SPEED, FCPC_F_SPEED): live 2026-11-02 on the stand at 155 t they read 173.6, 153.7 and 137 while the MFD's
// PERF T.O page showed 173, 153 and 137 and the tape nothing. The tape's value while it shows the symbol, else the variable's as the
// PERF page shows it, the sentence unchanged; "not shown" only when neither has one.
public class A350GroundSpeedsTests
{
    [Fact]
    public void On_the_ground_green_dot_comes_from_its_variable()
    {
        Assert.Equal("Green dot speed 173 knots", A350Hotkeys.TapeSpeed("Green dot speed", null, 173.6));
        Assert.Equal("S speed 153 knots", A350Hotkeys.TapeSpeed("S speed", null, 153.66));
        Assert.Equal("F speed 137 knots", A350Hotkeys.TapeSpeed("F speed", null, 137));
    }

    [Fact]
    public void In_flight_the_tape_still_wins()
    {
        Assert.Equal("Green dot speed 172 knots", A350Hotkeys.TapeSpeed("Green dot speed", (172.3, 0), 173.6));
        Assert.Equal("Green dot speed above the speed scale", A350Hotkeys.TapeSpeed("Green dot speed", (300, 1), 173.6));
        Assert.Equal("F speed below the speed scale", A350Hotkeys.TapeSpeed("F speed", (100, -1), 137));
    }

    [Fact]
    public void Neither_gives_not_shown()
    {
        Assert.Equal("Green dot speed not shown", A350Hotkeys.TapeSpeed("Green dot speed", null, null));
        Assert.Equal("S speed not shown", A350Hotkeys.TapeSpeed("S speed", null, 0));
        Assert.Equal("F speed not shown", A350Hotkeys.TapeSpeed("F speed", null, double.NaN));
    }

    // Through the hotkey: on the stand with the displays not running yet, the variable still answers Shift+1.
    [Fact]
    public void Shift_1_on_the_ground_says_green_dot_from_its_variable()
    {
        var def = new MSFSBlindAssist.Aircraft.IniBuildsA350Definition { Cached = (_, key) => key == "A350_IN_FMGEC_GD_SPD" ? 173.6 : null };
        var sim = new MSFSBlindAssist.SimConnect.SimConnectManager(IntPtr.Zero);
        def.Attach(sim);
        var speech = new SpeechCapture();
        Assert.True(def.HandleHotkeyAction(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadSpeedGD, sim, speech, null!, null!));
        Assert.Equal(new[] { "Green dot speed 173 knots" }, speech.All);
    }

    [Fact]
    public void The_speed_reads_the_same_under_sv_SE()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Green dot speed 173 knots", A350Hotkeys.TapeSpeed("Green dot speed", null, 173.6));
            Assert.Equal("Green dot speed 172 knots", A350Hotkeys.TapeSpeed("Green dot speed", (172.3, 0), null));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
