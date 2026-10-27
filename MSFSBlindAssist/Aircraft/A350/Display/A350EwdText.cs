namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The Alt+E window's text (pure): the CTR as the pilot sees it, one line per display line, each
/// with its colours; the ECAM cursor and an action box said where they are.</summary>
public static class A350EwdText
{
    /// <param name="sdPage">INI_ECAM_ACTIVE_PAGE when known: the SD section then names the page and its values.</param>
    public static string Build(A350DrawCapture? capture, double? sdPage = null)
    {
        if (capture == null) return "The A350 displays are not available yet.";
        var view = A350Ewd.Parse(capture);
        if (!view.Powered) return "The E/WD and SD are off: no power.";

        var o = new List<string> { "Engine display:" };
        o.AddRange(A350SdPage.ReadEngineBlock(view.Engine));
        o.Add("Warning display:");
        if (view.Warning.Count == 0) o.Add("No messages");
        foreach (var l in view.Warning)
        {
            var marks = new List<string>();
            if (l.Selected) marks.Add("selected");
            if (l.Box == EcamBox.ToDo) marks.Add("to do");
            if (l.Box == EcamBox.Done) marks.Add("done");
            o.Add(l.Spoken + (marks.Count > 0 ? " (" + string.Join(", ", marks) + ")" : ""));
        }
        if (sdPage is double page)
        {
            o.Add("System display: " + A350SdText.PageName(page) + " page");
            o.AddRange(A350SdPage.Read(view.SystemDisplay, (int)Math.Round(page)));
            o.AddRange(A350SdSymbols.Read(capture.Shapes, (int)Math.Round(page)));
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
