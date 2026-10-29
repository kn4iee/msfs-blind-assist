namespace MSFSBlindAssist.Services.TextRecognition;

/// <summary>
/// Holds the text recognition engine for the 757 tablet reader (<c>Bbs757EfbBridge</c>): loads it on first use,
/// reports a load that failed once and does not try again until <see cref="Release"/>, and disposes it on
/// <see cref="Release"/>. The reader releases it when its window hides, so the 32 megabytes of models are held
/// only while the tablet is read, and a failed load is tried again only after the window was hidden and shown.
///
/// <para>Not thread-safe by design: one thread owns the slot, the bridge's loop, which also runs every read. So a
/// release from that thread can never come while a read is using the engine.</para>
/// </summary>
internal sealed class TextRecognitionEngineSlot<T> : IDisposable where T : class, IDisposable
{
    private readonly Func<T> _load;
    private readonly Action<Exception> _failed;
    private T? _engine;
    private bool _loadFailed;

    /// <param name="load">Loads the engine; it may throw (the models missing or damaged).</param>
    /// <param name="failed">Told once what a load threw; the slot then stays empty until <see cref="Release"/>.</param>
    public TextRecognitionEngineSlot(Func<T> load, Action<Exception> failed)
    {
        _load = load;
        _failed = failed;
    }

    /// <summary>
    /// The engine, loaded on the first call. A load that throws is reported to <c>failed</c> once, and every call
    /// after it returns null without loading again until <see cref="Release"/>.
    /// </summary>
    public T? Get()
    {
        if (_engine != null) return _engine;
        if (_loadFailed) return null;
        try
        {
            _engine = _load();
            return _engine;
        }
        catch (Exception ex)
        {
            // Marked before the report, so a report that throws still never brings a second attempt.
            _loadFailed = true;
            _failed(ex);
            return null;
        }
    }

    /// <summary>Disposes a loaded engine and forgets a failed load: the next <see cref="Get"/> loads again.</summary>
    public void Release()
    {
        T? engine = _engine;
        _engine = null;
        _loadFailed = false;
        engine?.Dispose();
    }

    /// <summary>The same as <see cref="Release"/>.</summary>
    public void Dispose() => Release();
}
