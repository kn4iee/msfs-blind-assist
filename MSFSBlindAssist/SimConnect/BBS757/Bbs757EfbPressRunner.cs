using System.Drawing;
using System.Globalization;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// What a press on the tablet window needs (plan 18, Task 8), as delegates: the bridge builds it over the pop-out's captures, its own
/// settled read, the EFB agent and the app's seams, and the tests stand a fake tablet, file and clock behind it. Every member is
/// called on the bridge's loop, one press at a time; the bridge marshals to the UI thread the ones that need it (the echoes, the
/// hook).
/// </summary>
public sealed class Bbs757EfbPressIo
{
    /// <summary>A fresh capture of the tablet; null when the pop-out cannot be captured. The caller disposes it.</summary>
    public required Func<Bitmap?> Capture { get; init; }

    /// <summary>Whether two captures show the same thing, rendering noise aside (<see cref="Bbs757EfbFrameCompare.Same"/>).</summary>
    public required Func<Bitmap, Bitmap, bool> Same { get; init; }

    /// <summary>Whether two captures' top bars show the same thing (their <c>Bbs757EfbBridge.TopBarSignature</c>s do not differ):
    /// <see cref="Same"/> leaves the top bar out with its clock, so a press on a top-bar control checks it apart. Only before a tap,
    /// never in the wait for a tap's change (a countdown ticking right after a tap is no change the tap made).</summary>
    public required Func<Bitmap, Bitmap, bool> SameTopBar { get; init; }

    /// <summary>The nav data cycle BlueBird has installed now (<c>NavigationData\cycle.json</c>), read before a nav data press's tap;
    /// null when none is installed or it cannot be read.</summary>
    public required Func<int?> ReadInstalledCycle { get; init; }

    /// <summary>Reads the tablet now, settled, as the bridge's loop reads it, and pushes it to the window when it says something new;
    /// the page read, or null when nothing could be read (the window then shows why).</summary>
    public required Func<CancellationToken, Task<Bbs757EfbScreenModel?>> Read { get; init; }

    /// <summary>One tap at a tablet point: true when it went out (it may have landed), false when nothing was tapped.</summary>
    public required Func<Point, CancellationToken, Task<bool>> Tap { get; init; }

    /// <summary>One mouse-wheel step on the page (positive scrolls down).</summary>
    public required Func<int, CancellationToken, Task> Wheel { get; init; }

    /// <summary>A BlueBird L:var read now through the EFB agent; null when unread.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadVar { get; init; }

    /// <summary>A variable the definition registers, read fresh (the ground power's connection); null when unread.</summary>
    public required Func<string, CancellationToken, Task<double?>> ReadRegistered { get; init; }

    /// <summary>A setting read now from BlueBird's file; null when it cannot be read.</summary>
    public required Func<Bbs757Setting, CancellationToken, Task<Bbs757Value?>> ReadSetting { get; init; }

    /// <summary>Says a text as the window's alert (spoken at once, once).</summary>
    public required Action<string> Alert { get; init; }

    /// <summary>An awaitable wait.</summary>
    public required Func<int, CancellationToken, Task> Delay { get; init; }

    /// <summary>The clock in milliseconds.</summary>
    public required Func<long> NowMs { get; init; }

    /// <summary>Opens and ends the definition's echo of a ground service's row around the tap on its tile.</summary>
    public required Action<Bbs757GroundService> ArmServiceEcho { get; init; }

    public required Action<Bbs757GroundService, double?> SettleServiceEcho { get; init; }

    /// <summary>Opens and ends plan 07's ground power echo around the tap on the GPU tile.</summary>
    public required Action ArmGroundPowerEcho { get; init; }

    public required Action<double?> SettleGroundPowerEcho { get; init; }

    /// <summary>A hook the press started (<see cref="Bbs757EfbExtras.HookFor"/>), right after its tap went out, with the nav data
    /// cycle installed before that tap for UPDATE or REMOVE (null for any other hook, or when none was installed).</summary>
    public required Action<Bbs757EfbHook, int?> Hooked { get; init; }
}

/// <summary>
/// A press the pilot made in the tablet window: the tablet control (<paramref name="Item"/>), the page the window showed when it was
/// pressed (<paramref name="Page"/>), and the frame that page was read from (<paramref name="Frame"/>; null when no longer kept, and
/// the tablet is then read again before any tap).
/// </summary>
public sealed record Bbs757EfbPressed(Bbs757EfbItem Item, Bbs757EfbScreenModel Page, Bitmap? Frame);

/// <summary>
/// The tablet window's presses (plan 18, Task 8), on the bridge's loop, one at a time, in the order the plan gives them:
/// <list type="number">
/// <item>Where: a fresh capture compared with the frame the page was read from (the page; for a control of the top bar, which the
/// page's compare leaves out, the bar too). The same: the control is tapped where the reader found it. Different: the page is read
/// again (settled) and the control found in it (<see cref="Bbs757EfbPress.Relocate"/>, from the page the pilot pressed on), the new
/// page pushed, and the control tapped where it is now. Gone, turned into words that are no control (START BOARDING become
/// "Boarding… 10 min"), or an answer whose question was answered meanwhile: nothing tapped, "{label} is no longer on the tablet."
/// Under a question that came up meanwhile, an answer to another question included (every question's YES is keyed alike):
/// nothing tapped and nothing said, since the read that found the question said it. A side-menu entry or a home-screen app whose
/// page the tablet shows already (pressed twice), and the window's own Home on the home screen: nothing tapped, nothing said.</item>
/// <item>Whether: a switch or check box only when its state read differs from the wish (<see cref="Bbs757EfbPress.NeedsTap"/>), a
/// slider only when it does not read the choice asked for. Nothing else refuses a press (the owner's ruling of 2026-10-29).</item>
/// <item>The tap, and right after it the press's hook (<see cref="Bbs757EfbExtras.HookFor"/>); then a wait for the picture to change
/// (at most <see cref="ChangeMaxMs"/>), and a read.</item>
/// <item>What it came to: a navigation (Home, a side-menu entry, a home-screen app) is read again until the page name is the one
/// tapped (at most <see cref="NavigateMaxMs"/>); any other button says its new words when the read after the tap finds it with
/// other words, whatever its key or kind now, and that it changed nothing when nothing on the page did
/// (<see cref="Bbs757EfbPress.ButtonOutcome"/>; the window says neither); a Settings switch or slider is judged by BlueBird's file
/// (at most <see cref="FileMaxMs"/>, the stored value of what was asked for), and anything with no setting behind it by the page (a
/// switch within <see cref="FollowMaxMs"/>); a callout the press switched off with it is named. A switch, check box or slider that
/// landed says nothing: the screen reader said its new state. The exception is a switch of two choices (the CPDLC network, the
/// units, the EHSI up reference): it says its new choice once, since the screen reader can say only checked or not checked of its
/// check box, which names no word.</item>
/// </list>
/// A tile of a ground service or the GPU opens the definition's echo for its row before the tap and ends it with the state read
/// after, so the row does not say what the press says of the tile's new words (CORE-7). Everything said is an alert, in plain words
/// that no Windows culture changes.
/// </summary>
public sealed class Bbs757EfbPressRunner
{
    /// <summary>After a tap, the longest wait for the picture to change. Measured 2026-10-27: a read 700 ms after a tap caught the
    /// old page on 4 of 5 app taps, and an unchanged old page looks perfectly still.</summary>
    public const int ChangeMaxMs = 3000;

    /// <summary>How long a navigation is read again for: measured 2026-10-27, the page follows the icon's press highlight 0.4-2.4 s
    /// after the first change.</summary>
    public const int NavigateMaxMs = 4000;

    /// <summary>How long a switch or check box with no setting behind it is read again for, from its tap.</summary>
    public const int FollowMaxMs = 4000;

    /// <summary>How long BlueBird's file is read for a Settings press. Measured 2026-10-27: its taps were in the file when read
    /// 1.3 s later; the 2,000 ms read is the margin.</summary>
    public const int FileMaxMs = 2000;

    private const int ChangePollMs = 100, AfterTapMs = 700, NavigatePollMs = 500, FollowPollMs = 300, FilePollMs = 250;

    /// <summary>How far a slider with no choices may sit from the percent asked for and still be set.</summary>
    private const double PercentSlack = 10;

    /// <summary>The window's own Home button (<see cref="Bbs757EfbElements"/>), on every app page.</summary>
    private const string HomeKey = "btn:HOME";

    private readonly Bbs757EfbPressIo _io;

    public Bbs757EfbPressRunner(Bbs757EfbPressIo io) => _io = io;

    // ------------------------------------------------------------------ buttons, tiles, menus

    /// <summary>
    /// A button, tile, side-menu entry, tab, top bar control or question answer pressed. A switch or check box clicked is set to
    /// the other state (<see cref="SwitchAsync"/>); a slider clicked does nothing (the window sets it).
    /// </summary>
    public async Task ClickAsync(Bbs757EfbPressed pressed, CancellationToken ct)
    {
        switch (pressed.Item.Kind)
        {
            case Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox:
                await SwitchAsync(pressed, pressed.Item.On != true, ct);
                return;
            case Bbs757EfbKind.Slider:
                return;
        }

        using var target = await LocateAsync(pressed, ct);
        if (target == null) return;
        var item = target.Item;

        // A tile's words from its key: "btn:|FUEL TRUCK@56,28" is FUEL TRUCK, the state word left out; "" for any other control.
        string words = Bbs757EfbScreen.KeyWords(item.Key);
        var service = Bbs757GroundServices.ForTile(words);
        bool gpu = service == null && words.StartsWith("GPU", StringComparison.Ordinal);
        if (service != null) _io.ArmServiceEcho(service);
        if (gpu) _io.ArmGroundPowerEcho();
        bool tapped = false;
        try
        {
            tapped = await TapAsync(item, target, ct);
            if (!tapped) return;
            await WaitForChangeAsync(target.Picture, ct);
            var after = await _io.Read(ct);
            if (after == null) return;   // nothing read: the window says why

            if (NavigationTarget(item) is { } page)
                await ArriveAsync(after, page, ct);
            // The control's new words, or that the press changed nothing: said here, once, since the window says neither.
            else if (Bbs757EfbPress.ButtonOutcome(target.Page, after, item) is { } outcome)
                _io.Alert(outcome);
        }
        finally
        {
            // The state read after the tap is the row's again: the echo ends with it (with nothing read when nothing was tapped).
            if (service != null)
                _io.SettleServiceEcho(service, tapped && !ct.IsCancellationRequested ? await _io.ReadVar(service.StateVar, ct) : null);
            if (gpu)
                _io.SettleGroundPowerEcho(tapped && !ct.IsCancellationRequested ? await _io.ReadRegistered(Bbs757SharedKeys.GpuConnected, ct) : null);
        }
    }

    /// <summary>The page a navigation tap asks for: Home for the window's Home button, the entry's words for a side-menu entry or a
    /// home-screen app; null for any other control (a tab changes the list under it, not the page's name).</summary>
    private static string? NavigationTarget(Bbs757EfbItem item) =>
        item.Key == HomeKey ? "Home"
        : item.Kind == Bbs757EfbKind.Menu && (item.Key.StartsWith("menu:", StringComparison.Ordinal) || item.Key.StartsWith("app:", StringComparison.Ordinal))
            ? item.Text
            : null;

    /// <summary>Reads the tablet again until it shows <paramref name="page"/>, for <see cref="NavigateMaxMs"/>, and says so when it
    /// never does.</summary>
    private async Task ArriveAsync(Bbs757EfbScreenModel after, string page, CancellationToken ct)
    {
        long until = _io.NowMs() + NavigateMaxMs;
        var now = after;
        while (!PageIs(now, page) && _io.NowMs() < until)
        {
            await _io.Delay(NavigatePollMs, ct);
            if (await _io.Read(ct) is not { } next) return;   // nothing read: the window says why
            now = next;
        }
        if (PageIs(now, page)) return;
        Log.Debug("BBS757", $"EFB reader: tapped '{page}', page reads '{now.PageName}'");
        _io.Alert(NotShownText(page));
    }

    /// <summary>Whether the page read is the one a navigation tap asked for (an app's or a section's name).</summary>
    public static bool PageIs(Bbs757EfbScreenModel? m, string expect)
    {
        if (m == null) return false;
        string want = expect.Replace(" (current page)", "").Trim();
        return m.PageName.Equals(want, StringComparison.OrdinalIgnoreCase)
            || m.PageName.StartsWith(want + ",", StringComparison.OrdinalIgnoreCase)
            || m.PageName.EndsWith(", " + want, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A navigation the tablet never showed.</summary>
    public static string NotShownText(string page) => "The tablet did not show " + page + ". Try again, or use Read the tablet again.";

    // ------------------------------------------------------------------ switches and check boxes

    /// <summary>
    /// A switch or check box set to <paramref name="wantOn"/>: tapped only when the tablet does not show the wish already. A row of
    /// the Settings pages that is one of BlueBird's settings (<see cref="Bbs757EfbSettingsMatch.SettingFor"/>) is judged by its file:
    /// a switch must store its wished state; a setting of two choices (the CPDLC network, the units, the EHSI up reference) the
    /// choice that was not set before the tap, since the pill's side names no word the same way on every row (measured 2026-10-29:
    /// EHSI's HDG reads on, CPDLC's Hoppie off); the TOD pause distance switched off its empty box. A row that is no setting (a label
    /// misread, a checklist line), or a file that cannot be read, is judged by the page. Not followed: "{label} did not change."
    /// Followed, a switch or check box says nothing, since the screen reader said its new state, except a switch of two choices:
    /// the screen reader can say only checked or not checked of its check box, which names no word, so the press says its new choice
    /// once, "{label}: {word}" ("CPDLC network: SayIntentions"). The word is the catalog's for the choice that was not set before the
    /// tap, for a setting of two choices whose choice before is known (from the row's word, else the file), whether the file or the
    /// page followed; else, for a row that is no setting or whose choice before is unknown, the word the page shows now
    /// (<see cref="Bbs757EfbPress.NewChoiceWord"/>), when it is another than the one it showed before. Switched on, a callout of an
    /// exclusive group names each other member that read on before and is off now: "{label} turned off".
    /// </summary>
    public async Task SwitchAsync(Bbs757EfbPressed pressed, bool wantOn, CancellationToken ct)
    {
        if (pressed.Item.Kind is not (Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox)) return;
        using var target = await LocateAsync(pressed, ct);
        if (target == null) return;
        var item = target.Item;
        if (!Bbs757EfbPress.NeedsTap(item, wantOn)) return;   // it shows the wish already: a tap would undo it

        var setting = Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, item);
        var wish = setting == null ? null : await FileWishAsync(setting, item, wantOn, ct);
        IReadOnlyList<Bbs757Setting> onBefore = setting != null && wantOn ? await GroupOnAsync(setting, target.Page, ct) : Array.Empty<Bbs757Setting>();

        if (!await TapAsync(item, target, ct)) return;
        long tappedAt = _io.NowMs();
        await WaitForChangeAsync(target.Picture, ct);
        if (await _io.Read(ct) is not { } after) return;   // nothing read: the window says why

        var verdict = setting != null && wish != null ? await FileHoldsAsync(setting, wish.Accepts, ct) : Verdict.Unread;
        Bbs757EfbItem? shown = null;   // the switch as the page shows it once it followed
        if (verdict == Verdict.Unread) (verdict, shown) = await PageFollowsAsync(target.Page, after, item, wantOn, tappedAt, ct);
        if (verdict == Verdict.Differs)
        {
            _io.Alert(Bbs757EfbPress.NotChangedText(item.Text));
            return;
        }
        if (verdict != Verdict.Holds) return;

        // A callout is an On/Off switch, so a switch of two choices never has a callout to name: one alert at most.
        if (NewChoice(item, setting, wish, shown) is { } choice)
            _io.Alert(Bbs757EfbPress.ChoiceText(item.Text, choice));
        else if (onBefore.Count > 0 && await TurnedOffAsync(onBefore, after, ct) is { } turnedOff)
            _io.Alert(turnedOff);
    }

    /// <summary>
    /// The new choice a switch of two choices says once its tap landed; null for any other switch (an On/Off switch, the TOD pause
    /// distance, a check box), which says nothing. A matched setting of two choices whose choice before the tap is known
    /// (<paramref name="wish"/> carries the word): the catalog's word for the other choice, so a choice the tablet shows no word for
    /// (EFB brightness mode's Manual) is named too. Else, for a row that is no setting (a label misread) or a setting whose choice
    /// before was unknown and whose file could not be read, the word the page shows now (<paramref name="shown"/>, the control found
    /// again by the page that followed), when it differs from the word the row read before (<see cref="Bbs757EfbPress.NewChoiceWord"/>).
    /// </summary>
    private static string? NewChoice(Bbs757EfbItem item, Bbs757Setting? setting, Wish? wish, Bbs757EfbItem? shown)
    {
        if (item.Kind != Bbs757EfbKind.Toggle) return null;
        if (wish?.Word is { } word) return word;
        return setting is null or { Kind: Bbs757SettingKind.Choice } ? Bbs757EfbPress.NewChoiceWord(item.Value, shown?.Value) : null;
    }

    /// <summary>What a switch's tap must leave in BlueBird's file: <see cref="Accepts"/> says whether a stored value is it, and
    /// <see cref="Word"/> is, for a setting of two choices, the catalog's word for the choice the tap switches to (null for any other
    /// setting).</summary>
    private sealed record Wish(Func<Bbs757Value, bool> Accepts, string? Word = null);

    /// <summary>What BlueBird's file must hold once a switch's tap landed; null when the setting names no value to judge by.</summary>
    private async Task<Wish?> FileWishAsync(Bbs757Setting setting, Bbs757EfbItem row, bool wantOn, CancellationToken ct)
    {
        switch (setting.Kind)
        {
            case Bbs757SettingKind.OnOff:
                return Bbs757EfbSettingsMatch.StoredFor(setting, wantOn, null) is { } on ? new Wish(v => v == on) : null;
            case Bbs757SettingKind.Choice when setting.Choices.Count == 2:
            {
                // The choice set before the tap: the word the row read, else the file's.
                int before = Bbs757EfbSettingsMatch.ChoiceIndex(setting, row.Value);
                if (before < 0 && await _io.ReadSetting(setting, ct) is { } stored)
                    before = setting.Choices.ToList().FindIndex(c => c.Stored == stored);
                if (before < 0) return null;
                var other = setting.Choices[1 - before];
                return new Wish(v => v == other.Stored, other.Word);
            }
            case Bbs757SettingKind.Number when setting.EmptyStored is { } empty:
                // The TOD pause distance's switch: off is its empty box, on any distance.
                return new Wish(wantOn ? v => v != empty : v => v == empty);
            default:
                return null;
        }
    }

    /// <summary>The other members of the setting's exclusive group that read on before the tap.</summary>
    private async Task<IReadOnlyList<Bbs757Setting>> GroupOnAsync(Bbs757Setting setting, Bbs757EfbScreenModel page, CancellationToken ct)
    {
        var on = new List<Bbs757Setting>();
        foreach (var other in Bbs757EfbSettingsMatch.SwitchedOffWith(setting))
        {
            if (await IsOnAsync(other, page, ct) == true) on.Add(other);
        }
        return on;
    }

    /// <summary>"2500 turned off" for each of <paramref name="onBefore"/> that is off now; null when none is.</summary>
    private async Task<string?> TurnedOffAsync(IReadOnlyList<Bbs757Setting> onBefore, Bbs757EfbScreenModel after, CancellationToken ct)
    {
        var off = new List<string>();
        foreach (var other in onBefore)
        {
            if (await IsOnAsync(other, after, ct) == false) off.Add(Bbs757EfbSettingsMatch.TurnedOffText(other));
        }
        return off.Count == 0 ? null : string.Join(", ", off);
    }

    /// <summary>Whether a switch setting is on: from BlueBird's file, else from its row on <paramref name="page"/>; null when
    /// neither tells.</summary>
    private async Task<bool?> IsOnAsync(Bbs757Setting setting, Bbs757EfbScreenModel page, CancellationToken ct)
    {
        if (await _io.ReadSetting(setting, ct) is { Type: Bbs757ValueType.Bool } stored) return stored.Bool;
        var row = page.Items.FirstOrDefault(i => Bbs757EfbSettingsMatch.SettingFor(page.PageName, i)?.Path == setting.Path);
        return row?.On;
    }

    private enum Verdict { Holds, Differs, Unread }

    /// <summary>BlueBird's file read for <see cref="FileMaxMs"/> until it holds the wish: Holds, Differs (read, never the wish), or
    /// Unread (never read: the page decides).</summary>
    private async Task<Verdict> FileHoldsAsync(Bbs757Setting setting, Func<Bbs757Value, bool> wish, CancellationToken ct)
    {
        long until = _io.NowMs() + FileMaxMs;
        bool read = false;
        while (true)
        {
            if (await _io.ReadSetting(setting, ct) is { } stored)
            {
                if (wish(stored)) return Verdict.Holds;
                read = true;
            }
            if (_io.NowMs() >= until) return read ? Verdict.Differs : Verdict.Unread;
            await _io.Delay(FilePollMs, ct);
        }
    }

    /// <summary>The page read again until the switch shows the wish, for <see cref="FollowMaxMs"/> from its tap: Holds, with the
    /// switch as that read shows it (<see cref="Bbs757EfbPress.Relocate"/>), Differs, or Unread (a read failed: the window says
    /// why).</summary>
    private async Task<(Verdict Verdict, Bbs757EfbItem? Shown)> PageFollowsAsync(Bbs757EfbScreenModel page, Bbs757EfbScreenModel after,
        Bbs757EfbItem item, bool wantOn, long tappedAt, CancellationToken ct)
    {
        long until = tappedAt + FollowMaxMs;
        var now = after;
        while (true)
        {
            if (Bbs757EfbPress.Relocate(page, now, item) is { } found && found.On == wantOn) return (Verdict.Holds, found);
            if (_io.NowMs() >= until) return (Verdict.Differs, null);
            await _io.Delay(FollowPollMs, ct);
            if (await _io.Read(ct) is not { } next) return (Verdict.Unread, null);
            now = next;
        }
    }

    // ------------------------------------------------------------------ sliders

    /// <summary>
    /// A slider set to <paramref name="value"/>: one of its choices (<see cref="Bbs757EfbElements.OptionsFor"/>'s words), or else a
    /// percent along its track (a slider with no choices, and one with choices whose current one was not read, which the window
    /// shows as a 0-100 range). A choice: the choice's place tapped (<see cref="Bbs757EfbPress.ChoiceX"/>), read back, and stepped
    /// toward it (<see cref="Bbs757EfbPress.NextChoiceX"/>), at most <see cref="Bbs757EfbPress.MaxSliderTaps"/> taps; then judged by
    /// BlueBird's file when the row is one of its settings (the stored value of the choice asked for), else by the page. A percent:
    /// one tap at its place, judged by where the knob is. Not set, or a value that is neither: "{label} could not be set to {value}."
    /// </summary>
    public async Task SliderAsync(Bbs757EfbPressed pressed, string value, CancellationToken ct)
    {
        if (pressed.Item.Kind != Bbs757EfbKind.Slider) return;
        var options = Bbs757EfbElements.OptionsFor(pressed.Item, pressed.Page.PageName) ?? Array.Empty<string>();
        int wanted = -1;
        for (int index = 0; index < options.Count && wanted < 0; index++)
            if (string.Equals(options[index], value, StringComparison.OrdinalIgnoreCase)) wanted = index;
        if (wanted >= 0)
            await ChoiceAsync(pressed, options, wanted, ct);
        else
            await PercentAsync(pressed, value, ct);
    }

    private async Task ChoiceAsync(Bbs757EfbPressed pressed, IReadOnlyList<string> options, int wanted, CancellationToken ct)
    {
        using var target = await LocateAsync(pressed, ct);
        if (target?.Item.Slider is not { } track) return;
        var row = target.Item;
        var page = target.Page;
        int read = Bbs757EfbElements.CurrentChoice(row, page.PageName, options);
        if (read == wanted) return;   // it shows the wish already

        int x = Bbs757EfbPress.ChoiceX(track, wanted, options.Count);
        for (int taps = 1; ; taps++)
        {
            if (!await TapAsync(row with { Hit = new Point(x, row.Hit.Y) }, target, ct)) return;
            await WaitForChangeAsync(target.Picture, ct);
            if (await _io.Read(ct) is not { } after) return;   // nothing read: the window says why
            var now = Bbs757EfbPress.Relocate(page, after, row);
            read = now == null ? -1 : Bbs757EfbElements.CurrentChoice(now, after.PageName, options);
            if (read == wanted || Bbs757EfbPress.NextChoiceX(track, x, wanted, read, options.Count, taps) is not int next) break;
            x = next;
            page = after;
            row = now!;
            target.Retake(_io.Capture());
        }

        var setting = Bbs757EfbSettingsMatch.SettingFor(target.Page.PageName, target.Item);
        var verdict = setting != null && Bbs757EfbSettingsMatch.StoredFor(setting, null, options[wanted]) is { } stored
            ? await FileHoldsAsync(setting, v => v == stored, ct)
            : Verdict.Unread;
        if (verdict == Verdict.Holds || (verdict == Verdict.Unread && read == wanted)) return;
        _io.Alert(Bbs757EfbPress.CouldNotSetText(target.Item.Text, options[wanted]));
    }

    private async Task PercentAsync(Bbs757EfbPressed pressed, string value, CancellationToken ct)
    {
        // The window's range control writes the number, invariant. Anything else is no place on the track: nothing is tapped.
        if (!NumberParsing.TryParseExternal(value, out double percent) || !double.IsFinite(percent))
        {
            _io.Alert(Bbs757EfbPress.CouldNotSetText(pressed.Item.Text, value));
            return;
        }
        percent = Math.Clamp(percent, 0, 100);

        using var target = await LocateAsync(pressed, ct);
        if (target?.Item.Slider is not { } track) return;
        int x = track.TrackLeft + (int)Math.Round(percent / 100 * (track.TrackRight - track.TrackLeft));
        if (!await TapAsync(target.Item with { Hit = new Point(x, target.Item.Hit.Y) }, target, ct)) return;
        await WaitForChangeAsync(target.Picture, ct);
        if (await _io.Read(ct) is not { } after) return;   // nothing read: the window says why

        if (Bbs757EfbPress.Relocate(target.Page, after, target.Item)?.Slider is { } now && Math.Abs(KnobPercent(now) - percent) <= PercentSlack)
            return;
        _io.Alert(Bbs757EfbPress.CouldNotSetText(target.Item.Text, percent.ToString("0.#", CultureInfo.InvariantCulture) + " percent"));
    }

    private static double KnobPercent(Bbs757EfbSlider s) =>
        s.TrackRight > s.TrackLeft ? 100.0 * (s.KnobX - s.TrackLeft) / (s.TrackRight - s.TrackLeft) : 0;

    // ------------------------------------------------------------------ scrolling

    /// <summary>One wheel step on the page, the change waited for, and the page read.</summary>
    public async Task ScrollAsync(bool down, CancellationToken ct)
    {
        using var before = _io.Capture();
        await _io.Wheel(down ? Bbs757EfbScreen.WheelStep : -Bbs757EfbScreen.WheelStep, ct);
        await WaitForChangeAsync(before, ct);
        await _io.Read(ct);
    }

    // ------------------------------------------------------------------ where, and the tap

    /// <summary>A control where it is to be tapped: the control, the page it is on there, and the picture the tap's change is waited
    /// from (owned).</summary>
    private sealed class Target : IDisposable
    {
        public Target(Bbs757EfbItem item, Bbs757EfbScreenModel page, Bitmap? picture)
        {
            Item = item;
            Page = page;
            Picture = picture;
        }

        public Bbs757EfbItem Item { get; }
        public Bbs757EfbScreenModel Page { get; }
        public Bitmap? Picture { get; private set; }

        /// <summary>A newer picture to wait from (a slider's next step).</summary>
        public void Retake(Bitmap? picture)
        {
            Picture?.Dispose();
            Picture = picture;
        }

        public void Dispose() => Picture?.Dispose();
    }

    /// <summary>
    /// The pressed control where it is to be tapped: a fresh capture compared with the frame the page was read from (the page's
    /// compare; for a control of the top bar, which that compare leaves out with its clock, the top bar's too); the same, the control
    /// as read; else the page read again and the control found in it. Null, with nothing tapped, when it is gone or no longer a control
    /// ("{label} is no longer on the tablet."), when the question an answer was pressed on was answered meanwhile (said the same way),
    /// when a question came up over the page, another question in an answer's place included (the read that found it said it, and
    /// nothing is said over it), when the pressed navigation is done already (the tablet shows the page a side-menu entry or a
    /// home-screen app asks for, or Home for the window's Home: nothing said), and when nothing could be read (the window says why).
    /// </summary>
    private async Task<Target?> LocateAsync(Bbs757EfbPressed pressed, CancellationToken ct)
    {
        var picture = _io.Capture();
        if (picture != null && pressed.Frame != null && _io.Same(picture, pressed.Frame)
            && (!pressed.Page.TopBar.Contains(pressed.Item) || _io.SameTopBar(picture, pressed.Frame)))
            return new Target(pressed.Item, pressed.Page, picture);
        picture?.Dispose();

        if (await _io.Read(ct) is not { } now) return null;
        var found = Find(pressed, now, out bool gone);
        if (found != null) return new Target(found, now, _io.Capture());
        if (gone) _io.Alert(Bbs757EfbPress.GoneText(pressed.Item.Text));
        return null;
    }

    /// <summary>The pressed control on the page read again, or null; <paramref name="gone"/> says when that is said.</summary>
    private static Bbs757EfbItem? Find(Bbs757EfbPressed pressed, Bbs757EfbScreenModel now, out bool gone)
    {
        gone = false;
        var item = pressed.Item;
        if (item.Key == HomeKey)
            return now.App == "HOME" || now.Question != null ? null : item;

        // A side-menu entry or a home-screen app whose page the tablet shows already (pressed twice: the first press took it
        // there): its tap is done. It is no longer on the page it was pressed on, but saying so would be wrong.
        if (NavigationDone(pressed, now)) return null;

        // Every question's answers are keyed alike (q:YES, q:NO): an answer is the one pressed only under the same question. Under
        // another one, the read that found it said it, so nothing is said over it; with none up, the answer is gone.
        if (pressed.Page.Question?.Buttons.Contains(item) == true)
        {
            if (now.Question != null && !Bbs757EfbPress.SameQuestion(pressed.Page.Question, now.Question)) return null;
            var answer = now.Question != null ? Bbs757EfbPress.Relocate(pressed.Page, now, item) : null;
            gone = answer == null || now.Question?.Buttons.Contains(answer) != true;
            return gone ? null : answer;
        }

        var found = Bbs757EfbPress.Relocate(pressed.Page, now, item);
        if (found == null || found.Kind is not (Bbs757EfbKind.Button or Bbs757EfbKind.Menu or Bbs757EfbKind.Toggle
                or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Slider))
        {
            gone = true;
            return null;
        }
        // Under a question the tablet takes no tap on its page: only the top bar presses (the answers were found above).
        if (now.Question != null && !now.TopBar.Contains(found)) return null;
        return found;
    }

    /// <summary>Whether the tablet shows the page a side-menu entry or a home-screen app was pressed for: the entry's section of
    /// the app it was pressed in ("Aircraft, Fuel" for Fuel pressed on "Aircraft, Ground services"), or the app.</summary>
    private static bool NavigationDone(Bbs757EfbPressed pressed, Bbs757EfbScreenModel now)
    {
        var item = pressed.Item;
        if (item.Kind != Bbs757EfbKind.Menu) return false;
        if (item.Key.StartsWith("app:", StringComparison.Ordinal))
            return string.Equals(now.App, item.Key.Substring("app:".Length), StringComparison.OrdinalIgnoreCase);
        return item.Key.StartsWith("menu:", StringComparison.Ordinal)
            && string.Equals(now.App, pressed.Page.App, StringComparison.OrdinalIgnoreCase) && PageIs(now, item.Text);
    }

    /// <summary>
    /// The tap at <paramref name="item"/>'s point, and, once it went out, the press's hook; false, said, when nothing was tapped. A
    /// nav data press reads the cycle installed BEFORE its tap and hands it over with its hook: BlueBird's REMOVE may delete the file
    /// as it is tapped, and the watch must know what was there.
    /// </summary>
    private async Task<bool> TapAsync(Bbs757EfbItem item, Target target, CancellationToken ct)
    {
        var hook = Bbs757EfbExtras.HookFor(target.Page.PageName, item);
        int? cycleBefore = hook is Bbs757EfbHook.NavDataUpdate or Bbs757EfbHook.NavDataRemove ? _io.ReadInstalledCycle() : null;
        if (!await _io.Tap(item.Hit, ct))
        {
            _io.Alert(Bbs757EfbPress.UnreachableText);
            return false;
        }
        if (hook != Bbs757EfbHook.None) _io.Hooked(hook, cycleBefore);
        return true;
    }

    /// <summary>After a tap or a wheel step, waits for the picture to change from <paramref name="before"/>, at most
    /// <see cref="ChangeMaxMs"/>; the read that follows settles as usual.</summary>
    private async Task WaitForChangeAsync(Bitmap? before, CancellationToken ct)
    {
        if (before == null)
        {
            await _io.Delay(AfterTapMs, ct);
            return;
        }
        long start = _io.NowMs();
        while (_io.NowMs() - start < ChangeMaxMs)
        {
            await _io.Delay(ChangePollMs, ct);
            using var now = _io.Capture();
            if (now != null && !_io.Same(before, now))
            {
                Log.Debug("BBS757", $"EFB reader: picture changed {_io.NowMs() - start} ms after the tap");
                return;
            }
        }
        Log.Debug("BBS757", $"EFB reader: no change in {ChangeMaxMs} ms after the tap");
    }
}
