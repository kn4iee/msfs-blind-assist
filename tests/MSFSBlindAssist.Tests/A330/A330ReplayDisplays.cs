using MSFSBlindAssist.Aircraft.A330.Display;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The A330's displays for a test, with no simulator and no display reader: frames a test shows (or the fixtures'
/// frames), the reader's state a test sets, and a record of who wants which display. Give it to the definition through
/// <c>IniA330Definition.Displays</c>. It keeps what the client keeps: <see cref="Show"/> makes a frame the display's
/// newest, and its latest when complete, raising <see cref="FrameChanged"/> only when the texts differ from the last
/// complete frame's, as the client does. Frames never age here (the client gives no latest frame once it is older than
/// three refreshes): <see cref="Forget"/> stands for that. Everything runs on the test's thread; the events are raised
/// at once. The default state is Running.
/// </summary>
internal sealed class A330ReplayDisplays : IA330Displays
{
    private readonly Dictionary<A330Gauge, A330Frame> _newest = new();
    private readonly Dictionary<A330Gauge, A330Frame> _latest = new();
    private readonly Dictionary<(A330Gauge Gauge, object Owner), int> _wanted = new();
    private string? _padCaptain;
    private string? _padFirstOfficer;

    public event Action<A330Gauge>? FrameChanged;
    public event Action<A330ReaderState>? StateChanged;

    public A330ReaderState State { get; private set; } = A330ReaderState.Running;

    public string StatusText => A330ReaderStatus.Text(State);

    /// <summary>Every <see cref="Want"/>, in order.</summary>
    public List<(A330Gauge Gauge, object Owner, int RefreshMs)> Wants { get; } = new();

    /// <summary>Every <see cref="Release"/>, in order.</summary>
    public List<(A330Gauge Gauge, object Owner)> Releases { get; } = new();

    /// <summary>The refresh <paramref name="owner"/> wants <paramref name="gauge"/> at now, or null when it does not want it.</summary>
    public int? RefreshWanted(A330Gauge gauge, object owner) => _wanted.TryGetValue((gauge, owner), out int ms) ? ms : null;

    /// <summary>A replay of the named fixtures' frames (tests/MSFSBlindAssist.Tests/A330/Fixtures, read from the source
    /// tree), each shown in turn.</summary>
    public static A330ReplayDisplays FromFixtures(params string[] fixtureNames)
    {
        var replay = new A330ReplayDisplays();
        foreach (var name in fixtureNames)
        {
            string json = File.ReadAllText(Path.Combine(
                ClaudeContextBudgetTests.RepoRoot(), "tests", "MSFSBlindAssist.Tests", "A330", "Fixtures", name));
            var frame = A330ReaderProtocol.ParseFrame(json, 0)
                ?? throw new InvalidOperationException($"the fixture {name} is not a frame");
            replay.Show(frame);
        }
        return replay;
    }

    public void Want(A330Gauge gauge, object owner, int refreshMs)
    {
        ArgumentNullException.ThrowIfNull(owner);
        Wants.Add((gauge, owner, refreshMs));
        _wanted[(gauge, owner)] = refreshMs;
    }

    public void Release(A330Gauge gauge, object owner)
    {
        Releases.Add((gauge, owner));
        _wanted.Remove((gauge, owner));
    }

    public A330Frame? Latest(A330Gauge gauge) => _latest.TryGetValue(gauge, out var frame) ? frame : null;

    public A330Frame? Newest(A330Gauge gauge) => _newest.TryGetValue(gauge, out var frame) ? frame : null;

    public string? McduScratchpad(bool firstOfficer) => firstOfficer ? _padFirstOfficer : _padCaptain;

    /// <summary>The display's newest frame, and its latest when complete.</summary>
    public void Show(A330Frame frame)
    {
        _newest[frame.Gauge] = frame;
        if (!frame.Complete) return;
        bool changed = !_latest.TryGetValue(frame.Gauge, out var before) || !A330ReaderClient.SameTexts(before.Texts, frame.Texts);
        _latest[frame.Gauge] = frame;
        if (changed) FrameChanged?.Invoke(frame.Gauge);
    }

    /// <summary>The display has no frame any more, as when the client's latest one ages out.</summary>
    public void Forget(A330Gauge gauge)
    {
        _newest.Remove(gauge);
        _latest.Remove(gauge);
    }

    public void SetState(A330ReaderState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    public void SetScratchpad(bool firstOfficer, string? text)
    {
        if (firstOfficer) _padFirstOfficer = text;
        else _padCaptain = text;
    }
}
