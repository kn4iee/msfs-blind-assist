namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>An equipment option the cockpit shows its controls for only when fitted (OIS OPTIONS page).</summary>
/// <param name="Var">The option's L:var, 1 when fitted.</param>
/// <param name="Name">What it is, as a sentence starts it ("Fuel jettison").</param>
/// <param name="ControlIds">The map controls the cockpit hides without it.</param>
/// <param name="Plural">Whether <paramref name="Name"/> takes "are" ("Brake fans").</param>
public sealed record A350Option(string Var, string Name, IReadOnlyList<string> ControlIds, bool Plural = false)
{
    /// <summary>Its MSFSBA variable key, the status line on its controls' panel.</summary>
    public string Key => "A350_OPT_" + Var;

    /// <summary>Said instead of writing to one of its controls while it is not fitted.</summary>
    public string NotFitted => Plural
        ? $"{Name} are not fitted. They are an option on the OIS options page."
        : $"{Name} is not fitted. It is an option on the OIS options page.";
}

/// <summary>
/// The options whose controls the cockpit model hides when they are not fitted (their visibility conditions in
/// <c>A350_Interior.behavior.xml</c>). Without the option the aircraft also clears the controls' variables every frame
/// (fuel jettison: <c>UpdateFuelJettison</c>), so a press would only ever "not move". Live 2026-10-26: with
/// <c>INI_OPTION_FUEL_JETTISON</c> 1, ARM and ACTIVE work and the jettison valve opens.
/// </summary>
public static class A350Options
{
    public static readonly IReadOnlyList<A350Option> All = new[]
    {
        new A350Option("INI_OPTION_FUEL_JETTISON", "Fuel jettison", new[] { "AIRLINER_JETTISON_ARM", "AIRLINER_JETTISON_ACTIVE" }),
        new A350Option("INI_OPTION_BRAKE_FANS", "Brake fans", new[] { "AIRLINER_LDG_BRK_HOT" }, Plural: true),
        // Without it the cockpit shows no first officer's ISIS (SKEL_INT_OPTION_FO_ISIS_VISIBILITY_ON/OFF).
        new A350Option("INI_OPTION_FO_ISIS", "First officer ISIS", new[]
        {
            "AIRLINER_ISIS_KNOB_BUTTON_2", "AIRLINER_ISIS_LS_2", "AIRLINER_ISIS_MENU_2", "AIRLINER_ISIS_MINUS_2",
            "AIRLINER_ISIS_MODE_2", "AIRLINER_ISIS_PLUS_2", "AIRLINER_ISIS_KNOB_2",
        }),
        // The HUD (the OIS option writes INI_IS_HUD): it picks the cockpit's HUD skeleton (SKEL_INT_COCKPIT_VISIBILITY_
        // ULR_VIS on (L:INI_IS_HUD) 1 ==, NONULR on 0), the one holding both HUD units, their covers and handles, and
        // the glareshield's HUD keys.
        new A350Option("INI_IS_HUD", "HUD", new[]
        {
            "AIRLINER_FCU_HUD_BRIGHTNESS_LEFT", "AIRLINER_FCU_HUD_BRIGHTNESS_RIGHT", "AIRLINER_FCU_HUD_XWIND_LEFT",
            "AIRLINER_FCU_HUD_XWIND_RIGHT", "INSTRUMENT_FCU_HUD_DECLUTTER_LEFT_IE_ID", "INSTRUMENT_FCU_HUD_DECLUTTER_RIGHT_IE_ID",
            "AIRLINER_HUD_LEFT", "AIRLINER_HUD_RIGHT", "AIRLINER_HUD_HANDLE_LEFT", "AIRLINER_HUD_HANDLE_RIGHT",
        }),
    };

    private static readonly Dictionary<string, A350Option> ByControl =
        All.SelectMany(o => o.ControlIds.Select(id => (id, o))).ToDictionary(p => p.id, p => p.o, StringComparer.Ordinal);

    /// <summary>The option a control needs, or null.</summary>
    public static A350Option? For(string controlId) => ByControl.GetValueOrDefault(controlId);
}
