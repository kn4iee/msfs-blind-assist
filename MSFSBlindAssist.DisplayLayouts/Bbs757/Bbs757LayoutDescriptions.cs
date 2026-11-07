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
        };
        Words(lines, l.SourceFile, "A/T", l.AtWords);
        Words(lines, l.SourceFile, "pitch", l.PitchWords);
        Words(lines, l.SourceFile, "pitch armed", l.PitchArmedWords);
        Words(lines, l.SourceFile, "roll", l.RollWords);
        Words(lines, l.SourceFile, "roll armed", l.RollArmedWords);
        return lines;
    }

    private static void Words(List<string> lines, string file, string column, Bbs757WordTable table)
    {
        string body = string.Join(", ", table.Words.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        lines.Add($"file analysis: {file}: FMA {column} words: {body}");
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
            $"overlayListOpen {l.OverlayListOpen}, overlayPanelRect {l.OverlayPanelRect}, overlayPanelHeight {l.OverlayPanelHeight}",
            $"file analysis: {l.SourceFile}: EFB classes: {classes}",
        };
    }
}
