using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Aircraft.A330.Sweep;
using Xunit;
using Control = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Control;
using Position = MSFSBlindAssist.Aircraft.A330.A330ControlMap.Position;
using ProofInfo = MSFSBlindAssist.Aircraft.A330.A330ControlMap.ProofInfo;

namespace MSFSBlindAssist.Tests;

public class A330PanelSweepPlannerTests
{
    private static Position P(double value, double input, string label) => new() { Value = value, Input = input, Label = label };

    private static Control Select(string key, params Position[] ps) => new()
    {
        Key = key, Label = key, Kind = "select", Event = "EV_" + key, Read = "INI_" + key, Positions = ps.ToList(),
    };

    private static Control Guard(string key) => Select(key, P(0, 0, "Closed"), P(1, 1, "Open"));

    private static SweepPlan Plan(IEnumerable<Control> controls, Dictionary<string, double?>? starts = null,
        Dictionary<string, string>? denied = null, Dictionary<string, string>? pageKeys = null) =>
        SweepPlanner.Plan(controls.ToList(), starts ?? new(), denied ?? new(), pageKeys ?? new());

    private static ControlPlan For(SweepPlan plan, string key) => plan.Controls.Single(c => c.Key == key);

    [Fact]
    public void A_selector_visits_every_other_position_and_returns()
    {
        // Three positions, read 0/1/2 written with inputs 1/0/2; it starts on the middle one (read 1).
        var sel = Select("SEL", P(0, 1, "A"), P(1, 0, "B"), P(2, 2, "C"));
        var plan = For(Plan(new[] { sel }, new() { ["SEL"] = 1 }), "SEL");

        Assert.Null(plan.SkipReason);
        Assert.Equal(new[] { SweepStepKind.SetPosition, SweepStepKind.SetPosition, SweepStepKind.ReturnToStart },
            plan.Steps.Select(s => s.Kind).ToArray());
        Assert.Equal(new double?[] { 1, 2, 0 }, plan.Steps.Select(s => s.Input).ToArray());
        Assert.Equal(new double?[] { 0, 2, 1 }, plan.Steps.Select(s => s.ExpectedRead).ToArray());
    }

    [Fact]
    public void A_denied_control_is_skipped_with_its_reason()
    {
        var sel = Select("BAT", P(1, 0, "Auto"), P(0, 1, "Off"));
        var plan = For(Plan(new[] { sel }, new() { ["BAT"] = 1 }, new() { ["BAT"] = "the cockpit goes dark" }), "BAT");

        Assert.Empty(plan.Steps);
        Assert.Equal("denied: the cockpit goes dark", plan.SkipReason);
    }

    [Fact]
    public void A_guard_is_opened_only_to_reach_its_switch_and_closed_again()
    {
        var guard = Guard("COVER");
        var sw = Select("SW", P(0, 0, "Off"), P(1, 1, "On"));
        sw.Guard = "COVER";
        var plan = Plan(new[] { guard, sw }, new() { ["COVER"] = 0, ["SW"] = 0 });

        var guardPlan = For(plan, "COVER");
        Assert.Empty(guardPlan.Steps);
        Assert.NotNull(guardPlan.SkipReason);

        var kinds = For(plan, "SW").Steps.Select(s => s.Kind).ToArray();
        Assert.Equal(new[] { SweepStepKind.OpenGuard, SweepStepKind.SetPosition, SweepStepKind.ReturnToStart, SweepStepKind.CloseGuard }, kinds);
        Assert.All(For(plan, "SW").Steps.Where(s => s.Kind is SweepStepKind.OpenGuard or SweepStepKind.CloseGuard),
            s => Assert.Equal("COVER", s.ControlKey));

        // A guard that was already open is left open: the sweep puts back what it found.
        var open = For(Plan(new[] { guard, sw }, new() { ["COVER"] = 1, ["SW"] = 0 }), "SW");
        Assert.DoesNotContain(open.Steps, s => s.Kind is SweepStepKind.OpenGuard or SweepStepKind.CloseGuard);
    }

    [Fact]
    public void A_button_with_no_state_is_skipped()
    {
        var noState = new Control { Key = "CALL", Label = "Call", Kind = "button", Event = "EV", Mode = "push" };
        var withState = new Control { Key = "ECAM", Label = "ECAM", Kind = "button", Event = "EV2", Mode = "push", State = "INI_X" };
        var plan = Plan(new[] { noState, withState });

        Assert.Equal("no readable state", For(plan, "CALL").SkipReason);
        Assert.Equal(new[] { SweepStepKind.Press }, For(plan, "ECAM").Steps.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void A_proof_on_a_lower_ecam_page_selects_the_page_first()
    {
        var guard = Guard("COVER");
        var sw = Select("PUMP", P(0, 0, "Off"), P(1, 1, "On"));
        sw.Guard = "COVER";
        sw.Proof = new ProofInfo { Kind = "sd_page", Page = "FUEL", Text = "PUMP" };
        var pageKeys = new Dictionary<string, string> { ["FUEL"] = "ECAM_FUEL_KEY" };
        var plan = For(Plan(new[] { guard, sw }, new() { ["COVER"] = 0, ["PUMP"] = 0 }, pageKeys: pageKeys), "PUMP");

        Assert.Equal(SweepStepKind.PressEcamPage, plan.Steps[0].Kind);
        Assert.Equal("ECAM_FUEL_KEY", plan.Steps[0].ControlKey);
        Assert.Equal(SweepStepKind.OpenGuard, plan.Steps[1].Kind);

        var missing = For(Plan(new[] { guard, sw }, new() { ["COVER"] = 0, ["PUMP"] = 0 }), "PUMP");
        Assert.Empty(missing.Steps);
        Assert.Contains("FUEL", missing.SkipReason);
    }

    [Fact]
    public void Every_control_is_planned_or_skipped_once_in_the_maps_order_and_unknown_starts_are_never_guessed()
    {
        var a = Select("A", P(0, 0, "x"), P(1, 1, "y"));
        var status = new Control { Key = "S", Kind = "status", Read = "INI_S" };
        var cont = new Control { Key = "C", Kind = "continuous", Event = "E", Read = "INI_C", Min = 0, Max = 100, Step = 5 };
        var plan = Plan(new[] { a, status, cont }, new() { ["C"] = 30 });

        Assert.Equal(new[] { "A", "S", "C" }, plan.Controls.Select(c => c.Key).ToArray());
        Assert.Equal("start not read", For(plan, "A").SkipReason);
        Assert.Equal("read-only row", For(plan, "S").SkipReason);
        Assert.Equal(new double?[] { 0, 50, 100, 30 }, For(plan, "C").Steps.Select(s => s.Input).ToArray());
        Assert.Equal(SweepStepKind.ReturnToStart, For(plan, "C").Steps[^1].Kind);
    }

    [Fact]
    public void A_multi_press_key_is_restored_by_its_presses_to_cycle()
    {
        var chrono = new Control { Key = "CH", Kind = "button", Event = "E", Mode = "push", State = "INI_CH", PressesToCycle = 3 };
        var steps = For(Plan(new[] { chrono }), "CH").Steps;

        Assert.Equal(new[] { SweepStepKind.Press, SweepStepKind.RestorePress, SweepStepKind.RestorePress },
            steps.Select(s => s.Kind).ToArray());
    }

    [Fact]
    public void Deny_list_text_parses_key_tab_reason_and_refuses_a_line_without_a_reason()
    {
        var d = DenyList.Parse("# comment\n\nA330_X\tcalls the cabin\r\nA330_Y\tthe cockpit goes dark\n");
        Assert.Equal("calls the cabin", d["A330_X"]);
        Assert.Equal(2, d.Count);
        Assert.Throws<FormatException>(() => DenyList.Parse("A330_X\n"));
        Assert.Throws<FormatException>(() => DenyList.Parse("A330_X\tone\nA330_X\ttwo\n"));
    }

    [Fact]
    public void Every_key_in_the_deny_list_file_is_a_control_the_aircraft_offers()
    {
        string root = RepoRoot();
        var denied = DenyList.Parse(File.ReadAllText(Path.Combine(root, "tools", "a330-gen", "PanelSweep", "denylist.txt")));
        var known = new HashSet<string>(A330ControlMap.Embedded.Controls.Select(c => c.Key));
        foreach (var (_, _, key) in IniA330Definition.OwnPlacedControls) known.Add(key);

        Assert.NotEmpty(denied);
        Assert.Empty(denied.Keys.Where(k => !known.Contains(k)));
    }

    // What the sweep must never drive: deleting a line of the deny list for one of these must fail a test.
    private static readonly string[] MustDeny =
    {
        "A330_ENG1_MASTER", "A330_ENG2_MASTER", "A330_ENG_MODE", "A330_MAN_START1", "A330_MAN_START2",
        "A330_ENG1_FIRE", "A330_ENG2_FIRE", "A330_APU_FIRE",
        "A330_ENG1_AGENT1", "A330_ENG1_AGENT2", "A330_ENG2_AGENT1", "A330_ENG2_AGENT2", "A330_APU_AGENT",
        "A330_ENG_FIRE_TEST", "A330_APU_FIRE_TEST", "A330_CARGO_SMOKE_TEST", "A330_FWD_CARGO_AGENT", "A330_AFT_CARGO_AGENT",
        "A330_IDG1", "A330_IDG2", "A330_RAT",
        "A330_IRS1_MODE", "A330_IRS2_MODE", "A330_IRS3_MODE", "A330_IR1", "A330_IR2", "A330_IR3",
        "A330_BAT1", "A330_BAT2", "A330_APU_BAT", "A330_EXT_A", "A330_EXT_B",
        "A330_APU_MASTER", "A330_APU_START", "A330_DITCHING", "A330_EMER_EXIT_LT",
        "A330_CALL_ALL", "A330_CALL_FWD", "A330_CALL_MID", "A330_CALL_AFT", "A330_CALL_PURS", "A330_CALL_EXIT", "A330_MECH_CALL",
        "A330_ECAM_EMER_CANC",
        "A330_RMP1_NAV", "A330_CLOCK_RST", "A330_ISIS_RST", "A330_DFDR_EVENT", "A330_CPT_RAIN_RPL",
        "A330_GEAR", "A330_FLAPS", "A330_SPDBRK", "A330_SPLR_ARM", "A330_PARKBRK",
    };

    [Fact]
    public void The_controls_that_cannot_be_undone_or_move_the_aircraft_stay_in_the_deny_list()
    {
        var denied = DenyList.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "tools", "a330-gen", "PanelSweep", "denylist.txt")));
        Assert.Empty(MustDeny.Where(k => !denied.ContainsKey(k)));
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

}
