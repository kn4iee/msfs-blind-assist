using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.BBS757;

/// <summary>
/// The "757 EFB" window (Shift+T): BlueBird's tablet is a WASM picture a screen reader cannot use,
/// so this window operates it for the pilot, by scripted clicks through the tablet's own mouse
/// handler (<see cref="CoherentBbs757EfbClient"/>, positions in <see cref="Bbs757EfbLayout"/>), and
/// checks each result where it can be read: BlueBird's work folder (<see cref="Bbs757WorkFolder"/>)
/// for the nav data cycle, the SimBrief ID, the units and the nosewheel steering, and the hidden helper view
/// (<see cref="CoherentBbs757TabletClient"/>) for the Navigraph sign-in.
///
/// Both clients are MainForm's: this window uses them and disposes neither.
///
/// The nav data update clicks the spot that is BlueBird's REMOVE whenever BlueBird saw no newer
/// cycle at the flight's load, so it clicks only under <see cref="Bbs757EfbLayout.MayClickUpdate"/>,
/// re-checked by the agent on the page it clicks, once per flight load, and never when anything it
/// needs cannot be read or the work folders disagree. The units answer is pressed only right after
/// a finished fetch, on its page, and what it did is read back from BlueBird's unit system setting.
/// A cockpit preset loads only in its phase: a ground one on the ground, a flight one airborne.
/// The SimBrief fuel and payload load taps BlueBird's own IMPORT FROM SIMBRIEF and START BOARDING
/// and hands the rest to the definition's load watch (<see cref="Bbs757LoadIo"/>, MainForm's seams),
/// which says when the fueling and the boarding are complete. The steering switch toggles, so it is
/// clicked only while BlueBird's setting reads the tiller. The ground power buttons read BlueBird's ground
/// power first and tap the GPU tile (it toggles too) only when it differs from the wish, refuse a disconnect
/// while external power is on line, and say what the ground power reads afterward
/// (<see cref="Bbs757GroundPower.SwitchAsync"/>).
///
/// Speech (CORE-7): the screen reader says the button; the window says once what came of it, which
/// the reader cannot know ("Nav data cycle 2610 installed…", "SimBrief ID saved", "Weight units now
/// pounds", "Loading Before start", "Fueling and boarding started…", "Ground power connected…"). An
/// action the window cannot do is refused aloud: the EFB unreachable, an action still running, an ID
/// that is not digits, a preset for the other phase, a plan the tanks or holds cannot take or in other
/// units than the aircraft's, a ground power disconnect while external power is on line.
/// The SimBrief ID is never logged or spoken; the Navigraph code is spoken letter by letter, before
/// the browser opens, and shown in the status box, never logged. The status box is a read-only
/// TextBox (VAT-13).
/// </summary>
public class BBS757EfbForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public const string NotReachableText = "The 757's EFB is not reachable. Is the aircraft loaded?";
    public const string LayoutUnknownText = "The 757's EFB layout is not the one this version knows.";
    public const string BusyText = "The EFB is still busy with the last action.";
    public const string NotFinishedText = "The EFB did not finish the clicks. Try again.";
    public const string FolderMissingText = "BlueBird's 757 folder was not found";
    public const string ReloadFirstText = "BlueBird checks for new nav data when the flight loads. Reload the flight, then try again.";
    public const string StillCheckingText = "BlueBird is still checking for nav data. Try again in a minute.";
    public const string NotSignedInText = "Sign in to Navigraph first, then reload the flight and update.";
    public const string AlreadyRanText = "The nav data update already ran on this flight. Reload the flight, then try again.";
    public const string FetchFirstText = "Fetch the SimBrief flight first.";
    public const string RemovedText =
        "BlueBird removed the installed nav data instead of updating it. With an active Navigraph subscription, reload the flight and update again.";
    public const string FoldersDisagreeText =
        "More than one BlueBird work folder was found and they disagree. Update from the EFB in the sim.";
    public const string GroundStateUnknownText = "The aircraft's ground state cannot be read, so no preset was loaded.";
    public const string SetSimBriefIdFirstText = "Set your SimBrief ID first.";
    public const string LoadOnGroundOnlyText = "Fuel and payload load only on the ground.";
    public const string LoadGroundUnknownText = "The aircraft's ground state cannot be read, so nothing was loaded.";
    public const string PlanUnreadableText = "The SimBrief plan's fuel and weights could not be read. Nothing was loaded.";
    public const string LoadNestMissingText = MSFSBlindAssist.Aircraft.BBS757Definition.NestMissingWarning + " Nothing was loaded.";
    public const string UnitsQuestionOpenText =
        "BlueBird is asking whether to switch the weight units. Press Accept units or Keep units first.";

    /// <summary>The two hosts a Navigraph sign-in address may have to be opened.</summary>
    private static readonly string[] NavigraphHosts = { "navigraph.com", "identity.api.navigraph.com" };

    private const int StatusTickMs = 1000;
    private const int CyclePollMs = 1000;
    private const int CycleWaitMs = 20000;
    private const int SimBriefWaitMs = 3000;
    private const int SimBriefPollMs = 250;
    // How long the units setting is watched for BlueBird's write after Accept units (it is written
    // at once, as the SimBrief ID is), and after Keep units, which should leave it as it was.
    private const int UnitsAcceptWaitMs = 3000;
    private const int UnitsKeepWaitMs = 1000;
    private const int UnitsPollMs = 250;
    // The fresh SIM ON GROUND read a preset waits for (the 1 Hz batch carries it).
    private const int OnGroundReadTimeoutMs = 2500;
    // The tablet client scrapes every second while the CDU window is hidden: two cycles and a margin.
    private const int TabletReadableWaitMs = 2500;
    // One fresh read of a registered variable (an individual definition answers within a frame or two).
    private const int LoadReadTimeoutMs = 1500;
    // The fresh ground power reads: a batch row answers with the next 1 Hz delivery, as OnGroundReadTimeoutMs does.
    private const int GroundPowerReadTimeoutMs = 2500;
    // How long the steering setting is watched for BlueBird's write after the click (measured: at once).
    private const int SteeringWaitMs = 3000;
    private const int SteeringPollMs = 250;
    // The chocks are read back this long after the tile's tap.
    private const int ChocksReadBackDelayMs = 1500;

    private readonly CoherentBbs757EfbClient _efb;
    private readonly CoherentBbs757TabletClient _tablet;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly Func<IReadOnlyList<string>> _workRoots;
    // SIM ON GROUND read fresh: true on the ground, false airborne, null when it cannot be read.
    private readonly Func<int, CancellationToken, Task<bool?>> _readOnGround;
    private readonly Bbs757LoadIo _loadIo;
    // Canceled when the window is disposed (a profile switch, app exit): no pending action may
    // click or speak after that.
    private readonly CancellationTokenSource _lifetime = new();

    private Button _navigraphButton = null!;
    private Button _updateButton = null!;
    private TextBox _simBriefBox = null!;
    private Button _setSimBriefButton = null!;
    private Button _fetchButton = null!;
    private Button _acceptUnitsButton = null!;
    private Button _keepUnitsButton = null!;
    private Button _loadSimBriefButton = null!;
    private ComboBox _presetCombo = null!;
    private Button _loadPresetButton = null!;
    private Button _apuButton = null!;
    private Button _irsButton = null!;
    private Button _steeringButton = null!;
    private Button _removeChocksButton = null!;
    private Button _setChocksButton = null!;
    private Button _connectGroundPowerButton = null!;
    private Button _disconnectGroundPowerButton = null!;
    private TextBox _statusBox = null!;
    private Button _closeButton = null!;
    private readonly System.Windows.Forms.Timer _statusTimer;

    private IntPtr _previousWindow = IntPtr.Zero;
    private string _lastResult = "";
    private bool _busy;
    private bool _disposed;
    // What ran last (the units answer may only follow a finished fetch), and the page the fetch ran on.
    private Bbs757EfbRunKind? _lastRunKind;
    private bool _lastRunDone;
    private long? _fetchPageMs;
    // Whether a fetch left BlueBird's units question up: the load refuses while it may be ([B757-4]).
    private readonly Bbs757UnitsQuestion _unitsQuestion = new();
    // The work folder last written to the log, so a change of folder is logged once.
    private string? _loggedFolder;
    private bool _folderLogged;

    /// <param name="readOnGround">
    /// Reads SIM ON GROUND fresh within the given milliseconds: true on the ground, false airborne,
    /// null when it cannot be read (no simulator connection). A preset or a fuel and payload load
    /// is made only after it.
    /// </param>
    /// <param name="loadIo">
    /// The plan fetch, the definition's load watch, fresh reads (the chocks, the ground power) and the ground power echo.
    /// </param>
    public BBS757EfbForm(CoherentBbs757EfbClient efb, CoherentBbs757TabletClient tablet, ScreenReaderAnnouncer announcer,
        Func<int, CancellationToken, Task<bool?>> readOnGround, Bbs757LoadIo loadIo)
        : this(efb, tablet, announcer, readOnGround, loadIo, Bbs757WorkFolder.DefaultRoots)
    {
    }

    internal BBS757EfbForm(CoherentBbs757EfbClient efb, CoherentBbs757TabletClient tablet, ScreenReaderAnnouncer announcer,
        Func<int, CancellationToken, Task<bool?>> readOnGround, Bbs757LoadIo loadIo, Func<IReadOnlyList<string>> workRoots)
    {
        _efb = efb;
        _tablet = tablet;
        _announcer = announcer;
        _readOnGround = readOnGround;
        _loadIo = loadIo;
        _workRoots = workRoots;

        BuildForm();
        _statusTimer = new System.Windows.Forms.Timer { Interval = StatusTickMs };
        _statusTimer.Tick += (_, _) => UpdateStatus();
    }

    // ------------------------------------------------------------------
    // Pure helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether a sign-in address may be opened in the pilot's browser: an absolute https address on
    /// the default port, no user name, its host exactly navigraph.com or identity.api.navigraph.com.
    /// The host is compared whole, never searched in the text ("navigraph.com.example" and
    /// "example/navigraph.com" are refused).
    /// </summary>
    internal static bool IsTrustedNavigraphUri(string? text, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var parsed)) return false;
        if (parsed.Scheme != Uri.UriSchemeHttps || !parsed.IsDefaultPort || parsed.UserInfo.Length > 0) return false;
        foreach (string host in NavigraphHosts)
        {
            if (string.Equals(parsed.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                uri = parsed;
                return true;
            }
        }
        return false;
    }

    /// <summary>A sign-in code spelled for speech: "A B 1 2", a dash said as "dash", spaces dropped.</summary>
    internal static string SpellCode(string code)
    {
        var parts = new List<string>();
        foreach (char c in code)
        {
            if (char.IsWhiteSpace(c)) continue;
            parts.Add(c == '-' ? "dash" : c.ToString());
        }
        return string.Join(" ", parts);
    }

    /// <summary>
    /// How long the browser waits after the code is spoken, so opening it (which moves focus, and
    /// NVDA then reads the browser) does not cut the code off: 1.5 s and 350 ms per spoken
    /// character (a dash is a word), at least 2 s and at most 6 s. The code also stays in the status box.
    /// </summary>
    internal static int CodeSpeechMs(string code)
    {
        int spoken = SpellCode(code).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(1500 + 350 * spoken, 2000, 6000);
    }

    /// <summary>The refusal on a new cycle's first day: no flight loaded today can have seen it.</summary>
    internal static string NewTodayText(int cycle) =>
        $"Nav data cycle {cycle.ToString(CultureInfo.InvariantCulture)} is new today. " +
        "Reload the flight after midnight UTC, then update.";

    /// <summary>
    /// What a finished SimBrief fetch says. BlueBird asks to switch the unit system only when the
    /// plan's weight units differ from the aircraft's, and the question cannot be seen. With both units
    /// read (the plan's from the app's own fetch of the same plan), this says whether the question is up;
    /// otherwise when it comes, what each answer does, and the aircraft's units now when they can be read.
    /// </summary>
    internal static string FetchedText(Bbs757WeightUnits? aircraftUnits, Bbs757WeightUnits? planUnits = null)
    {
        const string next = " Then run ACARS INIT FLT in the CDU window.";
        if (aircraftUnits is Bbs757WeightUnits aircraft && planUnits is Bbs757WeightUnits plan)
            return plan == aircraft
                ? $"SimBrief flight requested. The plan and the aircraft both use {Bbs757EfbLayout.UnitsName(aircraft)}.{next}"
                : $"SimBrief flight requested. The plan is in {Bbs757EfbLayout.UnitsName(plan)} and the aircraft in " +
                  $"{Bbs757EfbLayout.UnitsName(aircraft)}, so BlueBird is asking whether to switch: Accept units switches, " +
                  $"Keep units keeps them, and the app says which units are set.{next}";
        string now = aircraftUnits is Bbs757WeightUnits u ? $" The aircraft uses {Bbs757EfbLayout.UnitsName(u)}." : "";
        return "SimBrief flight requested. If the plan's weight units differ from the aircraft's, BlueBird asks to switch them: " +
               "Accept units switches, Keep units keeps them, and the app says which units are set." + now + next;
    }

    // ------------------------------------------------------------------
    // Form construction
    // ------------------------------------------------------------------

    private void BuildForm()
    {
        Text = "757 EFB";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        KeyPreview = true;
        ClientSize = new Size(470, 470);

        const int left = 15, fullW = 440, halfW = 215, h = 32, gap = 8;
        int y = 15, tab = 0;

        Button MakeButton(string text, int x, int width, Func<Task> action)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, h), TabIndex = tab++ };
            b.Click += (_, _) => RunAction(action);
            Controls.Add(b);
            return b;
        }

        _navigraphButton = MakeButton("Sign in to Navigraph", left, fullW, SignInToNavigraphAsync);
        y += h + gap;
        _updateButton = MakeButton("Update nav data", left, fullW, UpdateNavDataAsync);
        y += h + gap + 4;

        var simBriefLabel = new Label { Text = "SimBrief ID", Location = new Point(left, y + 6), AutoSize = true, TabIndex = tab++ };
        _simBriefBox = new TextBox
        {
            Location = new Point(left + 90, y + 2),
            Size = new Size(140, 25),
            MaxLength = 10,
            AccessibleName = "SimBrief ID",
            TabIndex = tab++,
        };
        _simBriefBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return && !e.Alt && !e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                RunAction(SetSimBriefIdAsync);
            }
        };
        Controls.Add(simBriefLabel);
        Controls.Add(_simBriefBox);
        _setSimBriefButton = MakeButton("Set SimBrief ID", left + 245, 195, SetSimBriefIdAsync);
        y += h + gap;

        _fetchButton = MakeButton("Fetch SimBrief flight", left, fullW, FetchSimBriefFlightAsync);
        y += h + gap;
        _acceptUnitsButton = MakeButton("Accept units", left, halfW, () => AnswerUnitsAsync(accept: true));
        _keepUnitsButton = MakeButton("Keep units", left + halfW + 10, halfW, () => AnswerUnitsAsync(accept: false));
        y += h + gap;
        _loadSimBriefButton = MakeButton("Load fuel and payload from SimBrief", left, fullW, LoadFuelAndPayloadAsync);
        y += h + gap + 4;

        var presetLabel = new Label { Text = "Cockpit preset", Location = new Point(left, y + 6), AutoSize = true, TabIndex = tab++ };
        _presetCombo = new ComboBox
        {
            Location = new Point(left + 105, y + 2),
            Size = new Size(170, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Cockpit preset",
            TabIndex = tab++,
        };
        _presetCombo.Items.AddRange(Bbs757EfbLayout.PresetNames.Cast<object>().ToArray());
        _presetCombo.SelectedIndex = Bbs757EfbLayout.PresetNames.ToList().IndexOf("Before start");
        Controls.Add(presetLabel);
        Controls.Add(_presetCombo);
        _loadPresetButton = MakeButton("Load preset", left + 285, 155, LoadPresetAsync);
        y += h + gap;

        _apuButton = MakeButton("APU quick start", left, halfW, () => QuickStartAsync("APU quick start"));
        _irsButton = MakeButton("IRS rapid align", left + halfW + 10, halfW, () => QuickStartAsync("IRS rapid align"));
        y += h + gap;
        _steeringButton = MakeButton("Set steering to rudder", left, fullW, SetSteeringToRudderAsync);
        y += h + gap;
        _removeChocksButton = MakeButton("Remove chocks", left, halfW, () => ChocksAsync(wantInPlace: false));
        _setChocksButton = MakeButton("Set chocks", left + halfW + 10, halfW, () => ChocksAsync(wantInPlace: true));
        y += h + gap;
        _connectGroundPowerButton = MakeButton("Connect ground power", left, halfW, () => GroundPowerAsync(connect: true));
        _disconnectGroundPowerButton = MakeButton("Disconnect ground power", left + halfW + 10, halfW, () => GroundPowerAsync(connect: false));
        y += h + gap + 4;

        var statusLabel = new Label { Text = "Status", Location = new Point(left, y), AutoSize = true, TabIndex = tab++ };
        Controls.Add(statusLabel);
        y += 20;
        _statusBox = new TextBox
        {
            Location = new Point(left, y),
            Size = new Size(fullW, 90),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "EFB status",
            TabIndex = tab++,
        };
        Controls.Add(_statusBox);
        y += 90 + gap;

        _closeButton = new Button { Text = "Close", Location = new Point(left, y), Size = new Size(fullW, h), TabIndex = tab++ };
        _closeButton.Click += (_, _) => HideAndRestore();
        Controls.Add(_closeButton);
        ClientSize = new Size(470, y + h + 15);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && !e.Alt && !e.Control && !e.Shift)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                HideAndRestore();
            }
        };

        FormClosing += (_, e) =>
        {
            // Let real app/OS shutdown through; everything else hides, as the CDU window does.
            if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing)
                return;
            e.Cancel = true;
            HideAndRestore();
        };
    }

    // ------------------------------------------------------------------
    // Status
    // ------------------------------------------------------------------

    private void UpdateStatus()
    {
        if (_disposed) return;
        string link = _efb.IsLayoutUnknown ? LayoutUnknownText
            : _efb.IsReady ? "EFB connected."
            : "EFB not reachable. Is the aircraft loaded?";
        string text = _lastResult.Length > 0 ? link + Environment.NewLine + _lastResult : link;
        if (_statusBox.Text != text) _statusBox.Text = text;
    }

    private void SetResult(string text)
    {
        _lastResult = text;
        UpdateStatus();
    }

    /// <summary>The outcome of an action, said once and kept in the status box.</summary>
    private void Say(string text)
    {
        if (_disposed) return;
        SetResult(text);
        _announcer.Announce(text);
    }

    /// <summary>An action that could not run, said at once.</summary>
    private void Refuse(string text)
    {
        if (_disposed) return;
        SetResult(text);
        _announcer.AnnounceImmediate(text);
    }

    /// <summary>
    /// A run that did not finish, said as what the pilot can do about it. Everything that stopped
    /// before a click is a refusal (said at once); a run that started and stopped is a result.
    /// </summary>
    private void SayRunProblem(Bbs757EfbRunResult result)
    {
        switch (result)
        {
            case Bbs757EfbRunResult.Done: return;
            case Bbs757EfbRunResult.LayoutUnknown: Refuse(LayoutUnknownText); return;
            case Bbs757EfbRunResult.PageTooEarly: Refuse(ReloadFirstText); return;
            case Bbs757EfbRunResult.PageTooYoung: Refuse(StillCheckingText); return;
            case Bbs757EfbRunResult.AlreadyRan: Refuse(AlreadyRanText); return;
            case Bbs757EfbRunResult.OtherPage: Refuse(FetchFirstText); return;
            case Bbs757EfbRunResult.Unreachable: Refuse(NotReachableText); return;
            default: Say(NotFinishedText); return;
        }
    }

    /// <summary>
    /// Runs a sequence and remembers what ran (for the units answer). Every run counts, refused or
    /// not: after any other run the units question can no longer be assumed on screen.
    /// </summary>
    private async Task<Bbs757EfbRunResult> RunAsync(Bbs757EfbRunKind kind,
        IReadOnlyList<(EfbPoint Point, int DelayAfterMs)> seq, Bbs757EfbRunGuard? guard = null)
    {
        _lastRunKind = kind;
        _lastRunDone = false;
        var result = await _efb.RunAsync(seq, Lifetime, guard);
        _lastRunDone = result == Bbs757EfbRunResult.Done;
        if (kind == Bbs757EfbRunKind.FetchOfp) _fetchPageMs = _lastRunDone ? _efb.LastRunPageLoadedMs : null;
        return result;
    }

    /// <summary>The work folder now (newest settings file, <see cref="Bbs757WorkFolder.Find(IReadOnlyList{string})"/>); a change is logged once, never a file's content.</summary>
    private string? ResolveWorkFolder() => ResolveWorkFolder(_workRoots());

    private string? ResolveWorkFolder(IReadOnlyList<string> roots)
    {
        string? folder = Bbs757WorkFolder.Find(roots);
        if (!_folderLogged || !string.Equals(folder, _loggedFolder, StringComparison.OrdinalIgnoreCase))
        {
            _folderLogged = true;
            _loggedFolder = folder;
            Log.Info("BBS757", folder == null ? "BlueBird work folder: none found." : $"BlueBird work folder: {folder}");
        }
        return folder;
    }

    // ------------------------------------------------------------------
    // Actions
    // ------------------------------------------------------------------

    /// <summary>
    /// One action at a time: a second press while one runs is refused aloud (the tablet would get
    /// two click sequences). Buttons stay enabled, so focus never jumps while an action runs.
    /// </summary>
    private async void RunAction(Func<Task> action)
    {
        if (_disposed) return;
        if (_busy)
        {
            _announcer.AnnounceImmediate(BusyText);
            return;
        }
        _busy = true;
        try
        {
            await action();
        }
        catch (OperationCanceledException) { /* the window is going away */ }
        catch (ObjectDisposedException) { /* the window is going away */ }
        catch (Exception ex)
        {
            Log.Error("BBS757", $"EFB action failed: {ex.GetType().Name}: {ex.Message}");
            Say("The EFB action failed.");
        }
        finally
        {
            _busy = false;
        }
    }

    private CancellationToken Lifetime => _lifetime.Token;

    private async Task SignInToNavigraphAsync()
    {
        SetResult("Reading the Navigraph sign-in.");
        var (signedIn, code, uriText) = await _tablet.GetNavigraphAsync();
        if (_disposed) return;
        if (!signedIn && string.IsNullOrWhiteSpace(code))
        {
            // Nothing read, or read too soon after the helper's client connected: read again once
            // it answers, so the answer given is the helper's, not the empty one.
            if (!await TabletReadableAsync())
            {
                Refuse(NotReachableText);
                return;
            }
            (signedIn, code, uriText) = await _tablet.GetNavigraphAsync();
            if (_disposed) return;
        }
        if (signedIn)
        {
            Say("Already signed in to Navigraph");
            return;
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            Refuse("No Navigraph sign-in code is waiting yet. Try again in a minute.");
            return;
        }

        // The code is spoken BEFORE the browser opens: opening it moves focus and the screen reader
        // reads the browser, which would cut the code off. The status box holds the code as it is,
        // for reading character by character.
        if (!IsTrustedNavigraphUri(uriText, out var uri) || uri == null)
        {
            string why = string.IsNullOrWhiteSpace(uriText)
                ? "No sign-in address came with it."
                : "Its sign-in address is not a Navigraph address, so it was not opened.";
            SetResult($"Navigraph code {code.Trim()}. {why}");
            _announcer.Announce($"Navigraph code {SpellCode(code)}. {why}");
            return;
        }

        SetResult($"Navigraph code {code.Trim()}. The sign-in page opens in your browser.");
        _announcer.Announce($"Navigraph code {SpellCode(code)}. The sign-in page opens in your browser in a moment.");
        await Task.Delay(CodeSpeechMs(code), Lifetime);
        if (_disposed) return;
        if (!OpenInBrowser(uri))
        {
            SetResult($"Navigraph code {code.Trim()}. The sign-in page could not be opened.");
            _announcer.Announce("The sign-in page could not be opened.");
        }
    }

    /// <summary>
    /// Whether the helper view answers. "Not signed in, no code" and "not reachable" read the same
    /// from <see cref="CoherentBbs757TabletClient.GetNavigraphAsync"/>, and a client that has just
    /// connected is readable only after its first scrape (about a second later, measured), so this
    /// waits a little before it says the helper cannot be read.
    /// </summary>
    private async Task<bool> TabletReadableAsync()
    {
        var until = DateTime.UtcNow.AddMilliseconds(TabletReadableWaitMs);
        while (!_tablet.IsReadable)
        {
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(100, Lifetime);
        }
        return true;
    }

    private static bool OpenInBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            // Never the address: it carries the sign-in code.
            Log.Debug("BBS757", $"Navigraph sign-in page not opened: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// Whether the pilot is signed in to Navigraph, read fresh: true, false, or null when the
    /// helper cannot be read. A signed-out first answer is read again once the helper answers
    /// (it may have come before the helper's first scrape).
    /// </summary>
    private async Task<bool?> ReadSignedInAsync()
    {
        var (signedIn, _, _) = await _tablet.GetNavigraphAsync();
        if (signedIn) return true;
        if (!await TabletReadableAsync()) return null;
        (signedIn, _, _) = await _tablet.GetNavigraphAsync();
        return signedIn;
    }

    /// <summary>
    /// The nav data update. BlueBird's UPDATE spot is its REMOVE button whenever its own
    /// latest-cycle check, made inside the WASM when the flight loaded, saw no newer cycle, so the
    /// click is bound to that load (<see cref="Bbs757EfbLayout.MayClickUpdate"/>): the installed
    /// cycle out of date, the pilot signed in and not seen signed out since the tablet's page
    /// loaded, that page loaded on or after the current cycle's first full day and at least a
    /// minute ago. The agent checks the page conditions again on the page it clicks (the very page
    /// judged here), and runs the update once per page. Whatever cannot be known stops it: no work
    /// folder, an unreadable cycle file, work folders that disagree, an unreadable page or sign-in.
    /// </summary>
    private async Task UpdateNavDataAsync()
    {
        var roots = _workRoots();
        string? folder = ResolveWorkFolder(roots);
        if (folder == null)
        {
            Refuse($"{FolderMissingText}, so the installed nav data cannot be checked. Nothing was clicked.");
            return;
        }
        int? installed = Bbs757WorkFolder.ReadInstalledCycle(folder);
        if (installed == null && Bbs757WorkFolder.HasCycleFile(folder))
        {
            // Some cycle is installed but its number cannot be read: the spot may be REMOVE.
            Refuse("The installed nav data cycle cannot be read, so nothing was clicked.");
            return;
        }
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (!Bbs757EfbLayout.ShouldUpdate(installed, today))
        {
            Refuse($"Nav data is current, cycle {installed!.Value.ToString(CultureInfo.InvariantCulture)}");
            return;
        }
        // Another work folder holding current (or unreadable) nav data: BlueBird may be using that
        // one, and then the spot is REMOVE.
        if (Bbs757WorkFolder.OthersDisagree(roots, folder, AiracCycle.ForDate(today)))
        {
            Log.Info("BBS757", "Nav data update refused: the BlueBird work folders disagree.");
            Refuse(FoldersDisagreeText);
            return;
        }

        var page = await _efb.ReadPageAsync(Lifetime);
        if (_disposed) return;
        if (page == null)
        {
            Refuse(_efb.IsLayoutUnknown ? LayoutUnknownText : NotReachableText);
            return;
        }
        bool? signedIn = await ReadSignedInAsync();
        if (_disposed) return;
        if (signedIn == null)
        {
            Refuse("The Navigraph sign-in cannot be read. Try again in a moment.");
            return;
        }

        var now = DateTime.UtcNow;
        today = DateOnly.FromDateTime(now);
        bool sawSignedOut = Bbs757EfbLayout.SignedOutDuringLoad(_tablet.LastSignedOutUtc, page.LoadedUtc);
        switch (Bbs757EfbLayout.MayClickUpdate(installed, now, page.LoadedUtc, signedIn.Value, sawSignedOut))
        {
            case Bbs757UpdateGate.Allowed:
                break;
            case Bbs757UpdateGate.Current:
                Refuse($"Nav data is current, cycle {installed!.Value.ToString(CultureInfo.InvariantCulture)}");
                return;
            case Bbs757UpdateGate.NotSignedIn:
                Refuse(NotSignedInText);
                return;
            case Bbs757UpdateGate.NewToday:
                Refuse(NewTodayText(AiracCycle.ForDate(today)));
                return;
            case Bbs757UpdateGate.StillChecking:
                Refuse(StillCheckingText);
                return;
            default:
                Refuse(ReloadFirstText);
                return;
        }

        SetResult("Updating nav data.");
        var result = await RunAsync(Bbs757EfbRunKind.Update, Bbs757EfbLayout.UpdateNavData(),
            Bbs757EfbLayout.UpdateGuard(today, page.LoadedMs));
        if (result != Bbs757EfbRunResult.Done)
        {
            // A run refused or stopped before its start clicked nothing: said as it is. One that
            // started and did not report finishing may have clicked UPDATE (or REMOVE), so the
            // cycle file is watched all the same and the outcome said from it.
            if (!_efb.LastRunMayHaveClicked)
            {
                SayRunProblem(result);
                return;
            }
            Log.Debug("BBS757", $"Nav data update run ended {result}; checking the cycle file.");
            SetResult("The update's clicks were not all confirmed. Checking the nav data.");
        }

        // BlueBird may rewrite the cycle file during an update, so a missing file is judged only
        // once the whole window has passed.
        var until = DateTime.UtcNow.AddMilliseconds(CycleWaitMs);
        while (true)
        {
            int? cycle = Bbs757WorkFolder.ReadInstalledCycle(folder);
            if (cycle is int n && (installed is null || n > installed))
            {
                Say($"Nav data cycle {n.ToString(CultureInfo.InvariantCulture)} installed. Reload the flight to use it.");
                return;
            }
            if (DateTime.UtcNow >= until) break;
            await Task.Delay(CyclePollMs, Lifetime);
        }
        // The spot was REMOVE after all (a lapsed subscription, a late cycle): said plainly.
        if (installed != null && !Bbs757WorkFolder.HasCycleFile(folder))
            Say(RemovedText);
        else
            Say("The update did not finish. Try again after reloading the flight.");
    }

    private async Task SetSimBriefIdAsync()
    {
        string digits = _simBriefBox.Text.Trim();
        if (digits.Length == 0)
        {
            Refuse("Type your SimBrief ID first.");
            return;
        }
        if (!Bbs757EfbLayout.IsSimBriefId(digits))
        {
            Refuse("The SimBrief ID must be digits only.");
            return;
        }

        // The stored ID before, to tell "not taken" from "taken as something else" after. Neither
        // value is ever spoken or logged.
        string? folder = ResolveWorkFolder();
        long? before = folder == null ? null : Bbs757WorkFolder.ReadSimBriefId(folder);

        SetResult("Entering the SimBrief ID.");
        var result = await RunAsync(Bbs757EfbRunKind.SimBriefId, Bbs757EfbLayout.EnterSimBriefId(digits));
        if (result != Bbs757EfbRunResult.Done)
        {
            SayRunProblem(result);
            return;
        }

        if (folder == null)
        {
            Say($"SimBrief ID entered. {FolderMissingText}, so it could not be checked.");
            return;
        }
        long typed = Bbs757EfbLayout.SimBriefIdValue(digits);
        long? after = Bbs757WorkFolder.ReadSimBriefId(folder);
        var until = DateTime.UtcNow.AddMilliseconds(SimBriefWaitMs);
        while (after != typed && DateTime.UtcNow < until)
        {
            await Task.Delay(SimBriefPollMs, Lifetime);
            after = Bbs757WorkFolder.ReadSimBriefId(folder);
        }
        switch (Bbs757EfbLayout.JudgeSimBriefEntry(before, after, typed))
        {
            case Bbs757SimBriefEntry.Saved: Say("SimBrief ID saved"); break;
            case Bbs757SimBriefEntry.NotSaved: Say("BlueBird did not save the SimBrief ID"); break;
            default: Say("BlueBird stored a different SimBrief ID"); break;
        }
    }

    /// <summary>
    /// FETCH SIMBRIEF OFP on the ACARS dashboard. Afterwards the app fetches the same latest plan itself,
    /// off the UI thread, to learn its weight units: when they differ from the aircraft's, BlueBird is
    /// asking whether to switch them, which the window says, and the EFB load refuses until Accept units
    /// or Keep units answers it (<see cref="Bbs757UnitsQuestion"/>, [B757-4]). The ID is never logged or
    /// spoken.
    /// </summary>
    private async Task FetchSimBriefFlightAsync()
    {
        // Without an ID BlueBird fetches nothing and says nothing.
        string? folder = ResolveWorkFolder();
        long? id = folder == null ? null : Bbs757WorkFolder.ReadSimBriefId(folder);
        if (folder != null && id == null)
        {
            Refuse(SetSimBriefIdFirstText);
            return;
        }

        SetResult("Fetching the SimBrief flight.");
        var result = await RunAsync(Bbs757EfbRunKind.FetchOfp, Bbs757EfbLayout.FetchOfp());
        if (result != Bbs757EfbRunResult.Done)
        {
            SayRunProblem(result);
            return;
        }
        Bbs757WeightUnits? aircraftUnits = folder == null ? null : Bbs757WorkFolder.ReadUnits(folder);
        Bbs757WeightUnits? planUnits = id is long simBriefId ? await PlanUnitsAsync(simBriefId) : null;
        if (_disposed) return;
        _unitsQuestion.AfterFetch(planUnits, aircraftUnits, _fetchPageMs);
        Say(FetchedText(aircraftUnits, planUnits));
    }

    /// <summary>The weight units of the latest plan for the SimBrief ID, by the app's own fetch; null when it cannot be read.</summary>
    private async Task<Bbs757WeightUnits?> PlanUnitsAsync(long simBriefId)
    {
        var ct = Lifetime;
        var (xml, error) = await Task.Run(() => _loadIo.FetchOfpXml(simBriefId, ct), ct);
        if (error != null || xml == null) return null;
        return (await Task.Run(() => Bbs757Loadsheet.ParsePlan(xml), ct))?.Units;
    }

    /// <summary>
    /// YES or NO on "switch the aircraft unit system?". The question is only on screen right after a
    /// fetch, so the answer is pressed only when the last run was a finished fetch, with no run since
    /// (<see cref="Bbs757EfbLayout.MayAnswerUnits"/>), and on the page the fetch ran on. Whether a
    /// question was up cannot be seen, so the window reads BlueBird's unit system before and after
    /// the click and says what is set (<see cref="Bbs757EfbLayout.UnitsAnswerPhrase"/>).
    /// </summary>
    private async Task AnswerUnitsAsync(bool accept)
    {
        if (!Bbs757EfbLayout.MayAnswerUnits(_lastRunKind, _lastRunDone) || _fetchPageMs == null)
        {
            Refuse(FetchFirstText);
            return;
        }
        string? folder = ResolveWorkFolder();
        Bbs757WeightUnits? before = folder == null ? null : Bbs757WorkFolder.ReadUnits(folder);

        var guard = new Bbs757EfbRunGuard(SamePageAsMs: _fetchPageMs);
        var result = await RunAsync(Bbs757EfbRunKind.Units, Bbs757EfbLayout.AnswerUnits(accept), guard);
        // A press that went out answered the question, whatever the run reported after it.
        if (result == Bbs757EfbRunResult.Done || _efb.LastRunMayHaveClicked) _unitsQuestion.Answered();
        if (result != Bbs757EfbRunResult.Done)
        {
            SayRunProblem(result);
            return;
        }
        if (folder == null)
        {
            Say($"{(accept ? "Accept units" : "Keep units")} pressed. {FolderMissingText}, so the units could not be checked.");
            return;
        }

        // BlueBird writes its settings when the unit system changes; watch for that write. A read
        // that lands mid-write comes back null, so it is waited past rather than reported.
        Bbs757WeightUnits? after = Bbs757WorkFolder.ReadUnits(folder);
        var until = DateTime.UtcNow.AddMilliseconds(accept ? UnitsAcceptWaitMs : UnitsKeepWaitMs);
        while ((after == before || after == null) && DateTime.UtcNow < until)
        {
            await Task.Delay(UnitsPollMs, Lifetime);
            after = Bbs757WorkFolder.ReadUnits(folder);
        }
        Say(Bbs757EfbLayout.UnitsAnswerPhrase(before, after));
    }

    /// <summary>
    /// The SimBrief plan's fuel and payload, loaded by BlueBird's own EFB (task 11e): FETCH SIMBRIEF
    /// OFP, then the Aircraft app's FUEL page, IMPORT FROM SIMBRIEF and START BOARDING, each followed
    /// by the defuel question's YES (<see cref="Bbs757EfbLayout.LoadThroughEfb"/>), and BlueBird fuels
    /// and boards at its own pace. The window says "Fueling and boarding started…", and the
    /// definition's load watch (<see cref="Bbs757LoadWatch"/>) says "Fueling complete." and "Boarding
    /// complete." as each finishes, or within three minutes that BlueBird has not started. BlueBird's
    /// completion boxes are left alone (ruling R42).
    /// Nothing is tapped unless: no load is being watched ("The load is already running."); BBS Nest
    /// runs (without it BlueBird ignores every press); BlueBird holds a SimBrief ID, which its own fetch
    /// uses; BA's fetch of that ID's latest plan parses, is for a B752 and fits the tanks and holds,
    /// giving the watch its targets; the plan's weight units are the aircraft's
    /// (<see cref="Bbs757Loadsheet.UnitsRefusal"/>), so BlueBird's fetch never asks to switch them, and no
    /// earlier fetch left that question up (<see cref="Bbs757UnitsQuestion"/>, [B757-4]); and the aircraft
    /// is on the ground (SIM ON GROUND read fresh, as the presets). A run that may have pressed something
    /// without confirming every press is watched, never offered again: what a second START BOARDING press
    /// does during a boarding is unmeasured. The ID is never logged or spoken, and the log has numbers
    /// only. The fetch and the parse run off the UI thread, and each await comes back to it, so the taps,
    /// the watch and the speech go out from it.
    /// </summary>
    private async Task LoadFuelAndPayloadAsync()
    {
        if (_loadIo.WatchRunning())
        {
            Refuse(Bbs757LoadWatch.AlreadyRunningText);
            return;
        }
        if (!_loadIo.NestRunning())
        {
            Refuse(LoadNestMissingText);
            return;
        }
        string? folder = ResolveWorkFolder();
        if (folder == null)
        {
            Refuse($"{FolderMissingText}, so the SimBrief ID and the weight units cannot be checked. Nothing was loaded.");
            return;
        }
        long? id = Bbs757WorkFolder.ReadSimBriefId(folder);
        if (id is not long simBriefId)
        {
            Refuse(SetSimBriefIdFirstText);
            return;
        }

        var ct = Lifetime;
        SetResult("Fetching the SimBrief plan.");
        var (xml, error) = await Task.Run(() => _loadIo.FetchOfpXml(simBriefId, ct), ct);
        if (_disposed) return;
        if (error != null || xml == null)
        {
            Refuse(error ?? PlanUnreadableText);
            return;
        }
        var plan = await Task.Run(() => Bbs757Loadsheet.ParsePlan(xml), ct);
        if (_disposed) return;
        if (plan == null)
        {
            Refuse(PlanUnreadableText);
            return;
        }
        var (loads, refusal) = Bbs757Loadsheet.Compute(plan);
        if (loads == null)
        {
            Refuse(refusal ?? PlanUnreadableText);
            return;
        }
        Bbs757WeightUnits? units = Bbs757WorkFolder.ReadUnits(folder);
        if (Bbs757Loadsheet.UnitsRefusal(plan.Units, units) is { } unitsRefusal)
        {
            Refuse(unitsRefusal);
            return;
        }
        // A question an earlier fetch raised stays up until answered, on the page it was raised on.
        long? pageMs = (await _efb.ReadPageAsync(ct))?.LoadedMs;
        if (_disposed) return;
        if (_unitsQuestion.MayBeOpen(pageMs))
        {
            Refuse(UnitsQuestionOpenText);
            return;
        }

        // The ground is judged last, right before the taps.
        bool? onGround = await _readOnGround(OnGroundReadTimeoutMs, ct);
        if (_disposed) return;
        if (onGround is not bool ground)
        {
            Refuse(LoadGroundUnknownText);
            return;
        }
        if (!ground)
        {
            Refuse(LoadOnGroundOnlyText);
            return;
        }

        SetResult("Starting BlueBird's fuel and payload load.");
        var result = await RunAsync(Bbs757EfbRunKind.Load, Bbs757EfbLayout.LoadThroughEfb());
        if (result != Bbs757EfbRunResult.Done && !_efb.LastRunMayHaveClicked)
        {
            SayRunProblem(result);   // nothing was pressed: trying again is safe
            return;
        }
        bool confirmed = result == Bbs757EfbRunResult.Done;
        Log.Info("BBS757", Bbs757Loadsheet.LogLine(confirmed ? "started" : "started, presses unconfirmed", loads, null));
        bool watching = _loadIo.StartWatch(loads, units);
        if (!watching) Log.Warn("BBS757", "SimBrief load: the load watch did not start.");
        Say(Bbs757LoadWatch.StartedPhrase(confirmed, watching));
    }

    /// <summary>
    /// A cockpit preset: a ground one (Cold and dark to Ready for takeoff) only on the ground, a
    /// flight one (After takeoff to Final approach) only airborne, judged on SIM ON GROUND read fresh
    /// (<see cref="Bbs757EfbLayout.PresetRefusal"/>); nothing is clicked when it cannot be read.
    /// </summary>
    private async Task LoadPresetAsync()
    {
        if (_presetCombo.SelectedItem is not string preset)
        {
            Refuse("Choose a cockpit preset first.");
            return;
        }
        bool? onGround = await _readOnGround(OnGroundReadTimeoutMs, Lifetime);
        if (_disposed) return;
        if (onGround is not bool ground)
        {
            Refuse(GroundStateUnknownText);
            return;
        }
        string? refusal = Bbs757EfbLayout.PresetRefusal(preset, ground);
        if (refusal != null)
        {
            Refuse(refusal);
            return;
        }
        SetResult($"Opening the {preset} preset.");
        var result = await RunAsync(Bbs757EfbRunKind.Preset, Bbs757EfbLayout.LoadPreset(preset));
        if (result != Bbs757EfbRunResult.Done)
        {
            SayRunProblem(result);
            return;
        }
        Say($"Loading {preset}");
    }

    /// <summary>
    /// NOSEWHEEL STEERING to RUDDER (BlueBird's default is REALISTIC, the tiller, which a pilot steering
    /// with the rudder pedals cannot use). The switch TOGGLES, so it is clicked only while BlueBird's
    /// <c>tiller_realistic</c> reads true (<see cref="Bbs757EfbLayout.MayClickSteering"/>): already
    /// rudder says so and clicks nothing; unreadable (no work folder, no such setting) refuses. After the
    /// click the setting is read back (BlueBird writes it at once, measured) and the result said:
    /// "Steering set to rudder", or that it did not change or could not be read. A run that started
    /// and did not report finishing is judged from the setting all the same; it is never run again.
    /// </summary>
    private async Task SetSteeringToRudderAsync()
    {
        string? folder = ResolveWorkFolder();
        if (folder == null)
        {
            Refuse($"{FolderMissingText}, so the steering setting cannot be checked. Nothing was clicked.");
            return;
        }
        bool? tiller = Bbs757WorkFolder.ReadTillerRealistic(folder);
        if (tiller == false)
        {
            Say(Bbs757EfbLayout.SteeringAlreadyRudderText);
            return;
        }
        if (!Bbs757EfbLayout.MayClickSteering(tiller))
        {
            Refuse("BlueBird's steering setting cannot be read, so nothing was clicked.");
            return;
        }

        SetResult("Setting the steering to rudder.");
        var result = await RunAsync(Bbs757EfbRunKind.Steering, Bbs757EfbLayout.SetSteeringToRudder());
        if (result != Bbs757EfbRunResult.Done && !_efb.LastRunMayHaveClicked)
        {
            SayRunProblem(result);
            return;
        }

        bool? after = Bbs757WorkFolder.ReadTillerRealistic(folder);
        var until = DateTime.UtcNow.AddMilliseconds(SteeringWaitMs);
        while (after != false && DateTime.UtcNow < until)
        {
            await Task.Delay(SteeringPollMs, Lifetime);
            after = Bbs757WorkFolder.ReadTillerRealistic(folder);
        }
        Say(Bbs757EfbLayout.SteeringResultPhrase(after));
    }

    /// <summary>
    /// Remove chocks / Set chocks (the owner's INITIAL CHOCKS IN PLACE puts them on at every load, and
    /// the aircraft does not move with them). The CHOCKS tile TOGGLES <c>L:BBS757_vis_chocks</c>, so the
    /// variable is read fresh first (<see cref="Bbs757EfbLayout.MayClickChocks"/>): already where wanted
    /// says so and taps nothing ("Chocks are already removed"); unreadable refuses. Otherwise the tile is
    /// tapped once, the variable read about 1.5 s later, and the result said: "Chocks removed", "Chocks
    /// in place" or "BlueBird did not move the chocks". In flight too (BlueBird shows the tile there).
    /// A run that started and did not report finishing is judged from the variable all the same.
    /// </summary>
    private async Task ChocksAsync(bool wantInPlace)
    {
        var ct = Lifetime;
        double? before = await _loadIo.Read(Bbs757EfbLayout.ChocksVar, LoadReadTimeoutMs, ct);
        if (_disposed) return;
        if (Bbs757EfbLayout.ChocksInPlace(before) is not bool inPlace)
        {
            Refuse(Bbs757EfbLayout.ChocksUnreadableText);
            return;
        }
        if (!Bbs757EfbLayout.MayClickChocks(before, wantInPlace))
        {
            Say(Bbs757EfbLayout.ChocksAlreadyText(inPlace));
            return;
        }

        SetResult(wantInPlace ? "Setting the chocks." : "Removing the chocks.");
        var result = await RunAsync(Bbs757EfbRunKind.Chocks, Bbs757EfbLayout.ToggleChocks());
        if (result != Bbs757EfbRunResult.Done && !_efb.LastRunMayHaveClicked)
        {
            SayRunProblem(result);
            return;
        }
        await Task.Delay(ChocksReadBackDelayMs, ct);
        double? after = await _loadIo.Read(Bbs757EfbLayout.ChocksVar, LoadReadTimeoutMs, ct);
        if (_disposed) return;
        Say(Bbs757EfbLayout.ChocksResultPhrase(wantInPlace, after));
    }

    /// <summary>
    /// Connect ground power / Disconnect ground power. The GPU tile TOGGLES BlueBird's ground power, so
    /// <see cref="Bbs757GroundPower.SwitchAsync"/> reads it fresh first and taps the tile only when it differs
    /// from the wish ([B757-2]'s pattern): already where wanted says so ("Ground power is already connected."),
    /// a disconnect while external power is on line is refused (take it off first), and a connection or external
    /// power that cannot be read taps nothing. After the tap the connection is read until it shows the wish, for
    /// up to six seconds, and the result said: "Ground power connected…", "Ground power disconnected." or
    /// "BlueBird did not connect the ground power.". A run that started and did not report finishing is judged
    /// from the variable all the same, never run again; one that tapped nothing is said as any run problem. The
    /// definition's Ground Power row does not say the change a second time while the run lasts
    /// (<see cref="Bbs757LoadIo.ArmGroundPowerEcho"/>), so one press is one utterance (CORE-7).
    /// </summary>
    private async Task GroundPowerAsync(bool connect)
    {
        var io = new Bbs757GroundPowerIo
        {
            Read = (key, token) => _loadIo.Read(key, GroundPowerReadTimeoutMs, token),
            Run = async (seq, _) =>
            {
                SetResult(connect ? "Connecting the ground power." : "Disconnecting the ground power.");
                var result = await RunAsync(Bbs757EfbRunKind.GroundPower, seq);
                return (result, _efb.LastRunMayHaveClicked);
            },
            Delay = Task.Delay,
            NowMs = () => Environment.TickCount64,
            ArmEcho = _loadIo.ArmGroundPowerEcho,
            SettleEcho = _loadIo.SettleGroundPowerEcho,
        };
        var outcome = await Bbs757GroundPower.SwitchAsync(connect, io, Lifetime);
        // One debug line per run, so a failed in-sim check can be read from debug.log: the wish, what came of it,
        // and for a run that tapped nothing how the run ended. Names only, no text from the sim or the EFB.
        string wish = connect ? "connect" : "disconnect";
        string runEnd = outcome.Kind == Bbs757GroundPowerOutcomeKind.RunProblem ? $", run {outcome.Run}" : "";
        Log.Debug("BBS757", $"Ground power {wish}: outcome {outcome.Kind}{runEnd}.");
        if (_disposed) return;
        switch (outcome.Kind)
        {
            case Bbs757GroundPowerOutcomeKind.Refused: Refuse(outcome.Text!); return;
            case Bbs757GroundPowerOutcomeKind.RunProblem: SayRunProblem(outcome.Run); return;
            default: Say(outcome.Text!); return;   // Already and Result: said once
        }
    }

    /// <summary>A quick start tile, which has nothing to read back: clicked, then "{name} pressed".</summary>
    private async Task QuickStartAsync(string name)
    {
        var result = await RunAsync(Bbs757EfbRunKind.QuickStart, Bbs757EfbLayout.QuickStart(name));
        if (result != Bbs757EfbRunResult.Done)
        {
            SayRunProblem(result);
            return;
        }
        Say($"{name} pressed");
    }

    // ------------------------------------------------------------------
    // Show / hide
    // ------------------------------------------------------------------

    public void ShowForm()
    {
        _previousWindow = GetForegroundWindow();
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        _navigraphButton.Focus();
        UpdateStatus();
    }

    private void HideAndRestore()
    {
        Hide();
        if (_previousWindow != IntPtr.Zero)
            SetForegroundWindow(_previousWindow);
    }

    // The EFB client checks its link only while the window is shown; the status box refreshes then.
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_disposed) return;
        _efb.SetActive(Visible);
        if (Visible) _statusTimer.Start();
        else _statusTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _statusTimer.Stop();
            _statusTimer.Dispose();
            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            _lifetime.Dispose();
            _efb.SetActive(false);
            // The clients are MainForm's: never disposed here.
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// What the EFB window needs from the app beyond the tablet, as MainForm's seams: the SimBrief fuel
/// and payload load's plan fetch and the definition's load watch, fresh reads (the chocks, the ground
/// power), and the definition's ground power echo.
/// Each is called on the UI thread, except <see cref="FetchOfpXml"/>, which the window runs off it.
/// </summary>
public sealed class Bbs757LoadIo
{
    /// <summary>A registered variable read fresh within the given milliseconds; null when it cannot be read.</summary>
    public required Func<string, int, CancellationToken, Task<double?>> Read { get; init; }

    /// <summary>The latest OFP's XML for a SimBrief ID, or a friendly error (<c>SimBriefService.FetchLatestOfpXmlAsync</c>).</summary>
    public required Func<long, CancellationToken, Task<(string? Xml, string? Error)>> FetchOfpXml { get; init; }

    /// <summary>Whether the definition's load watch runs (<c>BBS757Definition.LoadWatchRunning</c>).</summary>
    public required Func<bool> WatchRunning { get; init; }

    /// <summary>Whether BBS Nest runs now (<c>Bbs757Nest.IsRunning</c>): without it BlueBird ignores every press.</summary>
    public required Func<bool> NestRunning { get; init; }

    /// <summary>
    /// Starts the definition's load watch toward the loads, speaking in the aircraft's units
    /// (<c>BBS757Definition.StartLoadWatch</c>); false when it did not start.
    /// </summary>
    public required Func<Bbs757Loads, Bbs757WeightUnits?, bool> StartWatch { get; init; }

    /// <summary>
    /// Opens the definition's ground power echo right before the window's ground power taps
    /// (<c>BBS757Definition.ArmGroundPowerEcho</c>): the Ground Power row then does not say a second time the
    /// change the window says itself.
    /// </summary>
    public required Action ArmGroundPowerEcho { get; init; }

    /// <summary>
    /// Ends it when the run is over, with the last ground power the window read, null when none
    /// (<c>BBS757Definition.SettleGroundPowerEcho</c>).
    /// </summary>
    public required Action<double?> SettleGroundPowerEcho { get; init; }
}
