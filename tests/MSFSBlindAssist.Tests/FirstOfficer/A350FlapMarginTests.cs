using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer;

// Plan 05 Task 1, flown 2026-11-02 at VCBI (1.2.8, about 153 t): on the approach phase (PERF APPR activated, INI_flight_phase 6), level at
// 5000 ft with managed speed, the aircraft decelerated to green dot (172) and sat at 170 to 174 kt for over two minutes, inside
// ExtendWithinKts, without flaps 1: the approach context armed only on a descent of 300 ft below the leg's highest altitude, and the
// intercept was flown level. Flown again descending, every step came within 3 kt of its target (flaps 1 at 174, 2 at 153 with S 152,
// 3 at 139 with F 138), never past VFE next, and FULL at the landing gate.
public class A350FlapMarginTests
{
    private const double Field = 30;

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    private sealed class SpeechLog : MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer
    {
        public SpeechLog() : base(IntPtr.Zero) { }
        public override void Announce(string message) { }
        public override void AnnounceQueued(string message) { }
        public override void AnnounceWithQueue(string message) { }
        public override void AnnounceImmediate(string message) { }
    }

    private sealed class Rig
    {
        private readonly SimConnectManager _sim;
        private readonly TestClock _clock = new();
        private readonly RecordingA350Definition _definition = new();
        private readonly A350ActionExecutor _executor;
        public A350FOAutoManager Manager { get; }

        public Rig(double? fmsPhase)
        {
            _sim = SeededSimConnectCache.ConnectedWith(
                ("A350_IN_FMGEC_GD_SPD", 172), ("A350_IN_FCPC_S_SPEED", 152), ("A350_IN_FCPC_F_SPEED", 140), ("A350_IN_INI_VFE_SPEED", 255),
                ("A350_FLAPS", 0), ("A350_LDG_LEVER", 0), ("A350_ENGINE_THROTTLE_1", 25), ("A350_ENGINE_THROTTLE_2", 25));
            if (fmsPhase is double phase) SeededSimConnectCache.Cache(_sim)["A350_IN_INI_flight_phase"] = phase;
            var state = new A350StateEvaluator();
            state.SetSimConnect(_sim);
            _executor = _definition.Executor();
            Manager = new A350FOAutoManager(_executor, state, new SpeechLog(), () => _clock.Now) { AutoFlapsEnabled = true };
        }

        public void Fly(double msl, double vs, double ias, int seconds = 1)
        {
            for (int i = 0; i < seconds; i++)
            {
                Manager.Update(msl, vs, msl - Field, ias, onGround: false);
                _clock.Advance(1);
            }
        }

        public async Task<int[]> LeverWrites()
        {
            await _executor.WaitForDispatchDrainAsync();
            return _definition.Writes.Where(w => w.Key == "A350_FLAPS").Select(w => (int)w.Value).ToArray();
        }
    }

    /// <summary>The flight of 2026-11-02 to the decision: a climb to 5000 ft, level there, slowing to green dot.</summary>
    private static void ClimbThenLevelDeceleration(Rig rig)
    {
        rig.Fly(3000, 2000, 250, 10);
        rig.Fly(5000, 0, 250, 30);
        rig.Fly(5000, 0, 200, 10);
        rig.Fly(5000, 0, 173, 20);
    }

    [Fact]
    public async Task A_level_deceleration_in_the_approach_phase_extends_flaps_1_at_green_dot()
    {
        var rig = new Rig(fmsPhase: 6);
        ClimbThenLevelDeceleration(rig);
        Assert.Equal(new[] { 1 }, await rig.LeverWrites());
    }

    [Fact]
    public async Task A_departure_level_off_in_the_climb_phase_still_extends_nothing()
    {
        var rig = new Rig(fmsPhase: 3);
        ClimbThenLevelDeceleration(rig);
        Assert.Empty(await rig.LeverWrites());
    }

    [Fact]
    public async Task An_unread_flight_phase_arms_nothing_on_a_level_segment()
    {
        var rig = new Rig(fmsPhase: null);
        ClimbThenLevelDeceleration(rig);
        Assert.Empty(await rig.LeverWrites());
    }

    [Fact]
    public async Task A_go_around_climb_in_the_approach_phase_is_not_an_approach()
    {
        // TOGA on a level approach segment: the FMS phase reads 6 for a moment before 9, and the aircraft is climbing.
        var rig = new Rig(fmsPhase: 6);
        rig.Fly(5000, 0, 250, 30);
        rig.Fly(5300, 2500, 173, 20);
        Assert.Empty(await rig.LeverWrites());
    }
}
