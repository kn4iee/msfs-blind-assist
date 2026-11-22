namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// The FMS message on an MCDU's scratchpad line, for the call-out that speaks it with the MCDU window closed (the
/// "FMS messages" row in Ctrl+M). One instance per watch; pure: it reads what it is given and says what to speak.
/// </summary>
/// <remarks>
/// <para>The line: the scratchpad row's characters as the window shows them, without the F-PLN's slew arrows (the
/// codes <c>e</c> and <c>f</c>, drawn at the line's right end), trimmed. It is a message unless it is empty, or is the
/// MCDU's typed line, a start of it, or a line that starts with it (the page and the typed line can be a frame
/// apart while the pilot types).</para>
/// <para>A screen that may hold hidden list rows (<see cref="A330McduScreen.HiddenRowsLeftOut"/> false: one can sit on
/// the scratchpad line) counts for nothing: no message, and nothing kept from it. The first reading after the watch
/// starts is its starting point and is never spoken. A message is spoken once two readings in a row agree on it and it
/// is not the last one spoken; two readings in a row with no message on the line forget the last one spoken, so the
/// same message coming back later is spoken again, and one stray reading does not.</para>
/// </remarks>
internal sealed class A330McduMessages
{
    /// <summary>What the call-out says before the message.</summary>
    public const string Prefix = "MCDU message: ";

    private bool _started;
    private string _previous = "";   // the last reading's message, "" for none
    private string? _spoken;         // the last message spoken (or on the line at the start); null once forgotten

    /// <summary>The sentence to speak for this reading, or null.</summary>
    /// <param name="screen">The MCDU's screen, from a complete frame.</param>
    /// <param name="typedScratchpad">The MCDU's typed line from the display reader; null when it gives none.</param>
    public string? Next(A330McduScreen screen, string? typedScratchpad)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (!screen.HiddenRowsLeftOut) return null;
        string message = Message(LineText(screen), typedScratchpad) ?? "";

        if (!_started)
        {
            _started = true;
            _previous = message;
            _spoken = message.Length > 0 ? message : null;
            return null;
        }

        bool agree = message == _previous;
        _previous = message;
        if (!agree) return null;
        if (message.Length == 0)
        {
            _spoken = null;
            return null;
        }
        if (message == _spoken) return null;
        _spoken = message;
        return Prefix + message;
    }

    /// <summary>The scratchpad line's text: its characters as the window shows them (<see cref="A330McduCharacters.Display"/>),
    /// the slew arrows left out, trimmed.</summary>
    public static string LineText(A330McduScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        var text = new System.Text.StringBuilder(A330McduGeometry.Columns);
        for (int c = 0; c < A330McduGeometry.Columns; c++)
            text.Append(screen[A330McduLines.ScratchpadRow, c] is { } cell && cell.Character is not ('e' or 'f')
                ? A330McduCharacters.Display(cell)
                : " ");
        return text.ToString().Trim();
    }

    /// <summary>The line's text when it is a message; null when it is empty or is typed text: the typed line, a start
    /// of it, or a line that starts with it. An empty or missing typed line hides nothing.</summary>
    public static string? Message(string line, string? typedScratchpad)
    {
        string text = (line ?? "").Trim();
        if (text.Length == 0) return null;
        string typed = (typedScratchpad ?? "").Trim();
        if (typed.Length > 0
            && (typed.StartsWith(text, StringComparison.Ordinal) || text.StartsWith(typed, StringComparison.Ordinal)))
            return null;
        return text;
    }
}
