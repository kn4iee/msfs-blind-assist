using System.Globalization;
using System.Text.RegularExpressions;
using MSFSBlindAssist.SimConnect.BBS757;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// One VOR receiver as the CDU's NAV RADIO page shows it. <paramref name="Mhz"/> is null when it is not
/// tuned (the page shows <c>------</c>). <paramref name="Mode"/> is the letter after the frequency: 'A'
/// auto (the FMC tuned it), 'R' remote (tuned from the CDU), any other letter manual; null when the page
/// shows none. <paramref name="Ident"/> is the station's ident once received, else null.
/// </summary>
public sealed record Bbs757VorTuning(double? Mhz, char? Mode, string? Ident);

/// <summary>The NAV RADIO page's two VORs.</summary>
public sealed record Bbs757NavRadPage(Bbs757VorTuning Left, Bbs757VorTuning Right);

/// <summary>
/// The 757's own nav radios, for output N. BlueBird's VOR and ILS receivers are its own and leave the
/// stock NAV variables alone (measured, docs/b757/spec-gaps.md 3.2), so the stock NAV radios are never
/// read here. The courses are BlueBird's L:vars; the VOR frequencies are on the CDU's NAV RADIO page,
/// which the first officer's CDU is brought to (<see cref="Bbs757CduPages"/>, ruling R52). Pure.
///
/// The measured page (session 8, captain's CDU): row 0 the title <c>NAV RADIO</c>, row 1 the labels
/// <c>VOR L … VOR R</c>, row 2 the tunings, row 3 <c>CRS RADIAL CRS</c>, row 4 the courses. A tuning is
/// a frequency, a mode letter and the ident: <c>116.90AGSU</c> (auto), <c>113.90R</c> (tuned from the
/// CDU, no ident yet), or <c>------</c> (not tuned). Only VOR L's text was seen tuned; VOR R is read
/// as the same form, flush right. A row that does not read that way gives no page rather than a guess.
/// </summary>
public static class Bbs757NavRad
{
    /// <summary>The CDU key that brings up the NAV RADIO page (<see cref="Bbs757CduKeys.NavRad"/>).</summary>
    public const string PageKey = Bbs757CduKeys.NavRad;

    /// <summary>The variable keys the readout reads fresh: BlueBird's course L:vars, integer degrees.</summary>
    public const string VorLCourseKey = "BBS757_VOR_L_COURSE";
    public const string VorRCourseKey = "BBS757_VOR_R_COURSE";
    public const string IlsCourseKey = "BBS757_ILS_COURSE";

    /// <summary>The course L:vars the keys read, with the names the variables are shown under.</summary>
    public static readonly IReadOnlyList<(string Key, string LVar, string Label)> CourseVariables = new[]
    {
        (VorLCourseKey, "BBS_VOR_L_COURSE", "VOR L Course"),
        (VorRCourseKey, "BBS_VOR_R_COURSE", "VOR R Course"),
        (IlsCourseKey, "BBS_ILS_COURSE", "ILS Course"),
    };

    private const string PageTitle = "NAV RADIO";

    // The tuning row, and the column the two receivers' texts divide at (24 columns: VOR L is flush
    // left and at most 11 characters, VOR R flush right).
    private const int TuningRow = 2;
    private const int SplitColumn = 12;

    // A tuning: frequency, optional mode letter, optional ident ("116.90AGSU", "113.90R", "108.00").
    private static readonly Regex TuningPattern = new(
        @"^(?<freq>\d{3}\.\d{2})(?<mode>[A-Z])?(?<ident>[A-Z0-9]*)$", RegexOptions.CultureInvariant);

    private static readonly Bbs757VorTuning NotTuned = new(null, null, null);

    /// <summary>Whether the screen is the NAV RADIO page.</summary>
    public static bool IsNavRadio(Bbs757CduScreen s)
        => s.Lines.Count > 0 && string.Equals(Bbs757CduTitle.PageName(s.Lines[0]), PageTitle, StringComparison.Ordinal);

    /// <summary>
    /// The two VOR tunings of a NAV RADIO screen. Null when the screen is another page, or when a
    /// tuning text is neither a tuning nor dashes (a half-drawn row, a layout not seen): the readout
    /// then says the frequencies are unavailable, never "not tuned" for a row it could not read.
    /// </summary>
    public static Bbs757NavRadPage? Parse(Bbs757CduScreen s)
    {
        if (!IsNavRadio(s) || s.Lines.Count <= TuningRow) return null;
        string row = s.Lines[TuningRow].PadRight(Bbs757CduFrame.Columns);
        if (TryParseSide(row[..SplitColumn], out var left) && TryParseSide(row[SplitColumn..], out var right))
            return new Bbs757NavRadPage(left, right);
        Log.Debug("BBS757", $"NAV RADIO: the tuning row is not a tuning: \"{row.TrimEnd()}\"");
        return null;
    }

    private static bool TryParseSide(string text, out Bbs757VorTuning tuning)
    {
        tuning = NotTuned;
        text = text.Trim();
        if (text.Length > 0 && text.All(c => c == '-')) return true;   // ------: not tuned
        var m = TuningPattern.Match(text);
        if (!m.Success || !NumberParsing.TryParseExternal(m.Groups["freq"].Value, out double mhz)) return false;
        char? mode = m.Groups["mode"].Success ? m.Groups["mode"].Value[0] : null;
        string ident = m.Groups["ident"].Value;
        tuning = new Bbs757VorTuning(mhz, mode, ident.Length == 0 ? null : ident);
        return true;
    }

    /// <summary>
    /// A course L:var's value as whole degrees; null when it was not read or is not a course (NaN,
    /// below 0 or above 360).
    /// </summary>
    public static int? Course(double? raw)
        => raw is double v && double.IsFinite(v) && v >= 0 && v <= 360 ? (int)Math.Round(v) : null;

    /// <summary>
    /// One receiver as spoken: "VOR L 116.90 auto, GSU", "VOR L 113.90 remote", "VOR R not tuned". The
    /// mode letter is a word (A auto, R remote, any other manual; none, no word), the ident follows a
    /// comma, the frequency has two decimals and a dot whatever the PC's culture.
    /// </summary>
    public static string TuningPhrase(string side, Bbs757VorTuning t)
    {
        if (t.Mhz is not double mhz) return $"{side} not tuned";
        string said = $"{side} {mhz.ToString("0.00", CultureInfo.InvariantCulture)}";
        if (t.Mode is char mode) said += mode switch { 'A' => " auto", 'R' => " remote", _ => " manual" };
        if (!string.IsNullOrEmpty(t.Ident)) said += $", {t.Ident}";
        return said;
    }

    /// <summary>
    /// What N says. With the page: "VOR L 116.90 auto, GSU, course 095. VOR R not tuned, course 000. ILS
    /// course 095." Without it (the first officer's CDU busy or unreachable): "VOR L course 095, VOR R
    /// course 000, ILS course 095. Frequencies unavailable." A course that was not read is "course
    /// unavailable".
    /// </summary>
    public static string Readout(Bbs757NavRadPage? page, int? vorL, int? vorR, int? ils)
    {
        if (page == null)
            return $"VOR L course {CourseWords(vorL)}, VOR R course {CourseWords(vorR)}, ILS course {CourseWords(ils)}. Frequencies unavailable.";
        return $"{TuningPhrase("VOR L", page.Left)}, course {CourseWords(vorL)}. "
            + $"{TuningPhrase("VOR R", page.Right)}, course {CourseWords(vorR)}. "
            + $"ILS course {CourseWords(ils)}.";
    }

    private static string CourseWords(int? course)
        => course is int c ? c.ToString("000", CultureInfo.InvariantCulture) : "unavailable";
}
