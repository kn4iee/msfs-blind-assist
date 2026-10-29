using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>Tools review 2026-10-30 (critical): A350Probe's `ois` mode printed each OIS field's value, so on OPTIONS > 3RD
/// PARTY it printed the pilot's SimBrief ID, SayIntentions key and Hoppie logon to the console and %TEMP%. A developer tool
/// prints a field whose label says ID, KEY, LOGON, PASSWORD or TOKEN, or one the agent marks secret, as its length only,
/// whatever STREAMER MODE is set to ([A350-12]).</summary>
public class A350OisSecretsTests
{
    [Theory]
    [InlineData("SIMBRIEF ID")]
    [InlineData("SIMBRIEF ID, hidden, 7 characters")]
    [InlineData("SayIntentions API key")]
    [InlineData("HOPPIE LOGON CODE")]
    [InlineData("NAVIGRAPH PASSWORD")]
    [InlineData("TOKEN")]
    public void A_credential_label_prints_its_length_only(string label)
    {
        Assert.True(A350OisSecrets.IsSecretLabel(label));
        Assert.Equal("<hidden, 7 chars>", A350OisSecrets.Printable(label, "ABCDEFG", secret: false));
    }

    [Theory]
    [InlineData("WIND")]
    [InlineData("IDLE")]
    [InlineData("KEYBOARD")]
    [InlineData("VALID UNTIL")]
    public void Any_other_field_prints_its_value(string label)
    {
        Assert.False(A350OisSecrets.IsSecretLabel(label));
        Assert.Equal("230/5", A350OisSecrets.Printable(label, "230/5", secret: false));
    }

    [Fact]
    public void A_field_the_agent_marks_secret_prints_its_length_whatever_its_label()
    {
        Assert.Equal("<hidden, 0 chars>", A350OisSecrets.Printable("FIELD", null, secret: true));
        Assert.Equal("<hidden, 4 chars>", A350OisSecrets.Printable(null, "abcd", secret: true));
    }

    // The tools' pattern is the agent's own: one list of credential words, never two that drift apart.
    [Fact]
    public void The_pattern_is_the_ois_agents_secret_label()
    {
        string agent = File.ReadAllText(Path.Combine(ClaudeContextBudgetTests.RepoRoot(), "MSFSBlindAssist", "Resources",
            "coherent-a350-ois-agent.js"));
        Assert.Contains("A.SECRET_LABEL = /" + A350OisSecrets.LabelPattern + "/i;", agent);
    }
}
