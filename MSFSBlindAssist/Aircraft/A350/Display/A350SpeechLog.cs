using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>
/// What MSFSBA said while the A350 was loaded, in a350_speech.log: every sentence through the one announcer
/// (<see cref="MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer.Trace"/>), and each background call-out it kept quiet,
/// with why, so "it never told me" and "it told me twice" can both be answered from a pilot's log. Nothing secret can reach
/// it: what is never spoken is never here ([A350-12]).
/// </summary>
public static class A350SpeechLog
{
    private static readonly LogChannel Channel = Log.Channel("a350_speech");

    /// <summary>A sentence the announcer took: how (announce, immediate, queued, dropped) and what.</summary>
    public static void Said(string how, string line) => Channel.Info($"{how}: {line}");

    /// <summary>A call-out kept quiet: from what reader, why (muted, settling after a load, the pilot's own) and what.</summary>
    public static void Quiet(string reader, string why, string line) => Channel.Debug($"quiet ({reader}, {why}): {line}");
}
