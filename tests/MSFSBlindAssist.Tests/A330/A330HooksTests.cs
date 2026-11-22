using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The seam of the A330 definition: one partial-void hook per owner at each dispatch point.
/// No behaviour changes with the hooks in place; these tests pin the dispatchers' own flow.</summary>
[Collection("SettingsManagerGlobalState")]
public class A330HooksTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A330HooksTests()
    {
        _savedMutes = SettingsManager.Current.A330DisabledMonitorVariables;
        SettingsManager.Current.A330DisabledMonitorVariables = new List<string>();
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    public void Dispose()
    {
        SettingsManager.Current.A330DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static string AircraftFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
                return Path.Combine(dir.FullName, "MSFSBlindAssist", "Aircraft");
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Every_hook_is_declared_once_and_called_once()
    {
        string folder = AircraftFolder();
        string hooks = File.ReadAllText(Path.Combine(folder, "IniA330Definition.Hooks.cs"));
        var declared = Regex.Matches(hooks, @"^\s*partial void (\w+)\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(declared);
        Assert.Equal(declared.Count, declared.Distinct().Count());

        // Hooks.cs holds declarations only: every statement ends in ";", no body opens inside the class
        string classBody = hooks[(hooks.IndexOf("partial class IniA330Definition", StringComparison.Ordinal))..];
        Assert.Equal(1, classBody.Count(c => c == '{'));
        Assert.Equal(1, classBody.Count(c => c == '}'));

        string callers = File.ReadAllText(Path.Combine(folder, "IniA330Definition.Handlers.cs"))
            + "\n" + File.ReadAllText(Path.Combine(folder, "IniA330Definition.Variables.cs"));
        foreach (string name in declared)
        {
            int calls = Regex.Matches(callers, @"(?<![\w.])" + name + @"\(").Count;
            // a reset hook has two call sites by design: ResetAnnouncementBaselines (false) and OnSimContextReset (true)
            int expected = name.StartsWith("Reset", StringComparison.Ordinal) ? 2 : 1;
            Assert.True(calls == expected, $"{name} is called {calls} times in Handlers.cs and Variables.cs (expected {expected})");
        }
    }

    [Fact]
    public void A_hook_is_declared_without_an_access_modifier()
    {
        string hooks = File.ReadAllText(Path.Combine(AircraftFolder(), "IniA330Definition.Hooks.cs"));
        var withModifier = Regex.Matches(hooks, @"^\s*(?:public|private|protected|internal|static|virtual|override|abstract)\b[^\n]*\bpartial void\b[^\n]*", RegexOptions.Multiline)
            .Concat(Regex.Matches(hooks, @"^\s*partial\s+(?:public|private|protected|internal)\b[^\n]*", RegexOptions.Multiline))
            .Select(m => m.Value.Trim()).ToList();
        Assert.True(withModifier.Count == 0, "partial void hooks take no access modifier: " + string.Join(" | ", withModifier));
    }

    private static readonly (string Method, string[] Hooks)[] Contract =
    {
        ("BuildVariables", new[] { "AddPanelVariables", "AddFcuVariables", "AddFlightDisplayVariables", "AddEcamVariables",
            "AddMcduVariables", "AddReadoutVariables", "AddFirstOfficerVariables" }),
        ("HandleUIVariableSet", new[] { "HandleFcuSet", "HandleEcamSet", "HandleFlightDisplaySet", "HandlePanelSet" }),
        ("ProcessSimVarUpdate", new[] { "ProcessReadoutUpdate", "ProcessPanelUpdate", "ProcessFlightDisplayUpdate",
            "ProcessFcuUpdate", "ProcessEcamUpdate", "ProcessMcduUpdate" }),
        ("TryGetDisplayOverride", new[] { "FormatFlightDisplay", "FormatFcuDisplay", "FormatPanelDisplay",
            "FormatReadoutDisplay", "FormatEcamDisplay" }),
        ("HandleHotkeyAction", new[] { "HandleReadoutHotkey", "HandleFcuHotkey", "HandleFlightDisplayHotkey",
            "HandleEcamHotkey", "HandleMcduHotkey", "HandleEfbHotkey" }),
        ("ResetAnnouncementBaselines", new[] { "ResetPanelBaselines", "ResetFlightDisplayBaselines", "ResetReadoutBaselines",
            "ResetFcuBaselines", "ResetEcamBaselines", "ResetMcduBaselines", "ResetReader" }),
        ("OnSimContextReset", new[] { "ResetPanelBaselines", "ResetFlightDisplayBaselines", "ResetReadoutBaselines",
            "ResetFcuBaselines", "ResetEcamBaselines", "ResetMcduBaselines", "ResetReader" }),
        ("OnSwitchedAway", new[] { "CloseMcduWindows", "CloseEfb", "CloseEcamWindows", "CloseFlightDisplayWindows",
            "CloseFcuWindows", "CloseReadouts", "StopReader" }),
        ("IsMuteWrapExempt", new[] { "IsReadoutMuteWrapExempt", "IsPanelMuteWrapExempt", "IsFlightDisplayMuteWrapExempt",
            "IsEcamMuteWrapExempt", "IsMcduMuteWrapExempt" }),
    };

    /// <summary>The method's lines from its signature to its closing brace (the first line that is exactly
    /// four spaces and "}"), whichever of the two files holds it.</summary>
    private static string BodyOf(string method, string source)
    {
        var sig = Regex.Match(source, @"^    (?:public|protected) override [^\n]*\b" + method + @"\(", RegexOptions.Multiline);
        if (!sig.Success) return "";
        int end = source.IndexOf("\n    }", sig.Index, StringComparison.Ordinal);
        return source[sig.Index..(end < 0 ? source.Length : end)];
    }

    [Fact]
    public void The_dispatchers_call_their_hooks_in_the_contract_order()
    {
        string folder = AircraftFolder();
        string hooksSrc = File.ReadAllText(Path.Combine(folder, "IniA330Definition.Hooks.cs"));
        var declared = Regex.Matches(hooksSrc, @"^\s*partial void (\w+)\(", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value).ToHashSet();
        string source = File.ReadAllText(Path.Combine(folder, "IniA330Definition.Handlers.cs"))
            + "\n" + File.ReadAllText(Path.Combine(folder, "IniA330Definition.Variables.cs"));

        foreach (var (method, expected) in Contract)
        {
            string body = BodyOf(method, source);
            Assert.True(body.Length > 0, $"{method} was not found in Handlers.cs or Variables.cs");
            var found = Regex.Matches(body, @"(?<![\w.])(\w+)\(")
                .Select(m => (m.Groups[1].Value, m.Index))
                .Where(t => declared.Contains(t.Item1))
                .OrderBy(t => t.Index).Select(t => t.Item1).ToList();
            Assert.True(expected.SequenceEqual(found),
                $"{method} calls hooks [{string.Join(", ", found)}], contract is [{string.Join(", ", expected)}]");
        }

        // the reset hooks take false in ResetAnnouncementBaselines and true in OnSimContextReset
        foreach (var (method, arg) in new[] { ("ResetAnnouncementBaselines", "false"), ("OnSimContextReset", "true") })
        {
            string body = BodyOf(method, source);
            foreach (string hook in declared.Where(n => n.StartsWith("Reset", StringComparison.Ordinal)))
                Assert.True(Regex.IsMatch(body, @"(?<![\w.])" + hook + @"\(" + arg + @"\)"), $"{method} must call {hook}({arg})");
        }
    }

    [Fact]
    public void The_dispatchers_still_reach_the_base_class_for_an_unknown_variable()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var speech = new SpeechCapture();
        const string key = "NOT_AN_A330_KEY";
        Assert.False(def.HandleUIVariableSet(key, 1, new SimVarDefinition(), sim, speech));
        Assert.False(def.TryGetDisplayOverride(key, 1, out _));
        Assert.False(def.ProcessSimVarUpdate(key, 1, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void Only_the_take_off_feed_is_exempt_from_the_mute_wrap()
    {
        var def = new IniA330Definition();
        Assert.True(def.IsMuteWrapExempt(IniA330Definition.TakeoffIasKey));
        Assert.False(def.IsMuteWrapExempt(IniA330Definition.TakeoffV1Key));
        Assert.False(def.IsMuteWrapExempt("A330_FLAPS"));
    }

    [Fact]
    public void An_unknown_hotkey_is_not_handled()
    {
        var def = new IniA330Definition();
        var sim = new SimConnectManager(IntPtr.Zero);
        var speech = new SpeechCapture();
        Assert.False(def.HandleHotkeyAction(HotkeyAction.ReadAltitudeAGL, sim, speech, null!, null!));
    }

    [Fact]
    public void The_flap_take_off_and_flight_mode_branches_are_reached_as_before()
    {
        var def = new IniA330Definition();
        def.GetVariables();
        def.OnDisplayPanelShown("test", new SimConnectManager(IntPtr.Zero));
        var speech = new SpeechCapture();

        // flap lever: the first sample is a silent baseline, the next change speaks its detent
        Assert.True(def.ProcessSimVarUpdate("A330_FLAPS", 0, speech));
        Assert.Empty(speech.All);
        Assert.True(def.ProcessSimVarUpdate("A330_FLAPS", 1, speech));
        Assert.Equal(new[] { "Flaps 1" }, speech.All);

        // take-off: V1 set, then the airspeed crossing it
        Assert.True(def.ProcessSimVarUpdate(IniA330Definition.TakeoffV1Key, 142, speech));
        Assert.True(def.ProcessSimVarUpdate(IniA330Definition.TakeoffVrKey, 148, speech));
        Assert.True(def.ProcessSimVarUpdate(IniA330Definition.TakeoffV2Key, 155, speech));
        foreach (double ias in new double[] { 5, 60, 141, 143 })
            Assert.True(def.ProcessSimVarUpdate(IniA330Definition.TakeoffIasKey, ias, speech));
        Assert.Equal(new[] { "V1" }, speech.Interrupts);

        // flight modes: an input is consumed and shows in the composed FMA
        Assert.True(def.ProcessSimVarUpdate("A330_M_ROLL", 5, speech));
        Assert.Equal("LOC", def.CurrentFma().Lateral);
    }
}
