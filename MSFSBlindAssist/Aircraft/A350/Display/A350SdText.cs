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

    /// <summary>The page to read a draw of the SD by (plan 26's re-review): the page the draw itself shows (<see cref="A350SdPage.PageOf"/>),
    /// since the A350's page variable reaches MSFSBA a moment after the draw shows a new page; the variable where the draw names none.
    /// While the variable says STATUS it stands: STATUS with nothing to say draws another page (<see cref="Shown"/> names it so). Null
    /// with neither.</summary>
    public static double? PageToRead(IReadOnlyList<IReadOnlyList<DrawnCell>> rows, double? variable) =>
        variable is 14 ? 14 : A350SdPage.PageOf(rows) ?? variable;

    public static string Build(A350DrawCapture? capture, double page) =>
        capture == null ? PageName(page) + " page\r\n" + A350DisplayStatus.NotAvailableYet : Build(capture, A350Ewd.Parse(capture), page);

    /// <summary>The text from a draw already parsed (<paramref name="view"/> is <paramref name="capture"/>'s).</summary>
    public static string Build(A350DrawCapture capture, A350EwdView view, double page)
    {
        var head = PageName(page) + " page";
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
