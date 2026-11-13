namespace MSFSBlindAssist.Forms.A350;

/// <summary>One control of a display window: the label it shows, the panel row it presses on each side (the captain's 0, the first
/// officer's 1), and whether it is a two-position row pressed to its other position.</summary>
public sealed record A350WindowKey(string Label, Func<int, string> Row, bool Toggle = false);

/// <summary>
/// A row of a display window's keys (plan 26), the A350's `FenixChannelDisplayForm.BuildKeyRow` with the Fenix ND window's state
/// buttons: one button for each key, in order, reading "&lt;label&gt;: &lt;state&gt;" as the row's panel shows it, its bare label while the
/// state is unread (never a guessed one). A click presses the side's row once, or toggles it, and nothing else; the screen reader reads
/// the button's new text, so the row says nothing more ([CORE-7]). The display windows and the ND window use it.
/// </summary>
public sealed class A350KeyRow : FlowLayoutPanel
{
    private readonly IReadOnlyList<A350WindowKey> _keys;
    private readonly IA350WindowRows _rows;
    private readonly Func<int> _side;
    private readonly List<Button> _buttons = new();

    /// <param name="pressed">Called after each press, for the window to read the display again.</param>
    public A350KeyRow(IReadOnlyList<A350WindowKey> keys, IA350WindowRows rows, Func<int> side, Action? pressed = null)
    {
        _keys = keys;
        _rows = rows;
        _side = side;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = true;
        FlowDirection = FlowDirection.LeftToRight;
        foreach (var key in keys)
        {
            var button = new Button { Text = key.Label, AccessibleName = key.Label, AutoSize = true };
            button.Click += (_, _) =>
            {
                string row = key.Row(_side());
                if (key.Toggle) _rows.Toggle(row);
                else _rows.Press(row);
                RefreshStates();
                pressed?.Invoke();
            };
            _buttons.Add(button);
            Controls.Add(button);
        }
        for (int i = 0; i < Controls.Count; i++) Controls[i].TabIndex = i;
    }

    /// <summary>The buttons, in the keys' order.</summary>
    internal IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>Each button's text from its row's state on the side shown now; changed only when it differs, so a refresh is silent.</summary>
    public void RefreshStates()
    {
        int side = _side();
        for (int i = 0; i < _keys.Count; i++)
        {
            string label = _rows.State(_keys[i].Row(side)) is { Length: > 0 } state ? $"{_keys[i].Label}: {state}" : _keys[i].Label;
            if (_buttons[i].Text != label)
            {
                _buttons[i].Text = label;
                _buttons[i].AccessibleName = label;
            }
        }
    }
}

/// <summary>A typed row of a display window (the QNH), as the panel's field: the pilot types, Enter applies it through the side's typed
/// row (<see cref="IA350WindowRows.Type"/>), whose plan validates it and says its refusal; the text stays, as in the panel's field.</summary>
public sealed class A350TypedBox : TextBox
{
    private readonly Func<int, string> _row;
    private readonly IA350WindowRows _rows;
    private readonly Func<int> _side;
    private readonly Action? _typed;

    public A350TypedBox(string label, Func<int, string> row, IA350WindowRows rows, Func<int> side, Action? typed = null)
    {
        _row = row;
        _rows = rows;
        _side = side;
        _typed = typed;
        AccessibleName = label;
        Width = 120;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = e.SuppressKeyPress = true;
            _rows.Type(_row(_side()), Text.Trim());
            _typed?.Invoke();
            return;
        }
        base.OnKeyDown(e);
    }
}
