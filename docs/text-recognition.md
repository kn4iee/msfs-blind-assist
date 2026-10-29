# Text recognition (the BlueBird 757 tablet reader)

The BlueBird 757's tablet is drawn inside the aircraft's WASM, so there is no text a screen reader
can reach: the app captures its screen and reads it with PaddleOCR (PP-OCRv6 small), run through
RapidOcrNet and ONNX Runtime. The engine ships inside the app; the models do not. Each pilot downloads
them once, 32 megabytes, from Settings, Text recognition, or from the offer the app makes when the 757
is selected, and the reader then works offline. Without them the reader refuses and offers the
download; there is no fallback to Windows OCR. Rules: `.claude/rules/text-recognition.md` (OCR-n).

## Why PaddleOCR, and why a download

Until plan 17 the reader used Windows OCR, which needed two passes, a gray filter, upscaling and
close-up re-reads for the small and colored text it missed, and still read fewer of the tablet's items
right than PaddleOCR does in one plain pass at the tablet's own size (Measurements, below). The owner's
rulings of 2026-10-28:

- **No models, no reading.** The reader refuses and offers the download; it never reads with a worse
  engine. Windows OCR left the app together with its Windows SDK files (the target framework went from
  `net10.0-windows10.0.19041.0` to `net10.0-windows`), so the app grows by only a few megabytes.
- **PP-OCRv6 small**, 32 megabytes, not tiny or medium.
- **The Text recognition tab, and an offer** when the 757 starts or is picked, governed by one checkbox.
  A "Not now" is never remembered (OCR-2).
- **The four files on a release of the app's own GitHub repository**, each pinned by SHA-256 in the
  app, created last.
- **The engine inside the app**: RapidOcrNet 4.2.0, ONNX Runtime and SkiaSharp. Downloading the engine
  too, and shipping the models in the app, were both ruled out.

## The model files

`TextRecognitionModels.Files` is the pinned list (list version 1). The files are published on the app's
own GitHub release `text-recognition-1`, titled "Text recognition models 1 (PaddleOCR PP-OCRv6
small)", and each is fetched from `TextRecognitionModels.BaseUrl`,
`https://github.com/oasis1701/msfs-blind-assist/releases/download/text-recognition-1/`, plus its name.
Together the files are 32,257,432 bytes.

- The tag is deliberately not a version: the updater reads every release's tag as one
  (`UpdateCandidateSelector`) and skips only what does not parse, so a tag that parsed would be offered
  to pilots as an app update (`The_release_tag_is_no_version`). The release is created with
  `--latest=false`, so `releases/latest` still names the app's newest version.
- It holds the four files, the Apache 2.0 license text (`LICENSE-PaddleOCR.txt`) and a `README.txt`
  that says what the files are, where they come from and that the app downloads them itself.
- It is created by hand and last (plan 17's final task, with the owner's word), once the reader has been
  checked in the sim with the models placed by hand. The app has no test hook for the download; the
  suite tests the download against a fake server. A 404 (no release yet, or a file missing from it) is
  told apart from a network failure: "the files were not found on GitHub".

**Where the files come from.** They are the ONNX files of PaddleOCR PP-OCRv6 small as RapidOCR v3.10.0
publishes them on ModelScope (`RapidAI/RapidOCR`). RapidOCR's `default_models.yaml` pins the detector, the
recognizer and the text-line classifier by SHA-256, and the values below are those. The dictionary is
RapidOCR's `ppocrv6_dict.txt` for `PP-OCRv6_rec_small`, for which RapidOCR pins no hash: its SHA-256
below is that of the copy the benchmark used, and the release step fetches ModelScope's file,
`https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt`,
and stops unless its SHA-256 is the one pinned here. The models are PaddlePaddle's, under Apache 2.0;
the Text recognition tab's credits box says so (below).

- `PP-OCRv6_det_small.onnx`, 9,929,594 bytes, finds the lines of text.
  SHA-256 `090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f`.
- `PP-OCRv6_rec_small.onnx`, 21,234,383 bytes, reads each line.
  SHA-256 `6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884`.
- `ppocrv6_dict.txt`, 74,947 bytes, the recognizer's character dictionary.
  SHA-256 `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d`.
- `ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx`, 1,018,508 bytes, the text-line orientation
  classifier. RapidOcrNet loads it whenever it loads the models, so it stays in the list although the
  reader never turns angle detection on.
  SHA-256 `54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7`.

"32 megabytes" is decimal, rounded to the nearest (a half up): 32,257,432 bytes. Every size the app
tells goes through `TextRecognitionText.MegabytePhrase`: "N megabytes", "1 megabyte" for a size that
rounds to one and "less than 1 megabyte" for a sliver (the dictionary alone is 74,947 bytes), never
"1 megabytes" or "0 megabytes".

## The folder and the marker

The files live in `%APPDATA%\MSFSBlindAssist\text-recognition` (`TextRecognitionModels.DefaultFolder`).
A finished download writes `installed.json` there LAST, after every file was verified:
`{"version":1,"files":[{"name":"…","sha256":"…"},…]}`, compact, in the list's order. A download in
progress writes `<name>.part`, which never counts. A folder filled by hand with the four files and a
marker in this exact format reads Ready; plan 17's in-sim checks of the reader are set up that way, before
the release is published, so they test the reading and not the download.

`TextRecognitionModels.Check` reads the folder and answers one of four states (OCR-1):

- **Missing**: no marker, and no listed file at its size.
- **Incomplete**: some listed files at their size but no marker (a cancelled, failed or killed
  download, one that died before writing the marker, or a model that failed to load and took the marker
  with it), or this version's marker with a file missing or cut short (an antivirus quarantine after the
  install).
- **Outdated**: the marker is of another list version, or cannot be read. A new app version that
  changes the models bumps `ListVersion`.
- **Ready**: this version's marker and every listed file at its exact size.

Every check is cheap (file sizes only, and the folder is never created). The only code that hashes the
files is the download job (OCR-4).

## Downloading

`TextRecognitionDownloader`, owned by MainForm so a download keeps running when Settings or the offer
window closes, and started from the tab or the offer window. It holds no announcer and touches no UI: it
raises `StatusChanged` and `Speak` on its worker threads, and the listeners marshal (MainForm with
`SafeBeginInvoke`; the tab and the window with a `BeginInvoke` inside try/catch, A380C-6). It follows
`LocationDataDownloader` (LOC-5's speech: milestones, stalls and the end, never per chunk) with a
smaller job: whole-file requests, no resume, no build step. One job at a time: a press while a job runs
starts nothing, so the tab and the window pressed together make one job and request each file once
(`Start_while_running_starts_nothing`).

The job:

1. Creates the folder and deletes every `<name>.part` a killed job left (never resumed, never counted:
   `A_part_file_left_by_a_killed_job_is_fetched_again`), then checks the disk: at least 100,000,000 bytes
   free on the folder's drive, else it fails before any request.
2. Hashes the files already in the folder. A file is kept only when it is there at its exact size with
   the listed SHA-256 (OCR-4), so a damaged file of the right size is fetched again and the good ones are
   not. With every file kept there is no download: the job writes the marker and says only that it is
   ready.
3. Says "Downloading text recognition, {size}." for what it still fetches ({size} is told as under "The
   model files": "32 megabytes", "1 megabyte", "less than 1 megabyte"), then fetches each other file
   whole into `<name>.part`, hashes it and renames it into place. A failed attempt (the network, a
   read that stalled, a wrong hash) is tried again after 2 s, then 4 s, up to 3 attempts for each file;
   a 404 and a failing disk end the job at once. A watchdog speaks a stall at 20 s without data and
   abandons the attempt at 60 s (`StallDetector`, shared with the location data download).
4. Removes the files in the folder that the list does not name (models of an older list, anything else)
   and writes the marker last.

Cancel keeps every verified file: the next job hashes them, keeps them and fetches only the rest
(`Cancel_keeps_verified_files_and_the_next_job_fetches_only_the_rest`). A failed job keeps them too.
Closing the app cancels without a word (`Shutdown`, which waits up to 2 s for the job to let go of its
files), and a `.part` file that a kill leaves is deleted by the next job. The startup update check is
skipped while this download runs, as it is for the location data download (UPD-9, OCR-2).

### What a pilot hears

Speech follows LOC-5 and CORE-7: the screen reader already reads every control and its label, so the app
speaks only what it cannot, and never per chunk. Immediate speech interrupts what is being said; queued
speech waits its turn, so progress never cuts off a callout. Every number is formatted with the
invariant culture, so a German or Swedish Windows hears and reads the same words.

| When | Words | How |
|---|---|---|
| The press of Download, when the job fetches something | "Downloading text recognition, 32 megabytes." (a job that resumes says its own size: "21 megabytes", "2 megabytes", "less than 1 megabyte") | immediate |
| 25, 50 and 75 percent of the whole list | "Text recognition 25 percent." (50, 75); milestones that the kept files already cover stay quiet | queued |
| No data for 20 s | "Text recognition download stalled." | queued |
| Data again after a stall | "Download resumed." | queued |
| The job ends | "Text recognition ready." / "Text recognition download failed: {reason}." / "Text recognition download cancelled." | immediate |

The reasons: "could not reach GitHub", "the files were not found on GitHub" (an HTTP 404), "a file did not match
its checksum", "not enough disk space, 100 megabytes needed", "the files could not be saved". Anything
unforeseen is passed on as the exception said it, without its closing full stop.

### What the status line says

The Settings tab and the offer window show the job's latest status in a read-only box (VAT-13),
rewritten in place at a phase change or a whole percent, so NVDA keeps its line. It comes from
`TextRecognitionDownloader.DisplayStatus()`, which reads the folder when no job ran (OCR-5):

- Missing: "Not downloaded. The download is 32 megabytes."
- Downloading: "Downloading: {p} percent of 32 megabytes."
- Ready: "Ready: PaddleOCR PP-OCRv6 small."
- Outdated: "An update is needed: this version of the app uses new text recognition files. The download is 32 megabytes."
- Incomplete: "Incomplete: {k} of 4 files are kept; the next download fetches the rest, {size}." With every
  file at its size but no marker: "Incomplete: the files are here but not checked. Download text recognition to check them."
- Failed: "The last download failed: {reason}. Download again to retry."
- Cancelled: "Download cancelled. {k} of 4 files are kept; the next download fetches the rest, {size}."
  With none kept: "Download cancelled."

{size} is what the next download still fetches, told as "N megabytes", "1 megabyte" or "less than 1
megabyte". A failure or a cancel stays on the line until the next job.

## The tab and the offers

**The tab.** Settings gains "Text recognition" right after GeoNames. It holds the intro "Text recognition
for the BlueBird 757 tablet reader (downloaded once, then used offline):", the status box, the buttons
"Download text recognition" and "Cancel download", the checkbox "Offer this download when the BlueBird
757 is selected" and a credits box: "Text recognition: PaddleOCR PP-OCRv6 small (PaddlePaddle, Apache
2.0), run with RapidOcrNet and ONNX Runtime." Download stays enabled when the models are Ready (a press
checks the files again); while a job runs it hands focus to Cancel, and back when the job ends. Only the
checkbox is saved (`UserSettings.OfferTextRecognitionFor757`, on by default).

**The offer window** (`TextRecognitionOfferDialog`), titled "Download text recognition", modal over the main
window. It opens on its read-only status box, which the screen reader reads: "The 757 tablet reader needs
text recognition: PaddleOCR, a 32 megabyte download. You can also download it later from Settings, Text
recognition." Its buttons are "&Download text recognition" and "&Not now" (the ampersand marks the access
key). There is no default button, so
Enter never starts a download; Escape is Not now. The window stays open once a download starts and
follows the job: the box shows the job's own status, as the tab does, the Download button reads
"Downloading text recognition" while it runs and "Text recognition ready" when the models are installed (a
press then starts nothing, and the button is never dimmed, since a dimmed control drops out of the tab
order), and after a failure or a cancel it reads "&Download text recognition" again. Not now reads "&Close"
as soon as a job has run or started. Closing the window never stops the download.

**When the offer is made** (OCR-2). `TextRecognitionOffer.ShouldOffer` is true when the loaded aircraft is
the BlueBird 757 profile, the checkbox is on, the models are not Ready and no download is running:

- at startup on a restored 757 profile (`MainForm_Shown`), after the startup data check and the PMDG SDK
  check and before the update check;
- after the 757 is picked in the Aircraft menu (`BBS757MenuItem_Click`, after `SwitchAircraft`);
- never at connect, where the window would open behind the simulator, unheard.

A dismissal is never remembered: the one thing that silences the offer is the checkbox. "Read the tablet
screen" (the button on the 757 EFB window, Shift+T) is not an offer: while the models are not Ready it
shows the same window instead of the reader, whatever the checkbox says. Because the window is modal and
comes before the update check, the update window never opens on top of it; a download that is still running
when the offers are done makes that launch skip the update check, and the next launch asks again.

## The engine

`PaddleOcrEngine` is the host: it loads the four models from the folder and reads one picture at a time
into `OcrLine`s (text, the rectangle around it, its words in order with their boxes, and a confidence).
`OcrLine` and `OcrWord` are public because the reader's public signatures take them; the rest of the
feature is internal.

- **Package.** RapidOcrNet 4.2.0, referenced with `ExcludeAssets="build"`: its build targets would copy
  13.7 MB of PP-OCRv5 models next to the exe. The app, the test project and the probe all target
  `net10.0-windows`.
- **Models.** `RapidOcrModelSet.PPOCRv6Small` with the four paths pointed into the folder, absolute (the
  library's own are relative to the app) and, from 260 characters on, in the extended form (`\\?\`, or
  `\\?\UNC\` for a network path) that the file calls under ONNX Runtime need past MAX_PATH.
- **Options.** `RapidOcrOptions.PPOCRv6` with word boxes on (the reader clicks on words) and angle
  detection off (the tablet is upright). The preset adds no white border and no resize cap, and raises a
  short side under 736 pixels to 736, as the v6 models were exported (the v5 defaults starve the v6
  detector). The tablet, 1397 by 1048 pixels, is read at its own size.
- **Threads.** `PaddleOcrEngine.ThreadsFor`: one per eight logical processors, at least 1, at most 4
  (4 on the owner's PC), so reading the tablet leaves the simulator its cores (OCR-3).
- **One read at a time.** A second `ReadAsync` waits outside the engine and can be cancelled there.
  The recognition runs on a thread-pool thread, so the caller's thread stays free.
- **The picture.** The tablet's `Bitmap` is copied row by row into a BGRA `SKBitmap`, with no PNG encode
  and decode between. GDI+ converts any source format to 32-bit ARGB as it locks the bits; the alpha is
  kept as it is.
- **The lines.** Blank blocks are dropped and the text is trimmed. The words are the library's own word
  boxes when it gives any; otherwise the line's box is shared out by characters. The confidence is the
  mean of the character scores (1 when it gave none).
- **Free of the app.** `PaddleOcrEngine.cs` uses only the BCL, RapidOcrNet, SkiaSharp, System.Drawing and
  `TextRecognitionModels`, and it logs nothing: a caller that cannot `Load` reports why. The 757 probe
  (`tools/Bbs757EfbProbe`) compiles it beside `TextRecognitionModels.cs`.

## How the 757 reader uses it

`Bbs757EfbOcr.ReadAsync` makes one read of the whole tablet picture, at the tablet's own size, with no
preprocessing (no gray filter, no upscaling, no second pass) and no close-up re-reads, and maps the
engine's lines to the reader's, pass P, in reading order. `Bbs757EfbPageReader` follows it with
`Bbs757EfbScreen.Parse`, and the probe uses the same two, so what the probe proves is what ships.

`Bbs757EfbBridge` holds the engine in a `TextRecognitionEngineSlot<PaddleOcrEngine>` (OCR-3):

- The engine loads on the bridge's loop, off the UI thread, at the first read while the tablet screen
  window is shown (`MainForm` supplies `PaddleOcrEngine.Load(TextRecognitionModels.DefaultFolder(), ...)`
  with `ThreadsFor(Environment.ProcessorCount)`), and is released when the window hides. The slot is
  touched only on that loop: `Release` in its inactive branch and `Dispose` in the `finally` after it
  ended, so it is never released under a running read.
- A load that fails is reported once. MainForm logs it and removes the marker (OCR-1), so the next check
  offers the download again, and the reader's status says: "Text recognition could not start, so the
  tablet cannot be read. Download it again from Settings, Text recognition." Until the window was hidden
  and shown again nothing is captured or read, and the slot does not try to load again.
- The window opens only when the models are Ready; otherwise "Read the tablet screen" shows the offer
  window instead (OCR-2).

## Measurements

The reader's accuracy and speed with Windows OCR and with PaddleOCR, the engine's load time and memory,
and what is still misread are in `docs/b757/evidence/tablet-ocr.md`.

## The code

`MSFSBlindAssist/Services/TextRecognition/`:

- `TextRecognitionModels`: the list, the release address, the folder, the marker and the installed check.
- `TextRecognitionText`: every sentence the pilot reads or hears (pure, invariant culture).
- `TextRecognitionOffer`: `ShouldOffer` (pure).
- `TextRecognitionDownloader`: the job, the status and the speech.
- `PaddleOcrEngine`: the engine host, with `OcrLine` and `OcrWord`.
- `TextRecognitionEngineSlot`: the bridge's hold on the engine.

Callers: `Forms/Settings/TextRecognitionPanel.cs` (the tab), `Forms/TextRecognitionOfferDialog.cs`
(the offer window), `MainForm.cs` (owns the downloader, speaks its words, makes the startup offer, skips
the update check while it runs, shuts it down), `MainForm.BBS757.cs` (the 757 menu offer, the refusal, the
engine seam), `Settings/UserSettings.cs` (the checkbox's setting), and in `SimConnect/BBS757/` the bridge
(`Bbs757EfbBridge`), `Bbs757EfbOcr` and `Bbs757EfbPageReader`. Logs go to debug.log under the category
`TextRecognition` (the download) and `BBS757` (the reader).

Tests in `tests/MSFSBlindAssist.Tests/TextRecognition/` need no model files: the download runs against a
fake server (fresh, retried, failed, cancelled, killed, damaged, a full disk), every sentence runs under
en-US, de-DE and sv-SE, and the engine's conversions run on fake text blocks. `Bbs757EfbScreenTests` parse
the saved PaddleOCR reads (`Fixtures/bbs757-efb/**/*.lines.json`) of 11 recorded tablet pages. Reading
real pages with the real models is the probe's job: `tools/Bbs757EfbProbe reread <dir>` runs the app's
reader on saved pictures, and `PROBE_SAVE` records the lines for the fixtures.
