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

    /// <summary>A value's letters and digits, upper-cased invariantly: how names and words are compared.</summary>
    internal static string Fold(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant));
}
