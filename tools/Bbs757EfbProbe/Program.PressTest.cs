// The press test (plan 18's measurement, 2026-10-29): does a tap at the point the reader found for a control land on
// that control, and change nothing else? For every control on the page shown that an operable window would offer
// (the reader's buttons and tiles, switches, check boxes, sliders and tabs), outside a list of controls with big or
// irreversible effects: record every BlueBird L:var (one Coherent call, the expression in PROBE_SNAP_JS) and
// BlueBird's settings files (PROBE_WORK; credentials compared by hash, never printed), tap where the reader placed the
// control, read the page again and record again; then put the control back and check that it is back.
//
//   presstest <outDir> <label> [--nav x,y ...] [--only text] [--skip text] [--dry]
//   compare <dir>                  the saved pages read with the small and the medium model: where the controls differ
//
// --nav: the taps that bring the tablet to this page from anywhere (Home first), used when a tap left the page.
// --only / --skip: controls whose text or key holds the text (case-insensitive). --dry: list the plan, tap nothing.
// PROBE_ENGINE=medium reads with the medium model from PROBE_MEDIUM (a folder with the medium files and the classifier).
// Writes <outDir>/<label>.log (everything printed), <label>.tsv (one row per control) and <label>.png (the page as
// first read, for the model comparison).
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;
using RapidOcrNet;

internal static partial class Program
{
    /// <summary>BlueBird's settings keys that hold credentials: compared by hash, never printed.</summary>
    private static readonly string[] CredentialKeys = { "simbrief_id", "hoppie_logon_code", "sayintentions_acars_key" };

    /// <summary>Never tapped by the press test: big or irreversible effects, each with its own check in the app.</summary>
    private static readonly string[] DangerWords =
    {
        "REMOVE ALL", "PB TUG", "REMOVE TUG", "FAIL NOW", "SET UP", "REPAIR", "FETCH", "REFRESH", "REMOVE FP",
        "SAVE USER", "LOAD USER", "PUSH ALL", "RESET ALL", "LOGOUT", "UPDATE", "GND SRVC",
    };

    /// <summary>Pages the press test does not tap on: presets (panel states), AIRAC (UPDATE/REMOVE, Navigraph), the
    /// Systems diagrams and the moving maps and charts.</summary>
    private static readonly string[] DangerPages =
        { "Aircraft, Cockpit presets", "Settings, AIRAC", "Systems", "Navigation, Charts", "Navigation, Map" };

    private static readonly string[] DismissWords = { "NO", "CANCEL", "CLOSE", "OK", "BACK" };

    private sealed record Snap(Dictionary<string, double?> Vars, Dictionary<string, string> Settings);

    /// <summary>The variables that move on their own, learned as the run goes.</summary>
    private static readonly HashSet<string> _noise = new(StringComparer.Ordinal);

    private static StreamWriter? _log;

    private static void L(string s)
    {
        Console.WriteLine(s);
        _log?.WriteLine(s);
        _log?.Flush();
    }

    private static async Task<int> PressTest(string[] args)
    {
        string outDir = args[1], label = args[2];
        var nav = new List<Point>();
        var only = new List<string>();
        var skip = new List<string>();
        bool dry = false;
        for (int a = 3; a < args.Length; a++)
        {
            switch (args[a])
            {
                case "--nav":
                    while (a + 1 < args.Length && !args[a + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        var xy = args[++a].Split(',');
                        nav.Add(new Point(int.Parse(xy[0], CultureInfo.InvariantCulture), int.Parse(xy[1], CultureInfo.InvariantCulture)));
                    }
                    break;
                case "--only": only.Add(args[++a]); break;
                case "--skip": skip.Add(args[++a]); break;
                case "--dry": dry = true; break;
            }
        }
        if (Popout.Find() == IntPtr.Zero) { Console.WriteLine("no pop-out: run 'popout' first"); return 2; }
        Directory.CreateDirectory(outDir);
        _log = new StreamWriter(Path.Combine(outDir, label + ".log"), append: true) { AutoFlush = true };
        var tsv = new StreamWriter(Path.Combine(outDir, label + ".tsv"), append: false) { AutoFlush = true };
        tsv.WriteLine(string.Join('\t', "page", "n", "kind", "key", "text", "tint", "on", "value", "hit", "tap", "plan", "changeMs",
            "pageAfter", "controlAfter", "otherElements", "vars", "settings", "restored", "left", "verdict"));
        L($"===== presstest {label} {DateTime.Now:yyyy-MM-dd HH:mm:ss} engine={EngineName}");
        Engine();

        // The variables that move on their own: the session-8 list, then what moves over four idle seconds.
        var noise = _noise;
        if (Environment.GetEnvironmentVariable("PROBE_NOISE") is { Length: > 0 } nf && File.Exists(nf))
            foreach (var name in File.ReadAllText(nf).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) noise.Add(name);
        var s1 = await TakeSnap();
        await Task.Delay(2000);
        var s2 = await TakeSnap();
        await Task.Delay(2000);
        var s3 = await TakeSnap();
        foreach (var k in ChangedVars(s1.Vars, s2.Vars, noise).Concat(ChangedVars(s2.Vars, s3.Vars, noise))) noise.Add(k);
        L($"  {s3.Vars.Count} variables, {noise.Count} treated as noise; {s3.Settings.Count} settings values");

        var (m0, b0) = await ReadNow();
        if (m0 == null || b0 == null) { L("  capture failed"); return 3; }
        using (b0) b0.Save(Path.Combine(outDir, label + ".png"), ImageFormat.Png);
        string page = m0.PageName;
        L($"  page: {page}");
        var plan = new List<Bbs757EfbItem>();
        foreach (var i in Candidates(m0))
        {
            string? why = DangerReason(page, i)
                ?? (only.Count > 0 && !only.Any(o => Holds(i, o)) ? "not in --only" : null)
                ?? (skip.Any(o => Holds(i, o)) ? "--skip" : null)
                ?? (i.Kind == Bbs757EfbKind.Menu && i.Selected ? "the tab shown" : null);
            L($"  {(why == null ? "TEST" : "skip")} {i.Kind,-8} {i.Text} [{i.Key}] {i.Tint} on={i.On} value='{i.Value}' @{i.Hit.X},{i.Hit.Y}{(why == null ? "" : "  (" + why + ")")}");
            if (why == null) plan.Add(i);
        }
        if (dry) return 0;

        int n = 0;
        foreach (var planned in plan)
        {
            n++;
            // A fresh read: an earlier control's revert, or a page that moved, changed what is where.
            var (read, bb) = await ReadNow();
            bb?.Dispose();
            if (read == null) { L("  capture failed"); break; }
            Bbs757EfbScreenModel before = read;
            if (before.PageName != page)
            {
                L($"  [{n}] the page reads '{before.PageName}', not '{page}': going back");
                if (!await GoBack(nav, page)) { L("  could not get back to the page: stopping"); break; }
                (read, bb) = await ReadNow();
                bb?.Dispose();
                if (read == null || read.PageName != page) { L("  still not the page: stopping"); break; }
                before = read;
            }
            var item = Match(before, planned);
            if (item == null) { L($"  [{n}] {planned.Text}: not on the page now"); Row(tsv, page, n, planned, null, "", "", "", "", "", "", "", "", "", "VANISHED"); continue; }
            var (tap, back, how) = TapPlan(item);
            if (tap == Point.Empty) { L($"  [{n}] {item.Text}: {how}"); Row(tsv, page, n, item, null, how, "", "", "", "", "", "", "", "", "SKIPPED"); continue; }
            if (Bbs757EfbScreen.UpdateGuard.Contains(tap)) { L($"  [{n}] {item.Text}: tap point in the guard box"); continue; }

            L($"  [{n}] {item.Kind} '{item.Text}' [{item.Key}] {item.Tint} on={item.On} value='{item.Value}' hit {item.Hit.X},{item.Hit.Y} tap {tap.X},{tap.Y} ({how})");
            var pre1 = await TakeSnap();
            await Task.Delay(1000);
            var pre = await TakeSnap();
            var drift = new HashSet<string>(ChangedVars(pre1.Vars, pre.Vars, noise), StringComparer.Ordinal);
            foreach (var d in drift) noise.Add(d);

            long changeMs = await TapAndWait(tap);
            var post = await StableSnap(noise);
            var (after, ab) = await ReadNow();
            ab?.Dispose();
            if (after == null) { L("  capture failed"); break; }

            var vars = ChangedVars(pre.Vars, post.Vars, noise);
            var settings = ChangedSettings(pre.Settings, post.Settings);
            string pageAfter = after.PageName == page ? "" : after.PageName;
            var tappedAfter = Match(after, item);
            var others = OtherChanges(before, after, item, tappedAfter);
            string controlAfter = tappedAfter == null ? "(gone)" : Sig(tappedAfter) == Sig(item) ? "(unchanged)" : Describe(tappedAfter);
            L($"      change seen {(changeMs < 0 ? "never" : changeMs + " ms")}; page {(pageAfter.Length == 0 ? "same" : "-> " + pageAfter)}; control {controlAfter}");
            foreach (var v in vars) L($"      var {v}: {Num(pre.Vars.GetValueOrDefault(v))} -> {Num(post.Vars.GetValueOrDefault(v))}");
            foreach (var s in settings) L($"      setting {s}: {Show(pre.Settings.GetValueOrDefault(s))} -> {Show(post.Settings.GetValueOrDefault(s))}");
            foreach (var o in others) L($"      other element: {o}");

            // Put it back.
            string restored = "";
            List<string> left = new();
            bool changed = vars.Count > 0 || settings.Count > 0 || pageAfter.Length > 0 || controlAfter != "(unchanged)" || others.Count > 0;
            if (changed)
            {
                restored = await Revert(item, tappedAfter, back, before, after, nav, page);
                var end = await StableSnap(noise);
                left = ChangedVars(pre.Vars, end.Vars, noise).Select(v => $"{v} {Num(pre.Vars.GetValueOrDefault(v))}->{Num(end.Vars.GetValueOrDefault(v))}")
                    .Concat(ChangedSettings(pre.Settings, end.Settings).Select(s => $"{s} {Show(pre.Settings.GetValueOrDefault(s))}->{Show(end.Settings.GetValueOrDefault(s))}")).ToList();
                L($"      revert: {restored}; {(left.Count == 0 ? "all back" : "left changed: " + string.Join("; ", left))}");
            }

            string verdict = Verdict(item, vars, settings, pageAfter, controlAfter, others);
            L($"      VERDICT {verdict}");
            Row(tsv, page, n, item, tap, how, changeMs < 0 ? "" : changeMs.ToString(CultureInfo.InvariantCulture), pageAfter, controlAfter,
                string.Join("; ", others),
                string.Join("; ", vars.Select(v => $"{v} {Num(pre.Vars.GetValueOrDefault(v))}->{Num(post.Vars.GetValueOrDefault(v))}")),
                string.Join("; ", settings.Select(s => $"{s} {Show(pre.Settings.GetValueOrDefault(s))}->{Show(post.Settings.GetValueOrDefault(s))}")),
                restored, string.Join("; ", left), verdict);
        }
        L($"===== presstest {label} done: {n} controls");
        return 0;
    }

    private static bool Holds(Bbs757EfbItem i, string text) =>
        i.Text.Contains(text, StringComparison.OrdinalIgnoreCase) || i.Key.Contains(text, StringComparison.OrdinalIgnoreCase);

    /// <summary>What an operable window would offer: buttons and tiles, switches, check boxes, sliders and the tabs
    /// inside a page (the side menus and the home screen are page navigation, which the tours measured).</summary>
    private static IEnumerable<Bbs757EfbItem> Candidates(Bbs757EfbScreenModel m) =>
        m.Menu.Concat(m.Items).Where(i => i.Clickable
            && (i.Kind is Bbs757EfbKind.Button or Bbs757EfbKind.Toggle or Bbs757EfbKind.Checkbox or Bbs757EfbKind.Slider
                || (i.Kind == Bbs757EfbKind.Menu && i.Key.StartsWith("tab:", StringComparison.Ordinal))));

    private static string? DangerReason(string page, Bbs757EfbItem i)
    {
        foreach (var p in DangerPages)
            if (page.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return "page: " + p;
        string up = (i.Text + " " + i.Key).ToUpperInvariant();
        foreach (var w in DangerWords)
            if (up.Contains(w, StringComparison.Ordinal)) return "danger: " + w;
        if (Bbs757EfbScreen.UpdateGuard.Contains(i.Hit)) return "guard box";
        if (page.StartsWith("Navigation, PERF", StringComparison.OrdinalIgnoreCase) && !i.Key.StartsWith("tab:", StringComparison.Ordinal))
            return "perf calc input";
        return null;
    }

    /// <summary>Where to tap, where to tap to put it back (sliders), and how the tap was chosen.</summary>
    private static (Point Tap, Point? Back, string How) TapPlan(Bbs757EfbItem i)
    {
        if (i.Kind == Bbs757EfbKind.Slider && i.Slider is { } sl)
        {
            string name = i.Key.StartsWith("sld:", StringComparison.Ordinal) ? i.Key[4..] : "";
            if (Bbs757EfbVocabulary.SliderOptions.TryGetValue(name, out var opts))
            {
                int cur = Array.FindIndex(opts, o => string.Equals(o, i.Value, StringComparison.OrdinalIgnoreCase));
                if (cur < 0) return (Point.Empty, null, $"the value '{i.Value}' is none of its options");
                int target = cur + 1 < opts.Length ? cur + 1 : cur - 1;
                return (new Point(OptX(sl, target, opts.Length), i.Hit.Y), new Point(OptX(sl, cur, opts.Length), i.Hit.Y),
                    $"option {opts[cur]} -> {opts[target]}");
            }
            double pct = sl.TrackRight > sl.TrackLeft ? 100.0 * (sl.KnobX - sl.TrackLeft) / (sl.TrackRight - sl.TrackLeft) : 0;
            double tgt = pct <= 50 ? pct + 30 : pct - 30;
            int x = sl.TrackLeft + (int)Math.Round(tgt / 100 * (sl.TrackRight - sl.TrackLeft));
            return (new Point(x, i.Hit.Y), new Point(sl.KnobX, i.Hit.Y), $"knob {pct:0}% -> {tgt:0}%");
        }
        return (i.Hit, null, "hit");
    }

    private static int OptX(Bbs757EfbSlider s, int index, int count) =>
        count <= 1 ? s.TrackLeft : s.TrackLeft + (int)Math.Round((double)index * (s.TrackRight - s.TrackLeft) / (count - 1));

    /// <summary>The control on a page read: the same key, else the control of the same kind whose box overlaps the
    /// old one most (a tile's words change with its state, and its key with them).</summary>
    private static Bbs757EfbItem? Match(Bbs757EfbScreenModel m, Bbs757EfbItem old)
    {
        var all = m.Menu.Concat(m.Items).ToList();
        var byKey = all.FirstOrDefault(i => i.Key == old.Key);
        if (byKey != null) return byKey;
        Bbs757EfbItem? best = null;
        long bestArea = 0;
        foreach (var i in all.Where(i => i.Kind == old.Kind))
        {
            var r = Rectangle.Intersect(i.Box, old.Box);
            long area = (long)r.Width * r.Height;
            if (area > bestArea) { bestArea = area; best = i; }
        }
        return best;
    }

    /// <summary>What a control reads as, spaces left out: OCR reads "79F" and "79 F" for the same picture.</summary>
    private static string Sig(Bbs757EfbItem i) =>
        System.Text.RegularExpressions.Regex.Replace($"{i.Kind}|{i.Text}|{i.Value}|{i.On}|{i.Tint}|{i.Selected}", @"\s+", "");

    private static string Describe(Bbs757EfbItem i) =>
        $"'{i.Text}' {i.Tint} on={i.On} value='{i.Value}'{(i.Selected ? " selected" : "")}";

    /// <summary>Every element but the tapped control (and anything overlapping it) that reads differently after the
    /// tap: changed, gone or new.</summary>
    private static List<string> OtherChanges(Bbs757EfbScreenModel a, Bbs757EfbScreenModel b, Bbs757EfbItem tapped, Bbs757EfbItem? tappedAfter)
    {
        bool Near(Bbs757EfbItem i) => i.Key == tapped.Key || (tappedAfter != null && i.Key == tappedAfter.Key)
            || (!i.Box.IsEmpty && (i.Box.IntersectsWith(tapped.Box) || (tappedAfter != null && i.Box.IntersectsWith(tappedAfter.Box))));
        var o = new List<string>();
        var aa = a.Menu.Concat(a.Items).Where(i => !Near(i)).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
        var bb = b.Menu.Concat(b.Items).Where(i => !Near(i)).GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.First());
        foreach (var (k, i) in aa)
        {
            if (!bb.TryGetValue(k, out var j)) o.Add($"gone {Describe(i)}");
            else if (Sig(i) != Sig(j)) o.Add($"{Describe(i)} -> {Describe(j)}");
        }
        foreach (var (k, j) in bb)
            if (!aa.ContainsKey(k)) o.Add($"new {Describe(j)}");
        // An element whose box grew or shrank a little gets a new key: the same words gone and new are no change.
        foreach (var d in o.Where(x => x.StartsWith("gone ", StringComparison.Ordinal)).ToList())
        {
            string twin = "new " + d[5..];
            if (o.Contains(twin)) { o.Remove(d); o.Remove(twin); }
        }
        return o;
    }

    /// <summary>
    /// LANDED: the control (or its variables or settings) changed and no other existing control did; elements that
    /// came or went with it (a notification and its time, REMOVE ALL once a service runs) are its consequences.
    /// NOTHING: no change at all. CHECK: anything else, judged by hand from the log: another control changed, the
    /// page changed, or only variables moved.
    /// </summary>
    private static string Verdict(Bbs757EfbItem item, List<string> vars, List<string> settings, string pageAfter, string controlAfter, List<string> others)
    {
        bool tab = item.Kind == Bbs757EfbKind.Menu;
        bool controlMoved = controlAfter != "(unchanged)" && controlAfter != "(gone)";
        var real = others.Where(o => !IsNotification(o)).ToList();
        bool otherControlChanged = real.Any(o => !o.StartsWith("new ", StringComparison.Ordinal) && !o.StartsWith("gone ", StringComparison.Ordinal));
        if (vars.Count == 0 && settings.Count == 0 && pageAfter.Length == 0 && !controlMoved && real.Count == 0) return "NOTHING";
        if (tab) return controlAfter.Contains("selected", StringComparison.Ordinal) && pageAfter.Length == 0 ? "LANDED (tab shown)" : "CHECK (tab)";
        if (pageAfter.Length > 0) return "CHECK (page changed)";
        if (otherControlChanged) return "CHECK (another element changed)";
        if (!controlMoved) return "CHECK (variables, settings or new elements only)";
        return real.Count > 0 ? "LANDED (with elements that came or went)" : "LANDED";
    }

    /// <summary>A notification the tablet showed or took down, or its time: "new 'Notifications: GPU connected'",
    /// "gone '12:06Z'".</summary>
    private static bool IsNotification(string change) =>
        change.Contains("'Notifications:", StringComparison.Ordinal)
        || System.Text.RegularExpressions.Regex.IsMatch(change, @"^(new|gone) '\d{1,2}:\d\dZ'");

    /// <summary>Puts the control back: a switch, check box or tile tapped once more where it is now (a tile until its
    /// words are the old ones again, at most five taps); a slider at its old place; a tab by tapping the tab that was
    /// shown; a question by its NO, CANCEL, CLOSE or OK; a page left by the --nav taps.</summary>
    private static async Task<string> Revert(Bbs757EfbItem item, Bbs757EfbItem? now, Point? back, Bbs757EfbScreenModel before,
        Bbs757EfbScreenModel after, List<Point> nav, string page)
    {
        if (after.PageName != page)
            return await GoBack(nav, page) ? "went back to the page" : "COULD NOT go back to the page";
        // A question that came up with the tap: dismissed.
        var dialog = OtherNew(before, after).FirstOrDefault(i => i.Kind == Bbs757EfbKind.Button
            && DismissWords.Contains(i.Text.Trim().ToUpperInvariant()));
        if (dialog != null)
        {
            await TapAndWait(dialog.Hit);
            return $"dismissed with '{dialog.Text}'";
        }
        if (item.Kind == Bbs757EfbKind.Menu)
        {
            var shown = before.Items.FirstOrDefault(i => i.Kind == Bbs757EfbKind.Menu && i.Selected && i.Key.StartsWith("tab:", StringComparison.Ordinal));
            if (shown == null) return await GoBack(nav, page, force: true) ? "re-navigated (no tab was read as shown)" : "COULD NOT re-navigate";
            var shownNow = Match(after, shown);
            await TapAndWait((shownNow ?? shown).Hit);
            return $"tapped the tab '{shown.Text}'";
        }
        if (back is Point p)
        {
            await TapAndWait(p);
            return $"tapped {p.X},{p.Y}";
        }
        var target = now ?? item;
        for (int t = 1; t <= 5; t++)
        {
            await TapAndWait(target.Hit);
            // A door or a truck is waited out before the control is judged: mid-way it reads neither way.
            await StableSnap(_noise);
            var (m, b) = await ReadNow();
            b?.Dispose();
            if (m == null) return "capture failed";
            var c = Match(m, item);
            if (c != null && Sig(c) == Sig(item)) return t == 1 ? "tapped again" : $"tapped again {t} times";
            if (item.Kind != Bbs757EfbKind.Button || c == null) return t == 1 ? "tapped again (the control reads differently)" : $"tapped again {t} times";
            target = c;
        }
        return "tapped again 5 times, the control never read as before";
    }

    private static IEnumerable<Bbs757EfbItem> OtherNew(Bbs757EfbScreenModel a, Bbs757EfbScreenModel b)
    {
        var keys = a.Menu.Concat(a.Items).Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        return b.Menu.Concat(b.Items).Where(i => !keys.Contains(i.Key));
    }

    /// <summary>The --nav taps, then a read until the page is the one wanted (at most three tries).</summary>
    private static async Task<bool> GoBack(List<Point> nav, string page, bool force = false)
    {
        if (nav.Count == 0) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            foreach (var p in nav) await TapAndWait(p);
            await Task.Delay(500);
            var (m, b) = await ReadNow();
            b?.Dispose();
            if (m != null && m.PageName == page) return true;
        }
        return false;
    }

    /// <summary>Taps and waits (at most three seconds) for the picture to change; how long it took, -1 for never.</summary>
    private static async Task<long> TapAndWait(Point p)
    {
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        using var before = Popout.CaptureTablet();
        string r = await Agent($"JSON.stringify(__MSFSBA_BBS757EFB.click({p.X},{p.Y}))");
        if (!r.Contains("ok", StringComparison.Ordinal)) L($"      (tap {p.X},{p.Y}: {r})");
        var sw = Stopwatch.StartNew();
        while (before != null && sw.ElapsedMilliseconds < 3000)
        {
            await Task.Delay(100);
            using var now = Popout.CaptureTablet();
            if (now != null && !Bbs757EfbFrameCompare.Same(before, now)) return sw.ElapsedMilliseconds;
        }
        return -1;
    }

    /// <summary>The page shown, read as the app reads it: the mirror refreshed, a settled capture, one read.</summary>
    private static async Task<(Bbs757EfbScreenModel? Model, Bitmap? Bmp)> ReadNow()
    {
        await Agent("JSON.stringify(__MSFSBA_BBS757EFB.mirror(true))");
        var (bmp, _, _) = await Settled();
        if (bmp == null) return (null, null);
        var r = await Bbs757EfbPageReader.ReadAsync(Engine(), bmp);
        return (r.Model, bmp);
    }

    // ---------------- snapshots: every BlueBird L:var and BlueBird's settings files ----------------

    private static string? _snapJs;

    private static async Task<Snap> TakeSnap() => new(await ReadAllVars(), ReadSettings());

    /// <summary>Snapshots until two in a row agree (every 0.8 s, at most <paramref name="maxMs"/>): a door moving or a
    /// truck arriving is waited out.</summary>
    private static async Task<Snap> StableSnap(ISet<string> noise, int maxMs = 30000)
    {
        var sw = Stopwatch.StartNew();
        var prev = await TakeSnap();
        while (sw.ElapsedMilliseconds < maxMs)
        {
            await Task.Delay(800);
            var now = await TakeSnap();
            bool moved = ChangedVars(prev.Vars, now.Vars, noise).Count > 0 || ChangedSettings(prev.Settings, now.Settings).Count > 0;
            prev = now;
            if (!moved) break;
        }
        return prev;
    }

    private static async Task<Dictionary<string, double?>> ReadAllVars()
    {
        _snapJs ??= File.ReadAllText(Environment.GetEnvironmentVariable("PROBE_SNAP_JS")
            ?? throw new InvalidOperationException("Set PROBE_SNAP_JS to the snapshot expression (snap.js)."));
        string json = await Eval(_snapJs);
        var o = new Dictionary<string, double?>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
            o[p.Name] = p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetDouble() : null;
        return o;
    }

    /// <summary>BlueBird's efb_settings.json and liveries\*.json, flattened ("file:key.sub" = raw JSON value); a
    /// credential is kept as a hash marker that is never printed.</summary>
    private static Dictionary<string, string> ReadSettings()
    {
        string work = Environment.GetEnvironmentVariable("PROBE_WORK")
            ?? throw new InvalidOperationException("Set PROBE_WORK to BlueBird's work folder.");
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<string> { Path.Combine(work, "efb_settings.json") };
        string liveries = Path.Combine(work, "liveries");
        if (Directory.Exists(liveries)) files.AddRange(Directory.GetFiles(liveries, "*.json").OrderBy(f => f, StringComparer.Ordinal));
        foreach (var f in files)
        {
            string prefix = Path.GetFileNameWithoutExtension(f) + ":";
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(f));
                    Flatten(doc.RootElement, prefix, o);
                    break;
                }
                catch (Exception) when (attempt < 5) { Thread.Sleep(100); }
                catch (Exception e) { o[prefix + "!"] = "unreadable: " + e.GetType().Name; break; }
            }
        }
        return o;
    }

    private static void Flatten(JsonElement e, string path, Dictionary<string, string> o)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in e.EnumerateObject()) Flatten(p.Value, path + (path.EndsWith(':') ? "" : ".") + p.Name, o);
            return;
        }
        string leaf = path[(path.LastIndexOfAny(new[] { ':', '.' }) + 1)..];
        string text = e.GetRawText();
        if (CredentialKeys.Contains(leaf, StringComparer.OrdinalIgnoreCase))
            text = "cred:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        o[path] = text;
    }

    private static List<string> ChangedVars(Dictionary<string, double?> a, Dictionary<string, double?> b, ISet<string> ignore) =>
        a.Keys.Union(b.Keys).Where(k => !ignore.Contains(k) && !SameNum(a.GetValueOrDefault(k), b.GetValueOrDefault(k)))
            .OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static List<string> ChangedSettings(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Keys.Union(b.Keys).Where(k => a.GetValueOrDefault(k) != b.GetValueOrDefault(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();

    private static bool SameNum(double? x, double? y) => x == y || (x is double p && y is double q && Math.Abs(p - q) < 1e-9);

    private static string Num(double? v) => v is double d ? d.ToString("0.####", CultureInfo.InvariantCulture) : "null";

    private static string Show(string? v) => v == null ? "(none)" : v.StartsWith("cred:", StringComparison.Ordinal) ? "(credential)" : v;

    private static void Row(StreamWriter tsv, string page, int n, Bbs757EfbItem item, Point? tap, string plan, string changeMs, string pageAfter,
        string controlAfter, string others, string vars, string settings, string restored, string left, string verdict)
    {
        string Clean(string s) => s.Replace('\t', ' ').Replace('\n', ' ');
        tsv.WriteLine(string.Join('\t', new[]
        {
            page, n.ToString(CultureInfo.InvariantCulture), item.Kind.ToString(), item.Key, item.Text, item.Tint.ToString(),
            item.On?.ToString() ?? "", item.Value, $"{item.Hit.X},{item.Hit.Y}", tap is Point t ? $"{t.X},{t.Y}" : "", plan, changeMs,
            pageAfter, controlAfter, others, vars, settings, restored, left, verdict,
        }.Select(Clean)));
    }

    // ---------------- the medium model, and the model comparison ----------------

    private static string EngineName => Environment.GetEnvironmentVariable("PROBE_ENGINE") is { Length: > 0 } e ? e : "small";

    /// <summary>PP-OCRv6 medium from PROBE_MEDIUM, read with the app's options and thread cap.</summary>
    private static PaddleOcrEngine LoadMedium()
    {
        string dir = Environment.GetEnvironmentVariable("PROBE_MEDIUM")
            ?? throw new InvalidOperationException("Set PROBE_MEDIUM to the folder with the medium model files.");
        var ocr = new RapidOcr();
        ocr.InitModels(RapidOcrModelSet.PPOCRv6Medium with
        {
            DetModelPath = Path.Combine(dir, "PP-OCRv6_det_medium.onnx"),
            RecModelPath = Path.Combine(dir, "PP-OCRv6_rec_medium.onnx"),
            KeysPath = Path.Combine(dir, "ppocrv6_medium_dict.txt"),
            ClsModelPath = Path.Combine(dir, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
        }, PaddleOcrEngine.ThreadsFor(Environment.ProcessorCount));
        return new PaddleOcrEngine((picture, ct) => ocr.Detect(picture, PaddleOcrEngine.Options, ct).TextBlocks, ocr);
    }

    private static PaddleOcrEngine? _small, _medium;

    /// <summary>The saved pages read with both models: for each page, the controls (as the press test picks them)
    /// whose kind, words, state or tap point differ, and the time each read took.</summary>
    private static async Task<int> Compare(string dir)
    {
        _small ??= PaddleOcrEngine.Load(TextRecognitionModels.DefaultFolder(), PaddleOcrEngine.ThreadsFor(Environment.ProcessorCount));
        _medium ??= LoadMedium();
        int pages = 0, same = 0, controls = 0, differing = 0;
        foreach (var png in Directory.GetFiles(dir, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            using var bmp = new Bitmap(png);
            var rs = await Bbs757EfbPageReader.ReadAsync(_small, bmp);
            var rm = await Bbs757EfbPageReader.ReadAsync(_medium, bmp);
            var cs = Candidates(rs.Model).ToList();
            var cm = Candidates(rm.Model).ToList();
            pages++;
            var diffs = new List<string>();
            foreach (var c in cs)
            {
                var m = Match(rm.Model, c);
                if (m == null || m.Kind != c.Kind) { diffs.Add($"  small only: {c.Kind} '{c.Text}' @{c.Hit.X},{c.Hit.Y}"); continue; }
                int dx = m.Hit.X - c.Hit.X, dy = m.Hit.Y - c.Hit.Y;
                bool words = !string.Equals(m.Text, c.Text, StringComparison.Ordinal) || m.Value != c.Value;
                bool state = m.On != c.On || m.Tint != c.Tint || m.Selected != c.Selected;
                if (words || state || Math.Abs(dx) > 3 || Math.Abs(dy) > 3)
                    diffs.Add($"  {c.Kind} '{c.Text}' {c.Tint} on={c.On} @{c.Hit.X},{c.Hit.Y}  vs medium '{m.Text}' {m.Tint} on={m.On} @{m.Hit.X},{m.Hit.Y}");
            }
            foreach (var m in cm)
                if (Match(rs.Model, m) is not { } s || s.Kind != m.Kind) diffs.Add($"  medium only: {m.Kind} '{m.Text}' @{m.Hit.X},{m.Hit.Y}");
            controls += cs.Count;
            differing += diffs.Count;
            if (diffs.Count == 0) same++;
            Console.WriteLine($"{Path.GetFileNameWithoutExtension(png)}: small '{rs.Model.PageName}' {cs.Count} controls {rs.OcrMs} ms; "
                + $"medium '{rm.Model.PageName}' {cm.Count} controls {rm.OcrMs} ms; {(diffs.Count == 0 ? "same controls" : diffs.Count + " differences")}");
            foreach (var d in diffs) Console.WriteLine(d);
        }
        Console.WriteLine($"SUMMARY: {pages} pages, {same} with the same controls; {controls} controls (small), {differing} differences");
        return 0;
    }
}
