using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// One BlueBird 757 CDU screen as text: always <see cref="Bbs757CduFrame.Rows"/> lines of exactly
/// <see cref="Bbs757CduFrame.Columns"/> characters, blanks as spaces. Line 0 is the page title,
/// lines 1-12 the six label and data rows of the line select keys, line 13 the scratchpad.
/// </summary>
public sealed record Bbs757CduScreen(IReadOnlyList<string> Lines)
{
    /// <summary>The page title, trimmed ("ACT RTE 1          1/3").</summary>
    public string Title => Lines[0].Trim();

    /// <summary>The scratchpad line, trimmed: an entry or a message ("NAV DATA OUT OF DATE").</summary>
    public string Scratchpad => Lines[Bbs757CduFrame.ScratchpadRow].Trim();
}

/// <summary>
/// Reads a CDU frame from the 757's Comm Bus (<c>BBS_CDU_DISPLAY_1</c>/<c>_2</c>, see
/// docs/b757/evidence/cdu.md): MobiFlight WinCtrl JSON <c>{"Target":"Display","Data":[...]}</c>,
/// 14 rows of 24 cells row-major, a cell <c>[]</c> (blank) or <c>[char, color, size, inverse]</c>.
/// Only the character is read. Pure, and it never throws: a short frame is padded, extra cells are
/// ignored, and missing or malformed JSON, or a payload for another target, is a blank screen.
/// </summary>
public static class Bbs757CduFrame
{
    public const int Rows = 14;
    public const int Columns = 24;
    public const int ScratchpadRow = 13;

    /// <summary>The CDU's entry-box glyph, read as an underscore so a screen reader names it.</summary>
    private const char BoxGlyph = '☐';

    private static readonly string BlankLine = new(' ', Columns);

    public static Bbs757CduScreen Blank { get; } = new(Enumerable.Repeat(BlankLine, Rows).ToArray());

    public static Bbs757CduScreen Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Blank;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Blank;
            if (root.TryGetProperty("Target", out var target)
                && !(target.ValueKind == JsonValueKind.String && target.GetString() == "Display"))
                return Blank;
            if (!root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array) return Blank;

            var grid = new char[Rows * Columns];
            Array.Fill(grid, ' ');
            int i = 0;
            foreach (var cell in data.EnumerateArray())
            {
                if (i >= grid.Length) break;
                grid[i++] = CharOf(cell);
            }

            var lines = new string[Rows];
            for (int row = 0; row < Rows; row++) lines[row] = new string(grid, row * Columns, Columns);
            return new Bbs757CduScreen(lines);
        }
        catch (JsonException)
        {
            return Blank;
        }
    }

    /// <summary>A cell's character: the first of its string, else a space.</summary>
    private static char CharOf(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() == 0) return ' ';
        var first = cell[0];
        if (first.ValueKind != JsonValueKind.String) return ' ';
        string? s = first.GetString();
        if (string.IsNullOrEmpty(s)) return ' ';
        char c = s[0];
        if (c == BoxGlyph) return '_';
        if (char.IsControl(c) || char.IsSurrogate(c)) return ' ';
        return c;
    }

    /// <summary>
    /// The screen as the CDU window lists it, the HS787 FMC window's layout: the title; then for
    /// each line select key its label row indented three spaces (left out when blank) and its data
    /// row numbered "n:" (Ctrl+n or Alt+n presses that key); then the scratchpad. Trailing spaces
    /// are dropped, leading ones kept, so a right-hand field stays on the right in braille.
    /// </summary>
    public static IReadOnlyList<string> ToDisplayLines(Bbs757CduScreen screen)
    {
        var lines = new List<string> { screen.Lines[0].TrimEnd() };
        for (int key = 1; key <= 6; key++)
        {
            string label = screen.Lines[key * 2 - 1].TrimEnd();
            string data = screen.Lines[key * 2].TrimEnd();
            if (label.Trim().Length > 0) lines.Add("   " + label);
            lines.Add(data.Trim().Length > 0 ? $"{key}: {data}" : $"{key}:");
        }
        lines.Add(screen.Lines[ScratchpadRow].TrimEnd());
        return lines;
    }
}
