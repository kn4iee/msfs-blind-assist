using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using MSFSBlindAssist.Services.Geography;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>Where the download stands, as the Settings tab and the offer window show it.</summary>
internal enum TextRecognitionPhase { Idle, Downloading, Ready, Failed, Cancelled }

/// <summary>The job's state for a status box. <see cref="Percent"/> is -1 when the phase has no percentage.</summary>
internal sealed record TextRecognitionStatus(TextRecognitionPhase Phase, string Text, int Percent = -1)
{
    internal bool IsRunning => Phase == TextRecognitionPhase.Downloading;
}

/// <summary>Thrown before any request when the drive holding the folder has less free space than a job needs.</summary>
internal sealed class TextRecognitionDiskSpaceException(long freeBytes)
    : Exception(string.Create(CultureInfo.InvariantCulture,
        $"Only {freeBytes} bytes are free; a text recognition download needs {TextRecognitionModels.RequiredFreeBytes}."));

/// <summary>
/// Downloads the text recognition model files from the app's own GitHub release, one job at a time. Owned by
/// MainForm so it keeps running when Settings closes; it holds no announcer and touches no UI: it raises
/// <see cref="StatusChanged"/> and <see cref="Speak"/> on worker threads and the listeners marshal (the
/// announcer is UI-thread only). It follows <c>LocationDataDownloader</c> (LOC-5's speech: milestones, stalls
/// and the end, never per chunk) with a smaller job: whole-file requests, no resume, no build step.
///
/// <para>A job hashes what is already in the folder and fetches only the rest, each file into
/// <c>&lt;name&gt;.part</c>, hashed again before it is renamed into place. A read that goes quiet for a minute is
/// abandoned and the file asked for again, up to three attempts. At the end it removes the files the list does
/// not name and writes the marker LAST, so a folder is never Ready with a file unverified; the pilot's cancel
/// keeps every verified file for the next job.</para>
/// </summary>
internal sealed class TextRecognitionDownloader : IDisposable
{
    private const string LogCategory = "TextRecognition";
    private const string PartSuffix = ".part";
    private const int MaxAttempts = 3;
    private const int BufferSize = 1 << 16;
    private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);

    private readonly string _folder;
    private readonly IReadOnlyList<TextRecognitionFile> _files;
    private readonly int _listVersion;
    private readonly Func<string, long> _freeBytes;
    private readonly HttpClient _client;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _job;
    private volatile bool _silentCancel;
    private TextRecognitionStatus _status;

    /// <param name="folder">The models folder (default <see cref="TextRecognitionModels.DefaultFolder"/>).</param>
    /// <param name="handler">Test seam: the HTTP handler.</param>
    /// <param name="freeBytes">Test seam: free bytes on the drive of the folder it is given.</param>
    /// <param name="files">Test seam: the file list (default <see cref="TextRecognitionModels.Files"/>).</param>
    /// <param name="listVersion">Test seam: the list version the marker records.</param>
    internal TextRecognitionDownloader(string? folder = null, HttpMessageHandler? handler = null, Func<string, long>? freeBytes = null,
        IReadOnlyList<TextRecognitionFile>? files = null, int listVersion = TextRecognitionModels.ListVersion)
    {
        _folder = folder ?? TextRecognitionModels.DefaultFolder();
        _client = CreateClient(handler ?? CreateHandler());
        _freeBytes = freeBytes ?? LocationDataDownloader.FreeBytes;
        _files = files ?? TextRecognitionModels.Files;
        _listVersion = listVersion;
        _status = IdleStatus();
    }

    /// <summary>Test seam: after a failed attempt n the job waits n times this before attempt n + 1.</summary>
    internal TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Test seam: how often the stall watch looks at the clock.</summary>
    internal TimeSpan StallCheckInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Test seam: the clock the stall watch reads.</summary>
    internal Func<DateTime> UtcNow { get; init; } = static () => DateTime.UtcNow;

    /// <summary>The running (or last) job.</summary>
    internal Task Job { get { lock (_gate) return _job ?? Task.CompletedTask; } }

    /// <summary>Raised whenever the status changes (a phase change or a whole percent): on the job's worker thread,
    /// except the first, which <see cref="Start"/> raises on its caller's thread. Listeners marshal.</summary>
    internal event EventHandler<TextRecognitionStatus>? StatusChanged;

    /// <summary>Raised on a worker thread with a sentence to speak. Listeners marshal.</summary>
    internal event EventHandler<DownloadSpeech>? Speak;

    internal TextRecognitionStatus Status => Volatile.Read(ref _status);

    internal bool IsRunning => Status.IsRunning;

    /// <summary>What the folder holds, read now: the tab, the offer and the reader ask this.</summary>
    internal TextRecognitionInstall Installed() => TextRecognitionModels.Check(_folder, _files, _listVersion);

    /// <summary>
    /// What a status box shows: the running or last job's status, or what the folder holds when none ran. A
    /// finished job's Ready is not kept past its files: models removed afterwards (an antivirus quarantine, or a
    /// model that failed to load and took the marker with it) read as what the folder holds, so the pilot can
    /// download again in the same session. A failure or a cancel stays until the next job.
    /// </summary>
    internal TextRecognitionStatus DisplayStatus()
    {
        TextRecognitionStatus status = Status;
        bool stale = status.Phase == TextRecognitionPhase.Ready && Installed().State != TextRecognitionState.Ready;
        return status.Phase == TextRecognitionPhase.Idle || stale ? IdleStatus() : status;
    }

    private TextRecognitionStatus IdleStatus() => new(TextRecognitionPhase.Idle, TextRecognitionText.Idle(Installed()));

    /// <summary>Starts a download (or the check and repair of what is in the folder). Ignored while a job runs.</summary>
    internal void Start()
    {
        lock (_gate)
        {
            if (_job is { IsCompleted: false }) return;
            _silentCancel = false;
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            CancellationToken ct = _cts.Token;
            SetStatus(new TextRecognitionStatus(TextRecognitionPhase.Downloading, TextRecognitionText.Downloading(0), 0));
            _job = Task.Run(() => RunAsync(ct));
        }
    }

    /// <summary>The pilot's Cancel: spoken, and the files verified so far are kept for the next job.</summary>
    internal void Cancel()
    {
        lock (_gate) _cts?.Cancel();
    }

    /// <summary>App close: cancels without speaking and waits briefly for the job to let go of its files.</summary>
    internal void Shutdown(TimeSpan wait)
    {
        Task? job;
        lock (_gate)
        {
            _silentCancel = true;
            _cts?.Cancel();
            job = _job;
        }
        try { job?.Wait(wait); }
        catch (AggregateException) { }
    }

    public void Dispose()
    {
        _client.Dispose();
        _cts?.Dispose();
    }

    // ---- the job ----

    /// <summary>The bytes of the whole list that are on disk and verified so far, with the gate that decides
    /// which milestones are spoken.</summary>
    private sealed class JobProgress
    {
        internal JobProgress(long total, long done)
        {
            Total = total;
            Done = done;
            // Bytes kept from an earlier job were not downloaded now, so the milestones they cover stay quiet.
            Gate.Restart(done, total);
        }

        internal long Total { get; }
        internal long Done { get; set; }
        internal DownloadProgressGate Gate { get; } = new();

        /// <summary>The whole percent last shown; the status Start raised showed 0.</summary>
        internal int Percent { get; set; }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            // Before the disk check: the partial files of a killed job may be what the space is short of.
            DeletePartFiles();
            CheckSpace();

            // A file counts as kept only when its SHA-256 matches, not merely its size. A model that failed to
            // load leaves every file at its size and no marker; the damaged one is found here and fetched again.
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long done = 0;
            foreach (TextRecognitionFile file in _files)
            {
                if (!await IsKeptAsync(file, ct).ConfigureAwait(false)) continue;
                kept.Add(file.Name);
                done += file.Bytes;
            }

            long total = _files.Sum(file => file.Bytes);
            var progress = new JobProgress(total, done);
            Log.Info(LogCategory, $"Download started: {kept.Count} of {_files.Count} files kept, {total - done} bytes to fetch");
            Say(TextRecognitionText.Started(total - done), immediate: true);
            ReportProgress(progress);

            var stall = new StallDetector(UtcNow());
            foreach (TextRecognitionFile file in _files)
            {
                if (!kept.Contains(file.Name)) await FetchAsync(file, progress, stall, ct).ConfigureAwait(false);
            }

            RemoveUnlistedFiles();
            // Last: every file above was verified, and a folder with this marker reads Ready.
            TextRecognitionModels.WriteMarker(_folder, _files, _listVersion);
            Log.Info(LogCategory, "Download finished: the marker is written");
            SetStatus(new TextRecognitionStatus(TextRecognitionPhase.Ready, TextRecognitionText.Ready));
            Say(TextRecognitionText.ReadySpoken, immediate: true);
        }
        // Whatever surfaces once the pilot has cancelled (the cancelled read, or a stream torn down under it) is the cancel.
        catch (Exception) when (ct.IsCancellationRequested)
        {
            TextRecognitionInstall left = Installed();
            SetStatus(new TextRecognitionStatus(TextRecognitionPhase.Cancelled, TextRecognitionText.Cancelled(left.FilesKept, left.BytesToFetch)));
            if (!_silentCancel) Say(TextRecognitionText.CancelledSpoken, immediate: true);
            Log.Info(LogCategory, $"Download cancelled{(_silentCancel ? " (app closing)" : "")}: {left.FilesKept} files kept");
        }
        catch (Exception ex)
        {
            string reason = TextRecognitionText.FailureReason(ex);
            Log.Error(LogCategory, $"Download failed: {reason}", ex);
            SetStatus(new TextRecognitionStatus(TextRecognitionPhase.Failed, TextRecognitionText.Failed(reason)));
            if (!_silentCancel) Say(TextRecognitionText.FailedSpoken(reason), immediate: true);
        }
    }

    private void CheckSpace()
    {
        long free = _freeBytes(_folder);
        if (free < TextRecognitionModels.RequiredFreeBytes) throw new TextRecognitionDiskSpaceException(free);
    }

    /// <summary>One file, whole: asked for, written to <c>&lt;name&gt;.part</c>, hashed and renamed into place. A
    /// failed attempt (the network, a stall, a wrong hash) is tried again after n times <see cref="RetryDelay"/>,
    /// up to <see cref="MaxAttempts"/>; a 404 and a failing disk end the job at once.</summary>
    private async Task FetchAsync(TextRecognitionFile file, JobProgress progress, StallDetector stall, CancellationToken ct)
    {
        string path = Path.Combine(_folder, file.Name);
        string part = path + PartSuffix;
        string url = TextRecognitionModels.BaseUrl + file.Name;
        for (int attempt = 1; ; attempt++)
        {
            long counted = 0; // this attempt's bytes in the progress, taken back if the attempt fails
            try
            {
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using HttpResponseMessage response = await SendAsync(request, attemptCts.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                await using (Stream body = await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false))
                {
                    // The silence clock starts with the body: the wait for the headers has its own timeout.
                    stall.Restart(UtcNow());
                    await CopyWithStallWatchAsync(body, output, read =>
                    {
                        counted += read;
                        if (stall.OnBytes(UtcNow())) Say(TextRecognitionText.Resumed, immediate: false);
                        Advance(progress, read);
                    }, stall, attemptCts).ConfigureAwait(false);
                }

                if (!string.Equals(await HashAsync(part, ct).ConfigureAwait(false), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{file.Name} did not match its published checksum.");
                File.Move(part, path, overwrite: true);
                Log.Info(LogCategory, $"{file.Name}: fetched and verified");
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && LocationDataDownloader.IsRetryable(ex))
            {
                progress.Done -= counted;
                ReportProgress(progress);
                Log.Warn(LogCategory, $"{file.Name}: attempt {attempt} of {MaxAttempts} failed ({ex.Message}); trying again");
                await Task.Delay(RetryDelay * attempt, ct).ConfigureAwait(false);
            }
            finally
            {
                // Gone after a move; after a failed or cancelled attempt it is a partial file nothing can resume.
                DeleteQuietly(part);
            }
        }
    }

    /// <summary>Copies a body, cancelling the attempt when the stall detector says so (a stalled read never returns
    /// by itself). Speaks the stall once, at 20 seconds. <paramref name="onRead"/> must feed
    /// <see cref="StallDetector.OnBytes"/> itself.</summary>
    private async Task CopyWithStallWatchAsync(Stream body, Stream output, Action<int> onRead, StallDetector stall, CancellationTokenSource attemptCts)
    {
        CancellationToken ct = attemptCts.Token;
        using var watchdog = new System.Threading.Timer(_ =>
        {
            switch (stall.Check(UtcNow()))
            {
                case StallVerdict.AnnounceStall:
                    Say(TextRecognitionText.Stalled, immediate: false);
                    break;
                case StallVerdict.Abort:
                    try { attemptCts.Cancel(); } catch (ObjectDisposedException) { }
                    break;
            }
        }, null, StallCheckInterval, StallCheckInterval);

        var buffer = new byte[BufferSize];
        int read;
        while ((read = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            onRead(read);
        }
    }

    /// <summary>Counts bytes that arrived: the status box at each whole percent, and a spoken milestone when the
    /// gate allows one.</summary>
    private void Advance(JobProgress progress, long bytes)
    {
        progress.Done += bytes;
        ReportProgress(progress);
        if (progress.Gate.Observe(progress.Done, progress.Total) is int milestone)
            Say(TextRecognitionText.Progress(milestone), immediate: false);
    }

    private void ReportProgress(JobProgress progress)
    {
        int percent = DownloadProgressGate.WholePercent(progress.Done, progress.Total);
        if (percent == progress.Percent) return;
        progress.Percent = percent;
        SetStatus(new TextRecognitionStatus(TextRecognitionPhase.Downloading, TextRecognitionText.Downloading(percent), percent));
    }

    // ---- the folder ----

    /// <summary>True when the file is here at its size with the SHA-256 the list pins.</summary>
    private async Task<bool> IsKeptAsync(TextRecognitionFile file, CancellationToken ct)
    {
        string path = Path.Combine(_folder, file.Name);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Bytes) return false;
            return string.Equals(await HashAsync(path, ct).ConfigureAwait(false), file.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file nobody can read is not kept: the fetch replaces it, or fails saying the files could not be saved.
            Log.Warn(LogCategory, $"{file.Name} could not be read, so it is fetched again: {ex.Message}");
            return false;
        }
    }

    /// <summary>The partial files of a job that was killed. A <c>.part</c> file is never resumed and never counts.</summary>
    private void DeletePartFiles()
    {
        foreach (string path in Directory.GetFiles(_folder))
        {
            if (!path.EndsWith(PartSuffix, StringComparison.OrdinalIgnoreCase)) continue;
            Log.Info(LogCategory, $"Removing {Path.GetFileName(path)}, left by a download that did not finish");
            DeleteQuietly(path);
        }
    }

    /// <summary>Every file the list does not name goes, the marker excepted: models of an older list, and anything else
    /// that does not belong in the folder.</summary>
    private void RemoveUnlistedFiles()
    {
        var wanted = new HashSet<string>(_files.Select(file => file.Name), StringComparer.OrdinalIgnoreCase) { TextRecognitionModels.MarkerName };
        foreach (string path in Directory.GetFiles(_folder))
        {
            string name = Path.GetFileName(path);
            if (wanted.Contains(name)) continue;
            Log.Info(LogCategory, $"Removing {name}, which the file list does not name");
            DeleteQuietly(path);
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn(LogCategory, $"Could not delete {Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }

    // ---- helpers ----

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct);
        headers.CancelAfter(HeaderTimeout);
        return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token).ConfigureAwait(false);
    }

    private void SetStatus(TextRecognitionStatus status)
    {
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, status);
    }

    private void Say(string text, bool immediate) => Speak?.Invoke(this, new DownloadSpeech(text, immediate));

    private static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        AutomaticDecompression = DecompressionMethods.All,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
    };

    private static HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        string version = typeof(TextRecognitionDownloader).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"MSFSBlindAssist/{version} (text-recognition)");
        return client;
    }
}
