using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>
/// Every sentence the text recognition download shows (the Settings tab's status box, the offer window) or
/// speaks, and the offer window's button labels. Pure and invariant: a German or Swedish Windows hears and
/// reads the same words, so every number is formatted with the invariant culture. The megabyte figures and
/// the file count come from <see cref="TextRecognitionModels"/>, never typed in, so a later file list keeps
/// them right.
/// </summary>
internal static class TextRecognitionText
{
    /// <summary>The size of the whole download in the megabytes a pilot counts: 32 for the first list.</summary>
    private static int ListMegabytes => TextRecognitionModels.Megabytes(TextRecognitionModels.TotalBytes);

    // ---- the status line (the Settings tab, the offer window) ----

    /// <summary>The status line of an installed set; also what a finished job shows.</summary>
    internal const string Ready = "Ready: PaddleOCR PP-OCRv6 small.";

    /// <summary>The status line when no job runs, from what the folder holds. An update names the whole
    /// download, whatever files it keeps; an incomplete set names what is still to fetch.</summary>
    internal static string Idle(TextRecognitionInstall install) => install.State switch
    {
        TextRecognitionState.Ready => Ready,
        TextRecognitionState.Outdated => string.Create(CultureInfo.InvariantCulture,
            $"An update is needed: this version of the app uses new text recognition files. The download is {ListMegabytes} megabytes."),
        TextRecognitionState.Incomplete when install.BytesToFetch > 0 => string.Create(CultureInfo.InvariantCulture,
            $"Incomplete: {install.FilesKept} of {TextRecognitionModels.Files.Count} files are kept; the next download fetches the rest, {TextRecognitionModels.Megabytes(install.BytesToFetch)} megabytes."),
        // Every file is at its size but there is no marker: nothing to fetch, only the files to check.
        TextRecognitionState.Incomplete => "Incomplete: the files are here but not checked. Download text recognition to check them.",
        _ => string.Create(CultureInfo.InvariantCulture, $"Not downloaded. The download is {ListMegabytes} megabytes."),
    };

    internal static string Downloading(int percent)
        => string.Create(CultureInfo.InvariantCulture, $"Downloading: {percent} percent of {ListMegabytes} megabytes.");

    internal static string Failed(string reason) => $"The last download failed: {reason}. Download again to retry.";

    /// <summary>The status line after the pilot cancelled: the files kept, and the megabytes the next download fetches.</summary>
    internal static string Cancelled(int kept, long bytesLeft) => kept <= 0
        ? "Download cancelled."
        : string.Create(CultureInfo.InvariantCulture,
            $"Download cancelled. {kept} of {TextRecognitionModels.Files.Count} files are kept; the next download fetches the rest, {TextRecognitionModels.Megabytes(bytesLeft)} megabytes.");

    // ---- spoken ----

    /// <summary>The press confirmation: the megabytes this job still fetches (all 32 on a fresh download).</summary>
    internal static string Started(long bytesToFetch)
        => string.Create(CultureInfo.InvariantCulture, $"Downloading text recognition, {TextRecognitionModels.Megabytes(bytesToFetch)} megabytes.");

    /// <summary>A milestone (25, 50, 75), queued so it never cuts the pilot off.</summary>
    internal static string Progress(int percent)
        => string.Create(CultureInfo.InvariantCulture, $"Text recognition {percent} percent.");

    internal const string Stalled = "Text recognition download stalled.";

    internal const string Resumed = "Download resumed.";

    internal const string ReadySpoken = "Text recognition ready.";

    internal static string FailedSpoken(string reason) => $"Text recognition download failed: {reason}.";

    internal const string CancelledSpoken = "Text recognition download cancelled.";

    /// <summary>
    /// Why a job failed, as the pilot hears it after "Text recognition download failed: ". The disk check's own
    /// exception and a 404 are told apart from the network; a connection that broke while the body was read is an
    /// <see cref="IOException"/> too, but the network's fault, not the disk's. Anything unforeseen is passed on as
    /// the exception said it, without the full stop the sentence supplies itself.
    /// </summary>
    internal static string FailureReason(Exception ex) => ex switch
    {
        TextRecognitionDiskSpaceException => string.Create(CultureInfo.InvariantCulture,
            $"not enough disk space, {TextRecognitionModels.Megabytes(TextRecognitionModels.RequiredFreeBytes)} megabytes needed"),
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "the files were not found on GitHub",
        HttpRequestException => "could not reach GitHub",
        InvalidDataException => "a file did not match its checksum",
        IOException io when IsNetworkError(io) => "could not reach GitHub",
        IOException or UnauthorizedAccessException => "the files could not be saved",
        // Reached only when the pilot did not cancel: a header timeout or a stall that used up its attempts.
        OperationCanceledException => "could not reach GitHub",
        _ => Plain(ex.Message),
    };

    /// <summary>A connection that broke mid-transfer (the response ended early, or a socket error under the
    /// transport stream), never a local file.</summary>
    private static bool IsNetworkError(IOException io) =>
        io is HttpIOException || io.InnerException is SocketException || io.InnerException?.InnerException is SocketException;

    private static string Plain(string message)
    {
        string plain = message.Trim().TrimEnd('.').TrimEnd();
        return plain.Length > 0 ? plain : "an unexpected error";
    }

    /// <summary>The BlueBird 757 tablet reader's alert when the engine could not start; the pilot is sent to the tab.</summary>
    internal const string ReaderCannotStart =
        "Text recognition could not start, so the tablet cannot be read. Download it again from Settings, Text recognition.";

    // ---- the offer window ----

    /// <summary>The offer window's status box: why the reader asks, and where else the download lives.</summary>
    internal static string OfferStatus => string.Create(CultureInfo.InvariantCulture,
        $"The 757 tablet reader needs text recognition: PaddleOCR, a {ListMegabytes} megabyte download. You can also download it later from Settings, Text recognition.");

    /// <summary>
    /// The offer window's download button: its label, and whether a press starts a download. Running or installed
    /// it reads what is going on and a press starts nothing (it is never dimmed: a dimmed control drops out of the
    /// tab order); otherwise, a failure and a cancel included, it reads "Download text recognition" again.
    /// </summary>
    internal static (string Text, bool PressStarts) OfferButton(TextRecognitionPhase phase, TextRecognitionState state)
    {
        if (phase == TextRecognitionPhase.Downloading) return ("Downloading text recognition", false);
        if (phase == TextRecognitionPhase.Ready || state == TextRecognitionState.Ready) return ("Text recognition ready", false);
        return ("&Download text recognition", true);
    }
}
