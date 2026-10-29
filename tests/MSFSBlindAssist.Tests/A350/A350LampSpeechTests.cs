using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>When the A350's light changes are spoken: one sentence at a batch end once due, netted per light, with the
/// timings measured on the iniBuilds A300, whose lamps are made of the same light power variables.</summary>
public class A350LampSpeechTests
{
    private static A350LampChange On(string name) => new(name, true);
    private static A350LampChange Off(string name) => new(name, false);

    [Fact]
    public void A_light_coming_on_is_spoken_after_its_gather_period()
    {
        var speech = new A350LampSpeech();
        speech.Add(On("APU start available light"), 0);
        Assert.Null(speech.Flush(A350LampSpeech.GatherMs - 1));
        Assert.Equal("APU start available light on", speech.Flush(A350LampSpeech.GatherMs));
        Assert.Null(speech.Flush(A350LampSpeech.GatherMs + 1000));
    }

    [Fact]
    public void A_light_going_dark_waits_out_a_flash()
    {
        var speech = new A350LampSpeech();
        speech.Add(Off("Captain autoland light"), 0);
        speech.Add(On("Captain autoland light"), 600);   // lit again inside the hold: a flash, nothing said
        Assert.Null(speech.Flush(5000));
        speech.Add(Off("Captain autoland light"), 6000);
        Assert.Null(speech.Flush(6000 + A350LampSpeech.OffHoldMs - 1));
        Assert.Equal("Captain autoland light off", speech.Flush(6000 + A350LampSpeech.OffHoldMs));
    }

    [Fact]
    public void A_power_change_speaks_each_light_once_after_it_settles()
    {
        var speech = new A350LampSpeech();
        speech.NotePowerChange(0);
        speech.Add(On("BAT 1 fault light"), 100);
        speech.Add(Off("BAT 1 fault light"), 900);      // the power flag arrived out of order: on then off nets out
        speech.Add(On("EXT 1 available light"), 1200);
        Assert.Null(speech.Flush(A350LampSpeech.SettleMs - 1));
        Assert.Equal("EXT 1 available light on", speech.Flush(3000));
    }

    [Fact]
    public void A_silent_off_is_held_through_a_flash_then_dropped_even_in_a_power_settle()
    {
        // A master light: MASTER WARN flashes, and its going out is the pilot's own press.
        var speech = new A350LampSpeech();
        speech.Add(On("Master warning"), 0);
        speech.Add(Off("Master warning"), 500, silentOff: true);   // caught dark before the gather ended
        speech.Add(On("Master warning"), 1000);                    // a flash: no second "on"
        Assert.Equal("Master warning on", speech.Flush(A350LampSpeech.GatherMs));
        speech.NotePowerChange(2000);
        speech.Add(Off("Master warning"), 2100, silentOff: true);  // an off in a power settle nets unheld otherwise
        Assert.Null(speech.Flush(2100 + A350LampSpeech.SettleMs + A350LampSpeech.OffHoldMs));
    }

    // The owner (2026-10-27): several lights changing at once spoke "2 lights on: ..."; each light is spoken on its own,
    // one at a time, as every other aircraft's lights are.
    [Fact]
    public void Several_changes_read_one_light_at_a_time_lights_on_first()
    {
        var speech = new A350LampSpeech();
        speech.NotePowerChange(0);
        speech.Add(Off("Pack 1 fault light"), 10);
        speech.Add(On("GEN 1A fault light"), 20);
        speech.Add(On("GEN 1B fault light"), 30);
        Assert.Equal("GEN 1A fault light on. GEN 1B fault light on. Pack 1 fault light off", speech.Flush(5000));
    }

    [Fact]
    public void A_forgotten_light_drops_its_waiting_change()
    {
        var speech = new A350LampSpeech();
        speech.Add(On("A/BRK light"), 0);
        speech.Forget("A/BRK light");
        Assert.Null(speech.Flush(5000));
    }

}

/// <summary>The A350's lights as the cockpit shows them, decided from the deliveries themselves: a lamp's first decided
/// value is a silent baseline; a change of its rule or its bus's light power turns it on or off.</summary>
public class A350LampBoardTests
{
    private static readonly A350Placement Placement = A350PanelLayout.Place(A350ControlMap.Load());
    private static readonly IReadOnlyList<A350Lamp> Lamps =
        A350PanelLamps.Resolve(A350ControlMap.Load(), Placement, new[] { "Overhead", "Glareshield", "Instrument" });

    private const string BatFault = "A350_LT_ELEC_BAT_1_SEQ1_LIGHT";

    [Fact]
    public void A_lamps_first_decided_value_is_a_silent_baseline_and_a_change_is_reported()
    {
        var board = new A350LampBoard(Lamps);
        var lamp = Lamps.Single(l => l.Key == BatFault);
        foreach (var key in lamp.InputKeys)
            Assert.Empty(board.Update(key, key == BatFault ? 0 : 1));
        var change = Assert.Single(board.Update(BatFault, 1));
        Assert.Equal((lamp.Name, true), (change.Lamp.Name, change.On));
    }

    [Fact]
    public void A_dark_bus_darkens_its_lit_lamps_and_a_fault_on_a_dark_bus_says_nothing()
    {
        var board = new A350LampBoard(Lamps);
        var lamp = Lamps.Single(l => l.Key == BatFault);
        string power = lamp.InputKeys.Single(k => k is A350PanelLamps.AcPowerKey or A350PanelLamps.DcPowerKey);
        foreach (var key in lamp.InputKeys)
            board.Update(key, 1);                                   // lit, powered
        Assert.True(board.PowerFlips(power, 0));
        Assert.Contains(board.Update(power, 0), c => c.Lamp.Key == BatFault && !c.On);
        Assert.Empty(board.Update(BatFault, 0));                    // the fault clears on a dark bus: nothing to see
    }

    [Fact]
    public void A_seed_fills_only_unread_inputs_and_baselines_silently()
    {
        var board = new A350LampBoard(Lamps);
        var lamp = Lamps.Single(l => l.Key == BatFault);
        Assert.True(board.Seed(BatFault, 1));
        foreach (var key in lamp.InputKeys.Where(k => k != BatFault))
            board.Seed(key, 1);
        Assert.False(board.Seed(BatFault, 0));                      // already read: a seed never overwrites
        var change = Assert.Single(board.Update(BatFault, 0));      // the seeded baseline makes this a change
        Assert.False(change.On);
    }

    [Fact]
    public void A_light_power_flag_is_a_shared_input()
    {
        var board = new A350LampBoard(Lamps);
        Assert.Contains(A350PanelLamps.DcPowerKey, board.SharedInputKeys);
        Assert.True(board.Handles(BatFault));
        Assert.False(board.Handles("A350_ELEC_BAT_1"));
    }
}
