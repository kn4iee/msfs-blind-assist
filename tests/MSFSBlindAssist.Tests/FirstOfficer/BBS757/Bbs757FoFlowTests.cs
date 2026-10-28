using System;
using System.Collections.Generic;
using System.Linq;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.FirstOfficer.BBS757;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using Xunit;
using K = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoKeys;
using X = MSFSBlindAssist.FirstOfficer.BBS757.Bbs757FoActionExecutor;

namespace MSFSBlindAssist.Tests.FirstOfficer.BBS757;

/// <summary>
/// Plan 10, task 6: the 757 flows. Every line a flow latches has a step behind it (FO-6, FO-7), the
/// engine start runs GND, the fuel point, fuel, the cut-out and the engine running in that order,
/// and the gear checks are the read-only last steps of their flows (FO-13).
/// </summary>
public class Bbs757FoFlowTests
{
    private static readonly List<FlowDefinition<Bbs757FoStateEvaluator>> Flows = Bbs757FoFlowDefinitions.Build();
    private static readonly List<ChecklistGroup<X, Bbs757FoStateEvaluator>> Groups = Bbs757FoChecklistDefinitions.Build();
    private static readonly BBS757Definition Def = new();

    private static FlowDefinition<Bbs757FoStateEvaluator> Flow(string id) => Flows.Single(f => f.Id == id);

    private static List<(FlowDefinition<Bbs757FoStateEvaluator> Flow, List<ChecklistItem<X, Bbs757FoStateEvaluator>> Items)> Latched()
        => FlowChecklistLinkAudit.FlowsWithItems(Flows, Groups, f => f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    private static IEnumerable<(string Flow, FlowStep<Bbs757FoStateEvaluator> Step, string Key, int? Target)> Writes()
    {
        foreach (var f in Flows)
            foreach (var s in f.Steps)
            {
                if (s.ActionType == FlowStepActionType.SetSwitch && s.EventName != null)
                    yield return (f.Id, s, s.EventName, s.TargetValue);
                else if (s.ActionType == FlowStepActionType.SetSwitchMultiple)
                    foreach (var (ev, tv) in s.MultiActions) yield return (f.Id, s, ev, tv);
            }
    }

    [Fact]
    public void FlowIds_InFlightOrder()
        => Assert.Equal(new[] { "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START", "BEFORE_TAKEOFF",
            "AFTER_TAKEOFF", "DESCENT", "BEFORE_LANDING", "AFTER_LANDING", "SHUTDOWN", "SECURE" }, Flows.Select(f => f.Id));

    [Fact]
    public void StepIds_AreUniqueAcrossFlows()
    {
        var ids = Flows.SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryFlowRelatedGroup_Exists()
    {
        var ids = Groups.Select(g => g.Id).ToHashSet();
        foreach (var f in Flows)
            foreach (var gid in f.RelatedChecklistGroupIds)
                Assert.True(ids.Contains(gid), $"{f.Id} names group {gid}, which does not exist");
    }

    // ------------------------------------------------------------------ FO-6 / FO-7

    /// <summary>
    /// Every line in a group a flow latches has a step behind it — a write that sets it, a read-only
    /// check that reads it, or a Captain reminder — so a finished flow never latches a line the
    /// aircraft does not show. The only exceptions are the two StayComplete start-selector lines,
    /// which latch on the engine's N2 past the starter and are never linked (FOB-1).
    /// </summary>
    [Fact]
    public void Only_the_start_selector_lines_are_latched_with_no_step_behind_them()
        => Assert.Equal(new[] { "ENGINE_START/ES_E1_GND", "ENGINE_START/ES_E2_GND" },
            FlowChecklistLinkAudit.UndeliveredLines(Latched()));

    [Fact]
    public void No_step_links_a_StayComplete_start_selector_line()
    {
        var stay = Groups.SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "ES_E1_GND", "ES_E2_GND" }, stay);
        foreach (var flow in Flows)
            foreach (var step in flow.Steps)
                Assert.False(step.LinkedChecklistItemIds.Any(stay.Contains),
                    $"{flow.Id}/{step.Id} links a StayComplete line: a GND is not a started engine");
    }

    [Fact]
    public void Every_link_names_a_line_of_a_group_its_flow_latches()
    {
        foreach (var (flow, items) in Latched())
        {
            var ids = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(ids.Contains(id), $"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
    }

    /// <summary>A step's primary link is its own action group's line, never only a read-back line.</summary>
    [Fact]
    public void A_step_that_completes_an_action_line_names_its_own_group()
    {
        foreach (var flow in Flows)
        {
            var own = Groups.SingleOrDefault(g => g.Id == flow.Id);
            if (own == null) continue;
            var ownIds = own.Items.Select(i => i.Id).ToHashSet();
            foreach (var step in flow.Steps.Where(s => s.CompletesChecklistItemId != null))
            {
                bool isReadBack = !ownIds.Contains(step.CompletesChecklistItemId!);
                if (isReadBack)
                    Assert.True(Groups.Any(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)
                                                && g.Items.Any(i => i.Id == step.CompletesChecklistItemId)),
                        $"{flow.Id}/{step.Id} completes {step.CompletesChecklistItemId}, in neither its own group nor a read-back group");
            }
        }
    }

    /// <summary>A read-only check never writes: every line it completes reads the field it waits on.</summary>
    [Fact]
    public void A_read_only_check_completes_only_lines_that_read_its_own_field()
    {
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var flow in Flows)
            foreach (var step in flow.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition
                                                        && s.Id.Contains("_CHK_", StringComparison.Ordinal)))
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(items[id].StateFieldName == step.ConditionFieldName,
                        $"{flow.Id}/{step.Id} reads {step.ConditionFieldName} but completes {id}, which reads {items[id].StateFieldName}");
    }

    /// <summary>
    /// A flow and its action group are one procedure in two forms, so they list the same lines in
    /// the same order (a Captain step stands in for its reminder line). A line two consecutive steps
    /// deliver (the APU start and its wait for running, both "APU: running") is one line.
    /// </summary>
    [Fact]
    public void EveryActionGroup_ListsTheSameLinesInTheSameOrderAsItsFlow()
    {
        foreach (var f in Flows)
        {
            var group = Groups.SingleOrDefault(g => g.Id == f.Id);
            if (group == null) continue;
            var itemIds = group.Items.Select(i => i.Id).ToHashSet();
            var fromFlow = new List<string>();
            foreach (var id in f.Steps.Select(s => s.CompletesChecklistItemId ?? s.Id).Where(itemIds.Contains))
                if (fromFlow.Count == 0 || fromFlow[^1] != id) fromFlow.Add(id);
            Assert.True(group.Items.Select(i => i.Id).SequenceEqual(fromFlow),
                $"{f.Id}: group [{string.Join(", ", group.Items.Select(i => i.Id))}] vs flow [{string.Join(", ", fromFlow)}]");
        }
    }

    /// <summary>Every line of a flow's own action group is delivered by exactly one of its steps (order aside).</summary>
    [Fact]
    public void EveryActionGroupLine_IsDeliveredOnceByItsFlow()
    {
        var stay = new[] { "ES_E1_GND", "ES_E2_GND" };
        foreach (var f in Flows)
        {
            var group = Groups.SingleOrDefault(g => g.Id == f.Id);
            if (group == null) continue;
            foreach (var item in group.Items.Where(i => !stay.Contains(i.Id)))
                Assert.True(f.Steps.Count(s => s.CompletesChecklistItemId == item.Id) == 1,
                    $"{f.Id}/{item.Id}: completed by {f.Steps.Count(s => s.CompletesChecklistItemId == item.Id)} steps, expected 1");
        }
    }

    // ------------------------------------------------------------------ what flows write

    [Fact]
    public void EveryWrittenKey_IsAControlOfTheMap_OrTheExecutorsOwn()
    {
        int n = 0;
        foreach (var (flow, step, key, _) in Writes())
        {
            n++;
            Assert.True(X.PseudoKeys.Contains(key) || Def.HasControl(key), $"{flow}.{step.Id}: '{key}' would silently do nothing");
        }
        Assert.True(n > 60, $"only {n} writes enumerated");
    }

    [Fact]
    public void EveryPositionalWrite_NamesOneOfItsControlsPositions()
    {
        foreach (var (flow, step, key, target) in Writes().Where(w => !X.PseudoKeys.Contains(w.Key) && w.Target != null))
        {
            var c = Def.ControlFor(key)!;
            if (Bbs757Write.IsMomentary(c)) continue;
            Assert.True(Bbs757Rpn.PositionIndex(c, target!.Value) >= 0,
                $"{flow}.{step.Id}: {key} -> {target} is not one of [{string.Join(", ", c.Positions.Select(p => p.Value))}]");
        }
    }

    [Fact]
    public void NoFlowMovesTheFlapGearOrThrustLevers_OrSetsTheLandingAutobrake()
    {
        foreach (var (flow, step, key, target) in Writes())
        {
            Assert.NotEqual(K.GearLever, key);
            Assert.NotEqual(K.FlapsHandle, key);
            if (key == K.Autobrake)
                Assert.True(target == (int)K.AutobrakeRto || target == (int)K.AutobrakeOff,
                    $"{flow}.{step.Id} sets the autobrake to {target}: only RTO and OFF are the First Officer's (FO-14)");
        }
    }

    [Fact]
    public void EveryConditionField_IsSyntheticOrRegistered()
    {
        var vars = Def.GetVariables();
        var synthetic = Bbs757FoStateEvaluator.SyntheticKeys.ToHashSet();
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.ConditionFieldName != null))
            Assert.True(synthetic.Contains(s.ConditionFieldName!) || vars.ContainsKey(s.ConditionFieldName!)
                        || s.ConditionFieldName == "SIM_ON_GROUND",
                $"{s.Id}: {s.ConditionFieldName}");
    }

    // ------------------------------------------------------------------ engine start

    [Fact]
    public void EngineStart_Shape_RightEngineFirst()
    {
        var ids = Flow("ENGINE_START").Steps.Select(s => s.Id).ToArray();
        Assert.Equal(new[]
        {
            "ES_ON_GROUND", "ES_N2_DATA", "ES_PACKS_OFF", "ES_BLEEDS", "ES_ISOLATION", "ES_APU",
            "ES_E2_GND", "ES_E2_FUEL_POINT", "ES_E2_FUEL_STEP", "ES_E2_LIGHT_OFF", "ES_E2_CUTOUT", "ES_E2_RUNNING",
            "ES_E1_GND", "ES_E1_FUEL_POINT", "ES_E1_FUEL_STEP", "ES_E1_LIGHT_OFF", "ES_E1_CUTOUT", "ES_E1_RUNNING",
        }, ids);
    }

    [Fact]
    public void EngineStart_Preconditions_StopTheFlow()
    {
        var steps = Flow("ENGINE_START").Steps;
        foreach (var (id, field) in new[] { ("ES_ON_GROUND", "SIM_ON_GROUND"), ("ES_N2_DATA", "FO_N2_READ"), ("ES_APU", "FO_APU_RUNNING") })
        {
            var s = steps.Single(x => x.Id == id);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(FlowStepFailurePolicy.Stop, s.FailurePolicy);
            Assert.False(s.Condition!(double.NaN), $"{id} must not pass on an unread value");
        }
    }

    [Theory]
    [InlineData(2, "Right")]
    [InlineData(1, "Left")]
    public void EngineStart_EachEngine_GndFuelPointFuelLightOffCutoutRunning(int n, string side)
    {
        var steps = Flow("ENGINE_START").Steps;
        string startNode = n == 1 ? K.LeftStart : K.RightStart;
        string fuelKey = n == 1 ? X.FuelControl1 : X.FuelControl2;

        var gnd = steps.Single(s => s.Id == $"ES_E{n}_GND");
        Assert.Equal(FlowStepActionType.SetSwitch, gnd.ActionType);
        Assert.Equal(startNode, gnd.EventName);
        Assert.Equal((int)K.StartGnd, gnd.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Stop, gnd.FailurePolicy);
        Assert.Empty(gnd.LinkedChecklistItemIds);                     // FOB-1: the line latches on N2
        Assert.Contains(side, gnd.Label);

        var point = steps.Single(s => s.Id == $"ES_E{n}_FUEL_POINT");
        Assert.Equal(FlowStepActionType.WaitForCondition, point.ActionType);
        Assert.Equal($"FO_ENG{n}_FUEL_POINT", point.ConditionFieldName);
        Assert.Equal(gnd.Id, point.RunOnlyIfStepSent);                // only after THIS run's GND
        Assert.Equal(FlowStepFailurePolicy.Stop, point.FailurePolicy);
        Assert.False(point.Condition!(double.NaN));

        var fuel = steps.Single(s => s.Id == $"ES_E{n}_FUEL_STEP");
        Assert.Equal(fuelKey, fuel.EventName);
        Assert.Equal(X.FuelForStart, fuel.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Stop, fuel.FailurePolicy);
        Assert.Equal(new[] { $"ES_E{n}_FUEL" }, fuel.LinkedChecklistItemIds.ToArray());
        Assert.Equal($"{side} fuel control switch: RUN (RICH on a cold Rolls-Royce)", fuel.Label);

        // The light-off and the cut-out are the executor's watches (they abort a start that times out),
        // only after THIS run's fuel and GND, and a timeout stops the flow.
        var lightOff = steps.Single(s => s.Id == $"ES_E{n}_LIGHT_OFF");
        Assert.Equal(FlowStepActionType.SetSwitch, lightOff.ActionType);
        Assert.Equal(n == 1 ? X.LightOff1 : X.LightOff2, lightOff.EventName);
        Assert.Equal(fuel.Id, lightOff.RunOnlyIfStepSent);
        Assert.Equal(FlowStepFailurePolicy.Stop, lightOff.FailurePolicy);
        Assert.Empty(lightOff.LinkedChecklistItemIds);

        var cutout = steps.Single(s => s.Id == $"ES_E{n}_CUTOUT");
        Assert.Equal(FlowStepActionType.SetSwitch, cutout.ActionType);
        Assert.Equal(n == 1 ? X.Cutout1 : X.Cutout2, cutout.EventName);
        Assert.Equal(gnd.Id, cutout.RunOnlyIfStepSent);
        Assert.Equal(FlowStepFailurePolicy.Stop, cutout.FailurePolicy);
        Assert.Empty(cutout.LinkedChecklistItemIds);

        var running = steps.Single(s => s.Id == $"ES_E{n}_RUNNING");
        Assert.Equal($"FO_ENG{n}_RUNNING", running.ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, running.FailurePolicy);

        Assert.True(steps.IndexOf(gnd) < steps.IndexOf(point));
        Assert.True(steps.IndexOf(point) < steps.IndexOf(fuel));
        Assert.Equal(steps.IndexOf(fuel) + 1, steps.IndexOf(lightOff));
        Assert.Equal(steps.IndexOf(lightOff) + 1, steps.IndexOf(cutout));
        Assert.True(steps.IndexOf(cutout) < steps.IndexOf(running));
    }

    /// <summary>The second engine's starter never engages until the first one's has released (the cut-out wait).</summary>
    [Fact]
    public void EngineStart_TheLeftStarterWaitsForTheRightCutout()
    {
        var steps = Flow("ENGINE_START").Steps;
        Assert.True(steps.FindIndex(s => s.Id == "ES_E2_CUTOUT") < steps.FindIndex(s => s.Id == "ES_E1_GND"));
        Assert.True(steps.FindIndex(s => s.Id == "ES_E2_RUNNING") < steps.FindIndex(s => s.Id == "ES_E1_GND"));
    }

    // ------------------------------------------------------------------ Before Start tail, APU wait (FO-11)

    [Fact]
    public void BeforeStart_EndsWithAcarsThenTheClearance()
    {
        var steps = Flow("BEFORE_START").Steps;
        var acars = steps[^2];
        Assert.Equal("BS_ACARS", acars.Id);
        Assert.Equal(FlowStepActionType.CaptainReminder, acars.ActionType);
        Assert.Contains("BSC_ACARS", acars.LinkedChecklistItemIds);
        Assert.Contains("BS_ACARS", acars.LinkedChecklistItemIds);
        Assert.Equal("BS_CLEARANCE", steps[^1].Id);
    }

    [Theory]
    [InlineData("BEFORE_START", "BS_APU", "BS_APU_RUNNING")]
    [InlineData("AFTER_LANDING", "AL_APU", "AL_APU_RUNNING")]
    public void TheApuStartIsFollowedByAWaitForRunning_ThatCompletesTheLine(string flowId, string startId, string waitId)
    {
        var steps = Flow(flowId).Steps;
        var start = steps.Single(s => s.Id == startId);
        var wait = steps.Single(s => s.Id == waitId);
        Assert.Equal(X.ApuStart, start.EventName);
        Assert.Empty(start.LinkedChecklistItemIds);                 // starting is not running
        Assert.Equal(steps.IndexOf(start) + 1, steps.IndexOf(wait));
        Assert.Equal("FO_APU_RUNNING", wait.ConditionFieldName);
        Assert.Contains(startId, wait.LinkedChecklistItemIds);
    }

    // ------------------------------------------------------------------ gear checks (FO-13)

    [Theory]
    [InlineData("AFTER_TAKEOFF", "AT_CHK_GEAR", "FO_GEAR_UP_OFF", "ATC_GEAR")]
    [InlineData("BEFORE_LANDING", "BL_CHK_GEAR", "FO_GEAR_DOWN", "BLC_GEAR")]
    public void The_gear_check_is_the_last_step_and_read_only(string flowId, string stepId, string field, string line)
    {
        var step = Flow(flowId).Steps.Last();
        Assert.Equal(stepId, step.Id);
        Assert.Equal(FlowStepActionType.WaitForCondition, step.ActionType);
        Assert.Equal(20, step.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.Equal(field, step.ConditionFieldName);
        Assert.Contains(line, step.LinkedChecklistItemIds);
        Assert.False(step.Condition!(double.NaN));
    }

    [Fact]
    public void NoFlowWritesToTheGearOrFlaps_ReadOnlyChecksOnly()
    {
        foreach (var s in Flows.SelectMany(f => f.Steps).Where(s => s.ConditionFieldName is "FO_GEAR_DOWN" or "FO_GEAR_UP_OFF" or "FO_FLAPS_UP"
                     or "FO_FLAPS_TAKEOFF" or "FO_FLAPS_LANDING"))
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
    }

    // ------------------------------------------------------------------ the manual's lines, step by step

    private static Bbs757FoStateEvaluator State(params (string Key, double Value)[] cache)
    {
        var s = new Bbs757FoStateEvaluator();
        s.SetSimConnect(SeededSimConnectCache.With(cache));
        return s;
    }

    private static FlowStep<Bbs757FoStateEvaluator> Step(string flow, string id) => Flow(flow).Steps.Single(s => s.Id == id);

    private static int Index(string flow, string id) => Flow(flow).Steps.FindIndex(s => s.Id == id);

    /// <summary>Live 2026-10-28: a 5 s IRS check failed every normal start from cold. It waits out the alignment, last before the Captain's items.</summary>
    [Fact]
    public void BeforeStart_WaitsForTheIrsAlignment_LastBeforeTheCaptainsItems()
    {
        var irs = Step("BEFORE_START", "BS_CHK_IRS");
        Assert.Equal(600, irs.TimeoutSeconds);
        Assert.Equal("FO_IRS_ALIGNED", irs.ConditionFieldName);
        Assert.Equal(Index("BEFORE_START", "BS_ACARS") - 1, Index("BEFORE_START", "BS_CHK_IRS"));
        Assert.Contains("BSC_IRS", irs.LinkedChecklistItemIds);
    }

    /// <summary>S36 #8-9: no external power on line, the First Officer starts the APU; the Captain hears only when neither works.</summary>
    [Fact]
    public void PowerUp_StartsTheApu_WhenExternalPowerIsNotOnLine()
    {
        var apu = Step("POWER_UP", "PU_APU");
        Assert.Equal(X.ApuStart, apu.EventName);
        Assert.True(Index("POWER_UP", "PU_EXT_PWR") < Index("POWER_UP", "PU_APU"));
        Assert.True(apu.LeaveAloneWhen!(State((K.ExtPwrOn, 1))));          // external power on line: not needed
        Assert.False(apu.LeaveAloneWhen!(State((K.ExtPwrOn, 0))));
        Assert.False(apu.LeaveAloneWhen!(State()));                         // unread: the start itself refuses an unread APU

        var wait = Step("POWER_UP", "PU_APU_RUNNING");
        Assert.Equal(Index("POWER_UP", "PU_APU") + 1, Index("POWER_UP", "PU_APU_RUNNING"));
        Assert.Equal("PU_APU", wait.RunOnlyIfStepSent);
        Assert.Equal("FO_APU_RUNNING", wait.ConditionFieldName);

        var captain = Step("POWER_UP", "PU_EXT_PWR_CPT");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.True(Index("POWER_UP", "PU_APU_RUNNING") < Index("POWER_UP", "PU_EXT_PWR_CPT"));
        Assert.True(captain.SkipCondition!(State((K.ExtPwrOn, 1))));
        Assert.True(captain.SkipCondition!(State((K.ExtPwrOn, 0), (K.ApuState, 2))));
        Assert.False(captain.SkipCondition!(State((K.ExtPwrOn, 0), (K.ApuState, 0))));   // neither: the Captain is asked
    }

    [Fact]
    public void Preflight_TurnsTheRunwayTurnoffLightsOff_ArmsTheAtByItsFlag_AndRemindsTheLandingAltitude()
    {
        var lights = Step("PREFLIGHT", "PF_LIGHTS_OFF");
        Assert.Contains((K.LeftTurnoff, (int?)0), lights.MultiActions);
        Assert.Contains((K.RightTurnoff, (int?)0), lights.MultiActions);

        var at = Step("PREFLIGHT", "PF_AT_ARM");
        Assert.Equal(X.ArmAutothrottle, at.EventName);
        Assert.True(at.SkipCondition!(State((K.AutothrottleArmedState, 1), (K.AutothrottleArm, K.AutothrottleOff))));
        Assert.False(at.SkipCondition!(State((K.AutothrottleArmedState, 0), (K.AutothrottleArm, K.AutothrottleArmed))));   // the switch alone is not armed

        var alt = Step("PREFLIGHT", "PF_LANDING_ALT");
        Assert.Equal(FlowStepActionType.CaptainReminder, alt.ActionType);
        Assert.Contains("landing altitude", alt.ReminderText!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>S36 #10-11: external power off only with the APU on line, then the GPU and a recheck of what a source change deletes.</summary>
    [Fact]
    public void BeforeStart_DeselectsExternalPower_OnlyWithTheApuOnLine_ThenRemindsTheGpu()
    {
        var ext = Step("BEFORE_START", "BS_EXT_PWR_OFF");
        Assert.Equal(X.ExtPower, ext.EventName);
        Assert.Equal(0, ext.TargetValue);
        Assert.Equal(Index("BEFORE_START", "BS_APU_RUNNING") + 1, Index("BEFORE_START", "BS_EXT_PWR_OFF"));
        Assert.True(ext.LeaveAloneWhen!(State((K.ApuState, 1), (K.ApuGen, 1))));     // still starting
        Assert.True(ext.LeaveAloneWhen!(State((K.ApuState, 2), (K.ApuGen, 0))));     // generator control off
        Assert.True(ext.LeaveAloneWhen!(State()));
        Assert.False(ext.LeaveAloneWhen!(State((K.ApuState, 2), (K.ApuGen, 1))));

        var gpu = Step("BEFORE_START", "BS_GPU");
        Assert.Equal(FlowStepActionType.CaptainReminder, gpu.ActionType);
        Assert.Equal("BS_EXT_PWR_OFF", gpu.RunOnlyIfStepSent);
        Assert.Equal("Disconnect the GPU on the EFB; recheck TAKEOFF REF speeds and autobrake.", gpu.ReminderText);
    }

    [Fact]
    public void EngineStart_OpensTheIsolationValve_BesideTheBleeds()
    {
        var iso = Step("ENGINE_START", "ES_ISOLATION");
        Assert.Equal(Index("ENGINE_START", "ES_BLEEDS") + 1, Index("ENGINE_START", "ES_ISOLATION"));
        Assert.Equal(K.Isolation, iso.EventName);
        Assert.Equal((int)Bbs757FoStateEvaluator.IsolationOpen, iso.TargetValue);
        Assert.True(iso.SkipCondition!(State((K.Isolation, Bbs757FoStateEvaluator.IsolationOpen))));
        Assert.Contains("ES_ISOLATION", iso.LinkedChecklistItemIds);
    }

    [Fact]
    public void BeforeTakeoff_TurnsTheWingLightsOn_AndReadsTheEnginesForTheChecklist()
    {
        var wing = Step("BEFORE_TAKEOFF", "BT_WING");
        Assert.Equal((K.WingLights, (int?)1), (wing.EventName!, wing.TargetValue));
        Assert.Contains("BTC_ENGINES", Step("BEFORE_TAKEOFF", "BT_CHK_ENGINES").LinkedChecklistItemIds);
    }

    [Fact]
    public void Descent_DoesNotLatchTheApproachChecklist()
        => Assert.DoesNotContain("APPROACH_CL", Flow("DESCENT").RelatedChecklistGroupIds);

    [Fact]
    public void BeforeLanding_ReadsTheLandingFlap_BeforeTheGear()
    {
        var flaps = Step("BEFORE_LANDING", "BL_CHK_FLAPS");
        Assert.Equal("FO_FLAPS_LANDING", flaps.ConditionFieldName);
        Assert.Equal(FlowStepActionType.WaitForCondition, flaps.ActionType);
        Assert.Contains("BLC_FLAPS", flaps.LinkedChecklistItemIds);
        Assert.Equal(Index("BEFORE_LANDING", "BL_CHK_GEAR") - 1, Index("BEFORE_LANDING", "BL_CHK_FLAPS"));
    }

    [Fact]
    public void AfterLanding_WingAntiIceAndWingLightsOff_AndTheApuStartsLast()
    {
        Assert.Equal((K.WingAntiIce, (int?)0), (Step("AFTER_LANDING", "AL_WING_ANTI_ICE").EventName!, Step("AFTER_LANDING", "AL_WING_ANTI_ICE").TargetValue));
        Assert.Equal((K.WingLights, (int?)0), (Step("AFTER_LANDING", "AL_WING_LIGHTS").EventName!, Step("AFTER_LANDING", "AL_WING_LIGHTS").TargetValue));
        Assert.Equal("AL_APU_RUNNING", Flow("AFTER_LANDING").Steps[^1].Id);
        Assert.Equal("AL_APU", Groups.Single(g => g.Id == "AFTER_LANDING").Items[^1].Id);
    }

    /// <summary>E194-198: the stabilizer reminder comes before the hydraulics go; the parked aircraft shows position lights only, transponder standby.</summary>
    [Fact]
    public void Shutdown_TheStabReminderBeforeTheHydraulics_PositionLightsOnly_TransponderStandby()
    {
        var stab = Step("SHUTDOWN", "SD_STAB_TRIM");
        Assert.Equal(FlowStepActionType.CaptainReminder, stab.ActionType);
        Assert.Equal("Stabilizer trim set before the hydraulics go off.", stab.ReminderText);
        Assert.Equal(Index("SHUTDOWN", "SD_ELEC_HYD") - 1, Index("SHUTDOWN", "SD_STAB_TRIM"));

        var lights = Step("SHUTDOWN", "SD_EXT_LIGHTS");
        Assert.Equal(new[] { (K.WingLights, (int?)0), (K.LogoLights, (int?)0), (K.TaxiLight, (int?)0) }, lights.MultiActions);
        Assert.DoesNotContain(lights.MultiActions, a => a.EventName == K.PositionLights);

        var xpdr = Step("SHUTDOWN", "SD_XPDR");
        Assert.Equal((K.TcasMode, (int?)(int)K.TcasStandby), (xpdr.EventName!, xpdr.TargetValue));

        Assert.Equal((K.WingAntiIce, (int?)0), (Step("SHUTDOWN", "SD_WING_ANTI_ICE").EventName!, Step("SHUTDOWN", "SD_WING_ANTI_ICE").TargetValue));
        Assert.Equal((K.Wipers, (int?)0), (Step("SHUTDOWN", "SD_WIPERS").EventName!, Step("SHUTDOWN", "SD_WIPERS").TargetValue));
    }

    /// <summary>The taxi light was turned off nowhere: some flow must.</summary>
    [Fact]
    public void SomeFlowTurnsTheTaxiLightOff()
        => Assert.Contains(Writes(), w => w.Key == K.TaxiLight && w.Target == 0);

    [Fact]
    public void Shutdown_ApuBleedOn_OnlyWithTheApuRunning()
    {
        var bleed = Step("SHUTDOWN", "SD_APU_BLEED");
        Assert.Equal((K.ApuBleed, (int?)1), (bleed.EventName!, bleed.TargetValue));
        Assert.True(bleed.LeaveAloneWhen!(State((K.ApuState, 0))));
        Assert.True(bleed.LeaveAloneWhen!(State()));
        Assert.False(bleed.LeaveAloneWhen!(State((K.ApuState, 2))));
    }

    /// <summary>E199: the battery only with the APU stopped and external power off; external power that will not go off is the GPU to remove.</summary>
    [Fact]
    public void Secure_RemindsTheGpu_AndGatesTheBatteryOnTheApuAndExternalPower()
    {
        var gpu = Step("SECURE", "SC_GPU");
        Assert.Equal(FlowStepActionType.CaptainReminder, gpu.ActionType);
        Assert.Contains("Remove the GPU on the EFB", gpu.ReminderText);
        Assert.Equal(Index("SECURE", "SC_EXT_PWR") + 1, Index("SECURE", "SC_GPU"));
        Assert.Equal(Index("SECURE", "SC_BATTERY") - 1, Index("SECURE", "SC_GPU"));
        Assert.True(gpu.SkipCondition!(State((K.ExtPwrOn, 0))));
        Assert.False(gpu.SkipCondition!(State((K.ExtPwrOn, 1))));

        var battery = Step("SECURE", "SC_BATTERY");
        var parked = State(("SIM_ON_GROUND", 1), (K.ApuState, 0), (K.ExtPwrOn, 0));
        parked.SetEngineN2(0, 0);
        Assert.False(battery.LeaveAloneWhen!(parked));
        var extOn = State(("SIM_ON_GROUND", 1), (K.ApuState, 0), (K.ExtPwrOn, 1));
        extOn.SetEngineN2(0, 0);
        Assert.True(battery.LeaveAloneWhen!(extOn));
        var apuOn = State(("SIM_ON_GROUND", 1), (K.ApuState, 2), (K.ExtPwrOn, 0));
        apuOn.SetEngineN2(0, 0);
        Assert.True(battery.LeaveAloneWhen!(apuOn));
    }

    // ------------------------------------------------------------------ center pumps (FO-20)

    [Fact]
    public void CenterPumpTarget_IsNull_WhileTheCenterTankIsUnread()
    {
        var unread = new Bbs757FoStateEvaluator();
        Assert.Null(Bbs757FoFlowDefinitions.CenterPumpTarget(unread));

        var full = new Bbs757FoStateEvaluator();
        full.SetSimConnect(SeededSimConnectCache.With((K.CenterFuelGallons, 2000)));
        Assert.Equal(1, Bbs757FoFlowDefinitions.CenterPumpTarget(full));

        var empty = new Bbs757FoStateEvaluator();
        empty.SetSimConnect(SeededSimConnectCache.With((K.CenterFuelGallons, 0)));
        Assert.Equal(0, Bbs757FoFlowDefinitions.CenterPumpTarget(empty));
    }
}
