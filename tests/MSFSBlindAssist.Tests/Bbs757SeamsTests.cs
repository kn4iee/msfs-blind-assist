using System.Text.RegularExpressions;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Services;
using R = MSFSBlindAssist.Aircraft.BBS757.Bbs757Readouts;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The seams the 757 gap plans build on (docs/b757/plans/02-gaps/01-foundation.md): the optional
/// partial-method hooks on <see cref="BBS757Definition"/>, the Ctrl+M rows they extend and the shared
/// variable keys. An unimplemented hook compiles away, so with none implemented the definition must
/// behave as it did before the seams.
/// </summary>
public class Bbs757SeamsTests
{
    private static readonly BBS757Definition Def = new();

    /// <summary>Every hook later plans implement, by name (the contract in plan 01, Task 1).</summary>
    public static readonly string[] HookNames =
    {
        // Plan 03, then 12
        "AddOverheadVariables", "AddOverheadPanels", "HandleOverheadTypedText", "HandleOverheadSet", "FormatOverheadDisplay",
        // Plan 04
        "AddRadioVariables", "AddRadioPanels", "HandleRadioSet", "HandleRadioTypedText", "ProcessRadioUpdate",
        "HandleRadioHotkey", "FormatRadioDisplay", "ResetRadios", "CloseRadioWindows",
        // Plan 05
        "ProcessAutoflightUpdate", "ResetAutoflight",
        // Plan 07, then 11
        "AddEfbVariables", "AddEfbPanels", "ProcessEfbUpdate", "FormatEfbDisplay",
        // Plan 06, then 14
        "HandleReadoutHotkey", "OnOwnSquawkSet", "OnOwnAltimeterSet",
        // Plan 15
        "AddThrustVariables", "AddThrustPanels", "HandleThrustSet", "HandleThrustTypedText", "FormatThrustDisplay", "ResetThrust",
        // Plan 10
        "AddFirstOfficerVariables",
        // Plan 13
        "OnOwnWrite", "OnConnectionBatch", "ResetAnnouncements",
        // Ctrl+M rows (plans 09 and 13)
        "AddCduMonitorRows", "AddCockpitMonitorRows",
    };

    [Fact]
    public void Hooks_file_declares_every_seam()
    {
        string source = File.ReadAllText(Path.Combine(RepoRoot(), "MSFSBlindAssist", "Aircraft", "BBS757Definition.Hooks.cs"));

        var missing = HookNames.Where(name => !Regex.IsMatch(source, $@"partial void\s+{name}\s*\(")).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Monitor_rows_without_hooks_are_todays()
    {
        Assert.Equal(MonitorRowBuilder.Build(Def.GetVariables()), Def.MonitorRows());
    }

    [Fact]
    public void Shared_keys_are_distinct()
    {
        string[] keys = { Bbs757SharedKeys.ExtPwrOn, Bbs757SharedKeys.ExtPwrAvail, Bbs757SharedKeys.GpuConnected };

        Assert.Equal(new[] { "BBS757_EXT_PWR_ON", "BBS757_EXT_PWR_AVAIL", "BBS757_GPU_CONNECTED" }, keys);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, k => Assert.StartsWith("BBS757_", k, StringComparison.Ordinal));
    }

    [Fact]
    public void Display_override_unchanged_without_hooks()
    {
        // A characterization guard: the display seam runs first, so with no hook implemented the
        // squawk (BCO16) and the flap handle still read as they did before it.
        Assert.True(Def.TryGetDisplayOverride(Bbs757Squawk.CodeKey, 0x4743, out string squawk));
        Assert.Equal("4743", squawk);
        Assert.True(Def.TryGetDisplayOverride(R.FlapsHandleKey, 2, out string flaps));
        Assert.Equal("5", flaps);
    }

    internal static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}
