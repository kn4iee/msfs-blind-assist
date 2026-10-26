namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>An equipment option the cockpit shows its controls for only when fitted (OIS OPTIONS page).</summary>
/// <param name="Var">The option's L:var, 1 when fitted.</param>
/// <param name="Name">What it is, as a sentence starts it ("Fuel jettison").</param>
/// <param name="ControlIds">The map controls the cockpit hides without it.</param>
public sealed record A350Option(string Var, string Name, IReadOnlyList<string> ControlIds)
{
    /// <summary>Its MSFSBA variable key, the status line on its controls' panel.</summary>
    public string Key => "A350_OPT_" + Var;

    /// <summary>Said instead of writing to one of its controls while it is not fitted.</summary>
    public string NotFitted => $"{Name} is not fitted. It is an option on the OIS options page.";
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
    };

    private static readonly Dictionary<string, A350Option> ByControl =
        All.SelectMany(o => o.ControlIds.Select(id => (id, o))).ToDictionary(p => p.id, p => p.o, StringComparer.Ordinal);

    /// <summary>The option a control needs, or null.</summary>
    public static A350Option? For(string controlId) => ByControl.GetValueOrDefault(controlId);
}
