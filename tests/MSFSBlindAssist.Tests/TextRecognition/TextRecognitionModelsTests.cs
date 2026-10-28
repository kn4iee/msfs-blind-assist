using System.Text;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// The pinned list of text recognition model files and the check of a folder against it: what the
/// Settings tab and the offer say before anything is downloaded. Small fake lists through the
/// overloads stand in for the 32 megabytes of real files.
/// </summary>
public sealed class TextRecognitionModelsTests : IDisposable
{
    private const int Version = 1;

    // Four files of different sizes, so a count and a byte total each name exactly the files they are about.
    private static readonly IReadOnlyList<TextRecognitionFile> Fake = new[]
    {
        new TextRecognitionFile("det.onnx", 100, new string('a', 64)),
        new TextRecognitionFile("rec.onnx", 200, new string('b', 64)),
        new TextRecognitionFile("dict.txt", 30, new string('c', 64)),
        new TextRecognitionFile("cls.onnx", 4, new string('d', 64)),
    };

    private const long FakeTotal = 334;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-textrec-" + Guid.NewGuid().ToString("N"));

    public TextRecognitionModelsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MarkerPath => Path.Combine(_dir, TextRecognitionModels.MarkerName);

    private TextRecognitionInstall Check() => TextRecognitionModels.Check(_dir, Fake, Version);

    private static TextRecognitionInstall Install(TextRecognitionState state, int kept, long toFetch) => new(state, kept, toFetch);

    private void Put(string name, long bytes) => File.WriteAllBytes(Path.Combine(_dir, name), new byte[bytes]);

    private void PutAll()
    {
        foreach (TextRecognitionFile file in Fake) Put(file.Name, file.Bytes);
    }

    [Fact]
    public void Files_are_the_four_pinned_models()
    {
        Assert.Equal(new[]
        {
            new TextRecognitionFile("PP-OCRv6_det_small.onnx", 9_929_594, "090f04abcd9d9a7498bc4ebf677e4cb9bdce1fe4197ddb7e529f1ef44e1ff94f"),
            new TextRecognitionFile("PP-OCRv6_rec_small.onnx", 21_234_383, "6f327246b50388f3c176ae304bd95767ea6dc0c9ae92153ef8cbe210b3c14884"),
            new TextRecognitionFile("ppocrv6_dict.txt", 74_947, "b5f2bfe2bdd9448429e3e82b51c789775d9b42f2403d082b00662eb77e401c5d"),
            new TextRecognitionFile("ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx", 1_018_508, "54379ae5174d026780215fc748a7f31910dee36818e63d49e17dc598ecc82df7"),
        }, TextRecognitionModels.Files);
        Assert.Equal(32_257_432, TextRecognitionModels.TotalBytes);
        Assert.Equal(32, TextRecognitionModels.Megabytes(TextRecognitionModels.TotalBytes));
    }

    [Fact]
    public void The_pinned_values_are_the_specified_ones()
    {
        // These meet things outside the code: the release the files are published on, the marker the
        // job and the first run write, the free space the strings promise ("100 megabytes needed").
        Assert.Equal(1, TextRecognitionModels.ListVersion);
        Assert.Equal("text-recognition-1", TextRecognitionModels.ReleaseTag);
        Assert.Equal("https://github.com/oasis1701/msfs-blind-assist/releases/download/text-recognition-1/", TextRecognitionModels.BaseUrl);
        Assert.Equal("installed.json", TextRecognitionModels.MarkerName);
        Assert.Equal(100_000_000, TextRecognitionModels.RequiredFreeBytes);
        Assert.Equal(100, TextRecognitionModels.Megabytes(TextRecognitionModels.RequiredFreeBytes));
    }

    [Fact]
    public void The_default_folder_is_under_app_data()
        => Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "text-recognition"),
            TextRecognitionModels.DefaultFolder());

    [Fact]
    public void The_release_tag_is_no_version()
    {
        // The updater reads every release's tag as a version and skips only what it cannot parse: a tag that
        // parsed would be offered to pilots as an app update.
        Assert.Null(SemanticVersion.TryParse(TextRecognitionModels.ReleaseTag));
        Assert.EndsWith("/download/text-recognition-1/", TextRecognitionModels.BaseUrl, StringComparison.Ordinal);

        var release = new ReleaseCandidate(TextRecognitionModels.ReleaseTag, "Text recognition models 1 (PaddleOCR PP-OCRv6 small)",
            Body: null, IsPrerelease: false, IsDraft: false, ZipDownloadUrl: null);
        foreach (UpdateChannel channel in Enum.GetValues<UpdateChannel>())
            Assert.Equal(UpdateVerdict.NoCandidate,
                UpdateCandidateSelector.Select(new[] { release }, channel, SemanticVersion.TryParse("9.0.0")).Verdict);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(499_999, 0)]
    [InlineData(500_000, 1)]      // a half rounds up, as a pilot counts it, not to the even number
    [InlineData(1_500_000, 2)]
    [InlineData(2_500_000, 3)]
    [InlineData(21_309_330, 21)]
    [InlineData(32_257_432, 32)]  // decimal: the 30.8 binary megabytes would read "31"
    public void Megabytes_are_decimal_and_rounded(long bytes, int expected)
        => Assert.Equal(expected, TextRecognitionModels.Megabytes(bytes));

    [Fact]
    public void Empty_folder_is_missing()
        => Assert.Equal(Install(TextRecognitionState.Missing, 0, FakeTotal), Check());

    [Fact]
    public void A_missing_folder_is_missing_and_is_not_created()
    {
        string folder = Path.Combine(_dir, "not-there");

        Assert.Equal(Install(TextRecognitionState.Missing, 0, FakeTotal), TextRecognitionModels.Check(folder, Fake, Version));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Kept_files_without_a_marker_are_incomplete()
    {
        Put("det.onnx", 100);
        Put("dict.txt", 30);

        // The other two files, 200 + 4 bytes, are what a download still has to fetch.
        Assert.Equal(Install(TextRecognitionState.Incomplete, 2, 204), Check());

        // A whole set is no better without its marker, which is written last: a job that died
        // before writing it has not finished.
        Put("rec.onnx", 200);
        Put("cls.onnx", 4);
        Assert.Equal(Install(TextRecognitionState.Incomplete, 4, 0), Check());
    }

    [Fact]
    public void A_part_file_never_counts()
    {
        Put("det.onnx.part", 100);   // a download in flight, or killed, at the file's full size

        Assert.Equal(Install(TextRecognitionState.Missing, 0, FakeTotal), Check());

        foreach (TextRecognitionFile file in Fake.Skip(1)) Put(file.Name, file.Bytes);
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);

        Assert.Equal(Install(TextRecognitionState.Incomplete, 3, 100), Check());
    }

    [Fact]
    public void Marker_with_every_file_is_ready()
    {
        PutAll();
        Put("leftover.bin", 7);   // a file the list does not name is not looked at
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);

        Assert.Equal(Install(TextRecognitionState.Ready, 4, 0), Check());
    }

    [Fact]
    public void Ready_marker_with_a_file_missing_is_incomplete()
    {
        // An antivirus quarantine can remove a model after the install: the offer must come back.
        PutAll();
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);
        File.Delete(Path.Combine(_dir, "rec.onnx"));

        Assert.Equal(Install(TextRecognitionState.Incomplete, 3, 200), Check());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    [InlineData(31)]
    public void Ready_marker_with_a_wrong_size_file_is_incomplete(int size)
    {
        PutAll();
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);
        Put("dict.txt", size);

        Assert.Equal(Install(TextRecognitionState.Incomplete, 3, 30), Check());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Marker_of_another_version_is_outdated(int markerVersion)
    {
        PutAll();
        TextRecognitionModels.WriteMarker(_dir, Fake, markerVersion);

        Assert.Equal(Install(TextRecognitionState.Outdated, 4, 0), Check());
    }

    [Fact]
    public void An_outdated_install_still_counts_the_files_it_keeps()
    {
        Put("det.onnx", 100);
        Put("rec.onnx", 200);
        TextRecognitionModels.WriteMarker(_dir, Fake, 2);

        // The job re-hashes whatever is on disk anyway; the bytes are what is missing at the right size.
        Assert.Equal(Install(TextRecognitionState.Outdated, 2, 34), Check());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":1")]       // cut off mid-write
    [InlineData("{\"files\":[]}")]       // no version
    [InlineData("{\"version\":\"1\"}")]  // a version that is not a number
    [InlineData("{\"version\":1.5}")]    // ... or not an integer
    [InlineData("[1]")]                  // not an object
    public void Unreadable_marker_is_outdated(string content)
    {
        PutAll();
        File.WriteAllText(MarkerPath, content);

        Assert.Equal(Install(TextRecognitionState.Outdated, 4, 0), Check());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Marker_json_is_exact(string culture)
    {
        var files = new[]
        {
            new TextRecognitionFile("det.onnx", 100, "0a1b"),
            new TextRecognitionFile("dict.txt", 30, "2c3d"),
        };

        Assert.Equal(
            "{\"version\":1,\"files\":[{\"name\":\"det.onnx\",\"sha256\":\"0a1b\"},{\"name\":\"dict.txt\",\"sha256\":\"2c3d\"}]}",
            TestCulture.Run(culture, () => TextRecognitionModels.MarkerJson(files, 1)));
        Assert.StartsWith("{\"version\":12,", TestCulture.Run(culture, () => TextRecognitionModels.MarkerJson(files, 12)), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteMarker_writes_the_json_without_a_byte_order_mark_or_newline()
    {
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);

        byte[] written = File.ReadAllBytes(MarkerPath);
        Assert.Equal(Encoding.UTF8.GetBytes(TextRecognitionModels.MarkerJson(Fake, Version)), written);
        Assert.Equal((byte)'{', written[0]);
        Assert.Equal((byte)'}', written[^1]);

        // The overload without a list writes the real one at this build's list version.
        TextRecognitionModels.WriteMarker(_dir);
        Assert.Equal(
            Encoding.UTF8.GetBytes(TextRecognitionModels.MarkerJson(TextRecognitionModels.Files, TextRecognitionModels.ListVersion)),
            File.ReadAllBytes(MarkerPath));
    }

    [Fact]
    public void RemoveMarker_leaves_the_files()
    {
        PutAll();
        TextRecognitionModels.WriteMarker(_dir, Fake, Version);

        TextRecognitionModels.RemoveMarker(_dir);

        Assert.False(File.Exists(MarkerPath));
        Assert.All(Fake, file => Assert.True(File.Exists(Path.Combine(_dir, file.Name))));
        Assert.Equal(Install(TextRecognitionState.Incomplete, 4, 0), Check());
    }

    [Fact]
    public void RemoveMarker_without_a_marker_does_nothing()
    {
        Assert.Null(Record.Exception(() => TextRecognitionModels.RemoveMarker(_dir)));
        Assert.Null(Record.Exception(() => TextRecognitionModels.RemoveMarker(Path.Combine(_dir, "not-there"))));
    }
}
