using System.Windows.Forms;
using MSFSBlindAssist.Forms.BBS757;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The 757 EFB settings window (Shift+T with no simulator running): the pure parts of its content (what leaving a number or
/// text box with a change not saved says, <c>BBS757EfbForm.UnsavedEditText</c>; where a key moves the random failure
/// frequency's slider, <c>Bbs757LockableSlider.Target</c>; which keys with Control it takes and ignores,
/// <c>Bbs757LockableSlider.IsSliderKeyWithControl</c>; which keys a choice list takes, <c>Bbs757LockableComboBox.ChangesSelection</c>
/// and <c>IsTabKeyWithControl</c>), and what the window is made of once it is built: its title, no tab
/// control, the settings status box first, and none of the Flight page's controls or the nav data buttons in any section.
/// The window is built and never shown, so no handle is made; how it reads and behaves is checked in the simulator, by ear.
/// </summary>
public class Bbs757SettingsTabTests
{
    private static Bbs757SettingRow Row(string path, string text, Bbs757RowState state = Bbs757RowState.Value) =>
        new(Bbs757EfbSettings.ByPath(path)!, state, -1, text);

    // ---- a box left with a change not saved ---------------------------------------------------------

    [Fact]
    public void Leaving_a_box_with_a_change_not_saved_says_so()
    {
        Assert.Equal("TOD pause distance (NM) not saved", BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), "25"));
        Assert.Equal("Hoppie logon code not saved", BBS757EfbForm.UnsavedEditText(Row("hoppie_logon_code", ""), "ABC"));
        // An emptied box is a change too: it would have switched the distance off, or cleared the ID.
        Assert.Equal("SimBrief ID number not saved", BBS757EfbForm.UnsavedEditText(Row("simbrief_id", "1234567"), ""));
    }

    [Fact]
    public void Leaving_a_box_that_holds_the_stored_value_says_nothing()
    {
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), "1"));
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", "1"), " 1 "));
        Assert.Null(BBS757EfbForm.UnsavedEditText(Row("tod_pause_distance", ""), ""));
        // A box showing a state, not a value, is read-only: there is nothing of the pilot's to drop.
        Assert.Null(BBS757EfbForm.UnsavedEditText(
            Row("display_brightness", Bbs757SettingsRows.AutomaticText, Bbs757RowState.Automatic), "29"));
        Assert.Null(BBS757EfbForm.UnsavedEditText(
            Row("simbrief_id", Bbs757SettingsRows.UnreadableText, Bbs757RowState.Unreadable), "1"));
        // Before the first read there is no stored value to compare with.
        Assert.Null(BBS757EfbForm.UnsavedEditText(null, "25"));
    }

    // ---- the slider's keys ---------------------------------------------------------------------------

    [Theory]
    [InlineData(Keys.Up, 4, 5)]
    [InlineData(Keys.Right, 4, 5)]
    [InlineData(Keys.Down, 4, 3)]
    [InlineData(Keys.Left, 4, 3)]
    [InlineData(Keys.PageUp, 4, 14)]
    [InlineData(Keys.PageDown, 14, 4)]
    [InlineData(Keys.Home, 14, 0)]
    [InlineData(Keys.End, 4, 30)]
    [InlineData(Keys.Up | Keys.Shift, 4, 5)]
    // Kept at its ends.
    [InlineData(Keys.Up, 30, 30)]
    [InlineData(Keys.PageUp, 25, 30)]
    [InlineData(Keys.Down, 0, 0)]
    [InlineData(Keys.PageDown, 5, 0)]
    public void Up_and_page_up_move_the_slider_higher(Keys key, int from, int to)
    {
        Assert.Equal(to, Bbs757LockableSlider.Target(key, from, 0, 30, 10));
    }

    [Theory]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Tab | Keys.Shift)]
    [InlineData(Keys.Space)]
    [InlineData(Keys.Return)]
    [InlineData(Keys.Escape)]
    [InlineData(Keys.A)]
    [InlineData(Keys.Up | Keys.Alt)]
    [InlineData(Keys.Right | Keys.Control)]
    [InlineData(Keys.Tab | Keys.Control)]
    public void Other_keys_and_the_window_s_shortcuts_leave_the_slider_alone(Keys key)
    {
        Assert.Null(Bbs757LockableSlider.Target(key, 4, 0, 30, 10));
    }

    [Theory]
    // With Control, an arrow, Home or End moves nothing: the native trackbar would act on it, Up and Down the other way.
    [InlineData(Keys.Up | Keys.Control, true)]
    [InlineData(Keys.Down | Keys.Control, true)]
    [InlineData(Keys.Left | Keys.Control, true)]
    [InlineData(Keys.Right | Keys.Control, true)]
    [InlineData(Keys.Home | Keys.Control, true)]
    [InlineData(Keys.End | Keys.Control, true)]
    [InlineData(Keys.Up | Keys.Control | Keys.Shift, true)]
    // Page Up and Page Down too: the old window's tab switch, pressed out of habit, which the native trackbar would take as a
    // page the other way (and the window save), as nothing takes it first since the window lost its tabs.
    [InlineData(Keys.PageUp | Keys.Control, true)]
    [InlineData(Keys.PageDown | Keys.Control, true)]
    [InlineData(Keys.PageDown | Keys.Control | Keys.Shift, true)]
    // Ctrl+Tab, Alt (the native trackbar ignores it), and the plain keys are not taken here.
    [InlineData(Keys.Tab | Keys.Control, false)]
    [InlineData(Keys.Up | Keys.Control | Keys.Alt, false)]
    [InlineData(Keys.PageUp | Keys.Control | Keys.Alt, false)]
    [InlineData(Keys.Up | Keys.Alt, false)]
    [InlineData(Keys.Up, false)]
    [InlineData(Keys.PageUp, false)]
    public void An_arrow_page_home_or_end_with_control_is_taken_and_moves_nothing(Keys key, bool taken)
    {
        Assert.Equal(taken, Bbs757LockableSlider.IsSliderKeyWithControl(key));
    }

    // ---- the choice lists' keys ----------------------------------------------------------------------

    [Theory]
    // The old window's tab switch, pressed out of habit: taken and doing nothing, held or not (an attempt while held).
    [InlineData(Keys.PageUp | Keys.Control, true)]
    [InlineData(Keys.PageDown | Keys.Control, true)]
    [InlineData(Keys.PageUp | Keys.Control | Keys.Shift, true)]
    // The list's own keys, Ctrl+Tab, and AltGr (Ctrl+Alt, which types a character) are not.
    [InlineData(Keys.PageUp, false)]
    [InlineData(Keys.PageDown, false)]
    [InlineData(Keys.Down | Keys.Control, false)]
    [InlineData(Keys.Tab | Keys.Control, false)]
    [InlineData(Keys.PageUp | Keys.Control | Keys.Alt, false)]
    public void Ctrl_page_up_and_down_do_nothing_on_a_choice_list(Keys key, bool taken)
    {
        Assert.Equal(taken, Bbs757LockableComboBox.IsTabKeyWithControl(key));
    }

    [Theory]
    // What a closed list answers by changing its selection or opening its list: held, each is an attempt.
    [InlineData(Keys.Up, true)]
    [InlineData(Keys.Down, true)]
    [InlineData(Keys.Down | Keys.Alt, true)]   // opens the list
    [InlineData(Keys.Left, true)]
    [InlineData(Keys.Right, true)]
    [InlineData(Keys.PageUp, true)]
    [InlineData(Keys.PageDown, true)]
    [InlineData(Keys.Home, true)]
    [InlineData(Keys.End, true)]
    [InlineData(Keys.F4, true)]
    [InlineData(Keys.PageUp | Keys.Control, true)]
    // Alt+F4 is the window's; Tab, Enter and Escape change nothing.
    [InlineData(Keys.F4 | Keys.Alt, false)]
    [InlineData(Keys.Left | Keys.Alt, false)]
    [InlineData(Keys.Tab, false)]
    [InlineData(Keys.Return, false)]
    [InlineData(Keys.Escape, false)]
    public void A_held_choice_list_takes_the_keys_that_would_change_it(Keys key, bool changes)
    {
        Assert.Equal(changes, Bbs757LockableComboBox.ChangesSelection(key));
    }

    // ---- the window, built and never shown -----------------------------------------------------------

    /// <summary>
    /// The settings window built over an empty work folder (the only one it is told of) and an announcer that speaks nowhere.
    /// Never shown: no handle is made and no message loop runs, so what is checked is what the constructor builds. Disposing
    /// it disposes the window, the announcer and the folder.
    /// </summary>
    private sealed class SettingsWindow : IDisposable
    {
        private readonly DirectoryInfo _work = Directory.CreateTempSubdirectory("msfsba-bbs757-settings-");
        private readonly SpeechCapture _announcer = new();

        public SettingsWindow() => Form = new BBS757EfbForm(_announcer, () => new[] { _work.FullName });

        public BBS757EfbForm Form { get; }

        /// <summary>What the window said.</summary>
        public SpeechCapture Speech => _announcer;

        public void Dispose()
        {
            Form.Dispose();
            _announcer.Dispose();
            try
            {
                _work.Delete(recursive: true);
            }
            catch (IOException)
            {
                // A temp folder left behind is no failure.
            }
        }
    }

    // Every control under a parent, a control before its children.
    private static IEnumerable<Control> Under(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Under(child)));

    // Every control under a parent in the order Tab visits them: a parent's children by TabIndex, each before its own children.
    private static IEnumerable<Control> InTabOrder(Control parent) =>
        parent.Controls.Cast<Control>().OrderBy(child => child.TabIndex)
            .SelectMany(child => new[] { child }.Concat(InTabOrder(child)));

    // What the window no longer holds: the Flight page's buttons and status, the button that opened the tablet reader and the
    // two buttons that ended the AIRAC data section (the tablet window has their work now).
    private static readonly string[] RemovedWords =
    {
        "Read the tablet screen", "EFB status", "Sign in to Navigraph", "Update nav data",
        "Fetch SimBrief flight", "Accept units", "Keep units", "Load fuel and payload", "Cockpit preset", "Load preset",
        "APU quick start", "IRS rapid align", "Remove chocks", "Set chocks", "Connect ground power", "Disconnect ground power",
    };

    [Fact]
    public void The_settings_window_is_titled_for_its_settings_and_has_no_tab_control()
    {
        using var window = new SettingsWindow();

        Assert.Equal("757 EFB settings", window.Form.Text);
        Assert.DoesNotContain(Under(window.Form), control => control is TabControl or TabPage);
    }

    [Fact]
    public void The_settings_window_starts_with_its_settings_content_and_its_status_box_is_the_first_stop()
    {
        using var window = new SettingsWindow();

        // The settings content begins with its "Status" label; no tab strip, no Flight page comes before it.
        Assert.Equal("Status", Assert.IsType<Label>(InTabOrder(window.Form).First()).Text);
        // The first stop Tab reaches is the settings status: a read-only TextBox, not a Label ([VAT-13]).
        TextBox status = Assert.IsType<TextBox>(InTabOrder(window.Form).First(control => control.TabStop));
        Assert.Equal("Settings status", status.AccessibleName);
        Assert.True(status.ReadOnly);
    }

    [Fact]
    public void No_section_of_the_settings_window_holds_a_flight_control_or_a_nav_data_button()
    {
        using var window = new SettingsWindow();
        ComboBox sections = Under(window.Form).OfType<ComboBox>().Single(combo => combo.AccessibleName == "Section");
        Assert.Equal(Enum.GetValues<Bbs757SettingSection>().Length, sections.Items.Count);

        for (int index = 0; index < sections.Items.Count; index++)
        {
            // The window rebuilds the rows of the section picked; that needs no handle.
            sections.SelectedIndex = index;
            string section = (string)sections.Items[index]!;
            List<Control> controls = Under(window.Form).ToList();

            // Every Button the old window had (Close, Read the tablet screen, the Flight page's, the two AIRAC data ones) is gone.
            Assert.DoesNotContain(controls, control => control is Button);
            foreach (Control control in controls)
            {
                foreach (string words in new[] { control.Text, control.AccessibleName ?? "", control.AccessibleDescription ?? "" })
                {
                    foreach (string removed in RemovedWords)
                    {
                        Assert.False(words.Contains(removed, StringComparison.OrdinalIgnoreCase),
                            $"{section}: a {control.GetType().Name} still carries '{removed}'.");
                    }
                }
            }
        }
    }

    // ---- the EFB colors theme is a note (B757-21) ------------------------------------------------------

    [Fact]
    public void The_colors_theme_row_is_a_read_only_note_that_nothing_in_the_window_changes()
    {
        // The owner's ruling of 2026-10-30: the theme is no control in the settings window either. Its row is a label and a
        // read-only box ([VAT-13]) holding the note; General has no choice list, check box or slider for it; and keys in the
        // box save nothing (a save, or a refused one, would speak).
        using var window = new SettingsWindow();   // General is the section shown first
        List<Control> controls = Under(window.Form).ToList();

        TextBox note = Assert.Single(controls.OfType<TextBox>(), box => box.AccessibleName == "EFB colors theme");
        Assert.True(note.ReadOnly);
        Assert.True(note.TabStop);
        Assert.Equal("Non-default color schemes are not supported.", note.Text);   // a folder with no settings file: no choice
        Assert.DoesNotContain(controls, control => control is ComboBox or CheckBox or Bbs757LockableSlider
            && (control.AccessibleName == "EFB colors theme" || control.Text == "EFB colors theme"));
        Assert.DoesNotContain(controls.OfType<ComboBox>(), combo => combo.Items.Contains("Light blue"));

        var onKeyDown = typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (Keys key in new[] { Keys.Return, Keys.Down, Keys.Up, Keys.Space, Keys.Delete })
            onKeyDown.Invoke(note, new object[] { new KeyEventArgs(key) });

        Assert.Empty(window.Speech.All);
        Assert.True(note.ReadOnly);
    }

    [Fact]
    public void No_save_of_another_setting_writes_the_colors_theme()
    {
        // The theme's own row is no control (above), and no other setting's writes reach ui_theme: no linked key, no group.
        foreach (Bbs757Setting setting in Bbs757EfbSettings.All.Where(s => s.Path != Bbs757EfbSettings.ThemePath))
        {
            IEnumerable<Bbs757Value> values = setting.Kind switch
            {
                Bbs757SettingKind.OnOff or Bbs757SettingKind.Choice => setting.Choices.Select(choice => choice.Stored),
                Bbs757SettingKind.Slider => setting.Steps!.Select(Bbs757Value.OfFloat),
                _ => setting.EmptyStored is { } empty ? new[] { empty } : Array.Empty<Bbs757Value>(),
            };
            foreach (Bbs757Value value in values)
                Assert.DoesNotContain(Bbs757EfbSettings.WritesFor(setting, value), write => write.Path == Bbs757EfbSettings.ThemePath);
        }
    }

    [Fact]
    public void The_airac_data_section_still_shows_the_simbrief_id()
    {
        using var window = new SettingsWindow();
        ComboBox sections = Under(window.Form).OfType<ComboBox>().Single(combo => combo.AccessibleName == "Section");

        sections.SelectedIndex = sections.Items.IndexOf(Bbs757EfbSettings.SectionName(Bbs757SettingSection.AiracData));

        // The section keeps its one setting; the Navigraph sign-in and the nav data update are on the tablet window's page.
        Assert.Contains(Under(window.Form), control => control is TextBox { AccessibleName: "SimBrief ID number" });
    }
}
