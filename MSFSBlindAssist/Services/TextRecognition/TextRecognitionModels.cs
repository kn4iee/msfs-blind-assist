using System.Text;
using System.Text.Json;

namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>One file of the text recognition download: its name on the release and in the folder, its
/// exact size and its SHA-256 as lowercase hex.</summary>
internal sealed record TextRecognitionFile(string Name, long Bytes, string Sha256);

/// <summary>What the models folder holds, as the pilot is told: nothing, part of the files, files of an
/// older list, or all of them.</summary>
internal enum TextRecognitionState { Missing, Incomplete, Outdated, Ready }

/// <summary>The result of a check: the state, how many listed files are on disk at their size, and how
/// many bytes a download would still have to fetch.</summary>
internal sealed record TextRecognitionInstall(TextRecognitionState State, int FilesKept, long BytesToFetch);

/// <summary>
/// The text recognition models the BlueBird 757 tablet reader runs on (PaddleOCR PP-OCRv6 small): the
/// four pinned files, the release they are downloaded from, and the check that says whether the folder
/// holds them. Pure and free of UI: the downloader fetches, and the Settings tab and the offer show what
/// <see cref="Check(string)"/> says.
///
/// "Installed" is a marker file, <c>installed.json</c>, that a finished download writes LAST, after every
/// file was verified; files without it are only a download in progress or a job that died. A file counts
/// only at its exact size, so a model removed or cut short after the install (an antivirus quarantine)
/// makes the folder Incomplete again and the offer comes back.
/// </summary>
internal static class TextRecognitionModels
{
    /// <summary>The version of the file list. A new list (other models, other files) bumps it, which turns a
    /// folder installed from the old one into <see cref="TextRecognitionState.Outdated"/>.</summary>
    internal const int ListVersion = 1;

    /// <summary>The GitHub release the files are published on. It must never parse as a version:
    /// <c>UpdateCandidateSelector</c> reads every release's tag as one and skips only what does not parse, so a
    /// tag that did would be offered to pilots as an app update.</summary>
    internal const string ReleaseTag = "text-recognition-1";

    /// <summary>Each file is downloaded from this address plus its name.</summary>
    internal const string BaseUrl = "https://github.com/oasis1701/msfs-blind-assist/releases/download/" + ReleaseTag + "/";

    /// <summary>The marker a finished download writes last; its <c>version</c> says by which list.</summary>
    internal const string MarkerName = "installed.json";

    /// <summary>Free space a job requires before it fetches anything: the 32 megabytes of files with a wide margin.</summary>
    internal const long RequiredFreeBytes = 100_000_000;

    /// <summary>
    /// The files, in the order the marker lists them. Sizes and hashes are those of the files published on
    /// <see cref="ReleaseTag"/>. The classifier is in the list although the reader never turns angle detection
    /// on: RapidOcrNet loads it whenever it loads the models.
    /// </summary>
    internal static IReadOnlyList<TextRecognitionFile> Files { get; } = Array.AsReadOnly(new[]
    {
        new TextRecognitionFile("PP-OCRv6_det_small.onnx", 9_929_594, "090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f"),
        new TextRecognitionFile("PP-OCRv6_rec_small.onnx", 21_234_383, "6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884"),
        new TextRecognitionFile("ppocrv6_dict.txt", 74_947, "b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d"),
        new TextRecognitionFile("ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx", 1_018_508, "54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7"),
    });

    /// <summary>The size of all the files together: 32,257,432 bytes, "32 megabytes".</summary>
    internal static long TotalBytes => Files.Sum(file => file.Bytes);

    /// <summary>The folder the files live in, under the app's own folder in %APPDATA%.</summary>
    internal static string DefaultFolder()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "text-recognition");

    /// <summary>Megabytes as the pilot hears them: decimal (a million bytes), rounded to the nearest, a half
    /// up. 32,257,432 bytes is "32".</summary>
    internal static int Megabytes(long bytes) => (int)Math.Round(bytes / 1_000_000.0, MidpointRounding.AwayFromZero);

    /// <summary>Checks <paramref name="folder"/> against the real list at this build's list version.</summary>
    internal static TextRecognitionInstall Check(string folder) => Check(folder, Files, ListVersion);

    /// <summary>
    /// What <paramref name="folder"/> holds of <paramref name="files"/>. A file counts only when it is there at
    /// its exact size; a <c>.part</c> file (a download in flight or killed) and anything the list does not name
    /// are never looked at, and the folder is never created (a missing one is Missing).
    /// <list type="bullet">
    /// <item>No marker: Missing when no file counts, otherwise Incomplete (a cancelled, failed or killed job,
    /// or one that died before writing the marker).</item>
    /// <item>A marker of another version, or one that cannot be read: Outdated.</item>
    /// <item>This version's marker: Ready when every file counts, otherwise Incomplete (a model removed
    /// after the install).</item>
    /// </list>
    /// <c>BytesToFetch</c> is the size of the files that do not count, so 0 for Ready. For Outdated it is the same
    /// figure, although the job re-hashes the kept files anyway.
    /// </summary>
    internal static TextRecognitionInstall Check(string folder, IReadOnlyList<TextRecognitionFile> files, int listVersion)
    {
        int kept = 0;
        long toFetch = 0;
        foreach (TextRecognitionFile file in files)
        {
            if (HasSize(Path.Combine(folder, file.Name), file.Bytes)) kept++;
            else toFetch += file.Bytes;
        }

        string markerPath = Path.Combine(folder, MarkerName);
        TextRecognitionState state;
        if (!File.Exists(markerPath)) state = kept == 0 ? TextRecognitionState.Missing : TextRecognitionState.Incomplete;
        else if (MarkerVersion(markerPath) != listVersion) state = TextRecognitionState.Outdated;
        else state = kept == files.Count ? TextRecognitionState.Ready : TextRecognitionState.Incomplete;
        return new TextRecognitionInstall(state, kept, toFetch);
    }

    /// <summary>
    /// The marker's text: <c>{"version":1,"files":[{"name":"…","sha256":"…"},…]}</c>, the files in the list's order,
    /// compact, no trailing newline. Only the version is read back (by <see cref="Check(string)"/>); the names and
    /// hashes record what was verified.
    /// </summary>
    internal static string MarkerJson(IReadOnlyList<TextRecognitionFile> files, int listVersion)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", listVersion);
            writer.WriteStartArray("files");
            foreach (TextRecognitionFile file in files)
            {
                writer.WriteStartObject();
                writer.WriteString("name", file.Name);
                writer.WriteString("sha256", file.Sha256);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the marker for the real list at this build's list version. A finished job calls it last,
    /// with every file verified.</summary>
    internal static void WriteMarker(string folder) => WriteMarker(folder, Files, ListVersion);

    /// <summary>Writes the marker for <paramref name="files"/> at <paramref name="listVersion"/>: UTF-8 without a
    /// byte order mark. The folder must exist; a write that fails throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/>.</summary>
    internal static void WriteMarker(string folder, IReadOnlyList<TextRecognitionFile> files, int listVersion)
        => File.WriteAllText(Path.Combine(folder, MarkerName), MarkerJson(files, listVersion));

    /// <summary>Removes the marker and nothing else: the files stay, and the folder reads Incomplete until a job
    /// writes the marker again. No marker, or no folder, is not an error; a marker that cannot be deleted throws
    /// <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/>.</summary>
    internal static void RemoveMarker(string folder)
    {
        string path = Path.Combine(folder, MarkerName);
        if (File.Exists(path)) File.Delete(path);
    }

    private static bool HasSize(string path, long bytes)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length == bytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>The marker's <c>version</c>, or null when the file cannot be read, is not JSON, is not an object
    /// or has no integer <c>version</c>.</summary>
    private static int? MarkerVersion(string path)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("version", out JsonElement version)
                && version.ValueKind == JsonValueKind.Number
                && version.TryGetInt32(out int number)
                    ? number
                    : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
