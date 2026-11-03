using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// The OIS options whose controls the cockpit hides when they are not fitted (plan 23, re-checked on 1.2.8: the behaviour
/// file's only option visibility conditions are these four and DISPLAY TYPE, which swaps the display models and hides no
/// control). Every hidden control refuses aloud while its option is off, whatever kind of row it is.
/// </summary>
public class A350OptionsTests
{
    private readonly IniBuildsA350Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();

    public A350OptionsTests()
    {
        _def = new IniBuildsA350Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            Delay = _ => new TaskCompletionSource().Task,
            ReadFresh = (_, _, _) => Task.FromResult<double?>(null),
        };
        _def.Attach(_sim);
    }

    public static TheoryData<string, string> HiddenControls()
    {
        var data = new TheoryData<string, string>();
        foreach (var option in A350Options.All)
            foreach (var id in option.ControlIds)
                data.Add(option.Var, id);
        return data;
    }

    [Fact]
    public void The_options_are_the_four_the_cockpit_hides_controls_for()
    {
        Assert.Equal(new[] { "INI_OPTION_FUEL_JETTISON", "INI_OPTION_BRAKE_FANS", "INI_OPTION_FO_ISIS", "INI_IS_HUD" },
            A350Options.All.Select(o => o.Var));
    }

    [Theory]
    [MemberData(nameof(HiddenControls))]
    public void Every_hidden_control_is_in_the_control_map(string optionVar, string controlId)
    {
        Assert.True(A350ControlMap.Load().Controls.Any(c => c.Id == controlId), $"{controlId} ({optionVar}) is not in the 1.2.8 map");
    }

    [Fact]
    public void No_control_is_hidden_by_two_options()
    {
        var ids = A350Options.All.SelectMany(o => o.ControlIds).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Every_option_variable_is_registered_once()
    {
        var vars = _def.GetVariables();
        foreach (var option in A350Options.All)
        {
            Assert.Single(vars.Values, v => v.Name == option.Var);
            Assert.Equal(option.Name + " option", vars[option.Key].DisplayName);
        }
    }

    /// <summary>Each of the option's controls, as each of its rows: a press, a set, a typed value or a step.</summary>
    [Theory]
    [MemberData(nameof(HiddenControls))]
    public void Each_option_not_fitted_is_said_for_each_hidden_control(string optionVar, string controlId)
    {
        var option = A350Options.All.Single(o => o.Var == optionVar);
        var control = A350ControlMap.Load().Controls.Single(c => c.Id == controlId);
        var rows = _def.GetVariables().Keys.Where(k => k == control.Key || k.StartsWith(control.Key + "#", StringComparison.Ordinal))
            .Where(k => _def.GetPanelControls().Values.Any(list => list.Contains(k)))
            .ToList();
        Assert.NotEmpty(rows);
        _cache[option.Key] = 0;
        foreach (var row in rows)
        {
            _sent.Clear();
            _speech.All.Clear();
            _def.HandleUIVariableSet(row, 1, _def.GetVariables()[row], _sim, _speech);
            Assert.Empty(_sent);
            Assert.Equal(new[] { option.NotFitted }, _speech.All);
        }
    }
}
