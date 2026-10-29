using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// A page the tablet window was shown (plan 18, Task 8): the read, and the frame it was read from, which a press compares a fresh
/// capture with before it taps (<see cref="Bbs757EfbFrameCompare.Same"/>). The frame is dropped once newer pages were shown
/// (<see cref="Bbs757EfbPushes.KeptFrames"/>); a press on the page then reads the tablet again before it taps.
/// </summary>
public sealed class Bbs757EfbShownPage
{
    /// <param name="frame">Owned from here on: disposed when the frame is dropped.</param>
    public Bbs757EfbShownPage(Bbs757EfbScreenModel model, Bitmap? frame)
    {
        Model = model;
        Frame = frame;
    }

    public Bbs757EfbScreenModel Model { get; }

    /// <summary>The picture the page was read from; null once dropped (or never kept).</summary>
    public Bitmap? Frame { get; private set; }

    internal void DropFrame()
    {
        Frame?.Dispose();
        Frame = null;
    }
}

/// <summary>
/// The tablet window's recent pushes, and the numbers (aidx) their elements are given (plan 18, Task 8). A press names the element
/// by its number in the push the window showed when the pilot pressed, which can be older than the last push: a read pushed while
/// the press was on its way, or presses queued behind a long one. So an element is numbered by what it is: the page it is on, its
/// identity there (<see cref="Bbs757EfbElements.Build"/>), kept from one push to the next while it stays (the window's list fallback
/// keeps the number it built a control with, the browser patches it), and a new element gets a number never given before. A press
/// then finds the element it was made on, in the newest push that has it, with that push's page: the page Relocate starts from and
/// the frame the fresh capture is compared with (ruling 1). The last <see cref="KeptPushes"/> pushes are kept, and the frames of the
/// last <see cref="KeptFrames"/> pages shown. On the bridge's loop only.
/// </summary>
public sealed class Bbs757EfbPushes : IDisposable
{
    /// <summary>The pushes kept: a press named by an element no kept push has finds nothing and taps nothing.</summary>
    public const int KeptPushes = 32;

    /// <summary>The pages whose frame is kept (about 6 MB each); a press on an older page reads the tablet again first.</summary>
    public const int KeptFrames = 3;

    private readonly LinkedList<(IReadOnlyDictionary<int, Bbs757EfbElements.Action> Actions, Bbs757EfbShownPage? Page)> _pushes = new();
    private readonly LinkedList<Bbs757EfbShownPage> _framed = new();

    /// <summary>The last push's elements' numbers, by page and identity: an element in the next push too keeps its number.</summary>
    private Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
    private int _next = 1;

    /// <summary>A page newly shown, with the frame it was read from (owned from here on); the oldest kept frame is dropped
    /// beyond <see cref="KeptFrames"/>.</summary>
    public Bbs757EfbShownPage Show(Bbs757EfbScreenModel model, Bitmap? frame)
    {
        var page = new Bbs757EfbShownPage(model, frame);
        if (frame != null)
        {
            _framed.AddLast(page);
            while (_framed.Count > KeptFrames)
            {
                _framed.First!.Value.DropFrame();
                _framed.RemoveFirst();
            }
        }
        return page;
    }

    /// <summary>The window's elements for <paramref name="page"/> (null: a status in the page's place), with the alert and the app's
    /// extras, numbered, and kept as the newest push.</summary>
    public Bbs757EfbElements.Built Push(Bbs757EfbShownPage? page, string? alert, IReadOnlyList<Bbs757EfbExtraElement> extras)
    {
        string onPage = (page?.Model.PageName ?? "") + "\n";
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var built = Bbs757EfbElements.Build(page?.Model, alert, extras, identity =>
        {
            string element = onPage + identity;
            if (!_numbers.TryGetValue(element, out int number)) number = _next++;
            numbers[element] = number;
            return number;
        });
        _numbers = numbers;
        _pushes.AddLast((built.Actions, page));
        while (_pushes.Count > KeptPushes) _pushes.RemoveFirst();
        return built;
    }

    /// <summary>The action of the element numbered <paramref name="aidx"/> and the page of the newest push that has it; false when no
    /// kept push has that number, or the element has no action.</summary>
    public bool TryFind(int aidx, out Bbs757EfbElements.Action? action, out Bbs757EfbShownPage? page)
    {
        for (var node = _pushes.Last; node != null; node = node.Previous)
        {
            if (node.Value.Actions.TryGetValue(aidx, out var found))
            {
                action = found;
                page = node.Value.Page;
                return true;
            }
        }
        action = null;
        page = null;
        return false;
    }

    /// <summary>Drops every frame kept. On the loop, once no read runs any more.</summary>
    public void Dispose()
    {
        foreach (var page in _framed) page.DropFrame();
        _framed.Clear();
        _pushes.Clear();
    }
}

/// <summary>
/// What a read of the tablet says by itself (plan 18, Task 8), against the read before it: a question that was not in it, and
/// BlueBird's notifications that were not (<see cref="Bbs757EfbNotices.ToSpeak"/>, quiet for <see cref="Bbs757EfbNotices.QuietAfterPressMs"/>
/// after the pilot's own press, whose outcome says enough). The window speaks one alert at a time, so the two are one text, the
/// question first. A question is the same question when its words are (case, spaces and punctuation ignored): a reread of it is not
/// said again, and one asked again after a read without it is. The first read takes BlueBird's notifications as they are: it did not
/// see them appear. On the bridge's loop only.
/// </summary>
public sealed class Bbs757EfbReadNews
{
    private IReadOnlyList<Bbs757EfbNotice>? _notices;
    private string? _question;

    /// <summary>What to say of <paramref name="read"/>, or null when it says nothing new.</summary>
    public string? After(Bbs757EfbScreenModel read, long msSinceOwnPress)
    {
        string? question = read.Question is { } q && Bbs757EfbPress.QuestionWords(q.Text) != _question ? q.Text : null;
        _question = read.Question is { } now ? Bbs757EfbPress.QuestionWords(now.Text) : null;

        string? notices = _notices == null ? null : Bbs757EfbNotices.ToSpeak(_notices, read.Notices, msSinceOwnPress);
        _notices = read.Notices;

        if (question == null) return notices;
        return notices == null ? question : question + " " + notices;
    }
}
