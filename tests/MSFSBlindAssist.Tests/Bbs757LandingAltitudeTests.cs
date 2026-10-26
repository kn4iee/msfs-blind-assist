using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757's pressurization landing altitude, as far as it runs without a sim: the typed text (rounded
/// to the cockpit's 10 ft step, range checked after the rounding), the calculator write that sets it
/// (measured: <c>1020 (&gt;L:B757_OVHD_LDG_ELEV) 1 (&gt;L:B757_OVHD_LDG_ELEV_CHG)</c> reads 1030) and the
/// phrases. CI runs under en-US, so the de-DE and sv-SE cases are the ones that catch a culture leak:
/// de-DE's "1.030" is a thousands group and "1030,0" a decimal, and sv-SE writes its minus sign as
/// U+2212, which the RPN parser rejects.
/// </summary>
public class Bbs757LandingAltitudeTests
{
    private const string Unavailable = "Landing altitude unavailable";

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

    [Fact]
    public void The_names_and_range_are_the_measured_ones()
    {
        Assert.Equal("B757_OVHD_LDG_ELEV", Bbs757LandingAltitude.ValueVar);
        Assert.Equal("B757_OVHD_LDG_ELEV_CHG", Bbs757LandingAltitude.StepVar);
        Assert.Equal("BBS757_LDG_ALT_SET", Bbs757LandingAltitude.SetKey);
        Assert.Equal("BBS757_LDG_ALT", Bbs757LandingAltitude.DisplayKey);
        Assert.Equal(-1070, Bbs757LandingAltitude.MinFeet);
        Assert.Equal(14100, Bbs757LandingAltitude.MaxFeet);
        Assert.Equal($"Landing altitude must be from {Bbs757LandingAltitude.MinFeet} to {Bbs757LandingAltitude.MaxFeet} feet.",
            Bbs757LandingAltitude.InvalidMessage);
    }

    // ---- Typed text ---------------------------------------------------------------------------

    [Theory]
    [InlineData("1030", 1030)]
    [InlineData(" 1030 ", 1030)]
    [InlineData("+1030", 1030)]
    [InlineData("1034", 1030)]
    [InlineData("1035", 1040)]          // a half rounds away from zero
    [InlineData("645", 650)]
    [InlineData("-65", -70)]
    [InlineData("-64", -60)]
    [InlineData("1034.5", 1030)]        // nearest 10 of 1034.5 is 1030, not 1035 then 1040
    [InlineData("1034.9", 1030)]
    [InlineData("5", 10)]
    [InlineData("4", 0)]
    [InlineData("-4", 0)]
    [InlineData("-5", -10)]
    [InlineData("0", 0)]
    [InlineData("-0", 0)]
    [InlineData("14100", 14100)]
    [InlineData("14104", 14100)]        // range is checked on the rounded value: 14104 is 14100
    [InlineData("-1070", -1070)]
    [InlineData("-1074", -1070)]        // and -1074 is -1070
    [InlineData("1030.0", 1030)]
    [InlineData("1030,0", 1030)]
    public void Parses_and_rounds(string text, int expected)
    {
        Assert.True(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(expected, feet);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_empty_with_its_own_words(string? text)
    {
        Assert.False(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(0, feet);
        Assert.Equal("Type the landing altitude in feet first.", error);
    }

    [Theory]
    [InlineData("14105")]               // rounds to 14110
    [InlineData("14110")]
    [InlineData("14200")]
    [InlineData("99999")]
    [InlineData("-1075")]               // rounds to -1080
    [InlineData("-1080")]
    [InlineData("-5000")]
    [InlineData("abc")]
    [InlineData("10 30")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]               // overflows to infinity
    [InlineData("1.030")]               // a possible thousands group is refused, never guessed (CORE-17)
    [InlineData("1,030")]
    [InlineData("14.100")]
    [InlineData("1.234,5")]
    public void Refuses_out_of_range_and_unreadable_with_the_range(string text)
    {
        Assert.False(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
        Assert.Equal(0, feet);
        Assert.Equal("Landing altitude must be from -1070 to 14100 feet.", error);
    }

    [Fact]
    public void Refuses_empty_and_out_of_range_with_different_words()
    {
        Assert.False(Bbs757LandingAltitude.TryParseText("", out _, out string empty));
        Assert.False(Bbs757LandingAltitude.TryParseText("20000", out _, out string range));
        Assert.NotEqual(empty, range);
    }

    [Fact]
    public void De_DE_typing()
    {
        InCulture("de-DE", () =>
        {
            // "1030,0" is a decimal and "1030.0" a decimal point: both are the pilot's 1030 ft.
            Assert.True(Bbs757LandingAltitude.TryParseText("1030,0", out int a, out string errorA));
            Assert.Equal(1030, a);
            Assert.Equal("", errorA);

            Assert.True(Bbs757LandingAltitude.TryParseText("1030.0", out int b, out string errorB));
            Assert.Equal(1030, b);
            Assert.Equal("", errorB);

            Assert.True(Bbs757LandingAltitude.TryParseText("-70,0", out int c, out _));
            Assert.Equal(-70, c);

            Assert.True(Bbs757LandingAltitude.TryParseText("1030", out int d, out _));
            Assert.Equal(1030, d);

            // "1.030" could be a thousands group (it is how a German writes 1030): CORE-17 refuses it
            // rather than guess, and the pilot gets the range sentence, never 1.03 ft or 103000 ft.
            foreach (string grouped in new[] { "1.030", "1,030", "14.100" })
            {
                Assert.False(Bbs757LandingAltitude.TryParseText(grouped, out int feet, out string error));
                Assert.Equal(0, feet);
                Assert.Equal("Landing altitude must be from -1070 to 14100 feet.", error);
            }

            // What goes out is invariant, whatever the PC's culture.
            Assert.Equal("1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", Bbs757LandingAltitude.Write(1030));
            Assert.Equal("-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)", Bbs757LandingAltitude.Write(-60));
        });
    }

    [Theory]
    [InlineData("sv-SE", "1030,0", 1030)]
    [InlineData("sv-SE", "1030.0", 1030)]
    [InlineData("sv-SE", "-70", -70)]
    [InlineData("en-US", "1030", 1030)]
    [InlineData("en-US", "1030,0", 1030)]
    public void Typing_reads_the_same_in_any_culture(string culture, string text, int expected)
    {
        InCulture(culture, () =>
        {
            Assert.True(Bbs757LandingAltitude.TryParseText(text, out int feet, out string error));
            Assert.Equal(expected, feet);
            Assert.Equal("", error);
        });
    }

    // ---- The write ----------------------------------------------------------------------------

    [Theory]
    [InlineData("en-US", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]   // measured: reads 1030
    [InlineData("en-US", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]     // measured: reads -60
    [InlineData("en-US", 0, "-10 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("en-US", 14100, "14090 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("en-US", -1070, "-1080 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("de-DE", 14100, "14090 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("sv-SE", 1030, "1020 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    [InlineData("sv-SE", -60, "-70 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]     // not U+2212, which the RPN parser rejects
    [InlineData("sv-SE", -1070, "-1080 (>L:B757_OVHD_LDG_ELEV) 1 (>L:B757_OVHD_LDG_ELEV_CHG)")]
    public void Writes_the_measured_sequence(string culture, int feet, string expected)
    {
        InCulture(culture, () =>
        {
            string rpn = Bbs757LandingAltitude.Write(feet);
            Assert.Equal(expected, rpn);
            Assert.DoesNotContain('−', rpn);
            Assert.True(rpn.All(ch => ch < 0x80), "the RPN is plain ASCII");
        });
    }

    // ---- The phrases --------------------------------------------------------------------------

    [Theory]
    [InlineData(1030.0, "Landing altitude 1030 feet")]
    [InlineData(-60.0, "Landing altitude minus 60 feet")]
    [InlineData(0.0, "Landing altitude 0 feet")]
    [InlineData(14100.0, "Landing altitude 14100 feet")]          // never "14,100" or "14.100"
    [InlineData(-1070.0, "Landing altitude minus 1070 feet")]
    [InlineData(1029.6, "Landing altitude 1030 feet")]            // a read-back is whole feet
    [InlineData(1030.4, "Landing altitude 1030 feet")]
    [InlineData(1030.5, "Landing altitude 1031 feet")]            // a half rounds away from zero
    [InlineData(-60.4, "Landing altitude minus 60 feet")]
    [InlineData(-0.4, "Landing altitude 0 feet")]                 // never "minus 0"
    [InlineData(null, Unavailable)]
    [InlineData(double.NaN, Unavailable)]
    [InlineData(double.PositiveInfinity, Unavailable)]
    [InlineData(double.NegativeInfinity, Unavailable)]
    public void Phrases(double? feet, string expected)
    {
        Assert.Equal(expected, Bbs757LandingAltitude.Phrase(feet));
    }

    [Theory]
    [InlineData(1030.0, "1030 feet")]
    [InlineData(-60.0, "-60 feet")]
    [InlineData(0.0, "0 feet")]
    [InlineData(14100.0, "14100 feet")]
    [InlineData(1029.6, "1030 feet")]
    [InlineData(-0.4, "0 feet")]
    [InlineData(double.NaN, "unavailable")]
    [InlineData(double.PositiveInfinity, "unavailable")]
    [InlineData(double.NegativeInfinity, "unavailable")]
    public void Displays(double feet, string expected)
    {
        Assert.Equal(expected, Bbs757LandingAltitude.Display(feet));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Display_and_phrase_are_invariant(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.Equal("14100 feet", Bbs757LandingAltitude.Display(14100));
            Assert.Equal("-60 feet", Bbs757LandingAltitude.Display(-60));        // an ASCII hyphen-minus, never U+2212
            Assert.Equal("1030 feet", Bbs757LandingAltitude.Display(1029.6));
            Assert.Equal("unavailable", Bbs757LandingAltitude.Display(double.NaN));

            Assert.Equal("Landing altitude 14100 feet", Bbs757LandingAltitude.Phrase(14100));
            Assert.Equal("Landing altitude minus 60 feet", Bbs757LandingAltitude.Phrase(-60));
            Assert.Equal("Landing altitude minus 1070 feet", Bbs757LandingAltitude.Phrase(-1070.0));
            Assert.Equal("Landing altitude 1030 feet", Bbs757LandingAltitude.Phrase(1029.6));
            Assert.Equal(Unavailable, Bbs757LandingAltitude.Phrase(null));
            Assert.Equal(Unavailable, Bbs757LandingAltitude.Phrase(double.NaN));

            Assert.DoesNotContain('−', Bbs757LandingAltitude.Display(-60));
            Assert.DoesNotContain(',', Bbs757LandingAltitude.Display(14100));
            Assert.DoesNotContain('.', Bbs757LandingAltitude.Display(14100));
        });
    }
}
