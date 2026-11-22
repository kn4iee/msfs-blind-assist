using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A330.Display;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class A330DisplaysLifecycleTests
{
    private sealed class FakeTransport : IA330ReaderTransport
    {
        public readonly List<string> Written = new();
        public event Action<string>? LineReceived;
        public event Action? Exited;
        public void WriteLine(string line) => Written.Add(line);
        public void Dispose() { }
    }

    private sealed class Capturing : ScreenReaderAnnouncer
    {
        public readonly List<string> Said = new();
        public readonly List<string> Immediate = new();
        public Capturing() : base(IntPtr.Zero) { }
        public override void Announce(string message) => Said.Add(message);
        public override void AnnounceImmediate(string message) => Immediate.Add(message);
    }

    private sealed class Rig
    {
        public long Clock = 5_000;
        public int Starts;
        public bool Installed = true;
        public readonly List<FakeTransport> Transports = new();
        public readonly IniA330Definition Def = new();
        public readonly Capturing Announcer = new();

        public Rig()
        {
            Def.RunReaderTimer = false;
            Def.NowMs = () => Clock;
            Def.ReaderTransportFactory = () =>
            {
                Starts++;
                if (!Installed) return null;
                var t = new FakeTransport();
                Transports.Add(t);
                return t;
            };
        }
    }

    [Fact]
    public void Switching_away_stops_the_reader()
    {
        var r = new Rig();
        r.Def.StartDisplays(r.Announcer);
        r.Def.OnSwitchedAway();
        Assert.Contains(A330ReaderProtocol.Quit(), r.Transports[0].Written);
        Assert.Equal(A330ReaderState.Stopped, r.Def.Displays.State);
    }

    [Fact]
    public void A_flight_reload_resets_it()
    {
        var r = new Rig();
        r.Def.StartDisplays(r.Announcer);
        var written = r.Transports[0].Written;
        r.Def.ResetAnnouncementBaselines();
        Assert.DoesNotContain(A330ReaderProtocol.Reset(), written);
        r.Def.OnSimContextReset();
        Assert.Contains(A330ReaderProtocol.Reset(), written);
        r.Def.OnSwitchedAway();
    }

    [Fact]
    public void Starting_twice_starts_one_program()
    {
        var r = new Rig();
        r.Def.StartDisplays(r.Announcer);
        r.Def.StartDisplays(r.Announcer);
        Assert.Equal(1, r.Starts);
        r.Def.OnSwitchedAway();
    }

    [Fact]
    public void A_reader_that_is_not_installed_is_spoken_once_at_thirty_seconds_and_never_interrupts()
    {
        var r = new Rig { Installed = false };
        r.Def.StartDisplays(r.Announcer);
        Assert.Equal(A330ReaderState.NotInstalled, r.Def.Displays.State);
        r.Clock = 5_000 + 29_900;
        r.Def.SpeakReaderNotice(r.Announcer);
        Assert.Empty(r.Announcer.Said);
        r.Clock = 5_000 + 30_000;
        r.Def.SpeakReaderNotice(r.Announcer);
        Assert.Equal(new[] { "A330 displays unavailable: the display reader is not installed. The MCDU window cannot read its screen." }, r.Announcer.Said);
        r.Clock += 10_000;
        r.Def.SpeakReaderNotice(r.Announcer);
        Assert.Single(r.Announcer.Said);
        Assert.Empty(r.Announcer.Immediate);
        r.Def.OnSwitchedAway();
    }

    [Fact]
    public void The_spoken_line_is_the_same_under_a_comma_decimal_culture()
    {
        TestCulture.Run("de-DE", () =>
        {
            var r = new Rig { Installed = false };
            r.Def.StartDisplays(r.Announcer);
            r.Clock += 30_000;
            r.Def.SpeakReaderNotice(r.Announcer);
            Assert.Equal("A330 displays unavailable: the display reader is not installed. The MCDU window cannot read its screen.", Assert.Single(r.Announcer.Said));
            r.Def.OnSwitchedAway();
            return 0;
        });
    }
}
