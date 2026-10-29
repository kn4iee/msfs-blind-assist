namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The SD page shown, as the ECAM Control Panel's status display reads it (pure; the A380's
/// "System Display Page" box): the page's name, its values named with their colours (A350SdPage), then the
/// permanent data line.
/// The rows come from the CTR draw the E/WD monitor keeps.</summary>
public static class A350SdText
{
    public static string PageName(double page) => A350Levers.SdPageWord(page) ?? $"Page {page:0}";

    public static string Build(A350DrawCapture? capture, double page)
    {
        var head = PageName(page) + " page";
        if (capture == null) return head + "\r\n" + A350DisplayStatus.NotAvailableYet;
        var view = A350Ewd.Parse(capture);
        if (!view.Powered) return head + "\r\nThe SD is off: no power.";
        var o = new List<string> { head };
        o.AddRange(A350SdPage.Read(view.SystemDisplay, (int)Math.Round(page)));
        o.AddRange(A350SdSymbols.Read(capture.Shapes, (int)Math.Round(page)));
        o.Add("Permanent data:");
        o.AddRange(A350SdPage.ReadPermanentData(view.PermanentData));
        return string.Join("\r\n", o);
    }
}
