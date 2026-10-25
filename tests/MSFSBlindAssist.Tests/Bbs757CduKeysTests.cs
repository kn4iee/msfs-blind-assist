using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757 CDU's keys. A name the cockpit does not have would be refused by the tablet
/// agent's whitelist ("bad-key") and the pilot's key would silently do nothing, so every name the
/// CDU window can send is checked against the agent's own list, read from the shipped script.
/// </summary>
public class Bbs757CduKeysTests
{
    private static IReadOnlyList<string> AgentKeyNames()
    {
        string agent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Resources", "coherent-bbs757-tablet-agent.js"));
        var list = Regex.Match(agent, @"var KEY_NAMES = \[(.*?)\];", RegexOptions.Singleline);
        Assert.True(list.Success, "the agent's KEY_NAMES list was not found");
        return Regex.Matches(list.Groups[1].Value, "'([A-Z0-9]+)'").Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void The_cockpit_has_69_keys_per_cdu_and_the_agent_whitelists_exactly_them()
    {
        var agent = AgentKeyNames();

        Assert.Equal(69, Bbs757CduKeys.All.Count);
        Assert.Equal(69, Bbs757CduKeys.All.Distinct().Count());
        Assert.Equal(Bbs757CduKeys.All.OrderBy(k => k, StringComparer.Ordinal), agent.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData('a', "KEYA")]
    [InlineData('K', "KEYK")]
    [InlineData('z', "KEYZ")]
    [InlineData('0', "NUM0")]
    [InlineData('9', "NUM9")]
    [InlineData('.', "NUMPER")]
    [InlineData(' ', "KEYSP")]
    [InlineData('/', "KEYSLASH")]
    [InlineData('-', "NUMPLUS")]
    [InlineData('+', "NUMPLUS NUMPLUS")]   // the Boeing +/- key: "-" first, a second press makes it "+"
    public void A_typed_character_takes_its_cdu_keys(char c, string keys)
    {
        Assert.Equal(keys.Split(' '), Bbs757CduKeys.ForChar(c));
    }

    [Theory]
    [InlineData('*')]
    [InlineData('_')]
    [InlineData(',')]
    [InlineData('é')]   // é
    [InlineData('\n')]
    public void A_character_with_no_cdu_key_takes_none(char c)
    {
        Assert.Empty(Bbs757CduKeys.ForChar(c));
    }

    [Theory]
    [InlineData(Keys.I, "INIT")]
    [InlineData(Keys.R, "RTE")]
    [InlineData(Keys.D, "DEPARR")]
    [InlineData(Keys.T, "ATC")]
    [InlineData(Keys.V, "VNAV")]
    [InlineData(Keys.F, "FIX")]
    [InlineData(Keys.G, "LEGS")]
    [InlineData(Keys.H, "HOLD")]
    [InlineData(Keys.O, "FMCCOMM")]
    [InlineData(Keys.P, "PROG")]
    [InlineData(Keys.M, "MENU")]
    [InlineData(Keys.N, "NAVRAD")]
    [InlineData(Keys.E, "EXEC")]
    [InlineData(Keys.S, null)]      // Alt+S focuses the scratchpad box
    [InlineData(Keys.A, null)]      // the HS787's ALTN: the 757 has no such key
    [InlineData(Keys.C, null)]      // the HS787's long CLR: never sent on the 757
    [InlineData(Keys.Home, null)]
    public void Alt_letters_press_the_hs787_windows_page_keys(Keys key, string? expected)
    {
        Assert.Equal(expected, Bbs757CduKeys.ForAltLetter(key));
    }

    [Fact]
    public void Line_select_keys_are_L1_to_L6_and_R1_to_R6()
    {
        Assert.Equal("L1", Bbs757CduKeys.Lsk(right: false, 1));
        Assert.Equal("R6", Bbs757CduKeys.Lsk(right: true, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757CduKeys.Lsk(right: false, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757CduKeys.Lsk(right: true, 7));
    }

    [Fact]
    public void Every_key_the_cdu_window_can_send_is_in_the_agents_whitelist()
    {
        var agent = AgentKeyNames().ToHashSet(StringComparer.Ordinal);
        var sendable = new List<string>();

        // The named keys (buttons, PREV/NEXT, EXEC, CLR, DEL) are the class's constants.
        sendable.AddRange(typeof(Bbs757CduKeys).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!));
        for (int n = 1; n <= 6; n++)
        {
            sendable.Add(Bbs757CduKeys.Lsk(right: false, n));
            sendable.Add(Bbs757CduKeys.Lsk(right: true, n));
        }
        for (int c = 0; c <= 0x2FF; c++) sendable.AddRange(Bbs757CduKeys.ForChar((char)c));
        foreach (Keys k in Enum.GetValues<Keys>())
            if (Bbs757CduKeys.ForAltLetter(k) is { } page) sendable.Add(page);

        Assert.Empty(sendable.Where(k => !agent.Contains(k)).Distinct());
        Assert.All(sendable, k => Assert.True(Bbs757CduKeys.IsKey(k), k));
    }
}
