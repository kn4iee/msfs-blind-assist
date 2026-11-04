using System.Globalization;
namespace MSFSBlindAssist.Aircraft.A350.Display;

/// <summary>The SD page shown, as the ECAM Control Panel's status display reads it (pure; the A380's
/// "System Display Page" box): the page's name, its values named with their colours (A350SdPage), then the
/// permanent data line.
/// The rows come from the CTR draw the E/WD monitor keeps.</summary>
public static class A350SdText
{
    public static string PageName(double page) => A350Levers.SdPageWord(page) ?? string.Create(CultureInfo.InvariantCulture, $"Page {page:0}");

    /// <summary>The page drawn under the SD page number and its title: the STATUS key with nothing for STATUS shows another page
    /// under its number, said as "Status page, showing the Cruise page" and read as that page.</summary>
    public static (string Title, int Page) Shown(IReadOnlyList<IReadOnlyList<DrawnCell>> rows, double page)
    {
        var title = PageName(page) + " page";
        int shown = (int)Math.Round(page);
        if (shown == 14 && A350SdPage.StatusShowing(rows) is int other)
            return ($"{title}, showing the {PageName(other)} page", other);
        return (title, shown);
    }

    public static string Build(A350DrawCapture? capture, double page)
    {
        var head = PageName(page) + " page";
        if (capture == null) return head + "\r\n" + A350DisplayStatus.NotAvailableYet;
        var view = A350Ewd.Parse(capture);
        if (!view.Powered) return head + "\r\nThe SD is off: no power.";
        var (title, shown) = Shown(view.SystemDisplay, page);
        var o = new List<string> { title };
        o.AddRange(A350SdPage.Read(view.SystemDisplay, shown));
        o.AddRange(A350SdSymbols.Read(capture.Shapes, shown));
        o.Add("Permanent data:");
        o.AddRange(A350SdPage.ReadPermanentData(view.PermanentData));
        return string.Join("\r\n", o);
    }
}
