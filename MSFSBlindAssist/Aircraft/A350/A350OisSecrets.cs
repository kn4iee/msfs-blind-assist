using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A350;

/// <summary>
/// What a developer tool prints for an OIS field (pure). The OIS OPTIONS > 3RD PARTY page holds the pilot's SimBrief ID,
/// SayIntentions key, Hoppie logon and Navigraph login, so a field whose label says ID, KEY, LOGON, PASSWORD or TOKEN (the
/// OIS agent's own pattern), or one the agent marks secret, prints its length only, whatever STREAMER MODE is set to: a
/// tool's console and its %TEMP% copy are read by people and agents the pilot never chose ([A350-12]; tools review
/// 2026-10-30, A350Probe's `ois` mode printed them).
/// </summary>
public static class A350OisSecrets
{
    /// <summary>The agent's <c>A.SECRET_LABEL</c>, word for word (a test keeps the two equal).</summary>
    public const string LabelPattern = @"\b(ID|KEY|LOGON|PASSWORD|TOKEN)\b";

    private static readonly Regex Label = new(LabelPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether a field's label names a credential.</summary>
    public static bool IsSecretLabel(string? label) => label != null && Label.IsMatch(label);

    /// <summary>What to print for a field's value: its length when it is secret by its label or the agent's flag.</summary>
    public static string Printable(string? label, string? value, bool secret) =>
        secret || IsSecretLabel(label) ? $"<hidden, {(value ?? "").Length} chars>" : value ?? "";
}
