using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The EFB agent never says a stored logon code: a password field, enabled or not, only says whether one is set.</summary>
public class A330EfbAgentPasswordTests
{
    private static string FieldRowBuilder()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-a330-efb-agent.js"));
        int start = agent.IndexOf("A.controlFor = function", StringComparison.Ordinal);
        int end = agent.IndexOf("A.rangeName = function", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "the agent's field-row builder was not found");
        return agent.Substring(start, end - start);
    }

    [Fact]
    public void A_password_field_is_masked_before_the_disabled_or_read_only_read_out()
    {
        string body = FieldRowBuilder();
        int password = body.IndexOf("ty === 'password'", StringComparison.Ordinal);
        int readout = body.IndexOf("el.disabled || el.readOnly", StringComparison.Ordinal);
        Assert.True(password >= 0 && readout >= 0, "the password branch and the read-out branch must both exist");
        Assert.True(password < readout,
            "a disabled or read-only password field reaches A.readout, which speaks el.value");
    }

    [Fact]
    public void The_password_branch_never_puts_the_value_in_the_row()
    {
        string body = FieldRowBuilder();
        int at = body.IndexOf("ty === 'password'", StringComparison.Ordinal);
        Assert.True(at >= 0);
        int close = body.IndexOf("return A.el(", at, StringComparison.Ordinal);
        int end = body.IndexOf("});", close, StringComparison.Ordinal);
        string branch = body.Substring(at, end - at);
        Assert.DoesNotContain("A.readout", branch);
        // el.value is read only to say set / not set, never as the row's text or value
        string withoutTest = branch.Replace("(el.value ? ', set' : ', not set')", "");
        Assert.DoesNotContain("el.value", withoutTest);
        Assert.Contains("value: ''", branch);
    }
}
