using System.Text;
using System.Text.Json;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What a save of one value did.</summary>
public enum Bbs757SaveStatus
{
    /// <summary>The value was written.</summary>
    Saved,

    /// <summary>The file held the value already: nothing was written.</summary>
    Unchanged,

    /// <summary>Nothing was written, for the result's reason.</summary>
    Refused,
}

/// <summary>A save's result; <paramref name="Reason"/> says why it was refused, in words that follow "Couldn't save &lt;label&gt;: ".</summary>
public sealed record Bbs757SaveResult(Bbs757SaveStatus Status, string? Reason = null);

/// <summary>An edit's result: the whole changed file, or why it cannot be made (then <paramref name="Bytes"/> is null).</summary>
public sealed record Bbs757EditResult(byte[]? Bytes, string? Refusal);

/// <summary>
/// Changes one value in one of BlueBird's settings files, <c>efb_settings.json</c> or a registration's
/// <c>liveries\&lt;registration&gt;-opt.json</c>, and nothing else (rule [B757-17]). The bytes of that value's token are
/// replaced and every other byte stays as BlueBird wrote it; the result is parsed and checked (the value reads back,
/// every other value is unchanged); only then is it written to <c>&lt;file&gt;.msfsba-tmp</c> beside the file and moved
/// over it. A save writes only while the caller's guard allows, asked before reading and again just before writing: no
/// simulator may run, since BlueBird reads these files once, when the 757 loads, and writes its own copy back while it is
/// loaded (docs/b757/evidence/efb-settings.md, section 1). It never creates a file, and refuses rather than guesses: a
/// missing file or key, a value of another type, a file that is not JSON or that names a key on the way twice. The files
/// hold the pilot's SimBrief ID, Hoppie logon code and SayIntentions ACARS key: their content is never logged, and a
/// changed value only when the caller says the setting may be logged.
/// </summary>
public static class Bbs757SettingsFile
{
    public const string SimRunningReason = "the simulator is running";
    public const string MissingReason = "BlueBird's settings file is missing";
    public const string UnreadableReason = "the file could not be read";
    public const string NotInFileReason = "this BlueBird version doesn't have it";
    public const string VerifyReason = "the file would have changed unexpectedly";
    public const string InUseReason = "the file is in use";

    // Beside the file, so the move that replaces it is a rename within one folder.
    private const string TempSuffix = ".msfsba-tmp";

    // Larger than any settings file BlueBird writes (about 1 KB): a file past this is not one of them and is not read.
    private const long MaxFileBytes = 256 * 1024;

    private static readonly byte[] Utf8Bom = { 0xEF, 0xBB, 0xBF };

    // As lenient as Bbs757WorkFolder's readers. A comment or a trailing comma is read past and kept, as every byte is.
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonReaderOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// The file parsed, or null when it is missing, larger than any of BlueBird's files, unreadable or not JSON. A shared
    /// read: BlueBird may be writing the file at this moment. The caller disposes the document.
    /// </summary>
    public static JsonDocument? Read(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream, DocumentOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            // The content is never logged: the file holds the pilot's codes.
            return null;
        }
    }

    /// <summary>
    /// The value at <paramref name="path"/> (keys dotted through the file's objects: <c>options.egpws_callouts.80</c>) as
    /// <paramref name="type"/>, or null when a key on the way is missing or holds no object, or the value does not fit the
    /// type (<see cref="Bbs757Value.From"/>).
    /// </summary>
    public static Bbs757Value? ValueAt(JsonElement root, string path, Bbs757ValueType type)
    {
        JsonElement element = root;
        foreach (string key in path.Split('.'))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out JsonElement inner)) return null;
            element = inner;
        }
        return Bbs757Value.From(element, type);
    }

    /// <summary>
    /// <paramref name="original"/> with the value at <paramref name="path"/> changed to <paramref name="value"/> and every
    /// other byte kept, or why not: <see cref="UnreadableReason"/> when the file is not JSON or names a key on the path
    /// twice (which one BlueBird reads is unknown); <see cref="NotInFileReason"/> when the key is missing, holds an object
    /// or a list, or holds a value that <paramref name="value"/>'s type does not read (<see cref="Bbs757Value.From"/>);
    /// <see cref="VerifyReason"/> when the result, parsed, does not read back the value or differs anywhere else. A UTF-8
    /// byte order mark before the JSON is kept. Pure. Throws <see cref="ArgumentException"/> for a float that is not
    /// finite (<see cref="Bbs757Value.ToJson"/>).
    /// </summary>
    public static Bbs757EditResult Edit(byte[] original, string path, Bbs757Value value)
    {
        byte[] token = Encoding.UTF8.GetBytes(value.ToJson());
        int bom = BomLength(original);

        int start, length;
        try
        {
            string? refusal = FindToken(original.AsSpan(bom), path.Split('.'), value.Type, out start, out length);
            if (refusal != null) return new Bbs757EditResult(null, refusal);
        }
        catch (JsonException)
        {
            return new Bbs757EditResult(null, UnreadableReason);
        }
        start += bom;

        byte[] edited = new byte[original.Length - length + token.Length];
        original.AsSpan(0, start).CopyTo(edited);
        token.CopyTo(edited, start);
        original.AsSpan(start + length).CopyTo(edited.AsSpan(start + token.Length));

        string? problem = Verify(original, edited, path, value);
        return problem == null ? new Bbs757EditResult(edited, null) : new Bbs757EditResult(null, problem);
    }

    /// <summary>
    /// Changes the value at <paramref name="path"/> in the file to <paramref name="value"/>, in this order: refused while
    /// <paramref name="mayWrite"/> says no; the file read (missing: <see cref="MissingReason"/>; open elsewhere for writing:
    /// <see cref="InUseReason"/>); <see cref="Bbs757SaveStatus.Unchanged"/>, with nothing written, when it holds the value
    /// already; the <see cref="Edit"/>, refused for its reason; <paramref name="mayWrite"/> asked again, as the simulator
    /// may have started meanwhile; the safety copy (<paramref name="backup"/>); the changed bytes written to
    /// <c>&lt;file&gt;.msfsba-tmp</c> and moved over the file (failing: the temp file removed, <see cref="InUseReason"/>).
    /// A refused save leaves the file as it was. Logs the key, with the value only when <paramref name="mayLog"/> (never for
    /// the pilot's identifiers). Never throws for the file; throws <see cref="ArgumentException"/> for a float that is not
    /// finite, as <see cref="Edit"/> does.
    /// </summary>
    public static Bbs757SaveResult Save(string filePath, string path, Bbs757Value value, Func<bool> mayWrite,
        Bbs757SettingsBackup backup, bool mayLog)
    {
        if (!mayWrite()) return Refused(SimRunningReason);

        byte[] original;
        try
        {
            // Not shared with a writer: a file another program has open for writing is in use.
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes) return Refused(UnreadableReason);
            original = new byte[stream.Length];
            stream.ReadExactly(original);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Refused(MissingReason);
        }
        catch (IOException)
        {
            return Refused(InUseReason);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or ArgumentException or NotSupportedException
                                       or System.Security.SecurityException)
        {
            return Refused(UnreadableReason);
        }

        if (Holds(original, path, value)) return new Bbs757SaveResult(Bbs757SaveStatus.Unchanged);

        var edit = Edit(original, path, value);
        if (edit.Bytes is not { } changed) return Refused(edit.Refusal ?? UnreadableReason);

        if (!mayWrite()) return Refused(SimRunningReason);
        backup.BeforeFirstWrite(filePath);

        string temp = filePath + TempSuffix;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(changed);
                // On the disk before the move, so a power cut cannot leave the file renamed but empty.
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            TryDelete(temp);
            return Refused(InUseReason);
        }

        Log.Info("BBS757", mayLog ? $"settings: {path} = {value.ToJson()}" : $"settings: {path} changed");
        return new Bbs757SaveResult(Bbs757SaveStatus.Saved);
    }

    /// <summary>
    /// <see cref="Save"/> in each registration's file of the work folder (<see cref="Bbs757WorkFolder.RegistrationFiles"/>),
    /// each on its own: a file that cannot be changed is reported, and the others are saved all the same. By registration;
    /// empty when BlueBird has made no registration file yet.
    /// </summary>
    public static IReadOnlyList<(string Registration, Bbs757SaveResult Result)> SaveToRegistrations(string workFolder,
        string path, Bbs757Value value, Func<bool> mayWrite, Bbs757SettingsBackup backup, bool mayLog)
    {
        var results = new List<(string Registration, Bbs757SaveResult Result)>();
        foreach (var (registration, file) in Bbs757WorkFolder.RegistrationFiles(workFolder))
            results.Add((registration, Save(file, path, value, mayWrite, backup, mayLog)));
        return results;
    }

    /// <summary>
    /// Null when <paramref name="edited"/> reads back <paramref name="value"/> at <paramref name="path"/> and holds every
    /// other key of <paramref name="original"/>, in its order, with the same text for every other value; else
    /// <see cref="VerifyReason"/>. The check after the splice in <see cref="Edit"/>; internal for its tests
    /// (Properties/InternalsVisibleTo.cs).
    /// </summary>
    internal static string? Verify(byte[] original, byte[] edited, string path, Bbs757Value value)
    {
        using JsonDocument? before = Parse(original);
        using JsonDocument? after = Parse(edited);
        bool changedOnlyThere = before != null && after != null
            && SameElsewhere(before.RootElement, after.RootElement, path.Split('.'), 0)
            && ValueAt(after.RootElement, path, value.Type) == value;
        return changedOnlyThere ? null : VerifyReason;
    }

    /// <summary>
    /// Where the value at <paramref name="keys"/> is in <paramref name="json"/>: its token's first byte and length (a
    /// string's quotes and escapes included), or why there is none to replace. Reads to the end, so a file that is not JSON
    /// throws <see cref="JsonException"/> even when the value comes before the fault.
    /// </summary>
    private static string? FindToken(ReadOnlySpan<byte> json, string[] keys, Bbs757ValueType type, out int start, out int length)
    {
        start = -1;
        length = 0;
        var reader = new Utf8JsonReader(json, ReaderOptions);
        // For each open object or list, how many of the keys lead to it: 0 for the root object, -1 off the path or in a list.
        var levels = new Stack<int>();
        var seen = new int[keys.Length];   // the keys met on the path, at each level
        int valueLevel = -1;               // the level an object gets as the coming value
        bool valueIsTarget = false;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                {
                    int level = levels.Peek();
                    bool onPath = level >= 0 && level < keys.Length && reader.ValueTextEquals(keys[level]);
                    // A key on the path written twice: which one BlueBird reads is unknown.
                    if (onPath && ++seen[level] > 1) return UnreadableReason;
                    valueIsTarget = onPath && level == keys.Length - 1;
                    valueLevel = onPath ? level + 1 : -1;
                    break;
                }
                case JsonTokenType.StartObject:
                    if (valueIsTarget) return NotInFileReason;
                    levels.Push(levels.Count == 0 ? 0 : valueLevel);
                    valueIsTarget = false;
                    valueLevel = -1;
                    break;
                case JsonTokenType.StartArray:
                    if (valueIsTarget) return NotInFileReason;
                    levels.Push(-1);
                    valueIsTarget = false;
                    valueLevel = -1;
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    levels.Pop();
                    break;
                default:   // a string, a number, true, false or null
                    if (valueIsTarget)
                    {
                        start = checked((int)reader.TokenStartIndex);
                        // A string's value span is its raw text, escapes and all, without the two quotes.
                        length = reader.ValueSpan.Length + (reader.TokenType == JsonTokenType.String ? 2 : 0);
                        if (Bbs757Value.From(JsonElement.ParseValue(ref reader), type) is null) return NotInFileReason;
                    }
                    valueIsTarget = false;
                    valueLevel = -1;
                    break;
            }
        }
        return start < 0 ? NotInFileReason : null;
    }

    // Whether two elements are the same but for the value at `keys`: the same kind, an object's keys in the same order, a
    // list's items, every other value's raw text. `level` is how many of the keys lead to these two, -1 off the path.
    private static bool SameElsewhere(JsonElement before, JsonElement after, string[] keys, int level)
    {
        if (before.ValueKind != after.ValueKind) return false;
        switch (before.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var b = before.EnumerateObject();
                var a = after.EnumerateObject();
                while (true)
                {
                    bool more = b.MoveNext();
                    if (more != a.MoveNext()) return false;
                    if (!more) return true;
                    string name = b.Current.Name;
                    if (name != a.Current.Name) return false;
                    int next = level >= 0 && level < keys.Length && name == keys[level] ? level + 1 : -1;
                    if (next == keys.Length) continue;   // the value changed, read back on its own
                    if (!SameElsewhere(b.Current.Value, a.Current.Value, keys, next)) return false;
                }
            }
            case JsonValueKind.Array:
            {
                var b = before.EnumerateArray();
                var a = after.EnumerateArray();
                while (true)
                {
                    bool more = b.MoveNext();
                    if (more != a.MoveNext()) return false;
                    if (!more) return true;
                    if (!SameElsewhere(b.Current, a.Current, keys, -1)) return false;
                }
            }
            default:
                return before.GetRawText() == after.GetRawText();
        }
    }

    // Whether the bytes hold the value at the path already, so a save has nothing to write.
    private static bool Holds(byte[] bytes, string path, Bbs757Value value)
    {
        using JsonDocument? doc = Parse(bytes);
        return doc != null && ValueAt(doc.RootElement, path, value.Type) == value;
    }

    // The bytes parsed, after a UTF-8 byte order mark when one leads; null when they are not JSON.
    private static JsonDocument? Parse(byte[] bytes)
    {
        int bom = BomLength(bytes);
        try { return JsonDocument.Parse(new ReadOnlyMemory<byte>(bytes, bom, bytes.Length - bom), DocumentOptions); }
        catch (JsonException) { return null; }
    }

    private static int BomLength(byte[] bytes) => bytes.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;

    private static Bbs757SaveResult Refused(string reason) => new(Bbs757SaveStatus.Refused, reason);

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException) { }
    }
}

/// <summary>
/// The safety copy of BlueBird's settings files: before the first write to a file, the file as it was is copied to the
/// backup folder under its own name, replacing the copy an earlier session left (one copy is kept for each file). Once
/// for each file in this object's life, so the copy always holds the file from before the first change.
/// </summary>
public sealed class Bbs757SettingsBackup
{
    private readonly string _folder;
    private readonly HashSet<string> _copied = new(StringComparer.OrdinalIgnoreCase);

    public Bbs757SettingsBackup(string folder) => _folder = folder;

    /// <summary><c>%APPDATA%\MSFSBlindAssist\backups\bbs757</c>.</summary>
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "backups", "bbs757");

    /// <summary>
    /// Copies the file to the backup folder if this object has not tried to for that file yet. A copy that fails is not
    /// tried again (the file may have changed since) and never stops the write: never throws.
    /// </summary>
    public void BeforeFirstWrite(string filePath)
    {
        try
        {
            if (!_copied.Add(Path.GetFullPath(filePath))) return;
            Directory.CreateDirectory(_folder);
            File.Copy(filePath, Path.Combine(_folder, Path.GetFileName(filePath)), overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or System.Security.SecurityException)
        {
            // Best effort: the save goes on without a copy.
        }
    }
}
