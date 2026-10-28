using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>What a panel row does when the pilot operates it.</summary>
public enum A350RowAction { Set, Press, Increase, Decrease, Custom, Typed }

/// <summary>A row after placement: the variable key MSFSBA registers, and what it drives.</summary>
/// <param name="Positions">State value → spoken word, for a Set row's combo; empty for the rest.</param>
public sealed record A350PlacedRow(string Key, string Name, A350RowAction Action, A350Control? Control,
    IReadOnlyDictionary<double, string> Positions);

public sealed class A350Placement
{
    /// <summary>Section → panel names, in cockpit order.</summary>
    public Dictionary<string, List<string>> Structure { get; } = new();
    /// <summary>Panel name → placed rows, in cockpit order.</summary>
    public Dictionary<string, List<A350PlacedRow>> RowsByPanel { get; } = new();
}

/// <summary>
/// The A350's panels. The section and panel a control belongs to come from the component tree iniBuilds grouped
/// the cockpit by (A350_INTERIOR / OVERHEAD, EFIS, MIP, PEDESTAL, MISC / group), so an iniBuilds update is picked up
/// by regenerating the map. The overhead panels are named and ordered as the manual's cockpit layout numbers them
/// (Airbus A350 Manual 1.5, p. 24); a control iniBuilds files under another group goes to its own panel through
/// <see cref="ControlPanels"/> (the jettison buttons sit in FUEL, the RAT button in ELEC). Rows keep the cockpit
/// file's order. Guard covers are not rows (they move only the 3D model: their Set code writes O: variables alone),
/// nor are controls the map cannot drive (the levers, which are hand-written).
/// </summary>
public static partial class A350PanelLayout
{
    public static readonly IReadOnlyList<string> SectionOrder = new[]
    {
        "Overhead", "Glareshield", "Instrument", "Pedestal", "Cockpit",
    };

    private static readonly Dictionary<string, string> AreaSections = new(StringComparer.Ordinal)
    {
        ["OVERHEAD"] = "Overhead",
        ["EFIS"] = "Glareshield",
        ["MIP"] = "Instrument",
        ["PEDESTAL"] = "Pedestal",
        ["MISC"] = "Cockpit",
    };

    /// <summary>Whether a map control is placed on a panel at all.</summary>
    public static bool IsPlaced(A350Control control) =>
        control.Kind is not (A350Kinds.Cover or A350Kinds.None) && !DeferredPanels.Contains(control.Panel)
        && !HiddenControls.Contains(Short(control.Id));

    /// <summary>The section a panel's order lists it in, else iniBuilds' area's.</summary>
    public static string SectionFor(A350Control control)
    {
        string panel = PanelFor(control);
        foreach (var (section, panels) in PanelOrder)
            if (panels.Contains(panel))
                return section;
        return AreaSections.TryGetValue(control.Area, out var byArea) ? byArea : "Cockpit";
    }

    public static string PanelFor(A350Control control)
    {
        if (control.Panel == RmpGroup)
            return RmpPanel(Short(control.Id));
        if (ControlPanels.TryGetValue(Short(control.Id), out var own))
            return own;
        return PanelNames.TryGetValue(control.Panel, out var panel) ? panel : SpokenWord(control.Panel.Replace('_', ' '));
    }

    /// <summary>The input event id without its <c>AIRLINER_</c> prefix.</summary>
    public static string Short(string id) =>
        id.StartsWith("AIRLINER_", StringComparison.OrdinalIgnoreCase) ? id.Substring("AIRLINER_".Length) : id;

    /// <summary>The spoken name of a placed control.</summary>
    public static string NameFor(A350Control control)
    {
        string id = Short(control.Id);
        if (control.Panel == RmpGroup)
            return RmpName(id, control.Title);
        if (NameOverrides.TryGetValue(id, out var name))
            return name;
        return SpokenWord(string.IsNullOrWhiteSpace(control.Title) ? id.Replace('_', ' ') : control.Title);
    }

    /// <summary>State value → spoken word for a Set row: the layout's override where there is one, else iniBuilds'
    /// word in sentence case, else "Position n".</summary>
    public static IReadOnlyDictionary<double, string> PositionWords(A350Control control)
    {
        // A lever's words are already spoken (the A380's and A320's, A350Levers).
        IReadOnlyDictionary<double, string>? overrides =
            PositionOverrides.TryGetValue(Short(control.Id), out var own) ? own : RmpPositions(Short(control.Id)) ?? A350Levers.WordsFor(control.Id);
        var words = new Dictionary<double, string>();
        foreach (var (position, label) in control.OrderedPositions())
        {
            string word = overrides != null && overrides.TryGetValue(position, out var o) ? o
                : string.IsNullOrWhiteSpace(label) ? $"Position {position.ToString(CultureInfo.InvariantCulture)}"
                : SpokenWord(label);
            words[control.StateForPosition(position)] = word;
        }
        return words;
    }

    /// <summary>Abbreviations pilots say as letters, kept in capitals by <see cref="SpokenWord"/>.</summary>
    private static readonly HashSet<string> SpokenAsLetters = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC", "ACMS", "ADIRS", "ADR", "APU", "ATC", "AVNCS", "BAT", "CAB", "CKPT", "CTR", "CVR", "DC", "DFDR", "ELEC",
        "ELM", "ELT", "EMER", "ENG", "EVAC", "FADEC", "GND", "HF", "IDG", "IFEC", "IR", "IRS", "ISOL", "LT", "OIS",
        "PAX", "PRIM", "PURS", "PWR", "RAT", "RCDR", "ROP", "ROW", "SEC", "STBY", "SVCE", "SW", "TK", "VHF", "XBLEED",
        "XFR",
    };

    /// <summary>
    /// Spoken form of one of iniBuilds' upper-case tooltip words: sentence case ("SEAT BELTS" → "Seat belts"),
    /// except that a single letter, a token with a digit ("1A", "N1") and an abbreviation said as letters
    /// ("BAT 1", "APU GEN") keep their capitals. Tokens are separated by spaces, '/', '-', '(', ')' and '&amp;',
    /// which are kept as they were.
    /// </summary>
    public static string SpokenWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;
        string trimmed = word.Trim();
        var spoken = new StringBuilder(trimmed.Length);
        bool first = true;
        int i = 0;
        while (i < trimmed.Length)
        {
            if (IsWordSeparator(trimmed[i]))
            {
                spoken.Append(trimmed[i++]);
                continue;
            }
            int start = i;
            while (i < trimmed.Length && !IsWordSeparator(trimmed[i]))
                i++;
            spoken.Append(SpokenToken(trimmed.Substring(start, i - start), first));
            first = false;
        }
        return spoken.ToString();
    }

    private static bool IsWordSeparator(char c) => c is ' ' or '/' or '-' or '(' or ')' or '&';

    private static string SpokenToken(string token, bool first)
    {
        if (token.Any(char.IsAsciiDigit))
            return token.ToUpperInvariant();
        if ((token.Length == 1 && char.IsLetter(token[0])) || SpokenAsLetters.Contains(token))
            return token.ToUpperInvariant();
        string lower = token.ToLowerInvariant();
        return first ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
    }

    /// <summary>Places every placed map control in map (cockpit file) order, panels in <see cref="PanelOrder"/>.
    /// An encoder whose value the cockpit shows is one typed row (<see cref="A350TypedEntry"/>); any other becomes two
    /// rows ("… increase", "… decrease").</summary>
    public static A350Placement Place(A350ControlMap map)
    {
        var placement = new A350Placement();
        foreach (var section in SectionOrder)
            placement.Structure[section] = new List<string>();

        foreach (var control in A350Levers.Merge(map.Controls))   // the levers are hand-written (A350Levers)
        {
            if (!IsPlaced(control))
                continue;
            var rows = RowsFor(placement, SectionFor(control), PanelFor(control));
            string name = NameFor(control);
            switch (control.Kind)
            {
                case A350Kinds.Toggle when IsMomentary(control):
                    rows.Add(new A350PlacedRow(control.Key, name, A350RowAction.Press, control, new Dictionary<double, string>()));
                    break;
                case A350Kinds.Toggle:
                case A350Kinds.Command:
                case A350Kinds.Selector:
                case A350Kinds.Spring:
                    rows.Add(new A350PlacedRow(control.Key, name, A350RowAction.Set, control, PositionWords(control)));
                    break;
                case A350Kinds.Knob:
                    rows.Add(new A350PlacedRow(control.Key, name, A350RowAction.Set, control, new Dictionary<double, string>()));
                    break;
                case A350Kinds.Button:
                case A350Kinds.Hold:
                    rows.Add(new A350PlacedRow(control.Key, name, A350RowAction.Press, control, new Dictionary<double, string>()));
                    break;
                case A350Kinds.Encoder when control.Id == A350RunwayCondition.KnobId:
                    // A pick the knob makes by its own clicks (A350RunwayCondition), as one choice.
                    rows.Add(new A350PlacedRow(A350RunwayCondition.Key, A350RunwayCondition.Name, A350RowAction.Custom, control,
                        A350RunwayCondition.Codes));
                    break;
                case A350Kinds.Encoder when A350TypedEntry.ForControl(control.Id) is { } typed:
                    // A value the cockpit shows is typed, as the released aircraft type theirs.
                    rows.Add(new A350PlacedRow(typed.Key, typed.Name, A350RowAction.Typed, control, new Dictionary<double, string>()));
                    break;
                case A350Kinds.Encoder:
                    rows.Add(new A350PlacedRow(control.Key + "#INC", name + " increase", A350RowAction.Increase, control, new Dictionary<double, string>()));
                    rows.Add(new A350PlacedRow(control.Key + "#DEC", name + " decrease", A350RowAction.Decrease, control, new Dictionary<double, string>()));
                    break;
            }
        }

        foreach (var (section, panels) in placement.Structure)
        {
            var ordered = panels.OrderBy(p => PanelOrder.TryGetValue(section, out var order) && order.IndexOf(p) is int at && at >= 0 ? at : int.MaxValue)
                .ToList();   // a panel the order does not name keeps its place after the named ones (OrderBy is stable)
            panels.Clear();
            panels.AddRange(ordered);
        }
        foreach (var section in placement.Structure.Where(s => s.Value.Count == 0).Select(s => s.Key).ToList())
            placement.Structure.Remove(section);
        return placement;
    }

    private static List<A350PlacedRow> RowsFor(A350Placement placement, string section, string panel)
    {
        if (!placement.RowsByPanel.TryGetValue(panel, out var rows))
        {
            rows = new List<A350PlacedRow>();
            placement.RowsByPanel[panel] = rows;
            if (!placement.Structure.TryGetValue(section, out var panels))
                placement.Structure[section] = panels = new List<string>();
            panels.Add(panel);
        }
        return rows;
    }
}
