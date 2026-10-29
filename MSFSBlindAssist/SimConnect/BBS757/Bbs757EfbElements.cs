using System.Drawing;
using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// An element the app adds to the window on top of the tablet's own (plan 18): a button of the app's, such as the Navigraph sign-in,
/// or a line of text, such as the code to enter at navigraph.com. The bridge decides what is offered on which page.
/// <see cref="Key"/> is stable while the words change, so the window keeps the screen reader's focus on the element, and it names the
/// element when it is pressed (<see cref="Bbs757EfbElements.Action.ExtraKey"/>).
/// </summary>
public sealed record Bbs757EfbExtraElement(string Key, string Text, bool IsButton);

/// <summary>
/// A read tablet page (<see cref="Bbs757EfbScreenModel"/>) as the shared EFB window's elements, and what pressing each does. Pure:
/// tested. The window (FbwEfbForm) renders headings, buttons, check boxes, choice lists and sliders from the push's "items.N.*" keys
/// (the PMDG client's format) and posts back click_display_element / set_element_value with the element's aidx.
///
/// Every control the reader offers can be pressed (the owner's ruling of 2026-10-29): a button, tile, switch, check box, slider and tab
/// is clickable whatever the reader's <see cref="Bbs757EfbItem.Clickable"/> says, and nothing here refuses a press. The one exception
/// is the tablet's own: while it shows a question over its page, the page is dimmed and takes no tap, so its controls are text until the
/// question is answered. The question's answers press, and so does the top bar, which stays lit above the dimming.
///
/// The order the pilot meets: the alert; the tablet's question (its words as text, then its answers); the top bar; BlueBird's
/// notifications under a heading; Home and the side menu; the page; the app's extras; the scroll buttons; and "More" with Read the
/// tablet again. The window speaks only its first alert and remembers one text, so the question is no alert here: the bridge says a
/// question once, through its own alert, when a read first shows it (plan 18, Task 8), and the page keeps its words first.
/// </summary>
public static class Bbs757EfbElements
{
    /// <summary>What pressing an element does.</summary>
    public enum ActionKind { Tap, Refresh, ScrollDown, ScrollUp, Extra }

    /// <summary>One element's action: the tablet control it taps (Tap, and the control a set changes), or the key of the app's extra it
    /// stands for (Extra).</summary>
    public sealed record Action(ActionKind Kind, Bbs757EfbItem? Item = null, string? ExtraKey = null);

    /// <summary>A build: the push's data and each aidx's action.</summary>
    public sealed record Built(Dictionary<string, string> Data, IReadOnlyDictionary<int, Action> Actions, int Count);

    public const string ReadAgainText = "Read the tablet again";

    private const string NotificationsHeading = "Notifications";
    private const char OptionSeparator = (char)0x1f;

    /// <param name="model">The page read, or null when none is shown (a status in its place).</param>
    /// <param name="alert">A word the window speaks at once and shows first (a status, or the outcome of a press); null for none.</param>
    /// <param name="extras">What the app adds to the page, in order (the bridge decides, by page).</param>
    /// <param name="numberFor">The number (aidx) of an element, from its identity on the page: its key, else its words, and which of
    /// the page's elements with that identity it is ("btn:HOME#1"); a question's answer also carries its question's words
    /// (<see cref="Bbs757EfbPress.QuestionWords"/>), as every question's answers are keyed alike. The bridge keeps an element's number
    /// while the element stays and never gives one number to two elements (<see cref="Bbs757EfbPushes"/>), so a press the window
    /// sends names the element it was made on, even after a later push. Null numbers the elements 1, 2, 3 and so on.</param>
    public static Built Build(Bbs757EfbScreenModel? model, string? alert, IReadOnlyList<Bbs757EfbExtraElement> extras,
        Func<string, int>? numberFor = null)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["page"] = model?.PageName ?? "BlueBird tablet" };
        var actions = new Dictionary<int, Action>();
        var identities = new Dictionary<string, int>(StringComparer.Ordinal);
        int n = 0;

        void Add(string text, string kind = "", string controlType = "", bool clickable = false, int level = 0,
            string value = "", string key = "", bool announceChange = false, IReadOnlyList<string>? options = null,
            string live = "", double? min = null, double? max = null, double? step = null, Action? action = null,
            string? of = null)
        {
            int i = n++;
            string identity = (key.Length > 0 ? key : "text|" + text) + (of != null ? "|" + of : "");
            int occurrence = identities[identity] = identities.GetValueOrDefault(identity) + 1;
            int aidx = numberFor?.Invoke(identity + "#" + occurrence.ToString(CultureInfo.InvariantCulture)) ?? i + 1;
            data[$"items.{i}.aidx"] = aidx.ToString(CultureInfo.InvariantCulture);
            data[$"items.{i}.text"] = text;
            data[$"items.{i}.tag"] = "";
            data[$"items.{i}.role"] = "";
            data[$"items.{i}.value"] = value;
            data[$"items.{i}.type"] = controlType;
            data[$"items.{i}.clickable"] = clickable ? "true" : "false";
            data[$"items.{i}.kind"] = kind;
            data[$"items.{i}.level"] = level.ToString(CultureInfo.InvariantCulture);
            data[$"items.{i}.live"] = live;
            data[$"items.{i}.disabled"] = "false";
            if (announceChange) data[$"items.{i}.announceChange"] = "true";
            if (key.Length > 0) data[$"items.{i}.key"] = key;
            if (options != null) data[$"items.{i}.options"] = string.Join(OptionSeparator, options);
            if (min is double mn) data[$"items.{i}.min"] = mn.ToString(CultureInfo.InvariantCulture);
            if (max is double mx) data[$"items.{i}.max"] = mx.ToString(CultureInfo.InvariantCulture);
            if (step is double sp) data[$"items.{i}.step"] = sp.ToString(CultureInfo.InvariantCulture);
            if (action != null) actions[aidx] = action;
        }

        // The page name is the window's own title line and page announcement ("page" above), so it is
        // not repeated as a list item.
        if (alert != null) Add(alert, kind: "alert", live: "assertive", key: "alert");

        if (model != null)
        {
            // Under a question the tablet dims its page and takes no tap on it.
            bool dimmed = model.Question != null;
            if (model.Question is { } question)
            {
                // Text: an alert here would be said again whenever another alert went (see the class summary).
                Add(question.Text, key: "question");
                // Each answer is its question's: the next question's YES is another element, under the same key.
                string asked = Bbs757EfbPress.QuestionWords(question.Text);
                foreach (var answer in question.Buttons)
                    Add(answer.Text, kind: "button", clickable: true, key: answer.Key, action: new Action(ActionKind.Tap, answer), of: asked);
            }

            foreach (var barItem in model.TopBar)
                AddTopBar(barItem);

            if (model.Notices.Count > 0)
            {
                Add(NotificationsHeading, level: 2, key: "h:notifications");
                foreach (var notice in model.Notices)
                    Add(NoticeText(notice));
            }

            if (model.App != "HOME")
            {
                if (dimmed)
                    Add("Home", key: "btn:HOME");
                else
                {
                    var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:HOME");
                    Add("Home", kind: "button", clickable: true, key: "btn:HOME", action: new Action(ActionKind.Tap, home));
                }
            }
            foreach (var entry in model.Menu)
                AddItem(entry, model.PageName, dimmed);
            foreach (var it in model.Items)
                AddItem(it, model.PageName, dimmed);
        }

        foreach (var extra in extras)
        {
            if (extra.IsButton)
                Add(extra.Text, kind: "button", clickable: true, key: extra.Key, action: new Action(ActionKind.Extra, ExtraKey: extra.Key));
            else
                Add(extra.Text, key: extra.Key);
        }

        if (model?.Scrollable == true)
        {
            Add("Scroll down", kind: "button", clickable: true, key: "btn:scroll-down", action: new Action(ActionKind.ScrollDown));
            Add("Scroll up", kind: "button", clickable: true, key: "btn:scroll-up", action: new Action(ActionKind.ScrollUp));
        }
        Add("More", level: 2, key: "h:more");
        Add(ReadAgainText, kind: "button", clickable: true, key: "btn:refresh", action: new Action(ActionKind.Refresh));
        return new Built(data, actions, n);

        // The top bar stays lit above a question's dimming: its buttons press whatever the page does.
        void AddTopBar(Bbs757EfbItem it)
        {
            if (it.Kind != Bbs757EfbKind.Button)
            {
                Add(it.Text, key: it.Key);
                return;
            }
            // The notification button's words say whether BlueBird's list is open, and change when it is pressed.
            Add(it.Text, kind: "button", clickable: true, key: it.Key,
                announceChange: it.Key == Bbs757EfbTopBar.NotificationsKey || CarriesState(it), action: new Action(ActionKind.Tap, it));
        }

        void AddItem(Bbs757EfbItem it, string pageName, bool dimmed)
        {
            if (dimmed && AddDimmed(it, pageName)) return;
            switch (it.Kind)
            {
                case Bbs757EfbKind.Heading:
                    Add(it.Text, level: 2, key: it.Key);
                    break;
                case Bbs757EfbKind.Text:
                    Add(it.Text, key: it.Key);
                    break;
                case Bbs757EfbKind.Row:
                    Add(it.Value.Length > 0 ? it.Text + ": " + it.Value : it.Text, key: it.Key);
                    break;
                case Bbs757EfbKind.Menu:
                    Add(MenuText(it), kind: "button", clickable: true, key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Button:
                    Add(it.Text, kind: "button", clickable: true, key: it.Key, announceChange: CarriesState(it),
                        action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Toggle:
                {
                    string word = it.Value;
                    bool plain = word.Length == 0 || IsOnOff(word);
                    Add(plain ? it.Text : it.Text + ": " + word, controlType: "checkbox", clickable: true,
                        value: it.On == true ? "true" : "false", key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                }
                case Bbs757EfbKind.Checkbox:
                    Add(it.Text, controlType: "checkbox", clickable: true, value: it.On == true ? "true" : "false",
                        key: it.Key, action: new Action(ActionKind.Tap, it));
                    break;
                case Bbs757EfbKind.Slider:
                {
                    var options = OptionsFor(it, pageName);
                    int at = options == null ? -1 : CurrentChoice(it, pageName, options);
                    if (options != null && at >= 0)
                        Add(it.Text, controlType: "select", clickable: true, value: options[at], key: it.Key, options: options,
                            action: new Action(ActionKind.Tap, it));
                    else if (it.Slider is { } sl)
                    {
                        // No known choices (the cabin lights), or the one set was not read: a 0-100 slider from the knob's place.
                        string label = it.Value.Length > 0 ? it.Text + " (" + it.Value + ")" : it.Text;
                        Add(label, controlType: "range", clickable: true, value: KnobPercent(sl).ToString(CultureInfo.InvariantCulture),
                            key: it.Key, min: 0, max: 100, step: 10, action: new Action(ActionKind.Tap, it));
                    }
                    break;
                }
            }
        }

        // A control as text, for the page behind a question: its words and the state read for it. False for the kinds that render the
        // same either way (headings, text, rows).
        bool AddDimmed(Bbs757EfbItem it, string pageName)
        {
            switch (it.Kind)
            {
                case Bbs757EfbKind.Menu:
                    Add(MenuText(it), key: it.Key);
                    return true;
                case Bbs757EfbKind.Button:
                    Add(it.Text, key: it.Key);
                    return true;
                case Bbs757EfbKind.Toggle:
                case Bbs757EfbKind.Checkbox:
                    Add(it.Text + ": " + StateWord(it), key: it.Key);
                    return true;
                case Bbs757EfbKind.Slider:
                    Add(SliderStateText(it, pageName), key: it.Key);
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>A side-menu entry's words, with the page it is marked as the current one (a tab already says "(selected)").</summary>
    private static string MenuText(Bbs757EfbItem it) =>
        it.Text + (it.Selected && !it.Text.EndsWith("(selected)", StringComparison.Ordinal) ? " (current page)" : "");

    /// <summary>Whether a button or tile's words carry its state, so the window says them again when a press changes them: a red or
    /// green tile (on or off), or words with a colon ("GND SRVC Mode: BBS", "Notifications: 2").</summary>
    private static bool CarriesState(Bbs757EfbItem it) =>
        it.Tint is Bbs757EfbTint.Red or Bbs757EfbTint.Green || it.Text.Contains(':');

    /// <summary>A notification as a line of text: the time the tablet shows over its words, when it was read, then the words.</summary>
    private static string NoticeText(Bbs757EfbNotice notice) =>
        notice.Time is { Length: > 0 } time ? time + " " + notice.Text : notice.Text;

    private static bool IsOnOff(string word) =>
        word.Equals("On", StringComparison.OrdinalIgnoreCase) || word.Equals("Off", StringComparison.OrdinalIgnoreCase);

    private static string StateWord(Bbs757EfbItem it) =>
        it.Value.Length > 0 && !IsOnOff(it.Value) ? it.Value : it.On switch { true => "On", false => "Off", _ => "state not read" };

    private static string SliderStateText(Bbs757EfbItem it, string pageName)
    {
        string value = it.Value;
        var options = OptionsFor(it, pageName);
        int at = options == null ? -1 : CurrentChoice(it, pageName, options);
        if (options != null && at >= 0)
            value = options[at];
        else if (value.Length == 0 && it.Slider is { } slider && slider.TrackRight > slider.TrackLeft)
            value = KnobPercent(slider).ToString(CultureInfo.InvariantCulture) + " percent";
        return value.Length > 0 ? it.Text + ": " + value : it.Text;
    }

    /// <summary>Where a slider's knob is along its track, in whole percent.</summary>
    private static double KnobPercent(Bbs757EfbSlider slider)
    {
        double percent = slider.TrackRight > slider.TrackLeft
            ? Math.Round(100.0 * (slider.KnobX - slider.TrackLeft) / (slider.TrackRight - slider.TrackLeft))
            : 0;
        return Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// A slider's choices, left to right, as the tablet offers them; null when the slider is a number. A row of the Settings pages that is
    /// one of BlueBird's settings (<see cref="Bbs757EfbSettingsMatch.SettingFor"/>, by the page and the row's label) offers the settings
    /// catalog's words in its order, which is the tablet's from left to right; a setting with no choices (a continuous slider) offers
    /// none. A row that matches no setting (a label OCR misread, a slider on another page) takes
    /// <see cref="Bbs757EfbVocabulary.SliderOptions"/> by its key, and is otherwise a number.
    /// </summary>
    public static IReadOnlyList<string>? OptionsFor(Bbs757EfbItem it, string pageName)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(pageName, it) is { } setting)
            return Bbs757EfbSettingsMatch.ChoicesFor(setting);
        if (!it.Key.StartsWith("sld:", StringComparison.Ordinal)) return null;
        return Bbs757EfbVocabulary.SliderOptions.TryGetValue(it.Key.Substring(4), out var options) ? options : null;
    }

    /// <summary>
    /// The index among <paramref name="options"/> (the list <see cref="OptionsFor"/> gave for the same row) of the choice the row reads as;
    /// -1 when the reader gave none or it is none of them. For a catalog setting the reader's words are matched with case, spaces and
    /// punctuation ignored ("FULL(7 MIN+)" is "Full (7 min+)": <see cref="Bbs757EfbSettingsMatch.ChoiceIndex"/>); for any other slider,
    /// with case ignored.
    /// </summary>
    public static int CurrentChoice(Bbs757EfbItem it, string pageName, IReadOnlyList<string> options)
    {
        if (Bbs757EfbSettingsMatch.SettingFor(pageName, it) is { } setting)
            return Bbs757EfbSettingsMatch.ChoiceIndex(setting, it.Value);
        for (int index = 0; index < options.Count; index++)
            if (string.Equals(options[index], it.Value, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
    }
}
