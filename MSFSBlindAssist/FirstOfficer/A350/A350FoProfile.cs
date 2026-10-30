using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.A350;

/// <summary>
/// The iniBuilds A350's First Officer: wires the shared First Officer engine and window to the A350's concretes. The executor
/// and the evaluator need the live definition (its panel write path; which key streams an input), so it comes in through the
/// constructor (<c>FirstOfficerWindows</c>), as the A380's does.
/// </summary>
public sealed class A350FoProfile : IFoProfile<A350ActionExecutor, A350StateEvaluator>
{
    private readonly IniBuildsA350Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;

    public A350FoProfile(IniBuildsA350Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — iniBuilds A350";

    public A350ActionExecutor CreateExecutor() => new();
    public A350StateEvaluator CreateEvaluator() => new(_def);

    public void BindDataManager(A350StateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(A350ActionExecutor exec, SimConnectManager? sc)
    {
        exec.SetDefinition(_def);
        exec.SetAnnouncer(_announcer);
        exec.SetSimConnect(sc);
    }

    public List<FlowDefinition<A350StateEvaluator>> BuildFlows() => A350FlowDefinitions.Build();

    public List<ChecklistGroup<A350ActionExecutor, A350StateEvaluator>> BuildChecklists() => A350ChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(A350ActionExecutor exec, A350StateEvaluator state, ScreenReaderAnnouncer a, UserSettings s)
        => new A350FOAutoManager(exec, state, a)
        {
            AutoFlapsEnabled = s.FOAutoFlapsEnabled,   // speed-scheduled extension and retraction (the takeoff setting stays a Captain item)
        };

    public IFoPhaseMonitor CreatePhaseMonitor(A350ActionExecutor exec, A350StateEvaluator state, ScreenReaderAnnouncer a)
        => new A350FlightPhaseMonitor(exec, a);
}
