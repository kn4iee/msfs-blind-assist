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

/// <summary>A slider's geometry: where its track runs and where its knob is, in tablet pixels.</summary>
public sealed record Bbs757EfbSlider(int TrackLeft, int TrackRight, int KnobX);

/// <summary>
/// One element of the page. <see cref="Hit"/> is where to tap it (tablet pixels, the agent's click
/// coordinates); <see cref="Clickable"/> false means it is not a control (a read-out, a credential).
/// <see cref="Level"/> is a heading's level (2 or 3), 0 otherwise. <see cref="Label"/> is the name the page grammar gave a
/// control whose own words are a state or an action ("NOSE DOOR" for the button "OPEN"); <see cref="Name"/> is BlueBird's
/// own name for the control.
/// <see cref="Key"/> is stable while the element's state changes (its label without
/// the state word), so the window keeps the screen reader's focus on it across a refresh.
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
    string Name = "");

/// <summary>A parsed tablet page: which app and menu entry, then its elements in reading order.</summary>
public sealed record Bbs757EfbScreenModel(
    string App,
    string Section,
    IReadOnlyList<Bbs757EfbItem> Menu,
    IReadOnlyList<Bbs757EfbItem> Items)
{
    /// <summary>Whether the page scrolls (its content does not fit the viewport): the window then offers scroll
    /// buttons (the agent's wheel).</summary>
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

    /// <summary>The part of the tablet that shows the page, in tablet pixels; a control outside it is not on screen.</summary>
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
/// window share: the side-menu boundary, the window's own Home button and scroll wheel, and how a tile's key carries
/// its words. (This was the OCR screen parser; the EFB is now read from the module's memory, so only these shared
/// pieces remain.)
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

    /// <summary>Where the window's scroll buttons send the agent's wheel, and how far each step scrolls.</summary>
    public static readonly Point WheelPoint = new(800, 600);
    public const int WheelStep = 300;

    /// <summary>The words a tile's text ends in that say its state, not its name ("FUEL TRUCK OFF" is FUEL TRUCK): the
    /// key strips them so a press that changes a tile's state keeps its key, and <see cref="Bbs757EfbPress.Relocate"/>
    /// leaves them out when it compares a tile's words with another's.</summary>
    internal static readonly string[] StateWords = { "OFF", "ON", "REMOVED", "CONNECTED", "OPEN", "CLOSED", "ATTACHED", "UNAVAIL" };

    /// <summary>The words a box's key carries, between its caption and its place ("btn:LATEST CYCLE|UPDATE@59,14" is UPDATE,
    /// "btn:|FUEL TRUCK@56,28" is FUEL TRUCK): the box's own words without their state word, never the caption or the row
    /// label the reader may name the box by; "" for a key that is no box's (a switch, the top bar, a menu entry).</summary>
    internal static string KeyWords(string key) =>
        key.StartsWith("btn:", StringComparison.Ordinal) ? key.Substring(4).Split('|').Last().Split('@')[0] : "";
}
