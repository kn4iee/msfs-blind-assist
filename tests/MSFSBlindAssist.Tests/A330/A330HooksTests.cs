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
