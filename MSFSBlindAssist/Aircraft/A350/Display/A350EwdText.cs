namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The Alt+E window's text (pure): the upper display (E/WD) as the pilot sees it, the engine block, the ATC mailbox
/// drawn above the warning display, and the warning display, one line per display line, each with its colours; the ECAM
/// cursor and an action box said where they are. The CTR draw holds the lower display too: its SD page and permanent data
/// are the SD window's (<see cref="A350SdText"/>), never read here (Gus, 2026-11-16: "The engine display still seems to
/// have the lower display stuff inside of it").</summary>
public static class A350EwdText
{
    public static string Build(A350DrawCapture? capture) =>
        capture == null ? A350DisplayStatus.NotAvailableYet : FromView(capture, A350Ewd.Parse(capture));

    /// <summary>The text from a draw already parsed (<paramref name="view"/> is <paramref name="capture"/>'s), so a window that
    /// also asked whether the CTR is dark parses it once.</summary>
    public static string FromView(A350DrawCapture capture, A350EwdView view)
    {
        if (!view.Powered) return "The E/WD and SD are off: no power.";

        var mailbox = A350Mailbox.Read(capture).Where(l => l.Kind == "text").Select(l => l.Spoken).ToList();
        var o = new List<string> { "Engine display:" };
        o.AddRange(A350SdPage.ReadEngineBlock(view.Engine));
        if (mailbox.Count > 0)
        {
            o.Add("Mailbox:");
            o.AddRange(mailbox);
        }
        o.Add("Warning display:");
        if (view.Warning.Count == 0) o.Add("No messages");
        foreach (var l in view.Warning)
        {
            var marks = new List<string>();
            if (l.Selected) marks.Add(l.Choice is { } choice ? choice + " selected" : "selected");
            if (l.Box == EcamBox.ToDo) marks.Add("to do");
            if (l.Box == EcamBox.Done) marks.Add("done");
            o.Add(l.Spoken + (marks.Count > 0 ? " (" + string.Join(", ", marks) + ")" : ""));
        }
        return string.Join("\n", o);
    }
}
