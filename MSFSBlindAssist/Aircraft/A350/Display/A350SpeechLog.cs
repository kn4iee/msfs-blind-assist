using System.Text.RegularExpressions;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What MSFSBA said while the A350 was loaded, in a350_speech.log: every sentence through the one announcer
/// (<see cref="MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer.Trace"/>), and each background call-out it kept quiet,
/// with why, so "it never told me" and "it told me twice" can both be answered from a pilot's log. Nothing secret can reach
/// it: what is never spoken is never here, and a field's value that is spoken ("Set HOPPIE LOGON to ABC123", the OIS window's
/// list fallback with STREAMER MODE off) is left out ([A350-12]).
/// </summary>
public static class A350SpeechLog
{
    private static readonly LogChannel Channel = Log.Channel("a350_speech");

    /// <summary>The window's confirmation of a value set (<c>FbwEfbForm.FieldSetSpoken</c>).</summary>
    private static readonly Regex FieldSet = new(@"^(Set .+?) to .+$", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>A sentence as the log may hold it: a field set keeps the field's name and loses its value.</summary>
    internal static string ForTheLog(string line) => FieldSet.Match(line) is { Success: true } set ? set.Groups[1].Value + " to (value not logged)" : line;

    /// <summary>A sentence the announcer took: how (announce, immediate, queued, dropped) and what.</summary>
    public static void Said(string how, string line) => Channel.Info($"{how}: {ForTheLog(line)}");

    /// <summary>A call-out kept quiet: from what reader, why (muted, settling after a load, the pilot's own) and what.</summary>
    public static void Quiet(string reader, string why, string line) => Channel.Debug($"quiet ({reader}, {why}): {line}");
}
