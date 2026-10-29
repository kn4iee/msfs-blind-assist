using MSFSBlindAssist.Services.TextRecognition;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The tablet reader's hold on the text recognition engine as the bridge drives it (OCR-3): a load that fails is
/// reported once and said as the reader's status, said again when a later status took its place, and tried again
/// when the pilot asks for the reader with the models Ready. The bridge's loop never runs here: each test plays the
/// loop's part, calling what its turn calls, on a bridge made with no synchronization context, so its pushes are
/// raised inline. Every load fails: one that worked would go on to capture the simulator's pop-out window.
/// </summary>
public sealed class Bbs757EfbBridgeTextRecognitionTests
{
    private const string Unreachable = "The BlueBird tablet cannot be reached. Is the 757 loaded?";

    [Fact]
    public async Task A_failed_load_is_said_once_and_tried_again_only_when_the_loop_takes_a_retry()
    {
        using var h = new Harness();

        Assert.False(await h.Bridge.ReadIfChangedAsync(CancellationToken.None));
        Assert.Equal(1, h.Attempts);
        Assert.Single(h.Reported);
        Assert.Equal(TextRecognitionText.ReaderCannotStart, h.Shown);
        Assert.Single(h.Pushes);

        // Every read after it: no load, no report, nothing pushed again (F5 and Read the tablet again included).
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None, force: true);
        Assert.Equal(1, h.Attempts);
        Assert.Single(h.Pushes);

        // The pilot pressed Read the tablet screen with the models Ready again. The UI thread's call only sets a flag
        // (the slot is the loop's alone), so a read before the loop's next turn still loads nothing.
        h.Bridge.RetryTextRecognition();
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None, force: true);
        Assert.Equal(1, h.Attempts);

        // The loop's turn takes the flag: the next read loads again, and a load that fails again is reported and said again.
        h.Bridge.TakeTextRecognitionRetry();
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None);
        Assert.Equal(2, h.Attempts);
        Assert.Equal(2, h.Reported.Count);
        Assert.Equal(TextRecognitionText.ReaderCannotStart, h.Shown);
        Assert.Equal(2, h.Pushes.Count);

        // A retry is taken once: the turns after it load nothing until the pilot asks again.
        h.Bridge.TakeTextRecognitionRetry();
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None);
        Assert.Equal(2, h.Attempts);
        Assert.Equal(2, h.Reported.Count);
    }

    [Fact]
    public async Task A_later_status_never_takes_the_place_of_an_engine_that_cannot_start()
    {
        using var h = new Harness();
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None);
        Assert.Equal(TextRecognitionText.ReaderCannotStart, h.Shown);

        // The tablet could not be reached for a while (the loop's mirror check): its status takes the window's place.
        h.Bridge.Status(Unreachable);
        Assert.Equal(Unreachable, h.Shown);

        // Reachable again: the next read says the real cause again, with no second report and no second load.
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None);
        Assert.Equal(TextRecognitionText.ReaderCannotStart, h.Shown);
        Assert.Equal(3, h.Pushes.Count);
        Assert.Single(h.Reported);
        Assert.Equal(1, h.Attempts);

        // Said once: the reads after it push nothing.
        await h.Bridge.ReadIfChangedAsync(CancellationToken.None, force: true);
        Assert.Equal(3, h.Pushes.Count);
    }

    /// <summary>The window's alert in a push (its first element), or null when it has none.</summary>
    private static string? AlertOf(Dictionary<string, string> data)
        => data.TryGetValue("items.0.kind", out string? kind) && kind == "alert" ? data["items.0.text"] : null;

    /// <summary>A bridge whose engine never loads, its loop never started: each attempt counted, each report kept,
    /// each push recorded.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly CoherentBbs757EfbClient _client = new(() => "");

        public Harness()
        {
            // The bridge posts its pushes to the context it was made on: made with none, it raises them inline.
            SynchronizationContext? saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                Bridge = new Bbs757EfbBridge(_client, new Bbs757EfbBridgeIo
                {
                    ReadCamera = () => Task.FromResult<(int type, int index)?>(null),
                    SetCamera = (_, _) => { },
                    OnGround = _ => Task.FromResult<bool?>(null),
                    ReadRegistered = (_, _) => Task.FromResult<double?>(null),
                    OpenClassicWindow = () => { },
                    ArmServiceEcho = _ => { },
                    SettleServiceEcho = (_, _) => { },
                    ArmGroundPowerEcho = () => { },
                    SettleGroundPowerEcho = _ => { },
                    LoadOcr = () =>
                    {
                        Attempts++;
                        throw new FileNotFoundException("Detector model file does not exist");
                    },
                    OcrFailed = Reported.Add,
                });
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(saved);
            }
            Bridge.StateUpdated += (_, e) => Pushes.Add(e.Data);
        }

        public Bbs757EfbBridge Bridge { get; }

        public int Attempts { get; private set; }

        public List<Exception> Reported { get; } = new();

        public List<Dictionary<string, string>> Pushes { get; } = new();

        /// <summary>The alert the window was last given, or null when the last push had none or none came.</summary>
        public string? Shown => Pushes.Count == 0 ? null : AlertOf(Pushes[^1]);

        public void Dispose()
        {
            Bridge.Dispose();
            _client.Dispose();
        }
    }
}
