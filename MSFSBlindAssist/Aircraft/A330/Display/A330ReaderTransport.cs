using System.Diagnostics;
using System.Text;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.A330.Display;

/// The line channel to the display reader: one line out, one line in, and a notice when the program ends.
/// LineReceived and Exited arrive on the transport's own thread.
internal interface IA330ReaderTransport : IDisposable
{
    void WriteLine(string line);
    event Action<string>? LineReceived;      // one of the reader's lines
    event Action? Exited;
}

/// The display reader as a program the app starts beside itself: no window, lines on standard input and output,
/// its standard error in the "a330-displays" log. It joins the app's kill-on-close job, so it ends with the app.
internal sealed class A330ReaderProcess : IA330ReaderTransport
{
    public const string FileName = "A330DisplayReader.exe";

    private const string LogName = "a330-displays";

    private static readonly object JobLock = new();
    private static KillOnCloseJob? _job;
    private static bool _jobTried;

    private readonly Process _process;
    private int _exitedRaised;
    private int _disposed;
    private int _writeFailureLogged;

    public event Action<string>? LineReceived;
    public event Action? Exited;

    private A330ReaderProcess(Process process) => _process = process;

    /// The program beside the app's exe; null when it is not there.
    public static string? NextToApp()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FileName);
        return File.Exists(path) ? path : null;
    }

    /// Starts the program with no window and redirected input, output and error, and puts it in the job. Null (logged) when it will not start.
    public static A330ReaderProcess? Start(string path)
    {
        Process? process = null;
        try
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
            };
            process = new Process { StartInfo = info, EnableRaisingEvents = true };
            var result = new A330ReaderProcess(process);
            process.OutputDataReceived += result.OnOutput;
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log.Channel(LogName).Info(e.Data);
            };
            process.Exited += (_, _) => result.RaiseExited();
            process.Start();
            JoinJob(process);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return result;
        }
        catch (Exception ex)
        {
            Log.Channel(LogName).Error("the display reader did not start: " + ex.Message);
            try { process?.Dispose(); } catch (Exception) { }
            return null;
        }
    }

    private static void JoinJob(Process process)
    {
        try
        {
            KillOnCloseJob? job;
            lock (JobLock)
            {
                if (!_jobTried)
                {
                    _jobTried = true;
                    _job = KillOnCloseJob.TryCreate(out string? failure);
                    if (_job == null)
                        Log.Channel(LogName).Warn("no kill-on-close job: " + failure + "; the display reader keeps running if the app is killed");
                }
                job = _job;
            }
            if (job != null && !job.TryAssign(process, out string? assignFailure))
                Log.Channel(LogName).Warn("the display reader is not in the job: " + assignFailure);
        }
        catch (Exception ex)
        {
            Log.Channel(LogName).Warn("the display reader is not in the job: " + ex.Message);
        }
    }

    private void OnOutput(object sender, DataReceivedEventArgs e)
    {
        if (e.Data == null || Volatile.Read(ref _disposed) != 0) return;
        try { LineReceived?.Invoke(e.Data); }
        catch (Exception ex) { Log.Channel(LogName).Error("a reader line handler threw: " + ex.Message); }
    }

    private void RaiseExited()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _exitedRaised, 1) != 0) return;
        try { Exited?.Invoke(); }
        catch (Exception ex) { Log.Channel(LogName).Error("an exit handler threw: " + ex.Message); }
    }

    public void WriteLine(string line)
    {
        try
        {
            var input = _process.StandardInput;
            input.Write(line + "\n");
            input.Flush();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            if (Interlocked.Exchange(ref _writeFailureLogged, 1) == 0)
                Log.Channel(LogName).Info("a line could not be sent, the display reader has gone: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _process.StandardInput.Close(); } catch (Exception) { }
        try
        {
            if (!_process.WaitForExit(500)) _process.Kill(true);
        }
        catch (Exception) { }
        try { _process.Dispose(); } catch (Exception) { }
    }
}
