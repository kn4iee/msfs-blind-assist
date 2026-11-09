using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Human-readable dumps of the 757 layouts for the module log (<c>bbs757-module.log</c>), as the PMDG
/// module logs its layouts. Pure.</summary>
public static class Bbs757LayoutDescriptions
{
    public static IReadOnlyList<string> Describe(Bbs757FmaLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"file analysis: {l.SourceFile}: FMA: manager {H(l.ManagerBase)} span {l.ManagerSpan}, pfd vptr {H(l.PfdVptr)}, " +
            $"pfd->afds {l.PfdToAfds}, pfd->tmc {l.PfdToTmc}",
            $"file analysis: {l.SourceFile}: FMA fields: pitch {l.Pitch}, pitchArmed {l.PitchArmed}, roll {l.Roll}, " +
            $"rollArmed {l.RollArmed}, atMode {l.AtMode}",
            $"file analysis: {l.SourceFile}: FMA gate fields: status {l.Status}, fdWord {l.FdWord}, fdCaptain {l.FdCaptain}, " +
            $"fdFirstOfficer {l.FdFirstOfficer}, afdsToMcp {l.AfdsToMcp}, mcpAltitude {l.McpAltitude}, " +
            $"mcpHeading {l.McpHeading}, mcpValueType {l.McpValueType}",
        };
        Words(lines, l.SourceFile, "A/T", l.AtWords);
        Words(lines, l.SourceFile, "pitch", l.PitchWords);
        Words(lines, l.SourceFile, "pitch armed", l.PitchArmedWords);
        Words(lines, l.SourceFile, "roll", l.RollWords);
        Words(lines, l.SourceFile, "roll armed", l.RollArmedWords);
        Words(lines, l.SourceFile, "status", l.StatusWords);
        return lines;
    }

    private static void Words(List<string> lines, string file, string column, Bbs757WordTable table)
    {
        string body = string.Join(", ", table.Words.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        lines.Add($"file analysis: {file}: FMA {column} words: {body}");
    }

    public static IReadOnlyList<string> DescribeLowerEicas(Bbs757LowerEicasLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string p = $"file analysis: {l.SourceFile}: lower EICAS";
        var lines = new List<string>
        {
            $"{p}: manager {H(l.ManagerBase)} span {l.ManagerSpan}, lowerVptr {H(l.LowerVptr)}",
            $"{p} power: set {l.PowerSet}, clear {l.PowerClear}, blankClear {l.BlankClear}",
            $"{p} page: page {l.Page}, enginePage {l.EnginePage}, statusPage {l.StatusPage}, engineOff {l.EngineOff}, nLatch {l.NLatch}",
            $"{p} engines: left {l.LeftEngine}, right {l.RightEngine}, engineToData {l.EngineToData}, engineType {l.EngineType}, " +
            $"engineInvalid {l.EngineInvalid}, engineInvalidValue {l.EngineInvalidValue}, hpSpoolRr {l.HpSpoolRr}, hpSpoolPw {l.HpSpoolPw}",
        };
        if (l.Engine is { } e)
        {
            lines.Add($"{p} engine page data: n2 {e.N2}, n3 {e.N3}, fuelFlow {e.FuelFlow}, oilPressure {e.OilPressure}, " +
                $"oilTemperature {e.OilTemperature}, oilQuantity {e.OilQuantity}, vibration {e.Vibration}");
            lines.Add($"{p} engine page fields: maxN2 {e.MaxN2Left}/{e.MaxN2Right}, maxN3 {e.MaxN3Left}/{e.MaxN3Right}, " +
                $"lowerToSettings {e.LowerToSettings}, poundsFlag {e.PoundsFlag}, startFlags {e.StartFlagA}/{e.StartFlagB}/{e.StartFlagC}, " +
                $"holdTimer {e.HoldTimer}, holdDelay {e.HoldDelay}, holdBytes {e.HoldByteA}/{e.HoldByteB}");
        }
        else
        {
            lines.Add($"{p}: engine page missing: {l.EngineMissing}");
        }
        if (l.Status is { } s)
        {
            lines.Add($"{p} status list: listAddress {H(s.ListAddress)}, begin {s.ListBegin}, end {s.ListEnd}, stride {s.ListStride}, " +
                $"listCap {s.ListCap}, text {s.RecordText}, length {s.RecordLength}, sequence {s.RecordSequence}");
            lines.Add($"{p} status hydraulics: lowerToHydraulics {s.LowerToHydraulics}, pressures {s.HydraulicPressures} stride {s.HydraulicPressureStride}, " +
                $"quantities {s.HydraulicQuantities} stride {s.HydraulicQuantityStride}, capacity {s.HydraulicCapacity}, quantity {s.HydraulicQuantity}");
            lines.Add($"{p} status apu: lowerToApu {s.LowerToApu}, egt {s.ApuEgt}, rpm {s.ApuRpm}, oilQuantity {s.ApuOilQuantity}");
            lines.Add($"{p} status oxygen: lowerToOxygen {s.LowerToOxygen}, shown {s.OxygenShown}, capacity {s.OxygenCapacity}, " +
                $"active {s.OxygenActive}, full {s.OxygenFull}, oxygenLevel {s.OxygenLevel}");
            lines.Add($"{p} status pointers: rudder {s.Rudder}, aileronLeft {s.AileronLeft}, elevator {s.Elevator}, aileronRight {s.AileronRight}");
            lines.Add($"{p} status brakes: lowerToComputer {s.LowerToComputer}, brakeTemperatures {s.BrakeTemperatures}, wheels {s.BrakeWheels}");
        }
        else
        {
            lines.Add($"{p}: status page missing: {l.StatusMissing}");
        }
        return lines;
    }

    public static IReadOnlyList<string> DescribeEfb(Bbs757EfbMemLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        string classes = string.Join(", ", l.ClassVptrs.Select(c => $"{c.Kind}={H(c.Vptr)}"));
        return new List<string>
        {
            $"file analysis: {l.SourceFile}: EFB: manager {H(l.ManagerBase)} span {l.ManagerSpan}, efb vptr {H(l.EfbVptr)}, " +
            $"efb->bbos {l.EfbToBbos}, bbos->appMap {l.BbosToAppMap}",
            $"file analysis: {l.SourceFile}: EFB app fields: front {l.AppFront}, page {l.AppPage}, roots {l.AppRoots}, " +
            $"pageChildren {l.PageChildren}",
            $"file analysis: {l.SourceFile}: EFB control fields: name {l.CtrlName}, rect {l.CtrlRect}, visible {l.CtrlVisible}, " +
            $"children {l.CtrlChildren}, parent {l.CtrlParent}, labelText {l.CtrlLabelText}, buttonText {l.CtrlButtonText}, scrollboxOffset {l.ScrollboxOffset}, " +
            $"sliderKnob {l.SliderKnob}",
            $"file analysis: {l.SourceFile}: EFB phase 2 fields: textboxLabel {l.TextboxLabel}, multilineText {l.MultilineText}, " +
            $"pulldownFace {l.PulldownFace}, pulldownList {l.PulldownList}, pulldownEntries {l.PulldownEntries}, " +
            $"pulldownEntryText {l.PulldownEntryText}, pulldownOpen {l.PulldownOpen}, buttonSelectable {l.ButtonSelectable}, " +
            $"buttonUnselected {l.ButtonUnselected}, bbosModalOpen {l.BbosModalOpen}",
            $"file analysis: {l.SourceFile}: EFB notices: bbosNotices {l.BbosNotices}, noticeStride {l.NoticeStride}, " +
            $"noticeId {l.NoticeId}, noticeTitle {l.NoticeTitle}, noticeText {l.NoticeText}, noticeTime {l.NoticeTime}, " +
            $"noticeUnread {l.NoticeUnread}",
            $"file analysis: {l.SourceFile}: EFB notification panel: overlayPanelShown {l.OverlayPanelShown}, " +
            $"overlayListOpen {l.OverlayListOpen}, overlayPanelRect {l.OverlayPanelRect}, overlayPanelHeight {l.OverlayPanelHeight}, " +
            $"overlayFlightTimeText {l.OverlayFlightTimeText}",
            $"file analysis: {l.SourceFile}: EFB boarding box: overlayBoardingRect {l.OverlayBoardingRect}, bbosLoadState {l.BbosLoadState}, " +
            $"bbosLoadDeboarding {l.BbosLoadDeboarding}, bbosLoadFlown {l.BbosLoadFlown}, bbosLoadRemaining {l.BbosLoadRemaining}",
            $"file analysis: {l.SourceFile}: EFB keyboard: keyboardShown {l.KeyboardShown}, keyboardReady {l.KeyboardReady}, " +
            $"keyboardKeypad {l.KeyboardKeypad}, keyboardTarget {l.KeyboardTarget}, keypadRect {l.KeypadRect}, " +
            $"keyboardNumeric {l.KeyboardNumeric}, keyboardShift {l.KeyboardShift}, keyboardCaps {l.KeyboardCaps}, " +
            $"textboxCaret {l.TextboxCaret}, textboxMaxLength {l.TextboxMaxLength}, textboxKeypad {l.TextboxKeypad}, toggleOn {l.ToggleOn}",
            $"file analysis: {l.SourceFile}: EFB classes: {classes}",
        };
    }
}
