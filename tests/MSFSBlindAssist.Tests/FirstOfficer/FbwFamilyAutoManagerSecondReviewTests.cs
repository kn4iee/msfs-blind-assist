// PR #160 second review (2026-10-18): the FlyByWire-family auto-flap schedules (A32NX, A380, Headwind
// A330), flown through their real managers on all three airframes.
//  - The high go-around fallback (a climb above 3,000 ft AGL) only clears the approach latch, and only
//    on an ESTABLISHED climb: one +250 fpm sample level on a high final cleared it, armed a retraction
//    and took the pilot's flaps 1 up ("Flaps up.").
//  - The climb-out retraction ends once the flaps read up in the air, whoever moved them: after a pilot
//    cleaned up the climb-out himself, his flaps 1 on the arrival, level above the approach context,
//    came up too.
//  - The approach context's 300 ft descent is measured in MSL: in AGL a level departure over rising
//    ground "descended" and armed it ("Speed checked. Flaps 2." on the departure).
//  - Every flap call is queued (Announce): an interrupting one cut off a flow's Captain reminder.
//
// The rig: the manager's own evaluator reads a seeded cache that reads as connected, its executor's
// writes land on a recording definition, its announcer records how each call was spoken, and its clock
// is the test's, so the 4 s dwell and the fallback's 10 s climb cost no real time. The lever does not
// follow a write by itself: the test moves it in the cache, as the aircraft would.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.FirstOfficer.FBWA320;
using MSFSBlindAssist.FirstOfficer.FBWA380;
using MSFSBlindAssist.FirstOfficer.HWA330;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

public class FbwFamilyAutoManagerSecondReviewTests
{
    public static TheoryData<string> Airframes => new() { "A32NX", "A380", "A330", "A350" };

    // Live 2026-10-30, the A350's first take-off with the schedule on: its F speed reads below V2 (F 138, V2 148 at 200 t), so
    // the lever came up to 1 at lift-off and to 0 at 430 ft. The manual: "Accelerating above the F speed select Flaps 1", in the
    // acceleration segment, "between 1,000 and 1,500 ft". Nothing moves below that.
    [Fact]
    public async Task The_A350s_flaps_wait_for_the_acceleration_height()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(2);
        rig.OnGround();
        rig.Fly(agl: 10, vs: 500, ias: 155);
        rig.Fly(agl: 400, vs: 2_000, ias: 175);
        rig.Fly(agl: 900, vs: 2_000, ias: 180);
        Assert.Empty(await rig.LeverWrites());
        rig.Wait(5);
        rig.Fly(agl: 1_100, vs: 2_000, ias: 180);
        Assert.Equal(new[] { 1 }, await rig.LeverWrites());
    }

    // Live 2026-10-30, on a final at 3000 ft: ALT took a 200 ft sag back at 1000 fpm, which read as a go-around, and the flaps came up
    // from 2 to 0 at 9 NM. On the A350 a go-around has its levers at MCT or TOGA; nothing else climbs out of an approach as one.
    [Fact]
    public async Task The_A350s_climb_back_to_an_altitude_with_the_levers_at_climb_is_no_go_around()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(0);
        rig.OnGround();
        rig.Thrust(25);
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);  // the approach context arms
        rig.Lever(2);
        rig.Fly(agl: 2_800, vs: -500, ias: 170);    // the sag
        rig.Fly(agl: 2_900, vs: 1_000, ias: 170);
        rig.Fly(agl: 3_010, vs: 800, ias: 170);     // 200 ft above it, climbing
        rig.Wait(5);
        rig.Fly(agl: 3_000, vs: 0, ias: 170);       // past F, level
        rig.Wait(5);
        rig.Fly(agl: 3_000, vs: 0, ias: 170);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    // The levers come back to CL at the thrust reduction altitude: the go-around was still one, though the height came after that.
    [Fact]
    public async Task The_A350s_go_around_stands_when_the_levers_left_TOGA_before_the_height_came()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(0);
        rig.OnGround();
        rig.Thrust(25);
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);
        rig.Lever(4);
        rig.GearDown();
        rig.Fly(agl: 300, vs: -700, ias: 140);      // the lowest
        rig.Thrust(45);
        rig.Fly(agl: 400, vs: 1_500, ias: 145);
        rig.Thrust(25);
        rig.Fly(agl: 550, vs: 1_800, ias: 150);     // "Go-around. Flaps 3."

        Assert.Equal(new[] { 3 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Go-around. Flaps 3.") }, rig.Speech.All);
    }

    // Live 2026-10-30: at idle on the glide path, flaps 2 and gear down, the speed settled at 154 with F at 152.9 and stayed there through
    // 1100 ft, so the landing flaps never came. The manual's rule is "Always select your next flap setting when below Vfe next"; F is only
    // when the First Officer gets to it high on the approach.
    [Fact]
    public async Task The_A350s_landing_flaps_come_on_the_descent_below_1500_ft_whatever_the_speed_settled_at()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(0);
        rig.OnGround();
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);
        rig.Lever(2);
        rig.GearDown();
        rig.Fly(agl: 1_600, vs: -800, ias: 154);     // F is 150: above the gate it waits
        Assert.Empty(await rig.LeverWrites());
        rig.Fly(agl: 1_450, vs: -800, ias: 154);
        Assert.Equal(new[] { 3 }, await rig.LeverWrites());
        rig.Lever(3);
        rig.Wait(5);
        rig.Fly(agl: 1_350, vs: -800, ias: 154);
        Assert.Equal(new[] { 3, 4 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Speed checked. Flaps 3."), Queued("Speed checked. Flaps full.") }, rig.Speech.All);
    }

    // Level at circuit height is no final: it waits for the speed as before, and VFE next still holds it.
    [Fact]
    public async Task The_A350s_gate_holds_for_level_flight_and_for_VFE_next()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(0);
        rig.OnGround();
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);
        rig.Lever(2);
        rig.GearDown();
        rig.Fly(agl: 1_200, vs: 0, ias: 154);        // level
        rig.Fly(agl: 1_200, vs: -800, ias: 212);     // VFE next is 215: within its margin
        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    // The gear gate stands below it too: the last step is not taken with the gear up.
    [Fact]
    public async Task The_A350s_full_flaps_still_wait_for_the_gear_below_the_gate()
    {
        var rig = FlapRig.For("A350");
        rig.Lever(0);
        rig.OnGround();
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);
        rig.Lever(3);
        rig.Fly(agl: 1_400, vs: -800, ias: 154);
        Assert.Empty(await rig.LeverWrites());
        rig.GearDown();
        rig.Fly(agl: 1_300, vs: -800, ias: 154);
        Assert.Equal(new[] { 4 }, await rig.LeverWrites());
    }

    // Takeoff with CONF 1+F and a climb-out the pilot cleans up himself, then an arrival: a real descent
    // to a level high final at 4,000 ft AGL (the approach context arms on the way down), where the pilot
    // selects flaps 1 and flies above S.
    private static void ArriveLevelOnAHighFinalWithFlaps1(FlapRig rig)
    {
        rig.Lever(1);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 170);   // below S: the FO holds
        rig.Lever(0);
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);
        rig.Fly(agl: 5_500, vs: -1_500, ias: 240);
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);  // below 5,000 ft AGL, far below the top: armed
        rig.Lever(1);
        rig.Fly(agl: 4_000, vs: 0, ias: 195);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task One_climbing_sample_on_a_high_final_keeps_the_approach_context_and_moves_nothing(string airframe)
    {
        var rig = FlapRig.For(airframe);
        ArriveLevelOnAHighFinalWithFlaps1(rig);

        rig.Fly(agl: 4_005, vs: 250, ias: 195);   // one turbulence or ALT-capture sample

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);

        // The context still stands: slowing below S extends the next step.
        rig.Fly(agl: 4_005, vs: 0, ias: 180);
        Assert.Equal(new[] { 2 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Speed checked. Flaps 2.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task An_established_climb_above_3000_ft_only_clears_the_approach_latch(string airframe)
    {
        var rig = FlapRig.For(airframe);
        ArriveLevelOnAHighFinalWithFlaps1(rig);

        // ATC climbs the aircraft: 12 s above +200 fpm (400 fpm, under the go-around signature's 500).
        for (int s = 1; s <= 12; s++)
            rig.Fly(agl: 4_000 + 7 * s, vs: 400, ias: 195);
        // Level and slow below S: the latch is cleared, so nothing extends.
        rig.Fly(agl: 4_100, vs: 0, ias: 180);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);

        // The next real descent arms it again once 300 ft below the new top (4,600 ft MSL).
        rig.Fly(agl: 3_900, vs: -800, ias: 180);
        Assert.Empty(await rig.LeverWrites());
        rig.Fly(agl: 3_780, vs: -800, ias: 180);
        Assert.Equal(new[] { 2 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Speed checked. Flaps 2.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_flaps_1_the_pilot_selects_on_the_arrival_stays_out_after_he_cleaned_up_the_climb_out(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(2);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 145);   // below F: the FO holds
        rig.Lever(0);                               // the pilot cleans up himself
        rig.Fly(agl: 5_000, vs: 2_000, ias: 250);
        rig.Fly(agl: 29_500, vs: 0, ias: 280);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);       // level on the arrival, above the approach context
        rig.Lever(1);                               // the pilot's flaps 1, above S
        rig.Fly(agl: 7_000, vs: 0, ias: 210);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task The_climb_out_retraction_is_queued_and_ends_once_the_flaps_read_up(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(2);
        rig.OnGround();
        rig.Fly(agl: 1_000, vs: 2_000, ias: 160);   // past F: "Flaps 1."
        rig.Lever(1);
        rig.Fly(agl: 2_000, vs: 2_000, ias: 175);   // between F and S
        rig.Wait(5);                                // the 4 s dwell
        rig.Fly(agl: 3_000, vs: 2_000, ias: 200);   // past S: "Flaps up."
        rig.Lever(0);
        rig.Fly(agl: 5_000, vs: 2_000, ias: 250);
        // Level on the arrival the pilot selects flaps 1 above S: left out.
        rig.Fly(agl: 7_000, vs: 0, ias: 210);
        rig.Lever(1);
        rig.Fly(agl: 7_000, vs: 0, ias: 210);

        Assert.Equal(new[] { 1, 0 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Flaps 1."), Queued("Flaps up.") }, rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_level_departure_over_rising_ground_never_arms_the_approach_context(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(1);
        rig.OnGround();
        rig.Fly(agl: 2_000, vs: 2_000, ias: 180);   // CONF 1+F, below S: the FO holds
        rig.Fly(agl: 3_500, vs: 1_000, ias: 180);   // level-off at 4,000 ft MSL
        // Level at 4,000 ft MSL while the ground under the departure rises 400 ft...
        rig.Fly(agl: 3_300, vs: 0, ias: 180, ground: 700);
        rig.Fly(agl: 3_100, vs: 0, ias: 180, ground: 900);
        // ...and one sample under the descending threshold (an altitude-hold correction).
        rig.Fly(agl: 3_090, vs: -150, ias: 180, ground: 900);
        rig.Fly(agl: 3_100, vs: 0, ias: 180, ground: 900);

        Assert.Empty(await rig.LeverWrites());
        Assert.Empty(rig.Speech.All);
    }

    [Theory, MemberData(nameof(Airframes))]
    public async Task A_real_go_around_still_steps_the_flaps_and_arms_the_climb_out_retraction(string airframe)
    {
        var rig = FlapRig.For(airframe);
        rig.Lever(0);
        rig.OnGround();
        rig.Fly(agl: 10_000, vs: 1_500, ias: 250);  // flaps up in the air: no climb-out retraction pending
        rig.Fly(agl: 4_000, vs: -1_000, ias: 230);  // the approach context arms
        rig.Lever(4);                               // the pilot's landing configuration
        rig.GearDown();
        rig.Fly(agl: 500, vs: -700, ias: 140);
        rig.Fly(agl: 300, vs: -700, ias: 140);      // the approach's lowest point
        rig.Thrust(45);
        rig.Fly(agl: 400, vs: 1_500, ias: 145);     // TOGA: climbing, not yet 200 ft above it
        rig.Fly(agl: 550, vs: 1_800, ias: 150);     // "Go-around. Flaps 3."
        rig.Lever(3);
        rig.Wait(5);
        rig.Fly(agl: 1_500, vs: 1_500, ias: 160);   // past F: "Flaps 1."
        rig.Lever(1);
        rig.Wait(5);
        rig.Fly(agl: 2_500, vs: 1_500, ias: 200);   // past S: "Flaps up."
        rig.Lever(0);
        rig.Fly(agl: 3_000, vs: 0, ias: 210);

        Assert.Equal(new[] { 3, 1, 0 }, await rig.LeverWrites());
        Assert.Equal(new[] { Queued("Go-around. Flaps 3."), Queued("Flaps 1."), Queued("Flaps up.") },
            rig.Speech.All);
    }

    private static (string Kind, string Text) Queued(string text) => (nameof(ScreenReaderAnnouncer.Announce), text);

    /// <summary>An announcer that records every call with how it was spoken, so a test tells a queued
    /// call (<c>Announce</c>) from one that cancels NVDA's queue (<c>AnnounceImmediate</c>). The
    /// automatic calls drop while <c>Suppressed</c> is set, as the real announcer's do.</summary>
    private sealed class SpeechLog : ScreenReaderAnnouncer
    {
        public SpeechLog() : base(IntPtr.Zero) { }

        public List<(string Kind, string Text)> All { get; } = new();

        public override void Announce(string message) { if (!Suppressed) All.Add((nameof(Announce), message)); }
        public override void AnnounceQueued(string message) { if (!Suppressed) All.Add((nameof(AnnounceQueued), message)); }
        public override void AnnounceWithQueue(string message) { if (!Suppressed) All.Add((nameof(AnnounceWithQueue), message)); }
        public override void AnnounceImmediate(string message) => All.Add((nameof(AnnounceImmediate), message));
    }

    private sealed class TestClock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
        public void Advance(double seconds) => Now = Now.AddSeconds(seconds);
    }

    /// <summary>One airframe's auto manager on a seeded cache: green dot 210, S 190, F 150 and VFE-next
    /// 215 kt, the field at 500 ft MSL. Each <see cref="Fly"/> sample is one second of the test's clock.</summary>
    private sealed class FlapRig
    {
        private const double FieldElevation = 500;
        private readonly string _leverKey;
        private const double VfeNextKts = 215;

        private readonly SimConnectManager _sim;
        private readonly TestClock _clock;
        private readonly IFoActionExecutor _executor;
        private readonly string _gearKey;
        private readonly Func<IEnumerable<(string Key, double Value)>> _writes;

        public IFoAutoManager Manager { get; }
        public SpeechLog Speech { get; }

        private FlapRig(SimConnectManager sim, TestClock clock, SpeechLog speech, IFoActionExecutor executor,
            string gearKey, IFoAutoManager manager, Func<IEnumerable<(string Key, double Value)>> writes,
            string leverKey = "A32NX_FLAPS_HANDLE_INDEX")
        {
            _leverKey = leverKey;
            _sim = sim;
            _clock = clock;
            Speech = speech;
            _executor = executor;
            _gearKey = gearKey;
            Manager = manager;
            _writes = writes;
            Manager.AutoFlapsEnabled = true;
        }

        public static FlapRig For(string airframe)
        {
            var clock = new TestClock();
            var speech = new SpeechLog();
            var sim = SeededSimConnectCache.ConnectedWith(
                ("A32NX_SPEEDS_GD", 210), ("A32NX_SPEEDS_S", 190), ("A32NX_SPEEDS_F", 150));
            switch (airframe)
            {
                case "A32NX":
                {
                    var state = new FbwA320StateEvaluator();
                    state.SetSimConnect(sim);
                    // FO_VFE_NEXT decodes the FAC's V_FE_NEXT word (FOA-7): normal operation, 215 kt.
                    Seed(sim, "FAC_1_V_FE_NEXT",
                        (double)((3UL << 32) | BitConverter.SingleToUInt32Bits((float)VfeNextKts)));
                    var definition = new RecordingFbwA320Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "GEAR_HANDLE_POSITION",
                        new FbwA320FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes.Select(w => (w.Key, w.Value)));
                }
                case "A380":
                {
                    var state = new FbwA380StateEvaluator();
                    state.SetSimConnect(sim);
                    Seed(sim, "A32NX_SPEEDS_VFEN", VfeNextKts);
                    var definition = new RecordingA380Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "A32NX_GEAR_HANDLE_POSITION",
                        new FbwA380FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes);
                }
                case "A330":
                {
                    var state = new HwA330StateEvaluator();
                    state.SetSimConnect(sim);
                    Seed(sim, "A32NX_SPEEDS_VFEN", VfeNextKts);   // the A339X's plain L:var (FOA-7)
                    var definition = new RecordingHwA330Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "GEAR_HANDLE_POSITION",
                        new HwA330FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes.Select(w => (w.Key, w.Value)));
                }
                case "A350":
                {
                    var state = new A350StateEvaluator();
                    state.SetSimConnect(sim);
                    Seed(sim, "A350_IN_FMGEC_GD_SPD", 210);
                    Seed(sim, "A350_IN_FCPC_S_SPEED", 190);
                    Seed(sim, "A350_IN_FCPC_F_SPEED", 150);
                    Seed(sim, "A350_IN_INI_VFE_SPEED", VfeNextKts);
                    var definition = new RecordingA350Definition();
                    var executor = definition.Executor();
                    return new FlapRig(sim, clock, speech, executor, "A350_LDG_LEVER",
                        new A350FOAutoManager(executor, state, speech, () => clock.Now),
                        () => definition.Writes, "A350_FLAPS");
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(airframe), airframe, null);
            }
        }

        public void Lever(int position) => Seed(_sim, _leverKey, position);

        /// <summary>The A350's thrust levers (the only airframe here whose schedule reads them).</summary>
        public void Thrust(double tla)
        {
            Seed(_sim, "A350_ENGINE_THROTTLE_1", tla);
            Seed(_sim, "A350_ENGINE_THROTTLE_2", tla);
        }

        public void GearDown() => Seed(_sim, _gearKey, 1);
        public void OnGround() => Manager.Update(FieldElevation, 0, 0, 0, onGround: true);

        public void Fly(double agl, double vs, double ias, double ground = FieldElevation)
        {
            Manager.Update(ground + agl, vs, agl, ias, onGround: false);
            _clock.Advance(1);
        }

        public void Wait(double seconds) => _clock.Advance(seconds);

        /// <summary>The flap lever positions the First Officer wrote, once the executor's paced writes
        /// have all gone out.</summary>
        public async Task<int[]> LeverWrites()
        {
            await _executor.WaitForDispatchDrainAsync();
            return _writes().Where(w => w.Key == _leverKey).Select(w => (int)w.Value).ToArray();
        }

        private static void Seed(SimConnectManager sim, string key, double value) =>
            SeededSimConnectCache.Cache(sim)[key] = value;
    }
}
