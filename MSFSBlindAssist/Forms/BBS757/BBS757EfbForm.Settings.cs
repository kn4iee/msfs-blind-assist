using System.Globalization;
using System.Text;
using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.BBS757;

// The 757 EFB window's Settings page (BBS757EfbForm.cs builds the window and calls BuildSettingsTab): every setting of
// BlueBird's tablet, read from BlueBird's own files (efb_settings.json and each registration's
// liveries\<registration>-opt.json) and changed in them while no simulator process runs. BlueBird reads the files once,
// when the 757 loads, and writes its own copy back over them while it is loaded, so a change takes effect at the next
// load and none can be made while a simulator runs (docs/b757/evidence/efb-settings.md, section 1). The page also holds
// Sign in to Navigraph and Update nav data, its only live controls, with the window's own handlers.
//
// Top to bottom: the status box (Bbs757SimGuard.StatusText; a read-only TextBox, VAT-13), the two buttons, the Section
// list (the tablet's pages) and the chosen section's rows (Bbs757SettingsRows): a check box for a switch, a label and a
// choice list for the tablet's words, a label, a box and Set for a number or text. A registration section starts with
// the registrations its rows apply to. Picking a section rebuilds only the rows' panel; everything else (a save, the
// simulator closing, the page opening) updates the rows in place, so no control is recreated and focus never moves.
//
// The page is writable only while Bbs757SimGuard.SettingsWritable holds, checked when the page opens, on every 1 s
// status tick while the window shows, and by every write. While it does not, every input stays focusable and readable
// but does not change (a check box does not toggle, a choice list swallows what would change it, a box is read-only)
// and an attempt says why, at once. So does a row that waits for another (the announcement categories) or that cannot
// change for its own state (unreadable, automatic). A save checks first that every key it writes is in its file, then
// makes Bbs757EfbSettings.WritesFor's writes in order through Bbs757SettingsFile (which logs each save and refusal),
// stops at the first one that is not saved everywhere, and reads the files again.
//
// Speech (CORE-7): a check box or a choice list that saved says nothing, as the reader has said its new state; a number
// or text box says "<label> saved"; every failure speaks, at once ("Couldn't save <label>: <reason>"); each callout a
// save switched off that showed on is named ("<label> turned off"); the simulator starting or closing while the window
// shows is said once. The pilot's SimBrief ID, Hoppie logon code and SayIntentions ACARS key are shown in plain text and
// never logged: no value, write or row goes into a log line or an exception message here.
public partial class BBS757EfbForm
{
    // The rows inside the section panel: the label column, the input column, one row's height, the rows' width.
    private const int RowLeft = 4;
    private const int RowInputLeft = 205;
    private const int RowStep = 30;
    private const int RowWidth = 412;

    // A switch's choices are Off, then On (Bbs757SettingKind.OnOff).
    private const int OffChoice = 0;
    private const int OnChoice = 1;

    // The EFB brightness takes a value only while this setting reads Manual (the rows' Automatic state).
    private const string BrightnessModePath = "auto_brightness";

    private static readonly Bbs757SettingSection[] Sections = Enum.GetValues<Bbs757SettingSection>();

    // Asked by every write just before it reads and again just before it writes: the simulator may have started since
    // the last tick. A settings file that has gone is the file layer's to refuse.
    private static readonly Func<bool> MayWriteSettings =
        () => Bbs757SimGuard.SettingsWritable(Bbs757SimGuard.Check(), true);

    // The safety copy of each file, made before this window's first write to it.
    private readonly Bbs757SettingsBackup _settingsBackup = new(Bbs757SettingsBackup.DefaultFolder);

    // The rows of the section shown, in the tablet's order (Bbs757EfbSettings.InSection).
    private readonly List<SettingControl> _settingControls = new();

    private TextBox _settingsStatusBox = null!;
    private ComboBox _sectionCombo = null!;
    private Panel _sectionPanel = null!;
    // The registrations line of a registration section; null in the others.
    private TextBox? _registrationsBox;
    private Bbs757SettingSection _shownSection;

    // BlueBird's work folder (the newest settings file's, Bbs757WorkFolder.Find), found again when the page opens and
    // when the simulator closes, and the simulator it belongs to, named only for a pilot with more than one work folder.
    private string? _settingsFolder;
    private string? _settingsSimulatorName;

    // What the last check found; read-only until a check says otherwise.
    private bool _settingsWritable;
    private bool _settingsFileExists;

    // The simulator state the last check saw; null until the window's first check after it shows, which says nothing (a
    // change while the window was hidden is not news). Never compared with a default: default(Bbs757SimState) is
    // Unknown, on purpose.
    private Bbs757SimState? _lastSimState;

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the Settings page, top to bottom in tab order: the status box, Sign in to Navigraph, Update nav data, the
    /// Section list and the panel of the chosen section's rows (General first).
    /// </summary>
    private void BuildSettingsTab(TabPage page)
    {
        const int left = 15, fullW = 440, h = 32, gap = 8, statusH = 64;
        int y = 15, tab = 0;

        page.Controls.Add(new Label { Text = "Status", Location = new Point(left, y), AutoSize = true, TabIndex = tab++ });
        y += 20;
        _settingsStatusBox = new TextBox
        {
            Location = new Point(left, y),
            Size = new Size(fullW, statusH),
            Multiline = true,
            ReadOnly = true,
            TabStop = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Settings status",
            TabIndex = tab++,
        };
        page.Controls.Add(_settingsStatusBox);
        y += statusH + gap;

        // The window's own handlers, unchanged; both need the 757 loaded.
        _navigraphButton = new Button { Text = "Sign in to Navigraph", Location = new Point(left, y), Size = new Size(fullW, h), TabIndex = tab++ };
        _navigraphButton.Click += (_, _) => RunAction(SignInToNavigraphAsync);
        page.Controls.Add(_navigraphButton);
        y += h + gap;
        _updateButton = new Button { Text = "Update nav data", Location = new Point(left, y), Size = new Size(fullW, h), TabIndex = tab++ };
        _updateButton.Click += (_, _) => RunAction(UpdateNavDataAsync);
        page.Controls.Add(_updateButton);
        y += h + gap + 4;

        page.Controls.Add(new Label { Text = "Section", Location = new Point(left, y + 6), AutoSize = true, TabIndex = tab++ });
        _sectionCombo = new ComboBox
        {
            Location = new Point(left + 90, y + 2),
            Size = new Size(200, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Section",
            TabIndex = tab++,
        };
        foreach (Bbs757SettingSection section in Sections)
            _sectionCombo.Items.Add(Bbs757EfbSettings.SectionName(section));
        _sectionCombo.SelectedIndex = 0;
        _sectionCombo.SelectedIndexChanged += (_, _) => OnSectionPicked();
        page.Controls.Add(_sectionCombo);
        y += h + gap;

        int panelTop = y;
        _sectionPanel = new Panel
        {
            Location = new Point(left, panelTop),
            Size = new Size(fullW, 300),
            AutoScroll = true,
            TabIndex = tab++,
        };
        page.Controls.Add(_sectionPanel);
        // The rows take what the page leaves below the section list, whatever height the tab control gives the page.
        page.Resize += (_, _) => _sectionPanel.Height = Math.Max(120, page.ClientSize.Height - panelTop - 10);

        // The page opens: BlueBird's folder and files are read fresh.
        _tabs.Selected += (_, e) =>
        {
            if (e.TabPage == page) ReadSettingsAfresh();
        };

        ShowSection(Sections[0]);
    }

    // The pilot picked a section: only the rows' panel is rebuilt, and focus stays on the list.
    private void OnSectionPicked()
    {
        if (_disposed) return;
        int index = _sectionCombo.SelectedIndex;
        if (index < 0 || index >= Sections.Length || Sections[index] == _shownSection) return;
        ShowSection(Sections[index]);
    }

    /// <summary>
    /// Replaces the panel's rows with <paramref name="section"/>'s, in the tablet's order, and fills them from the
    /// files. The only place rows are made; called when the page is built and when the pilot picks a section.
    /// </summary>
    private void ShowSection(Bbs757SettingSection section)
    {
        _shownSection = section;
        // Focus is on the list that picked the section; were it on a row (the wheel over the list), it moves there
        // rather than onto a control about to go.
        if (_sectionPanel.ContainsFocus) _sectionCombo.Focus();
        _sectionPanel.SuspendLayout();
        try
        {
            Control[] old = _sectionPanel.Controls.Cast<Control>().ToArray();
            _sectionPanel.Controls.Clear();
            foreach (Control control in old) control.Dispose();
            _settingControls.Clear();
            _registrationsBox = null;
            // The new rows are placed from the top: a panel left scrolled down would shift every one of them.
            _sectionPanel.AutoScrollPosition = Point.Empty;

            IReadOnlyList<Bbs757Setting> settings = Bbs757EfbSettings.InSection(section);
            int y = 4, tab = 0;
            if (settings.Any(s => s.File == Bbs757SettingFile.Registration))
            {
                _sectionPanel.Controls.Add(new Label { Text = "Registrations", Location = new Point(RowLeft, y), AutoSize = true, TabIndex = tab++ });
                _registrationsBox = new TextBox
                {
                    Location = new Point(RowLeft, y + 18),
                    Size = new Size(RowWidth, 25),
                    ReadOnly = true,
                    TabStop = true,
                    AccessibleName = "Registrations",
                    TabIndex = tab++,
                };
                _sectionPanel.Controls.Add(_registrationsBox);
                y += 18 + RowStep;
            }
            foreach (Bbs757Setting setting in settings)
            {
                _settingControls.Add(AddRow(setting, y, ref tab));
                y += RowStep;
            }
        }
        finally
        {
            _sectionPanel.ResumeLayout();
        }
        RefreshSettingValues();
    }

    // One setting's row: a check box for a switch; a label and a choice list for the tablet's words; a label, a box and
    // Set for a number or text. Each input carries the tablet's label: a check box as its text, the others as their
    // AccessibleName after a label (never a mnemonic: "Boarding & load rate" keeps its "&").
    private SettingControl AddRow(Bbs757Setting setting, int y, ref int tab)
    {
        if (setting.Kind == Bbs757SettingKind.OnOff)
        {
            var check = new CheckBox
            {
                Text = setting.Label,
                UseMnemonic = false,
                AutoSize = true,
                Location = new Point(RowLeft, y + 2),
                TabIndex = tab++,
            };
            var switchRow = new SettingControl(setting, check) { Check = check };
            check.Click += (_, _) => OnSwitchClicked(switchRow);
            _sectionPanel.Controls.Add(check);
            return switchRow;
        }

        _sectionPanel.Controls.Add(new Label
        {
            Text = setting.Label,
            UseMnemonic = false,
            AutoEllipsis = true,
            Location = new Point(RowLeft, y + 4),
            Size = new Size(RowInputLeft - RowLeft - 6, 20),
            TabIndex = tab++,
        });

        if (setting.Kind == Bbs757SettingKind.Choice)
        {
            var combo = new Bbs757LockableComboBox
            {
                Location = new Point(RowInputLeft, y),
                Size = new Size(RowWidth - (RowInputLeft - RowLeft), 25),
                DropDownWidth = 280,
                AccessibleName = setting.Label,
                TabIndex = tab++,
            };
            foreach (Bbs757SettingChoice choice in setting.Choices) combo.Items.Add(choice.Word);
            var choiceRow = new SettingControl(setting, combo) { Combo = combo };
            combo.SelectionChangeCommitted += (_, _) => OnChoiceCommitted(choiceRow);
            combo.LockedAttempt += (_, _) => SayCannotChange(choiceRow);
            // A row is not shown into an open list (it would move the pilot's highlight): it is shown once the list closes.
            combo.DropDownClosed += (_, _) => ShowRow(choiceRow);
            _sectionPanel.Controls.Add(combo);
            return choiceRow;
        }

        // A number or a text: saved by Enter in the box or by Set.
        var box = new TextBox
        {
            Location = new Point(RowInputLeft, y),
            Size = new Size(140, 25),
            MaxLength = setting.Kind == Bbs757SettingKind.Number ? 10 : 64,
            AccessibleName = setting.Label,
            TabIndex = tab++,
        };
        var set = new Button { Text = "Set", Location = new Point(RowInputLeft + 150, y - 1), Size = new Size(60, 27), TabIndex = tab++ };
        var boxRow = new SettingControl(setting, box) { Box = box };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Return && !e.Alt && !e.Control)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                SaveBox(boxRow);
            }
        };
        set.Click += (_, _) => SaveBox(boxRow);
        _sectionPanel.Controls.Add(box);
        _sectionPanel.Controls.Add(set);
        return boxRow;
    }

    // ------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------

    // The window shows (ShowForm): the page is read as when it opens, and no simulator change is news until the
    // window's next check.
    private void OpenSettings()
    {
        _lastSimState = null;
        ReadSettingsAfresh();
    }

    // The page opens: BlueBird's work folder found again, the simulator checked and the files read.
    private void ReadSettingsAfresh()
    {
        if (_disposed) return;
        ReadSettingsFolder();
        UpdateSettingsGuard();
        RefreshSettingValues();
    }

    private void ReadSettingsFolder()
    {
        IReadOnlyList<string> roots = _workRoots();
        _settingsFolder = ResolveWorkFolder(roots);
        // Named only for a pilot with more than one work folder, so they know which one changes.
        _settingsSimulatorName = _settingsFolder != null && Bbs757WorkFolder.ExistingFolders(roots).Count > 1
            ? Bbs757WorkFolder.SimulatorOf(_settingsFolder)
            : null;
    }

    /// <summary>
    /// Reads BlueBird's files again and shows what they hold in the rows of the section shown, in place: each control's
    /// value, its description (an inactive row's <see cref="Bbs757SettingsRows.InactiveText"/>) and whether it may
    /// change. No control is recreated and focus does not move. After every save, when the page opens and when the
    /// simulator closes.
    /// </summary>
    private void RefreshSettingValues()
    {
        if (_disposed) return;
        using SettingsFiles files = SettingsFiles.Read(_settingsFolder);
        IReadOnlyList<Bbs757SettingRow> rows = Bbs757SettingsRows.For(_shownSection, files.GlobalRoot, files.RegistrationRoots);
        for (int i = 0; i < rows.Count && i < _settingControls.Count; i++)
        {
            SettingControl control = _settingControls[i];
            Bbs757SettingRow row = rows[i];
            // Both are the section's settings in the catalog's order; never a row on another setting's control.
            if (row.Setting.Path != control.Setting.Path) continue;
            control.Row = row;
            control.StateReason = row.State switch
            {
                Bbs757RowState.Unreadable => files.WhyNot(row.Setting.File, row.Setting.Path, row.Setting.Type)
                                             ?? Bbs757SettingsFile.NotInFileReason,
                Bbs757RowState.Automatic => AutomaticReason(),
                _ => null,
            };
            ShowRow(control);
        }
        if (_registrationsBox != null)
        {
            string line = Bbs757SettingsRows.RegistrationsLine(files.RegistrationNames);
            if (_registrationsBox.Text != line) _registrationsBox.Text = line;
        }
    }

    // Shows the control's row: its value, its description and whether it may change. Only what differs is set.
    private void ShowRow(SettingControl control)
    {
        if (_disposed || control.Row is not { } row) return;
        string? description = null;
        if (control.Check is { } check)
        {
            CheckState state = row.State != Bbs757RowState.Value ? CheckState.Indeterminate
                : row.ChoiceIndex == OnChoice ? CheckState.Checked
                : CheckState.Unchecked;
            if (check.CheckState != state) check.CheckState = state;
            // A box that holds neither word (unreadable, or different in the registrations) says what it holds here.
            if (row.State != Bbs757RowState.Value) description = row.Text;
        }
        else if (control.Combo is { } combo)
        {
            if (!combo.DroppedDown) ShowChoice(combo, row);
        }
        else if (control.Box is { } box)
        {
            // Out of the Value state the box shows that state's text, read-only, and it is never parsed.
            if (box.Text != row.Text) box.Text = row.Text;
        }
        if (row.Inactive && EnabledBy(row.Setting) is { } enabledBy)
        {
            string inactive = Bbs757SettingsRows.InactiveText(enabledBy);
            description = description == null ? inactive : $"{description}. {inactive}";
        }
        if (control.Input.AccessibleDescription != description) control.Input.AccessibleDescription = description;
        ApplyLock(control);
    }

    // The tablet's words, and when the row holds none of them (Other, Differs, Unreadable) its text as one more item,
    // selected; that item goes once a word is selected, so the selection never passes through none.
    private static void ShowChoice(ComboBox combo, Bbs757SettingRow row)
    {
        int words = row.Setting.Choices.Count;
        bool extra = row.State != Bbs757RowState.Value || row.ChoiceIndex < 0 || row.ChoiceIndex >= words;
        if (extra)
        {
            if (combo.Items.Count <= words) combo.Items.Add(row.Text);
            else if (!Equals(combo.Items[words], row.Text)) combo.Items[words] = row.Text;
        }
        int selected = extra ? words : row.ChoiceIndex;
        if (combo.SelectedIndex != selected) combo.SelectedIndex = selected;
        while (combo.Items.Count > (extra ? words + 1 : words)) combo.Items.RemoveAt(combo.Items.Count - 1);
    }

    private void ApplyLocks()
    {
        foreach (SettingControl control in _settingControls) ApplyLock(control);
    }

    // Whether the control takes a change now: the settings writable, the row not waiting for another and holding what can
    // be replaced (Bbs757SettingRow.CanEdit), and a box only with its own value in it. A control that does not stays
    // focusable: a check box does not toggle, a choice list is locked, a box is read-only.
    private void ApplyLock(SettingControl control)
    {
        bool editable = control.Row is { } row && row.CanEdit(_settingsWritable)
            && (control.Box == null || row.State == Bbs757RowState.Value);
        if (control.Check is { } check) check.AutoCheck = editable;
        else if (control.Combo is { } combo) combo.Locked = !editable;
        else if (control.Box is { } box) box.ReadOnly = !editable;
    }

    // ------------------------------------------------------------------
    // The guard
    // ------------------------------------------------------------------

    /// <summary>
    /// Whether BlueBird's settings may change now, on every 1 s status tick while the window shows and when the page
    /// opens: the simulator checked (<see cref="Bbs757SimGuard.Check()"/>), the settings file looked for, the status box
    /// set and every input locked or unlocked. The first check after the window shows only remembers the simulator's
    /// state. After it, a simulator that starts is said once (<see cref="Bbs757SimGuard.SimStartedText"/>), and one that
    /// closes has BlueBird's folder and files read again and is said once (<see cref="Bbs757SimGuard.SimClosedText"/>)
    /// when the settings can then be changed.
    /// </summary>
    private void UpdateSettingsGuard()
    {
        if (_disposed) return;
        Bbs757SimState state = Bbs757SimGuard.Check();
        bool changed = _lastSimState is { } before && before != state;
        _lastSimState = state;
        bool closed = changed && state == Bbs757SimState.NotRunning;
        // BlueBird may have written its files while the simulator ran, or made a work folder's first.
        if (closed) ReadSettingsFolder();

        bool fileExists = _settingsFolder != null && File.Exists(Path.Combine(_settingsFolder, Bbs757WorkFolder.SettingsFile));
        bool writable = Bbs757SimGuard.SettingsWritable(state, fileExists);
        string status = Bbs757SimGuard.StatusText(state, fileExists, _settingsSimulatorName);
        if (_settingsStatusBox.Text != status) _settingsStatusBox.Text = status;

        bool lockChanged = writable != _settingsWritable;
        _settingsWritable = writable;
        _settingsFileExists = fileExists;
        if (closed) RefreshSettingValues();
        else if (lockChanged) ApplyLocks();

        // A background change, said once and queued behind what the reader is saying.
        if (!changed) return;
        if (state == Bbs757SimState.Running) _announcer.Announce(Bbs757SimGuard.SimStartedText);
        // "can be changed" only when they can: not while BlueBird has made no settings file yet.
        else if (closed && writable) _announcer.Announce(Bbs757SimGuard.SimClosedText);
    }

    // Why a change was not made, said at once: the settings read-only, the row waiting for another, or its own state.
    private void SayCannotChange(SettingControl control)
    {
        if (_disposed) return;
        string text;
        if (!_settingsWritable)
        {
            // With no settings file, closing the simulator is not what is missing: the status box's own words.
            text = _settingsFileExists
                ? Bbs757SimGuard.LockedText
                : Bbs757SimGuard.StatusText(Bbs757SimState.NotRunning, false, null);
        }
        else if (control.Row is { Inactive: true } && EnabledBy(control.Setting) is { } enabledBy)
        {
            text = Bbs757SettingsRows.InactiveText(enabledBy);
        }
        else
        {
            text = CouldNotSave(control.Setting.Label, control.StateReason ?? Bbs757SettingsFile.NotInFileReason);
        }
        _announcer.AnnounceImmediate(text);
    }

    // ------------------------------------------------------------------
    // Saving
    // ------------------------------------------------------------------

    // A check box pressed. One that may change has toggled itself already (AutoCheck); one that may not has not.
    private void OnSwitchClicked(SettingControl control)
    {
        if (_disposed || control.Check is not { } check) return;
        if (!check.AutoCheck)
        {
            SayCannotChange(control);
            return;
        }
        int choice = check.CheckState == CheckState.Checked ? OnChoice : OffChoice;
        SaveSetting(control.Setting, control.Setting.Choices[choice].Stored);
    }

    // A choice committed: an arrow key on the closed list, or an item picked from the open one.
    private void OnChoiceCommitted(SettingControl control)
    {
        if (_disposed || control.Combo is not { } combo) return;
        if (combo.Locked)
        {
            // An item picked past the lock (from a list that was open when it came on): the stored value back, and why.
            ShowRow(control);
            SayCannotChange(control);
            return;
        }
        int index = combo.SelectedIndex;
        // The extra item is what the files hold now, not a value to write.
        if (index < 0 || index >= control.Setting.Choices.Count) return;
        SaveSetting(control.Setting, control.Setting.Choices[index].Stored);
    }

    // Enter in a number or text box, or its Set: the box's text read as the tablet takes it, then saved.
    private void SaveBox(SettingControl control)
    {
        if (_disposed || control.Box is not { } box) return;
        if (box.ReadOnly)
        {
            SayCannotChange(control);
            return;
        }
        if (!Bbs757EfbSettings.TryParseEntry(control.Setting, box.Text, out Bbs757Value value, out string? reason))
        {
            _announcer.AnnounceImmediate(CouldNotSave(control.Setting.Label, reason ?? "type another value"));
            return;
        }
        SaveSetting(control.Setting, value);
    }

    /// <summary>
    /// Saves <paramref name="setting"/> as <paramref name="value"/>: every key <see cref="Bbs757EfbSettings.WritesFor"/>
    /// plans is checked in its file first, and with one missing nothing is written; then each write is made in order,
    /// the settings file's by <c>Bbs757SettingsFile.Save</c> and the registrations' by
    /// <see cref="Bbs757SettingsFile.SaveToRegistrations"/>, each asking the guard again, and the first one that is not
    /// saved everywhere stops the rest. Then the rows are read again, in place, and the outcome said: a failure at once,
    /// with the callouts of the group that were switched off and showed on; a number or text box's "&lt;label&gt;
    /// saved"; nothing more for a check box or a choice list (CORE-7).
    /// </summary>
    private void SaveSetting(Bbs757Setting setting, Bbs757Value value)
    {
        if (_disposed) return;
        string? message;
        bool failed;
        try
        {
            (message, failed) = WriteSetting(setting, value);
        }
        catch (Exception ex)
        {
            // The type only: a message about a value could carry the pilot's codes.
            Log.Error("BBS757", $"settings: {setting.Path} not saved: {ex.GetType().Name}");
            (message, failed) = (CouldNotSave(setting.Label, Bbs757SettingsFile.UnreadableReason), true);
        }
        // What the files hold now: a change that failed shows the stored value again.
        RefreshSettingValues();
        if (message == null) return;
        if (failed) _announcer.AnnounceImmediate(message);
        else _announcer.Announce(message);
    }

    // The writes of one save and what to say of them; null when there is nothing to say.
    private (string? Message, bool Failed) WriteSetting(Bbs757Setting setting, Bbs757Value value)
    {
        string? folder = _settingsFolder;
        if (folder == null) return (CouldNotSave(setting.Label, Bbs757SettingsFile.MissingReason), true);
        IReadOnlyList<Bbs757SettingWrite> writes = Bbs757EfbSettings.WritesFor(setting, value);

        // Every key before the first write: a change of several keys (Units with units, a callout with the rest of its
        // group) never stops halfway at a key that is not there.
        using (SettingsFiles files = SettingsFiles.Read(folder))
        {
            foreach (Bbs757SettingWrite write in writes)
            {
                if (files.WhyNot(write.File, write.Path, write.Value.Type) is { } reason)
                {
                    Log.Info("BBS757", $"settings: {write.Path} not saved: {reason}");
                    return (CouldNotSave(setting.Label, reason), true);
                }
            }
        }

        // What the rows showed before the save, for the group's callouts it switches off.
        var shownBefore = new Dictionary<string, Bbs757SettingRow>(StringComparer.Ordinal);
        foreach (SettingControl shown in _settingControls)
        {
            if (shown.Row is { } row) shownBefore[shown.Setting.Path] = row;
        }

        string globalFile = Path.Combine(folder, Bbs757WorkFolder.SettingsFile);
        var sentences = new List<string>();
        string? failure = null;
        foreach (Bbs757SettingWrite write in writes)
        {
            // The setting's own key, or the key its choice also writes (Units' units); any other is a callout of its
            // group switched off.
            bool own = write.Setting.Path == setting.Path;
            failure = write.File == Bbs757SettingFile.Global
                ? SaveGlobal(globalFile, write)
                : SaveRegistrations(folder, write, own);
            if (failure != null) break;
            if (!own && shownBefore.TryGetValue(write.Setting.Path, out Bbs757SettingRow? before) && ShowedOn(before))
                sentences.Add($"{write.Setting.Label} turned off");
        }

        if (failure != null) sentences.Insert(0, failure);
        else if (setting.Kind is Bbs757SettingKind.Number or Bbs757SettingKind.Text) sentences.Add($"{setting.Label} saved");
        return (sentences.Count == 0 ? null : JoinSentences(sentences), failure != null);
    }

    // One write to the settings file: null when it holds the value now, else what to say.
    private string? SaveGlobal(string file, Bbs757SettingWrite write)
    {
        Bbs757SaveResult result = Bbs757SettingsFile.Save(file, write.Path, write.Value, MayWriteSettings,
            _settingsBackup, write.Setting.MayLog);
        return result.Status == Bbs757SaveStatus.Refused
            ? CouldNotSave(write.Setting.Label, result.Reason ?? Bbs757SettingsFile.UnreadableReason)
            : null;
    }

    // One write to every registration's file: null when each holds the value now; else "Couldn't save <label>: <reason>"
    // when none was saved, or "Saved for N of M registrations; ..." when some were. A callout of the group switched off
    // in some files only is named first, as the pilot pressed another.
    private string? SaveRegistrations(string folder, Bbs757SettingWrite write, bool own)
    {
        IReadOnlyList<(string Registration, Bbs757SaveResult Result)> results = Bbs757SettingsFile.SaveToRegistrations(
            folder, write.Path, write.Value, MayWriteSettings, _settingsBackup, write.Setting.MayLog);
        if (results.Count == 0) return CouldNotSave(write.Setting.Label, Bbs757SettingsFile.MissingReason);
        string[] notSaved = results
            .Where(r => r.Result.Status == Bbs757SaveStatus.Refused)
            .Select(r => r.Registration)
            .ToArray();
        if (notSaved.Length == 0) return null;
        if (notSaved.Length == results.Count)
            return CouldNotSave(write.Setting.Label, results[0].Result.Reason ?? Bbs757SettingsFile.UnreadableReason);
        string text = SavedForSome(results.Count - notSaved.Length, results.Count, notSaved);
        return own ? text : $"{write.Setting.Label}: {char.ToLowerInvariant(text[0])}{text[1..]}";
    }

    // "Saved for 2 of 3 registrations; G-XXXXX's file could not be read.", naming every registration not saved.
    private static string SavedForSome(int saved, int total, IReadOnlyList<string> notSaved)
    {
        string files = notSaved.Count == 1
            ? $"{notSaved[0]}'s file"
            : $"{string.Join(", ", notSaved.Take(notSaved.Count - 1).Select(r => r + "'s"))} and {notSaved[^1]}'s files";
        return string.Create(CultureInfo.InvariantCulture,
            $"Saved for {saved} of {total} registrations; {files} could not be read.");
    }

    // Whether a row showed a switch on: its On word, or values that differ between the registrations (on in some).
    private static bool ShowedOn(Bbs757SettingRow row) =>
        row.State == Bbs757RowState.Differs || (row.State == Bbs757RowState.Value && row.ChoiceIndex == OnChoice);

    private static Bbs757Setting? EnabledBy(Bbs757Setting setting) =>
        setting.EnabledBy is { } path ? Bbs757EfbSettings.ByPath(path) : null;

    // Why the EFB brightness takes no value while it reads Automatic: the tablet takes one only in its manual mode.
    private static string AutomaticReason()
    {
        Bbs757Setting? mode = Bbs757EfbSettings.ByPath(BrightnessModePath);
        Bbs757SettingChoice? manual = mode?.Choices.FirstOrDefault(c => c.Stored == Bbs757Value.Of(false));
        return mode != null && manual != null
            ? $"set {mode.Label} to {manual.Word} first"
            : "BlueBird sets it automatically";
    }

    private static string CouldNotSave(string label, string reason) => $"Couldn't save {label}: {reason}";

    // Sentences said as one: ". " between two, a space only after one that ends with its own full stop.
    private static string JoinSentences(IReadOnlyList<string> sentences)
    {
        var text = new StringBuilder();
        foreach (string sentence in sentences)
        {
            if (text.Length > 0) text.Append(text[text.Length - 1] == '.' ? " " : ". ");
            text.Append(sentence);
        }
        return text.ToString();
    }

    // ------------------------------------------------------------------
    // Rows and files
    // ------------------------------------------------------------------

    // One setting's controls on the page and what they show. Never logged or put in a message: its row's text can be
    // the pilot's codes.
    private sealed class SettingControl
    {
        public SettingControl(Bbs757Setting setting, Control input)
        {
            Setting = setting;
            Input = input;
        }

        public Bbs757Setting Setting { get; }

        // The control the screen reader reads for the setting: the check box, the choice list or the box.
        public Control Input { get; }

        public CheckBox? Check { get; init; }
        public Bbs757LockableComboBox? Combo { get; init; }
        public TextBox? Box { get; init; }

        // What the files held at the last read; null before the first.
        public Bbs757SettingRow? Row { get; set; }

        // Why the row cannot change for its own state (unreadable, automatic), in words that follow
        // "Couldn't save <label>: "; null when it can.
        public string? StateReason { get; set; }
    }

    // One read of BlueBird's files, the settings file and every registration's, parsed: for the rows, for why a row
    // cannot change and for the checks before a save. Disposed at once; nothing of it is kept or logged.
    private sealed class SettingsFiles : IDisposable
    {
        private readonly JsonDocument? _global;
        private readonly bool _globalExists;
        private readonly List<JsonDocument> _registrations = new();

        private SettingsFiles(string? folder)
        {
            RegistrationNames = Array.Empty<string>();
            if (folder == null) return;
            string globalFile = Path.Combine(folder, Bbs757WorkFolder.SettingsFile);
            _globalExists = File.Exists(globalFile);
            _global = Bbs757SettingsFile.Read(globalFile);
            IReadOnlyList<(string Registration, string Path)> files = Bbs757WorkFolder.RegistrationFiles(folder);
            RegistrationNames = files.Select(f => f.Registration).ToArray();
            foreach (var (_, file) in files)
            {
                if (Bbs757SettingsFile.Read(file) is { } document) _registrations.Add(document);
            }
        }

        /// <summary>The files of the work folder, none when there is no folder.</summary>
        public static SettingsFiles Read(string? folder) => new(folder);

        /// <summary>Every registration BlueBird has a file for, readable or not: a save goes to each.</summary>
        public IReadOnlyList<string> RegistrationNames { get; }

        public JsonElement? GlobalRoot => _global?.RootElement;

        public IReadOnlyList<JsonElement> RegistrationRoots => _registrations.Select(d => d.RootElement).ToArray();

        /// <summary>
        /// Null when the key at <paramref name="path"/> reads as <paramref name="type"/> in its file (for a registration
        /// key, in at least one registration's file); else why not, in the file layer's words: the file missing, unreadable,
        /// or without the key.
        /// </summary>
        public string? WhyNot(Bbs757SettingFile file, string path, Bbs757ValueType type)
        {
            if (file == Bbs757SettingFile.Global)
            {
                if (_global == null) return _globalExists ? Bbs757SettingsFile.UnreadableReason : Bbs757SettingsFile.MissingReason;
                return Bbs757SettingsFile.ValueAt(_global.RootElement, path, type) == null ? Bbs757SettingsFile.NotInFileReason : null;
            }
            if (RegistrationNames.Count == 0) return Bbs757SettingsFile.MissingReason;
            if (_registrations.Count == 0) return Bbs757SettingsFile.UnreadableReason;
            return _registrations.Any(d => Bbs757SettingsFile.ValueAt(d.RootElement, path, type) != null)
                ? null
                : Bbs757SettingsFile.NotInFileReason;
        }

        public void Dispose()
        {
            _global?.Dispose();
            foreach (JsonDocument document in _registrations) document.Dispose();
        }
    }
}
