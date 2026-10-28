using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>One cockpit light a panel shows: the lamp node it is, what a pilot calls it, its rule and its bus.</summary>
public sealed class A350Lamp
{
    private readonly IReadOnlyDictionary<string, string> _keyOf;

    internal A350Lamp(string node, string name, string panel, A350LampRule rule, A350LightPower power, string? controlId = null)
    {
        ControlId = controlId;
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

    /// <summary>The map control whose button this lamp is on, or null for an indicator of its own.</summary>
    public string? ControlId { get; }

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
        // Instrument panel: the BRAKES HOT legend on the fan button; OIS VIEW OFFSIDE's INOP (the other side views this
        // side's OIS) and its own ON.
        ["LDG_BRK_HOT_SEQ1_LIGHT"] = "Brakes hot light",
        ["LDG_AUTO_BRK_SEQ1_LIGHT"] = "A/BRK light",
        ["OIS_VIEW_OFFSIDE_LEFT_SEQ1_LIGHT"] = "Captain OIS view offside INOP light",
        ["OIS_VIEW_OFFSIDE_LEFT_SEQ2_LIGHT"] = "Captain OIS view offside on light",
        ["OIS_VIEW_OFFSIDE_RIGHT_SEQ1_LIGHT"] = "First officer OIS view offside INOP light",
        ["OIS_VIEW_OFFSIDE_RIGHT_SEQ2_LIGHT"] = "First officer OIS view offside on light",
        // ADIRS: each IR and ADR button's OFF legend, lit while it is off and its ADIRS runs.
        ["IRS_IR_1_SEQ2_LIGHT"] = "IR 1 off light", ["IRS_IR_2_SEQ2_LIGHT"] = "IR 2 off light", ["IRS_IR_3_SEQ2_LIGHT"] = "IR 3 off light",
        ["APU_START_SEQ2_LIGHT"] = "Start on light",   // ON while starting, until AVAIL
        // FCU: the mode buttons' lit bar; the master lights' two halves; one light per test button.
        ["FCU_AP_1_SEQ1_LIGHT"] = "AP 1 light", ["FCU_AP_2_SEQ1_LIGHT"] = "AP 2 light", ["FCU_ATHR_SEQ1_LIGHT"] = "A/THR light",
        ["FCU_LOC_SEQ1_LIGHT"] = "LOC light", ["FCU_APPR_SEQ1_LIGHT"] = "APPR light", ["FCU_ALT_SEQ1_LIGHT"] = "ALT light",
        ["FCU_MASTER_CAUTION_LEFT_SEQ1_LIGHT"] = "Captain master caution light",
        ["FCU_MASTER_CAUTION_LEFT_SEQ2_LIGHT"] = "Captain master caution lower light",
        ["FCU_MASTER_CAUTION_RIGHT_SEQ1_LIGHT"] = "First officer master caution light",
        ["FCU_MASTER_CAUTION_RIGHT_SEQ2_LIGHT"] = "First officer master caution lower light",
        ["FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT"] = "Captain master warning light",
        ["FCU_MASTER_WARNING_LEFT_SEQ2_LIGHT"] = "Captain master warning lower light",
        ["FCU_MASTER_WARNING_RIGHT_SEQ1_LIGHT"] = "First officer master warning light",
        ["FCU_MASTER_WARNING_RIGHT_SEQ2_LIGHT"] = "First officer master warning lower light",
        ["FCU_AUTO_LAND_LEFT_SEQ1_LIGHT"] = "Captain autoland light", ["FCU_AUTO_LAND_RIGHT_SEQ1_LIGHT"] = "First officer autoland light",
        ["FCU_ATC_MSG_LEFT_SEQ1_LIGHT"] = "Captain ATC message light", ["FCU_ATC_MSG_RIGHT_SEQ1_LIGHT"] = "First officer ATC message light",
        // Each cargo isolation valve's upper legend: lit while the valve and its button disagree.
        ["CARGO_AIR_COND_AFT_ISOL_VALVE_SEQ1_LIGHT"] = "Aft isolation valves fault light",
        ["CARGO_AIR_COND_FWD_ISOL_VALVE_SEQ1_LIGHT"] = "Forward isolation valves fault light",
        ["CARGO_AIR_COND_BULK_ISOL_VALVE_SEQ1_LIGHT"] = "Bulk isolation valves fault light",
        ["GND_HYD_GREEN_ELEC_PUMP_AUTO_SEQ2_LIGHT"] = "Green electric pump override on light",
        ["GND_HYD_YELLOW_ELEC_PUMP_AUTO_SEQ2_LIGHT"] = "Yellow electric pump override on light",
        ["ELEC_EXT_1_SEQ2_LIGHT"] = "EXT 1 on light", ["ELEC_EXT_2_SEQ2_LIGHT"] = "EXT 2 on light",
        // The DRIVE buttons' lower legend: the IDG disconnected.
        ["ELEC_IDG_1A_SEQ2_LIGHT"] = "DRIVE 1A off light", ["ELEC_IDG_1B_SEQ2_LIGHT"] = "DRIVE 1B off light",
        ["ELEC_IDG_2A_SEQ2_LIGHT"] = "DRIVE 2A off light", ["ELEC_IDG_2B_SEQ2_LIGHT"] = "DRIVE 2B off light",
        ["IRS_ADR_1_SEQ2_LIGHT"] = "ADR 1 off light", ["IRS_ADR_2_SEQ2_LIGHT"] = "ADR 2 off light", ["IRS_ADR_3_SEQ2_LIGHT"] = "ADR 3 off light",
        // Pedestal: each engine master's upper legend is its FIRE light (INI_ENG_n_FIRE).
        ["ENG_MASTER_1_SEQ1_LIGHT"] = "ENG 1 master fire light", ["ENG_MASTER_2_SEQ1_LIGHT"] = "ENG 2 master fire light",
        // The OIS ON CENTER buttons' upper legend is INOP (INI_OIS_ON_CENTER_*_INOP); the cockpit door's OPEN and FAULT;
        // EMER DESCENT's ARMED (INI_EMER_DES_ARMED_LIGHT).
        ["OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_LEFT_SEQ1_LIGHT"] = "Captain OIS on center INOP light",
        ["OIS_DISPLAY_TRANSFER_OIS_ON_CENTER_RIGHT_SEQ1_LIGHT"] = "First officer OIS on center INOP light",
        ["CKPT_DOOR_LT_SEQ1_LIGHT"] = "Cockpit door open light", ["CKPT_DOOR_LT_SEQ2_LIGHT"] = "Cockpit door fault light",
        ["EMER_DESCENT_SEQ2_LIGHT"] = "Emergency descent armed light",
    };

    /// <summary>Lamps read from a steadier variable than their emissive rule. MASTER WARN flashes, 0.5 s lit and 0.5 s
    /// dark, and the 1 Hz delivery caught only its dark phase for a whole fire test, twice in three (live 2026-10-28);
    /// INI_MASTER_WARNING_ACTIVE holds 1 for the whole warning until the pilot's press puts it out.</summary>
    private static readonly Dictionary<string, string> SteadyRules = new(StringComparer.Ordinal)
    {
        ["FCU_MASTER_WARNING_LEFT_SEQ1_LIGHT"] = "(L:INI_MASTER_WARNING_ACTIVE)",
        ["FCU_MASTER_WARNING_LEFT_SEQ2_LIGHT"] = "(L:INI_MASTER_WARNING_ACTIVE)",
        ["FCU_MASTER_WARNING_RIGHT_SEQ1_LIGHT"] = "(L:INI_MASTER_WARNING_ACTIVE)",
        ["FCU_MASTER_WARNING_RIGHT_SEQ2_LIGHT"] = "(L:INI_MASTER_WARNING_ACTIVE)",
    };

    /// <summary>Indicator lamps with no control of their own: node → the panel they sit on and their name, shown after
    /// the panel's button lights.</summary>
    private static readonly (string Node, string Panel, string Name)[] Indicators =
    {
        ("CARGO_SMOKE_BOTTLE_1_DISCH_LIGHT", "Cargo Smoke", "Bottle 1 discharged light"),
        ("CARGO_SMOKE_BOTTLE_2_DISCH_LIGHT", "Cargo Smoke", "Bottle 2 discharged light"),
        ("CARGO_SMOKE_BOTTLE_3_DISCH_LIGHT", "Cargo Smoke", "Bottle 3 discharged light"),
        ("VENT_AVNCS_SMOKE_SEQ1_LIGHT", "Ventilation", "Avionics smoke light"),
        ("MLG_BAY_LIGHT_SEQ1_LIGHT", "Fire", "MLG bay fire light"),
        ("IRS_ON_BAT_LIGHT", "ADIRS", "IRS on battery light"),
        ("CABIN_LAVATORY_SEQ1_LIGHT", "Cabin", "Lavatory occupied light"),
    };

    /// <summary>Indicators the map has no lamp node for, read from their own variable, shown after the map's: each oxygen
    /// mask's flow blinker, yellow while oxygen flows (its PRESS TO TEST held, live 2026-10-28), mechanical, so no bus.</summary>
    private static readonly (string Node, string Panel, string Name, string State)[] OwnIndicators =
    {
        ("OXYTEST_BLINKER_CPT", "Oxygen Masks", "Captain oxygen flow blinker", "(L:INI_OXYTEST_BLINKER_CPT) 0.5 >"),
        ("OXYTEST_BLINKER_FO", "Oxygen Masks", "First officer oxygen flow blinker", "(L:INI_OXYTEST_BLINKER_FO) 0.5 >"),
        ("OXYTEST_BLINKER_TO", "Oxygen Masks", "Third occupant oxygen flow blinker", "(L:INI_OXYTEST_BLINKER_TO) 0.5 >"),
        ("OXYTEST_BLINKER_4TH", "Oxygen Masks", "Fourth occupant oxygen flow blinker", "(L:INI_OXYTEST_BLINKER_4TH) 0.5 >"),
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

        var byNode = map.Lamps.GroupBy(l => l.Node).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var lamps = new List<A350Lamp>();
        foreach (var section in sections)
            foreach (var panel in placement.Structure[section])
            {
                foreach (var row in placement.RowsByPanel[panel])
                {
                    if (row.Control is not { } control || !byControl.TryGetValue(control.Id, out var own))
                        continue;
                    string? stateVar = control.StateVar is { Length: > 2 } sv && sv[1] == ':' ? sv.Substring(2) : null;
                    var rules = new HashSet<string>(StringComparer.Ordinal);
                    var rowLamps = new List<(A350MapLamp Lamp, A350LampRule Rule, string Legend)>();
                    foreach (var mapLamp in own.OrderBy(l => l.Node, StringComparer.Ordinal))
                    {
                        if (control.Kind == A350Kinds.Selector && mapLamp.Node.EndsWith("_PANEL_LIGHT", StringComparison.Ordinal))
                            continue;   // a selector position's marker: the row already says the position
                        string state = SteadyRules.GetValueOrDefault(mapLamp.Node, mapLamp.State);
                        if (!rules.Add(state))
                            continue;   // a second legend lit by the same rule (ATC MSG, AUTOLAND, MASTER WARN): one light
                        var rule = A350LampRule.Parse(state);
                        if (stateVar != null && rule.Inputs.All(i => i == stateVar))
                            continue;   // its switch's own position legend
                        rowLamps.Add((mapLamp, rule, LegendOf(mapLamp.Node, rule, stateVar)));
                    }
                    foreach (var (mapLamp, rule, legend) in rowLamps)
                    {
                        // A button's only light with no legend word of its own is just its light (CSTR, VOR 1).
                        bool alone = rowLamps.Count == 1 && legend is "upper legend" or "lower legend" or "";
                        string name = NameOverrides.TryGetValue(mapLamp.Node, out var given) ? given
                            : alone || legend.Length == 0 ? $"{row.Name} light" : $"{row.Name} {legend} light";
                        lamps.Add(new A350Lamp(mapLamp.Node, name, panel, rule, mapLamp.Power, control.Id));
                    }
                }
                foreach (var (node, onPanel, name) in Indicators)
                    if (onPanel == panel && byNode.TryGetValue(node, out var indicator))
                        lamps.Add(new A350Lamp(node, name, panel, A350LampRule.Parse(indicator.State), indicator.Power));
                foreach (var (node, onPanel, name, state) in OwnIndicators)
                    if (onPanel == panel)
                        lamps.Add(new A350Lamp(node, name, panel, A350LampRule.Parse(state), A350LightPower.None));
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
        // An engine's hydraulic SUPPLY button: its lower legend is SHUT, lit while its fire shut-off valve is shut.
        if (node.StartsWith("HYD_SUPPLY_ENG_", StringComparison.Ordinal) && node.Contains("_SEQ2_", StringComparison.Ordinal))
            return "shut";
        return node.Contains("_SEQ1_", StringComparison.Ordinal) ? "upper legend"
            : node.Contains("_SEQ2_", StringComparison.Ordinal) ? "lower legend"
            : "";
    }
}
