using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Changing one value in BlueBird's settings files (<see cref="Bbs757SettingsFile"/>): only that value's bytes change,
/// every other value is checked unchanged, the file is replaced through a temp file only while the guard allows and
/// never created, a safety copy is taken once, and a registration setting goes to every registration's file. Also the
/// work folder's listings (<see cref="Bbs757WorkFolder.RegistrationFiles"/>, <see cref="Bbs757WorkFolder.ExistingFolders"/>,
/// <see cref="Bbs757WorkFolder.SimulatorOf"/>). Temp folders only: never BlueBird's own files.
/// </summary>
public class Bbs757SettingsFileTests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    // efb_settings.json with made-up IDs and codes, and G-BBS75-opt.json as BlueBird wrote it (one line, its key order).
    private static readonly string SettingsFixture = Fixture("bbs757-efb-settings.json");
    private static readonly string LiveryFixture = Fixture("bbs757-livery-opt.json");

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "msfsba-bbs757-settings-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public string Sub(string name) => System.IO.Path.Combine(Path, name);
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// The settings fixture as <c>efb_settings.json</c> in <paramref name="tmp"/>: byte for byte without overrides; with
    /// them, each override's top-level key set to its JSON (added last when the fixture has no such key), written compact.
    /// </summary>
    private static string WriteFixtureSettings(TempDir tmp, params (string Key, string Json)[] overrides)
    {
        string file = tmp.Sub("efb_settings.json");
        if (overrides.Length == 0)
        {
            File.Copy(SettingsFixture, file);
            return file;
        }

        var root = JsonNode.Parse(File.ReadAllText(SettingsFixture))!.AsObject();
        foreach (var (key, json) in overrides) root[key] = JsonNode.Parse(json);
        File.WriteAllText(file, root.ToJsonString());
        return file;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static Bbs757Value ValueOf(object value) => value switch
    {
        long whole => Bbs757Value.Of(whole),
        bool on => Bbs757Value.Of(on),
        _ => throw new ArgumentException($"No test value of type {value.GetType()}.", nameof(value)),
    };

    private static int Occurrences(string text, string part)
    {
        int count = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + 1, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// <paramref name="json"/> with the value at <paramref name="path"/> written as <paramref name="newToken"/> and every
    /// other character kept: what an edit of that one value must give. The value's <c>"key":old</c> must occur once, so
    /// nothing else can be the part replaced.
    /// </summary>
    private static string WithValue(string json, string path, string newToken)
    {
        string[] keys = path.Split('.');
        using var doc = JsonDocument.Parse(json);
        JsonElement old = doc.RootElement;
        foreach (string key in keys) old = old.GetProperty(key);

        string before = $"\"{keys[^1]}\":{old.GetRawText()}";
        Assert.Equal(1, Occurrences(json, before));
        return json.Replace(before, $"\"{keys[^1]}\":{newToken}");
    }

    private static Bbs757SaveResult Refused(string reason) => new(Bbs757SaveStatus.Refused, reason);

    // ---- changing the bytes -----------------------------------------------------------------------

    [Theory]   // each path of a different type and depth
    [InlineData("loading_speed", 3L)]
    [InlineData("tiller_realistic", true)]
    [InlineData("failures.random_enabled", true)]
    public void Edit_changes_only_that_value_s_bytes(string path, object value)
    {
        byte[] original = File.ReadAllBytes(SettingsFixture);
        Bbs757Value wanted = ValueOf(value);

        var edit = Bbs757SettingsFile.Edit(original, path, wanted);

        Assert.Null(edit.Refusal);
        // Every byte before the value's token and after it is the fixture's own, its final line break included.
        Assert.Equal(Utf8(WithValue(Encoding.UTF8.GetString(original), path, wanted.ToJson())), edit.Bytes);
        using var doc = JsonDocument.Parse(edit.Bytes!);
        Assert.Equal(wanted, Bbs757SettingsFile.ValueAt(doc.RootElement, path, wanted.Type));
    }

    [Fact]
    public void Edit_changes_a_nested_registration_value_and_not_a_same_named_key_elsewhere()
    {
        byte[] livery = File.ReadAllBytes(LiveryFixture);

        var edit = Bbs757SettingsFile.Edit(livery, "options.egpws_callouts.80", Bbs757Value.Of(false));

        Assert.Null(edit.Refusal);
        Assert.Equal(Utf8(WithValue(Encoding.UTF8.GetString(livery), "options.egpws_callouts.80", "false")), edit.Bytes);
        using (var doc = JsonDocument.Parse(edit.Bytes!))
        {
            Assert.Equal(Bbs757Value.Of(false), Bbs757SettingsFile.ValueAt(doc.RootElement, "options.egpws_callouts.80", Bbs757ValueType.Bool));
            Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ValueAt(doc.RootElement, "options.pilot_callouts.80_knots", Bbs757ValueType.Bool));
        }

        // The same key earlier in the file, at the top, in a same-named object elsewhere and beside a longer key: only the
        // one at the path changes.
        byte[] decoys = Utf8("{\"80\":true,\"egpws_callouts\":{\"80\":true},\"options\":{\"pilot_callouts\":{\"80_knots\":true,\"80\":true},\"egpws_callouts\":{\"80\":true}}}");
        Assert.Equal(
            Utf8("{\"80\":true,\"egpws_callouts\":{\"80\":true},\"options\":{\"pilot_callouts\":{\"80_knots\":true,\"80\":true},\"egpws_callouts\":{\"80\":false}}}"),
            Bbs757SettingsFile.Edit(decoys, "options.egpws_callouts.80", Bbs757Value.Of(false)).Bytes);
    }

    [Fact]
    public void Edit_refuses_a_missing_key_a_wrong_type_an_object_and_invalid_json()
    {
        byte[] settings = File.ReadAllBytes(SettingsFixture);

        static void Refuses(byte[] file, string path, Bbs757Value value, string reason)
        {
            var edit = Bbs757SettingsFile.Edit(file, path, value);
            Assert.Equal(reason, edit.Refusal);
            Assert.Null(edit.Bytes);
        }

        // No such key: at the top, inside an object, under a key that holds a value, or only at another depth.
        Refuses(settings, "no_such_key", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "failures.no_such_key", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "loading_speed.speed", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "random_enabled", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        // The key holds another type than the value's.
        Refuses(settings, "loading_speed", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "tiller_realistic", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "hoppie_logon_code", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "display_brightness", Bbs757Value.Of(1L), Bbs757SettingsFile.NotInFileReason);   // 0.8: no whole number
        Refuses(Utf8("{\"loading_speed\":null}"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.NotInFileReason);
        // An object or a list where a value was expected.
        Refuses(settings, "failures", Bbs757Value.Of(true), Bbs757SettingsFile.NotInFileReason);
        Refuses(settings, "chart_view_states", Bbs757Value.Of("x"), Bbs757SettingsFile.NotInFileReason);
        Refuses(Utf8("{\"loading_speed\":[2]}"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.NotInFileReason);
        // Not JSON: text, cut short after the value, nothing at all.
        Refuses(Utf8("not json"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
        Refuses(Utf8("{\"loading_speed\":2"), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
        Refuses(Array.Empty<byte>(), "loading_speed", Bbs757Value.Of(3L), Bbs757SettingsFile.UnreadableReason);
    }

    [Fact]
    public void Edit_refuses_a_key_written_twice_on_its_path()
    {
        // Which of the two BlueBird reads is unknown, so neither is changed.
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            Bbs757SettingsFile.Edit(Utf8("{\"loading_speed\":2,\"loading_speed\":1}"), "loading_speed", Bbs757Value.Of(3L)).Refusal);
        Assert.Equal(Bbs757SettingsFile.UnreadableReason,
            Bbs757SettingsFile.Edit(Utf8("{\"failures\":{\"random_enabled\":false},\"failures\":{}}"), "failures.random_enabled",
                Bbs757Value.Of(true)).Refusal);

        // A key written twice off the path is not read, so it is no reason to refuse: it is kept as it is.
        Assert.Equal(Utf8("{\"x\":1,\"x\":2,\"loading_speed\":3}"),
            Bbs757SettingsFile.Edit(Utf8("{\"x\":1,\"x\":2,\"loading_speed\":2}"), "loading_speed", Bbs757Value.Of(3L)).Bytes);
    }

    [Fact]
    public void Edit_round_trips_codes_with_quotes_backslashes_and_non_ascii()
    {
        const string code = "AB\"C\\D é";
        byte[] original = File.ReadAllBytes(SettingsFixture);

        var edit = Bbs757SettingsFile.Edit(original, "hoppie_logon_code", Bbs757Value.Of(code));

        Assert.Null(edit.Refusal);
        // Escaped as little as JSON allows: the accent stays readable in the file.
        Assert.Contains("\"hoppie_logon_code\":\"AB\\\"C\\\\D é\",", Encoding.UTF8.GetString(edit.Bytes!));
        using (var doc = JsonDocument.Parse(edit.Bytes!))
            Assert.Equal(Bbs757Value.Of(code), Bbs757SettingsFile.ValueAt(doc.RootElement, "hoppie_logon_code", Bbs757ValueType.Text));

        // And back: the escaped token is replaced whole, however much longer its escapes make it.
        Assert.Equal(original, Bbs757SettingsFile.Edit(edit.Bytes!, "hoppie_logon_code", Bbs757Value.Of("NOT-A-REAL-CODE")).Bytes);
    }

    [Fact]
    public void A_byte_order_mark_before_the_json_is_kept()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. Utf8("{\"loading_speed\":2}")];
        byte[] expected = [0xEF, 0xBB, 0xBF, .. Utf8("{\"loading_speed\":3}")];

        Assert.Equal(expected, Bbs757SettingsFile.Edit(original, "loading_speed", Bbs757Value.Of(3L)).Bytes);
    }

    [Fact]
    public void The_check_after_the_splice_catches_any_other_change()
    {
        byte[] original = Utf8("{\"a\":1,\"b\":{\"c\":true,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}");
        string? Check(string edited) => Bbs757SettingsFile.Verify(original, Utf8(edited), "b.c", Bbs757Value.Of(false));

        Assert.Null(Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));                      // only the value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":true,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));    // not the value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":\"false\",\"d\":\"x\"},\"e\":[1,{\"f\":2}]}")); // another type
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":2,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));   // another value
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"y\"},\"e\":[1,{\"f\":2}]}"));   // its neighbor
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":3}]}"));   // in a list
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1.0,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}")); // written otherwise
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}],\"g\":0}")); // a key more
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]}"));             // a key less
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"b\":{\"c\":false,\"d\":\"x\"},\"a\":1,\"e\":[1,{\"f\":2}]}"));      // reordered
        Assert.Equal(Bbs757SettingsFile.VerifyReason, Check("{\"a\":1,\"b\":{\"c\":false,\"d\":\"x\"},\"e\":[1,{\"f\":2}]"));      // not JSON
    }

    // ---- reading ----------------------------------------------------------------------------------

    [Fact]
    public void ValueAt_follows_the_dotted_path_and_takes_only_the_type_asked()
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(LiveryFixture));
        JsonElement root = doc.RootElement;

        Assert.Equal(Bbs757Value.Of("lbs"), Bbs757SettingsFile.ValueAt(root, "options.general.weight_unit", Bbs757ValueType.Text));
        Assert.Equal(Bbs757Value.Of(true), Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.80", Bbs757ValueType.Bool));
        Assert.Equal(Bbs757Value.Of(1L), Bbs757SettingsFile.ValueAt(root, "options.model_visibility.cabin_layout", Bbs757ValueType.Integer));
        // A stored whole number is a valid float.
        Assert.Equal(Bbs757Value.OfFloat(1.0), Bbs757SettingsFile.ValueAt(root, "options.model_visibility.cabin_layout", Bbs757ValueType.Float));

        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.80", Bbs757ValueType.Integer));      // another type
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.egpws_callouts.81", Bbs757ValueType.Bool));         // no such key
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "80", Bbs757ValueType.Bool));                                 // only at its path
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.general", Bbs757ValueType.Text));                   // an object
        Assert.Null(Bbs757SettingsFile.ValueAt(root, "options.general.weight_unit.x", Bbs757ValueType.Text));     // through a value
        Assert.Null(Bbs757SettingsFile.ValueAt(default, "options", Bbs757ValueType.Text));                        // nothing read
    }

    [Fact]
    public void Read_parses_the_file_and_is_null_when_it_is_missing_too_large_or_not_json()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);

        using (var doc = Bbs757SettingsFile.Read(file))
        {
            Assert.NotNull(doc);
            Assert.Equal(Bbs757Value.Of(2L), Bbs757SettingsFile.ValueAt(doc!.RootElement, "loading_speed", Bbs757ValueType.Integer));
        }

        // Read while another program has the file open for writing, as BlueBird may.
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        using (var doc = Bbs757SettingsFile.Read(file))
            Assert.NotNull(doc);

        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("missing.json")));
        File.WriteAllText(tmp.Sub("not.json"), "not json");
        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("not.json")));
        File.WriteAllText(tmp.Sub("large.json"), "{\"a\":\"" + new string('x', 256 * 1024) + "\"}");
        Assert.Null(Bbs757SettingsFile.Read(tmp.Sub("large.json")));
    }

    // ---- saving -----------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_value_elsewhere_survives_a_save()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp, ("irs_align_time", "77"));      // fixture with irs_align_time set to 77
        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Contains("\"irs_align_time\":77", File.ReadAllText(file));
    }

    [Fact]
    public void Unknown_keys_are_kept()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp, ("future_key", "{\"a\":1}"));
        string before = File.ReadAllText(file);
        Assert.EndsWith(",\"future_key\":{\"a\":1}}", before);   // a key this BlueBird version does not write

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Equal(WithValue(before, "loading_speed", "3"), File.ReadAllText(file));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_float_is_saved_with_a_decimal_point(string culture)
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);

        var r = TestCulture.Run(culture, () => Bbs757SettingsFile.Save(file, "failures.random_multiplier", Bbs757Value.OfFloat(2.0),
            () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true));

        Assert.Equal(Bbs757SaveStatus.Saved, r.Status);
        Assert.Contains("\"random_multiplier\":2.0", File.ReadAllText(file));
    }

    [Fact]
    public void A_save_of_the_value_already_stored_writes_nothing()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        var written = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, written);
        byte[] before = File.ReadAllBytes(file);

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(2L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Unchanged), r);
        Assert.Equal(written, File.GetLastWriteTimeUtc(file));
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("bk")));   // nothing written, so nothing to keep a copy of
    }

    [Fact]
    public void A_save_refuses_while_the_simulator_runs()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] before = File.ReadAllBytes(file);

        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => false, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        Assert.Equal(Refused(Bbs757SettingsFile.SimRunningReason), r);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("bk")));
    }

    [Fact]
    public void A_save_refuses_when_the_simulator_starts_before_the_write()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] before = File.ReadAllBytes(file);
        int calls = 0;
        var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => ++calls == 1, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
        Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Refused, Bbs757SettingsFile.SimRunningReason), r);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
    }

    [Fact]
    public void A_save_refuses_a_file_in_use()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var r = Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);
            Assert.Equal(new Bbs757SaveResult(Bbs757SaveStatus.Refused, Bbs757SettingsFile.InUseReason), r);
        }
    }

    [Fact]
    public void A_missing_file_is_refused_and_never_created()
    {
        using var tmp = new TempDir();
        string file = tmp.Sub("efb_settings.json");
        string inMissingFolder = Path.Combine(tmp.Sub("work"), "efb_settings.json");
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        Assert.Equal(Refused(Bbs757SettingsFile.MissingReason),
            Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true));
        Assert.Equal(Refused(Bbs757SettingsFile.MissingReason),
            Bbs757SettingsFile.Save(inMissingFolder, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true));

        Assert.False(File.Exists(file));
        Assert.False(File.Exists(file + ".msfsba-tmp"));
        Assert.False(Directory.Exists(tmp.Sub("work")));
        Assert.False(Directory.Exists(tmp.Sub("bk")));
    }

    [Fact]
    public void The_first_write_copies_the_file_to_the_backup_folder_once()
    {
        using var tmp = new TempDir();
        string file = WriteFixtureSettings(tmp);
        byte[] original = File.ReadAllBytes(file);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(file, "loading_speed", Bbs757Value.Of(3L), () => true, backup, true).Status);
        Assert.Equal(Bbs757SaveStatus.Saved,
            Bbs757SettingsFile.Save(file, "irs_align_time", Bbs757Value.Of(2L), () => true, backup, true).Status);

        // One copy, under the file's own name, of the file as it was before the first save.
        string copy = Assert.Single(Directory.GetFiles(tmp.Sub("bk")));
        Assert.Equal("efb_settings.json", Path.GetFileName(copy));
        Assert.Equal(original, File.ReadAllBytes(copy));
        Assert.NotEqual(original, File.ReadAllBytes(file));
    }

    // ---- every registration -------------------------------------------------------------------------

    [Fact]
    public void Registration_saves_report_each_registration()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        string liveries = Path.Combine(work, "liveries");
        Directory.CreateDirectory(liveries);
        File.Copy(LiveryFixture, Path.Combine(liveries, "N757BB-opt.json"));
        File.WriteAllText(Path.Combine(liveries, "G-BBS75-opt.json"), "not json");
        File.Copy(LiveryFixture, Path.Combine(liveries, "D-ABCD-opt.json"));
        File.WriteAllText(Path.Combine(liveries, "G-BBS75.json"), "{}");   // not a registration's options file
        File.WriteAllText(Path.Combine(work, "X-OUT-opt.json"), "{}");     // not in liveries

        var results = Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false),
            () => true, new Bbs757SettingsBackup(tmp.Sub("bk")), true);

        // By registration, the one that could not be read named with the others.
        Assert.Equal(new (string, Bbs757SaveResult)[]
        {
            ("D-ABCD", new Bbs757SaveResult(Bbs757SaveStatus.Saved)),
            ("G-BBS75", Refused(Bbs757SettingsFile.UnreadableReason)),
            ("N757BB", new Bbs757SaveResult(Bbs757SaveStatus.Saved)),
        }, results);
        string saved = WithValue(File.ReadAllText(LiveryFixture), "options.egpws_callouts.80", "false");
        Assert.Equal(saved, File.ReadAllText(Path.Combine(liveries, "D-ABCD-opt.json")));
        Assert.Equal(saved, File.ReadAllText(Path.Combine(liveries, "N757BB-opt.json")));
        Assert.Equal("not json", File.ReadAllText(Path.Combine(liveries, "G-BBS75-opt.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(liveries, "G-BBS75.json")));
    }

    [Fact]
    public void No_registration_files_give_no_results()
    {
        using var tmp = new TempDir();
        string work = tmp.Sub("work");
        Directory.CreateDirectory(work);
        var backup = new Bbs757SettingsBackup(tmp.Sub("bk"));

        // No liveries folder yet, then an empty one.
        Assert.Empty(Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false), () => true, backup, true));
        Directory.CreateDirectory(Path.Combine(work, "liveries"));
        Assert.Empty(Bbs757SettingsFile.SaveToRegistrations(work, "options.egpws_callouts.80", Bbs757Value.Of(false), () => true, backup, true));
        Assert.Empty(Bbs757WorkFolder.RegistrationFiles(tmp.Sub("no-such-folder")));
    }

    // ---- the work folders ---------------------------------------------------------------------------

    [Fact]
    public void ExistingFolders_are_the_roots_holding_blueBirds_settings_in_their_order()
    {
        using var tmp = new TempDir();
        string first = tmp.Sub("first"), bare = tmp.Sub("bare"), second = tmp.Sub("second"), missing = tmp.Sub("missing");
        foreach (string folder in new[] { first, second })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "efb_settings.json"), "{}");
        }
        Directory.CreateDirectory(bare);   // a folder without BlueBird's settings file

        Assert.Equal(new[] { second, first }, Bbs757WorkFolder.ExistingFolders(new[] { missing, second, bare, first, "" }));
        Assert.Empty(Bbs757WorkFolder.ExistingFolders(new[] { missing, bare }));
        Assert.Empty(Bbs757WorkFolder.ExistingFolders(Array.Empty<string>()));
    }

    [Fact]
    public void SimulatorOf_names_the_store_and_steam_roots()
    {
        var roots = Bbs757WorkFolder.DefaultRoots();

        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[0]));   // Microsoft Store
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[1]));
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[2]));   // Steam
        Assert.Equal("MSFS 2020", Bbs757WorkFolder.SimulatorOf(roots[3]));   // Microsoft Store
        Assert.Equal("MSFS 2020", Bbs757WorkFolder.SimulatorOf(roots[4]));   // Steam
        Assert.Equal("MSFS 2024", Bbs757WorkFolder.SimulatorOf(roots[0].ToUpperInvariant()));
    }
}
