using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// BlueBird's 757 "work" folder, where the aircraft keeps its EFB settings and the nav data it
/// downloaded, and the only two values the app reads from it: the SimBrief ID (to confirm an entry
/// made on the EFB took) and the installed AIRAC cycle (so the nav data update never clicks where
/// BlueBird shows REMOVE). Read only: the app never writes BlueBird's files.
///
/// <c>efb_settings.json</c> also holds the pilot's Hoppie logon code and SayIntentions ACARS key:
/// it is parsed for <c>simbrief_id</c> alone, nothing else is taken from it, and neither file's
/// content is ever logged. The SimBrief ID is the pilot's own identifier: callers never log it
/// either. Pure, with the folder candidates passed in.
/// </summary>
public static class Bbs757WorkFolder
{
    public const string PackageName = "bluebirdsim-aircraft-757-200";
    public const string SettingsFile = "efb_settings.json";

    // Larger than any settings or cycle file BlueBird writes (about 1 KB and 300 bytes): a file
    // past this is not one of them and is not read.
    private const long MaxFileBytes = 256 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 16,
    };

    /// <summary>
    /// Where the work folder can be, most likely first: MSFS 2024 (Microsoft Store, then the
    /// MSFS2024 WASM folder, then Steam), then MSFS 2020 (Microsoft Store, then Steam).
    /// </summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalState", "WASM", "MSFS2020", PackageName, "work"),
            Path.Combine(local, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalState", "WASM", "MSFS2024", PackageName, "work"),
            Path.Combine(roaming, "Microsoft Flight Simulator 2024", "WASM", "MSFS2020", PackageName, "work"),
            Path.Combine(local, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState", "packages", PackageName, "work"),
            Path.Combine(roaming, "Microsoft Flight Simulator", "Packages", PackageName, "work"),
        };
    }

    /// <summary>
    /// The work folder BlueBird uses now: of <paramref name="roots"/>, the one whose
    /// <see cref="SettingsFile"/> was written last (a pilot with both simulators, or an old install
    /// left behind, has more than one; BlueBird rewrites its settings whenever one changes, so the
    /// newest is the one in use). With no settings file in any of them, the first that exists as a
    /// folder; null when none does. Only the file's time is looked at, never its content.
    /// </summary>
    public static string? Find(IReadOnlyList<string> roots)
    {
        string? firstExisting = null, newest = null;
        DateTime newestWrite = DateTime.MinValue;
        foreach (string root in roots)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                firstExisting ??= root;
                var settings = new FileInfo(Path.Combine(root, SettingsFile));
                if (settings.Exists && (newest == null || settings.LastWriteTimeUtc > newestWrite))
                {
                    newest = root;
                    newestWrite = settings.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException or System.Security.SecurityException) { }
        }
        return newest ?? firstExisting;
    }

    /// <summary>The work folder at one of the <see cref="DefaultRoots"/>, or null.</summary>
    public static string? Find() => Find(DefaultRoots());

    private static string CyclePath(string folder) => Path.Combine(folder, "NavigationData", "cycle.json");

    /// <summary>
    /// Whether BlueBird's cycle file exists (it is written by a nav data update). With none, no
    /// downloaded cycle is installed; with one that <see cref="ReadInstalledCycle"/> cannot read,
    /// the installed cycle is unknown.
    /// </summary>
    public static bool HasCycleFile(string folder)
    {
        try { return File.Exists(CyclePath(folder)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    /// <summary>
    /// The SimBrief ID BlueBird stored (<c>simbrief_id</c>), or null when it is not set (BlueBird
    /// writes -1), missing, not a whole number, or the file cannot be read.
    /// </summary>
    public static long? ReadSimBriefId(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("simbrief_id", out var id)) return null;
        if (id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out long value)) return null;
        return value > 0 ? value : null;
    }

    /// <summary>
    /// The installed AIRAC cycle from <c>NavigationData\cycle.json</c> (its <c>cycle</c> field,
    /// "2610"), or null when there is no such file, or it has another shape.
    /// </summary>
    public static int? ReadInstalledCycle(string folder)
    {
        using var doc = ReadJson(CyclePath(folder));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("cycle", out var cycle)) return null;

        int value;
        if (cycle.ValueKind == JsonValueKind.String)
        {
            string s = cycle.GetString() ?? "";
            if (s.Length != 4 || !s.All(char.IsAsciiDigit)) return null;
            value = (s[0] - '0') * 1000 + (s[1] - '0') * 100 + (s[2] - '0') * 10 + (s[3] - '0');
        }
        else if (cycle.ValueKind != JsonValueKind.Number || !cycle.TryGetInt32(out value))
        {
            return null;
        }
        return AiracCycle.IsCycleNumber(value) ? value : null;
    }

    /// <summary>The file parsed, or null when it is missing, too large, unreadable or not JSON.</summary>
    private static JsonDocument? ReadJson(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileBytes) return null;
            // Shared read: BlueBird may be writing the file at this moment.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonDocument.Parse(stream, ReadOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or ArgumentException or NotSupportedException)
        {
            // The content is never logged: the settings file holds credentials.
            return null;
        }
    }
}
