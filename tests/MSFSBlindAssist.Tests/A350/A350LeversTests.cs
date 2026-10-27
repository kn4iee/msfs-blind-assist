using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>The pedestal's levers, written the way the cockpit's own lever code writes them (measured live on the
/// A350-900, package 1.2.7): the flaps through their input event, the thrust levers through INI_TLAn_REQUEST, the
/// speed brake through the aircraft's own key events, the parking brake as a command on its stock state.</summary>
public class A350LeversTests
{
    private static readonly A350ControlMap Map = A350ControlMap.Load();
    private static readonly A350Placement Placement = A350PanelLayout.Place(Map);

    private static A350PlacedRow Row(string key) => Placement.RowsByPanel.Values.SelectMany(r => r).Single(r => r.Key == key);

    private static string Positions(A350PlacedRow row) =>
        string.Join(", ", row.Positions.OrderBy(p => p.Key).Select(p => $"{p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture)}={p.Value}"));

    private static string[] Rpn(A350Plan plan) => plan.Steps.OfType<A350CalcStep>().Select(s => s.Rpn).ToArray();

    [Fact]
    public void Flaps_are_a_lever_with_the_a380s_words_written_through_their_input_event()
    {
        var flaps = Row("A350_FLAPS");
        Assert.Equal(("Flaps", A350RowAction.Set), (flaps.Name, flaps.Action));
        Assert.Equal("L:INI_FLAPS_HANDLE_INDEX", flaps.Control!.StateVar);
        Assert.Equal("0=Up, 1=1, 2=2, 3=3, 4=Full", Positions(flaps));
        Assert.Equal(new[] { "8192 (>B:AIRLINER_Flaps_Set)" }, Rpn(A350WritePlan.ForSet(flaps.Control, 2, 0)));
        Assert.Equal(new[] { "16384 (>B:AIRLINER_Flaps_Set)" }, Rpn(A350WritePlan.ForSet(flaps.Control, 4, 2)));
        Assert.True(A350WritePlan.ForSet(flaps.Control, 2, 2).IsEmpty);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_thrust_lever_goes_to_a_detent_through_its_tla_request(int lever)
    {
        var row = Row($"A350_ENGINE_THROTTLE_{lever}");
        Assert.Equal(($"Thrust lever {lever}", A350RowAction.Set), (row.Name, row.Action));
        Assert.Equal($"L:INI_AUTOTHRUST_TLA:{lever}", row.Control!.StateVar);
        Assert.Equal("-20=Reverse, -6=Reverse idle, 0=Idle, 25=Climb, 35=Flex/MCT, 45=TOGA", Positions(row));
        Assert.Equal(new[] { $"25 (>L:INI_TLA{lever}_REQUEST)" }, Rpn(A350WritePlan.ForSet(row.Control, 25, 0)));
        Assert.Equal(new[] { $"-20 (>L:INI_TLA{lever}_REQUEST)" }, Rpn(A350WritePlan.ForSet(row.Control, -20, 0)));
    }

    [Fact]
    public void All_thrust_levers_move_together_and_read_lever_one()
    {
        var all = Row("A350_THRUST_LEVERS_ALL");
        Assert.Equal(("All thrust levers", A350RowAction.Set), (all.Name, all.Action));
        Assert.Equal("L:INI_AUTOTHRUST_TLA:1", all.Control!.StateVar);
        Assert.Equal(new[] { "45 (>L:INI_TLA1_REQUEST) 45 (>L:INI_TLA2_REQUEST)" }, Rpn(A350WritePlan.ForSet(all.Control, 45, 0)));
        // A lever moved by hand sits between detents: a pick is still sent, the levers are absolute.
        Assert.Equal(new[] { "0 (>L:INI_TLA1_REQUEST) 0 (>L:INI_TLA2_REQUEST)" }, Rpn(A350WritePlan.ForSet(all.Control, 0, 12.3)));
    }

    [Fact]
    public void The_speed_brake_leaves_arm_and_extends_through_the_aircrafts_key_events()
    {
        var sb = Row("A350_FCTL_SPD_BRK_LEVER");
        Assert.Equal(("Speed brake", A350RowAction.Set), (sb.Name, sb.Action));
        Assert.Equal("L:INI_SPOILERS_HANDLE_POSITION", sb.Control!.StateVar);
        Assert.Equal("0=Retracted, 0.25=Quarter, 0.5=Half, 0.75=Three quarters, 1=Full", Positions(sb));
        Assert.Equal(new[] { "'INI.SPOILERS_ARM_OFF' (>F:KeyEvent) 8192 'INI.SPOILERS_SET' (>F:KeyEvent)" },
            Rpn(A350WritePlan.ForSet(sb.Control, 0.5, 0)));
    }

    [Fact]
    public void Ground_spoilers_arm_from_retracted_and_disarm()
    {
        var arm = Row("A350_SPOILERS_ARM");
        Assert.Equal(("Ground spoilers", A350RowAction.Set), (arm.Name, arm.Action));
        Assert.Equal("L:INI_SPOILERS_ARMED", arm.Control!.StateVar);
        Assert.Equal("0=Disarmed, 1=Armed", Positions(arm));
        Assert.Equal(new[] { "0 'INI.SPOILERS_SET' (>F:KeyEvent) 'INI.SPOILERS_ARM_ON' (>F:KeyEvent)" },
            Rpn(A350WritePlan.ForSet(arm.Control, 1, 0)));
        Assert.Equal(new[] { "'INI.SPOILERS_ARM_OFF' (>F:KeyEvent)" }, Rpn(A350WritePlan.ForSet(arm.Control, 0, 1)));
    }

    [Fact]
    public void The_parking_brake_is_a_toggle_sent_only_when_its_known_state_differs()
    {
        var pb = Row("A350_PARK_BRK_PARKING_BRAKE");
        Assert.Equal(("Parking brake", A350RowAction.Set), (pb.Name, pb.Action));
        Assert.Equal(("A:BRAKE PARKING POSITION", "Bool"), (pb.Control!.StateVar, pb.Control.StateUnit));
        Assert.Equal("0=Off, 1=On", Positions(pb));
        Assert.Equal(new[] { "1 (>B:AIRLINER_PARK_BRK_PARKING_BRAKE_Set)" }, Rpn(A350WritePlan.ForSet(pb.Control, 1, 0)));
        Assert.True(A350WritePlan.ForSet(pb.Control, 1, 1).IsEmpty);
        Assert.Equal(A350WritePlan.UnknownPositionRefusal, A350WritePlan.ForSet(pb.Control, 1, null).Refusal);
    }

    /// <summary>The rudder trim switch trims while it is held off center (about 1.6 degrees a second, live): as on the
    /// A380 and the Fenix, two nudge buttons, never a combo left trimming.</summary>
    [Theory]
    [InlineData("A350_RUDDER_TRIM_LEFT", "Rudder trim left (nudge)", 2)]
    [InlineData("A350_RUDDER_TRIM_RIGHT", "Rudder trim right (nudge)", 0)]
    public void Rudder_trim_nudges_hold_the_switch_then_return_it(string key, string name, int side)
    {
        var nudge = Row(key);
        Assert.Equal((name, A350RowAction.Press), (nudge.Name, nudge.Action));
        var plan = A350WritePlan.ForPress(nudge.Control!);
        Assert.Equal(new A350Step[]
        {
            new A350CalcStep($"{side} (>B:AIRLINER_RUDDER_TRIM_Set)"),
            new A350DelayStep(A350Levers.NudgeMs),
            new A350CalcStep("1 (>B:AIRLINER_RUDDER_TRIM_Set)"),
        }, plan.Steps);
        Assert.DoesNotContain(Placement.RowsByPanel.Values.SelectMany(r => r), r => r.Key == "A350_RUDDER_TRIM");
    }

    /// <summary>The A380's "System display page" picker: the A350 has a key per page, so a pick presses that page's
    /// key through its input event (a hold: 2, then 0); the page is INI_ECAM_ACTIVE_PAGE (live 2026-10-27).</summary>
    [Fact]
    public void The_system_display_page_is_picked_by_pressing_its_key()
    {
        var page = Row("A350_SD_PAGE");
        Assert.Equal(("System display page", A350RowAction.Set), (page.Name, page.Action));
        Assert.Equal("L:INI_ECAM_ACTIVE_PAGE", page.Control!.StateVar);
        Assert.Equal("1=Engine, 2=Hyd, 3=Elec DC, 4=Elec AC, 5=Bleed, 6=Cond, 7=Press, 8=Fuel, 9=APU, 10=F/Ctl, 11=Door, 12=Wheel, "
            + "14=Status, 16=Video", Positions(page));
        Assert.Equal(new A350Step[]
        {
            new A350CalcStep("2 (>B:AIRLINER_ECAM_ENG_Set)"),
            new A350DelayStep(A350WritePlan.HoldMs),
            new A350CalcStep("0 (>B:AIRLINER_ECAM_ENG_Set)"),
        }, A350WritePlan.ForSet(page.Control, 1, 12).Steps);
        Assert.Equal("2 (>B:AIRLINER_ECAM_ELDC_Set)", Rpn(A350WritePlan.ForSet(page.Control, 3, 12))[0]);
        Assert.True(A350WritePlan.ForSet(page.Control, 12, 12).IsEmpty);
    }

    /// <summary>The pitch trim switches trim the THS while held, each alone or both together (live 2026-10-27): two
    /// nudges move both together, never a combo left trimming.</summary>
    [Theory]
    [InlineData("A350_PITCH_TRIM_UP", "Pitch trim nose up (nudge)", 0)]
    [InlineData("A350_PITCH_TRIM_DOWN", "Pitch trim nose down (nudge)", 2)]
    public void Pitch_trim_nudges_hold_both_switches_then_return_them(string key, string name, int side)
    {
        var nudge = Row(key);
        Assert.Equal((name, A350RowAction.Press), (nudge.Name, nudge.Action));
        Assert.Equal(new A350Step[]
        {
            new A350CalcStep($"{side} (>B:AIRLINER_PITCH_TRIM_LEFT_Set) {side} (>B:AIRLINER_PITCH_TRIM_RIGHT_Set)"),
            new A350DelayStep(A350Levers.PitchNudgeMs),   // about 0.2 degrees: the THS trims about 0.19 degrees a second
            new A350CalcStep("1 (>B:AIRLINER_PITCH_TRIM_LEFT_Set) 1 (>B:AIRLINER_PITCH_TRIM_RIGHT_Set)"),
        }, A350WritePlan.ForPress(nudge.Control!).Steps);
        Assert.DoesNotContain(Placement.RowsByPanel.Values.SelectMany(r => r), r => r.Key is "A350_PITCH_TRIM_LEFT" or "A350_PITCH_TRIM_RIGHT");
    }

    /// <summary>The sidestick takeover button's Set fires the aircraft's own 'INI.AUTOPILOT_OFF' key event: a button.</summary>
    [Theory]
    [InlineData("A350_SIDE_STICK_DISCO_CPT", "Captain sidestick takeover", "AIRLINER_SIDE_STICK_DISCO_CPT_Set")]
    [InlineData("A350_SIDE_STICK_DISCO_FO", "First officer sidestick takeover", "AIRLINER_SIDE_STICK_DISCO_FO_Set")]
    public void A_sidestick_takeover_button_disconnects_the_autopilot(string key, string name, string evt)
    {
        var button = Row(key);
        Assert.Equal((name, A350RowAction.Press), (button.Name, button.Action));
        Assert.Equal(new[] { $"1 (>B:{evt})" }, Rpn(A350WritePlan.ForPress(button.Control!)));
    }

    [Fact]
    public void Rudder_trim_reset_is_a_button()
    {
        var reset = Row("A350_RUDDER_TRIM_RESET");
        Assert.Equal(("Rudder trim reset", A350RowAction.Press), (reset.Name, reset.Action));
        Assert.Equal(new[] { "1 (>B:AIRLINER_RUDDER_TRIM_RESET_Set)" }, Rpn(A350WritePlan.ForPress(reset.Control!)));
    }
}
