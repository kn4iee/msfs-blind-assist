using System.Globalization;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's squawk entry: a typed four-digit code in the TCAS and transponder panel, sent as the
/// stock <c>K:XPNDR_SET</c> in BCD (measured: 0x4743 set 4743; a decimal 4743 set 1207) and read
/// back from the stock <c>TRANSPONDER CODE:1</c> (BCO16). The code is spoken as one number, "Squawk
/// 4743" (the owner's rule on every aircraft). Pure.
/// </summary>
public static class Bbs757Squawk
{
    /// <summary>The panel's text field: a key containing "_SET" is what MainForm renders as a box that Enter applies.</summary>
    public const string SetKey = "BBS757_SQUAWK_SET";

    /// <summary>The read-back row, <c>TRANSPONDER CODE:1</c> in BCO16, shown in the panel's status display.</summary>
    public const string CodeKey = "BBS757_SQUAWK";

    public const string InvalidMessage = "Squawk must be four digits, each 0 to 7.";
    public const string EmptyMessage = "Type a four-digit squawk first.";

    /// <summary>
    /// What the pilot typed, as MainForm hands it over: a double, so "0421" arrives as 421 and is padded
    /// back to four digits; an empty or unparseable box arrives as NaN (the field opts in to
    /// <c>UnparseableTextAsNaN</c>, since 0000 is a code a pilot can mean).
    /// </summary>
    public static bool TryParse(double typed, out string code, out string error)
    {
        code = "";
        error = "";
        if (double.IsNaN(typed)) { error = EmptyMessage; return false; }
        if (!double.IsFinite(typed) || typed < 0 || typed > 7777 || Math.Abs(typed - Math.Round(typed)) > 1e-9)
        {
            error = InvalidMessage;
            return false;
        }
        string digits = ((int)Math.Round(typed)).ToString("0000", CultureInfo.InvariantCulture);
        if (digits.Any(ch => ch > '7')) { error = InvalidMessage; return false; }
        code = digits;
        return true;
    }

    /// <summary>The code as BCD, one digit per nibble: "4743" → 0x4743.</summary>
    public static int Bcd(string code)
    {
        int bcd = 0;
        foreach (char c in code) bcd = (bcd << 4) | (c - '0');
        return bcd;
    }

    /// <summary>The calculator string that sets the transponder: "18243 (>K:XPNDR_SET)" for 4743.</summary>
    public static string Write(string code) => $"{Bcd(code).ToString(CultureInfo.InvariantCulture)} (>K:XPNDR_SET)";

    /// <summary><c>TRANSPONDER CODE:1</c> in BCO16, one octal digit per nibble: 0x4743 → "4743".</summary>
    public static string Decode(double bco16)
    {
        int w = (int)Math.Round(bco16);
        return $"{(w >> 12) & 0xF}{(w >> 8) & 0xF}{(w >> 4) & 0xF}{w & 0xF}";
    }

    /// <summary>
    /// What a set says once the transponder was read back: "Squawk 4743"; "Squawk 4743 entered, the
    /// transponder reads 1200" when it holds another code; "Squawk 4743 sent, the transponder did not
    /// report back" when it could not be read.
    /// </summary>
    public static string Confirmation(string requested, double? readBack)
    {
        if (readBack is not double v || !double.IsFinite(v)) return $"Squawk {requested} sent, the transponder did not report back";
        string now = Decode(v);
        return now == requested ? $"Squawk {requested}" : $"Squawk {requested} entered, the transponder reads {now}";
    }
}
