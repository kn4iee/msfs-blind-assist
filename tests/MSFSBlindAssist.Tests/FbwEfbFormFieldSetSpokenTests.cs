using MSFSBlindAssist.Forms.FBWA380;

namespace MSFSBlindAssist.Tests;

// The EFB window's list mode (its fallback when WebView2 fails) confirms Enter on a field by saying what was set; a secret
// field (the A350 OIS's IDs under STREAMER MODE, a password) says its length alone (review 2026-10-29, OIS I6).
public class FbwEfbFormFieldSetSpokenTests
{
    [Fact]
    public void A_field_says_its_new_value() => Assert.Equal("Set DZ % to 5", FbwEfbForm.FieldSetSpoken("DZ %", "5", secret: false));

    [Fact]
    public void A_field_with_no_name_is_a_field() => Assert.Equal("Set field to 5", FbwEfbForm.FieldSetSpoken("", "5", secret: false));

    [Theory]
    [InlineData("ABCD", "Set, hidden, 4 characters")]
    [InlineData("A", "Set, hidden, 1 character")]
    [InlineData("", "Set, hidden, empty")]
    public void A_secret_field_never_says_its_value(string value, string spoken) =>
        Assert.Equal(spoken, FbwEfbForm.FieldSetSpoken("HOPPIE LOGON, hidden, 2 characters", value, secret: true));
}
