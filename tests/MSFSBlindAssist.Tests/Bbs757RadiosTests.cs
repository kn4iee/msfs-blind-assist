using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757's COM radios (plan 04, task 2), as far as they run without a sim: the typed
/// standby frequency (25 and 8.33 kHz channels, 118.000 to 136.990), the stock-event code that sets
/// it (<c>K:COM_STBY_RADIO_SET_HZ</c>, Hz as an integer), the transfer's panel lookup and the phrases
/// ("COM1 active 124.850"). BlueBird's VHF L, C and R are the simulator's COM 1, 2 and 3 (measured).
/// CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch a culture leak.
/// </summary>
public class Bbs757RadiosTests
{
    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private const string RangeMessage = "Frequency must be 118.000 to 136.990, on a 25 or 8.33 kilohertz channel.";

    // ---- The typed frequency ---------------------------------------------------------------------

    [Theory]
    [InlineData("121.5", 121.5)]
    [InlineData("121.50", 121.5)]
    [InlineData("121.500", 121.5)]
    [InlineData("121", 121.0)]
    [InlineData("118", 118.0)]
    [InlineData("118.000", 118.0)]
    [InlineData("136.990", 136.99)]
    [InlineData("136.99", 136.99)]
    [InlineData("124.85", 124.85)]
    [InlineData("123.45", 123.45)]
    [InlineData("121.57", 121.575)]    // two decimals ending 7 get a trailing 5: 8.33 kHz channel 121.575
    [InlineData("121.52", 121.525)]    // ... and ending 2
    [InlineData("121.575", 121.575)]
    [InlineData("121.525", 121.525)]
    [InlineData("118.27", 118.275)]
    [InlineData("118.005", 118.005)]   // an 8.33 kHz channel typed in full
    [InlineData("120.905", 120.905)]   // measured: the module's knob stepped 120.900 to 120.905
    [InlineData("121.510", 121.51)]
    [InlineData("121.5004", 121.5)]    // rounded to 1 kHz
    [InlineData(" 124.85 ", 124.85)]   // trimmed
    [InlineData("121,5", 121.5)]       // a comma is the decimal mark whatever the PC's culture
    [InlineData("121,57", 121.575)]
    public void Parses_channels(string typed, double mhz)
    {
        Assert.True(Bbs757Radios.TryParseCom(typed, out double parsed, out string error));
        Assert.Equal(mhz, parsed);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("121.520")]     // kHz part 520: not a 25 or 8.33 kHz channel
    [InlineData("121.521")]
    [InlineData("121.512")]
    [InlineData("121.5.3")]
    [InlineData("117.995")]     // below the band
    [InlineData("117.990")]     // a channel, but below 118.000
    [InlineData("117")]
    [InlineData("136.995")]     // above 136.990
    [InlineData("137.000")]
    [InlineData("137")]
    [InlineData("1215")]
    [InlineData("99999999999999999999")]
    [InlineData("0")]
    [InlineData("-121.5")]
    [InlineData("+121.5")]
    [InlineData("abc")]
    [InlineData("121.5 MHz")]
    [InlineData("1.2e2")]       // a number, but not what a pilot types as a frequency
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1,215.0")]
    public void Refuses_off_channel_and_range(string typed)
    {
        Assert.False(Bbs757Radios.TryParseCom(typed, out double mhz, out string error));
        Assert.Equal(0, mhz);
        Assert.Equal(RangeMessage, error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_box_says_to_type_a_frequency_first(string? typed)
    {
        Assert.False(Bbs757Radios.TryParseCom(typed, out double mhz, out string error));
        Assert.Equal(0, mhz);
        Assert.Equal("Type a frequency first.", error);
    }

    [Fact]
    public void Every_channel_of_the_band_is_accepted_and_every_other_kilohertz_refused()
    {
        int accepted = 0;
        for (int khz = 118000; khz <= 136990; khz++)
        {
            string typed = $"{khz / 1000}.{khz % 1000:000}";
            bool onChannel = (khz % 1000 % 25) is 0 or 5 or 10 or 15;
            Assert.Equal(onChannel, Bbs757Radios.TryParseCom(typed, out double mhz, out _));
            if (!onChannel) continue;
            accepted++;
            Assert.Equal(khz / 1000.0, mhz);
        }
        Assert.Equal(19 * 160, accepted);   // 19 MHz (118 to 136), 4 channels in every 25 kHz
    }

    // ---- Culture ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void De_DE(string culture)
    {
        // CI runs under en-US: here "121,5" must still be 121.500, "121.57" 121.575, and the event's Hz
        // and the spoken frequency must carry no comma.
        InCulture(culture, () =>
        {
            Assert.True(Bbs757Radios.TryParseCom("121,5", out double a, out _));
            Assert.Equal(121.5, a);
            Assert.True(Bbs757Radios.TryParseCom("121,57", out double b, out _));
            Assert.Equal(121.575, b);
            Assert.True(Bbs757Radios.TryParseCom("121.57", out double c, out _));
            Assert.Equal(121.575, c);
            Assert.True(Bbs757Radios.TryParseCom("121.5", out double d, out _));
            Assert.Equal(121.5, d);
            Assert.True(Bbs757Radios.TryParseCom("120,905", out double e, out _));
            Assert.Equal(120.905, e);

            Assert.False(Bbs757Radios.TryParseCom("121,520", out _, out string error));
            Assert.Equal(RangeMessage, error);
            Assert.False(Bbs757Radios.TryParseCom("121.520", out _, out error));
            Assert.Equal(RangeMessage, error);

            Assert.Equal("121575000 (>K:COM_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(1, b));
            Assert.Equal("121500000 (>K:COM2_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(2, a));
            Assert.Equal("124.850", Bbs757Radios.Frequency(124.85));
            Assert.Equal("COM1 active 124.850", Bbs757Radios.ActivePhrase(1, 124.85));
            Assert.Equal("COM3 standby 121.575", Bbs757Radios.StandbyPhrase(3, b));
        });
    }

    // ---- Keys and stock-event code ----------------------------------------------------------------

    [Theory]
    [InlineData(1, "BBS757_COM1_ACTIVE", "BBS757_COM1_STANDBY", "BBS757_COM1_SET", "BBS757_COM1_TRANSFER")]
    [InlineData(2, "BBS757_COM2_ACTIVE", "BBS757_COM2_STANDBY", "BBS757_COM2_SET", "BBS757_COM2_TRANSFER")]
    [InlineData(3, "BBS757_COM3_ACTIVE", "BBS757_COM3_STANDBY", "BBS757_COM3_SET", "BBS757_COM3_TRANSFER")]
    public void The_variable_keys_carry_the_radio_number(int com, string active, string standby, string set, string transfer)
    {
        Assert.Equal(active, Bbs757Radios.ActiveKey(com));
        Assert.Equal(standby, Bbs757Radios.StandbyKey(com));
        Assert.Equal(set, Bbs757Radios.SetKey(com));
        Assert.Equal(transfer, Bbs757Radios.TransferKey(com));
    }

    [Theory]
    [InlineData(1, 121.5, "121500000 (>K:COM_STBY_RADIO_SET_HZ)")]   // COM 1's events carry no digit
    [InlineData(2, 118.275, "118275000 (>K:COM2_STBY_RADIO_SET_HZ)")]
    [InlineData(3, 136.99, "136990000 (>K:COM3_STBY_RADIO_SET_HZ)")]
    [InlineData(1, 121.575, "121575000 (>K:COM_STBY_RADIO_SET_HZ)")]   // no floating-point noise in the Hz
    [InlineData(1, 120.905, "120905000 (>K:COM_STBY_RADIO_SET_HZ)")]
    [InlineData(2, 118.005, "118005000 (>K:COM2_STBY_RADIO_SET_HZ)")]
    public void The_standby_set_is_the_stock_event_with_the_hertz_as_an_integer(int com, double mhz, string code)
    {
        Assert.Equal(code, Bbs757Radios.StandbySetCode(com, mhz));
    }

    [Fact]
    public void The_standby_code_carries_what_the_parser_returned_for_every_channel()
    {
        // Parse, then send: the Hz must be the typed kHz times 1000 for every channel of the band.
        for (int khz = 118000; khz <= 136990; khz++)
        {
            if (khz % 1000 % 25 is not (0 or 5 or 10 or 15)) continue;
            Assert.True(Bbs757Radios.TryParseCom($"{khz / 1000}.{khz % 1000:000}", out double mhz, out _));
            Assert.Equal($"{khz * 1000L} (>K:COM_STBY_RADIO_SET_HZ)", Bbs757Radios.StandbySetCode(1, mhz));
        }
    }

    [Theory]
    [InlineData(1, "(>K:COM_STBY_RADIO_SWAP)")]
    [InlineData(2, "(>K:COM2_RADIO_SWAP)")]
    [InlineData(3, "(>K:COM3_RADIO_SWAP)")]
    public void The_swap_is_the_stock_event(int com, string code)
    {
        Assert.Equal(code, Bbs757Radios.SwapCode(com));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(-1)]
    public void A_radio_outside_1_to_3_throws_in_every_member(int com)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.ActiveKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbyKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.SetKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferKey(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbySetCode(com, 121.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.SwapCode(com));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.TransferPanelFor(com, 2, 4, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.ActivePhrase(com, 121.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => Bbs757Radios.StandbyPhrase(com, 121.5));
    }

    // ---- The tuning panel that selects a radio ------------------------------------------------------

    [Theory]
    [InlineData(1, 2.0, 4.0, 8.0, 1)]     // each panel's own radio: bit 2 VHF L, 4 VHF C, 8 VHF R
    [InlineData(2, 2.0, 4.0, 8.0, 2)]
    [InlineData(3, 2.0, 4.0, 8.0, 3)]
    [InlineData(1, 4.0, 2.0, 8.0, 2)]     // the panels' selectors moved: the radio is where the bit is
    [InlineData(2, 4.0, 2.0, 8.0, 1)]
    [InlineData(3, 8.0, 4.0, 2.0, 1)]
    [InlineData(1, 2.0, 2.0, 2.0, 1)]     // the first panel whose lights have the bit wins
    [InlineData(1, 4.0, 2.0, 2.0, 2)]
    [InlineData(1, 6.0, 4.0, 8.0, 1)]     // lights hold more than one bit
    [InlineData(2, 7.0, 4.0, 8.0, 1)]
    [InlineData(1, 3.0, 4.0, 8.0, 1)]     // ... and bits of other lights
    [InlineData(2, 3.0, 4.0, 8.0, 2)]
    public void Transfer_panel(int com, double p1, double p2, double p3, int expected)
    {
        Assert.Equal(expected, Bbs757Radios.TransferPanelFor(com, p1, p2, p3));
    }

    [Fact]
    public void No_panel_selecting_the_radio_gives_none()
    {
        Assert.Null(Bbs757Radios.TransferPanelFor(1, 4, 4, 8));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, 2, 2, 8));
        Assert.Null(Bbs757Radios.TransferPanelFor(3, 2, 4, 4));
        Assert.Null(Bbs757Radios.TransferPanelFor(1, 0, 0, 0));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, 1, 1, 1));
    }

    [Fact]
    public void An_unread_panel_is_skipped_and_none_read_gives_none()
    {
        Assert.Equal(2, Bbs757Radios.TransferPanelFor(1, null, 2, 8));
        Assert.Equal(3, Bbs757Radios.TransferPanelFor(3, null, null, 8));
        Assert.Equal(1, Bbs757Radios.TransferPanelFor(1, 2, null, null));
        Assert.Null(Bbs757Radios.TransferPanelFor(1, null, 4, 8));      // the one panel that might select it was not read
        Assert.Null(Bbs757Radios.TransferPanelFor(1, null, null, null));
        Assert.Null(Bbs757Radios.TransferPanelFor(2, null, null, null));
    }

    [Fact]
    public void A_lights_value_that_is_not_a_number_of_lights_is_skipped()
    {
        Assert.Null(Bbs757Radios.TransferPanelFor(1, double.NaN, double.PositiveInfinity, -2));
        Assert.Equal(3, Bbs757Radios.TransferPanelFor(1, double.NaN, -2, 2));
    }

    // ---- Phrases -----------------------------------------------------------------------------------

    [Fact]
    public void Phrases()
    {
        Assert.Equal("COM1 active 124.850", Bbs757Radios.ActivePhrase(1, 124.85));
        Assert.Equal("COM1 standby 121.500", Bbs757Radios.StandbyPhrase(1, 121.5));
        Assert.Equal("COM2 active 118.000", Bbs757Radios.ActivePhrase(2, 118.0));
        Assert.Equal("COM2 standby 121.500", Bbs757Radios.StandbyPhrase(2, 121.5));
        Assert.Equal("COM3 active 136.990", Bbs757Radios.ActivePhrase(3, 136.99));
        Assert.Equal("COM3 standby 120.905", Bbs757Radios.StandbyPhrase(3, 120.905));
    }

    [Theory]
    [InlineData(124.85, "124.850")]
    [InlineData(121.5, "121.500")]
    [InlineData(118.0, "118.000")]
    [InlineData(136.99, "136.990")]
    [InlineData(121.575, "121.575")]   // never rounded to 121.58
    [InlineData(120.905, "120.905")]
    public void A_frequency_has_three_decimals(double mhz, string said)
    {
        Assert.Equal(said, Bbs757Radios.Frequency(mhz));
    }
}
