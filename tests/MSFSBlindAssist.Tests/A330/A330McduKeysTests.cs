using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Forms.IniA330;

namespace MSFSBlindAssist.Tests;

/// <summary>The A330 MCDU window's keys: what each character types, which MCDU key each shortcut presses, and each
/// side's own keys and display.</summary>
public class A330McduKeysTests
{
    [Fact]
    public void The_A330_profile_types_punctuation_with_its_own_key_names()
    {
        Assert.Equal("DECIMAL", CduFormProfile.A330KeyForChar('.'));
        Assert.Equal("PLUS", CduFormProfile.A330KeyForChar('-'));
        Assert.Equal("SPACE", CduFormProfile.A330KeyForChar(' '));
        Assert.Equal("Q", CduFormProfile.A330KeyForChar('Q'));
    }

    // Key for key, as the FlyByWire A320 window ("UP" on Page Up) and the Fenix window ("ARROW_UP" on Page Up):
    // Page Up presses the MCDU's up-arrow key. The window had the two swapped until 2026-11-20.
    [Fact]
    public void The_A330_window_slews_key_for_key_like_the_other_Airbus_windows()
    {
        var profile = CduFormProfile.A330(false);
        Assert.Equal("UARROW", profile.SlewUp);
        Assert.Equal("DOWN", profile.SlewDown);
        Assert.Contains(("Slew up", "UARROW", "Page Up"), profile.PageKeys);
        Assert.Contains(("Slew down", "DOWN", "Page Down"), profile.PageKeys);
    }

    // The MCDU has one +/- key (measured 2026-11-20): its first press types '-', a second press turns that '-' into '+'.
    [Fact]
    public void A_plus_is_PLUS_pressed_twice_and_a_minus_once()
    {
        var profile = CduFormProfile.A330(false);

        Assert.Equal(new[] { new CduTypedKey("PLUS", "-"), new CduTypedKey("PLUS", "+"), new CduTypedKey("5", "+5") },
            profile.KeysToType("+5", ""));
        Assert.Equal(new[] { new CduTypedKey("PLUS", "FL-"), new CduTypedKey("5", "FL-5") },
            profile.KeysToType("-5", "FL"));

        // A plus after other characters turns only its own minus.
        Assert.Equal(new[] { new CduTypedKey("1", "A-1"), new CduTypedKey("PLUS", "A-1-"), new CduTypedKey("PLUS", "A-1+") },
            profile.KeysToType("1+", "A-"));

        // Lower case types upper case; a character the MCDU has no key for is left out.
        Assert.Equal(new[] { new CduTypedKey("A", "A"), new CduTypedKey("SPACE", "A "), new CduTypedKey("B", "A B"),
                new CduTypedKey("DECIMAL", "A B."), new CduTypedKey("SLASH", "A B./") },
            profile.KeysToType("a@ b./", ""));
        Assert.Empty(profile.KeysToType("@#", ""));
    }

    [Fact]
    public void Each_side_has_its_own_keys_and_display()
    {
        var captain = CduFormProfile.A330(false);
        var firstOfficer = CduFormProfile.A330(true);

        Assert.Equal("INI_MCDU1_", captain.KeyPrefix);
        Assert.Equal(A330Gauge.McduCaptain, captain.Gauge);
        Assert.False(captain.FirstOfficer);
        Assert.Equal("INI_MCDU2_", firstOfficer.KeyPrefix);
        Assert.Equal(A330Gauge.McduFirstOfficer, firstOfficer.Gauge);
        Assert.True(firstOfficer.FirstOfficer);

        // The same window either side: only the keys' prefix and the display differ.
        Assert.Equal(captain.PageKeys, firstOfficer.PageKeys);
        Assert.Equal(captain.AltKeys, firstOfficer.AltKeys);

        var switched = captain.ForSide(true);
        Assert.Equal("INI_MCDU2_", switched.KeyPrefix);
        Assert.Equal(A330Gauge.McduFirstOfficer, switched.Gauge);
        Assert.True(switched.FirstOfficer);
        var back = switched.ForSide(false);
        Assert.Equal("INI_MCDU1_", back.KeyPrefix);
        Assert.Equal(A330Gauge.McduCaptain, back.Gauge);
        Assert.False(back.FirstOfficer);

        // CLR is the MCDU's CLR key, never a held state ([INI-7]).
        Assert.Contains(("CLR", "CLR", "Backspace"), captain.PageKeys);
    }
}
