using System.Globalization;
using System.Text;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.A350;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

/// <summary>
/// The A350's First Officer on the real engine, without its window (the repl's <c>fo</c>): the real flows, checklists, executor and
/// evaluator on the sim, the one-second work the window's timer does (N2, auto-detection), and the automation on request. What the
/// First Officer says lands in the capture announcer (<c>say</c>); what its steps did, with times, in <c>fo log</c>.
/// </summary>
internal sealed class FoDriver : IDisposable
{
    private readonly SimConnectManager _sim;
    private readonly Func<string, Task<string>> _eval;
    private readonly A350ActionExecutor _executor = new();
    private readonly A350StateEvaluator _state;
    private readonly List<ChecklistGroup<A350ActionExecutor, A350StateEvaluator>> _groups = A350ChecklistDefinitions.Build();
    private readonly List<FlowDefinition<A350StateEvaluator>> _flows = A350FlowDefinitions.Build();
    private readonly ChecklistManager<A350ActionExecutor, A350StateEvaluator> _checklist;
    private readonly FlowManager<A350ActionExecutor, A350StateEvaluator> _manager;
    private readonly A350FlightPhaseMonitor _monitor;
    private readonly A350FOAutoManager _flaps;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly List<string> _log = new();
    private bool _automation, _automationBusy;

    public FoDriver(IniBuildsA350Definition def, SimConnectManager sim, Cap cap, Func<string, Task<string>> eval)
    {
        _sim = sim;
        _eval = eval;
        _state = new A350StateEvaluator(def);
        _state.SetSimConnect(sim);
        _executor.SetDefinition(def);
        _executor.SetAnnouncer(cap);
        _executor.SetSimConnect(sim);
        _checklist = new ChecklistManager<A350ActionExecutor, A350StateEvaluator>(_state, _executor, _groups);
        _manager = new FlowManager<A350ActionExecutor, A350StateEvaluator>(_state, _executor, _checklist, cap);
        _monitor = new A350FlightPhaseMonitor(_executor, cap);
        _flaps = new A350FOAutoManager(_executor, _state, cap) { AutoFlapsEnabled = true };
        void Note(string line) { lock (_log) _log.Add($"{DateTime.Now:HH:mm:ss} {line}"); }
        _manager.StepStarted += (_, step, _) => Note($"> {step.Id}: {step.Label}");
        _manager.StepCompleted += (_, step, _) => Note($"  done {step.Id}");
        _manager.StepSkipped += (_, step, _) => Note($"  skipped {step.Id}");
        _manager.StepFailed += (_, step, _, why) => Note($"  FAILED {step.Id}: {why}");
        _manager.CaptainReminderRequired += text => Note($"  captain: {text}");
        _manager.FlowCompleted += flow =>
        {
            Note($"flow {flow.Id} completed; unfinished: {string.Join(", ", _manager.UnfinishedChecklistItemIds)}");
            foreach (var groupId in flow.CompletionGroupIds(id => _groups.Any(g => g.Id == id)))
                _checklist.MarkGroupComplete(groupId, _manager.UnfinishedChecklistItemIds);
        };
        _manager.FlowFailed += (flow, why) => Note($"flow {flow.Id} FAILED: {why}");
        _manager.FlowCancelled += flow => Note($"flow {flow.Id} cancelled");
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private async Task TickAsync()
    {
        if (_sim.GetCachedVariableValue("A350_AVAR_TURB_ENG_N2_1") is double n1 && _sim.GetCachedVariableValue("A350_AVAR_TURB_ENG_N2_2") is double n2)
            _state.SetEngineN2(n1, n2);
        _checklist.EvaluateAutoDetection();
        if (!_automation || _automationBusy) return;
        _automationBusy = true;
        try
        {
            string[] v = (await _eval("[SimVar.GetSimVarValue('A:PLANE ALTITUDE','feet'),SimVar.GetSimVarValue('A:VERTICAL SPEED','feet per minute'),"
                + "SimVar.GetSimVarValue('A:PLANE ALT ABOVE GROUND','feet'),SimVar.GetSimVarValue('A:AIRSPEED INDICATED','knots'),SimVar.GetSimVarValue('A:SIM ON GROUND','bool')].join(' ')"))
                .Split(' ');
            if (v.Length < 5) return;
            double N(int i) => double.Parse(v[i], CultureInfo.InvariantCulture);
            _monitor.Update(N(0), N(1), N(4) > 0.5);
            _flaps.Update(N(0), N(1), N(2), N(3), N(4) > 0.5);
        }
        catch (FormatException) { /* a read the sim answered with nothing: the next second's */ }
        finally { _automationBusy = false; }
    }

    /// <summary>One <c>fo</c> command's answer.</summary>
    public string Command(string rest)
    {
        var p = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (p.ElementAtOrDefault(0) ?? "")
        {
            case "list":
                return string.Join(", ", _flows.Select(f => f.Id));
            case "run":
            {
                var flow = _flows.FirstOrDefault(f => f.Id == p.ElementAtOrDefault(1));
                if (flow == null) return "no such flow";
                if (_manager.IsRunning) return "a flow is running";
                lock (_log) _log.Add($"{DateTime.Now:HH:mm:ss} flow {flow.Id} started");
                _manager.StartFlow(flow);
                return "started " + flow.Id;
            }
            case "stop":
                _manager.Cancel();
                return "stopped";
            case "running":
                return _manager.IsRunning ? "running" : "idle";
            case "log":
                lock (_log)
                {
                    string text = string.Join("\n", _log);
                    _log.Clear();
                    return text;
                }
            case "items":
            {
                var sb = new StringBuilder();
                foreach (var group in _groups.Where(g => p.ElementAtOrDefault(1) is not { } id || g.Id == id))
                {
                    sb.Append("== ").AppendLine(group.Id);
                    foreach (var item in group.Items)
                        sb.Append(item.IsChecked ? "[x] " : "[ ] ").Append(item.Id).Append(": ").Append(item.Label)
                            .Append(item.LiveValue?.Invoke(_state) is { } live ? " (" + live + ")" : "").AppendLine();
                }
                return sb.ToString();
            }
            case "tick":
            {
                var item = _groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Id == p.ElementAtOrDefault(1));
                if (item == null) return "no such line";
                _checklist.ToggleItem(item.GroupId, item.Id, out string? leftAlone);
                return leftAlone ?? (item.IsChecked ? "ticked " : "unticked ") + item.Id;
            }
            case "field":
                return string.Join("\n", (p.ElementAtOrDefault(1) ?? "").Split(',').Select(f => $"{f}\t{_state.GetValue(f.Trim()).ToString(CultureInfo.InvariantCulture)}"));
            case "automation":
                _automation = p.ElementAtOrDefault(1) != "off";
                return _automation ? "automation on" : "automation off";
            case "seatbelts":
                _monitor.AutoSeatbeltMode = Enum.Parse<FoSeatbeltMode>(p.ElementAtOrDefault(1) ?? "Disabled");
                return "seat belts " + _monitor.AutoSeatbeltMode;
            case "transition":
            {
                var t = (p.ElementAtOrDefault(1) ?? "").Split(' ');
                _monitor.SetThresholds(int.Parse(t[0], CultureInfo.InvariantCulture), int.Parse(t[1], CultureInfo.InvariantCulture));
                return "transition set";
            }
            default:
                return "fo list | run FLOW | stop | running | log | items [GROUP] | tick LINE | field F,F | automation [off] | seatbelts MODE | transition ALT LEVEL";
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _executor.ReleaseHeldPresses();
    }
}
