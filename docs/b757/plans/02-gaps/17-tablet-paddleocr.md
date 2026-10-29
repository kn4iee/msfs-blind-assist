# BlueBird 757 Tablet Reader on PaddleOCR Implementation Plan (plan 17, added 2026-10-28)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. Task 0, the claim, is the controller's own, done before Task 1 is dispatched. Tasks marked [controller] or [owner] are never dispatched to an implementer.

**Goal:** The 757 tablet screen reader reads BlueBird's tablet with PaddleOCR (PP-OCRv6 small) instead of Windows OCR, its models downloaded once on demand, and Windows OCR leaves the app.

**Architecture:** The engine (RapidOcrNet with ONNX Runtime and SkiaSharp) ships in the app; the four model files are fetched from a GitHub release of the app's repository into `%APPDATA%\MSFSBlindAssist\text-recognition\`, each checked against a pinned SHA-256, with an `installed.json` marker written last. A new Settings tab and an offer window start the download; the reader refuses without the models. Plan 11's reader keeps its parser and records: `Bbs757EfbOcr` maps the engine's lines into them, one read per page.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, RapidOcrNet 4.2.0, xUnit.

**Spec:** the owner's design of 2026-10-28, kept locally (`docs/superpowers/specs/2026-10-28-b757-tablet-paddleocr-design.md`, git-ignored by the owner's choice); every decision and value a task needs is copied into Global Constraints below. Background: `docs/b757/evidence/flight-11.md` (the reader), `docs/location-data.md` (the download this one follows), rules B757-*, LOC-5, UPD-9, CORE-7.

**Why (measured 2026-10-27/28, the 2026-10-27 benchmark's hand-checked truth):** items read exactly on 23 tablet pages: PP-OCRv6 small 91.6%, tiny 85.7%, medium 91.2%, Windows OCR as the app reads today 61.5%; on the 12 dense pages (OFP, ACARS, performance) small 87.1%, tiny 77.9%. Through the parser on the 11 test pages: small 98.4% of page elements right (8 of 11 pages entirely), Windows 91.8% (5 of 11). Small reads a page in 548 ms on 4 threads, 705 ms on 2.

## Global Constraints

- **Owner's rulings (2026-10-28):** no models, no reading (the reader refuses and offers the download; no Windows OCR fallback); PP-OCRv6 small; the Text recognition tab plus an offer at startup on the 757 and at the 757 menu pick, governed by one checkbox, never a remembered "Not now"; the four files on a release of oasis1701/msfs-blind-assist, created LAST (Task 9) with the owner's word; plan 17 may change plan 11's files and the frozen files listed here.
- **Owned files.** New: `MSFSBlindAssist/Services/TextRecognition/**`, `Forms/Settings/TextRecognitionPanel.cs`, `Forms/TextRecognitionOfferDialog.cs`, `tests/MSFSBlindAssist.Tests/TextRecognition/**`, `.claude/rules/text-recognition.md`, `docs/text-recognition.md`, `docs/invariants/text-recognition.md`, `docs/b757/evidence/tablet-ocr.md`. Plan 11's (Declan Webster's), with the owner's agreement: `SimConnect/BBS757/Bbs757EfbOcr.cs`, `Bbs757EfbPageReader.cs`, `Bbs757EfbBridge.cs`, `Bbs757EfbScreen.cs` and `Bbs757EfbVocabulary.cs` (parser fixes only), `MainForm.BBS757.cs`, `tests/.../Bbs757EfbScreenTests.cs`, `tests/.../Fixtures/bbs757-efb/**/*.lines.json`, `tools/Bbs757EfbProbe/**`. Frozen, with the owner's agreement: `MSFSBlindAssist.csproj`, `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj`, `Settings/UserSettings.cs`, `Forms/Settings/SettingsForm.cs`, `MainForm.cs`, `MainForm.MenuHandlers.cs`, `CLAUDE.md` (one row), and the mirror lines the context-budget test asks for. Nothing else.
- **The model list (list version 1):** `PP-OCRv6_det_small.onnx` 9,929,594 bytes `090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f`; `PP-OCRv6_rec_small.onnx` 21,234,383 `6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884`; `ppocrv6_dict.txt` 74,947 `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d`; `ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx` 1,018,508 `54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7`. Total 32,257,432 bytes ("32 megabytes"). RapidOcrNet's `InitModels(RapidOcrModelSet, …)` loads the classifier unconditionally, so it stays in the list.
- **Release:** tag `text-recognition-1`, title "Text recognition models 1 (PaddleOCR PP-OCRv6 small)", base address `https://github.com/oasis1701/msfs-blind-assist/releases/download/text-recognition-1/`, created with `--latest=false`.
- **Folder:** `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "text-recognition")`; marker `installed.json`, exactly `{"version":1,"files":[{"name":"<name>","sha256":"<hash>"},…]}` in the list's order, written last.
- **Engine:** `RapidOcrModelSet.PPOCRv6Small with { DetModelPath, ClsModelPath, RecModelPath, KeysPath }` as absolute paths into the folder (a path of 260 or more characters gets the `\\?\` prefix); `RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true, DoAngle = false }`; threads `Math.Clamp(processorCount / 8, 1, 4)`; one read at a time; pixels copied into a BGRA `SKBitmap` (no PNG round trip). `PaddleOcrEngine.cs` uses nothing from the app beyond `TextRecognitionModels` (the probe links both).
- **Package:** `<PackageReference Include="RapidOcrNet" Version="4.2.0" ExcludeAssets="build" />` (its `build` targets would copy 13.7 MB of v5 models); target framework `net10.0-windows` in both project files and the probe.
- **Visibility:** `PaddleOcrEngine`, `OcrLine` and `OcrWord` are `public` (plan 11's public reader signatures and `Bbs757EfbBridgeIo`'s required members take them); everything else new is `internal`, as `LocationDataDownloader` is (the tests see internals). Namespace `MSFSBlindAssist.Services.TextRecognition`.
- **Strings, verbatim** (all invariant culture; megabytes are decimal, rounded: 32,257,432 bytes → "32"):
  - Offer window: title "Download text recognition"; status box "The 757 tablet reader needs text recognition: PaddleOCR, a 32 megabyte download. You can also download it later from Settings, Text recognition."; buttons "&Download text recognition" ("Downloading text recognition" while running, "Text recognition ready" when installed) and "&Not now" ("&Close" once a download started; Escape).
  - Tab: title "Text recognition"; intro "Text recognition for the BlueBird 757 tablet reader (downloaded once, then used offline):"; status "Not downloaded. The download is 32 megabytes." / "Downloading: {p} percent of 32 megabytes." / "Ready: PaddleOCR PP-OCRv6 small." / "An update is needed: this version of the app uses new text recognition files. The download is 32 megabytes." / "The last download failed: {reason}. Download again to retry." / "Download cancelled. {k} of 4 files are kept; the next download fetches the rest, {n} megabytes." (k = 0: "Download cancelled."); buttons "Download text recognition", "Cancel download"; checkbox "Offer this download when the BlueBird 757 is selected"; credits "Text recognition: PaddleOCR PP-OCRv6 small (PaddlePaddle, Apache 2.0), run with RapidOcrNet and ONNX Runtime."
  - Spoken: "Downloading text recognition, 32 megabytes." (immediate); "Text recognition {25|50|75} percent." (queued); "Text recognition download stalled." and "Download resumed." (queued); "Text recognition ready." (immediate); "Text recognition download failed: {reason}." (immediate); "Text recognition download cancelled." (immediate). Reasons: "could not reach GitHub", "the files were not found on GitHub" (HTTP 404), "a file did not match its checksum", "not enough disk space, 100 megabytes needed", "the files could not be saved".
  - Reader alert: "Text recognition could not start, so the tablet cannot be read. Download it again from Settings, Text recognition."
- **Disk:** at least 100,000,000 bytes free before a job fetches anything.
- **Offers:** `ShouldOffer` = the 757 profile, the checkbox on (`UserSettings.OfferTextRecognitionFor757`, default true), the models not Ready, no job running. At startup after the PMDG SDK check and before the update check; at the 757 menu pick after `SwitchAircraft`; never at connect. "Read the tablet screen" while not Ready shows the window whatever the checkbox says. Nothing about a dismissal is stored.
- **House rules:** CORE-7 (one confirmation, only what the screen reader cannot say), VAT-13 (status is a read-only `TextBox`), A380C-6 (background pushes marshal through a try/catch `BeginInvoke`), LOC-5's speech pattern, CORE-14/15 (`Log.*` only), CORE-16 (Read a file before changing it), US spelling, every spoken or formatted text tested under de-DE and sv-SE.
- **Workflow:** the index's (`00-index.md`) workflow and shared conventions; every commit ends with a `Plan: 17` trailer; build `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`; tests `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` (subagents add `--artifacts-path <worktree>/bin/b757-p17`); no test reads `docs/b757/`. Pushes past the claim only with the owner's word.

## Review Focus

- **The app killed mid-download:** a `.part` file left behind is deleted and its file fetched again; it never counts as a kept file (Task 2 test `A_part_file_left_by_a_killed_job_is_fetched_again`).
- **A model file removed after install** (an antivirus quarantine): the installed check says Incomplete, not Ready, so the offer comes back (Task 1 test `Ready_marker_with_a_file_missing_is_incomplete`).
- **Download pressed twice, or from the tab and the offer window at once:** one job, each file requested once (Task 2 test `Start_while_running_starts_nothing`).
- **The release tag as an update:** `text-recognition-1` must never parse as a version, or the updater would offer it (Task 1 test `The_release_tag_is_no_version`).
- **A model that fails to load:** the reader says so once, not at every capture, and tries again only after the window was hidden and shown (Task 4 test `A_failed_load_is_reported_once_until_released`); the engine is released only from the bridge's own loop, never under a running read (the reviewer checks no other path releases it).

---

### Task 0 [controller]: Claim this plan

- [ ] **Step 1: Check plan 17 is free.** `git fetch kn4iee`, then `git ls-tree kn4iee/B757 docs/b757/plans/02-gaps/` lists no `17-*.md`. If one exists, stop and ask the owner.
- [ ] **Step 2: Branch and claim.** `git branch -m B757-p17-tablet-paddleocr` (this worktree's branch, rebased onto the `B757` head fetched in step 1); in this file's Execution record set Status to claimed and Started to today and the base commit; commit only this file: `docs(b757): claim plan 17` with the `Plan: 17` trailer.
- [ ] **Step 3: Push the claim.** `git fetch kn4iee`, `git rebase kn4iee/B757`, `git push kn4iee HEAD:B757`; repeat on a rejected push; confirm with `git log kn4iee/B757 -1`. Task 1 starts only after this.

### Task 1: The model list, the installed check, and the area's rule file

**Files:**
- Create: `MSFSBlindAssist/Services/TextRecognition/TextRecognitionModels.cs`
- Create: `.claude/rules/text-recognition.md` (paths `MSFSBlindAssist/Services/TextRecognition/**` and `tests/MSFSBlindAssist.Tests/TextRecognition/**` only: the context-budget test fails a glob that matches no file, so Task 5 adds the two form files' paths when it creates them; until Task 6, a heading and one line naming its doc), `docs/text-recognition.md` (purpose, the model list, the folder), one CLAUDE.md row after location-data's: `| [text-recognition.md](docs/text-recognition.md) | PaddleOCR text recognition: the on-demand model download, the Text recognition tab and offer, the engine the 757 tablet reader uses | text-recognition |`
- Test: `tests/MSFSBlindAssist.Tests/TextRecognition/TextRecognitionModelsTests.cs`

**Interfaces:**
- Produces (all `internal`):
  - `sealed record TextRecognitionFile(string Name, long Bytes, string Sha256);`
  - `enum TextRecognitionState { Missing, Incomplete, Outdated, Ready }`
  - `sealed record TextRecognitionInstall(TextRecognitionState State, int FilesKept, long BytesToFetch);`
  - `static class TextRecognitionModels`: `const int ListVersion = 1`, `const string ReleaseTag`, `const string BaseUrl`, `const string MarkerName = "installed.json"`, `const long RequiredFreeBytes = 100_000_000`, `IReadOnlyList<TextRecognitionFile> Files`, `long TotalBytes`, `string DefaultFolder()`, `TextRecognitionInstall Check(string folder)` and `Check(string folder, IReadOnlyList<TextRecognitionFile> files, int listVersion)`, `string MarkerJson(IReadOnlyList<TextRecognitionFile> files, int listVersion)`, `void WriteMarker(string folder)` and its `(folder, files, listVersion)` overload, `void RemoveMarker(string folder)`, `int Megabytes(long bytes)` (decimal, rounded).
- Check: no marker → Missing when no listed file is present at its size, else Incomplete (FilesKept, BytesToFetch = the rest); a marker of another version or unreadable → Outdated; this version's marker with every file at its size → Ready, otherwise Incomplete. `.part` files never count.

- [ ] **Steps 1-5.** Tests (temp folders, small fake lists through the overloads): `Files_are_the_four_pinned_models` (names, bytes, hashes and order exactly as in Global Constraints; `TotalBytes == 32_257_432`; `Megabytes(TotalBytes) == 32`); `The_release_tag_is_no_version` (`SemanticVersion.TryParse(ReleaseTag)` is null; `BaseUrl` ends with `/download/text-recognition-1/`); `Empty_folder_is_missing`; `Kept_files_without_a_marker_are_incomplete` (2 of 4 at size → FilesKept 2, BytesToFetch = the other two); `A_part_file_never_counts`; `Marker_with_every_file_is_ready`; `Ready_marker_with_a_file_missing_is_incomplete`; `Ready_marker_with_a_wrong_size_file_is_incomplete`; `Marker_of_another_version_is_outdated`; `Unreadable_marker_is_outdated`; `Marker_json_is_exact` (the string for a two-file list); `RemoveMarker_leaves_the_files`. The full suite (the context-budget test included) is green with the rule file in place. Commit `feat(b757): the text recognition model list and installed check`.

### Task 2: The downloader and its texts

**Files:**
- Create: `MSFSBlindAssist/Services/TextRecognition/TextRecognitionText.cs`, `TextRecognitionDownloader.cs`
- Test: `tests/MSFSBlindAssist.Tests/TextRecognition/TextRecognitionTextTests.cs`, `TextRecognitionDownloaderTests.cs`

**Interfaces:**
- Consumes: Task 1; `Services.Geography.DownloadSpeech`, `DownloadProgressGate`, `StallDetector` (reused as they are, LOC-5's pattern).
- Produces (all `internal`):
  - `enum TextRecognitionPhase { Idle, Downloading, Ready, Failed, Cancelled }`; `sealed record TextRecognitionStatus(TextRecognitionPhase Phase, string Text, int Percent = -1) { bool IsRunning => Phase == TextRecognitionPhase.Downloading; }`
  - `static class TextRecognitionText`: `Idle(TextRecognitionInstall)`, `Downloading(int percent)`, `Ready`, `Failed(string reason)`, `Cancelled(int kept, long bytesLeft)`, `Started`, `Progress(int percent)`, `Stalled`, `Resumed`, `ReadySpoken`, `FailedSpoken(string reason)`, `CancelledSpoken`, `FailureReason(Exception)`, `ReaderCannotStart`, `OfferStatus`, `OfferButton(TextRecognitionPhase phase, TextRecognitionState state)` returning `(string Text, bool PressStarts)`. All strings as in Global Constraints.
  - `sealed class TextRecognitionDownloader : IDisposable`: ctor `(string? folder = null, HttpMessageHandler? handler = null, Func<string, long>? freeBytes = null, IReadOnlyList<TextRecognitionFile>? files = null, int listVersion = TextRecognitionModels.ListVersion)`; `event EventHandler<TextRecognitionStatus>? StatusChanged` and `event EventHandler<DownloadSpeech>? Speak` (raised on worker threads); `TextRecognitionStatus Status`, `bool IsRunning`, `Task Job`, `TextRecognitionInstall Installed()`, `TextRecognitionStatus DisplayStatus()` (the running or last job's status, else `Idle(Installed())`), `void Start()` (ignored while running), `void Cancel()`, `void Shutdown(TimeSpan wait)` (silent), `Dispose()`.
- The job: the disk check before any request; stale `.part` files deleted; per file, a present file whose SHA-256 matches is kept, otherwise fetched to `<name>.part` (stall speech at 20 s, abandoned at 60 s, 3 attempts with backoff), hashed, renamed; files in the folder not in the list removed (the marker excepted); the marker written last; User-Agent `MSFSBlindAssist/{version} (text-recognition)`.

- [ ] **Steps 1-5.** Text tests under en-US, de-DE and sv-SE: every string verbatim; `Downloading(45)` is "Downloading: 45 percent of 32 megabytes."; `Cancelled(2, 21_234_383 + 74_947)` names 21 megabytes; `FailureReason` maps `HttpRequestException` → "could not reach GitHub", an HTTP 404 → "the files were not found on GitHub", `InvalidDataException` → "a file did not match its checksum", the disk check's exception → "not enough disk space, 100 megabytes needed", `IOException`/`UnauthorizedAccessException` → "the files could not be saved"; `OfferButton` for idle, running, failed, cancelled and Ready. Downloader tests against a fake `HttpMessageHandler` (as `Geography/LocationDataDownloaderTests.FakeServer`) with a small file list: `Fresh_download_fetches_every_file_and_writes_the_marker_last`; `A_failed_attempt_is_retried`; `A_checksum_mismatch_fails_and_keeps_the_verified_files` (no marker, no `.part` left); `Cancel_keeps_verified_files_and_the_next_job_fetches_only_the_rest` (the second job requests only the missing names); `A_full_disk_stops_before_any_request`; `A_missing_release_says_not_found`; `Files_not_in_the_list_are_removed`; `A_part_file_left_by_a_killed_job_is_fetched_again`; `Start_while_running_starts_nothing`; `Shutdown_cancels_without_speaking`; the speech order of a fresh job (Started, 25, 50, 75 as the gate allows, ReadySpoken). Commit `feat(b757): the text recognition download`.

### Task 3: The engine host

**Files:**
- Create: `MSFSBlindAssist/Services/TextRecognition/PaddleOcrEngine.cs`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` (the RapidOcrNet reference only; the framework changes in Task 4)
- Test: `tests/MSFSBlindAssist.Tests/TextRecognition/PaddleOcrEngineTests.cs`

**Interfaces:**
- Consumes: `TextRecognitionModels.Files` (the names).
- Produces: `public sealed record OcrWord(string Text, Rectangle Box, float Confidence);`, `public sealed record OcrLine(string Text, Rectangle Box, IReadOnlyList<OcrWord> Words, float Confidence);`, `public sealed class PaddleOcrEngine : IDisposable` with `static PaddleOcrEngine Load(string folder, int threads)` (throws when the models cannot load), `Task<IReadOnlyList<OcrLine>> ReadAsync(Bitmap picture, CancellationToken ct)` (serialized by a `SemaphoreSlim`, runs off the caller's thread), `static int ThreadsFor(int processorCount)`, `static RapidOcrOptions Options`, `static RapidOcrModelSet ModelSet(string folder)`, `static IReadOnlyList<OcrLine> ToLines(IReadOnlyList<TextBlock> blocks)`, `static SKBitmap ToSkBitmap(Bitmap picture)`.
- `ToLines`: blank blocks dropped; text trimmed; box = the outline's bounds (`Rectangle.FromLTRB` of min/max X and Y); confidence = the mean of `CharScores` (1 when none); words from `WordResults` (text, bounds, score) when present and not empty, else the line's words split across its box by character count (word at character offset s of length n in a line of length L: x from `box.X + box.Width * s / L` to `box.X + box.Width * (s + n) / L`, at least 1 px wide).

- [ ] **Steps 1-5.** Tests: `ThreadsFor` theory (1→1, 4→1, 8→1, 16→2, 24→3, 32→4, 64→4); `Options_read_at_native_size_with_word_boxes_and_no_orientation` (Padding 0, ImgResize 0, LimitSideLen 736, ReturnWordBox true, DoAngle false); `ModelSet_points_into_the_folder` (four absolute paths with the list's names; DetMean and DetStd equal `PPOCRv6Small`'s); `ToLines_takes_the_outline_bounds_and_trims`; `ToLines_keeps_word_boxes_in_order`; `ToLines_splits_evenly_without_word_boxes` ("TOD PAUSE" in (100, 10, 90, 20) → TOD x 100-130, PAUSE x 140-190); `ToLines_drops_blank_blocks`; `ToSkBitmap_copies_pixels_in_order` (a 3x2 bitmap's colours read back from the `SKBitmap`). Commit `feat(b757): the PaddleOCR engine host`.

### Task 4: The reader on PaddleOCR, Windows OCR removed

**Files:**
- Create: `MSFSBlindAssist/Services/TextRecognition/TextRecognitionEngineSlot.cs`; test `tests/MSFSBlindAssist.Tests/TextRecognition/TextRecognitionEngineSlotTests.cs`
- Modify: `MSFSBlindAssist/MSFSBlindAssist.csproj` and `tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj` (target framework `net10.0-windows`; the output-folder comment says the pinned folder stays whatever the framework), `SimConnect/BBS757/Bbs757EfbOcr.cs`, `Bbs757EfbPageReader.cs`, `Bbs757EfbBridge.cs`, `MainForm.BBS757.cs` (the bridge's engine seam only), `tools/Bbs757EfbProbe/Bbs757EfbProbe.csproj` and `Program.cs`, the 11 `tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/**/*.lines.json`, `tests/.../Bbs757EfbScreenTests.cs`; `Bbs757EfbScreen.cs`/`Bbs757EfbVocabulary.cs` only for a fix step 6 calls for.

**Interfaces:**
- Consumes: Task 3's `PaddleOcrEngine`, `OcrLine`; Task 1's `DefaultFolder`, `RemoveMarker`; Task 2's `TextRecognitionText.ReaderCannotStart`.
- Produces: `internal sealed class TextRecognitionEngineSlot<T> : IDisposable where T : class, IDisposable` with ctor `(Func<T> load, Action<Exception> failed)`, `T? Get()` (loads on the first call; after a failed load calls `failed` once and returns null, without loading again, until `Release`), `void Release()` (disposes a loaded engine, forgets a failure), `Dispose()` (= `Release`); `Bbs757EfbOcr.ReadAsync(PaddleOcrEngine engine, Bitmap tablet, CancellationToken ct = default)` and the pure `Bbs757EfbOcr.FromEngine(IReadOnlyList<OcrLine> lines)` (pass `'P'`, words kept, `Bbs757EfbReadingOrder.Lines`); `Bbs757EfbPageReader.ReadAsync(PaddleOcrEngine engine, Bitmap tablet, CancellationToken ct = default)` returning `Bbs757EfbPageRead(Model, Lines, OcrMs)`; `Bbs757EfbBridgeIo.LoadOcr` (`required Func<PaddleOcrEngine>`) and `OcrFailed` (`required Action<Exception>`). Removed: Windows OCR, `Filter`, `Scale`, `ReadRegionAsync`, `Merge`, `Preprocess`, `Luminance`, `DarkestChannel`, `InvertForTest`, `IsAvailable`, the page reader's re-reads (`ControlTries`, `Score`, `SmallTextHeight`, `Merge`) and `Rereads`.

- [ ] **Step 0 [controller]: Place the models.** Copy from the 2026-10-27 benchmark's scratchpad (`…\b757-efb-ocr-reliability-2091e7\9769966b-…\scratchpad\models\v6\`) `PP-OCRv6_det_small.onnx`, `PP-OCRv6_rec_small.onnx` and `ppocrv6_small_dict.txt` (renamed `ppocrv6_dict.txt`), and from the NuGet cache (`%USERPROFILE%\.nuget\packages\rapidocrnet\4.2.0\models\v5\`) the classifier, into `DefaultFolder()`; check all four SHA-256s; write `installed.json` exactly as Global Constraints give it.
- [ ] **Step 1:** tests: `TextRecognitionEngineSlotTests` (`Get_loads_once`; `A_failed_load_is_reported_once_until_released` (two `Get`s after a throwing load: one `failed` call, one load attempt); `Release_disposes_and_the_next_Get_loads_again`; `Dispose_releases`), and in `Bbs757EfbScreenTests` for `FromEngine` (boxes and words carried, pass 'P', two lines on one row in left-to-right order); delete `Ocr_merge_keeps_pass_a_and_adds_only_new_lines_from_b`. Run them failing.
- [ ] **Step 2:** the slot; the two project files; `Bbs757EfbOcr`, `Bbs757EfbPageReader` as above; the bridge holds a `TextRecognitionEngineSlot<PaddleOcrEngine>` built from `LoadOcr` and a `failed` that calls `OcrFailed` and shows `TextRecognitionText.ReaderCannotStart` as the reader's alert; it calls `Get()` before a read (null: read nothing), `Release()` in the loop's inactive branch and `Dispose()` in `Dispose` after the loop ended; reads are logged without re-reads. `MainForm.BBS757.cs` passes `LoadOcr = () => PaddleOcrEngine.Load(TextRecognitionModels.DefaultFolder(), PaddleOcrEngine.ThreadsFor(Environment.ProcessorCount))` and `OcrFailed = ex => { Log.Warn("BBS757", $"Text recognition could not start: {ex.Message}"); TextRecognitionModels.RemoveMarker(TextRecognitionModels.DefaultFolder()); }`.
- [ ] **Step 3: The probe.** Its project follows the framework and package and links `Services/TextRecognition/PaddleOcrEngine.cs` and `TextRecognitionModels.cs`; it loads the engine once from `DefaultFolder()` with `ThreadsFor`, prints the load time and the process working set before and after; `region` reads the crop once with PaddleOCR (its filter and scale loop goes); `sweep` and `tapread` read crops the same way; `reread` prints each page's OCR time. Build it: `dotnet build tools/Bbs757EfbProbe -c Debug`.
- [ ] **Step 4: Re-record the test pages.** For `bright` and `dim`: `PROBE_SAVE=<scratch>/<set>` and `dotnet run --project tools/Bbs757EfbProbe -c Debug -- reread tests/MSFSBlindAssist.Tests/Fixtures/bbs757-efb/<set>`; copy only the `.lines.json` files back (the PNGs stay byte-identical: `git diff --stat` shows no `.png`).
- [ ] **Step 5:** the full suite. Each `Bbs757EfbScreenTests` failure is looked up on its capture: where PaddleOCR reads the page rightly and the expectation held a Windows OCR habit, the expectation changes; where the parser trips, step 6.
- [ ] **Step 6: Parser fixes, each with its test.** Add `Home_lists_exactly_the_five_apps` (dim `01-home`: the five app items and no other menu item; the 10-27 run read a stray "tlt" off an icon). Fix only what a test shows wrong, in `Bbs757EfbScreen.cs` or `Bbs757EfbVocabulary.cs`. OCR misreads the parser cannot know ("Recal", "OB/-PLAN") are recorded in Task 7's evidence, not patched.
- [ ] **Step 7:** clean Release build: `bin\x64\Release\net10.0-windows\` holds `MSFSBlindAssist.exe`, `MSFSBlindAssistUpdater.exe`, `onnxruntime.dll`, `libSkiaSharp.dll`, `RapidOcrNet.dll`, and no `models\v5\`, `Microsoft.Windows.SDK.NET.dll` or `WinRT.Runtime.dll`. Full suite green. Commit `feat(b757): the tablet reader reads with PaddleOCR`.

### Task 5: The Text recognition tab, the offer window and the wiring

**Files:**
- Create: `MSFSBlindAssist/Forms/Settings/TextRecognitionPanel.cs`, `MSFSBlindAssist/Forms/TextRecognitionOfferDialog.cs`, `MSFSBlindAssist/Services/TextRecognition/TextRecognitionOffer.cs`
- Modify: `Settings/UserSettings.cs`, `Forms/Settings/SettingsForm.cs`, `MainForm.cs`, `MainForm.MenuHandlers.cs`, `MainForm.BBS757.cs`
- Test: `tests/MSFSBlindAssist.Tests/TextRecognition/TextRecognitionOfferTests.cs`

**Interfaces:**
- Consumes: Tasks 1, 2.
- Also: add `MSFSBlindAssist/Forms/Settings/TextRecognitionPanel.cs` and `MSFSBlindAssist/Forms/TextRecognitionOfferDialog.cs` to `.claude/rules/text-recognition.md`'s paths in the same commit that creates them.
- Produces: `internal static bool TextRecognitionOffer.ShouldOffer(bool is757, bool settingOn, TextRecognitionState state, bool running)`; `UserSettings.OfferTextRecognitionFor757` (`bool`, default true, commented like `CheckForMissingDataOnStartup`); `SettingsForm` takes `TextRecognitionDownloader? textRecognitionDownloader = null` and adds the tab right after GeoNames; MainForm's `textRecognitionDownloader` field.
- The panel follows `GeoNamesPanel`'s download block (status box through `DisplayText.SetPreserveCaret`, Download disabled and Cancel enabled while running, focus handed back, unsubscribe in `Dispose`) and saves the checkbox through `ApplyTo`. The dialog follows `StartupDataDialog` (opens on its read-only status box, no `AcceptButton`, `CancelButton` = Not now, labels from `TextRecognitionText.OfferButton`, follows `StatusChanged`, the download keeps running when it closes). MainForm: the downloader's `Speak` marshalled as `locationDataDownloader`'s is (queued or immediate); in `MainForm_Shown`, after `OfferPmdgSdkSetupAsync`, offer when `currentAircraft is BBS757Definition` and `ShouldOffer`; the update check is skipped while either download runs; `Shutdown(TimeSpan.FromSeconds(2))` and `Dispose` beside the location downloader's; `BBS757MenuItem_Click` offers after `SwitchAircraft`; `ShowBBS757EfbReader` opens the dialog instead of the reader while `Installed().State` is not Ready.

- [ ] **Steps 1-5.** Tests: `ShouldOffer` truth table (each condition alone false, all true; Missing, Incomplete and Outdated offer, Ready never; a running job never); `The_same_inputs_give_the_same_answer` (no state between calls); `Setting_defaults_to_on` (`new UserSettings().OfferTextRecognitionFor757`). Build, full suite green. Commit `feat(b757): the Text recognition tab and the 757 offer`.

### Task 6: Rules and docs

**Files:**
- Modify: `.claude/rules/text-recognition.md`, `docs/text-recognition.md`; create `docs/invariants/text-recognition.md`; mirror lines and `PassingMentions` entries only where `ClaudeContextBudgetTests` asks.

- [ ] **Step 1:** the rule lines, one per mechanism, at most 400 characters, in `.claude/rules/text-recognition.md`, with their full text under `## OCR-n` in `docs/invariants/text-recognition.md`:
  - `[OCR-1]` Load the models only from a folder whose `installed.json` marker matches `TextRecognitionModels.ListVersion`; the marker is written last, after every file's SHA-256 matched; a model that fails to load removes the marker, so the next check offers the download again.
  - `[OCR-2]` The download offer at startup and at the 757 menu pick is governed only by `UserSettings.OfferTextRecognitionFor757` (`TextRecognitionOffer.ShouldOffer`), never by a remembered "Not now"; "Read the tablet screen" without the models always offers it.
  - `[OCR-3]` PaddleOCR runs at most `PaddleOcrEngine.ThreadsFor` threads, one read at a time, and is loaded only while the tablet screen window is shown: `Bbs757EfbBridge` holds it in a `TextRecognitionEngineSlot` released only from its loop's inactive branch, never under a running read.
- [ ] **Step 2:** `docs/text-recognition.md` in full: what it is, the release and its origin (RapidOCR v3.10.0 on ModelScope, the checksums), the folder and marker, the download (LOC-5's speech), the engine and its settings, the offers, the 757 reader's use, the measurements (`docs/b757/evidence/tablet-ocr.md`).
- [ ] **Step 3:** `dotnet test … --filter "FullyQualifiedName~ClaudeContextBudget"` green, doing what its messages ask (mirrors into `settings-call-sites.md`, a `mainform-*-call-sites.md` or `b757.md` as named, or `PassingMentions`). Full suite green. Commit `docs(b757): text recognition rules and doc`.

### Task 7 [controller]: The re-measure

**Files:** Create `docs/b757/evidence/tablet-ocr.md`.

- [ ] **Step 1:** the probe's `reread` with `PROBE_SAVE` over the 11 test pages and the benchmark's 12 live and OFP captures (`…\scratchpad\live\stab\*__g01.png`, `live\stab2\*__g01.png`); the lines converted to the benchmark's results format.
- [ ] **Step 2:** `python -I scripts/score.py` (the benchmark's) against `gt/all` and `gt/model-live`: items read exactly, CER, small text; the parser level on the 11 pages from the fixture tests and `ocrbench model`; the probe's time per page, engine load time and working set (models loaded and freed).
- [ ] **Step 3:** write the evidence file: method, before (Windows OCR) and after (as shipped), the remaining misreads, the owner's PC; no ground truth, OFP text or SimBrief data copied in. Commit `docs(b757): the tablet reader's PaddleOCR measurements`.

### Task 8 [owner present]: Deploy and the owner's checks

- [ ] **Step 1 [controller, simulator up, MSFSBA closed]:** the probe's `tour 3` from Home and from Settings: each page landed and read the same three times; recorded in the evidence file.
- [ ] **Step 2 [controller]:** `Get-Process MSFSBlindAssist` shows none; read `(Get-Item C:\MSFSBA-B757\MSFSBlindAssist.dll).VersionInfo.ProductVersion` and check its commit is an ancestor of HEAD (merge it in first if not); build Release after the last commit; `robocopy` to `C:\MSFSBA-B757` (rc below 8; the old Windows SDK DLLs stay behind, harmless); the models stay placed as in Task 4 Step 0.
- [ ] **Step 3 [owner]:** the In-sim checks below, one per message; results in the evidence file and the Execution record.

### Task 9 [controller, owner's word]: The public release

- [ ] **Step 1:** fetch ModelScope's `https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt` into an empty folder; its SHA-256 must be `b5f2bfe2…401c5d`; stop and ask the owner if not.
- [ ] **Step 2:** assets in one folder: the four files, `LICENSE-PaddleOCR.txt` (the Apache 2.0 text), `README.txt` (what the files are, their origin URLs and checksums, that the app downloads them itself); notes saying the same in two sentences.
- [ ] **Step 3:** `gh release create text-recognition-1 --repo oasis1701/msfs-blind-assist --title "Text recognition models 1 (PaddleOCR PP-OCRv6 small)" --notes-file <notes> --latest=false <assets>`.
- [ ] **Step 4:** `gh api repos/oasis1701/msfs-blind-assist/releases/latest --jq .tag_name` still names the app's latest version; each file fetched through `BaseUrl` matches its SHA-256. Record in the Execution record.

## In-sim checks (owner)

The models are in place (Task 4 Step 0); this run tests reading, not the download.

1. Read the tablet screen: pages read and navigated, the dense ones included (OFP, ACARS, performance); how accurate and how steady the reads sound.
2. With the models moved aside (Claude moves them): pick the 757, and the offer opens (listen whether "Switched to …" is still heard before the window's own speech); Not now; then Read the tablet screen opens the offer instead of the reader.
3. Untick the box in Settings, Text recognition; pick the 757: no offer.
4. Start the app on the 757 with the models moved aside: the offer comes after the other startup windows.
5. After Task 9 (the release exists), with the models moved aside: the first-run download through the app: the offer, Download, the progress speech and "Text recognition ready.", then Read the tablet screen opens the reader and reads.

## Handoff to plan 16

- **Pilot-facing:** "The 757 tablet screen reader now reads with PaddleOCR text recognition, much more accurately. It needs a one-time 32 megabyte download, offered when you select the BlueBird 757 and in Settings, Text recognition." Changelog `improvement`; the guide chapter for the Text recognition tab, the offer and the reader's refusal (strings in `TextRecognitionText`).
- **docs/b757.md:** the EFB section names PaddleOCR and the download; plan 11's "Build output folder" handoff no longer depends on a Windows SDK framework (the pinned folder lines stay).
- **docs/updates.md and UPD-9:** the startup update check is now also skipped while the text recognition download runs (`MainForm_Shown`), as it is for the location data download.
- **The index:** list plan 17 (it could not edit the frozen `00-index.md`).

## Execution record

- **Status:** claimed
- **Claimed by:** Robin Kipp (robin24), 2026-10-28
- **Worked by:** Robin Kipp with Claude (subagent-driven development)
- **Started:** 2026-10-28, from `B757` 71532bf72; rebased onto `B757` b7d473fcf on 2026-10-29 (full suite 19,195 passed)
- **Landed:** not yet: built and reviewed on `B757-p17-tablet-paddleocr`, held back from `B757` until the release exists (the owner's ruling, 2026-10-29: teammates' readers would refuse and their downloads fail before it)
- **Tests at landing:** 2026-10-29 on 082500fc2: full suite 19,055 passed, 0 failed (18,887 at the claim)
- **Tasks:** 0 done (claim 218be890c). 1 done (d0217e0c6). 2 done (1e5fe2c23, 107d61b33). 3 done (40e83d11e). 4 done (c36560152, 7fccff974, c0d978947; Step 0, the models placed by hand, done by the controller, and placed again on the real disk on 2026-10-29: the first copy had landed only in a sandboxed view of `%APPDATA%`). 5 done (0b1d852e3). 6 done (0d9106572, fc91d72c6). 7 done (ef272b03d, `docs/b757/evidence/tablet-ocr.md`). Each task reviewed, then one whole-branch review and one fix wave (3d67e8fbc, 082500fc2). 8 done 2026-10-29 (5ff4d2906 deployed to `C:\MSFSBA-B757`; the probe's tour from Home and from Settings, and the credential rows, in the evidence file; the owner's checks 1-4 passed). 9 done 2026-10-29 at the owner's word: ModelScope's `ppocrv6_dict.txt` matched b5f2bfe2…401c5d; https://github.com/oasis1701/msfs-blind-assist/releases/tag/text-recognition-1 published, not Latest (v9.1.0 stayed Latest), the tag on main's 279e0a9fb, the four files plus `LICENSE-PaddleOCR.txt` (apache.org's `LICENSE-2.0.txt` byte for byte) and `README.txt`; each file fetched through `BaseUrl` matched its SHA-256; no workflow ran.
- **Waiting on the owner:** in-sim check 5 (the first-run download), then the push to `B757`.
- **In-sim results:** 2026-10-29, the owner by ear on the deployed 5ff4d2906: checks 1-4 passed (reading; the offer at the 757 pick and the reader's refusal with the models aside; no offer with the box unticked; the offer at startup, after the other startup windows). Whether "Switched to …" is still heard before the offer's own speech was not reported. Check 5 waits for the release.
- **Changed from the plan:**
  - OCR-3's line names both release points ("released only on its own loop (the inactive branch, and when the loop ends)"); Task 6's text above has the plan's narrower wording.
  - Two more rule lines, from the reviews: OCR-4 (a model file is kept only by its SHA-256, never by its size) and OCR-5 (status boxes and offer buttons read `DisplayStatus()`/`Installed()`, never `Status`). OCR-1 and OCR-2 are also mirrored into `b757.md` and OCR-2 into `mainform-main-call-sites.md`, where their code lives.
  - The reader tries a failed engine again when the pilot asks for it again ("Read the tablet screen") after the models are back, not only after the window was hidden and shown (the Review Focus wording): the planned behavior left a blind pilot in silence after following the reader's own advice. F5 inside the reader does not retry.
  - The engine failure is a lasting status (`Status`), not an 8-second alert, and a later status no longer hides it.
  - Texts: an Idle line for an incomplete install; "1 megabyte", "less than 1 megabyte" and no "Downloading…" sentence when nothing is fetched; a 404 reason ("the files were not found on GitHub"); a cancelled repair no longer says "0 megabytes".
  - The download job hashes the files present to decide what it fetches (a same-size damaged file after a failed load is fetched again).
  - Parser changes for PaddleOCR's padded boxes, each with its test: column headings name no button; a figure is never snapped away; a side-menu icon is no entry; SET UP names its failure row; a known label ending in ":" keeps the rest as its value (the Failures page's "off"). One changed expectation ("Set UP, L engine fire"). `RegionsToReread` and `EmptySlots` removed (dead once the re-reads went).
  - The dictionary is named `ppocrv6_dict.txt`, as ModelScope and RapidOcrNet name it.
  - A bridge test file, `tests/MSFSBlindAssist.Tests/Bbs757EfbBridgeTextRecognitionTests.cs`, beside the listed test folder: it tests plan 17's change to the bridge, and under `TextRecognition/` its name would load OCR-1 to OCR-3 twice.
- **Open after the final review (the owner's call, or later work):**
  - F5 and "Read the tablet again" inside the reader do not retry a failed engine after a new download, and the "could not start" status names no button: a clause in `TextRecognitionText.ReaderCannotStart`, or F5 retrying too.
  - A retry whose load fails again is not spoken again (the shared EFB window skips an identical alert); one `Push()` after the status is taken back would fix it.
  - A second "Read the tablet screen" press during a failing load can carry a stale Ready verdict into one extra load attempt; clearing the retry flag in the slot's failure callback would close it.
  - Deferred from the task reviews, all judged able to wait: the downloader's private helpers near-copy `LocationDataDownloader`'s (sharing them needs a Geography edit); its lifecycle edges (Cancel or Shutdown after Dispose, a throwing status listener); stalls and retries do not show in the status box; the column-heading rule is broader than its purpose (watch the probe's tour); the offer dialog's status choice and Close rule live in form code, untested, and it snapshots its status in the constructor; `MarkerVersion`'s IO-failure branch and `RemoveMarker` throwing are untested; wording (`TextRecognitionModels` called "pure", "one thread owns the slot", "the rest of the feature is internal", `mainform-main-call-sites.md`'s two-line header); the credential rows (SimBrief, Hoppie, SayIntentions) are not yet read with PaddleOCR (check in the tour; keep its output out of the evidence); the Release output carries two harmless `onnxruntime*.lib` files.
- **Rulings made while working:** 2026-10-28, the owner with Claude: the design above (no Windows OCR fallback; v6 small; the tab and offers; the app's own GitHub release, created last; plan 17 may change plan 11's files and the frozen files listed); before the release, the models are placed by hand, with no test hook in the app. 2026-10-29, the owner: plan 17 goes to `B757` only after the release exists; the release's notes and README describe the models generically ("OCR-related features"), naming neither BlueBird nor the 757, which is unreleased. The controller's rulings while building (accepted deviations, wording, the extra rules and mirrors, the retry) are listed under "Changed from the plan" and in the commit messages.
