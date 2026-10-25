using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>BlueBird's unit system (its <c>units</c> setting), named by the weight it uses.</summary>
public enum Bbs757WeightUnits
{
    /// <summary>METRIC, stored as 0.</summary>
    Kilograms,
    /// <summary>IMPERIAL, stored as 1.</summary>
    Pounds,
}

/// <summary>
/// BlueBird's 757 "work" folder, where the aircraft keeps its EFB settings and the nav data it
/// downloaded, and the only four values the app reads from it: the SimBrief ID (to confirm an
/// entry made on the EFB took), the unit system (to say what the weight units answer did), the
/// nosewheel steering setting (so the steering button never clicks its toggle when it is already
/// rudder) and the installed AIRAC cycle (so the nav data update never clicks where BlueBird shows
/// REMOVE). Read only: the app never writes BlueBird's files.
///
/// <c>efb_settings.json</c> also holds the pilot's Hoppie logon code and SayIntentions ACARS key:
/// it is parsed for <c>simbrief_id</c>, <c>units</c> and <c>tiller_realistic</c> alone, nothing else is taken from it, and
/// neither file's content is ever logged. The SimBrief ID is the pilot's own identifier: callers
/// never log it either. Pure, with the folder candidates passed in.
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
    /// BlueBird's unit system (<c>units</c>, the EFB's Settings, Aircraft, UNITS: IMPERIAL or METRIC
    /// for the systems, the EFB and the FMS; the "switch the aircraft unit system?" question after a
    /// SimBrief fetch sets it), or null when it is missing, another value, or the file cannot be
    /// read. 0 is METRIC (kilograms) and 1 IMPERIAL (pounds): read 1 live on 2026-10-25 after the
    /// question was answered YES ("Unit system set to Imperial"), and the module's own display table
    /// lists METRIC, IMPERIAL, INVALID in that order. Nothing else in the file is read.
    /// </summary>
    public static Bbs757WeightUnits? ReadUnits(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("units", out var units)) return null;
        if (units.ValueKind != JsonValueKind.Number || !units.TryGetInt32(out int value)) return null;
        return value switch
        {
            0 => Bbs757WeightUnits.Kilograms,
            1 => Bbs757WeightUnits.Pounds,
            _ => null,
        };
    }

    /// <summary>
    /// BlueBird's NOSEWHEEL STEERING setting (<c>tiller_realistic</c>, the EFB's Settings, AIRCRAFT):
    /// true REALISTIC (the tiller), false RUDDER. Measured 2026-10-25: it read false right after the
    /// switch was clicked from REALISTIC to RUDDER (a global setting, written at once). Null when it
    /// is missing, not a JSON bool, or the file cannot be read. Nothing else in the file is read.
    /// </summary>
    public static bool? ReadTillerRealistic(string folder)
    {
        using var doc = ReadJson(Path.Combine(folder, SettingsFile));
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Object) return null;
        if (!doc.RootElement.TryGetProperty("tiller_realistic", out var tiller)) return null;
        return tiller.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>
    /// Whether a work folder other than <paramref name="chosen"/> among <paramref name="roots"/>
    /// holds nav data at or above <paramref name="currentCycle"/>, or a cycle file whose cycle cannot
    /// be read. Then the folders disagree about what is installed: BlueBird may be using the other
    /// one, where its AIRAC page shows REMOVE at the UPDATE spot, so the update must not click.
    /// Folders that do not exist, or hold no cycle file, agree with anything. Paths are compared
    /// whole, case-insensitive, after normalizing.
    /// </summary>
    public static bool OthersDisagree(IReadOnlyList<string> roots, string chosen, int currentCycle)
    {
        string chosenKey = PathKey(chosen);
        foreach (string root in roots)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                if (string.Equals(PathKey(root), chosenKey, StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasCycleFile(root)) continue;
                int? cycle = ReadInstalledCycle(root);
                if (cycle == null || cycle >= currentCycle) return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or NotSupportedException or System.Security.SecurityException) { }
        }
        return false;
    }

    private static string PathKey(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
                                       or System.Security.SecurityException) { return path; }
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
