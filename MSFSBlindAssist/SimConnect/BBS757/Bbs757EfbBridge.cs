using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>One read of the tablet from the module's memory: the page, or the reason it could not be read (shown as the window's
/// status). Exactly one of the two is set.</summary>
public sealed record Bbs757EfbModelRead(Bbs757EfbScreenModel? Model, string? Status);

/// <summary>What the 757 EFB bridge needs from the app, as delegates (MainForm builds it).</summary>
public sealed class Bbs757EfbBridgeIo
{
    /// <summary>Reads the tablet's control tree from the 757 module's memory now, as the window's model, or the reason there is none
    /// (the module not located yet, no access to the simulator, the BlueBird file not recognised). Called on the bridge's loop, off
    /// the UI thread; must never throw.</summary>
    public required Func<CancellationToken, Task<Bbs757EfbModelRead>> ReadModel { get; init; }

    /// <summary>A variable the definition registers, read fresh (the ground power's connection, read after a GPU tile's tap).</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>
    /// A setting read now from BlueBird's file (<c>efb_settings.json</c>, or for a registration setting the registration file
    /// written last), in the work folder <see cref="Bbs757WorkFolder.Find()"/> picks; null when it cannot be read. Called on the
    /// bridge's loop, to judge a Settings press by the file.
    /// </summary>
    public required Func<Bbs757Setting, CancellationToken, Task<Bbs757Value?>> ReadSetting { get; init; }

    /// <summary>Whether the pilot is signed in to Navigraph, read through the definition's tablet client: true, false, or null when
    /// it cannot be read. Called on the bridge's loop when the AIRAC page was newly read.</summary>
    public required Func<CancellationToken, Task<bool?>> SignedIn { get; init; }

    /// <summary>
    /// The Navigraph sign-in (<see cref="Bbs757NavigraphSignIn.SignInAsync"/>, MainForm-built over the tablet client): it speaks as
    /// it goes and returns the line the page keeps. Its early line reaches the page through <see cref="Bbs757EfbBridge.ShowSignInLine"/>.
    /// </summary>
    public required Func<CancellationToken, Task<string>> SignIn { get; init; }

    /// <summary>A press started one of the app's hooks (<see cref="Bbs757EfbExtras.HookFor"/>): the load watch or the nav data watch.
    /// Called on the UI thread (the bridge marshals), once the tap went out; for UPDATE or REMOVE with the nav data cycle installed
    /// before the tap, else null.</summary>
    public required Action<Bbs757EfbHook, int?> Hooked { get; init; }

    /// <summary>Opens / ends the definition's echo for a service row around the window's own tap.</summary>
    public required Action<Bbs757GroundService> ArmServiceEcho { get; init; }
    public required Action<Bbs757GroundService, double?> SettleServiceEcho { get; init; }

    /// <summary>Opens / ends plan 07's ground power echo around a GPU tap.</summary>
    public required Action ArmGroundPowerEcho { get; init; }
    public required Action<double?> SettleGroundPowerEcho { get; init; }
}

/// <summary>
/// The 757 EFB for the shared accessible EFB window (FbwEfbForm): BlueBird's tablet, READ from the 757 module's memory
/// (<see cref="Bbs757EfbModelReader"/> over <see cref="Bbs757EfbMemory"/>) and pressed through the EFB agent's clicks where the
/// reader found each control, as an IMcduBridge.
///
/// While the window is shown the tablet's control tree is read from memory about every <see cref="TickMs"/> ms; each read becomes the
/// window's elements (<see cref="Bbs757EfbElements"/>), kept with the page it was read from (<see cref="Bbs757EfbPushes"/>), and
/// pushed only when the page says something new (its fingerprint changed), so a still tablet costs one cheap memory read. What a read
/// says by itself, a question that appeared or BlueBird's new notifications, is said as the window's alert
/// (<see cref="Bbs757EfbReadNews"/>). Hidden, nothing is read. No capture, no OCR, no mirror: the ND stays the ND.
///
/// Presses (the owner's ruling of 2026-10-29: nothing refuses a press) queue on the command channel and run one at a time on the
/// loop, through <see cref="Bbs757EfbPressRunner"/>: a press names the element of the push it was made on, so it is judged against the
/// page the pilot pressed on, and is tapped where the control is now in a fresh memory read, or not at all when it is gone. The AIRAC
/// page carries the app's extras (the Navigraph sign-in while signed out, and its code line), and a press that starts one of the app's
/// hooks tells MainForm on the UI thread. Speech follows CORE-7: the screen reader reads the control; a pressed control the tablet
/// gave other words is said once in its new words, by the press runner; a failure is said as an alert.
/// </summary>
public sealed class Bbs757EfbBridge : IMcduBridge, IDisposable
{
    private const string StateTypeElements = "fbw_efb_elements";
    private const string StateTypeConnected = "fbw_efb_connected";
    private const int TickMs = 700;
    private const int AlertMs = 8000;
    private const int StatusMs = 600_000;

    /// <summary>The app's extras on the AIRAC page: the sign-in button and the sign-in's line.</summary>
    private const string SignInKey = "x:navigraph", SignInLineKey = "x:navigraph-code";

    private readonly CoherentBbs757EfbClient _client;
    private readonly Bbs757EfbBridgeIo _io;
    private readonly Bbs757EfbPressRunner _press;
    private readonly SynchronizationContext? _sync;
    /// <summary>The window's commands, each with the showing it was sent in (<see cref="_showing"/>).</summary>
    private readonly System.Threading.Channels.Channel<(string Command, Dictionary<string, string>? Payload, int Showing)> _commands =
        System.Threading.Channels.Channel.CreateUnbounded<(string, Dictionary<string, string>?, int)>();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _wake = new(0, int.MaxValue);
    /// <summary>The pushes the window may still press on, with the pages they showed. On the loop only.</summary>
    private readonly Bbs757EfbPushes _pushes = new();
    private readonly Bbs757EfbReadNews _news = new();

    private volatile bool _active;
    /// <summary>Counts the times the window hid (UI thread; read on the loop): a command sent in an earlier showing is dropped.</summary>
    private int _showing;
    private bool _everRead;
    private string? _lastFingerprint;
    /// <summary>The page the window shows, with the read it was built from; null while a status stands in its place.</summary>
    private Bbs757EfbShownPage? _shown;
    /// <summary>The page the last read gave, shown or not (the press runner's read).</summary>
    private Bbs757EfbScreenModel? _lastRead;
    /// <summary>When the pilot's last tap went out: BlueBird's notifications are not said for a while after it.</summary>
    private long _lastOwnTapMs = long.MinValue / 2;
    private bool? _signedIn;
    private bool _signInStale;
    private string? _signInLine;
    private string? _alert;
    private long _alertUntilMs;
    private Task? _loop;

    public event EventHandler<EFBStateUpdateEventArgs>? StateUpdated;

    public Bbs757EfbBridge(CoherentBbs757EfbClient client, Bbs757EfbBridgeIo io)
    {
        _client = client;
        _io = io;
        _sync = SynchronizationContext.Current;
        _press = new Bbs757EfbPressRunner(PressIo());
    }

    public bool IsBridgeConnected => _client.IsReady && _everRead;

    private Bbs757EfbScreenModel? Model => _shown?.Model;

    /// <summary>Starts the bridge's loop (once). It reads only while <see cref="SetActive"/> says the window is shown.</summary>
    public void Start() => _loop ??= Task.Run(() => RunLoopAsync(_cts.Token));

    /// <summary>The window was shown (true) or hidden (false): reading follows it. Hidden, the presses it sent and the loop has not
    /// come to are dropped: they were made on a tablet the next showing may no longer show.</summary>
    public void SetActive(bool active)
    {
        if (!active) Interlocked.Increment(ref _showing);
        _active = active;
        _wake.Release();
    }

    public void EnqueueCommand(string command, Dictionary<string, string>? payload = null)
    {
        _commands.Writer.TryWrite((command, payload, Volatile.Read(ref _showing)));
        _wake.Release();
    }

    // ------------------------------------------------------------------ the loop

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_active)
                {
                    DropQueuedCommands();
                    await _wake.WaitAsync(ct);
                    continue;
                }
                if (await HandleNextCommandAsync(ct))
                {
                    await RefreshSignInAsync(ct);
                    continue;
                }
                await ReadNowAsync(ct, asked: false);
                await RefreshSignInAsync(ct);
                await WaitForCommandOrWakeAsync(ct, untilCommand: false, ms: TickMs);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Warn("BBS757", "EFB reader loop: " + ex.Message);
                try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task WaitForCommandOrWakeAsync(CancellationToken ct, bool untilCommand, int ms = 10_000)
    {
        if (untilCommand) { await _wake.WaitAsync(ct); return; }
        await _wake.WaitAsync(ms, ct);
    }

    /// <summary>
    /// Reads the tablet from memory now and, when the page says something new, pushes it to the window. A read that fails shows why,
    /// as a status in the page's place. Returns the page read, or null when nothing could be read. On the loop only, and the press
    /// runner's read seam; internal for the tests, which call it on a bridge whose loop never started.
    /// </summary>
    internal async Task<Bbs757EfbScreenModel?> ReadNowAsync(CancellationToken ct, bool asked)
    {
        var read = await _io.ReadModel(ct);
        if (read.Model is not { } model)
        {
            if (read.Status is { } status) Status(status);
            return null;
        }
        if (!_everRead)
        {
            _everRead = true;
            Raise(StateTypeConnected, new Dictionary<string, string>());
        }
        TakeRead(model, asked);
        return model;
    }

    /// <summary>
    /// A read taken as what the tablet shows. What the read says by itself is said (<see cref="Bbs757EfbReadNews"/>: a question or
    /// BlueBird's notifications that were not in the read before, unless the pilot's own tap was under four seconds ago); and it is
    /// pushed when it says something the window does not show yet (its fingerprint changed), kept for the presses made on it. The
    /// AIRAC page has the Navigraph sign-in read again when it is newly shown or read because a read was asked for
    /// (<paramref name="asked"/>). On the loop only; internal for the tests.
    /// </summary>
    internal void TakeRead(Bbs757EfbScreenModel model, bool asked = false)
    {
        _lastRead = model;
        string? said = _news.After(model, Environment.TickCount64 - _lastOwnTapMs);
        string fp = Bbs757EfbPress.Fingerprint(model);
        bool fresh = fp != _lastFingerprint;
        if (IsAiracPage(model.PageName) && (fresh || asked)) _signInStale = true;
        if (!fresh)
        {
            if (said == null) return;
        }
        else
        {
            _lastFingerprint = fp;
            _shown = _pushes.Show(model);
        }
        if (said != null) SetAlert(said);
        Push();
    }

    // ------------------------------------------------------------------ commands

    /// <summary>
    /// The next command the window sent, handled; false when none waits. A command sent before the window last hid is dropped
    /// unhandled. On the loop only; internal for the tests.
    /// </summary>
    internal async Task<bool> HandleNextCommandAsync(CancellationToken ct)
    {
        while (_commands.Reader.TryRead(out var cmd))
        {
            if (cmd.Showing != Volatile.Read(ref _showing))
            {
                Log.Debug("BBS757", $"EFB reader: {cmd.Command} sent before the window hid, dropped.");
                continue;
            }
            await HandleAsync(cmd.Command, cmd.Payload, ct);
            return true;
        }
        return false;
    }

    /// <summary>The window hid: what it sent before is dropped, so nothing it asked for runs at the next showing.</summary>
    private void DropQueuedCommands()
    {
        int dropped = 0;
        while (_commands.Reader.TryRead(out _)) dropped++;
        if (dropped > 0) Log.Debug("BBS757", $"EFB reader: {dropped.ToString(CultureInfo.InvariantCulture)} command(s) dropped as the window hid.");
    }

    /// <summary>A command from the window, on the loop: a read asked for, or a press on an element of a push. On the loop only;
    /// internal for the tests.</summary>
    internal async Task HandleAsync(string command, Dictionary<string, string>? payload, CancellationToken ct)
    {
        switch (command)
        {
            case "get_display_elements":
                await ReadNowAsync(ct, asked: true);
                return;
            case "click_display_element":
                if (Pressed(payload) is { } click) await ClickAsync(click.Action, click.Page, ct);
                return;
            case "set_element_value":
                if (Pressed(payload) is { } set) await SetAsync(set.Action, set.Page, payload!.TryGetValue("value", out var v) ? v : "", ct);
                return;
        }
    }

    /// <summary>The element a press names (its push's number), with the page that push showed; null when no kept push gave the
    /// number out, so nothing is tapped.</summary>
    private (Bbs757EfbElements.Action Action, Bbs757EfbShownPage? Page)? Pressed(Dictionary<string, string>? payload)
    {
        if (payload == null || !payload.TryGetValue("index", out var index)
            || !int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out int aidx)) return null;
        if (_pushes.TryFind(aidx, out var action, out var page) && action != null) return (action, page);
        Log.Debug("BBS757", $"EFB reader: a press named element {aidx.ToString(CultureInfo.InvariantCulture)}, which no kept push gave out; nothing tapped.");
        return null;
    }

    private async Task ClickAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, CancellationToken ct)
    {
        BeginPress();
        switch (act.Kind)
        {
            case Bbs757EfbElements.ActionKind.Extra:
                await PressExtraAsync(act.ExtraKey, ct);
                return;
            case Bbs757EfbElements.ActionKind.Tap when act.Item is { } item && page != null:
                await _press.ClickAsync(new Bbs757EfbPressed(item, page.Model), ct);
                return;
        }
    }

    private async Task SetAsync(Bbs757EfbElements.Action act, Bbs757EfbShownPage? page, string value, CancellationToken ct)
    {
        if (act.Kind != Bbs757EfbElements.ActionKind.Tap || act.Item is not { } item || page == null) return;
        BeginPress();
        var pressed = new Bbs757EfbPressed(item, page.Model);
        if (item.Kind is Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)
            await _press.SwitchAsync(pressed, value == "true", ct);
        else if (item.Kind == Bbs757EfbKind.Slider)
            await _press.SliderAsync(pressed, value, ct);
    }

    /// <summary>A press of the pilot's begins: the alert shown goes, so the window speaks the next one even when it has the same
    /// words. A status stays: it is why no page is shown.</summary>
    private void BeginPress()
    {
        if (_alert == null || Model == null) return;
        _alert = null;
        Push();
    }

    /// <summary>The press runner's seams: this bridge's memory read, the agent's taps and variable reads, BlueBird's file
    /// through the app, the window's alert, and the app's echoes and hooks marshaled to the UI thread.</summary>
    private Bbs757EfbPressIo PressIo() => new()
    {
        Read = ct => ReadNowAsync(ct, asked: true),
        Tap = TapAsync,
        ReadInstalledCycle = () => Bbs757WorkFolder.Find() is { } folder ? Bbs757WorkFolder.ReadInstalledCycle(folder) : null,
        ReadVar = ReadVarAsync,
        ReadRegistered = (key, ct) => _io.ReadRegistered(key, ct),
        ReadSetting = (setting, ct) => _io.ReadSetting(setting, ct),
        Alert = Alert,
        Delay = (ms, ct) => Task.Delay(ms, ct),
        NowMs = () => Environment.TickCount64,
        ArmServiceEcho = s => PostUi(() => _io.ArmServiceEcho(s)),
        SettleServiceEcho = (s, v) => PostUi(() => _io.SettleServiceEcho(s, v)),
        ArmGroundPowerEcho = () => PostUi(_io.ArmGroundPowerEcho),
        SettleGroundPowerEcho = v => PostUi(() => _io.SettleGroundPowerEcho(v)),
        Hooked = (hook, cycle) => PostUi(() => _io.Hooked(hook, cycle)),
    };

    /// <summary>One tap through the EFB agent: true when it went out (done, or started and maybe clicked), and then it is the pilot's
    /// own press, after which BlueBird's notifications are not said for a while.</summary>
    private async Task<bool> TapAsync(Point p, CancellationToken ct)
    {
        long at = Environment.TickCount64;
        var run = await _client.RunAsync(new[] { (new EfbPoint(p.X, p.Y), 250) }, ct);
        bool tapped = run == Bbs757EfbRunResult.Done || _client.LastRunMayHaveClicked;
        if (tapped) _lastOwnTapMs = at;
        if (run != Bbs757EfbRunResult.Done) Log.Debug("BBS757", $"EFB reader: a tap ended {run}{(tapped ? ", and may have clicked" : "")}.");
        return tapped;
    }

    private async Task<double?> ReadVarAsync(string name, CancellationToken ct)
    {
        var vals = await _client.ReadVarsAsync(new[] { name }, ct);
        return vals != null && vals.TryGetValue(name, out var v) ? v : null;
    }

    // ------------------------------------------------------------------ the AIRAC page's extras

    private static bool IsAiracPage(string pageName) => Bbs757EfbSettingsMatch.SectionFor(pageName) == Bbs757SettingSection.AiracData;

    /// <summary>The app's extras on the page shown: on the AIRAC page, the Navigraph sign-in while the pilot is known to be signed
    /// out, and the sign-in's line.</summary>
    private IReadOnlyList<Bbs757EfbExtraElement> Extras()
    {
        if (Model is not { } page || !IsAiracPage(page.PageName)) return Array.Empty<Bbs757EfbExtraElement>();
        var extras = new List<Bbs757EfbExtraElement>(2);
        if (Bbs757EfbExtras.OffersSignIn(page.PageName, _signedIn)) extras.Add(new(SignInKey, Bbs757EfbExtras.SignInText, IsButton: true));
        if (_signInLine != null) extras.Add(new(SignInLineKey, _signInLine, IsButton: false));
        return extras;
    }

    /// <summary>The Navigraph sign-in read again, between presses and reads and never inside one, after a read of the AIRAC page that
    /// was new or asked for, and after a sign-in ran. On the loop only; internal for the tests.</summary>
    internal async Task RefreshSignInAsync(CancellationToken ct)
    {
        if (!_signInStale) return;
        _signInStale = false;
        bool? signedIn = await _io.SignedIn(ct);
        bool lineUsed = signedIn == true && _signInLine != null;
        if (lineUsed) _signInLine = null;
        if (signedIn == _signedIn && !lineUsed) return;
        _signedIn = signedIn;
        if (Model != null) Push();
    }

    /// <summary>The sign-in button pressed: the flow runs (it speaks as it goes), and the line it returns stays on the page.</summary>
    private async Task PressExtraAsync(string? key, CancellationToken ct)
    {
        if (key != SignInKey) return;
        string line = await _io.SignIn(ct);
        _signInLine = line;
        _signInStale = true;   // the flow may have found the pilot signed in
        if (Model != null) Push();
    }

    /// <summary>The sign-in's early line ("Navigraph code: {code}"), put on the page before the code is spoken. Called by the sign-in
    /// flow the loop awaits (MainForm wires it). Never logged.</summary>
    public void ShowSignInLine(string line)
    {
        _signInLine = line;
        if (Model != null) Push();
    }

    // ------------------------------------------------------------------ pushing

    /// <summary>Shows <paramref name="text"/> in the page's place, until the next page or status. Internal for the tests.</summary>
    internal void Status(string text)
    {
        _shown = null;
        _lastFingerprint = null;
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + StatusMs;
        Push();
    }

    private void SetAlert(string text)
    {
        _alert = text;
        _alertUntilMs = Environment.TickCount64 + AlertMs;
    }

    private void Alert(string text)
    {
        SetAlert(text);
        Push();
    }

    /// <summary>The window's elements now, numbered and kept with the page they show, for the presses made on them.</summary>
    private void Push()
    {
        string? alert = _alert != null && Environment.TickCount64 < _alertUntilMs ? _alert : null;
        var built = _pushes.Push(_shown, alert, Extras());
        Raise(StateTypeElements, built.Data);
    }

    private void Raise(string type, Dictionary<string, string> data)
    {
        var args = new EFBStateUpdateEventArgs { Type = type, Data = data };
        if (_sync == null) StateUpdated?.Invoke(this, args);
        else Post(() => StateUpdated?.Invoke(this, args));
    }

    private void PostUi(Action a)
    {
        if (_sync == null) a();
        else Post(a);
    }

    /// <summary>Posts to the UI thread's context, tolerating it going away (app exit): a WinForms context's post is a BeginInvoke on
    /// its marshaling control, which throws once that handle is destroyed (A380C-6's lesson).</summary>
    private void Post(Action a)
    {
        try { _sync!.Post(_ => a(), null); }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1500); } catch { }
        _cts.Dispose();
    }
}
