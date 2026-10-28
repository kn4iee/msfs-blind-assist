# Text recognition (the BlueBird 757 tablet reader)

The BlueBird 757's tablet is drawn inside the aircraft's WASM, so there is no text a screen reader
can reach: the app captures its screen and reads it with PaddleOCR (PP-OCRv6 small), run through
RapidOcrNet and ONNX Runtime. The models are not shipped with the app: each pilot downloads them
once, 32 megabytes, from Settings, Text recognition, or from the offer the app makes when the 757 is
selected, and the reader then works offline. Without them the reader refuses and offers the download;
there is no fallback to Windows OCR. Rules: `.claude/rules/text-recognition.md` (none yet). The rest
of this page, the downloader, the tab and the offer, and the engine, is written as those parts land.

## The model files

`TextRecognitionModels.Files` is the pinned list (list version 1), downloaded from the GitHub release
`text-recognition-1` (`TextRecognitionModels.BaseUrl`). The tag is deliberately not a version, so the
updater, which reads every release's tag as one, never offers it. Together the files are 32,257,432
bytes.

- `PP-OCRv6_det_small.onnx`, 9,929,594 bytes, finds the lines of text.
  SHA-256 `090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f`.
- `PP-OCRv6_rec_small.onnx`, 21,234,383 bytes, reads each line.
  SHA-256 `6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884`.
- `ppocrv6_dict.txt`, 74,947 bytes, the recognizer's character dictionary.
  SHA-256 `b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d`.
- `ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx`, 1,018,508 bytes, the text-line orientation
  classifier. RapidOcrNet loads it whenever it loads the models, so it stays in the list.
  SHA-256 `54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7`.

## The folder and the marker

The files live in `%APPDATA%\MSFSBlindAssist\text-recognition` (`TextRecognitionModels.DefaultFolder`).
A finished download writes `installed.json` there LAST, after every file was verified:
`{"version":1,"files":[{"name":"…","sha256":"…"},…]}`, compact, in the list's order. A download in
progress writes `<name>.part`, which never counts.

`TextRecognitionModels.Check` reads the folder and answers one of four states:

- **Missing**: no marker, and no listed file at its size.
- **Incomplete**: some listed files at their size but no marker (a cancelled, failed or killed
  download, or one that died before writing the marker), or this version's marker with a file missing
  or cut short (an antivirus quarantine after the install).
- **Outdated**: the marker is of another list version, or cannot be read. A new app version that
  changes the models bumps `ListVersion`.
- **Ready**: this version's marker and every listed file at its exact size.
