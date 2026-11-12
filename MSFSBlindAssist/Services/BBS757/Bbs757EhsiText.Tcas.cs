namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The TCAS rows, traffic and messages (plan 28), from <see cref="Bbs757EhsiReading.Tcas"/>, as BlueBird draws them: the
/// left block's messages, TRAFFIC on the right, and in MAP each traffic symbol among the map's items. Traffic is listed,
/// never spoken; the messages a change in the TCAS itself brings are spoken once.
/// </summary>
public static partial class Bbs757EhsiText
{
    private const string TcasFailKey = "tcas-fail", TaOnlyKey = "ta-only", TcasOffKey = "tcas-off", TrafficKey = "traffic";

    /// <summary>The one map row of the TCAS self-test, which draws four fixed symbols instead of the list.</summary>
    private const string TestPattern = "Map: TCAS test pattern";

    /// <summary>
    /// The left block and TRAFFIC, in screen order: line 1 ("Left: TFC", "Left: TCAS OFF" or "Left: TCAS FAIL", the
    /// latter two over both lines), TEST over it ("Left: TEST"), line 2 ("Left: TA ONLY"); then "Right: TRAFFIC, amber" (or
    /// red for an RA) while the IRS is valid. FAIL (failed or not powered) shows nothing below it; TFC, TCAS OFF and FAIL
    /// only where the nd's gate allows them, TA ONLY and TEST in every mode. No computer: nothing. The part missing: its
    /// one row.
    /// </summary>
    private static void TcasRows(List<string> rows, Bbs757EhsiReading r)
    {
        if (r.Tcas is not { } t)
        {
            NotRead(rows, TcasPart, r.TcasMissing);
            return;
        }
        if (!t.Computer) return;
        var w = t.Words;
        bool fail = Failed(t);
        if (fail)
        {
            if (t.MessagesAllowed) rows.Add("Left: " + Word(w.Tcas) + " " + Word(w.Fail));
        }
        else if (t.Level == Bbs757EhsiTcasLevel.Off)
        {
            if (t.MessagesAllowed) rows.Add("Left: " + Word(w.Tcas) + " " + Word(w.Off));
        }
        else if (t.TrafficSwitch && t.MessagesAllowed) rows.Add("Left: " + Word(w.Tfc));
        if (t.Test) rows.Add("Left: " + Word(w.Test));
        if (!fail && t.Level == Bbs757EhsiTcasLevel.TaOnly) rows.Add("Left: " + Word(w.TaOnly));
        if (TrafficShown(r, t)) rows.Add("Right: " + Word(w.Traffic) + (t.ResolutionAdvisory ? ", red" : ", amber"));
    }

    private static bool Failed(Bbs757EhsiTcasReading t) => !t.Powered || t.Failed;

    /// <summary>TRAFFIC: a TA or an RA present, while the IRS is valid, in any mode.</summary>
    private static bool TrafficShown(Bbs757EhsiReading r, Bbs757EhsiTcasReading t) =>
        r.Valid && (t.TrafficAdvisory || t.ResolutionAdvisory);

    /// <summary>
    /// The traffic among the map's items (MAP only; the caller lists the map only while the IRS is valid): in a full
    /// reading each symbol inside the screen's clip, "Map: traffic, 8.7 NM, 38° left, +07", "Map: proximate traffic, …, +10
    /// climbing", "Map: traffic advisory, …", "Map: resolution advisory, …, -03 descending"; other and proximate traffic
    /// only while TFC is on. During the self-test the list is not drawn: "Map: TCAS test pattern" at the screen's center.
    /// </summary>
    private static void AddTraffic(List<MapItem> items, Bbs757EhsiReading r)
    {
        if (!Is(r, EhsiMode.Map) || r.Tcas is not { Computer: true } t || t.Traffic is null) return;
        if (t.Test)
        {
            items.Add(new MapItem(r.Map.Height / 2, r.Map.Width / 2, TestPattern));
            return;
        }
        foreach (var x in t.Traffic)
        {
            if (!t.TrafficSwitch && x.Kind is Bbs757EhsiTrafficKind.Other or Bbs757EhsiTrafficKind.Proximate) continue;
            if (Where(r, x.Latitude, x.Longitude, null) is not { } at) continue;
            items.Add(new MapItem(at.Y, at.X, "Map: " + KindWords(x.Kind) + ", " + at.Place + ", "
                + Tag(x.RelativeFeet, t.TagDivisor) + TrendWords(x.Trend)));
        }
    }

    private static string KindWords(Bbs757EhsiTrafficKind kind) => kind switch
    {
        Bbs757EhsiTrafficKind.Proximate => "proximate traffic",
        Bbs757EhsiTrafficKind.Advisory => "traffic advisory",
        Bbs757EhsiTrafficKind.Resolution => "resolution advisory",
        _ => "traffic",
    };

    private static string TrendWords(Bbs757EhsiTrafficTrend trend) => trend switch
    {
        Bbs757EhsiTrafficTrend.Climbing => " climbing",
        Bbs757EhsiTrafficTrend.Descending => " descending",
        _ => "",
    };

    /// <summary>The symbol's tag as drawn: "+" at or above (so 0 is "+"), "-" below, then at least two digits of the
    /// relative altitude over the layout's divisor, truncated toward zero ("+07", "+00", "-00", "-03").</summary>
    private static string Tag(double feet, double divisor)
    {
        string sign = feet >= 0 ? "+" : "-";
        double hundreds = Math.Truncate(Math.Abs(feet) / divisor);
        return double.IsFinite(hundreds) && hundreds < int.MaxValue ? sign + ((int)hundreds).ToString("D2", Inv) : sign;
    }

    /// <summary>The TCAS messages spoken: TCAS FAIL, TA ONLY, TCAS OFF and TRAFFIC, each as its row shows it. TFC (the
    /// pilot's switch) and TEST (the pilot's knob) are shown, not spoken.</summary>
    private static void TcasMessages(List<Item> messages, Bbs757EhsiReading r)
    {
        if (!r.Powered || r.Tcas is not { Computer: true } t) return;
        var w = t.Words;
        bool fail = Failed(t);
        if (fail && t.MessagesAllowed) messages.Add(new Item(TcasFailKey, Word(w.Tcas) + " " + Word(w.Fail)));
        if (!fail && t.Level == Bbs757EhsiTcasLevel.TaOnly) messages.Add(new Item(TaOnlyKey, Word(w.TaOnly)));
        if (!fail && t.Level == Bbs757EhsiTcasLevel.Off && t.MessagesAllowed)
            messages.Add(new Item(TcasOffKey, Word(w.Tcas) + " " + Word(w.Off)));
        if (TrafficShown(r, t)) messages.Add(new Item(TrafficKey, Word(w.Traffic)));
    }
}
