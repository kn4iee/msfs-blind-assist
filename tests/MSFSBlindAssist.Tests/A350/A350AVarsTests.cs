using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A350.Display;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A350;

// The A350's draw code asks the sim for a few A: vars itself (live, every gauge drawn: engine N2 on the CTR,
// the three baro settings on the EFIS and ISIS). The copy cannot hold them, so MSFSBA streams each as a stock
// variable and answers from its cache; until 2026-10-27 they all answered 0.
public class A350AVarsTests
{
    [Theory]
    [InlineData("TURB ENG N2:1|Percent", "TURB ENG N2:1", "Percent")]
    [InlineData("TURB ENG N2:2|Percent", "TURB ENG N2:2", "Percent")]
    [InlineData("KOHLSMAN SETTING HG:1|inches of mercury", "KOHLSMAN SETTING HG:1", "inches of mercury")]
    [InlineData("KOHLSMAN SETTING HG:3|inches of mercury", "KOHLSMAN SETTING HG:3", "inches of mercury")]
    [InlineData("PLANE LATITUDE:0|Degrees", "PLANE LATITUDE", "Degrees")]     // asked in flight; index 0 is none
    [InlineData("PLANE LONGITUDE:0|Degrees", "PLANE LONGITUDE", "Degrees")]
    // The SD's F/CTL rudder index (live 2026-10-28): unanswered, the rudder read "centred" at full deflection.
    [InlineData("RUDDER POSITION:0|POSITION", "RUDDER POSITION", "POSITION")]
    // The ND's (live 2026-10-30, every display drawn on the stand): unanswered, they read 0.
    [InlineData("TOTAL WEIGHT:0|kg", "TOTAL WEIGHT", "kg")]
    [InlineData("AMBIENT PRESSURE:0|hectopascal", "AMBIENT PRESSURE", "hectopascal")]
    public void A_draw_code_ask_maps_to_a_stock_variable(string ask, string name, string units)
    {
        var v = Assert.Single(A350AVars.All, a => a.Ask == ask);
        Assert.Equal(name, v.Name);
        Assert.Equal(units, v.Units);
        Assert.True(A350AVars.TryKey(ask, out var key));
        Assert.Equal(v.Key, key);
    }

    [Fact]
    public void An_unknown_ask_has_no_key()
    {
        Assert.False(A350AVars.TryKey("AIRSPEED INDICATED:1|knots", out _));
    }

    [Fact]
    public void An_ask_is_answered_from_the_cache_and_an_unknown_one_reported_once()
    {
        var cache = new Dictionary<string, double> { ["A350_AVAR_TURB_ENG_N2_2"] = 64.4 };
        var unknown = new List<string>();
        var answers = new A350AVarAnswers(k => cache.TryGetValue(k, out var v) ? v : null, unknown.Add);
        Assert.Equal(64.4, answers.Answer("TURB ENG N2:2|Percent"));
        Assert.Null(answers.Answer("TURB ENG N2:1|Percent"));          // streamed but not yet read
        Assert.Null(answers.Answer("AIRSPEED INDICATED:1|knots"));
        Assert.Null(answers.Answer("AIRSPEED INDICATED:1|knots"));
        Assert.Equal(new[] { "AIRSPEED INDICATED:1|knots" }, unknown);
    }

    // The EFIS asks the first officer's baro in millibars too (live, in flight): answered from the inches.
    [Fact]
    public void A_baro_asked_in_millibars_is_converted_from_the_inches()
    {
        var cache = new Dictionary<string, double> { ["A350_AVAR_KOHLSMAN_SETTING_HG_2"] = 29.92 };
        var unknown = new List<string>();
        var answers = new A350AVarAnswers(k => cache.TryGetValue(k, out var v) ? v : null, unknown.Add);
        Assert.Equal(1013.2, answers.Answer("KOHLSMAN SETTING HG:2|millibar")!.Value, 1);
        Assert.Null(answers.Answer("KOHLSMAN SETTING HG:1|millibar"));   // inches not yet read
        Assert.Empty(unknown);
    }

    // The EFIS also runs calculator code for three idents (live, in flight): answered as strings from the NAV
    // radio read; an ident not yet read, or a code not listed, answers nothing.
    [Fact]
    public void A_calculator_ident_ask_is_answered_as_a_string()
    {
        var idents = new A350CalcStrings();
        Assert.Null(idents.Answer("(A:NAV IDENT:2, String)"));
        idents.Update(nav1: "KAT", nav2: "IKW", adf1: "CB", adf2: "");
        Assert.Equal("IKW", idents.Answer("(A:NAV IDENT:2, String)"));
        Assert.Equal("KAT", idents.Answer("(A:NAV IDENT:1, String)"));
        Assert.Equal("CB", idents.Answer("(A:ADF IDENT:1, String)"));
        Assert.Equal("", idents.Answer("(A:ADF IDENT:2, String)"));
        Assert.Null(idents.Answer("(A:ATC ID, String)"));
    }

    [Fact]
    public void The_definition_streams_each_one_silently()
    {
        var vars = new IniBuildsA350Definition().GetVariables();
        foreach (var a in A350AVars.All)
        {
            var def = vars[a.Key];
            Assert.Equal(a.Name, def.Name);
            Assert.Equal(SimVarType.SimVar, def.Type);
            Assert.Equal(a.Units, def.Units);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.ExcludeFromMonitorManager);   // consumed silently: no Ctrl+M row ([VAR-9])
        }
    }
}
