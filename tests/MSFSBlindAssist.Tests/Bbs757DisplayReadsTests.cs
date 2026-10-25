using System.Reflection;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757-200's four AI display reads: which hotkey reads which display, which
/// instrument camera view frames it (measured on the live aircraft, 2026-10-25: index 0 frames the
/// ADI and EHSI, index 1 the EICAS view, which also holds the whole MCP, the standby instruments,
/// the autobrake, the flap gauge, the gear lever and the first officer's airspeed), and the prompts
/// that tell the AI which display of a busy frame to describe. Whether the camera lands on the
/// right view and the AI reads the right CRT is checked in the sim.
/// </summary>
public class Bbs757DisplayReadsTests
{
    private static readonly string Fallback = GeminiService.GetPromptForDisplay((GeminiService.DisplayType)(-1));

    // ---- The table ----

    [Fact]
    public void FourReads_OnePerDisplayKey()
    {
        var actions = Bbs757DisplayReads.All.Select(r => r.Action).ToArray();

        Assert.Equal(4, actions.Length);
        Assert.Equal(actions.Length, actions.Distinct().Count());
        Assert.Contains(HotkeyAction.ReadDisplayPFD, actions);
        Assert.Contains(HotkeyAction.ReadDisplayND, actions);
        Assert.Contains(HotkeyAction.ReadDisplayUpperECAM, actions);
        Assert.Contains(HotkeyAction.ReadDisplayLowerECAM, actions);
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.Adi757, "ADI", 0)]
    [InlineData(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.Ehsi757, "EHSI", 0)]
    [InlineData(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperEicas757, "Upper EICAS", 1)]
    [InlineData(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerEicas757, "Lower EICAS", 1)]
    public void EachRead_HasItsPromptNameAndView(HotkeyAction action, GeminiService.DisplayType type, string name, int view)
    {
        Assert.True(AiDisplayRead.TryGet(Bbs757DisplayReads.All, action, out var read));
        Assert.Equal(type, read.DisplayType);
        Assert.Equal(name, read.SpokenName);
        Assert.Equal(view, read.InstrumentViewIndex);
    }

    [Fact]
    public void TheViewConstants_AreTheMeasuredIndices()
    {
        Assert.Equal(0, Bbs757DisplayReads.AdiEhsiView);
        Assert.Equal(1, Bbs757DisplayReads.EicasView);
    }

    [Fact]
    public void EveryRead_NamesAMeasuredView_NeverTheCurrentOne()
    {
        // A null index would capture whatever is on screen; the 757's views are measured, so none may be null.
        Assert.All(Bbs757DisplayReads.All, r => Assert.NotNull(r.InstrumentViewIndex));
    }

    [Theory]
    [InlineData(HotkeyAction.ReadDisplayISIS)]
    [InlineData(HotkeyAction.ReadFlaps)]
    public void AnUnrelatedAction_IsNotARead(HotkeyAction action)
    {
        // The standby instruments sit in the EICAS view but have no key of their own on this aircraft.
        Assert.False(AiDisplayRead.TryGet(Bbs757DisplayReads.All, action, out _));
    }

    [Fact]
    public void TheDefinition_DispatchesThisTable()
    {
        // The override is the whole wiring: BaseAircraftDefinition.HandleHotkeyAction dispatches the table.
        var property = typeof(BBS757Definition).GetProperty("DisplayReads", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);

        var table = property.GetValue(new BBS757Definition()) as IReadOnlyList<AiDisplayRead>;

        Assert.Same(Bbs757DisplayReads.All, table);
    }

    // ---- The prompts ----

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757, "ADI")]
    [InlineData(GeminiService.DisplayType.Ehsi757, "EHSI")]
    [InlineData(GeminiService.DisplayType.UpperEicas757, "upper EICAS")]
    [InlineData(GeminiService.DisplayType.LowerEicas757, "lower EICAS")]
    public void EachPrompt_NamesTheAircraftAndItsDisplay_AndSaysToIgnoreTheRest(GeminiService.DisplayType type, string display)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.Contains("Boeing 757", prompt);
        Assert.Contains(display, prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The image may contain several displays. ONLY describe the", prompt);
        Assert.Contains("Ignore ", prompt);
        Assert.NotEqual(Fallback, prompt);
        Assert.Contains("Do not use markdown", prompt);
    }

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757)]
    [InlineData(GeminiService.DisplayType.Ehsi757)]
    [InlineData(GeminiService.DisplayType.UpperEicas757)]
    [InlineData(GeminiService.DisplayType.LowerEicas757)]
    public void EachPrompt_KeepsTheCockpitsOwnWordsAndUnits(GeminiService.DisplayType type)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.Contains("exactly as", prompt);
        Assert.Contains("Do not expand abbreviations or convert units", prompt);
    }

    [Theory]
    [InlineData(GeminiService.DisplayType.Adi757)]
    [InlineData(GeminiService.DisplayType.Ehsi757)]
    [InlineData(GeminiService.DisplayType.UpperEicas757)]
    [InlineData(GeminiService.DisplayType.LowerEicas757)]
    public void EachPrompt_IsWrittenInUsEnglish(GeminiService.DisplayType type)
    {
        string prompt = GeminiService.GetPromptForDisplay(type);

        Assert.DoesNotContain("colour", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centre", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("centred", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFourPrompts_AreDistinct()
    {
        var prompts = new[]
        {
            GeminiService.DisplayType.Adi757, GeminiService.DisplayType.Ehsi757,
            GeminiService.DisplayType.UpperEicas757, GeminiService.DisplayType.LowerEicas757,
        }.Select(GeminiService.GetPromptForDisplay).ToArray();

        Assert.Equal(4, prompts.Distinct().Count());
    }

    [Fact]
    public void TheAdiPrompt_LeadsWithTheFlightModeAnnunciations_AndLeavesTheEhsiAlone()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Adi757);

        Assert.Contains("flight mode annunciations", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.IndexOf("flight mode annunciations", StringComparison.OrdinalIgnoreCase)
                    < prompt.IndexOf("airspeed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Ignore the EHSI", prompt);
    }

    [Fact]
    public void TheEhsiPrompt_LeavesTheAdiAlone()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.Ehsi757);

        Assert.Contains("Ignore the ADI", prompt);
    }

    [Fact]
    public void TheUpperEicasPrompt_ReadsTheEngineGaugesAndAlerts_AndIgnoresTheRestOfTheEicasFrame()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.UpperEicas757);

        Assert.Contains("No alerts", prompt);
        Assert.Contains("EPR", prompt);
        Assert.Contains("N1", prompt);
        Assert.Contains("EGT", prompt);
        // The EICAS view also frames the MCP, the standby instruments, the flap gauge and the gear lever.
        Assert.Contains("Ignore the lower EICAS", prompt);
        Assert.Contains("MCP", prompt);
        Assert.Contains("standby", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheLowerEicasPrompt_AsksForThePageFirst_AndIgnoresTheRestOfTheEicasFrame()
    {
        string prompt = GeminiService.GetPromptForDisplay(GeminiService.DisplayType.LowerEicas757);

        Assert.Contains("page", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.True(prompt.IndexOf("First line", StringComparison.Ordinal)
                    < prompt.IndexOf("Then report", StringComparison.Ordinal));
        Assert.Contains("Ignore the upper EICAS", prompt);
        Assert.Contains("MCP", prompt);
        Assert.Contains("standby", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
