using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using MSFSBlindAssist.Services.Geography;
using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// The whole text recognition download job against a fake server that serves four made-up files: a fresh
/// download, a retry, a damaged file, the pilot's cancel and the next job's resume, a full disk, a missing
/// release, files a killed job left behind, and what the job says along the way. Nothing here touches the
/// real %APPDATA% folder: every test works in a temp folder with a fake HTTP handler and a fake list of four
/// files, five megabytes in all, sized so that the 25, 50 and 75 percent milestones fall inside the files (the
/// first two inside "det.onnx", the third inside "rec.onnx") and are spoken as the bytes arrive.
/// </summary>
public sealed class TextRecognitionDownloaderTests : IDisposable
{
    private const int Version = 1;

    private static byte[] Blob(int length, int seed)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static readonly byte[] DetBytes = Blob(2_600_000, 1);
    private static readonly byte[] RecBytes = Blob(2_000_000, 2);
    private static readonly byte[] DictBytes = Blob(300_000, 3);
    private static readonly byte[] ClsBytes = Blob(100_000, 4);

    private static TextRecognitionFile Describe(string name, byte[] bytes)
        => new(name, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    // Five megabytes in all, four files like the real list, in the order the job fetches them.
    private static readonly IReadOnlyList<TextRecognitionFile> Files = new[]
    {
        Describe("det.onnx", DetBytes),
        Describe("rec.onnx", RecBytes),
        Describe("dict.txt", DictBytes),
        Describe("cls.onnx", ClsBytes),
    };

    private static readonly string[] Names = Files.Select(file => file.Name).ToArray();

    private static readonly long TotalBytes = Files.Sum(file => file.Bytes);

    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "msfsba-textrec-dl-" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    private readonly List<DownloadSpeech> _spoken = new();
    private readonly List<TextRecognitionStatus> _statuses = new();

    public TextRecognitionDownloaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // A folder that does not exist yet: the job creates it.
    private string Folder => Path.Combine(_dir, "text-recognition");

    private string MarkerPath => Path.Combine(Folder, TextRecognitionModels.MarkerName);

    private string PathOf(string name) => Path.Combine(Folder, name);

    private TextRecognitionDownloader Downloader(long freeBytes = long.MaxValue)
    {
        var d = new TextRecognitionDownloader(Folder, _server, _ => freeBytes, Files, Version) { RetryDelay = TimeSpan.Zero };
        Listen(d);
        return d;
    }

    private void Listen(TextRecognitionDownloader d)
    {
        d.Speak += (_, speech) => { lock (_spoken) _spoken.Add(speech); };
        d.StatusChanged += (_, status) => { lock (_statuses) _statuses.Add(status); };
    }

    private DownloadSpeech[] Speech() { lock (_spoken) return _spoken.ToArray(); }

    private string[] Said() => Speech().Select(speech => speech.Text).ToArray();

    private TextRecognitionStatus[] Statuses() { lock (_statuses) return _statuses.ToArray(); }

    private void ForgetSpeech() { lock (_spoken) _spoken.Clear(); }

    private string[] FolderListing() => Directory.GetFiles(Folder).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray();

    private string[] PartFiles() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.part") : Array.Empty<string>();

    private TextRecognitionInstall Check() => TextRecognitionModels.Check(Folder, Files, Version);

    private void Put(string name, byte[] bytes)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllBytes(PathOf(name), bytes);
    }

    private void PutAll()
    {
        Put("det.onnx", DetBytes);
        Put("rec.onnx", RecBytes);
        Put("dict.txt", DictBytes);
        Put("cls.onnx", ClsBytes);
    }

    /// <summary>Starts a job, waits until "rec.onnx" has begun (so "det.onnx" is whole and kept), cancels it and
    /// waits for the job to stop; the server is let go of afterwards so the next job runs freely.</summary>
    private async Task CancelDuringRecAsync(TextRecognitionDownloader d)
    {
        _server.HoldName = "rec.onnx";
        d.Start();
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        d.Cancel();
        await d.Job;
        _server.HoldName = null;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met within 10 seconds.");
            await Task.Delay(5);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Fresh_download_fetches_every_file_and_writes_the_marker_last()
    {
        bool markerTooEarly = false;
        _server.Observe = _ => markerTooEarly |= File.Exists(MarkerPath);
        using var d = Downloader();
        d.StatusChanged += (_, status) => markerTooEarly |= status.Phase == TextRecognitionPhase.Downloading && File.Exists(MarkerPath);

        d.Start();
        await d.Job;

        Assert.Equal(Names, _server.Requests);
        Assert.False(markerTooEarly);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(TextRecognitionText.Ready, d.Status.Text);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Ready, 4, 0), d.Installed());
        Assert.Equal(DetBytes, File.ReadAllBytes(PathOf("det.onnx")));
        Assert.Equal(RecBytes, File.ReadAllBytes(PathOf("rec.onnx")));
        Assert.Equal(DictBytes, File.ReadAllBytes(PathOf("dict.txt")));
        Assert.Equal(ClsBytes, File.ReadAllBytes(PathOf("cls.onnx")));
        Assert.Equal(TextRecognitionModels.MarkerJson(Files, Version), File.ReadAllText(MarkerPath));
        Assert.Equal(new[] { "cls.onnx", "det.onnx", "dict.txt", "installed.json", "rec.onnx" }, FolderListing());

        // The marker is the last thing written: no file was written after it.
        DateTime marker = File.GetLastWriteTimeUtc(MarkerPath);
        Assert.All(Files, file => Assert.True(marker >= File.GetLastWriteTimeUtc(PathOf(file.Name))));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_fresh_job_speaks_started_the_milestones_and_ready_in_order()
    {
        using var d = Downloader();
        d.Start();
        await d.Job;

        // Five megabytes in all; "det.onnx" alone is past 50 percent of it, "rec.onnx" brings 75.
        Assert.Equal(new[]
        {
            new DownloadSpeech("Downloading text recognition, 5 megabytes.", Immediate: true),
            new DownloadSpeech("Text recognition 25 percent.", Immediate: false),
            new DownloadSpeech("Text recognition 50 percent.", Immediate: false),
            new DownloadSpeech("Text recognition 75 percent.", Immediate: false),
            new DownloadSpeech("Text recognition ready.", Immediate: true),
        }, Speech());
    }

    [Fact(Timeout = 30_000)]
    public async Task The_status_changes_per_whole_percent_and_ends_with_the_outcome()
    {
        using var d = Downloader();
        d.Start();
        await d.Job;

        TextRecognitionStatus[] seen = Statuses();
        Assert.Equal(new TextRecognitionStatus(TextRecognitionPhase.Downloading, TextRecognitionText.Downloading(0), 0), seen[0]);
        Assert.Equal(new TextRecognitionStatus(TextRecognitionPhase.Ready, TextRecognitionText.Ready), seen[^1]);

        // Between them only progress: whole percents, rising, none repeated, each worded by the status text.
        TextRecognitionStatus[] progress = seen[1..^1];
        Assert.All(progress, status => Assert.Equal(TextRecognitionPhase.Downloading, status.Phase));
        Assert.All(progress, status => Assert.Equal(TextRecognitionText.Downloading(status.Percent), status.Text));
        int[] percents = seen[..^1].Select(status => status.Percent).ToArray();
        Assert.Equal(percents.Distinct().Order(), percents);
        Assert.Equal(100, percents[^1]);
    }

    [Theory, MemberData(nameof(Cultures))]
    public void A_job_speaks_and_shows_the_same_words_under_every_culture(string culture)
    {
        TestCulture.Run(culture, () =>
        {
            using var d = Downloader();
            d.Start();
            Assert.True(d.Job.Wait(TimeSpan.FromSeconds(30)));
            return 0;
        });

        Assert.Equal(new[]
        {
            "Downloading text recognition, 5 megabytes.",
            "Text recognition 25 percent.",
            "Text recognition 50 percent.",
            "Text recognition 75 percent.",
            "Text recognition ready.",
        }, Said());
        Assert.Equal("Downloading: 0 percent of 32 megabytes.", Statuses()[0].Text);
        Assert.Equal("Ready: PaddleOCR PP-OCRv6 small.", Statuses()[^1].Text);
    }

    [Fact(Timeout = 30_000)]
    public async Task Requests_say_who_is_asking()
    {
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(Names.Length, _server.UserAgents.Length);
        Assert.All(_server.UserAgents, agent => Assert.Matches(@"^MSFSBlindAssist/\d+(\.\d+)* \(text-recognition\)$", agent));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_failed_attempt_is_retried()
    {
        _server.Respond = (name, nth) =>
        {
            if (name != "rec.onnx") return null;
            if (nth == 1) throw new HttpRequestException("The connection was reset.");
            if (nth == 2) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            return null;
        };
        using var d = Downloader();
        d.Start();
        await d.Job;

        // Two attempts failed, the third succeeded: the job never mentions it.
        Assert.Equal(new[] { "det.onnx", "rec.onnx", "rec.onnx", "rec.onnx", "dict.txt", "cls.onnx" }, _server.Requests);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Ready, 4, 0), d.Installed());
        Assert.DoesNotContain(Said(), said => said.Contains("failed", StringComparison.Ordinal));
        Assert.Empty(PartFiles());
    }

    [Fact(Timeout = 30_000)]
    public async Task Three_failed_attempts_fail_the_job_and_keep_the_files_before_it()
    {
        _server.Respond = (name, _) => name == "rec.onnx" ? new HttpResponseMessage(HttpStatusCode.BadGateway) : null;
        using var d = Downloader();
        d.Start();
        await d.Job;

        // Three attempts at "rec.onnx", and "dict.txt" is never asked for.
        Assert.Equal(new[] { "det.onnx", "rec.onnx", "rec.onnx", "rec.onnx" }, _server.Requests);
        Assert.Equal(TextRecognitionPhase.Failed, d.Status.Phase);
        Assert.Equal("The last download failed: could not reach GitHub. Download again to retry.", d.Status.Text);
        Assert.Equal(d.Status, d.DisplayStatus()); // a failure stays on the status line until the next job
        Assert.Equal(new DownloadSpeech("Text recognition download failed: could not reach GitHub.", Immediate: true), Speech()[^1]);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Incomplete, 1, 2_400_000), Check());
        Assert.False(File.Exists(MarkerPath));
        Assert.Empty(PartFiles());
    }

    [Fact(Timeout = 30_000)]
    public async Task Retries_wait_longer_each_time()
    {
        var stamps = new List<long>();
        _server.Respond = (name, nth) =>
        {
            if (name != "det.onnx") return null;
            lock (stamps) stamps.Add(Stopwatch.GetTimestamp());
            return nth < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        };
        using var d = new TextRecognitionDownloader(Folder, _server, _ => long.MaxValue, Files, Version) { RetryDelay = TimeSpan.FromMilliseconds(150) };
        d.Start();
        await d.Job;

        // Attempt n waits n x the delay before attempt n + 1: 150 ms, then 300 ms. Only lower bounds, so a slow
        // machine never fails it; a constant delay would leave the second wait near 150 ms.
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(3, stamps.Count);
        Assert.True(Stopwatch.GetElapsedTime(stamps[0], stamps[1]) >= TimeSpan.FromMilliseconds(140));
        Assert.True(Stopwatch.GetElapsedTime(stamps[1], stamps[2]) >= TimeSpan.FromMilliseconds(290));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_checksum_mismatch_fails_and_keeps_the_verified_files()
    {
        byte[] damaged = (byte[])RecBytes.Clone();
        damaged[1_000] ^= 0xFF; // the right size, other content
        _server.Bodies["rec.onnx"] = damaged;
        using var d = Downloader();
        d.Start();
        await d.Job;

        // The damaged file is fetched again within its three attempts, then the job fails.
        Assert.Equal(new[] { "det.onnx", "rec.onnx", "rec.onnx", "rec.onnx" }, _server.Requests);
        Assert.Equal(TextRecognitionPhase.Failed, d.Status.Phase);
        Assert.Equal("The last download failed: a file did not match its checksum. Download again to retry.", d.Status.Text);
        Assert.Equal("Text recognition download failed: a file did not match its checksum.", Said()[^1]);

        // "det.onnx" was verified and stays; the damaged file never takes its place; no marker, no .part.
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Incomplete, 1, 2_400_000), d.Installed());
        Assert.Equal(new[] { "det.onnx" }, FolderListing());
    }

    [Fact(Timeout = 30_000)]
    public async Task Cancel_keeps_verified_files_and_the_next_job_fetches_only_the_rest()
    {
        using var d = Downloader();
        await CancelDuringRecAsync(d);

        Assert.Equal(TextRecognitionPhase.Cancelled, d.Status.Phase);
        Assert.Equal("Download cancelled. 1 of 4 files are kept; the next download fetches the rest, 2 megabytes.", d.Status.Text);
        Assert.Equal(d.Status, d.DisplayStatus());
        Assert.Equal(new DownloadSpeech("Text recognition download cancelled.", Immediate: true), Speech()[^1]);
        Assert.Equal(new[] { "det.onnx" }, FolderListing()); // the half of "rec.onnx" is gone, and there is no marker
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Incomplete, 1, 2_400_000), d.Installed());

        int before = _server.Requests.Length;
        d.Start();
        await d.Job;

        Assert.Equal(new[] { "rec.onnx", "dict.txt", "cls.onnx" }, _server.Requests[before..]);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Ready, 4, 0), d.Installed());
        Assert.Equal(RecBytes, File.ReadAllBytes(PathOf("rec.onnx")));
    }

    [Fact(Timeout = 30_000)]
    public async Task A_resumed_job_speaks_only_what_it_still_fetches()
    {
        using var d = Downloader();
        await CancelDuringRecAsync(d);
        ForgetSpeech();

        d.Start();
        await d.Job;

        // 2.4 of the 5 megabytes are left, and the 25 and 50 percent "det.onnx" already covered stay quiet.
        Assert.Equal(new[]
        {
            new DownloadSpeech("Downloading text recognition, 2 megabytes.", Immediate: true),
            new DownloadSpeech("Text recognition 75 percent.", Immediate: false),
            new DownloadSpeech("Text recognition ready.", Immediate: true),
        }, Speech());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_cancel_before_any_file_is_whole_keeps_nothing()
    {
        _server.HoldName = "det.onnx";
        using var d = Downloader();
        d.Start();
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        d.Cancel();
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Cancelled, d.Status.Phase);
        Assert.Equal("Download cancelled.", d.Status.Text);
        Assert.Equal("Text recognition download cancelled.", Said()[^1]);
        Assert.Empty(FolderListing());
        Assert.Equal(TextRecognitionState.Missing, d.Installed().State);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_full_disk_stops_before_any_request()
    {
        string? asked = null;
        using var d = new TextRecognitionDownloader(Folder, _server, folder =>
        {
            asked = folder;
            return TextRecognitionModels.RequiredFreeBytes - 1;
        }, Files, Version) { RetryDelay = TimeSpan.Zero };
        Listen(d);
        d.Start();
        await d.Job;

        Assert.Empty(_server.Requests);
        Assert.Equal(Folder, asked);
        Assert.Equal(TextRecognitionPhase.Failed, d.Status.Phase);
        Assert.Equal("The last download failed: not enough disk space, 100 megabytes needed. Download again to retry.", d.Status.Text);

        // The first and only thing said is the failure: the press confirmation waits for the disk check.
        Assert.Equal(new[] { new DownloadSpeech("Text recognition download failed: not enough disk space, 100 megabytes needed.", Immediate: true) }, Speech());
        Assert.Empty(FolderListing());
    }

    [Fact(Timeout = 30_000)]
    public async Task Exactly_the_required_free_space_is_enough()
    {
        using var d = Downloader(freeBytes: TextRecognitionModels.RequiredFreeBytes);
        d.Start();
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_missing_release_says_not_found()
    {
        _server.Bodies.Clear(); // every address answers 404, as a release that was never created does
        using var d = Downloader();
        d.Start();
        await d.Job;

        // A 404 is not retried: one request, then the failure.
        Assert.Equal(new[] { "det.onnx" }, _server.Requests);
        Assert.Equal(TextRecognitionPhase.Failed, d.Status.Phase);
        Assert.Equal("The last download failed: the files were not found on GitHub. Download again to retry.", d.Status.Text);
        Assert.Equal("Text recognition download failed: the files were not found on GitHub.", Said()[^1]);
        Assert.Empty(FolderListing());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_save_that_fails_says_the_files_could_not_be_saved()
    {
        // A folder where a model belongs: the finished download cannot be moved into place.
        Directory.CreateDirectory(PathOf("det.onnx"));
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Failed, d.Status.Phase);
        Assert.Equal("The last download failed: the files could not be saved. Download again to retry.", d.Status.Text);
        Assert.Empty(PartFiles());
        Assert.False(File.Exists(MarkerPath));
    }

    [Fact(Timeout = 30_000)]
    public async Task Files_not_in_the_list_are_removed()
    {
        Put("PP-OCRv5_old_model.onnx", Blob(1_000, 9));
        Put("notes.txt", Blob(10, 8));
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(new[] { "cls.onnx", "det.onnx", "dict.txt", "installed.json", "rec.onnx" }, FolderListing());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_part_file_left_by_a_killed_job_is_fetched_again()
    {
        // The whole, correct bytes under a .part name: only a job that deletes it and asks again gets past this.
        Put("det.onnx.part", DetBytes);
        Put("rec.onnx.part", RecBytes[..1_000]);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Missing, 0, TotalBytes), Check()); // a .part file never counts

        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(Names, _server.Requests);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(DetBytes, File.ReadAllBytes(PathOf("det.onnx")));
        Assert.Empty(PartFiles());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_killed_jobs_partial_files_are_gone_before_the_disk_is_checked()
    {
        // The leftovers of a killed job may be what the space is short of: the check has to see the folder after they went.
        Put("rec.onnx.part", RecBytes);
        string leftover = PathOf("rec.onnx.part");
        using var d = new TextRecognitionDownloader(Folder, _server, _ => File.Exists(leftover) ? 0 : long.MaxValue, Files, Version)
        {
            RetryDelay = TimeSpan.Zero,
        };
        d.Start();
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
    }

    [Fact(Timeout = 30_000)]
    public async Task A_damaged_file_of_the_right_size_is_fetched_again_and_the_good_ones_are_not()
    {
        // What a model that fails to load leaves: every file at its size, the marker gone. The job hashes them.
        PutAll();
        byte[] damaged = (byte[])RecBytes.Clone();
        damaged[500] ^= 0xFF;
        Put("rec.onnx", damaged);
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Incomplete, 4, 0), Check()); // the size check cannot see it

        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Equal(new[] { "rec.onnx" }, _server.Requests);
        Assert.Equal(RecBytes, File.ReadAllBytes(PathOf("rec.onnx")));
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);

        // The 3 megabytes that checked out are the starting point: 2 megabytes to fetch, and 75 percent is still ahead.
        Assert.Equal(new[] { "Downloading text recognition, 2 megabytes.", "Text recognition 75 percent.", "Text recognition ready." }, Said());
    }

    [Fact(Timeout = 30_000)]
    public async Task Files_that_are_all_here_are_checked_and_marked_without_a_request()
    {
        PutAll();
        using var d = Downloader();
        d.Start();
        await d.Job;

        Assert.Empty(_server.Requests);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(TextRecognitionModels.MarkerJson(Files, Version), File.ReadAllText(MarkerPath));
        Assert.Equal(new TextRecognitionInstall(TextRecognitionState.Ready, 4, 0), d.Installed());

        // There is no download to announce: the one thing said is that it is ready, and the status line goes
        // from the press straight to Ready, without a moment at 100 percent.
        Assert.Equal(new[] { new DownloadSpeech("Text recognition ready.", Immediate: true) }, Speech());
        Assert.Equal(new[]
        {
            new TextRecognitionStatus(TextRecognitionPhase.Downloading, TextRecognitionText.Downloading(0), 0),
            new TextRecognitionStatus(TextRecognitionPhase.Ready, TextRecognitionText.Ready),
        }, Statuses());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_job_that_fetches_only_a_sliver_says_less_than_a_megabyte()
    {
        // Every file but "cls.onnx" (100,000 bytes) is here and checks out.
        Put("det.onnx", DetBytes);
        Put("rec.onnx", RecBytes);
        Put("dict.txt", DictBytes);
        using var d = Downloader();
        d.Start();
        await d.Job;

        // Never "0 megabytes"; and the 98 percent already there leaves no milestone to speak.
        Assert.Equal(new[] { "cls.onnx" }, _server.Requests);
        Assert.Equal(new[] { "Downloading text recognition, less than 1 megabyte.", "Text recognition ready." }, Said());
    }

    [Fact(Timeout = 30_000)]
    public async Task An_outdated_install_is_updated_and_keeps_its_old_marker_until_the_end()
    {
        Put("det.onnx", DetBytes);
        Put("dict.txt", DictBytes);
        TextRecognitionModels.WriteMarker(Folder, Files, 0);
        string old = File.ReadAllText(MarkerPath);
        var markers = new List<string>();
        _server.Observe = _ => { lock (markers) markers.Add(File.ReadAllText(MarkerPath)); };
        using var d = Downloader();
        Assert.Equal(TextRecognitionState.Outdated, d.Installed().State);
        d.Start();
        await d.Job;

        // Only what is missing is fetched, and the old marker stands until the new one replaces it.
        Assert.Equal(new[] { "rec.onnx", "cls.onnx" }, _server.Requests);
        Assert.Equal(2, markers.Count);
        Assert.All(markers, marker => Assert.Equal(old, marker));
        Assert.Equal(TextRecognitionModels.MarkerJson(Files, Version), File.ReadAllText(MarkerPath));
        Assert.Equal(TextRecognitionState.Ready, d.Installed().State);
    }

    [Fact(Timeout = 30_000)]
    public async Task Start_while_running_starts_nothing()
    {
        _server.HoldName = "det.onnx";
        using var d = Downloader();

        // Pressed from the tab and from the offer window at once: eight starts, one job.
        Parallel.For(0, 8, _ => d.Start());
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task running = d.Job;
        d.Start();
        Assert.Same(running, d.Job);
        Assert.True(d.IsRunning);

        _server.Release.SetResult();
        await d.Job;

        Assert.Equal(Names, _server.Requests); // every file asked for once
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Single(Said(), said => said.StartsWith("Downloading text recognition, ", StringComparison.Ordinal));
    }

    [Fact(Timeout = 30_000)]
    public async Task Shutdown_cancels_without_speaking()
    {
        _server.HoldName = "rec.onnx";
        using var d = Downloader();
        d.Start();
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        int before = Speech().Length;

        d.Shutdown(TimeSpan.FromSeconds(10));

        Assert.True(d.Job.IsCompleted); // Shutdown waited for the job to let go of its files
        Assert.Equal(before, Speech().Length);
        Assert.Equal(TextRecognitionPhase.Cancelled, d.Status.Phase);
        Assert.Empty(PartFiles());
        Assert.Equal(new[] { "det.onnx" }, FolderListing());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_stalled_attempt_is_announced_abandoned_and_retried()
    {
        var clock = new FakeClock();
        _server.HoldName = "det.onnx"; // the first attempt gets half the file, then the line goes quiet
        using var d = new TextRecognitionDownloader(Folder, _server, _ => long.MaxValue, Files, Version)
        {
            RetryDelay = TimeSpan.Zero,
            StallCheckInterval = TimeSpan.FromMilliseconds(5),
            UtcNow = clock.Now,
        };
        Listen(d);
        d.Start();
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _server.HoldName = null; // the retry is served whole

        clock.Advance(TimeSpan.FromSeconds(21));
        await WaitUntilAsync(() => Said().Contains("Text recognition download stalled."));
        clock.Advance(TimeSpan.FromSeconds(40)); // 61 seconds without a byte
        await d.Job;

        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase);
        Assert.Equal(new[] { "det.onnx", "det.onnx", "rec.onnx", "dict.txt", "cls.onnx" }, _server.Requests);
        string[] said = Said();
        Assert.Single(said, "Text recognition download stalled.");
        Assert.Single(said, "Download resumed.");
        Assert.True(Array.IndexOf(said, "Download resumed.") > Array.IndexOf(said, "Text recognition download stalled."));
        Assert.Equal(DetBytes, File.ReadAllBytes(PathOf("det.onnx")));

        // The abandoned attempt's bytes were taken back, so the status fell to the start of the file and climbed again.
        int[] percents = Statuses().Where(status => status.Phase == TextRecognitionPhase.Downloading).Select(status => status.Percent).ToArray();
        Assert.Contains(percents.Zip(percents.Skip(1)), pair => pair.Second < pair.First);
        Assert.Equal(100, percents[^1]);
    }

    [Fact(Timeout = 30_000)]
    public async Task DisplayStatus_shows_the_running_or_last_job_else_the_folder()
    {
        using var d = Downloader();

        Assert.False(d.IsRunning);
        Assert.Equal(TextRecognitionPhase.Idle, d.Status.Phase);
        Assert.Equal("Not downloaded. The download is 32 megabytes.", d.Status.Text);
        Assert.Equal(new TextRecognitionStatus(TextRecognitionPhase.Idle, TextRecognitionText.Idle(d.Installed())), d.DisplayStatus());

        _server.HoldName = "det.onnx";
        d.Start();
        await _server.BodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(d.IsRunning);
        Assert.True(d.Status.IsRunning);
        Assert.Equal(TextRecognitionPhase.Downloading, d.DisplayStatus().Phase);

        _server.Release.SetResult();
        await d.Job;

        Assert.False(d.IsRunning);
        Assert.Equal(new TextRecognitionStatus(TextRecognitionPhase.Ready, TextRecognitionText.Ready), d.DisplayStatus());
    }

    [Fact(Timeout = 30_000)]
    public async Task A_ready_status_does_not_outlive_the_files()
    {
        using var d = Downloader();
        d.Start();
        await d.Job;
        File.Delete(PathOf("rec.onnx")); // an antivirus quarantine, or a model that failed to load

        // The tab and the offer must not claim a download that is no longer here, nor disable pressing Download.
        TextRecognitionStatus shown = d.DisplayStatus();
        Assert.Equal(TextRecognitionPhase.Idle, shown.Phase);
        Assert.Equal("Incomplete: 3 of 4 files are kept; the next download fetches the rest, 2 megabytes.", shown.Text);
        Assert.Equal(TextRecognitionPhase.Ready, d.Status.Phase); // the job's own record is unchanged

        int before = _server.Requests.Length;
        d.Start();
        await d.Job;

        Assert.Equal(new[] { "rec.onnx" }, _server.Requests[before..]);
        Assert.Equal(TextRecognitionState.Ready, d.Installed().State);
    }

    /// <summary>A clock the test moves by hand, so a 20 and a 60 second silence takes no time.</summary>
    private sealed class FakeClock
    {
        private long _ticks = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc).Ticks;

        internal DateTime Now() => new(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

        internal void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <summary>Serves the four made-up files as the release's addresses would.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        internal readonly Dictionary<string, byte[]> Bodies = new()
        {
            ["det.onnx"] = DetBytes,
            ["rec.onnx"] = RecBytes,
            ["dict.txt"] = DictBytes,
            ["cls.onnx"] = ClsBytes,
        };

        private readonly List<string> _requests = new();
        private readonly List<string> _userAgents = new();

        /// <summary>Answers a request itself: given the file name and how often it was asked for (1 the first
        /// time), it returns a response, throws, or returns null to let the body be served as usual.</summary>
        internal Func<string, int, HttpResponseMessage?>? Respond;

        /// <summary>Called with the file name of each request before it is answered.</summary>
        internal Action<string>? Observe;

        /// <summary>The file whose body is served half and then held until released (a slow or stalled line).</summary>
        internal volatile string? HoldName;

        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource BodyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal string[] Requests { get { lock (_requests) return _requests.ToArray(); } }

        internal string[] UserAgents { get { lock (_requests) return _userAgents.ToArray(); } }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string url = request.RequestUri!.ToString();
            string name = url.StartsWith(TextRecognitionModels.BaseUrl, StringComparison.Ordinal) ? url[TextRecognitionModels.BaseUrl.Length..] : url;
            int nth;
            lock (_requests)
            {
                _requests.Add(name);
                _userAgents.Add(request.Headers.UserAgent.ToString());
                nth = _requests.Count(asked => asked == name);
            }
            Observe?.Invoke(name);
            await Task.Yield();

            if (Respond?.Invoke(name, nth) is { } answer) return answer;
            if (!Bodies.TryGetValue(name, out byte[]? body)) return new HttpResponseMessage(HttpStatusCode.NotFound);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = name == HoldName ? new StreamContent(new HeldStream(body, Release, BodyStarted)) : new ByteArrayContent(body),
            };
            response.Content.Headers.ContentLength = body.Length;
            return response;
        }
    }

    /// <summary>Gives the first half of the bytes, then blocks (honoring the reader's token) until released.</summary>
    private sealed class HeldStream(byte[] bytes, TaskCompletionSource hold, TaskCompletionSource started) : Stream
    {
        private int _position;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            int half = bytes.Length / 2;
            if (_position >= half && !hold.Task.IsCompleted)
            {
                started.TrySetResult();
                await hold.Task.WaitAsync(ct);
            }
            int limit = _position < half ? half : bytes.Length;
            int n = Math.Min(buffer.Length, limit - _position);
            bytes.AsMemory(_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => bytes.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
