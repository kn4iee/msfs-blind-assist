using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// The A350 First Officer's data against the aircraft (FO-3, FO-6, FO-7): every field it reads and every key it writes is
/// one the definition registers, every value it writes is a position that row has, every flow latches its own group and
/// the aircraft's own checklist behind it, and every line it can read or act on has a delivering step.
/// </summary>
public class A350FoStructureTests
{
    private static readonly IniBuildsA350Definition Definition = new();

    /// <summary>The executor's own names, each with where it goes.</summary>
    private static readonly Dictionary<string, string> ExecutorKeys = new()
    {
        [A350ActionExecutor.FireTestKey] = "the held fire test",
        [A350ActionExecutor.ToConfigTestKey] = "the held T.O CONFIG press",
        [A350ActionExecutor.BaroStdKey] = "both baro knobs pushed",
        [A350ActionExecutor.BaroQnhKey] = "both baro knobs pulled",
        [A350ActionExecutor.BaroUnitHectopascalsKey] = "the unit selectors reading inches, pressed",
        [A350ActionExecutor.ExternalPowerKey] = "the EXT 1 and 2 buttons, pressed where they differ",
    };

    private static bool IsSynthetic(string field) => field.StartsWith("FO_", StringComparison.Ordinal);

    private static IEnumerable<string> ReadFields()
    {
        foreach (var item in A350ChecklistDefinitions.Build().SelectMany(g => g.Items))
        {
            if (item.StateFieldName != null) yield return item.StateFieldName;
            foreach (var field in item.AdditionalStateFields) yield return field;
        }
        foreach (var step in A350FlowDefinitions.Build().SelectMany(f => f.Steps))
        {
            if (step.ConditionFieldName != null) yield return step.ConditionFieldName;
            if (step.VerifyFieldName != null) yield return step.VerifyFieldName;
        }
    }

    [Fact]
    public void Every_field_it_reads_is_registered()
    {
        var variables = Definition.GetVariables();
        var missing = ReadFields().Where(f => !IsSynthetic(f) && !variables.ContainsKey(f)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Not registered: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_input_of_its_evaluator_is_registered()
    {
        var variables = Definition.GetVariables();
        var missing = A350StateEvaluator.Inputs.Where(i => !variables.ContainsKey(Definition.KeyForInput(i.Name))).Select(i => i.Name).ToList();
        Assert.True(missing.Count == 0, "Not streamed: " + string.Join(", ", missing));
    }

    private static IEnumerable<string> WrittenKeys() =>
        FoFbwUnclaimedEventKeyTests.FlowWrittenKeys(A350FlowDefinitions.Build())
            .Concat(FoFbwUnclaimedEventKeyTests.KeyLiteralsPassedToWrites(
                FoFbwUnclaimedEventKeyTests.ChecklistSourcePath("A350", "A350ChecklistDefinitions.cs")))
            .Concat(FoFbwUnclaimedEventKeyTests.KeyLiteralsPassedToWrites(
                FoFbwUnclaimedEventKeyTests.ExecutorSourcePath("A350", "A350ActionExecutor.cs")));

    [Fact]
    public void Every_key_it_writes_is_a_row_or_the_executors_own()
    {
        var variables = Definition.GetVariables();
        var missing = WrittenKeys().Where(k => !variables.ContainsKey(k) && !ExecutorKeys.ContainsKey(k)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Neither a row nor the executor's own: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_flow_writes_a_position_the_row_has()
    {
        var variables = Definition.GetVariables();
        var problems = new List<string>();
        foreach (var flow in A350FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(FlowChecklistLinkAudit.IsWrite))
                foreach (var (key, target) in FlowChecklistLinkAudit.Writes(step))
                    if (variables.TryGetValue(key, out var def) && def.ValueDescriptions is { Count: > 0 } positions && !positions.ContainsKey(target))
                        problems.Add($"{flow.Id}/{step.Id} writes {key} = {target}, which has {string.Join(",", positions.Keys)}");
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    private static List<(FlowDefinition<A350StateEvaluator> Flow, List<ChecklistItem<A350ActionExecutor, A350StateEvaluator>> Items)> FlowsWithItems()
    {
        var groups = A350ChecklistDefinitions.Build();
        var ids = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        return FlowChecklistLinkAudit.FlowsWithItems(A350FlowDefinitions.Build(), groups, f => f.CompletionGroupIds(ids.Contains));
    }

    [Fact]
    public void No_line_in_a_latched_group_is_left_without_a_delivering_step()
    {
        var undelivered = FlowChecklistLinkAudit.UndeliveredLines(FlowsWithItems());
        Assert.True(undelivered.Count == 0, "Latched with no step behind them: " + string.Join(", ", undelivered));
    }

    [Fact]
    public void Each_flow_latches_its_action_group_and_the_aircrafts_own_checklist()
    {
        var ids = A350ChecklistDefinitions.Build().Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var expected = new Dictionary<string, string[]>
        {
            ["COCKPIT_PREP"] = new[] { "COCKPIT_PREP" },
            ["BEFORE_START"] = new[] { "BEFORE_START_CL", "BEFORE_START" },
            ["ENGINE_START"] = new[] { "ENGINE_START" },
            ["AFTER_START"] = new[] { "AFTER_START_CL", "AFTER_START" },
            ["TAXI"] = new[] { "TAXI" },
            ["LINEUP"] = new[] { "BEFORE_TAKEOFF_CL", "LINEUP" },
            ["AFTER_TAKEOFF"] = new[] { "AFTER_TAKEOFF_CL", "AFTER_TAKEOFF" },
            ["APPROACH"] = new[] { "APPROACH_CL", "APPROACH" },
            ["LANDING"] = new[] { "LANDING_CL", "LANDING" },
            ["AFTER_LANDING"] = new[] { "AFTER_LANDING_CL", "AFTER_LANDING" },
            ["PARKING"] = new[] { "PARKING_CL", "PARKING" },
            ["SECURE"] = new[] { "SECURING_CL", "SECURE" },
        };
        var flows = A350FlowDefinitions.Build();
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), flows.Select(f => f.Id).OrderBy(k => k, StringComparer.Ordinal));
        foreach (var flow in flows)
            Assert.Equal(expected[flow.Id], flow.CompletionGroupIds(ids.Contains).ToArray());
    }

    // The APU START presses have no line of their own: "APU: ON" is the master's, and the AVAIL waits carry the start.
    private static readonly HashSet<string> WritesWithNoLine = new(StringComparer.Ordinal) { "BS_APU_START", "AL_APU_START" };

    [Fact]
    public void Every_write_step_completes_its_own_action_line()
    {
        var groups = A350ChecklistDefinitions.Build().ToDictionary(g => g.Id);
        var problems = new List<string>();
        foreach (var flow in A350FlowDefinitions.Build())
            foreach (var step in flow.Steps.Where(FlowChecklistLinkAudit.IsWrite))
            {
                bool hasLine = groups.TryGetValue(flow.Id, out var own) && own.Items.Any(i => i.Id == step.Id);
                if (WritesWithNoLine.Contains(step.Id))
                {
                    if (hasLine) problems.Add($"{flow.Id}/{step.Id} has an action line now");
                    if (step.LinkedChecklistItemIds.Any()) problems.Add($"{flow.Id}/{step.Id} links a line it does not set");
                }
                else if (!hasLine)
                    problems.Add($"{flow.Id}/{step.Id} has no action line of its own");
                else if (step.CompletesChecklistItemId != step.Id)
                    problems.Add($"{flow.Id}/{step.Id} completes {step.CompletesChecklistItemId ?? "nothing"}, not its own line");
            }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_linked_line_is_in_a_group_its_flow_latches()
    {
        var problems = new List<string>();
        foreach (var (flow, items) in FlowsWithItems())
        {
            var latched = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds.Where(id => !latched.Contains(id)))
                    problems.Add($"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // FO-3: a read-back group never acts.
    [Fact]
    public void A_read_back_group_is_action_free()
    {
        foreach (var group in A350ChecklistDefinitions.Build().Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)))
            Assert.All(group.Items, item => Assert.Null(item.CheckAction));
    }

    // FO-14: the landing autobrake is the Captain's; RTO before takeoff and off after takeoff and landing stay automated.
    [Fact]
    public void The_landing_autobrake_is_the_captains()
    {
        var approach = A350FlowDefinitions.Build().Single(f => f.Id == "APPROACH");
        Assert.DoesNotContain(approach.Steps, s => FlowChecklistLinkAudit.IsWrite(s) && FlowChecklistLinkAudit.Writes(s).Any(w => w.Event == "A350_LDG_AUTO_BRK"));
        Assert.Contains(approach.Steps, s => s.ActionType == FlowStepActionType.CaptainReminder && s.Id == "AP_AUTOBRAKE");
    }

    // FO-13: the gear lines are read-only, the last step of the flow that latches them.
    [Theory]
    [InlineData("LANDING", "LD_GEAR_DOWN_CHECK", "LDC_GEAR")]
    [InlineData("AFTER_TAKEOFF", "AT_GEAR_UP_CHECK", "ATC_GEAR")]
    public void A_gear_line_is_a_read_only_check_and_comes_last(string flowId, string stepId, string line)
    {
        var steps = A350FlowDefinitions.Build().Single(f => f.Id == flowId).Steps;
        var last = steps[^1];
        Assert.Equal(stepId, last.Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, last.ActionType);
        Assert.Equal(FlowStepFailurePolicy.Skip, last.FailurePolicy);
        Assert.Equal(20, last.TimeoutSeconds);
        Assert.Equal(new[] { line }, last.LinkedChecklistItemIds.ToArray());
    }

    // The aircraft's own checklist lines, in its own order (A35K.CHECKLISTS, 1.2.7).
    [Theory]
    [InlineData("BEFORE_START_CL", "Cockpit preparation: COMPLETED", "Gear pins and covers: REMOVED", "Fuel quantity: SET", "Seat belts: ON", "ADIRS: NAV",
        "Baro reference: SET", "T.O data: SET", "Doors: CLOSED and ARMED", "Thrust levers: IDLE", "Parking brake: AS REQUIRED", "Beacon: ON")]
    [InlineData("AFTER_START_CL", "Anti-ice: AS REQUIRED", "ECAM status: NORMAL", "Pitch trim: T.O", "Rudder trim: NEUTRAL")]
    [InlineData("AFTER_TAKEOFF_CL", "Landing gear: UP", "Flaps: 0", "Packs: ON", "Baro reference: SET")]
    [InlineData("APPROACH_CL", "Briefing: CONFIRMED", "ECAM status: NORMAL", "Seat belts: ON", "Baro reference: SET", "Minimum: SET")]
    [InlineData("LANDING_CL", "Cabin crew: ADVISED", "Autothrust: SPEED or OFF", "Autobrake: AS REQUIRED", "Cabin: READY", "Runway condition: SET", "Seat belts: ON",
        "Ground spoilers: ARM", "Landing gear: DOWN", "Flaps: LANDING")]
    [InlineData("AFTER_LANDING_CL", "Ground spoilers: DISARM", "Flaps: 0", "APU: START")]
    [InlineData("PARKING_CL", "Parking brake or chocks: AS REQUIRED", "All engines: OFF", "Seat belts: OFF", "Exterior lights: AS REQUIRED", "Fuel pumps: OFF", "APU bleed: ON", "Transponder: STANDBY")]
    [InlineData("SECURING_CL", "Crew oxygen supply: OFF", "ADIRS: OFF", "Emergency exit light: OFF", "Signs: OFF", "APU bleed: OFF", "APU: AS REQUIRED", "All laptops: OFF",
        "Logbooks: COMPLETED", "All 4 batteries: AS REQUIRED")]
    public void A_read_back_says_the_aircrafts_own_lines(string groupId, params string[] lines)
    {
        var group = A350ChecklistDefinitions.Build().Single(g => g.Id == groupId);
        Assert.Equal(lines, group.Items.Select(i => i.Label).ToArray());
    }

    [Fact]
    public void The_before_takeoff_read_back_says_the_aircrafts_own_lines() =>
        Assert.Equal(new[]
        {
            "Flight controls: CHECKED", "Flight instruments: CHECKED", "Briefing: CONFIRMED", "Flaps setting: SET", "V1, VR, V2 and thrust rating: SET", "Squawk: SET",
            "T.O runway: CONFIRMED", "Cabin crew: ADVISED", "Autobrake: RTO", "Seat belts: ON", "Ground spoilers: ARM", "Flaps: T.O", "T.O config: NORMAL", "Cabin: READY", "Packs: OFF if required",
        }, A350ChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_TAKEOFF_CL").Items.Select(i => i.Label).ToArray());
}
