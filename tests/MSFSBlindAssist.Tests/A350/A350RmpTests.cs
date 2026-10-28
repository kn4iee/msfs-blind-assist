using MSFSBlindAssist.Aircraft.A350;

namespace MSFSBlindAssist.Tests.A350;

// Tuning on an RMP (manual p. 75, live 2026-10-28): the page key, the frequency typed on the keypad (the leading 1 and
// the point optional: 2 1 9 is 121.900), the line select key loads it as standby, the activation key swaps it in.
// Each key is one Set of 1 on its input event; the window sends them through the calculator, one unique string each.
public class A350RmpTests
{
    [Theory]
    [InlineData('0', "0")]
    [InlineData('9', "9")]
    [InlineData('.', "PERIOD")]
    public void A_typed_character_is_a_keypad_key(char c, string key)
    {
        Assert.Equal(key, A350Rmp.KeyFor(c));
    }

    [Fact]
    public void A_character_with_no_key_has_none()
    {
        Assert.Null(A350Rmp.KeyFor('A'));
        Assert.Null(A350Rmp.KeyFor(' '));
    }

    [Fact]
    public void A_key_is_pressed_on_its_own_rmp()
    {
        Assert.Equal("1 (>B:AIRLINER_RMP_LSK1_1_Set)", A350Rmp.Press(1, "LSK1"));
        Assert.Equal("1 (>B:AIRLINER_RMP_9_3_Set)", A350Rmp.Press(3, "9"));
        Assert.Equal("1 (>B:AIRLINER_RMP_ADK2_2_Set)", A350Rmp.Press(2, A350Rmp.ActivationKey(2)));
        Assert.Equal("LSK3", A350Rmp.LineSelectKey(3));
    }

    [Fact]
    public void Every_page_key_is_an_rmp_key()
    {
        Assert.Equal(new[] { "VHF", "HF", "TEL", "SQWK", "MENU", "NAV" }, A350Rmp.Pages);
    }

    // A squawk is set with the stock XPNDR_SET (BCD), as the A380 window sets it: the A350 keeps TRANSPONDER CODE:1 and
    // its RMPs in step (live: 8192 put SQUAWK : 2000 on RMP 1), whatever the keypad's entry state.
    [Theory]
    [InlineData("2000", 0x2000)]
    [InlineData("7700", 0x7700)]
    [InlineData("1234", 0x1234)]
    public void A_squawk_is_four_octal_digits_in_bcd(string code, int bcd)
    {
        Assert.Equal(bcd, A350Rmp.SquawkBcd(code));
    }

    [Theory]
    [InlineData("2080")]   // 8 is no transponder digit
    [InlineData("200")]
    [InlineData("20000")]
    [InlineData("2a00")]
    public void An_invalid_squawk_has_no_code(string code)
    {
        Assert.Null(A350Rmp.SquawkBcd(code));
    }

    [Fact]
    public void Every_alt_letter_page_key_of_the_window_is_an_rmp_page_key()
    {
        Assert.All(MSFSBlindAssist.Forms.A350.A350RmpWindow.PageKeys.Values, k => Assert.Contains(k, A350Rmp.Pages));
        Assert.Equal("VHF", MSFSBlindAssist.Forms.A350.A350RmpWindow.PageKeys[System.Windows.Forms.Keys.V]);   // the A380's letter
        Assert.DoesNotContain(System.Windows.Forms.Keys.T, MSFSBlindAssist.Forms.A350.A350RmpWindow.PageKeys.Keys);   // T is the squawk
    }

    [Fact]
    public void Each_rmp_draws_on_its_own_gauge()
    {
        Assert.Equal(("RMP", 0), A350Rmp.Gauge(1));
        Assert.Equal(("RMP", 2), A350Rmp.Gauge(3));
    }
}
