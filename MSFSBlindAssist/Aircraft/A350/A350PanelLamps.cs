using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One cockpit light a panel shows: the lamp node it is, what a pilot calls it, its rule and its bus.</summary>
public sealed class A350Lamp
{
    private readonly IReadOnlyDictionary<string, string> _keyOf;

    internal A350Lamp(string node, string name, string panel, A350LampRule rule, A350LightPower power)
    {
        Node = node;
        Name = name;
        Panel = panel;
        Rule = rule;
        Power = power;
        _keyOf = rule.Inputs.Select((input, i) => (input, key: i == 0 ? Key : A350PanelLamps.InputKey(input)))
            .ToDictionary(p => p.input, p => p.key, StringComparer.Ordinal);
        var keys = rule.Inputs.Select(KeyFor).ToList();
        if (A350PanelLamps.PowerKeyFor(power) is string powerKey)
            keys.Add(powerKey);
        InputKeys = keys;
    }

    /// <summary>The lamp's node in the generated map, e.g. <c>ELEC_BAT_1_SEQ1_LIGHT</c>.</summary>
    public string Node { get; }
    public string Name { get; }
    public string Panel { get; }
    public A350LampRule Rule { get; }
    public A350LightPower Power { get; }

    /// <summary>Its MSFSBA variable key, which streams the rule's first variable and carries its status line.</summary>
    public string Key => "A350_LT_" + Node;

    /// <summary>The key a variable of its rule arrives under: the first under the lamp's own key, the others under
    /// shared <see cref="A350PanelLamps.InputKey"/> keys.</summary>
    public string KeyFor(string input) => _keyOf[input];

    /// <summary>Every key the lamp is made of: its rule's inputs, then its bus's light power.</summary>
    public IReadOnlyList<string> InputKeys { get; }

    /// <summary>Whether the cockpit shows the lamp lit, its rule AND its bus's light power, from values read by key;
    /// null while an input is unread.</summary>
    public bool? IsLit(Func<string, double?> read)
    {
        if (Rule.IsLit(input => read(KeyFor(input))) is not bool state)
            return null;
        if (A350PanelLamps.PowerKeyFor(Power) is not string powerKey)
            return state;
        return read(powerKey) is double powered ? state && powered >= 0.5 : null;
    }
}

/// <summary>
/// The cockpit lights each curated panel shows, generated from the map's lamps (the aircraft's own emissive rules):
/// a lamp belongs to the placed row whose control id is the longest prefix of its node (<c>ELEC_BAT_1_SEQ1_LIGHT</c>
/// → BAT 1), and is named after it plus its legend. A legend that reads nothing but its own switch's position is
/// left out: the row already says it. A lamp lights as the cockpit shows it, its rule AND its bus's light power
/// (<c>INI_AC_LIGHTS_FAILURE</c>, <c>INI_DC_LIGHTS_FAILURE</c>, 1 = powered, as on the A300).
/// </summary>
public static class A350PanelLamps
{
    public const string AcPowerKey = "A350_LIGHTS_AC_POWER";
    public const string AcPowerVar = "INI_AC_LIGHTS_FAILURE";
    public const string DcPowerKey = "A350_LIGHTS_DC_POWER";
    public const string DcPowerVar = "INI_DC_LIGHTS_FAILURE";

    private static readonly Regex NodeTail = new(@"(_SEQ\d+)?(_PANEL)?_LIGHT$", RegexOptions.CultureInvariant);

    /// <summary>A legend named from the suffix of the variable it reads, first match wins.</summary>
    private static readonly (string Suffix, string Legend)[] Legends =
    {
        ("_FAULT", "fault"), ("_FAILURE", "fault"), ("_OVHT", "overheat"), ("_SMOKE", "smoke"),
        ("_DISCH", "discharged"), ("_SQUIB", "squib"), ("_AVAILABLE", "available"), ("_AVAIL", "available"),
        ("_OPEN", "open"), ("_ISOL", "isolated"), ("_CALL", "call"), ("_END", "end"), ("_ON", "on"),
    };

    /// <summary>A lamp's name where the generated one is vague: its legend, as printed on the button.</summary>
    private static readonly Dictionary<string, string> NameOverrides = new(StringComparer.Ordinal)
    {
        ["EVAC_COMMAND_SEQ1_LIGHT"] = "EVAC light",
        // ADIRS: each IR and ADR button's OFF legend, lit while it is off and its ADIRS runs.
        ["IRS_IR_1_SEQ2_LIGHT"] = "IR 1 off light", ["IRS_IR_2_SEQ2_LIGHT"] = "IR 2 off light", ["IRS_IR_3_SEQ2_LIGHT"] = "IR 3 off light",
        ["APU_START_SEQ2_LIGHT"] = "Start on light",   // ON while starting, until AVAIL
        ["IRS_ADR_1_SEQ2_LIGHT"] = "ADR 1 off light", ["IRS_ADR_2_SEQ2_LIGHT"] = "ADR 2 off light", ["IRS_ADR_3_SEQ2_LIGHT"] = "ADR 3 off light",
    };

    public static string? PowerKeyFor(A350LightPower power) => power switch
    {
        A350LightPower.Ac => AcPowerKey,
        A350LightPower.Dc => DcPowerKey,
        _ => null,
    };

    /// <summary>The key a variable some lamp reads, other than as its first, arrives under: <c>A350_IN_</c> + its name.</summary>
    public static string InputKey(string input) => "A350_IN_" + input;

    /// <summary>The lights of the placed rows in <paramref name="sections"/>, panel by panel in row order.</summary>
    public static IReadOnlyList<A350Lamp> Resolve(A350ControlMap map, A350Placement placement, IEnumerable<string> sections)
    {
        var byControl = new Dictionary<string, List<A350MapLamp>>(StringComparer.Ordinal);
        var ids = map.Controls.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var lamp in map.Lamps)
            if (OwnerOf(lamp.Node, ids) is string id)
            {
                if (!byControl.TryGetValue(id, out var list))
                    byControl[id] = list = new List<A350MapLamp>();
                list.Add(lamp);
            }

        var lamps = new List<A350Lamp>();
        foreach (var section in sections)
            foreach (var panel in placement.Structure[section])
                foreach (var row in placement.RowsByPanel[panel])
                {
                    if (row.Control is not { } control || !byControl.TryGetValue(control.Id, out var own))
                        continue;
                    string? stateVar = control.StateVar is { Length: > 2 } sv && sv[1] == ':' ? sv.Substring(2) : null;
                    foreach (var mapLamp in own.OrderBy(l => l.Node, StringComparer.Ordinal))
                    {
                        var rule = A350LampRule.Parse(mapLamp.State);
                        if (stateVar != null && rule.Inputs.All(i => i == stateVar))
                            continue;   // its switch's own position legend
                        string legend = LegendOf(mapLamp.Node, rule, stateVar);
                        string name = NameOverrides.TryGetValue(mapLamp.Node, out var given) ? given
                            : legend.Length == 0 ? $"{row.Name} light" : $"{row.Name} {legend} light";
                        lamps.Add(new A350Lamp(mapLamp.Node, name, panel, rule, mapLamp.Power));
                    }
                }
        return lamps;
    }

    /// <summary>The control id (<c>AIRLINER_</c> + prefix) a lamp node belongs to: the longest one its base starts with.</summary>
    private static string? OwnerOf(string node, HashSet<string> ids)
    {
        string baseName = NodeTail.Replace(node, "");
        string? best = null;
        foreach (var id in ids)
        {
            if (!id.StartsWith("AIRLINER_", StringComparison.Ordinal))
                continue;
            string prefix = id.Substring("AIRLINER_".Length);
            if ((baseName == prefix || baseName.StartsWith(prefix + "_", StringComparison.Ordinal))
                && (best == null || id.Length > best.Length))
                best = id;
        }
        return best;
    }

    /// <summary>The legend from the first variable the rule reads other than its switch's own; failing a known suffix,
    /// which legend it is on the button.</summary>
    private static string LegendOf(string node, A350LampRule rule, string? stateVar)
    {
        string? other = rule.Inputs.FirstOrDefault(i => i != stateVar);
        if (other != null)
            foreach (var (suffix, legend) in Legends)
                if (other.EndsWith(suffix, StringComparison.Ordinal))
                    return legend;
        return node.Contains("_SEQ1_", StringComparison.Ordinal) ? "upper legend"
            : node.Contains("_SEQ2_", StringComparison.Ordinal) ? "lower legend"
            : "";
    }
}
