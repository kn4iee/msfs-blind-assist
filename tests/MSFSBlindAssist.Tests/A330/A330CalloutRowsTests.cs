using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>The Ctrl+M rows the A330's display plans add: their order, their keys, their mute state, and the
/// guard that keeps a variable key literal in one area file.</summary>
[Collection("SettingsManagerGlobalState")]
public class A330CalloutRowsTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A330CalloutRowsTests()
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

    private static (string Key, string Label)[] One(string key) => new[] { (key, key + " label") };

    private static Dictionary<string, SimVarDefinition> TwoVariables() => new()
    {
        ["B_VAR"] = new SimVarDefinition { Name = "B", DisplayName = "Bravo", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true },
        ["A_VAR"] = new SimVarDefinition { Name = "A", DisplayName = "Alpha", UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true },
    };

    [Fact]
    public void All_lists_the_areas_in_order()
    {
        var all = A330CalloutRows.Combine(One("mcdu"), One("ecam"), One("pfd"), One("nd"), One("datalink")).Select(r => r.Key);
        Assert.Equal(new[] { "mcdu", "ecam", "pfd", "nd", "datalink" }, all);
    }

    [Fact]
    public void The_real_lists_build_into_the_monitor_manager_without_a_duplicate_or_a_clash()
    {
        // Covers every later plan's list: a key added twice, or one a variable already has, throws here
        // and not from the Ctrl+M dialog's constructor when the pilot opens it.
        var vars = new IniA330Definition().GetVariables();
        var rows = A330CalloutRows.Build(MonitorRowBuilder.Build(vars), A330CalloutRows.All, vars.Keys);
        Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void The_monitor_manager_lists_the_added_rows_after_the_existing_ones()
    {
        var vars = TwoVariables();
        var rows = A330CalloutRows.Build(MonitorRowBuilder.Build(vars),
            A330CalloutRows.Combine(One("mcdu"), One("ecam"), Array.Empty<(string, string)>(), One("nd"), Array.Empty<(string, string)>()),
            vars.Keys);
        Assert.Equal(new[] { "A_VAR", "B_VAR", "mcdu", "ecam", "nd" }, rows.Select(r => r.Key));
        Assert.Equal("ecam label", rows[3].Label);
    }

    [Fact]
    public void An_added_row_with_an_existing_key_is_refused()
    {
        var vars = TwoVariables();
        var variableRows = MonitorRowBuilder.Build(vars);
        var viaRow = Assert.Throws<ArgumentException>(() => A330CalloutRows.Build(variableRows, One("A_VAR"), Array.Empty<string>()));
        Assert.Contains("A_VAR", viaRow.Message);
        // a variable's key that has no row (not announced) is refused too: it would be a second checkbox for it
        var viaKey = Assert.Throws<ArgumentException>(() => A330CalloutRows.Build(variableRows, One("SILENT_VAR"), new[] { "SILENT_VAR" }));
        Assert.Contains("SILENT_VAR", viaKey.Message);
        // and so is a key added twice
        var twice = Assert.Throws<ArgumentException>(() => A330CalloutRows.Build(variableRows,
            One("mcdu").Concat(One("mcdu")), vars.Keys));
        Assert.Contains("mcdu", twice.Message);
    }

    [Fact]
    public void No_added_row_shares_a_key_with_a_variable()
    {
        var keys = new IniA330Definition().GetVariables().Keys.ToHashSet();
        var clashes = A330CalloutRows.All.Select(r => r.Key).Where(keys.Contains).ToList();
        Assert.True(clashes.Count == 0, "added rows named like a variable: " + string.Join(", ", clashes));
    }

    [Fact]
    public void A_muted_added_row_reads_muted()
    {
        var def = new IniA330Definition();
        Assert.False(def.IsCalloutMuted("A330_TEST_CALLOUT"));
        SettingsManager.Current.A330DisabledMonitorVariables.Add("A330_TEST_CALLOUT");
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        Assert.True(def.IsCalloutMuted("A330_TEST_CALLOUT"));
        Assert.False(def.IsCalloutMuted("A330_OTHER_CALLOUT"));
    }

    private static string AircraftFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln")))
                return Path.Combine(dir.FullName, "MSFSBlindAssist", "Aircraft");
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }

    [Fact]
    public void No_variable_key_literal_appears_in_two_area_files()
    {
        string[] areas = { "Panels", "Fcu", "FlightDisplays", "Ecam", "Mcdu", "Efb", "Readouts", "FirstOfficer" };
        var seen = new Dictionary<string, List<string>>();
        foreach (string area in areas)
        {
            string text = File.ReadAllText(Path.Combine(AircraftFolder(), $"IniA330Definition.{area}.cs"));
            foreach (string key in Regex.Matches(text, "\"(A330_[A-Za-z0-9_]*)\"").Select(m => m.Groups[1].Value).Distinct())
            {
                if (!seen.TryGetValue(key, out var files)) seen[key] = files = new List<string>();
                files.Add(area);
            }
        }
        var shared = seen.Where(kv => kv.Value.Count > 1).Select(kv => $"{kv.Key} ({string.Join(", ", kv.Value)})").ToList();
        Assert.True(shared.Count == 0, "keys two area files both spell (move them to A330SharedKeys): " + string.Join("; ", shared));
    }
}
