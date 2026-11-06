using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>Human-readable dumps of the 757 layouts for the module log (<c>bbs757-module.log</c>), as the PMDG
/// module logs its layouts. Pure.</summary>
public static class Bbs757LayoutDescriptions
{
    public static IReadOnlyList<string> Describe(Bbs757FmaLayout l)
    {
        string H(uint v) => "0x" + v.ToString("x", CultureInfo.InvariantCulture);
        var lines = new List<string>
        {
            $"file analysis: {l.SourceFile}: FMA: manager {H(l.ManagerBase)} span {l.ManagerSpan}, pfd vptr {H(l.PfdVptr)}, " +
            $"pfd->afds {l.PfdToAfds}, pfd->tmc {l.PfdToTmc}",
            $"file analysis: {l.SourceFile}: FMA fields: pitch {l.Pitch}, pitchArmed {l.PitchArmed}, roll {l.Roll}, " +
            $"rollArmed {l.RollArmed}, atMode {l.AtMode}",
        };
        Words(lines, l.SourceFile, "A/T", l.AtWords);
        Words(lines, l.SourceFile, "pitch", l.PitchWords);
        Words(lines, l.SourceFile, "pitch armed", l.PitchArmedWords);
        Words(lines, l.SourceFile, "roll", l.RollWords);
        Words(lines, l.SourceFile, "roll armed", l.RollArmedWords);
        return lines;
    }

    private static void Words(List<string> lines, string file, string column, Bbs757WordTable table)
    {
        string body = string.Join(", ", table.Words.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        lines.Add($"file analysis: {file}: FMA {column} words: {body}");
    }
}
