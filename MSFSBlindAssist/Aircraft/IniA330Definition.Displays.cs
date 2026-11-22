using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The A330's display reader: one client the definition owns, started with the aircraft (MainForm calls
/// <see cref="StartDisplays"/>), reset on a flight reload and stopped on a switch away. A lasting reason the reader
/// cannot read is spoken once by <see cref="A330ReaderNotice"/>, from a UI timer, through the non-interrupting announce;
/// nothing on the client's thread touches the announcer.
/// </summary>
public partial class IniA330Definition
{
    private A330ReaderClient? _readerClient;
    private IA330Displays? _displaysOverride;
    private A330ReaderNotice? _notice;
    private System.Windows.Forms.Timer? _noticeTimer;

    /// <summary>Test seams: the reader's transport factory, the clock (milliseconds) and whether the client runs its own timer.
    /// Set before the first use of <see cref="Displays"/> or <see cref="StartDisplays"/>.</summary>
    internal Func<IA330ReaderTransport?> ReaderTransportFactory { get; set; } =
        () => A330ReaderProcess.NextToApp() is { } path ? A330ReaderProcess.Start(path) : null;
    internal Func<long> NowMs { get; set; } = () => Environment.TickCount64;
    internal bool RunReaderTimer { get; set; } = true;

    private A330ReaderClient ReaderClient =>
        _readerClient ??= new A330ReaderClient(() => ReaderTransportFactory(), () => NowMs(), RunReaderTimer);

    /// <summary>The displays the A330's windows and readouts consume: the reader client (Stopped until started), or a replay source a test sets.</summary>
    internal IA330Displays Displays
    {
        get => _displaysOverride ?? ReaderClient;
        set => _displaysOverride = value;
    }

    /// <summary>Starts the reader (a second call starts no second program) and the notice timer (made once).</summary>
    internal void StartDisplays(ScreenReaderAnnouncer announcer)
    {
        ReaderClient.Start();
        if (_noticeTimer != null) return;
        _notice = new A330ReaderNotice(NowMs());
        _noticeTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _noticeTimer.Tick += (_, _) => SpeakReaderNotice(announcer);
        _noticeTimer.Start();
    }

    /// <summary>The notice timer's work: speaks the line the notice gives, if any. UI thread only.</summary>
    internal void SpeakReaderNotice(ScreenReaderAnnouncer announcer)
    {
        var line = _notice?.Next(Displays.State, NowMs());
        if (line != null) announcer.Announce(line);
    }

    partial void ResetReader(bool contextReset)
    {
        if (contextReset) _readerClient?.Reset();
    }

    partial void StopReader()
    {
        _noticeTimer?.Stop();
        _noticeTimer?.Dispose();
        _noticeTimer = null;
        _notice = null;
        _readerClient?.Stop();
    }
}
