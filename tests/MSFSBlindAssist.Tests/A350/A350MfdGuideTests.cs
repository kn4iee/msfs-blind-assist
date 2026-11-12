namespace MSFSBlindAssist.Tests.A350;

/// <summary>Plan 25, final review: the pilot's guides teach the MFD window's keys as the window has them: Alt+S the scratchpad box,
/// Alt+W the OIS, Backspace and Delete as the A380's window, PageUp and PageDown "Previous page" and "Next page".</summary>
public class A350MfdGuideTests
{
    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { ClaudeContextBudgetTests.RepoRoot() }.Concat(path).ToArray()));

    /// <summary>The hotkey reference's MFD window block, from its heading to the next heading.</summary>
    private static string ReferenceBlock()
    {
        string all = Read("guide", "hotkey-reference.md");
        int at = all.IndexOf("{#hk-a350-mfd}", StringComparison.Ordinal);
        Assert.True(at >= 0, "the hotkey reference has no A350 MFD window block");
        int end = all.IndexOf("\n### ", at, StringComparison.Ordinal);
        return all[at..(end < 0 ? all.Length : end)];
    }

    [Fact]
    public void The_hotkey_reference_has_the_windows_keys()
    {
        string block = ReferenceBlock();
        Assert.Contains("**Alt+W** OIS", block);
        Assert.DoesNotContain("**Alt+S** OIS", block);
        Assert.Contains("**Alt+S**", block);
        Assert.Contains("**Delete**", block);
        Assert.Contains("Previous page", block);
        Assert.DoesNotContain("— CLR.", block);
    }

    [Theory]
    [InlineData("guide", "airbus-aircraft.md")]
    [InlineData("MSFSBlindAssist", "HotkeyGuides", "iniBuilds_A350_Hotkeys.txt")]
    public void No_guide_still_puts_the_ois_on_alt_s(params string[] path)
    {
        string text = Read(path);
        Assert.DoesNotContain("Alt+S OIS", text);
        Assert.Contains("Alt+W OIS", text);
    }
}
