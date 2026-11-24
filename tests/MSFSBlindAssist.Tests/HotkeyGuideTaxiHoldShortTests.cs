// The Hold short key (Input Alt+Y, spec 8.8) sits next to Continue (Input Y) in every hotkey guide that lists
// Continue: the guides are the only place a blind pilot can look a key up (the app reads them verbatim into the
// Hotkey List window), and nothing compiles against them. The guides are ENUMERATED, not listed by hand, so a new
// aircraft's guide that carries the Continue line is held to the same rule the day it lands (the SayIntentions
// chord test's hand-written list has no BBS757 and lists HS787 and iFly, which have no Continue line).
//
// It reads the guides from the TEST output directory, where the main project's CopyToOutputDirectory="Always"
// items land through the ProjectReference: the same AppContext.BaseDirectory\HotkeyGuides the app reads.
//
// The key itself is pinned from the source: Input mode registers it on MOD_ALT, 0x59 and unregisters it, the
// dispatch raises its action, and MainForm hands it to the taxi guidance manager.

using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Tests;

public class HotkeyGuideTaxiHoldShortTests
{
    private const string ContinueDescription = "Continue past current hold-short point";
    private const string HoldShortDescription = "Hold short of the next cleared runway";

    private static IEnumerable<string> GuidePaths()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "HotkeyGuides");
        Assert.True(Directory.Exists(dir), $"Hotkey guides not found where the app looks for them: {dir}");
        return Directory.GetFiles(dir, "*.txt").OrderBy(p => p, StringComparer.Ordinal);
    }

    private static string[] Lines(string path) => File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');

    [Fact]
    public void Every_guide_that_lists_Continue_lists_the_hold_short_key()
    {
        var withContinue = GuidePaths().Where(p => Lines(p).Any(l => l.Contains(ContinueDescription, StringComparison.Ordinal))).ToList();
        Assert.True(withContinue.Count >= 7, $"expected at least seven guides with the Continue line, found {withContinue.Count}");

        foreach (string path in withContinue)
        {
            string name = Path.GetFileName(path);
            var lines = Lines(path);
            var entries = lines.Where(l => l.Contains(HoldShortDescription, StringComparison.Ordinal)).ToList();
            Assert.True(entries.Count == 1, $"{name}: expected exactly one Hold short line, found {entries.Count}.");
            Assert.StartsWith("  Alt+Y", entries[0], StringComparison.Ordinal);
            // The guide's own layout: the key column is 11 wide, and the line sits straight under Continue.
            Assert.Equal($"  Alt+Y      {HoldShortDescription}", entries[0]);
            int continueAt = Array.FindIndex(lines, l => l.Contains(ContinueDescription, StringComparison.Ordinal));
            Assert.Equal(continueAt + 1, Array.IndexOf(lines, entries[0]));
        }
    }

    [Fact]
    public void A_guide_without_Continue_does_not_list_the_hold_short_key()
    {
        foreach (string path in GuidePaths())
        {
            var lines = Lines(path);
            if (lines.Any(l => l.Contains(ContinueDescription, StringComparison.Ordinal))) continue;
            Assert.DoesNotContain(lines, l => l.Contains(HoldShortDescription, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Input_mode_registers_and_unregisters_Alt_Y_as_the_hold_short_key()
    {
        string hotkeys = Source("MSFSBlindAssist", "Hotkeys", "HotkeyManager.cs");

        Assert.Contains("private const int HOTKEY_TAXI_HOLD_SHORT = 9221;", hotkeys);
        Assert.Single(Regex.Matches(hotkeys, @"\b9221\b"));
        Assert.Contains("RegisterHotKey(windowHandle, HOTKEY_TAXI_HOLD_SHORT, MOD_ALT, 0x59);", hotkeys);
        Assert.Contains("UnregisterHotKey(windowHandle, HOTKEY_TAXI_HOLD_SHORT);", hotkeys);
        Assert.Matches(@"case HOTKEY_TAXI_HOLD_SHORT:\s+TriggerHotkey\(HotkeyAction\.TaxiHoldShort\);\s+break;", hotkeys);
        Assert.Matches(@"\n\s+TaxiHoldShort,", hotkeys);

        // Registered in Input mode, beside Continue, and unregistered with it, never in Output mode, whose Alt+Y is Where Am I.
        string input = Between(hotkeys, "// Taxi guidance hotkeys (Input mode)", "InputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(HotkeyModeStatus.Activated));");
        Assert.Contains("HOTKEY_TAXI_HOLD_SHORT, MOD_ALT, 0x59", input);
        string inputOff = Between(hotkeys, "private void DeactivateInputHotkeyMode(", "InputHotkeyModeChanged?.Invoke(this, new HotkeyModeEventArgs(wasCancelled");
        Assert.Contains("UnregisterHotKey(windowHandle, HOTKEY_TAXI_HOLD_SHORT);", inputOff);
        Assert.Single(Regex.Matches(hotkeys, @"RegisterHotKey\(windowHandle, HOTKEY_TAXI_HOLD_SHORT,"));
        Assert.Single(Regex.Matches(hotkeys, @"UnregisterHotKey\(windowHandle, HOTKEY_TAXI_HOLD_SHORT\);"));
    }

    [Fact]
    public void MainForm_hands_the_key_to_the_taxi_guidance_manager()
    {
        string form = Source("MSFSBlindAssist", "MainForm.Hotkeys.cs");

        Assert.Contains("HoldShortOfNextClearedRunway()", form);
        Assert.Matches(@"case HotkeyAction\.TaxiHoldShort:\s+(//[^\n]*\n\s+)*taxiGuidanceManager\.HoldShortOfNextClearedRunway\(\);\s+break;", form);
    }

    private static string Between(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{from} not found");
        int end = text.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, $"{to} not found after {from}");
        return text[start..end];
    }

    private static string Source(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
