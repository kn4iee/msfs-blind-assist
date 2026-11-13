namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The Alt+E window's text (pure): the CTR as the pilot sees it, one line per display line, each
/// with its colours; the ECAM cursor and an action box said where they are.</summary>
public static class A350EwdText
{
    /// <param name="sdPage">INI_ECAM_ACTIVE_PAGE when known: the SD section then names the page and its values.</param>
    public static string Build(A350DrawCapture? capture, double? sdPage = null) =>
        capture == null ? A350DisplayStatus.NotAvailableYet : Build(capture, A350Ewd.Parse(capture), sdPage);

    /// <summary>The text from a draw already parsed (<paramref name="view"/> is <paramref name="capture"/>'s), so a window that also
    /// asked whether the CTR is dark parses it once.</summary>
    public static string Build(A350DrawCapture capture, A350EwdView view, double? sdPage)
    {
        if (!view.Powered) return "The E/WD and SD are off: no power.";

        var o = new List<string> { "Engine display:" };
        o.AddRange(A350SdPage.ReadEngineBlock(view.Engine));
        var mailbox = A350Mailbox.Read(capture).Where(l => l.Kind == "text").Select(l => l.Spoken).ToList();
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
        if (sdPage is double page)
        {
            var (title, shown) = A350SdText.Shown(view.SystemDisplay, page);
            o.Add("System display: " + title);
            o.AddRange(A350SdPage.Read(view.SystemDisplay, shown));
            o.AddRange(A350SdSymbols.Read(capture.Shapes, shown));
        }
        else
        {
            o.Add("System display:");
            o.AddRange(view.SystemDisplay.Select(A350DrawLayout.Speak));
        }
        o.Add("Permanent data:");
        o.AddRange(A350SdPage.ReadPermanentData(view.PermanentData));
        return string.Join("\n", o);
    }
}
