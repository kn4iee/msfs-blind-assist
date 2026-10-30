using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Tests.A350;

/// <summary>
/// Nothing an OIS field holds reaches a350_speech.log (review 3, 2026-10-30, C2, [A350-12]): the OIS window's list fallback says
/// "Set HOPPIE LOGON to ABC123" with STREAMER MODE off, every sentence of the announcer is logged, and the guide sends pilots to
/// that log when they report a problem. The pilot hears the value; the log never holds it.
/// </summary>
public class A350SpeechLogTests
{
    [Theory]
    [InlineData("Set HOPPIE LOGON to ABC123", "Set HOPPIE LOGON to (value not logged)")]
    [InlineData("Set SIMBRIEF USER ID to 123456", "Set SIMBRIEF USER ID to (value not logged)")]
    [InlineData("Set field to secret to me", "Set field to (value not logged)")]
    public void A_field_set_keeps_its_name_and_loses_its_value(string said, string logged) =>
        Assert.Equal(logged, A350SpeechLog.ForTheLog(said));

    [Theory]
    [InlineData("Set, hidden, 7 characters")]
    [InlineData("Flaps 1.")]
    [InlineData("Altimeters set to standard")]
    [InlineData("FCU altitude 10000, managed")]
    public void Everything_else_is_logged_as_said(string said) =>
        Assert.Equal(said, A350SpeechLog.ForTheLog(said));
}
