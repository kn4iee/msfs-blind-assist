using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's MCP value entry: the pure RPN and phrase builders, the typed-input
/// validators, and the set loops run against a simulated MCP (<see cref="FakeMcp"/>). What a real
/// 757 does with a burst of K-events is checked in the sim; what is pinned here is the logic
/// around it: how many steps, when to correct, what is spoken (the value READ, never the request),
/// and that no number is formatted with the PC's culture (CI runs under en-US, so the de-DE and
/// sv-SE tests are the ones that can catch a comma or a U+2212 minus).
/// </summary>
public class Bbs757McpTests
{
    private static int Count(IEnumerable<string> calls, string needle)
        => calls.Sum(c => c.Split(needle).Length - 1);

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- RPN builders -----------------------------------------------------------------------

    [Fact]
    public void Constants_name_the_measured_variables()
    {
        Assert.Equal("BBS_AP_TARGET_HEADING", Bbs757Mcp.HeadingVar);
        Assert.Equal("BBS_AP_TARGET_ALTITUDE", Bbs757Mcp.AltitudeVar);
        Assert.Equal("BBS757_ORANGE_AIRSPEED_BUGS", Bbs757Mcp.SpeedVar);
        Assert.Equal("BBS_AP_TARGET_VS", Bbs757Mcp.VsVar);
        Assert.Equal("BBS_AIRSPEED_IN_MACH", Bbs757Mcp.MachModeVar);
    }

    [Fact]
    public void SpeedSteps_up_is_fifty_increments_in_two_calls()
    {
        var calls = Bbs757Mcp.SpeedSteps(200, 250);

        Assert.Equal(2, calls.Count);
        Assert.Equal(50, Count(calls, "(>K:AP_SPD_VAR_INC)"));
        Assert.Equal(0, Count(calls, "DEC"));
    }

    [Fact]
    public void SpeedSteps_down_is_fifty_one_decrements()
    {
        var calls = Bbs757Mcp.SpeedSteps(250, 199);

        Assert.Equal(51, Count(calls, "(>K:AP_SPD_VAR_DEC)"));
        Assert.Equal(0, Count(calls, "INC"));
        Assert.All(calls, c => Assert.True(Count(new[] { c }, "(>K:") <= Bbs757Rpn.MaxKeysPerCall));
    }

    [Fact]
    public void SpeedSteps_when_already_there_is_empty()
    {
        Assert.Empty(Bbs757Mcp.SpeedSteps(200, 200));
        Assert.Empty(Bbs757Mcp.SpeedSteps(200.4, 199.6));   // both round to 200
    }

    [Fact]
    public void SpeedSteps_counts_whole_knots_between_the_rounded_values()
    {
        Assert.Equal(6, Count(Bbs757Mcp.SpeedSteps(204.4, 209.6), "(>K:AP_SPD_VAR_INC)"));   // 204 to 210
    }

    [Fact]
    public void SpeedSteps_of_an_unreadable_value_sends_nothing()
    {
        Assert.Empty(Bbs757Mcp.SpeedSteps(double.NaN, 250));
        Assert.Empty(Bbs757Mcp.SpeedSteps(200, double.PositiveInfinity));
    }

    [Fact]
    public void HeadingWrite_sets_the_target_lvar_and_wraps_360_to_north()
    {
        Assert.Equal("0 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(360));
        Assert.Equal("90 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(90));
        Assert.Equal("359 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(359));
        Assert.Equal("359 (>L:BBS_AP_TARGET_HEADING)", Bbs757Mcp.HeadingWrite(-1));
    }

    [Fact]
    public void AltitudeWrite_rounds_to_the_hundred()
    {
        Assert.Equal("12000 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(12049));
        Assert.Equal("12100 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(12050));
        Assert.Equal("35000 (>L:BBS_AP_TARGET_ALTITUDE)", Bbs757Mcp.AltitudeWrite(35000));
    }

    [Fact]
    public void VsWrite_sets_the_target_lvar_signed()
    {
        Assert.Equal("-1500 (>L:BBS_AP_TARGET_VS)", Bbs757Mcp.VsWrite(-1500));
        Assert.Equal("0 (>L:BBS_AP_TARGET_VS)", Bbs757Mcp.VsWrite(0));
    }

    [Fact]
    public void VsSteps_step_the_difference_in_hundreds()
    {
        Assert.Equal(15, Count(Bbs757Mcp.VsSteps(0, -1500), "(>K:AP_VS_VAR_DEC)"));
        Assert.Equal(15, Count(Bbs757Mcp.VsSteps(-1500, 0), "(>K:AP_VS_VAR_INC)"));
        Assert.Equal(1, Count(Bbs757Mcp.VsSteps(1450, 1500), "(>K:AP_VS_VAR_INC)"));   // 50 fpm rounds up to one step
        Assert.Empty(Bbs757Mcp.VsSteps(1500, 1500));
        Assert.Empty(Bbs757Mcp.VsSteps(1500, 1530));   // less than half a step
        Assert.Empty(Bbs757Mcp.VsSteps(double.NaN, 1500));
    }

    // ---- Typed input ------------------------------------------------------------------------

    [Fact]
    public void A_typed_speed_under_a_comma_decimal_culture_is_accepted()
    {
        InCulture("de-DE", () =>
        {
            Assert.True(NumberParsing.TryParseTyped("250", out double value));
            Assert.True(FcuValueEntry.TrySpeed(value, out int speed, out _));
            Assert.Equal(250, speed);

            Assert.True(Bbs757Mcp.TrySpeedInput("250", out int knots, out string? error));
            Assert.Equal(250, knots);
            Assert.Null(error);
        });
    }

    [Fact]
    public void Output_carries_no_comma_under_comma_decimal_cultures()
    {
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                var all = new List<string>
                {
                    Bbs757Mcp.HeadingWrite(90), Bbs757Mcp.AltitudeWrite(12050), Bbs757Mcp.VsWrite(-1500),
                    Bbs757Mcp.HeadingPhrase(90.4), Bbs757Mcp.SpeedPhrase(250.4),
                    Bbs757Mcp.AltitudePhrase(12000), Bbs757Mcp.AltitudePhrase(35000.2),
                    Bbs757Mcp.VsPhrase(-1500), Bbs757Mcp.VsPhrase(1500), Bbs757Mcp.VsPhrase(0),
                };
                all.AddRange(Bbs757Mcp.SpeedSteps(200, 250));
                all.AddRange(Bbs757Mcp.VsSteps(0, -1500));

                Assert.All(all, s => Assert.DoesNotContain(",", s));
                Assert.All(all, s => Assert.DoesNotContain("−", s));   // sv-SE's minus sign
            });
        }
    }

    [Fact]
    public void A_typed_speed_takes_knots_and_refuses_mach_aloud()
    {
        Assert.True(Bbs757Mcp.TrySpeedInput("250", out int knots, out _));
        Assert.Equal(250, knots);
        Assert.True(Bbs757Mcp.TrySpeedInput("100", out _, out _));
        Assert.True(Bbs757Mcp.TrySpeedInput("399", out _, out _));

        Assert.False(Bbs757Mcp.TrySpeedInput("0.82", out _, out string? mach));
        Assert.Equal("Mach entry is not supported yet. Enter a speed from 100 to 399 knots.", mach);
        InCulture("de-DE", () =>
        {
            Assert.False(Bbs757Mcp.TrySpeedInput("0,82", out _, out string? machDe));
            Assert.Equal(Bbs757Mcp.MachEntryRefusal, machDe);
        });

        Assert.False(Bbs757Mcp.TrySpeedInput("99", out _, out string? low));
        Assert.False(Bbs757Mcp.TrySpeedInput("400", out _, out string? high));
        Assert.False(Bbs757Mcp.TrySpeedInput("", out _, out string? empty));
        Assert.False(Bbs757Mcp.TrySpeedInput("fast", out _, out string? text));
        Assert.All(new[] { low, high, empty, text }, e => Assert.False(string.IsNullOrEmpty(e)));
        Assert.NotEqual(Bbs757Mcp.MachEntryRefusal, low);
    }

    [Fact]
    public void A_typed_heading_is_zero_to_360_and_360_is_north()
    {
        Assert.True(Bbs757Mcp.TryHeadingInput("90", out int hdg, out _));
        Assert.Equal(90, hdg);
        Assert.True(Bbs757Mcp.TryHeadingInput("360", out int north, out _));
        Assert.Equal(0, north);

        Assert.True(Bbs757Mcp.TryHeadingInput("090", out int padded, out _));   // the way a pilot types a heading
        Assert.Equal(90, padded);

        Assert.False(Bbs757Mcp.TryHeadingInput("361", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryHeadingInput("-5", out _, out _));
        Assert.False(Bbs757Mcp.TryHeadingInput("NaN", out _, out _));
        Assert.False(Bbs757Mcp.TryHeadingInput(null, out _, out _));
    }

    [Fact]
    public void A_typed_altitude_is_rounded_to_the_hundred()
    {
        Assert.True(Bbs757Mcp.TryAltitudeInput("12049", out int ft, out _));
        Assert.Equal(12000, ft);
        Assert.True(Bbs757Mcp.TryAltitudeInput("12050", out int up, out _));
        Assert.Equal(12100, up);
        Assert.True(Bbs757Mcp.TryAltitudeInput("100", out _, out _));
        Assert.True(Bbs757Mcp.TryAltitudeInput("49000", out _, out _));

        Assert.False(Bbs757Mcp.TryAltitudeInput("99", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryAltitudeInput("49001", out _, out _));
        Assert.False(Bbs757Mcp.TryAltitudeInput("high", out _, out _));
    }

    [Fact]
    public void A_typed_vertical_speed_is_minus_8000_to_6000_in_hundreds()
    {
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-1500", out int fpm, out _));
        Assert.Equal(-1500, fpm);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("0", out int level, out _));
        Assert.Equal(0, level);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-8000", out _, out _));
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("6000", out _, out _));

        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("1549", out int down, out _));
        Assert.Equal(1500, down);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("1550", out int up, out _));
        Assert.Equal(1600, up);
        Assert.True(Bbs757Mcp.TryVerticalSpeedInput("-1550", out int negUp, out _));
        Assert.Equal(-1600, negUp);

        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("6001", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("-8001", out _, out _));
        Assert.False(Bbs757Mcp.TryVerticalSpeedInput("up", out _, out _));
    }

    // ---- Spoken phrases ---------------------------------------------------------------------

    [Fact]
    public void The_phrases_say_the_window_value()
    {
        Assert.Equal("Heading 090", Bbs757Mcp.HeadingPhrase(90));
        Assert.Equal("Heading 005", Bbs757Mcp.HeadingPhrase(4.6));
        Assert.Equal("Heading 000", Bbs757Mcp.HeadingPhrase(0));
        Assert.Equal("Heading 000", Bbs757Mcp.HeadingPhrase(359.6));   // rounds to 360, which is north
        Assert.Equal("Speed 250", Bbs757Mcp.SpeedPhrase(250.2));
        Assert.Equal("Altitude 12000", Bbs757Mcp.AltitudePhrase(12000));
        Assert.Equal("Altitude 35000", Bbs757Mcp.AltitudePhrase(35000));
        Assert.Equal("Vertical speed minus 1500", Bbs757Mcp.VsPhrase(-1500));
        Assert.Equal("Vertical speed 1500", Bbs757Mcp.VsPhrase(1500));
        Assert.Equal("Vertical speed 0", Bbs757Mcp.VsPhrase(0));
        Assert.Equal("Vertical speed 0", Bbs757Mcp.VsPhrase(-0.2));   // never "minus 0"
    }

    [Fact]
    public void An_unreadable_value_is_unavailable_never_a_zero()
    {
        Assert.Equal("Heading unavailable", Bbs757Mcp.HeadingPhrase(null));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(null));
        Assert.Equal("Altitude unavailable", Bbs757Mcp.AltitudePhrase(null));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(null));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(double.NaN));
    }

    [Fact]
    public void The_mach_mode_message_is_the_plans()
    {
        Assert.Equal("MCP is in Mach mode. Set Mach from the Glareshield panel.", Bbs757Mcp.MachModeSetRefusal);
    }

    // ---- Set loops against a simulated MCP ---------------------------------------------------

    /// <summary>
    /// A simulated MCP behind <see cref="McpIo"/>. A K-event burst counts only
    /// <see cref="EventsCountedPerCall"/> events per calculator call (the real limit is unmeasured
    /// past 11) and <see cref="EventsCountedPerWindow"/> between two waits, and
    /// <see cref="DirectWritesAdopted"/> says whether the target L:var takes a write. It keeps a
    /// virtual clock (<see cref="Now"/>, advanced only by the loop's waits): with
    /// <see cref="LagMs"/> set a speed burst lands in two halves that far apart, and with
    /// <see cref="DriftPerRead"/> set the speed keeps moving after a burst so it never settles.
    /// </summary>
    private sealed class FakeMcp
    {
        public double? Speed = 200, Heading = 0, Altitude = 10000, Vs = 0, Mach = 0;
        public int EventsCountedPerCall = int.MaxValue;
        public int EventsCountedPerWindow = int.MaxValue;
        public bool DirectWritesAdopted = true;
        public readonly List<string> Writes = new();
        public int Delays;
        public long Now;                 // virtual ms
        public int LagMs;                // > 0: a speed burst lands half now, half this long later
        public double DriftPerRead;      // added to the speed on every read once a burst was sent
        public double? SpeedAfterSend;   // when set, the speed reads this once a burst was sent (a wild read)
        public double Peak;              // highest speed the target ever held after a burst landed
        private readonly List<(long Due, double Delta)> _pending = new();
        private bool _sent;
        private int _used;   // events counted since the last wait

        private void MoveSpeed(double delta)
        {
            if (Speed == null) return;
            Speed += delta;
            Peak = Math.Max(Peak, Speed.Value);
        }

        private void Land()
        {
            foreach (var due in _pending.Where(p => p.Due <= Now).ToList())
            {
                MoveSpeed(due.Delta);
                _pending.Remove(due);
            }
        }

        private int Counted(string rpn, string kEvent)
        {
            int n = Math.Min(Count(new[] { rpn }, kEvent), EventsCountedPerCall);
            n = Math.Min(n, Math.Max(0, EventsCountedPerWindow - _used));
            _used += n;
            return n;
        }

        public McpIo Io => new()
        {
            Read = (key, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Land();
                if (key == Bbs757Mcp.SpeedVar && _sent && DriftPerRead != 0) MoveSpeed(DriftPerRead);
                return Task.FromResult(key switch
                {
                    Bbs757Mcp.SpeedVar => Speed,
                    Bbs757Mcp.HeadingVar => Heading,
                    Bbs757Mcp.AltitudeVar => Altitude,
                    Bbs757Mcp.VsVar => Vs,
                    Bbs757Mcp.MachModeVar => Mach,
                    _ => (double?)null,
                });
            },
            Write = rpn =>
            {
                Writes.Add(rpn);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_HEADING)") && DirectWritesAdopted) Heading = Value(rpn);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_ALTITUDE)") && DirectWritesAdopted) Altitude = Value(rpn);
                if (rpn.EndsWith("(>L:BBS_AP_TARGET_VS)") && DirectWritesAdopted) Vs = Value(rpn);

                int inc = Counted(rpn, "(>K:AP_SPD_VAR_INC)");
                int dec = Counted(rpn, "(>K:AP_SPD_VAR_DEC)");
                int net = inc - dec;
                if (rpn.Contains("(>K:AP_SPD_VAR_"))
                {
                    _sent = true;
                    if (SpeedAfterSend != null) Speed = SpeedAfterSend;
                }
                if (LagMs > 0)
                {
                    int first = net / 2;
                    MoveSpeed(first);
                    _pending.Add((Now + LagMs, net - first));
                }
                else MoveSpeed(net);

                int vsInc = Counted(rpn, "(>K:AP_VS_VAR_INC)");
                int vsDec = Counted(rpn, "(>K:AP_VS_VAR_DEC)");
                if (Vs != null) Vs += 100 * (vsInc - vsDec);
            },
            Delay = (ms, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                Delays++;
                Now += ms;
                _used = 0;
                Land();
                return Task.CompletedTask;
            },
        };

        private static double Value(string rpn) => double.Parse(rpn.Split(' ')[0], CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task Setting_the_heading_writes_directly_and_speaks_the_window_value()
    {
        var mcp = new FakeMcp();

        string said = await Bbs757Mcp.SetHeadingAsync(mcp.Io, 90, default);

        Assert.Equal(new[] { "90 (>L:BBS_AP_TARGET_HEADING)" }, mcp.Writes);
        Assert.Equal("Heading 090", said);
    }

    [Fact]
    public async Task A_heading_the_window_did_not_take_is_spoken_as_the_window_shows_it()
    {
        var mcp = new FakeMcp { DirectWritesAdopted = false, Heading = 45 };

        Assert.Equal("Heading 045", await Bbs757Mcp.SetHeadingAsync(mcp.Io, 90, default));
    }

    [Fact]
    public async Task Setting_the_altitude_speaks_the_window_value()
    {
        var mcp = new FakeMcp();

        string said = await Bbs757Mcp.SetAltitudeAsync(mcp.Io, 12000, default);

        Assert.Equal(new[] { "12000 (>L:BBS_AP_TARGET_ALTITUDE)" }, mcp.Writes);
        Assert.Equal("Altitude 12000", said);
    }

    [Fact]
    public async Task A_speed_set_steps_the_difference_and_speaks_what_it_reads()
    {
        var mcp = new FakeMcp { Speed = 200 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.Equal(50, Count(mcp.Writes, "(>K:AP_SPD_VAR_INC)"));
        Assert.Equal(2, mcp.Writes.Count);   // nothing to correct
        Assert.DoesNotContain(mcp.Writes, w => w.Contains("L:BBS757_ORANGE_AIRSPEED_BUGS"));   // read-only: never written directly
    }

    [Fact]
    public async Task A_speed_already_set_sends_nothing()
    {
        var mcp = new FakeMcp { Speed = 250 };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_speed_set_in_mach_mode_writes_nothing_and_says_so()
    {
        var mcp = new FakeMcp { Mach = 1 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("MCP is in Mach mode. Set Mach from the Glareshield panel.", said);
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task An_unreadable_mach_flag_does_not_stop_a_knots_set()
    {
        var mcp = new FakeMcp { Mach = null };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
    }

    [Fact]
    public async Task A_speed_set_with_the_target_unreadable_writes_nothing()
    {
        var mcp = new FakeMcp { Speed = null };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_burst_the_aircraft_only_partly_counts_is_corrected_until_it_lands()
    {
        // 10 events count per calculator call, however many were sent: 25 + 25 sent counts 20.
        var mcp = new FakeMcp { Speed = 200, EventsCountedPerCall = 10 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.True(mcp.Writes.Count > 2, "the remainder was sent again");
    }

    [Fact]
    public async Task A_speed_that_does_not_move_is_not_chased()
    {
        // The variable is overwritten by the aircraft: the difference never shrinks, so nothing is
        // re-sent, and the pilot hears the value the window shows, not the one asked for.
        var mcp = new FakeMcp { Speed = 200, EventsCountedPerCall = 0 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 200", said);
        Assert.Equal(2, mcp.Writes.Count);   // the first send only
    }

    [Fact]
    public async Task Corrections_stop_after_three_rounds()
    {
        // Only 5 events count between two waits: the difference shrinks every round, forever.
        var mcp = new FakeMcp { Speed = 200, EventsCountedPerWindow = 5 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        // 1 initial send + 3 corrections, 5 kt each: 220, and no more.
        Assert.Equal("Speed 220", said);
    }

    [Fact]
    public async Task An_overshoot_is_brought_back()
    {
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var overshooting = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 4; }   // 254 after the first call
            },
        };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(overshooting, 250, default));
    }

    [Fact]
    public async Task A_speed_set_reads_again_after_it_waits()
    {
        var mcp = new FakeMcp { Speed = 200 };

        await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.True(mcp.Delays >= 1);
    }

    [Fact]
    public async Task A_burst_that_lands_in_two_parts_is_judged_once_both_have_landed()
    {
        // Half of each call lands at once, the other half 700 ms later. Judged 600 ms after the send
        // the speed reads 224 and the "remaining 26" would be sent again, overshooting to 276.
        var mcp = new FakeMcp { Speed = 200, LagMs = 700 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        Assert.Equal("Speed 250", said);
        Assert.Equal(250, mcp.Speed);
        Assert.Equal(250, mcp.Peak);          // never above the target, even for a moment
        Assert.Equal(2, mcp.Writes.Count);    // the first send only: nothing was left to correct
    }

    [Fact]
    public async Task The_settle_read_gives_up_after_about_a_second_and_a_half_with_the_last_read()
    {
        // The speed keeps moving after the burst (5 kt a read), so two reads never agree.
        var mcp = new FakeMcp { Speed = 200, DriftPerRead = 5 };

        string said = await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default);

        // 600 ms settle + ten 150 ms polls; the last of eleven reads is 250 + 5 * 11 = 305, which is
        // further from 250 than the 50 started with, so nothing is chased.
        Assert.Equal(600 + Bbs757Mcp.SettleGiveUpMs, mcp.Now);
        Assert.Equal("Speed 305", said);
        Assert.Equal(2, mcp.Writes.Count);
    }

    [Fact]
    public async Task An_overshoot_nearer_than_the_start_counts_as_progress()
    {
        // 200 to 250 lands on 256 (the remaining difference changed sign, 6 against 50): brought back.
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var overshooting = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 6; }
            },
        };

        Assert.Equal("Speed 250", await Bbs757Mcp.SetSpeedAsync(overshooting, 250, default));
        Assert.Contains(mcp.Writes, w => w.Contains("(>K:AP_SPD_VAR_DEC)"));
    }

    [Fact]
    public async Task An_overshoot_no_nearer_than_the_start_is_not_chased()
    {
        // 200 to 250 lands on 310 (60 off against 50): another round would only widen it.
        var mcp = new FakeMcp { Speed = 200 };
        var io = mcp.Io;
        bool first = true;
        var wild = new McpIo
        {
            Read = io.Read,
            Delay = io.Delay,
            Write = rpn =>
            {
                io.Write(rpn);
                if (first && rpn.Contains("INC")) { first = false; mcp.Speed += 60; }
            },
        };

        Assert.Equal("Speed 310", await Bbs757Mcp.SetSpeedAsync(wild, 250, default));
        Assert.DoesNotContain(mcp.Writes, w => w.Contains("(>K:AP_SPD_VAR_DEC)"));
    }

    [Theory]
    [InlineData(9999)]
    [InlineData(-5)]
    [InlineData(500.5)]
    [InlineData(double.PositiveInfinity)]
    public async Task A_speed_target_read_outside_zero_to_500_is_unreadable_and_sends_nothing(double wild)
    {
        var mcp = new FakeMcp { Speed = wild };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_speed_target_that_reads_wild_after_the_burst_is_not_corrected()
    {
        var mcp = new FakeMcp { Speed = 200, SpeedAfterSend = 4000 };

        Assert.Equal("Speed unavailable", await Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, default));
        Assert.Equal(2, mcp.Writes.Count);   // the first send only
    }

    [Fact]
    public void A_round_sends_at_most_300_speed_steps_and_140_vertical_speed_steps()
    {
        Assert.Equal(300, Count(Bbs757Mcp.SpeedSteps(0, 399), "(>K:AP_SPD_VAR_INC)"));
        Assert.Equal(300, Count(Bbs757Mcp.SpeedSteps(500, 0), "(>K:AP_SPD_VAR_DEC)"));
        Assert.Equal(299, Count(Bbs757Mcp.SpeedSteps(100, 399), "(>K:AP_SPD_VAR_INC)"));   // the full span fits
        Assert.Equal(140, Count(Bbs757Mcp.VsSteps(-8000, 6000), "(>K:AP_VS_VAR_INC)"));
        Assert.Equal(140, Count(Bbs757Mcp.VsSteps(-1_000_000, 6000), "(>K:AP_VS_VAR_INC)"));
    }

    [Fact]
    public void The_phrases_refuse_a_value_that_is_not_a_speed_or_a_vertical_speed()
    {
        Assert.Equal("Speed 500", Bbs757Mcp.SpeedPhrase(500));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(501));
        Assert.Equal("Speed unavailable", Bbs757Mcp.SpeedPhrase(-1));
        Assert.Equal("Vertical speed 6000", Bbs757Mcp.VsPhrase(6000));
        Assert.Equal("Vertical speed minus 8000", Bbs757Mcp.VsPhrase(-8000));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(6001));
        Assert.Equal("Vertical speed unavailable", Bbs757Mcp.VsPhrase(-8001));
    }

    [Fact]
    public async Task A_vertical_speed_read_outside_the_range_is_unreadable_and_not_stepped()
    {
        var mcp = new FakeMcp { Vs = 1_000_000, DirectWritesAdopted = false };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal("Vertical speed unavailable", said);
        Assert.Equal(new[] { "-1500 (>L:BBS_AP_TARGET_VS)" }, mcp.Writes);   // the direct write only
    }

    [Fact]
    public async Task A_canceled_speed_set_ends_without_speaking()
    {
        var mcp = new FakeMcp { Speed = 200 };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Bbs757Mcp.SetSpeedAsync(mcp.Io, 250, cts.Token));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task A_vertical_speed_the_window_takes_is_not_stepped()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = true };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal(new[] { "-1500 (>L:BBS_AP_TARGET_VS)" }, mcp.Writes);
        Assert.Equal("Vertical speed minus 1500", said);
    }

    [Fact]
    public async Task A_vertical_speed_write_the_window_ignores_is_stepped_by_hundreds()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = false };

        string said = await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default);

        Assert.Equal(15, Count(mcp.Writes, "(>K:AP_VS_VAR_DEC)"));
        Assert.Equal("Vertical speed minus 1500", said);
    }

    [Fact]
    public async Task A_vertical_speed_still_off_after_stepping_is_spoken_as_read()
    {
        var mcp = new FakeMcp { Vs = 0, DirectWritesAdopted = false, EventsCountedPerCall = 0 };

        Assert.Equal("Vertical speed 0", await Bbs757Mcp.SetVerticalSpeedAsync(mcp.Io, -1500, default));
    }

    [Fact]
    public async Task Readouts_speak_fresh_values()
    {
        var mcp = new FakeMcp { Heading = 90, Altitude = 12000, Speed = 250, Vs = -1500 };

        Assert.Equal("Heading 090", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude 12000", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
        Assert.Equal("Speed 250", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Vertical speed minus 1500", await Bbs757Mcp.ReadVerticalSpeedAsync(mcp.Io, default));
        Assert.Empty(mcp.Writes);
    }

    [Fact]
    public async Task The_speed_readout_does_not_pass_a_knots_target_off_as_the_mach_window()
    {
        var mcp = new FakeMcp { Speed = 250, Mach = 1 };

        Assert.Equal(Bbs757Mcp.MachModeReadNote, await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
    }

    [Fact]
    public async Task Unreadable_values_are_spoken_unavailable()
    {
        var mcp = new FakeMcp { Heading = null, Altitude = null, Speed = null, Vs = null };

        Assert.Equal("Heading unavailable", await Bbs757Mcp.ReadHeadingAsync(mcp.Io, default));
        Assert.Equal("Altitude unavailable", await Bbs757Mcp.ReadAltitudeAsync(mcp.Io, default));
        Assert.Equal("Speed unavailable", await Bbs757Mcp.ReadSpeedAsync(mcp.Io, default));
        Assert.Equal("Vertical speed unavailable", await Bbs757Mcp.ReadVerticalSpeedAsync(mcp.Io, default));
    }

    // ---- The definition ---------------------------------------------------------------------

    [Fact]
    public void The_mcp_variables_are_registered_on_request_so_a_fresh_read_can_deliver_them()
    {
        var vars = new BBS757Definition().GetVariables();

        foreach (string key in new[]
                 {
                     Bbs757Mcp.HeadingVar, Bbs757Mcp.AltitudeVar, Bbs757Mcp.SpeedVar,
                     Bbs757Mcp.VsVar, Bbs757Mcp.MachModeVar,
                 })
        {
            Assert.True(vars.ContainsKey(key), key);
            Assert.Equal(key, vars[key].Name);
            Assert.Equal(SimVarType.LVar, vars[key].Type);
            Assert.Equal(UpdateFrequency.OnRequest, vars[key].UpdateFrequency);
            Assert.False(vars[key].IsAnnounced);
            Assert.True(vars[key].ExcludeFromMonitorManager);   // read for the readouts only; a Ctrl+M row would mute nothing
        }
    }

    [Fact]
    public void The_mcp_variables_are_on_no_panel()
    {
        var onPanels = new BBS757Definition().GetPanelControls().SelectMany(p => p.Value).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(Bbs757Mcp.HeadingVar, onPanels);
        Assert.DoesNotContain(Bbs757Mcp.SpeedVar, onPanels);
    }

    [Fact]
    public async Task Every_set_that_cannot_land_is_refused_aloud_and_writes_nothing()
    {
        // Never connected: CalcWriteCanLand is false ([MD11-16]).
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.SetMcpHeadingAsync(90, sim, speech);
        await def.SetMcpSpeedAsync(250, sim, speech);
        await def.SetMcpAltitudeAsync(12000, sim, speech);
        await def.SetMcpVerticalSpeedAsync(-1500, sim, speech);

        Assert.Equal(new[]
        {
            "Heading unavailable", "Speed unavailable", "Altitude unavailable", "Vertical speed unavailable",
        }, speech.All);
    }

    /// <summary>A set held in flight until released, counting how many started and how many were canceled.</summary>
    private sealed class HeldSet
    {
        public readonly TaskCompletionSource<string> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started;
        public int Canceled;

        public Func<McpIo, CancellationToken, Task<string>> Set => async (_, ct) =>
        {
            Started++;
            try { return await Release.Task.WaitAsync(ct); }
            catch (OperationCanceledException) { Canceled++; throw; }
        };
    }

    [Fact]
    public async Task A_repeat_of_the_speed_being_set_is_ignored_but_a_different_speed_supersedes_it()
    {
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();

        Task first = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        Task repeat = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        Assert.True(repeat.IsCompleted);
        Assert.Equal(1, held.Started);   // the repeat started nothing
        Assert.Equal(0, held.Canceled);  // and did not cancel the set in flight

        Task other = def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 240, true, "Speed unavailable", sim, speech, held.Set);
        Assert.Equal(2, held.Started);

        held.Release.SetResult("Speed 240");
        await Task.WhenAll(first, other);

        Assert.Equal(1, held.Canceled);                    // 250 was superseded
        Assert.Equal(new[] { "Speed 240" }, speech.All);   // and said nothing
    }

    [Fact]
    public async Task The_same_speed_can_be_set_again_once_the_first_set_has_finished()
    {
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();
        held.Release.SetResult("Speed 250");

        await def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);
        await def.RunMcpSetAsync(Bbs757Mcp.SpeedVar, 250, true, "Speed unavailable", sim, speech, held.Set);

        Assert.Equal(2, held.Started);
        Assert.Equal(new[] { "Speed 250", "Speed 250" }, speech.All);
    }

    [Fact]
    public async Task A_repeat_of_a_heading_is_not_ignored()
    {
        // Only the slow speed loop is protected; a heading write is instant and idempotent.
        var def = new BBS757Definition { CanWrite = _ => true };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);
        var held = new HeldSet();

        Task first = def.RunMcpSetAsync(Bbs757Mcp.HeadingVar, 90, false, "Heading unavailable", sim, speech, held.Set);
        Task repeat = def.RunMcpSetAsync(Bbs757Mcp.HeadingVar, 90, false, "Heading unavailable", sim, speech, held.Set);

        Assert.Equal(2, held.Started);
        held.Release.SetResult("Heading 090");
        await Task.WhenAll(first, repeat);
        Assert.Equal(new[] { "Heading 090" }, speech.All);
    }

    [Fact]
    public void The_heading_window_and_the_guide_agree_on_the_range()
    {
        // Both say 360 is north: the validator takes it, and the window's label says so.
        Assert.True(Bbs757Mcp.TryHeadingInput("360", out int north, out _));
        Assert.Equal(0, north);
        string guide = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "HotkeyGuides", "BBS757_Hotkeys.txt"));
        Assert.Contains("Set MCP Heading (0-360, 360 is north)", guide);
    }

    [Fact]
    public async Task A_readout_with_no_connection_says_unavailable()
    {
        var def = new BBS757Definition();
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        await def.ReadMcpAsync(MSFSBlindAssist.Hotkeys.HotkeyAction.ReadHeading, sim, speech);

        Assert.Equal(new[] { "Heading unavailable" }, speech.All);
    }
}
