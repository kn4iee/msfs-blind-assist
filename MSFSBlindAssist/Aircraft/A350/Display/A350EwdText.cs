namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The Alt+E window's text (pure): the CTR as the pilot sees it, one line per display line, each
/// with its colours; the ECAM cursor and an action box said where they are.</summary>
public static class A350EwdText
{
    public static string Build(A350DrawCapture? capture)
    {
        if (capture == null) return "The A350 displays are not available yet.";
        var view = A350Ewd.Parse(capture);
        if (!view.Powered) return "The E/WD and SD are off: no power.";

        var o = new List<string> { "Engine display:" };
        o.AddRange(view.Engine.Select(A350DrawLayout.Speak));
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
        o.Add("System display:");
        o.AddRange(view.SystemDisplay.Select(A350DrawLayout.Speak));
        o.Add("Permanent data:");
        o.AddRange(view.PermanentData.Select(A350DrawLayout.Speak));
        return string.Join("\n", o);
    }
}
