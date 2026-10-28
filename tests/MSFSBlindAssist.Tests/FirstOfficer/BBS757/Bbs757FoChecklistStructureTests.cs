using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// Plan 10, task 2: BlueBird's Normal Checklist (the *_CL read-back groups) beside the action
/// groups, as the MD-11's (Md11FoChecklistStructureTests).
/// </summary>
public class Bbs757FoChecklistStructureTests
{
    private static readonly List<ChecklistGroup<X, Bbs757FoStateEvaluator>> Groups = Bbs757FoChecklistDefinitions.Build();
    private static readonly BBS757Definition Def = new();
    private static readonly Dictionary<string, SimVarDefinition> Vars = Def.GetVariables();

    private static ChecklistGroup<X, Bbs757FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);

    [Fact]
    public void GroupIds_InFlightOrder()
        => Assert.Equal(new[]
        {
            "POWER_UP", "PREFLIGHT", "BEFORE_START", "BEFORE_START_CL", "ENGINE_START", "AFTER_START", "AFTER_START_CL",
            "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL", "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "DESCENT", "DESCENT_CL", "APPROACH_CL",
            "BEFORE_LANDING", "BEFORE_LANDING_CL", "AFTER_LANDING", "AFTER_LANDING_CL", "SHUTDOWN", "SHUTDOWN_CL", "SECURE",
        }, Groups.Select(g => g.Id));

    [Fact]
    public void NoReadbackGroupHasACheckAction()
    {
        var readBack = Groups.Where(g => g.Id.EndsWith("_CL", StringComparison.Ordinal)).ToList();
        Assert.Equal(9, readBack.Count);
        foreach (var g in readBack)
            foreach (var i in g.Items)
                Assert.True(i.CheckAction == null, $"{g.Id}/{i.Id}: *_CL items are action-free (FO-3)");
    }

    [Fact]
    public void EveryItemsGroupIdIsItsOwnGroup()
    {
        foreach (var g in Groups)
            Assert.All(g.Items, i => Assert.Equal(g.Id, i.GroupId));
    }

    [Fact]
    public void ItemIds_AreUnique()
    {
        var ids = Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void LabelsAreUniqueWithinEachGroup()
    {
        foreach (var g in Groups)
        {
            var labels = g.Items.Select(i => i.Label).ToList();
            Assert.True(labels.Count == labels.Distinct().Count(), $"{g.Id} repeats a label");
        }
    }

    [Fact]
    public void BeforeStartChecklist_EndsWithStartAcars()
    {
        var last = Group("BEFORE_START_CL").Items.Last();
        Assert.Equal("BSC_ACARS", last.Id);
        Assert.Equal("Start ACARS", last.Label);
        Assert.Equal(ChecklistItemType.CaptainReminder, last.Type);
    }

    [Fact]
    public void AfterTakeoffChecklist_Exists_WithGearAndFlaps()
    {
        var g = Group("AFTER_TAKEOFF_CL");
        Assert.Equal(new[] { "ATC_GEAR", "ATC_FLAPS" }, g.Items.Select(i => i.Id));
        Assert.Equal("FO_GEAR_UP_OFF", g.Items[0].StateFieldName);
        Assert.Equal("FO_FLAPS_UP", g.Items[1].StateFieldName);
    }

    /// <summary>
    /// Every auto-detected line reads a synthetic field or a variable the definition registers (not
    /// write-only), and an OnRequest one is polled by the window, or it never leaves NaN.
    /// </summary>
    [Fact]
    public void EveryItemField_IsSyntheticOrRegistered_AndPolledWhenOnRequest()
    {
        var synthetic = Bbs757FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new Bbs757FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.StateFieldName != null))
            foreach (var field in i.AdditionalStateFields.Prepend(i.StateFieldName!))
            {
                if (synthetic.Contains(field)) continue;
                AssertReadable($"{i.Id}", field, polled);
            }
    }

    /// <summary>The variables the synthetic fields compute from: registered, and polled when OnRequest.</summary>
    [Fact]
    public void EverySyntheticInput_IsRegistered_AndPolledWhenOnRequest()
    {
        var polled = new Bbs757FoStateEvaluator().OnRequestPollFields.ToHashSet();
        Assert.NotEmpty(Bbs757FoStateEvaluator.SyntheticInputs);
        foreach (var field in Bbs757FoStateEvaluator.SyntheticInputs)
            AssertReadable("synthetic input", field, polled);
    }

    [Fact]
    public void EveryPolledField_IsRegistered()
    {
        foreach (var field in new Bbs757FoStateEvaluator().OnRequestPollFields)
            Assert.True(Vars.ContainsKey(field), $"polled field {field} is not registered");
    }

    [Fact]
    public void TheEngineVariant_IsTheFirstOfficersOwnOnRequestLVar()
    {
        Assert.True(Vars.TryGetValue(K.EngineVariant, out var def));
        Assert.Equal("B757_ENG_VARIANT", def!.Name);
        Assert.Equal(SimVarType.LVar, def.Type);
        Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        Assert.Contains(K.EngineVariant, new Bbs757FoStateEvaluator().OnRequestPollFields);
    }

    private static void AssertReadable(string who, string field, HashSet<string> polled)
    {
        Assert.True(Vars.TryGetValue(field, out var def), $"{who}: field {field} not registered by BBS757Definition");
        Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
        if (def.UpdateFrequency == UpdateFrequency.OnRequest)
            Assert.True(polled.Contains(field), $"{who}: OnRequest field {field} is not polled");
    }

    [Fact]
    public void AutoItemsAreLiveMirrors_ExceptTheEngineStartSelectors()
    {
        var latched = new[] { "ES_E1_GND", "ES_E2_GND" };
        var auto = Groups.SelectMany(g => g.Items).Where(i => i.Type == ChecklistItemType.AutoDetectable).ToArray();
        Assert.All(auto.Where(i => !latched.Contains(i.Id)), i => Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior));
        Assert.All(auto.Where(i => latched.Contains(i.Id)), i => Assert.Equal(RevertBehavior.StayComplete, i.RevertBehavior));
        Assert.Equal(latched.Length, auto.Count(i => latched.Contains(i.Id)));
    }

    /// <summary>A Captain reminder never auto-detects and never acts.</summary>
    [Fact]
    public void RemindersAreSilentAndManual()
    {
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.Type == ChecklistItemType.CaptainReminder))
        {
            Assert.Null(i.CheckAction);
            Assert.Null(i.StateFieldName);
            Assert.False(string.IsNullOrWhiteSpace(i.ReminderText));
        }
    }

    // ------------------------------------------------------------------ NaN never ticks, never reverts (review focus)

    [Fact]
    public void AnUnreadLine_NeverTicks_AndATickedLineNeverRevertsWhenItGoesUnread()
    {
        var sc = SeededSimConnectCache.ConnectedWith();
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new MSFSBlindAssist.FirstOfficer.ChecklistManager<X, Bbs757FoStateEvaluator>(state, new X(), groups);

        mgr.EvaluateAutoDetection();
        Assert.DoesNotContain(groups.SelectMany(g => g.Items), i => i.IsChecked);

        var line = groups.Single(g => g.Id == "BEFORE_START_CL").Items.Single(i => i.Id == "BSC_SEAT_BELTS");
        var cache = SeededSimConnectCache.Cache(sc);
        cache[K.SeatBelts] = K.SignOn;
        mgr.EvaluateAutoDetection();
        Assert.True(line.IsChecked);

        cache.TryRemove(K.SeatBelts, out _);          // unread again: NaN neither ticks nor reverts
        mgr.EvaluateAutoDetection();
        Assert.True(line.IsChecked);

        cache[K.SeatBelts] = K.SignOff;
        mgr.EvaluateAutoDetection();
        Assert.False(line.IsChecked);
    }

    [Fact]
    public void TheGearLine_NeedsTheLeverAndAllThreeLegs()
    {
        var sc = SeededSimConnectCache.ConnectedWith((K.GearLever, 1), (K.GearLegs[0], 1), (K.GearLegs[1], 1));
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new MSFSBlindAssist.FirstOfficer.ChecklistManager<X, Bbs757FoStateEvaluator>(state, new X(), groups);
        var line = groups.Single(g => g.Id == "BEFORE_LANDING_CL").Items.Single(i => i.Id == "BLC_GEAR");

        mgr.EvaluateAutoDetection();
        Assert.False(line.IsChecked);                   // the right leg unread

        SeededSimConnectCache.Cache(sc)[K.GearLegs[2]] = 1;
        mgr.EvaluateAutoDetection();
        Assert.True(line.IsChecked);
    }

    [Fact]
    public void TheFuelControlLine_ReadsTheVariantsOwnSwitch()
    {
        // An RR aircraft by its title: the PW pair reading 0 (which is CUTOFF on a PW) must not tick it.
        var sc = Bbs757TestTitles.ConnectedWith(Bbs757TestTitles.Rr, (K.EngineVariant, 0), (K.LeftFuelPw, 0), (K.RightFuelPw, 0),
            (K.LeftFuelRr, 0), (K.RightFuelRr, 0));
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new MSFSBlindAssist.FirstOfficer.ChecklistManager<X, Bbs757FoStateEvaluator>(state, new X(), groups);
        var line = groups.Single(g => g.Id == "SHUTDOWN_CL").Items.Single(i => i.Id == "SDC_FUEL");

        mgr.EvaluateAutoDetection();
        Assert.False(line.IsChecked);                   // RR at 0 is RUN

        SeededSimConnectCache.Cache(sc)[K.LeftFuelRr] = K.RrCutoff;
        SeededSimConnectCache.Cache(sc)[K.RightFuelRr] = K.RrCutoff;
        mgr.EvaluateAutoDetection();
        Assert.True(line.IsChecked);
    }

    // ------------------------------------------------------------------ never automated (FO-14, FO-2)

    [Fact]
    public void TheLandingAutobrakeIsOnlyAReminder_WithNoSetting()
    {
        var ds = Group("DESCENT").Items.Single(i => i.Id == "DS_AUTOBRAKE");
        Assert.Equal(ChecklistItemType.CaptainReminder, ds.Type);
        Assert.DoesNotMatch(@"\b(1|2|3|4|MAX)\b", ds.Label);
        // No action line sets the autobrake to a landing position (only RTO before takeoff, OFF after landing).
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.StateFieldName == K.Autobrake && i.CheckAction != null))
            Assert.Contains(i.Id, new[] { "PF_AUTOBRAKE", "AS_AUTOBRAKE", "AL_AUTOBRAKE" });
    }

    [Fact]
    public void NoChecklistLineReadsTheFlapOrGearLeverAsSomethingToSet()
    {
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.CheckAction != null))
            Assert.DoesNotContain(i.StateFieldName, new[] { "FO_GEAR_DOWN", "FO_GEAR_UP_OFF", "FO_FLAPS_UP", "FO_FLAPS_TAKEOFF", K.FlapsHandle, K.GearLever });
    }

    // ------------------------------------------------------------------ what the action lines write

    /// <summary>
    /// Every action line's writes name a control the 757 map knows (or a First Officer key), and a
    /// positional write names one of that control's positions: a write to a node the map lacks is a
    /// silent no-op, and a target that is no position is refused by <see cref="Bbs757Write.ToPosition"/>.
    /// Each action is run on both variants against a fake aircraft that records every set and press.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EveryActionWrite_NamesAKnownControlAndOneOfItsPositions(bool pw)
    {
        var io = new FakeBbs757FoTransport { KnownNodes = new HashSet<string>(StringComparer.Ordinal) };
        foreach (var node in AllMapNodes()) io.KnownNodes.Add(node);
        foreach (var node in io.KnownNodes.Where(n => Def.ControlFor(n) is { } c && Bbs757Write.IsMomentary(c)).ToList())
            io.Buttons.Add(node);
        // Everything reads "not there yet", so each action writes rather than finding its line already set.
        io.Values[K.ExtPwrOn] = 0;
        io.Values[K.ExtPwrAvail] = 1;
        io.Values[K.ApuState] = 0;
        io.Values[K.Speedbrake] = 0;
        io.Values[K.AutothrottleArmedState] = 0;
        io.Values[K.AutothrottleArm] = K.AutothrottleArmed;      // the A/T arm is then cycled OFF and ARM
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(Bbs757TestTitles.With(pw ? Bbs757TestTitles.Pw : Bbs757TestTitles.Rr, (K.LeftEgt, 20), (K.RightEgt, 20)));
        var exec = new X();
        exec.SetTransport(io);
        exec.SetState(state);

        foreach (var item in Groups.SelectMany(g => g.Items).Where(i => i.CheckAction != null))
            await item.CheckAction!(exec, state);

        Assert.NotEmpty(io.Sets);
        foreach (var (node, target) in io.Sets)
        {
            var control = Def.ControlFor(node);
            Assert.True(control != null, $"set of {node}: not a control of the 757 map");
            Assert.False(Bbs757Write.IsMomentary(control!), $"{node} is a button: it is pressed, never set");
            Assert.True(Bbs757Rpn.PositionIndex(control!, target) >= 0,
                $"{node} -> {target}: not one of its positions [{string.Join(", ", control!.Positions.Select(p => p.Value))}]");
        }
        foreach (var node in io.Presses)
            Assert.True(io.Buttons.Contains(node), $"press of {node}: not a button of the 757 map");
    }

    private static IEnumerable<string> AllMapNodes()
    {
        var keys = typeof(K).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)
            .Concat(typeof(K).GetFields().Where(f => f.IsStatic && !f.IsLiteral && f.FieldType == typeof(string[]))
                .SelectMany(f => (string[])f.GetValue(null)!))
            .Concat(new[] { X.SpeedbrakeArmedNode, X.SpeedbrakeDownNode });
        return keys.Where(Def.HasControl);
    }

    [Fact]
    public void TheButtonsTheExecutorPresses_AreMomentaryControlsOfTheMap()
    {
        foreach (var node in new[] { K.ExtPwrButton, X.SpeedbrakeArmedNode, X.SpeedbrakeDownNode })
        {
            var c = Def.ControlFor(node);
            Assert.True(c != null, $"{node} is not in the 757 map");
            Assert.True(Bbs757Write.IsMomentary(c!), $"{node} must be a button: FoPressAsync refuses anything else");
            Assert.NotNull(Bbs757Write.PressFor(c!));
        }
    }

    // ------------------------------------------------------------------ guards (FO-25)

    [Theory]
    [InlineData("PU_FUEL_CUTOFF")]
    [InlineData("PU_ELEC_HYD_OFF")]
    [InlineData("PF_FUEL_PUMPS")]
    [InlineData("SD_FUEL_CUTOFF")]
    [InlineData("SD_ELEC_HYD")]
    [InlineData("SD_FUEL_PUMPS")]
    [InlineData("SC_IRS")]
    [InlineData("SC_BATTERY")]
    [InlineData("ES_PACKS_OFF")]
    public void ACutLine_IsLeftAlone_WithNothingRead(string id)
    {
        var item = Groups.SelectMany(g => g.Items).Single(i => i.Id == id);
        Assert.NotNull(item.LeaveAloneWhen);
        Assert.False(string.IsNullOrWhiteSpace(item.LeaveAloneText));
        Assert.True(item.LeaveAloneWhen!(new Bbs757FoStateEvaluator()), $"{id} must be left alone while nothing is read");
    }

    // ------------------------------------------------------------------ the battery (E199) and the fuel point (p. 208)

    private static Bbs757FoStateEvaluator Parked(params (string Key, double Value)[] more)
    {
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.With(more.Prepend(("SIM_ON_GROUND", 1.0)).ToArray()));
        state.SetEngineN2(0, 0);
        return state;
    }

    [Fact]
    public void TheBattery_GoesOff_OnlyWithTheApuStopped_AndExternalPowerOff()
    {
        var battery = Groups.Single(g => g.Id == "SECURE").Items.Single(i => i.Id == "SC_BATTERY");
        Assert.False(battery.LeaveAloneWhen!(Parked((K.ApuState, 0), (K.ExtPwrOn, 0))));
        Assert.True(battery.LeaveAloneWhen!(Parked((K.ApuState, 2), (K.ExtPwrOn, 0))));     // APU running
        Assert.True(battery.LeaveAloneWhen!(Parked((K.ApuState, 3), (K.ExtPwrOn, 0))));     // still shutting down
        Assert.True(battery.LeaveAloneWhen!(Parked((K.ApuState, 0), (K.ExtPwrOn, 1))));     // external power on
        Assert.True(battery.LeaveAloneWhen!(Parked((K.ExtPwrOn, 0))));                      // APU unread
        Assert.True(battery.LeaveAloneWhen!(Parked((K.ApuState, 0))));                      // external power unread
    }

    /// <summary>A hand RUN never puts fuel into an engine short of its fuel point (manual p. 208).</summary>
    [Theory]
    [InlineData(1, "ES_E1_FUEL")]
    [InlineData(2, "ES_E2_FUEL")]
    public void AFuelControlTick_IsRefused_UnlessThatEngineIsAtItsFuelPoint(int n, string id)
    {
        var line = Groups.Single(g => g.Id == "ENGINE_START").Items.Single(i => i.Id == id);
        Assert.EndsWith("RUN (at the fuel point)", line.Label);
        string fuel = n == 1 ? K.LeftFuelPw : K.RightFuelPw;

        Bbs757FoStateEvaluator Pw(double n2, double fuelSwitch)
        {
            var s = Parked((K.EngineVariant, 1), (fuel, fuelSwitch));
            if (n == 1) s.SetEngineN2(n2, 0); else s.SetEngineN2(0, n2);
            return s;
        }

        Assert.True(line.LeaveAloneWhen!(new Bbs757FoStateEvaluator()));                   // nothing read
        Assert.True(line.LeaveAloneWhen!(Pw(10, K.PwCutoff)));                            // still motoring up
        Assert.True(line.LeaveAloneWhen!(Pw(0, K.PwCutoff)));                             // stationary
        Assert.False(line.LeaveAloneWhen!(Pw(Bbs757FoStateEvaluator.PwFuelPointN2, K.PwCutoff)));
        Assert.False(line.LeaveAloneWhen!(Pw(60, K.PwRun)));                              // already on: nothing to refuse
        Assert.Contains("not at the fuel point", line.LeaveAloneText);
    }

    [Fact]
    public void AFuelControlTick_ShortOfTheFuelPoint_IsRefusedAloud_AndWritesNothing()
    {
        var sc = SeededSimConnectCache.ConnectedWith(("SIM_ON_GROUND", 1), (K.EngineVariant, 1), (K.RightFuelPw, K.PwCutoff));
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(sc);
        state.SetEngineN2(0, 12);
        var io = new FakeBbs757FoTransport();
        var exec = new X();
        exec.SetTransport(io);
        exec.SetState(state);
        var groups = Bbs757FoChecklistDefinitions.Build();
        var mgr = new MSFSBlindAssist.FirstOfficer.ChecklistManager<X, Bbs757FoStateEvaluator>(state, exec, groups);

        Assert.False(mgr.ToggleItem("ENGINE_START", "ES_E2_FUEL", out string? why));
        Assert.Contains("not at the fuel point", why);
        Assert.Empty(io.Events);
    }

    [Theory]
    [InlineData("BTC_ENGINES", "BEFORE_TAKEOFF_CL", "FO_ENGINES_RUNNING")]
    [InlineData("BLC_FLAPS", "BEFORE_LANDING_CL", "FO_FLAPS_LANDING")]
    [InlineData("PF_AT_ARM", "PREFLIGHT", "FO_AT_ARMED")]
    public void TheseLines_ReadTheAircraft_NotACaptainReminder(string id, string group, string field)
    {
        var line = Group(group).Items.Single(i => i.Id == id);
        Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
        Assert.Equal(field, line.StateFieldName);
    }

    [Fact]
    public void ACutLine_IsActedOn_ParkedWithTheEnginesStopped()
    {
        var state = new Bbs757FoStateEvaluator();
        state.SetSimConnect(SeededSimConnectCache.With(("SIM_ON_GROUND", 1)));
        state.SetEngineN2(0, 0);
        Assert.False(Bbs757FoChecklistDefinitions.NotParked(state));
        state.SetEngineN2(0, 30);
        Assert.True(Bbs757FoChecklistDefinitions.NotParked(state));
    }
}
