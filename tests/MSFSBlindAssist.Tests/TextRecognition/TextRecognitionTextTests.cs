using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// Every sentence the text recognition download shows or speaks, word for word. The megabyte figures and the
/// file count are computed from the real file list, so these tests assert today's values (32 megabytes, 4
/// files). Each test runs under en-US, de-DE and sv-SE: the pilot's Windows region must not change a word.
/// </summary>
public sealed class TextRecognitionTextTests
{
    public static TheoryData<string> Cultures => new() { "en-US", "de-DE", "sv-SE" };

    private static TextRecognitionInstall Install(TextRecognitionState state, int kept, long toFetch) => new(state, kept, toFetch);

    private static void Under(string culture, Action check) => TestCulture.Run(culture, () =>
    {
        check();
        return 0;
    });

    // The rec model and the dictionary: the two files a download still fetches when the other two are kept.
    private const long RecAndDictionary = 21_234_383 + 74_947;

    [Theory, MemberData(nameof(Cultures))]
    public void Idle_says_what_the_folder_holds(string culture) => Under(culture, () =>
    {
        long total = TextRecognitionModels.TotalBytes;

        Assert.Equal("Not downloaded. The download is 32 megabytes.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Missing, 0, total)));
        Assert.Equal("Ready: PaddleOCR PP-OCRv6 small.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Ready, 4, 0)));

        // Outdated names the whole download, whatever the files in the folder happen to be.
        const string outdated = "An update is needed: this version of the app uses new text recognition files. The download is 32 megabytes.";
        Assert.Equal(outdated, TextRecognitionText.Idle(Install(TextRecognitionState.Outdated, 4, 0)));
        Assert.Equal(outdated, TextRecognitionText.Idle(Install(TextRecognitionState.Outdated, 0, total)));

        Assert.Equal("Incomplete: 2 of 4 files are kept; the next download fetches the rest, 21 megabytes.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Incomplete, 2, RecAndDictionary)));
        Assert.Equal("Incomplete: 0 of 4 files are kept; the next download fetches the rest, 32 megabytes.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Incomplete, 0, total)));

        // Every file is here but there is no marker: nothing to fetch, so the pilot is told to check, not to download.
        Assert.Equal("Incomplete: the files are here but not checked. Download text recognition to check them.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Incomplete, 4, 0)));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void Downloading_names_the_percent_and_the_size_of_the_download(string culture) => Under(culture, () =>
    {
        Assert.Equal("Downloading: 45 percent of 32 megabytes.", TextRecognitionText.Downloading(45));
        Assert.Equal("Downloading: 0 percent of 32 megabytes.", TextRecognitionText.Downloading(0));
        Assert.Equal("Downloading: 100 percent of 32 megabytes.", TextRecognitionText.Downloading(100));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void The_end_of_a_job_is_told_on_the_status_line(string culture) => Under(culture, () =>
    {
        Assert.Equal("Ready: PaddleOCR PP-OCRv6 small.", TextRecognitionText.Ready);
        Assert.Equal(TextRecognitionText.Ready, TextRecognitionText.Idle(Install(TextRecognitionState.Ready, 4, 0)));

        Assert.Equal("The last download failed: could not reach GitHub. Download again to retry.",
            TextRecognitionText.Failed("could not reach GitHub"));

        // 21,309,330 bytes still to fetch: the megabytes are decimal and rounded, as everywhere else.
        Assert.Equal("Download cancelled. 2 of 4 files are kept; the next download fetches the rest, 21 megabytes.",
            TextRecognitionText.Cancelled(2, RecAndDictionary));
        Assert.Equal("Download cancelled. 1 of 4 files are kept; the next download fetches the rest, 22 megabytes.",
            TextRecognitionText.Cancelled(1, 21_234_383 + 74_947 + 1_018_508));
        Assert.Equal("Download cancelled.", TextRecognitionText.Cancelled(0, TextRecognitionModels.TotalBytes));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void What_a_job_speaks_is_worded_as_specified(string culture) => Under(culture, () =>
    {
        // The press: the megabytes this job still fetches, 32 on a fresh download.
        Assert.Equal("Downloading text recognition, 32 megabytes.", TextRecognitionText.Started(TextRecognitionModels.TotalBytes));
        Assert.Equal("Downloading text recognition, 21 megabytes.", TextRecognitionText.Started(RecAndDictionary));

        Assert.Equal("Text recognition 25 percent.", TextRecognitionText.Progress(25));
        Assert.Equal("Text recognition 50 percent.", TextRecognitionText.Progress(50));
        Assert.Equal("Text recognition 75 percent.", TextRecognitionText.Progress(75));

        Assert.Equal("Text recognition download stalled.", TextRecognitionText.Stalled);
        Assert.Equal("Download resumed.", TextRecognitionText.Resumed);
        Assert.Equal("Text recognition ready.", TextRecognitionText.ReadySpoken);
        Assert.Equal("Text recognition download failed: a file did not match its checksum.",
            TextRecognitionText.FailedSpoken("a file did not match its checksum"));
        Assert.Equal("Text recognition download cancelled.", TextRecognitionText.CancelledSpoken);
    });

    [Theory, MemberData(nameof(Cultures))]
    public void A_size_is_told_as_one_phrase_with_three_cases(string culture) => Under(culture, () =>
    {
        // Nothing at all, or a size that rounds to a whole number of megabytes other than one: the plural.
        Assert.Equal("0 megabytes", TextRecognitionText.MegabytePhrase(0));
        Assert.Equal("2 megabytes", TextRecognitionText.MegabytePhrase(1_500_000));
        Assert.Equal("21 megabytes", TextRecognitionText.MegabytePhrase(RecAndDictionary));
        Assert.Equal("32 megabytes", TextRecognitionText.MegabytePhrase(TextRecognitionModels.TotalBytes));
        Assert.Equal("100 megabytes", TextRecognitionText.MegabytePhrase(TextRecognitionModels.RequiredFreeBytes));

        // A size that rounds to one: the singular.
        Assert.Equal("1 megabyte", TextRecognitionText.MegabytePhrase(500_000));
        Assert.Equal("1 megabyte", TextRecognitionText.MegabytePhrase(1_018_508)); // the classifier
        Assert.Equal("1 megabyte", TextRecognitionText.MegabytePhrase(1_499_999));

        // Something, but under half a megabyte: never "0 megabytes".
        Assert.Equal("less than 1 megabyte", TextRecognitionText.MegabytePhrase(1));
        Assert.Equal("less than 1 megabyte", TextRecognitionText.MegabytePhrase(74_947)); // the dictionary
        Assert.Equal("less than 1 megabyte", TextRecognitionText.MegabytePhrase(499_999));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void Every_size_the_texts_tell_goes_through_that_phrase(string culture) => Under(culture, () =>
    {
        const long dictionary = 74_947;
        const long classifier = 1_018_508;

        Assert.Equal("Downloading text recognition, less than 1 megabyte.", TextRecognitionText.Started(dictionary));
        Assert.Equal("Downloading text recognition, 1 megabyte.", TextRecognitionText.Started(classifier));

        Assert.Equal("Download cancelled. 3 of 4 files are kept; the next download fetches the rest, less than 1 megabyte.",
            TextRecognitionText.Cancelled(3, dictionary));
        Assert.Equal("Download cancelled. 3 of 4 files are kept; the next download fetches the rest, 1 megabyte.",
            TextRecognitionText.Cancelled(3, classifier));

        Assert.Equal("Incomplete: 3 of 4 files are kept; the next download fetches the rest, less than 1 megabyte.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Incomplete, 3, dictionary)));
        Assert.Equal("Incomplete: 3 of 4 files are kept; the next download fetches the rest, 1 megabyte.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Incomplete, 3, classifier)));

        // The fixed figures of today's list keep their words: the whole download is 32 megabytes, the disk needs 100.
        Assert.Equal("Downloading: 45 percent of 32 megabytes.", TextRecognitionText.Downloading(45));
        Assert.Equal("Not downloaded. The download is 32 megabytes.",
            TextRecognitionText.Idle(Install(TextRecognitionState.Missing, 0, TextRecognitionModels.TotalBytes)));
        Assert.Equal("not enough disk space, 100 megabytes needed",
            TextRecognitionText.FailureReason(new TextRecognitionDiskSpaceException(0)));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void The_reader_and_the_offer_have_their_own_sentences(string culture) => Under(culture, () =>
    {
        Assert.Equal("Text recognition could not start, so the tablet cannot be read. Download it again from Settings, Text recognition.",
            TextRecognitionText.ReaderCannotStart);
        Assert.Equal("The 757 tablet reader needs text recognition: PaddleOCR, a 32 megabyte download. You can also download it later from Settings, Text recognition.",
            TextRecognitionText.OfferStatus);
    });

    [Theory, MemberData(nameof(Cultures))]
    public void FailureReason_names_what_went_wrong(string culture) => Under(culture, () =>
    {
        // The network: no answer, a server error, a stalled or timed-out request.
        Assert.Equal("could not reach GitHub", TextRecognitionText.FailureReason(new HttpRequestException("No such host is known.")));
        Assert.Equal("could not reach GitHub",
            TextRecognitionText.FailureReason(new HttpRequestException("Bad gateway.", null, HttpStatusCode.BadGateway)));
        Assert.Equal("could not reach GitHub", TextRecognitionText.FailureReason(new TaskCanceledException("The operation was canceled.")));

        // An HTTP 404 is told apart from the network: the release is gone, not the connection.
        Assert.Equal("the files were not found on GitHub",
            TextRecognitionText.FailureReason(new HttpRequestException("Not found.", null, HttpStatusCode.NotFound)));

        Assert.Equal("a file did not match its checksum",
            TextRecognitionText.FailureReason(new InvalidDataException("rec.onnx did not match its published checksum.")));
        Assert.Equal("not enough disk space, 100 megabytes needed",
            TextRecognitionText.FailureReason(new TextRecognitionDiskSpaceException(99_999_999)));

        // The disk: a write that failed, or a folder the app may not write to.
        Assert.Equal("the files could not be saved", TextRecognitionText.FailureReason(new IOException("The process cannot access the file.")));
        Assert.Equal("the files could not be saved", TextRecognitionText.FailureReason(new UnauthorizedAccessException("Access to the path is denied.")));

        // A connection that broke while the body was read is an IOException too, but it is the network's fault.
        Assert.Equal("could not reach GitHub",
            TextRecognitionText.FailureReason(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely.")));
        Assert.Equal("could not reach GitHub",
            TextRecognitionText.FailureReason(new IOException("Unable to read data from the transport connection.", new SocketException(10054))));

        // Anything else is passed on as the exception said it, without the full stop the sentence supplies itself.
        Assert.Equal("Something odd happened", TextRecognitionText.FailureReason(new InvalidOperationException("Something odd happened.")));
        Assert.Equal("an unexpected error", TextRecognitionText.FailureReason(new InvalidOperationException("")));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void The_failure_lines_carry_the_reason_without_doubling_the_full_stop(string culture) => Under(culture, () =>
    {
        string reason = TextRecognitionText.FailureReason(new InvalidOperationException("Value cannot be null."));

        Assert.Equal("The last download failed: Value cannot be null. Download again to retry.", TextRecognitionText.Failed(reason));
        Assert.Equal("Text recognition download failed: Value cannot be null.", TextRecognitionText.FailedSpoken(reason));
    });

    [Theory, MemberData(nameof(Cultures))]
    public void The_offer_button_follows_the_job_and_the_install(string culture) => Under(culture, () =>
    {
        (string Text, bool PressStarts) download = ("&Download text recognition", true);
        (string Text, bool PressStarts) running = ("Downloading text recognition", false);
        (string Text, bool PressStarts) ready = ("Text recognition ready", false);

        // Idle: a press downloads, whatever is missing.
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Idle, TextRecognitionState.Missing));
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Idle, TextRecognitionState.Incomplete));
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Idle, TextRecognitionState.Outdated));

        // Running: never dimmed, but a press starts nothing.
        Assert.Equal(running, TextRecognitionText.OfferButton(TextRecognitionPhase.Downloading, TextRecognitionState.Missing));
        Assert.Equal(running, TextRecognitionText.OfferButton(TextRecognitionPhase.Downloading, TextRecognitionState.Incomplete));
        Assert.Equal(running, TextRecognitionText.OfferButton(TextRecognitionPhase.Downloading, TextRecognitionState.Ready));

        // After a failure or a cancel it reads "Download text recognition" again.
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Failed, TextRecognitionState.Missing));
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Failed, TextRecognitionState.Incomplete));
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Cancelled, TextRecognitionState.Incomplete));
        Assert.Equal(download, TextRecognitionText.OfferButton(TextRecognitionPhase.Cancelled, TextRecognitionState.Missing));

        // Installed: the phase or the state saying Ready is enough.
        Assert.Equal(ready, TextRecognitionText.OfferButton(TextRecognitionPhase.Ready, TextRecognitionState.Ready));
        Assert.Equal(ready, TextRecognitionText.OfferButton(TextRecognitionPhase.Idle, TextRecognitionState.Ready));
        Assert.Equal(ready, TextRecognitionText.OfferButton(TextRecognitionPhase.Ready, TextRecognitionState.Incomplete));
        Assert.Equal(ready, TextRecognitionText.OfferButton(TextRecognitionPhase.Failed, TextRecognitionState.Ready));
    });
}
