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

    // Plan 06 Task 1: the OIS's LOGIN WITH NAVIGRAPH pop-up is read aloud so the pilot can type the code on Navigraph's site, and the
    // announcer logs every sentence; the code signs the pilot's account in, so the log keeps the sentence and loses the code. As the OIS
    // window reads it (dialog.test.js, opt_navigraph_login): "User Code: <code>: This window will close automatically when sucessfully
    // logged in." (the OIS's spelling). Codes here are made up.
    [Fact]
    public void A_sign_in_code_is_not_logged() =>
        Assert.Equal("User Code: (not logged): This window will close automatically when sucessfully logged in.",
            A350SpeechLog.ForTheLog("User Code: TEST-0000: This window will close automatically when sucessfully logged in."));

    [Theory]
    [InlineData("User Code: TEST-0000 This window will close automatically when sucessfully logged in.",
        "User Code: (not logged) This window will close automatically when sucessfully logged in.")]
    [InlineData("User Code:: TEST-0000 This window will close automatically when sucessfully logged in.",
        "User Code:: (not logged) This window will close automatically when sucessfully logged in.")]
    [InlineData("Alert, User Code: TEST0000", "Alert, User Code: (not logged)")]
    public void Both_shapes_of_the_pop_up_are_masked(string said, string logged) =>
        Assert.Equal(logged, A350SpeechLog.ForTheLog(said));

    [Theory]
    [InlineData("Squawk code 4521")]
    [InlineData("QR Code")]
    [InlineData("REFRESH CODE, button")]
    [InlineData("Transponder code set to 7000")]
    public void A_line_that_only_says_code_is_logged_as_is(string said) =>
        Assert.Equal(said, A350SpeechLog.ForTheLog(said));

    [Fact]
    public void A_field_set_still_loses_its_value() =>
        Assert.Equal("Set NAVIGRAPH USER CODE to (value not logged)", A350SpeechLog.ForTheLog("Set NAVIGRAPH USER CODE to TEST-0000"));
}
