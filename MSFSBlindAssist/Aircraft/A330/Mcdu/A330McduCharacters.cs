namespace MSFSBlindAssist.Aircraft.A330.Mcdu;

/// <summary>
/// What the MCDU's two fonts draw for the codes that are not what they say, and what the window shows for each
/// (docs/a330/evidence/03-mcdu.md, "Task 2 Step 1"). `o` draws an entry box, `d` the degree sign, `a` and `b` the
/// left and right arrows, `e` and `f` the up and down arrows, `c` the overfly mark; the ditto `"` and every other
/// character are themselves. Each shows as exactly one character, so no later column moves. The box takes the
/// Fenix MCDU window's stand-in and the overfly mark its triangle ([A320-31]).
/// </summary>
internal static class A330McduCharacters
{
    private const char BoxCode = 'o';

    /// What the window shows for a cell: always one character. Font size and colour do not change it.
    public static string Display(A330McduCell cell) => cell.Character switch
    {
        BoxCode => "-",
        'd' => "\u00B0",
        'a' => "\u2190",
        'b' => "\u2192",
        'e' => "\u2191",
        'f' => "\u2193",
        'c' => "\u0394",
        var other => other.ToString(),
    };

    /// True for the code that draws an entry box.
    public static bool IsBox(char c) => c == BoxCode;
}
