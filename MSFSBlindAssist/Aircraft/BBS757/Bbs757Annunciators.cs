namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One of the 757's announced lights or states: the variable key the app reads it under, BlueBird's L:var,
/// the Ctrl+M label (also its status row's name), the phrase a value speaks (null: silent; the second
/// argument is a related row's last delivered value, null when there is none), the map panel whose status
/// display shows it (null: on no display), and the words that display shows per value.
/// </summary>
public sealed record Bbs757AnnunciatorRow(string Key, string LVar, string Label, Func<double, double?, string?> Phrase,
    string? Panel, IReadOnlyDictionary<double, string>? States = null);

/// <summary>
/// The 757's announced lights and states (plan 06): BlueBird's published L:vars, each spoken on change
/// through the definition's <see cref="Bbs757ChangeTracker"/> (the cockpit as found is a silent baseline).
/// Pure: the table and the words.
/// </summary>
public static class Bbs757Annunciators
{
    /// <summary>The EXT PWR button's map node, whose press reads back <see cref="Bbs757SharedKeys.ExtPwrOn"/>.</summary>
    public const string ExtPwrButtonNode = "B757_OVHD_EXT_PWR_BUTTON";

    /// <summary>The map's panel with the EXT PWR button.</summary>
    public const string ElectricalPanel = "Electrical";

    public static IReadOnlyList<Bbs757AnnunciatorRow> Rows { get; } = new[]
    {
        // The press's read-back and the row's change say the same words: the read-back rebaselines the row
        // with LinkedPhrase, so a different phrase would announce the press a second time.
        new Bbs757AnnunciatorRow(Bbs757SharedKeys.ExtPwrOn, "B757_OVHD_EXT_PWR_BUTTON_ON", "External Power On",
            (v, _) => OnOff(v, "EXT PWR on", "EXT PWR off"), ElectricalPanel, States("Off", "On")),
        new Bbs757AnnunciatorRow(Bbs757SharedKeys.ExtPwrAvail, "B757_OVHD_APUGENEXTPWR_R_AVAIL_DECAL", "External Power Available",
            (v, _) => OnOff(v, "External power available", "External power not available"), ElectricalPanel,
            States("Not available", "Available")),
    };

    private static readonly Dictionary<string, Bbs757AnnunciatorRow> ByKey = Rows.ToDictionary(r => r.Key, StringComparer.Ordinal);

    /// <summary>The row read under this variable key, or null.</summary>
    public static Bbs757AnnunciatorRow? ForKey(string key) => ByKey.GetValueOrDefault(key);

    /// <summary>The EXT PWR read-back: "EXT PWR on", "EXT PWR off", or "EXT PWR unavailable" when it was not read.</summary>
    public static string ExtPwrPhrase(double? value)
        => value is double v && ByKey[Bbs757SharedKeys.ExtPwrOn].Phrase(v, null) is { } phrase ? phrase : "EXT PWR unavailable";

    /// <summary>A light's words: above 0.5 is lit; null when the value is not a number.</summary>
    private static string? OnOff(double value, string lit, string dark)
        => double.IsFinite(value) ? (value > 0.5 ? lit : dark) : null;

    private static IReadOnlyDictionary<double, string> States(string dark, string lit)
        => new Dictionary<double, string> { [0] = dark, [1] = lit };
}
