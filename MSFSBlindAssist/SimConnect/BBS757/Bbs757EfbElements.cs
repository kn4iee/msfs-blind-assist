using System.Drawing;
using System.Globalization;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// A read tablet page (<see cref="Bbs757EfbScreenModel"/>) as the shared EFB window's elements, and what
/// pressing each does. Pure: tested. The window (FbwEfbForm) renders headings, buttons, check boxes,
/// choice lists and sliders from the push's "items.N.*" keys (the PMDG client's format) and posts back
/// click_display_element / set_element_value with the element's aidx.
/// </summary>
public static class Bbs757EfbElements
{
    /// <summary>What pressing an element does.</summary>
    public enum ActionKind { Tap, Classic, Refresh, Refuse, ScrollDown, ScrollUp }

    /// <summary>One element's action: the tablet item it taps (Tap, and the item a set changes), or the
    /// refusal said instead.</summary>
    public sealed record Action(ActionKind Kind, Bbs757EfbItem? Item = null, string? Refusal = null);

    /// <summary>A build: the push's data and each aidx's action.</summary>
    public sealed record Built(Dictionary<string, string> Data, IReadOnlyDictionary<int, Action> Actions, int Count);

    public const string ClassicWindowText =
        "Classic 757 EFB window: SimBrief load, Navigraph sign-in, nav data update, steering and ground power";
    public const string ReadAgainText = "Read the tablet again";
    public const string ReadOnlyNote =
        "This window only reads the tablet. To change anything, use the classic 757 EFB window.";
    public const string ClassicWindowReadOnlyText = "Classic 757 EFB window";
    public const string ReadOnlyRefusal = "This window only reads the tablet. Use the classic 757 EFB window to change it.";
    public const string SaveLoadRefusal =
        "Custom panel states are not offered here: BlueBird keeps SAVE or LOAD armed, invisibly, for the next tile.";
    public const string GuardRefusal =
        "This control shares its place with BlueBird's nav data UPDATE button, so it is not tapped from here.";
    public const string UpdateRefusal =
        "Update or remove nav data from the classic 757 EFB window, which checks the cycle first.";

    private const char OptionSeparator = (char)0x1f;

    /// <param name="readOnly">
    /// The tablet screen reader as shipped (team review of 2026-10-27): the tablet is READ from its pixels,
    /// and a misread must never become a press. Only page navigation stays pressable (Home, the tablet's
    /// menus, scrolling), since a wrong one only changes the page shown; every button, switch, check box
    /// and slider is plain text with the state read for it. False keeps the operable window for the tests
    /// and for a later decision to bring it back.
    /// </param>
    public static Built Build(Bbs757EfbScreenModel? model, string? alert, bool readOnly = false)
    {
        var data = new Dictionary<string, string>(StringComparer.Ordinal) { ["page"] = model?.PageName ?? "BlueBird tablet" };
        var actions = new Dictionary<int, Action>();
        int n = 0;

        void Add(string text, string kind = "", string controlType = "", bool clickable = false, int level = 0,
            string value = "", string key = "", bool announceChange = false, string[]? options = null,
            bool disabled = false, string live = "", double? min = null, double? max = null, double? step = null,
            Action? action = null)
        {
            int i = n++;
            int aidx = i + 1;
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
            data[$"items.{i}.disabled"] = disabled ? "true" : "false";
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
            if (model.App != "HOME")
            {
                var home = new Bbs757EfbItem(Bbs757EfbKind.Button, "Home", Rectangle.Empty, Bbs757EfbScreen.HomeButton, true, "btn:HOME");
                Add("Home", kind: "button", clickable: true, key: "btn:HOME", action: new Action(ActionKind.Tap, home));
            }
            foreach (var m in model.Menu)
                AddItem(m);
            foreach (var it in model.Items)
                AddItem(it);
        }

        if (model?.Scrollable == true)
        {
            Add("Scroll down", kind: "button", clickable: true, key: "btn:scroll-down", action: new Action(ActionKind.ScrollDown));
            Add("Scroll up", kind: "button", clickable: true, key: "btn:scroll-up", action: new Action(ActionKind.ScrollUp));
        }
        Add("More", level: 2, key: "h:more");
        if (readOnly) Add(ReadOnlyNote, key: "txt:read-only");
        Add(readOnly ? ClassicWindowReadOnlyText : ClassicWindowText, kind: "button", clickable: true, key: "btn:classic", action: new Action(ActionKind.Classic));
        Add(ReadAgainText, kind: "button", clickable: true, key: "btn:refresh", action: new Action(ActionKind.Refresh));
        return new Built(data, actions, n);

        void AddItem(Bbs757EfbItem it)
        {
            // On a page that scrolls, a control in the update guard is scrolled out of it and then tapped.
            if (!it.Clickable && model.Scrollable && Bbs757EfbScreen.UpdateGuard.Contains(it.Hit))
                it = it with { Clickable = true };
            if (readOnly && AddReadOnly(it)) return;
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
                {
                    string text = it.Text + (it.Selected && !it.Text.EndsWith("(selected)", StringComparison.Ordinal) ? " (current page)" : "");
                    Add(text, kind: "button", clickable: it.Clickable, key: it.Key,
                        action: it.Clickable ? new Action(ActionKind.Tap, it) : new Action(ActionKind.Refuse, it, GuardRefusal));
                    break;
                }
                case Bbs757EfbKind.Button:
                {
                    var refusal = ButtonRefusal(it);
                    bool state = it.Tint is Bbs757EfbTint.Red or Bbs757EfbTint.Green || it.Text.Contains(':');
                    Add(it.Text, kind: "button", clickable: refusal == null, key: it.Key, announceChange: state,
                        disabled: refusal != null,
                        action: refusal == null ? new Action(ActionKind.Tap, it) : new Action(ActionKind.Refuse, it, refusal));
                    break;
                }
                case Bbs757EfbKind.Toggle:
                {
                    string word = it.Value;
                    bool plain = word.Length == 0 || word.Equals("On", StringComparison.OrdinalIgnoreCase) || word.Equals("Off", StringComparison.OrdinalIgnoreCase);
                    string text = plain ? it.Text : it.Text + ": " + word;
                    Add(text, controlType: "checkbox", clickable: it.Clickable, value: it.On == true ? "true" : "false",
                        key: it.Key, disabled: !it.Clickable,
                        action: it.Clickable ? new Action(ActionKind.Tap, it) : new Action(ActionKind.Refuse, it, GuardRefusal));
                    break;
                }
                case Bbs757EfbKind.Checkbox:
                    Add(it.Text, controlType: "checkbox", clickable: it.Clickable, value: it.On == true ? "true" : "false",
                        key: it.Key, disabled: !it.Clickable,
                        action: it.Clickable ? new Action(ActionKind.Tap, it) : new Action(ActionKind.Refuse, it, GuardRefusal));
                    break;
                case Bbs757EfbKind.Slider:
                {
                    var options = OptionsFor(it);
                    string? current = options?.FirstOrDefault(o => string.Equals(o, it.Value, StringComparison.OrdinalIgnoreCase));
                    if (options != null && current != null)
                        Add(it.Text, controlType: "select", clickable: true, value: current, key: it.Key, options: options,
                            action: new Action(ActionKind.Tap, it));
                    else if (it.Slider is { } sl)
                    {
                        // No known choices (the cabin lights): a 0-100 slider from the knob's place.
                        double pct = sl.TrackRight > sl.TrackLeft ? Math.Round(100.0 * (sl.KnobX - sl.TrackLeft) / (sl.TrackRight - sl.TrackLeft)) : 0;
                        pct = Math.Clamp(pct, 0, 100);
                        string label = it.Value.Length > 0 ? it.Text + " (" + it.Value + ")" : it.Text;
                        Add(label, controlType: "range", clickable: true, value: pct.ToString(CultureInfo.InvariantCulture),
                            key: it.Key, min: 0, max: 100, step: 10, action: new Action(ActionKind.Tap, it));
                    }
                    break;
                }
            }
        }

        // A control as text in the read-only window; false for the kinds that render the same either way
        // (headings, text, rows, the menus).
        bool AddReadOnly(Bbs757EfbItem it)
        {
            switch (it.Kind)
            {
                case Bbs757EfbKind.Button:
                    Add(it.Text, key: it.Key);
                    return true;
                case Bbs757EfbKind.Toggle:
                case Bbs757EfbKind.Checkbox:
                {
                    string state = it.Value.Length > 0 && !(it.Value.Equals("On", StringComparison.OrdinalIgnoreCase) || it.Value.Equals("Off", StringComparison.OrdinalIgnoreCase))
                        ? it.Value
                        : it.On switch { true => "On", false => "Off", _ => "state not read" };
                    Add(it.Text + ": " + state, key: it.Key);
                    return true;
                }
                case Bbs757EfbKind.Slider:
                {
                    string value = it.Value;
                    if (value.Length == 0 && it.Slider is { } sl && sl.TrackRight > sl.TrackLeft)
                        value = Math.Clamp(Math.Round(100.0 * (sl.KnobX - sl.TrackLeft) / (sl.TrackRight - sl.TrackLeft)), 0, 100)
                            .ToString(CultureInfo.InvariantCulture) + " percent";
                    Add(value.Length > 0 ? it.Text + ": " + value : it.Text, key: it.Key);
                    return true;
                }
                default:
                    return false;
            }
        }
    }

    /// <summary>Why a tablet button is not tapped from the window, or null when it may be.</summary>
    internal static string? ButtonRefusal(Bbs757EfbItem it)
    {
        if (!it.Clickable) return GuardRefusal;
        string up = it.Key.ToUpperInvariant();
        if (up.Contains("SAVE USER CONFIG") || up.Contains("LOAD USER CONFIG")) return SaveLoadRefusal;
        if (up.Contains("|UPDATE@") || up.Contains("|REMOVE@")) return UpdateRefusal;
        return null;
    }

    /// <summary>The classic window's name for a preset tile's words ("COLD & DARK" -> "Cold and dark"),
    /// or null when the words are no preset: the window keeps the ground / flight gate
    /// (<see cref="Bbs757EfbLayout.PresetRefusal"/>).</summary>
    public static string? PresetName(string words)
    {
        string w = (words ?? "").Trim().ToUpperInvariant().Replace(" & ", " AND ");
        foreach (var name in Bbs757EfbLayout.PresetNames)
            if (string.Equals(name.ToUpperInvariant(), w, StringComparison.Ordinal)) return name;
        return null;
    }

    /// <summary>A slider's choices, left to right, when the tablet's are known
    /// (<see cref="Bbs757EfbVocabulary.SliderOptions"/>), as the tablet writes them.</summary>
    public static string[]? OptionsFor(Bbs757EfbItem it)
    {
        if (!it.Key.StartsWith("sld:", StringComparison.Ordinal)) return null;
        return Bbs757EfbVocabulary.SliderOptions.TryGetValue(it.Key.Substring(4), out var o) ? o : null;
    }

    /// <summary>Where on a slider's track option <paramref name="index"/> of <paramref name="count"/> sits
    /// (evenly spread, the ends at the track's ends: measured on IRS ALIGNMENT TIME, BOARDING &amp; LOAD
    /// RATE and NOTIFICATIONS FADE-OUT TIMER, 2026-10-26).</summary>
    public static int OptionX(Bbs757EfbSlider s, int index, int count)
    {
        if (count <= 1) return s.TrackLeft;
        return s.TrackLeft + (int)Math.Round((double)index * (s.TrackRight - s.TrackLeft) / (count - 1));
    }
}
