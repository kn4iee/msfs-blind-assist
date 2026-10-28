using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 10, task 4: the definition's silent, awaitable write path for the First Officer
/// (<c>FoSetAsync</c>, <c>FoPressAsync</c>, <c>FoWriteNow</c>). It reuses the panel's own decisions
/// and sim access (<c>WriteIo</c>, here a <see cref="Bbs757FakeIo"/>), returns true only when the
/// control reads its target afterwards, refuses when a write cannot land, and speaks nothing.
/// </summary>
public class Bbs757FoWriteTests
{
    private const string Press = "(L:X,bool) ! (>L:X,bool)";
    private const string ButtonPress = "1 (>L:B)";

    private static Bbs757Control Toggle() => new()
    {
        Node = "X", Label = "Thing", Kind = Bbs757Kinds.Toggle, StateVar = "X",
        Positions = { new(0, "OFF"), new(1, "ON") }, Press = Press,
    };

    private static Bbs757Control Button() => new()
    {
        Node = "B", Label = "Button", Kind = Bbs757Kinds.Button, Press = ButtonPress,
    };

    private const string LightsState = "B757_OVHD_PASS_OXY_Toggle_Switch_CLICK";
    private const string LightsGuardNode = "B757_OVHD_SWITCH_GAURD";
    private const string LightsGuardPress = "(L:B757_OVHD_SWITCH_GAURD, bool) ! (>L:B757_OVHD_SWITCH_GAURD, bool)";

    private static Bbs757Control Lights() => new()
    {
        Node = LightsState, Label = "Emergency Lights", Kind = Bbs757Kinds.Switch, StateVar = LightsState,
        Positions = { new(0, "OFF"), new(1, "ARMED"), new(2, "ON") },
        Guard = LightsGuardNode, GuardClosed = 0, CloseSets = 1,
    };

    private static Bbs757Control LightsGuard() => new()
    {
        Node = LightsGuardNode, Label = "Emergency Lights Guard", Kind = Bbs757Kinds.Guard, StateVar = LightsGuardNode,
        Positions = { new(0, "Closed"), new(1, "Open") }, Press = LightsGuardPress,
    };

    /// <summary>A 757 definition built from in-memory controls with a fake sim behind its writes.</summary>
    private sealed class Rig
    {
        public readonly Bbs757FakeIo Sim = new();
        public readonly BBS757Definition Def;
        public readonly SimConnectManager Manager = new(IntPtr.Zero);

        public Rig(params Bbs757Control[] controls) : this(null, controls) { }

        public Rig(bool? parkingBrakeRealistic, params Bbs757Control[] controls)
        {
            var map = new Bbs757ControlMap();
            foreach (var c in controls) map.Controls.Add(c);
            Def = new BBS757Definition(map)
            {
                NestRunning = () => true, WriteIo = _ => Sim.Io, ParkingBrakeRealistic = () => parkingBrakeRealistic,
            };
        }

        /// <summary>The toggle's press flips what it reads, as BlueBird's does.</summary>
        public Rig TogglingOnPress()
        {
            Sim.OnWrite = rpn =>
            {
                if (rpn == Press) Sim.Fresh["X"] = Sim.Fresh["X"] == 1 ? 0 : 1;
            };
            return this;
        }

        public Task<bool> Set(string node, double target) => Def.FoSetAsync(Manager, node, target, CancellationToken.None);
        public Task<bool> PressNode(string node) => Def.FoPressAsync(Manager, node, CancellationToken.None);
    }

    private const string ParkNode = Bbs757ParkingBrake.Node;
    private const string ParkPress = "(L:B757_FC_PARK_BRAKE,bool) ! (>L:B757_FC_PARK_BRAKE,bool)";

    private static Bbs757Control ParkBrake() => new()
    {
        Node = ParkNode, Label = "Parking Brake", Kind = Bbs757Kinds.Toggle, StateVar = "B757_FC_PARK_BRAKE",
        Positions = { new(0, "Released"), new(1, "Set") }, Press = ParkPress,
    };

    private static Rig ParkRig(bool? realistic)
    {
        var rig = new Rig(realistic, ParkBrake());
        rig.Sim.Fresh[ParkNode] = 0;
        return rig;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    [InlineData(true)]
    public async Task Parking_brake_the_lever_sets_gets_the_lever_alone(bool? realistic)
    {
        var rig = ParkRig(realistic);
        rig.Sim.OnWrite = rpn => { if (rpn == ParkPress) rig.Sim.Fresh[ParkNode] = 1; };

        Assert.True(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task Parking_brake_in_realistic_mode_is_retried_with_full_braking_and_the_pedals_let_go()
    {
        // REALISTIC mode (measured 2026-10-28): the lever alone does nothing; full braking in the same frame sets it.
        var rig = ParkRig(realistic: true);
        rig.Sim.OnWrite = rpn => { if (rpn == Bbs757ParkingBrake.FullBrakingCode(true)) rig.Sim.Fresh[ParkNode] = 1; };

        Assert.True(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress, Bbs757ParkingBrake.FullBrakingCode(true), Bbs757ParkingBrake.PedalsReleaseCode }, rig.Sim.Writes);
        Assert.Contains("(>K:BRAKES)", Bbs757ParkingBrake.FullBrakingCode(true));
    }

    [Fact]
    public async Task Parking_brake_in_easy_mode_is_never_retried_with_braking()
    {
        var rig = ParkRig(realistic: false);

        Assert.False(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(new[] { ParkPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task Parking_brake_that_does_not_set_fails_and_still_lets_the_pedals_go()
    {
        var rig = ParkRig(realistic: true);

        Assert.False(await rig.Def.FoSetParkingBrakeCoreAsync(rig.Sim.Io, CancellationToken.None));
        Assert.Equal(Bbs757ParkingBrake.PedalsReleaseCode, rig.Sim.Writes.Last());
    }

    [Fact]
    public async Task Parking_brake_already_set_or_unwritable_sends_nothing()
    {
        var set = ParkRig(realistic: true);
        set.Sim.Fresh[ParkNode] = 1;
        Assert.True(await set.Def.FoSetParkingBrakeCoreAsync(set.Sim.Io, CancellationToken.None));
        Assert.Empty(set.Sim.Writes);

        var refused = ParkRig(realistic: true);
        refused.Sim.CanWrite = false;
        Assert.False(await refused.Def.FoSetParkingBrakeCoreAsync(refused.Sim.Io, CancellationToken.None));
        Assert.Empty(refused.Sim.Writes);
    }

    // ------------------------------------------------------------------ FoSetAsync

    [Fact]
    public async Task A_set_that_lands_returns_true()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;

        Assert.True(await rig.Set("X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
        Assert.Equal(1, rig.Sim.Fresh["X"]);
    }

    [Fact]
    public async Task A_set_already_there_sends_nothing_and_returns_true()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 1;

        Assert.True(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_set_the_aircraft_does_not_take_returns_false()
    {
        var rig = new Rig(Toggle());          // the press goes, the switch never moves
        rig.Sim.Fresh["X"] = 0;

        Assert.False(await rig.Set("X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
    }

    [Fact]
    public async Task A_read_back_that_cannot_be_read_returns_false()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 0;
        rig.Sim.OnWrite = _ => rig.Sim.Fresh["X"] = null;

        Assert.False(await rig.Set("X", 1));
    }

    [Fact]
    public async Task A_set_is_refused_when_nothing_can_land()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;
        rig.Sim.CanWrite = false;

        Assert.False(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task An_unread_control_is_never_pressed()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();     // nothing in Fresh: unread

        Assert.False(await rig.Set("X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_target_that_is_no_position_is_refused()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;

        Assert.False(await rig.Set("X", 2));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task An_unknown_node_or_a_button_is_refused_by_set()
    {
        var rig = new Rig(Toggle(), Button());
        Assert.False(await rig.Set("NOT_IN_THE_MAP", 1));
        Assert.False(await rig.Set("B", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_canceled_set_returns_false()
    {
        var rig = new Rig(Toggle()).TogglingOnPress();
        rig.Sim.Fresh["X"] = 0;
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.False(await rig.Def.FoSetAsync(rig.Manager, "X", 1, cts.Token));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_guarded_set_goes_through_the_guard_plan_and_reads_back()
    {
        var rig = new Rig(Lights(), LightsGuard());
        rig.Sim.Fresh[LightsState] = 0;
        rig.Sim.Fresh[LightsGuardNode] = 0;     // closed
        rig.Sim.OnWrite = rpn =>
        {
            if (rpn == LightsGuardPress) rig.Sim.Fresh[LightsGuardNode] = rig.Sim.Fresh[LightsGuardNode] == 0 ? 1 : 0;
            else if (rpn.EndsWith($" (>L:{LightsState})", StringComparison.Ordinal))
                rig.Sim.Fresh[LightsState] = double.Parse(rpn.Split(' ')[0], CultureInfo.InvariantCulture);
        };

        var plan = Bbs757Guarded.Plan(Lights(), LightsGuard(), current: 0, guardState: 0, target: 1);
        Assert.False(plan.Refused);

        Assert.True(await rig.Set(LightsState, 1));    // ARMED
        Assert.Equal(plan.Calls, rig.Sim.Writes);      // the panel's own guarded plan, call for call
        Assert.Equal(1, rig.Sim.Fresh[LightsState]);
    }

    [Fact]
    public async Task A_guarded_set_with_its_guard_unread_is_refused()
    {
        var rig = new Rig(Lights(), LightsGuard());
        rig.Sim.Fresh[LightsState] = 0;            // the guard is not read

        Assert.False(await rig.Set(LightsState, 1));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ FoPressAsync

    [Fact]
    public async Task A_press_is_sent_and_returns_true()
    {
        var rig = new Rig(Button());
        Assert.True(await rig.PressNode("B"));
        Assert.Equal(new[] { ButtonPress }, rig.Sim.Writes);
    }

    [Fact]
    public async Task A_press_is_refused_when_nothing_can_land()
    {
        var rig = new Rig(Button());
        rig.Sim.CanWrite = false;
        Assert.False(await rig.PressNode("B"));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task A_press_of_a_positional_control_or_unknown_node_is_refused()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 0;
        Assert.False(await rig.PressNode("X"));
        Assert.False(await rig.PressNode("NOT_IN_THE_MAP"));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ FoWriteNow (the window's close)

    [Fact]
    public void WriteNow_plans_from_the_cache_with_no_read_back()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Cache["X"] = 0;
        Assert.True(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Equal(new[] { Press }, rig.Sim.Writes);
        Assert.Empty(rig.Sim.Delays);
    }

    [Fact]
    public void WriteNow_already_there_sends_nothing()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Cache["X"] = 1;
        Assert.True(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public void WriteNow_refuses_unread_guarded_buttons_and_no_landing()
    {
        var rig = new Rig(Toggle(), Button(), Lights(), LightsGuard());
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "X", 1));                     // not cached
        rig.Sim.Cache["X"] = 0;
        rig.Sim.Cache[LightsState] = 0;
        Assert.False(rig.Def.FoWriteNow(rig.Manager, LightsState, 1));             // guarded
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "B", 1));                     // a button
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "NOT_IN_THE_MAP", 1));
        rig.Sim.CanWrite = false;
        Assert.False(rig.Def.FoWriteNow(rig.Manager, "X", 1));
        Assert.Empty(rig.Sim.Writes);
    }

    // ------------------------------------------------------------------ reads

    [Fact]
    public async Task Reads_go_through_the_same_sim_access()
    {
        var rig = new Rig(Toggle());
        rig.Sim.Fresh["X"] = 1;
        rig.Sim.Cache["Y"] = 7;
        Assert.Equal(1, await rig.Def.FoReadAsync(rig.Manager, "X", CancellationToken.None));
        Assert.Null(await rig.Def.FoReadAsync(rig.Manager, "UNREAD", CancellationToken.None));
        Assert.Equal(7, rig.Def.FoCached(rig.Manager, "Y"));
        Assert.Null(rig.Def.FoCached(rig.Manager, "UNREAD"));
    }

    [Fact]
    public void The_map_lookups_the_executor_relies_on()
    {
        var rig = new Rig(Toggle(), Button());
        Assert.True(rig.Def.HasControl("X"));
        Assert.False(rig.Def.HasControl("NOT_IN_THE_MAP"));
        Assert.Same(rig.Def.ControlFor("B"), rig.Def.ControlFor("B"));
        Assert.Null(rig.Def.ControlFor("NOT_IN_THE_MAP"));
    }

    // ------------------------------------------------------------------ silence

    /// <summary>
    /// The First Officer's write path speaks nothing: none of its methods takes an announcer, so a
    /// refusal or a write that did not land can only be reported by its return value (the flow, or
    /// FO-9's "Unable to complete", speaks for it). The panel path's "{label} did not move" is not reachable.
    /// </summary>
    [Fact]
    public void The_write_path_has_no_announcer_to_speak_through()
    {
        var methods = typeof(BBS757Definition).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(m => m.Name.StartsWith("Fo", StringComparison.Ordinal)
                        && (m.Name.Contains("Set") || m.Name.Contains("Press") || m.Name.Contains("Write") || m.Name.Contains("Read")
                            || m.Name.Contains("Cached")))
            .ToList();
        Assert.Contains(methods, m => m.Name == "FoSetAsync");
        Assert.Contains(methods, m => m.Name == "FoPressAsync");
        foreach (var m in methods)
            Assert.DoesNotContain(m.GetParameters(), p => typeof(ScreenReaderAnnouncer).IsAssignableFrom(p.ParameterType));
    }
}
