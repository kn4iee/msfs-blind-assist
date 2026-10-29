using MSFSBlindAssist.Services.TextRecognition;

namespace MSFSBlindAssist.Tests.TextRecognition;

/// <summary>
/// The 757 tablet reader's hold on the text recognition engine: loaded on first use, a failed load reported once
/// and not tried again until the window was hidden (released), and the engine disposed on release. A fake engine
/// stands in for PaddleOCR, so no models are needed.
/// </summary>
public sealed class TextRecognitionEngineSlotTests
{
    /// <summary>An engine that counts how often it was disposed.</summary>
    private sealed class FakeEngine : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }

    private static Action<Exception> NeverFails => ex => throw new Xunit.Sdk.XunitException("No load fails in this test: " + ex.Message);

    [Fact]
    public void Get_loads_once()
    {
        int loads = 0;
        using var slot = new TextRecognitionEngineSlot<FakeEngine>(() => { loads++; return new FakeEngine(); }, NeverFails);

        FakeEngine? first = slot.Get();
        FakeEngine? second = slot.Get();

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, loads);
        Assert.Equal(0, first!.Disposals);
    }

    [Fact]
    public void A_failed_load_is_reported_once_until_released()
    {
        int attempts = 0;
        var reported = new List<Exception>();
        var cause = new FileNotFoundException("Detector model file does not exist");
        using var slot = new TextRecognitionEngineSlot<FakeEngine>(() => { attempts++; throw cause; }, reported.Add);

        Assert.Null(slot.Get());
        Assert.Null(slot.Get());

        // One attempt, one report, with what the load threw: the reader does not retry at every capture.
        Assert.Equal(1, attempts);
        Assert.Same(cause, Assert.Single(reported));

        // Released (the window was hidden): the next Get tries again, and a second failure is reported again.
        slot.Release();
        Assert.Null(slot.Get());
        Assert.Equal(2, attempts);
        Assert.Equal(2, reported.Count);
    }

    [Fact]
    public void Release_disposes_and_the_next_Get_loads_again()
    {
        var loaded = new List<FakeEngine>();
        using var slot = new TextRecognitionEngineSlot<FakeEngine>(() => { var engine = new FakeEngine(); loaded.Add(engine); return engine; }, NeverFails);

        FakeEngine first = slot.Get()!;
        slot.Release();
        Assert.Equal(1, first.Disposals);

        FakeEngine? second = slot.Get();
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, loaded.Count);
        Assert.Equal(0, second!.Disposals);

        // A release with nothing loaded disposes nothing: each engine is disposed exactly once.
        slot.Release();
        slot.Release();
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
    }

    [Fact]
    public void Dispose_releases()
    {
        var engine = new FakeEngine();
        var slot = new TextRecognitionEngineSlot<FakeEngine>(() => engine, NeverFails);
        Assert.Same(engine, slot.Get());

        slot.Dispose();

        Assert.Equal(1, engine.Disposals);
    }
}
