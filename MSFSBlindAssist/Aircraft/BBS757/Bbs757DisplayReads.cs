using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The BlueBird 757-200's four AI display reads and the instrument camera view each one needs. The
/// ADI, the EHSI and both EICAS CRTs are WASM-rendered gauges with no DOM and no text behind them
/// (docs/b757/spec.md §3.4), so AI vision on a camera capture is the only way to read them.
///
/// The view indices are 0-based into the aircraft's cameras.cfg instrument cameras (the sim's own
/// "instrument view 1" is index 0) and were measured on the live aircraft, 2026-10-25: index 0
/// frames the ADI and the EHSI; index 1, titled "EICAS", frames both EICAS CRTs and, with them, the
/// whole MCP, the standby instruments, the autobrake, the flap gauge, the gear lever and the first
/// officer's airspeed, so every EICAS prompt names the CRT to describe and says to ignore the rest.
/// Index 2 is the overhead and frames no display.
///
/// The rows are <see cref="AiDisplayRead"/>s and the dispatch is
/// <c>BaseAircraftDefinition.TryReadDisplayFor</c>, shared with every other aircraft that reads
/// displays this way. The hotkey actions keep the Airbus-derived names the rest of the app uses:
/// Alt+P (<see cref="HotkeyAction.ReadDisplayPFD"/>) reads the ADI, the 757's PFD-equivalent;
/// Alt+N the EHSI; Alt+E the upper EICAS; Alt+S the lower EICAS.
/// </summary>
internal static class Bbs757DisplayReads
{
    /// <summary>Instrument view 1: the ADI and the EHSI.</summary>
    public const int AdiEhsiView = 0;

    /// <summary>Instrument view 2: both EICAS CRTs, the MCP, the standby instruments, the autobrake, the flap gauge, the gear lever and the first officer's airspeed.</summary>
    public const int EicasView = 1;

    public static readonly IReadOnlyList<AiDisplayRead> All = new[]
    {
        new AiDisplayRead(HotkeyAction.ReadDisplayPFD, GeminiService.DisplayType.Adi757, "ADI", AdiEhsiView),
        new AiDisplayRead(HotkeyAction.ReadDisplayND, GeminiService.DisplayType.Ehsi757, "EHSI", AdiEhsiView),
        new AiDisplayRead(HotkeyAction.ReadDisplayUpperECAM, GeminiService.DisplayType.UpperEicas757, "Upper EICAS", EicasView),
        new AiDisplayRead(HotkeyAction.ReadDisplayLowerECAM, GeminiService.DisplayType.LowerEicas757, "Lower EICAS", EicasView),
    };
}
