// Parity task 10 (2026-10-29): metric altitude was the A380's alone, MainForm asking for the A380's type before reading
// its MTRS flag. Each aircraft now says whether it has the mode and whether it is on (IAircraftDefinition.MetricAltitude),
// so the A350's METRIC ALT drives the A and Q read-outs and the 300 m call-out bands as the A380's MTRS does.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class MetricAltitudeHookTests
{
    private static double FgWord(uint bitfield) =>
        (double)((0b11UL << 32) | BitConverter.SingleToUInt32Bits((float)bitfield));

    [Fact]
    public void An_aircraft_without_the_mode_says_so()
    {
        Assert.Null(new FlyByWireA320Definition().MetricAltitude);
    }

    [Fact]
    public void The_a380_answers_from_its_mtrs_mode()
    {
        var def = new FlyByWireA380Definition();
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(1u << 13), speech);
        Assert.True(def.MetricAltitude);
        def.ProcessSimVarUpdate("FMA_FG_ALERTS", FgWord(0), speech);
        Assert.False(def.MetricAltitude);
    }
}

public class AltitudeReadoutTests
{
    [Theory]
    [InlineData(12345.4, false, "12345 feet")]
    [InlineData(1000, true, "305 meters")]
    [InlineData(-11.2, false, "-11 feet")]   // below sea level: a plain minus, whatever the culture's sign
    [InlineData(-0.4, false, "0 feet")]      // a wheel height just under zero on the ground is never "minus 0"
    public void A_and_q_name_their_unit(double feet, bool metric, string spoken)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "sv-SE" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Equal(spoken, AltitudeReadout.Text(feet, metric));
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }
}
