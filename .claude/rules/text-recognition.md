---
paths:
  - "MSFSBlindAssist/Services/TextRecognition/**"
  - "MSFSBlindAssist/Forms/Settings/TextRecognitionPanel.cs"
  - "MSFSBlindAssist/Forms/TextRecognitionOfferDialog.cs"
  - "tests/MSFSBlindAssist.Tests/TextRecognition/**"
---
# Text recognition rules

Loaded when Claude reads matching code. Background: docs/text-recognition.md. Full text of each rule: docs/invariants/text-recognition.md.

- [OCR-1] Load the models only from a folder whose `installed.json` marker matches `TextRecognitionModels.ListVersion`; the marker is written last, after every file's SHA-256 matched; a model that fails to load removes the marker, so the next check offers the download again. Full: docs/invariants/text-recognition.md#ocr-1
- [OCR-2] The download offer at startup and at the 757 menu pick is governed only by `UserSettings.OfferTextRecognitionFor757` (`TextRecognitionOffer.ShouldOffer`), never by a remembered "Not now"; "Read the tablet screen" without the models always offers it. Full: docs/invariants/text-recognition.md#ocr-2
- [OCR-3] PaddleOCR runs at most `PaddleOcrEngine.ThreadsFor` threads, one read at a time, and is loaded only while the tablet screen window is shown: `Bbs757EfbBridge` holds it in a `TextRecognitionEngineSlot` released only on its own loop (the inactive branch, and when the loop ends), never under a running read. Full: docs/invariants/text-recognition.md#ocr-3
- [OCR-4] `TextRecognitionDownloader` keeps a model file only when its SHA-256 matched, never by its size: after a failed load all four files sit at their size with no marker, and a damaged one must be fetched again; `.part` files never count and are deleted when a job starts. Full: docs/invariants/text-recognition.md#ocr-4
- [OCR-5] Every status box and offer button reads `TextRecognitionDownloader.DisplayStatus()` (and `Installed()`), never `Status`: `Status` keeps a finished job's Ready after a failed load removed the marker, so the offer's Download button, never disabled, would read "Text recognition ready" and a press would do nothing all session. Full: docs/invariants/text-recognition.md#ocr-5
