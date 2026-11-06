namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// The page grammar's rules about where on the tablet the pilot is, read from BlueBird's own control names (plan 20, R1 and R2).
/// A side-menu section is told by a distinctive name its page draws; the few pages that draw no container named for their
/// section are in the table below, every other page's section is the container the reader finds by name
/// (<see cref="Bbs757EfbModelReader"/>). Evidence: <c>docs/b757/evidence/efb-memory-pages.md</c>.
/// </summary>
internal static class Bbs757EfbPageRules
{
    /// <summary>Per app, a name the page draws and the side-menu word it makes the section. A name ending in an underscore is a
    /// prefix (every <c>fuel_*</c> control of Fuel), any other is the exact name.</summary>
    private static readonly (string App, string Name, string Section)[] Table =
    {
        ("AIRCRAFT", "presets_label", "COCKPIT PRESETS"),
        ("AIRCRAFT", "fuel_", "FUEL"),
        ("AIRCRAFT", "wb_", "WEIGHT & BALANCE"),
        ("AIRCRAFT", "gnd_service_", "GROUND SERVICES"),
        ("AIRCRAFT", "cabin_lighting_section", "CABIN & DOORS"),
        ("AIRCRAFT", "maintenance_", "MAINTENANCE"),
        ("AIRCRAFT", "failures_canvas", "FAILURES"),
        // Settings > Livery options draws no scrollbox: its rows sit in the stackbox Settings > Aircraft shares.
        ("SETTINGS", "weight_units_setting_grid", "LIVERY OPTIONS"),
        ("SETTINGS", "airac_data_grid", "AIRAC DATA"),
    };

    /// <summary>The apps whose pages have sections (Systems and Checklist read as their app alone).</summary>
    internal static bool HasSections(string app) => app is "AIRCRAFT" or "NAVIGATION" or "SETTINGS";

    /// <summary>The section the table gives the page, from the control names it draws; null when no entry matches.</summary>
    internal static string? SectionFromTable(string app, IEnumerable<string> names)
    {
        var all = names as IReadOnlyCollection<string> ?? names.ToList();
        foreach (var (a, name, section) in Table)
        {
            if (a != app) continue;
            bool prefix = name.EndsWith('_');
            foreach (var n in all)
                if (prefix ? n.StartsWith(name, StringComparison.Ordinal) : n == name) return section;
        }
        return null;
    }

    private static readonly string[] ContainerSuffixes = { "_canvas", "_card", "_grid", "_scrollbox", "_panel" };

    /// <summary>The words a content container's name gives to compare with a menu word (<c>planning_perf_calc_canvas</c> →
    /// <c>PERFCALC</c>, folded); empty when the name is no container's.</summary>
    internal static string ContainerWords(string name)
    {
        foreach (var s in ContainerSuffixes)
        {
            if (!name.EndsWith(s, StringComparison.Ordinal)) continue;
            string stem = name[..^s.Length];
            if (stem.StartsWith("planning_", StringComparison.Ordinal)) stem = stem["planning_".Length..];
            return Fold(stem);
        }
        return "";
    }

    /// <summary>Whether a control name is a tab row's button: BlueBird's <c>_tab_button</c> (Maintenance), <c>_tab_button_N</c>,
    /// <c>_tab_N</c> (Failures), <c>_source_button_N</c> and <c>_type_button_N</c> (Charts), <c>_selector_button_N</c> (Weather). PERF calc's <c>_unit_button_N</c> is
    /// not one: they are three independent switches.</summary>
    internal static bool IsTabName(string name)
    {
        if (name.EndsWith("_tab_button", StringComparison.Ordinal)) return true;
        int us = name.LastIndexOf('_');
        if (us < 0 || us == name.Length - 1 || !name[(us + 1)..].All(char.IsAsciiDigit)) return false;
        string head = name[..us];
        return head.EndsWith("_tab_button", StringComparison.Ordinal) || head.EndsWith("_tab", StringComparison.Ordinal)
            || head.EndsWith("_source_button", StringComparison.Ordinal) || head.EndsWith("_type_button", StringComparison.Ordinal)
            || head.EndsWith("_selector_button", StringComparison.Ordinal);
    }

    /// <summary>The state a picture shows, from its file's name (the part after the last <c>/</c>), as the tablet's tiles use pictures
    /// for what their words do not say: the chocks tile's picture is <c>bbs_ldg_gr_chocks.png</c> with the chocks in place and
    /// <c>bbs_ldg_gr.png</c> with them removed (measured, <c>docs/b757/evidence/efb-memory-pages.md</c>). Null for any other picture.</summary>
    internal static string? PictureState(string text)
    {
        string file = text[(Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\')) + 1)..];
        return file.ToLowerInvariant() switch
        {
            "bbs_ldg_gr_chocks.png" => "IN PLACE",
            "bbs_ldg_gr.png" => "REMOVED",
            _ => null,
        };
    }

    /// <summary>What a name's role words make of its control, among the labels one line is read from.</summary>
    internal enum NameRole { None, Label, Value, Unit }

    /// <summary>A control name read as the page grammar reads it (plan 20, R3 and R4): its role, the parts left once the role words
    /// are taken out (a label and the value it pairs with have the same core), and whether it is a card's title.</summary>
    internal sealed record NameInfo(NameRole Role, IReadOnlyCollection<string> Core, bool IsTitle)
    {
        /// <summary>The core as one string, parts in a fixed order: how two names with the same core are matched.</summary>
        internal string CoreKey => string.Join(' ', Core.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>The headings BlueBird gives no title name: Cockpit presets' section labels, which the page draws as <c>*_label</c>
    /// (the exceptions table of the page grammar, plan 20 D5). Per app, the control names.</summary>
    private static readonly (string App, string Name)[] HeadingNames =
    {
        ("AIRCRAFT", "presets_label"), ("AIRCRAFT", "quick_start_label"), ("AIRCRAFT", "circuits_state_label"),
        ("AIRCRAFT", "ground_label"), ("AIRCRAFT", "inflight_label"), ("AIRCRAFT", "custom_label"),
    };

    /// <summary>Whether the table names the control as a heading of its app's page.</summary>
    internal static bool IsHeadingName(string app, string name) => HeadingNames.Contains((app, name));

    private static readonly HashSet<string> LabelWords = new(StringComparer.Ordinal) { "label", "title", "name", "lbl", "len", "heading" };
    private static readonly HashSet<string> ValueWords = new(StringComparer.Ordinal) { "value", "val", "saved" };

    /// <summary>A control name split on <c>_</c> and spaces, lower-case invariant.</summary>
    internal static string[] NameParts(string name) =>
        name.Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.ToLowerInvariant()).ToArray();

    /// <summary>
    /// The role of a name by its last role word: <c>label</c>, <c>title</c>, <c>name</c>, <c>lbl</c>, <c>len</c> make a label,
    /// <c>heading</c> (BlueBird's Callouts headings) makes a label, <c>value</c>, <c>val</c>, <c>saved</c> a value, and <c>value_label</c> or <c>saved_label</c> is a value (the value's own
    /// label), while <c>length_value_title</c> is a title. A name ending in <c>unit</c> is a unit. A title is a label whose name ends
    /// in <c>title</c>, <c>heading</c> or <c>title_label</c>. A name with no role word is none: a bare number or word.
    /// </summary>
    internal static NameInfo ReadName(string name)
    {
        var parts = NameParts(name);
        bool IsRole(string p) => LabelWords.Contains(p) || ValueWords.Contains(p);
        var core = parts.Where(p => !IsRole(p)).ToList();
        if (parts.Length > 1 && parts[^1] == "unit")
            return new NameInfo(NameRole.Unit, core.Where(p => p != "unit").ToList(), false);
        var roleAt = Enumerable.Range(0, parts.Length).Where(i => IsRole(parts[i])).ToList();
        if (roleAt.Count == 0) return new NameInfo(NameRole.None, parts, false);
        int last = roleAt[^1];
        bool value = ValueWords.Contains(parts[last])
            || (parts[last] == "label" && roleAt.Count >= 2 && roleAt[^2] == last - 1 && ValueWords.Contains(parts[last - 1]));
        if (value) return new NameInfo(NameRole.Value, core, false);
        bool title = parts[^1] is "title" or "heading" || (parts[^1] == "label" && parts.Length >= 2 && parts[^2] == "title");
        return new NameInfo(NameRole.Label, core, title);
    }

    /// <summary>A value's letters and digits, upper-cased invariantly: how names and words are compared.</summary>
    internal static string Fold(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));
}
