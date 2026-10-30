using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.FirstOfficer.A350;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// What the First Officer reports is what the aircraft was sent, on the real definition (review 3, 2026-10-30): a panel row's
/// refusal is true to MainForm (the row is claimed, the refusal spoken), and under the FO's Suppressed wrap it was a step done, a
/// line ticked and a group latched over a control nobody moved (FOA-15, FOA-4's lesson).
/// </summary>
public class A350FoWritePathTests
{
    private sealed record Rig(IniBuildsA350Definition Def, A350ActionExecutor Executor, List<string> Sent);

    private static Rig Real(bool canLand = true, params (string Name, double Value)[] seed)
    {
        var sent = new List<string>();
        var def = new IniBuildsA350Definition
        {
            CanLand = _ => canLand,
            Send = (_, rpn) => sent.Add(rpn),
            ReRead = (_, _) => { },
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
            Delay = _ => Task.CompletedTask,
        };
        var variables = def.GetVariables();
        var executor = new A350ActionExecutor();
        executor.SetSimConnect(SeededSimConnectCache.ConnectedWith(seed
            .Select(s => (variables.ContainsKey(s.Name) ? s.Name : def.KeyForInput(s.Name), s.Value)).ToArray()));
        executor.SetDefinition(def);
        executor.SetAnnouncer(new GatedSpeechCapture());
        return new Rig(def, executor, sent);
    }

    [Fact]
    public async Task A_row_sent_reports_success()
    {
        var rig = Real(seed: ("A350_LIGHTS_EXT_BEACON", 0));
        Assert.True(await rig.Executor.Set("A350_LIGHTS_EXT_BEACON", 1));
        Assert.NotEmpty(rig.Sent);
    }

    [Fact]
    public async Task A_row_already_there_reports_success_and_sends_nothing()
    {
        var rig = Real(seed: ("A350_LIGHTS_EXT_BEACON", 1));
        Assert.True(await rig.Executor.Set("A350_LIGHTS_EXT_BEACON", 1));
        Assert.Empty(rig.Sent);
    }

    // No MobiFlight module, or a probe that said no: every step spoke, every line ticked, the flow latched.
    [Fact]
    public async Task A_write_that_cannot_land_fails_and_sends_nothing()
    {
        var rig = Real(canLand: false, seed: ("A350_LIGHTS_EXT_BEACON", 0));
        Assert.False(await rig.Executor.Set("A350_LIGHTS_EXT_BEACON", 1));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public void An_executor_whose_writes_cannot_land_is_not_available()
    {
        Assert.False(Real(canLand: false).Executor.IsAvailable);
        Assert.True(Real().Executor.IsAvailable);
    }

    // A toggle's Set flips: unread, its plan is refused, and that read as a step done.
    [Fact]
    public async Task A_toggle_row_unread_fails_and_sends_nothing()
    {
        var rig = Real();
        Assert.False(await rig.Executor.Set("A350_AIR_PACK_1", 1));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task A_baro_that_cannot_land_fails()
    {
        var rig = Real(canLand: false);
        Assert.False(await rig.Executor.SetBaroStd(true));
        Assert.Empty(rig.Sent);
    }

    [Fact]
    public async Task A_held_test_that_cannot_land_fails_and_never_lets_go()
    {
        var rig = Real(canLand: false, seed: ("INI_FIRE_TEST", 0));
        Assert.False(await rig.Executor.HoldAsync("A350_FIRE_TEST", 10));
        Assert.Empty(rig.Sent);
    }

    // The release is an explicit release: a second press through the panel's path reads the cockpit's record, so with the pilot
    // already holding it, or the record late, it pressed down instead and the fire test stayed held with no timer.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_held_test_goes_down_then_up_whatever_the_cockpit_reads(double held)
    {
        var rig = Real(seed: ("INI_FIRE_TEST", held));
        Assert.True(await rig.Executor.HoldAsync("A350_FIRE_TEST", 10));
        Assert.Equal(2, rig.Sent.Count);
        Assert.StartsWith("2 ", rig.Sent[0]);
        Assert.StartsWith("0 ", rig.Sent[1]);
    }

    // The same on the record the panel's own press keeps: a press after the test lets nothing go by accident.
    [Fact]
    public async Task Both_held_tests_the_flows_use_are_latched_buttons()
    {
        foreach (var row in new[] { "A350_FIRE_TEST", "A350_ECAM_TO_CONFIG" })
        {
            var rig = Real();
            Assert.True(await rig.Executor.HoldAsync(row, 10));
            Assert.Equal(2, rig.Sent.Count);
        }
    }

    // Tick, untick, tick inside one batch period: judged from a stale cache the second tick pressed again and A/BRK ended disarmed.
    [Fact]
    public async Task A_flipping_button_is_judged_from_what_was_just_commanded()
    {
        var def = new RecordingA350Definition();
        var executor = def.Executor(("INI_AUTOBRAKE_ARMED", 0));
        Assert.True(await executor.Set("A350_LDG_AUTO_BRK", 1));
        Assert.Single(def.Writes);
        Assert.True(await executor.Set("A350_LDG_AUTO_BRK", 1));   // the cache still says 0
        Assert.Single(def.Writes);
        Assert.True(await executor.Set("A350_LDG_AUTO_BRK", 0));   // it does say 0, but a press is owed
        Assert.Equal(2, def.Writes.Length);
    }
}
