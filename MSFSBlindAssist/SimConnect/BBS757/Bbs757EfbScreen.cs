using System.Drawing;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>What an element on the tablet is.</summary>
public enum Bbs757EfbKind
{
    /// <summary>Plain text (a read-out, a caption, a note).</summary>
    Text,
    /// <summary>A section heading inside a page (unboxed short capitals above a group).</summary>
    Heading,
    /// <summary>An entry of the page's side menu, or a home-screen app.</summary>
    Menu,
    /// <summary>A boxed control: a button or a tile.</summary>
    Button,
    /// <summary>A two-state switch (a settings row, the checklist's INTERACTIVE).</summary>
    Toggle,
    /// <summary>A slider (a settings row, a cabin light).</summary>
    Slider,
    /// <summary>A checklist line with its box.</summary>
    Checkbox,
    /// <summary>A settings row with neither (a read-only value or a text field).</summary>
    Row,
    /// <summary>A value box: Text is its name, Value its value.</summary>
    Field,
    /// <summary>A tab of a page; Selected is the current one.</summary>
    Tab,
    /// <summary>A long text: Text is the whole text, its lines separated by <c>\n</c>.</summary>
    LongText,
}

/// <summary>The color of a box: on BlueBird's tablet the color IS the state of a tile.</summary>
public enum Bbs757EfbTint { None, Blue, Red, Green, Grey, Other }

/// <summary>
/// A slider's geometry, in tablet pixels: where its track runs and where its knob is. A track runs from <see cref="TrackLeft"/>, its
/// 0 end, to <see cref="TrackRight"/>, its 100 end, and <see cref="KnobX"/> is the knob's place along it: x left to right for a
/// slider wider than tall (every settings slider, the cabin lights, Weight &amp; balance), and y for a <see cref="Vertical"/> one (the
/// hydraulic reservoirs, Systems' zoom), whose 0 end is its bottom (provisional: Task 13 measures the direction) and which is tapped
/// at <see cref="AcrossX"/>, its track's x middle.
/// </summary>
public sealed record Bbs757EfbSlider(int TrackLeft, int TrackRight, int KnobX)
{
    /// <summary>Whether the track runs up and down (the slider's box is taller than wide).</summary>
    public bool Vertical { get; init; }

    /// <summary>A vertical track's x middle, where it is tapped; 0 for a horizontal one.</summary>
    public int AcrossX { get; init; }

    /// <summary>The place along the track of <paramref name="percent"/> (0 to 100) of it.</summary>
    public int At(double percent) => TrackLeft + (int)Math.Round((TrackRight - TrackLeft) * percent / 100.0);

    /// <summary>How far along the track <paramref name="place"/> is, in percent, 0 to 100; 0 for a track of no length.</summary>
    public double PercentOf(int place)
    {
        if (TrackRight == TrackLeft) return 0;
        double percent = 100.0 * (place - TrackLeft) / (TrackRight - TrackLeft);
        return percent <= 0 ? 0 : percent >= 100 ? 100 : percent;   // never -0 (a vertical track's 0 end divides by a negative length)
    }

    private static readonly System.Text.RegularExpressions.Regex Count =
        new(@"^\s*(\d+)\s*/\s*(\d+)\s*$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// The part of its track a slider's shown value says, in percent: a percent ("70%", "40"), or a count of its most ("8/16", Weight
    /// &amp; balance's passengers, as their titles show them); null for any other value ("100 ft", "OFF", nothing) and one past 0 to
    /// 100. Numbers read as another program writes them (CORE-17).
    /// </summary>
    public static double? PercentShown(string value)
    {
        var count = Count.Match(value);
        if (count.Success)
        {
            if (!int.TryParse(count.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
                || !int.TryParse(count.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int most)
                || most <= 0 || n > most) return null;
            return 100.0 * n / most;
        }
        string text = value.Trim().TrimEnd('%').Trim();
        return MSFSBlindAssist.Utils.NumberParsing.TryParseExternal(text, out double v) && double.IsFinite(v) && v is >= 0 and <= 100 ? v : null;
    }
}

/// <summary>
/// One element of the page. <see cref="Hit"/> is where to tap it (tablet pixels, the agent's click
/// coordinates); <see cref="Clickable"/> false means it is not a control (a read-out, a credential).
/// <see cref="Level"/> is a heading's level (2 or 3), 0 otherwise. <see cref="Label"/> is the name the page grammar gave a
/// control whose own words are a state or an action ("NOSE DOOR" for the button "OPEN"); <see cref="Name"/> is BlueBird's
/// own name for the control.
/// <see cref="Key"/> is stable while the element's state changes (its label without
/// the state word), so the window keeps the screen reader's focus on it across a refresh.
/// <see cref="Clip"/> is the drawn rectangle of the scrollbox the control sits in (its nearest <c>*_scrollbox</c> ancestor, within
/// any scrollbox above that one), the part of the tablet it can show in: the reader sets <see cref="Bbs757EfbScreen.ContentArea"/>
/// for a control in no scrollbox, and an item built with none (the default) is read as the content area too
/// (<see cref="Bbs757EfbPress.ClipOf"/>).
/// </summary>
public sealed record Bbs757EfbItem(
    Bbs757EfbKind Kind,
    string Text,
    Rectangle Box,
    Point Hit,
    bool Clickable,
    string Key,
    Bbs757EfbTint Tint = Bbs757EfbTint.None,
    bool? On = null,
    bool Selected = false,
    string Value = "",
    Bbs757EfbSlider? Slider = null,
    int Level = 0,
    string Label = "",
    string Name = "",
    Rectangle Clip = default);

/// <summary>A parsed tablet page: which app and menu entry, then its elements in reading order.</summary>
public sealed record Bbs757EfbScreenModel(
    string App,
    string Section,
    IReadOnlyList<Bbs757EfbItem> Menu,
    IReadOnlyList<Bbs757EfbItem> Items)
{
    /// <summary>Whether the page scrolls: a scrollbox of its content wraps a taller stackbox (its content does not fit the
    /// <see cref="Viewport"/>). The presses scroll a control of that scrollbox into view by dragging.</summary>
    public bool Scrollable { get; init; }

    /// <summary>The tablet's top bar, left to right: the departure countdown, the boarding control and the
    /// notification control, keyed <c>top:dep</c>, <c>top:boarding</c> and <c>top:notifications</c> whatever their
    /// words (see <see cref="Bbs757EfbTopBar"/> for the words). Empty when none shows.</summary>
    public IReadOnlyList<Bbs757EfbItem> TopBar { get; init; } = Array.Empty<Bbs757EfbItem>();

    /// <summary>BlueBird's notification panel's entries, top first (<see cref="Bbs757EfbNotice"/>); empty when it does
    /// not show.</summary>
    public IReadOnlyList<Bbs757EfbNotice> Notices { get; init; } = Array.Empty<Bbs757EfbNotice>();

    /// <summary>A question the tablet shows over the page, with its answers (not in <see cref="Items"/>); null when none.</summary>
    public Bbs757EfbQuestion? Question { get; init; }

    /// <summary>The part of the tablet the page scrolls in, in tablet pixels: the drawn rectangle of the one scrollbox that scrolls
    /// (<see cref="Scrollable"/>), else the content area. Each control is shown, and tapped, within its own <see cref="Bbs757EfbItem.Clip"/>;
    /// only a control whose clip is this rectangle is dragged into view.</summary>
    public Rectangle Viewport { get; init; } = Bbs757EfbScreen.ContentArea;

    /// <summary>Where BlueBird's QWERTY keyboard keys are, null when the reader found none. BlueBird's keyboard tree reads
    /// visible whether it shows or not (measured 2026-11-06), so this is only where the keys are, never "the keyboard is up".</summary>
    public Bbs757EfbKeyboard? Keyboard { get; init; }

    /// <summary>"Aircraft, Ground services" / "Home".</summary>
    public string PageName =>
        App.Length == 0 ? "Unknown page"
        : Section.Length == 0 ? Bbs757EfbVocabulary.Pretty(App)
        : Bbs757EfbVocabulary.Pretty(App) + ", " + Bbs757EfbVocabulary.Pretty(Section);
}

/// <summary>One entry of BlueBird's notification panel: its words as the tablet writes them, made readable ("GPU
/// connected."), and its time ("14:17Z"), null when the time is not known.</summary>
public sealed record Bbs757EfbNotice(string Text, string? Time);

/// <summary>BlueBird's keyboard: the box it covers and its keys.</summary>
public sealed record Bbs757EfbKeyboard(Rectangle Box, IReadOnlyList<Bbs757EfbKey> Keys);

/// <summary>One key: BlueBird's name (<c>char_btn_q|Q|1</c> is Lower "q", Upper "Q", Third "1"; a command key such as
/// <c>btn_enter</c> has its name and empty layers) and where to tap it.</summary>
public sealed record Bbs757EfbKey(string Name, string Lower, string Upper, string Third, Point Hit);

/// <summary>A box the tablet shows over its page with words and one or two answers (YES, NO, OK, CANCEL): the words,
/// then the answers as buttons, left to right.</summary>
public sealed record Bbs757EfbQuestion(string Text, IReadOnlyList<Bbs757EfbItem> Buttons);

/// <summary>
/// The constants and the key/state-word helpers the memory tablet reader (<see cref="Bbs757EfbModelReader"/>) and the
/// window share: the side-menu boundary, the window's own Home button, and how a tile's key carries
/// its words. (The EFB is read from the module's memory, so only these shared pieces live here.)
/// </summary>
public static class Bbs757EfbScreen
{
    /// <summary>The part of the tablet that shows a page (right of the side menu, below the top bar).</summary>
    public static readonly Rectangle ContentArea = new(250, 82, 1147, 966);

    /// <summary>Controls left of this (tablet x) are the side menu.</summary>
    public const int MenuRight = 252;

    /// <summary>The home icon left of every app's title: the window's own Home button taps it, as the reader offers
    /// no control for the icon.</summary>
    public static readonly Point HomeButton = new(19, 62);

    /// <summary>The words a tile's text ends in that say its state, not its name ("FUEL TRUCK OFF" is FUEL TRUCK): the
    /// key strips them so a press that changes a tile's state keeps its key, and <see cref="Bbs757EfbPress.Relocate"/>
    /// leaves them out when it compares a tile's words with another's. "IN PLACE" is the chocks tile's other state (the picture's, joined to
    /// the tile's words by <see cref="Bbs757EfbPageRules.PictureState"/>).</summary>
    internal static readonly string[] StateWords = { "OFF", "ON", "REMOVED", "CONNECTED", "OPEN", "CLOSED", "ATTACHED", "UNAVAIL", "IN PLACE" };

    /// <summary>The words that make a control's own words an action, not its name (plan 20, R5): such a control is named by a label
    /// (<see cref="Bbs757EfbItem.Label"/>), as one whose words are a state word is.</summary>
    internal static readonly string[] ActionWords = { "OPEN", "CLOSE", "CLOSED", "LOGOUT", "UPDATE", "REMOVE", "RESET", "FAIL NOW", "SET UP", "REPAIR", "REPAIR ALTN" };

    /// <summary>The words a box's key carries, between its caption and its place ("btn:LATEST CYCLE|UPDATE@59,14" is UPDATE,
    /// "btn:|FUEL TRUCK@56,28" is FUEL TRUCK): the box's own words without their state word, never the caption or the row
    /// label the reader may name the box by; "" for a key that is no box's (a switch, the top bar, a menu entry).</summary>
    internal static string KeyWords(string key) =>
        key.StartsWith("btn:", StringComparison.Ordinal) ? key.Substring(4).Split('|').Last().Split('@')[0] : "";
}
