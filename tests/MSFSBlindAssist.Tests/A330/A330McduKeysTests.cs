using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.A330.Display;
using MSFSBlindAssist.Forms.IniA330;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests;

/// <summary>The A330 MCDU window's keys: what each character types, which MCDU key each shortcut presses, and each
/// side's own keys and display.</summary>
[Collection("SettingsManagerGlobalState")]
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

    // The page keys as the Fenix A320 MCDU window has them (spec D16): label, MCDU key, tip.
    public static TheoryData<string, string, string> FenixPageKeys => new()
    {
        { "&DIR", "DIR", "Alt+D" }, { "&PROG", "PROG", "Alt+P" }, { "P&ERF", "PERF", "Alt+E" },
        { "&INIT", "INIT", "Alt+I" }, { "DA&TA", "DATA", "Alt+T" }, { "&F-PLN", "FPLAN", "Alt+F" },
        { "&RAD NAV", "RADNAV", "Alt+R" }, { "FUEL PRED", "FUEL", "Alt+U" }, { "SEC F-PLN", "SEC", "Alt+Shift+F" },
        { "ATC COMM", "ATC", "Alt+K" }, { "MCDU &MENU", "MENU", "Alt+M" }, { "&AIRPORT", "AIRPORT", "Alt+A" },
        { "&OVFY", "OVERFLY", "Alt+O" },
    };

    [Theory]
    [MemberData(nameof(FenixPageKeys))]
    public void The_page_keys_match_the_Fenix_MCDU_window(string label, string key, string tip)
    {
        foreach (bool firstOfficer in new[] { false, true })
            Assert.Contains((label, key, tip), CduFormProfile.A330(firstOfficer).PageKeys);
    }

    [Fact]
    public void No_two_page_keys_share_a_shortcut()
    {
        var profile = CduFormProfile.A330(false);
        Assert.Equal(profile.PageKeys.Length, profile.PageKeys.Select(k => k.tip).Distinct().Count());
        Assert.Equal(profile.PageKeys.Length, profile.PageKeys.Select(k => k.key).Distinct().Count());
        Assert.Equal(profile.AltKeys.Count, profile.AltKeys.Values.Distinct().Count());
        // the letter shortcuts of the tips are exactly AltKeys; Alt+Shift+F is taken by the form's key handler
        var tipLetters = profile.PageKeys.Select(k => k.tip).Where(t => t.StartsWith("Alt+") && t.Length == 5)
            .Select(t => t[4].ToString()).OrderBy(x => x);
        Assert.Equal(profile.AltKeys.Keys.Select(k => k.ToString()).OrderBy(x => x), tipLetters);
    }

    [Fact]
    public void Every_labels_letter_is_its_shortcuts_letter()
    {
        var profile = CduFormProfile.A330(false);
        int withLetter = 0;
        foreach (var (label, key, tip) in profile.PageKeys)
        {
            int amp = label.IndexOf('&');
            if (amp < 0) continue;
            withLetter++;
            char letter = char.ToUpperInvariant(label[amp + 1]);
            Assert.Equal("Alt+" + letter, tip);
            Assert.Equal(key, profile.AltKeys[Enum.Parse<Keys>(letter.ToString())]);
        }
        // the window's own mnemonics are the page keys' letters and the scratchpad's S, each once
        using var rig = new Rig();
        var form = rig.Form();
        var mnemonics = AllControls(form).Select(c => c.Text).Where(t => t.Contains('&'))
            .Select(t => char.ToUpperInvariant(t[t.IndexOf('&') + 1])).ToList();
        Assert.Contains('S', mnemonics);
        Assert.Equal(mnemonics.Count, mnemonics.Distinct().Count());
        Assert.DoesNotContain(Keys.S, profile.AltKeys.Keys);
        Assert.Equal(withLetter + 1, mnemonics.Count);
    }

    [Fact]
    public void Alt_Shift_F_presses_SEC_F_PLN()
    {
        var saved = SettingsManager.Current.MCDUUseAlternateLSKKeys;
        SettingsManager.Current.MCDUUseAlternateLSKKeys = false;
        try
        {
            using var rig = new Rig();
            var form = rig.Form();
            var args = new KeyEventArgs(Keys.Alt | Keys.Shift | Keys.F);
            form.HandleKey(args);
            Assert.Equal(new[] { "INI_MCDU1_SEC" }, rig.Sent);
            Assert.True(args.Handled);
            Assert.True(args.SuppressKeyPress);

            // Alt+F alone is still F-PLN, and the retired letters press nothing
            form.HandleKey(new KeyEventArgs(Keys.Alt | Keys.F));
            Assert.Equal("INI_MCDU1_FPLAN", rig.Sent[^1]);
            int count = rig.Sent.Count;
            foreach (var old in new[] { Keys.X, Keys.G, Keys.V })
            {
                var a = new KeyEventArgs(Keys.Alt | old);
                form.HandleKey(a);
                Assert.False(a.Handled);
            }
            Assert.Equal(count, rig.Sent.Count);
            form.HandleKey(new KeyEventArgs(Keys.Alt | Keys.E));
            form.HandleKey(new KeyEventArgs(Keys.Alt | Keys.K));
            form.HandleKey(new KeyEventArgs(Keys.Alt | Keys.T));
            Assert.Equal(new[] { "INI_MCDU1_PERF", "INI_MCDU1_ATC", "INI_MCDU1_DATA" }, rig.Sent.Skip(count));
        }
        finally { SettingsManager.Current.MCDUUseAlternateLSKKeys = saved; }
    }

    [Fact]
    public void The_tips_name_the_new_shortcuts()
    {
        using var rig = new Rig();
        var form = rig.Form();
        var tips = AllControls(form).OfType<Button>().ToDictionary(b => b.Text.Replace("&", ""), b => b.AccessibleDescription);
        Assert.Equal("Alt+E", tips["PERF"]);
        Assert.Equal("Alt+P", tips["PROG"]);
        Assert.Equal("Alt+T", tips["DATA"]);
        Assert.Equal("Alt+A", tips["AIRPORT"]);
        Assert.Equal("Alt+O", tips["OVFY"]);
        Assert.Equal("Alt+K", tips["ATC COMM"]);
        Assert.Equal("Alt+Shift+F", tips["SEC F-PLN"]);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllControls(c)) yield return d;
        }
    }

    private sealed class Rig : IDisposable
    {
        private readonly A330ReplayDisplays _displays = new();
        private IniA330CduForm? _form;
        public List<string> Sent { get; } = new();

        public IniA330CduForm Form() => _form = new IniA330CduForm(_displays, SeededSimConnectCache.With(), new SpeechCapture(), CduFormProfile.A330(false))
        {
            SendPressFlag = f => { Sent.Add(f); return true; },
            Delay = _ => Task.CompletedTask,
        };

        public void Dispose() => _form?.Dispose();
    }
}
