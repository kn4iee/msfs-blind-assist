using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    /// <summary>Continuous keys never spoken by the generic monitor (knob positions, caches).</summary>
    private readonly HashSet<string> _silentKeys = new();

    /// <summary>The Continuous Type:Name pairs added so far in the current BuildVariables, so a generated
    /// status row or lamp reading a name another row already watches is skipped ([VAR-7]).</summary>
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);

    private void Add(Dictionary<string, SimVarDefinition> vars, string key, SimVarDefinition d)
    {
        vars[key] = d;
        if (d.UpdateFrequency == UpdateFrequency.Continuous) _usedNames.Add(d.Type + ":" + d.Name);
    }

    private void Button(Dictionary<string, SimVarDefinition> vars, string key, string display) => Add(vars, key, new SimVarDefinition
    {
        Name = "MSFSBA_" + key, DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        RenderAsButton = true, SuppressRestingButtonState = true,
        ValueDescriptions = new Dictionary<double, string> { [0] = "Idle", [1] = "Press" },
    });

    private void Watched(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, Dictionary<double, string>? values = null, bool silent = false,
                 string units = "number", SimVarType type = SimVarType.LVar) => Add(vars, key, new SimVarDefinition
    {
        Name = name, DisplayName = display, Type = type, Units = units,
        UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
        ValueDescriptions = values ?? new Dictionary<double, string>(),
        ExcludeFromMonitorManager = silent,
    });

    private void Silent(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
    {
        Watched(vars, key, name, display, null, true, units, type);
        _silentKeys.Add(key);
    }

    private void Synthetic(Dictionary<string, SimVarDefinition> vars, string key, string display) => Add(vars, key, new SimVarDefinition
    {
        Name = $"MSFSBA_{key}", DisplayName = display, Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
    });

    private void OnRequestVar(Dictionary<string, SimVarDefinition> vars, string key, string name, string display, string units = "number", SimVarType type = SimVarType.LVar)
        => Add(vars, key, new SimVarDefinition { Name = name, DisplayName = display, Type = type, Units = units, UpdateFrequency = UpdateFrequency.OnRequest });

    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = new Dictionary<string, SimVarDefinition>();
        _usedNames.Clear();

        // End-to-end MobiFlight probe target: the momentary keys, the FCU press flags and the MCDU keys
        // are calculator writes, so they gate on the probe's verdict; registering this opts in.
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE", DisplayName = "Bridge Probe", Type = SimVarType.LVar, UpdateFrequency = UpdateFrequency.OnRequest,
        };

        // each owner adds its variables, in this order; the base variables below fill only what is absent
        AddPanelVariables(vars);   // plan 04
        AddFcuVariables(vars);   // plan 05
        AddFlightDisplayVariables(vars);   // plan 09
        AddEcamVariables(vars);   // plan 08
        AddMcduVariables(vars);   // plan 03
        AddReadoutVariables(vars);   // plan 10
        AddFirstOfficerVariables(vars);   // plan 06

        foreach (var kv in GetBaseVariables())
            if (!vars.ContainsKey(kv.Key)) vars[kv.Key] = kv.Value;
        return vars;
    }
}
