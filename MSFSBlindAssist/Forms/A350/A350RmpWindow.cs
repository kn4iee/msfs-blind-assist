using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A350 Radio Management Panels as a window (Ctrl+Shift+R, input mode; the A380 window's keys). The list is the
/// chosen RMP's screen (A350GroundDisplays.ReadRmp), refreshed twice a second. Ctrl+1/2/3 picks the radio line; digits
/// and the point are keyed into the cockpit RMP's keypad as you type (the leading 1 and the point are optional: 2 1 9 is
/// 121.900), the first one after selecting that line on the RMP as the manual does (<see cref="A350RmpEntry"/>: the RMP
/// types into its selected line, and its screen shows neither); Backspace deletes the last one (CLR) and says what is
/// left. Enter loads the entry as the line's standby (its line select key) and reads the line back, saying when the RMP
/// refused it; Alt+1/2/3 swaps that line's standby in (the activation key) and reads it back. Alt+V/H/L/N/U press the
/// VHF, HF, TEL, NAV and MENU page keys, and the lines that changed are spoken. A key that would leave an entry behind
/// (an activation or page key, another RMP, closing) deletes it first and says so. Alt+T starts a squawk: four digits
/// 0-7, set on the fourth through the stock XPNDR_SET (the A350 keeps the transponder and its RMPs in step). Keys go in
/// order, one at a time, 150 ms apart: two in one frame are read in the aircraft's own order (live: 4 3 2 1 typed in a
/// burst became 3412).
/// </summary>
public sealed class A350RmpWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int KeyGapMs = 150;

    /// <summary>How long after a line select or activation key its line is read back.</summary>
    private const int ReadBackMs = 300;

    /// <summary>Alt+letter page keys (the A380's V for VHF; T is the squawk, as on the A380).</summary>
    internal static readonly IReadOnlyDictionary<Keys, string> PageKeys = new Dictionary<Keys, string>
    {
        [Keys.V] = "VHF", [Keys.H] = "HF", [Keys.L] = "TEL", [Keys.N] = "NAV", [Keys.U] = "MENU",
    };

    private readonly Func<int, A350DrawCapture?> _latest;
    private readonly Func<int, Task<A350DrawCapture?>> _drawNow;
    private readonly Action<string> _send;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly DisplayListBox _list;
    private readonly ComboBox _side;
    private readonly IntPtr _previousWindow;
    private readonly A350RmpEntry _entry = new();
    private readonly A350ShownCapture _shown = new();

    /// <summary>Keys to press and read-backs to speak, in order; one pump runs them on the UI thread, after the window
    /// closes too (its entry's deletion).</summary>
    private readonly Queue<Func<Task>> _work = new();
    private bool _pumping;
    private int _rmp = 1;   // the RMP shown, kept so a change of RMP deletes the entry typed on the one left
    private IReadOnlyList<string> _lines = Array.Empty<string>();
    private System.Windows.Forms.Timer? _timer;
    private bool _busy;
    private int _line = 1;
    private string? _squawk;   // the squawk being typed, or null

    public A350RmpWindow(Func<int, A350DrawCapture?> latest, Func<int, Task<A350DrawCapture?>> drawNow, Action<string> send,
        ScreenReaderAnnouncer announcer)
    {
        _previousWindow = GetForegroundWindow();
        _latest = latest;
        _drawNow = drawNow;
        _send = send;
        _announcer = announcer;

        Text = "A350 RMP";
        Size = new Size(760, 420);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;

        _side = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "RMP", TabIndex = 0 };
        _side.Items.AddRange(new object[] { "Captain RMP (RMP 1)", "First Officer RMP (RMP 2)", "RMP 3" });
        _side.SelectedIndex = 0;
        _side.SelectedIndexChanged += (s, e) =>
        {
            Press(_rmp, Abandoned());
            _rmp = Rmp;
            _ = RefreshAsync(false);
        };

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 1, AccessibleName = "RMP screen",
            AccessibleDescription = "Control plus 1 to 3 picks the radio, type a frequency, Enter loads it as standby, Alt plus 1 to 3 "
                + "swaps it in, Backspace deletes a digit. Alt plus V, H, L, N, U: VHF, HF, TEL, NAV, MENU pages. Alt plus T: squawk.",
        };
        _list.SetText("Loading the RMP...");

        Controls.Add(_list);
        Controls.Add(_side);

        Load += async (s, e) =>
        {
            Activate();
            _list.Focus();
            await RefreshAsync(false);
            _timer = new System.Windows.Forms.Timer { Interval = 500 };
            _timer.Tick += async (s2, e2) => await RefreshAsync(false);
            _timer.Start();
        };
    }

    private int Rmp => (_side.SelectedIndex < 0 ? 0 : _side.SelectedIndex) + 1;

    private async Task RefreshAsync(bool now)
    {
        if (_busy || IsDisposed) return;
        _busy = true;
        try
        {
            var cap = now ? await _drawNow(Rmp) : null;
            if (cap is { Ok: true }) _shown.Fresh(_latest(Rmp));
            else
            {
                cap = _latest(Rmp);
                if (_shown.Superseded(cap)) return;   // older than the fresh draw shown
            }
            if (IsDisposed) return;
            if (cap == null) { _list.SetText("The A350 displays are not available yet."); return; }
            Show(Lines(cap));
        }
        catch { /* best-effort; keep the last good screen */ }
        finally { _busy = false; }
    }

    private static IReadOnlyList<string> Lines(A350DrawCapture cap)
    {
        var lines = A350GroundDisplays.ReadRmp(cap);
        return lines.Count == 0 ? new[] { "The RMP screen is off." } : lines;
    }

    private void Show(IReadOnlyList<string> lines)
    {
        _lines = lines;
        _list.SetLines(lines);
    }

    /// <summary>Queues keys for the chosen RMP, then <paramref name="then"/> once they are sent.</summary>
    private void Press(IEnumerable<string> keys, Func<Task>? then = null) => Press(Rmp, keys, then);

    private void Press(int rmp, IEnumerable<string> keys, Func<Task>? then = null)
    {
        foreach (var key in keys)
            _work.Enqueue(async () =>
            {
                _send(A350Rmp.Press(rmp, key));
                await Task.Delay(KeyGapMs);
            });
        if (then != null) _work.Enqueue(then);
        if (!_pumping) _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        _pumping = true;
        try
        {
            while (_work.Count > 0)
                await _work.Dequeue()();
        }
        catch { /* best-effort; the next key starts a new pump */ }
        finally { _pumping = false; }
    }

    /// <summary>The CLR presses deleting what was typed and not loaded, said aloud ("272 cleared") when there was any.</summary>
    private IReadOnlyList<string> Abandoned()
    {
        string typed = _entry.Typed;
        var keys = _entry.Abandon();
        if (typed.Length > 0) _announcer.AnnounceImmediate($"{typed} cleared");
        return keys;
    }

    /// <summary>Speaks what a page key changed, from a fresh draw (<see cref="A350Rmp.PageReadBack"/>).</summary>
    private async Task ChangesAsync(string page)
    {
        int rmp = Rmp;
        await Task.Delay(ReadBackMs);
        if (IsDisposed || rmp != Rmp || await _drawNow(rmp) is not { Ok: true } cap || IsDisposed || rmp != Rmp) return;
        _shown.Fresh(_latest(rmp));
        var lines = Lines(cap);
        string said = A350Rmp.PageReadBack(page, _lines, lines);
        Show(lines);
        _announcer.Announce(said);
    }

    /// <summary>Reads line <paramref name="line"/> of RMP <paramref name="rmp"/> back from a fresh draw, as
    /// <paramref name="say"/> words it: a line select or activation key's result.</summary>
    private async Task ReadBackAsync(int rmp, int line, Func<string, string>? say = null)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await Task.Delay(ReadBackMs);
            if (IsDisposed || rmp != Rmp) return;
            if (await _drawNow(rmp) is not { Ok: true } cap || IsDisposed || rmp != Rmp) continue;
            _shown.Fresh(_latest(rmp));
            var lines = Lines(cap);
            Show(lines);
            if (lines.ElementAtOrDefault(line - 1) is { } read) _announcer.Announce(say == null ? read : say(read));
            return;
        }
    }

    private void TypeSquawk(char c)
    {
        if (c is < '0' or > '7') { _announcer.AnnounceImmediate("A squawk digit is 0 to 7."); return; }
        _squawk += c;
        if (_squawk!.Length < 4) { _announcer.AnnounceImmediate(c.ToString()); return; }
        if (A350Rmp.SquawkBcd(_squawk) is int bcd)
        {
            _send($"{bcd} (>K:XPNDR_SET)");
            _announcer.AnnounceImmediate($"Squawk {_squawk}");
        }
        _squawk = null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        bool alt = (keyData & Keys.Alt) == Keys.Alt, ctrl = (keyData & Keys.Control) == Keys.Control;
        if (alt && key == Keys.T)
        {
            _squawk = "";
            _announcer.AnnounceImmediate("Squawk: type four digits");
            return true;
        }
        if (alt && PageKeys.TryGetValue(key, out var page))
        {
            _squawk = null;
            Press(Abandoned().Append(page), () => ChangesAsync(page));
            return true;
        }
        if ((alt || ctrl) && key is >= Keys.D1 and <= Keys.D3)
        {
            int line = key - Keys.D0;
            if (ctrl)
            {
                _line = line;
                Press(_entry.MoveTo(line));   // a frequency being typed follows the pick
                _announcer.AnnounceImmediate(_lines.ElementAtOrDefault(line - 1) is { } l ? $"Line {line}: {l}" : $"Line {line}");
            }
            else
            {
                int rmp = Rmp;
                Press(Abandoned().Append(A350Rmp.ActivationKey(line)), () => ReadBackAsync(rmp, line));
            }
            return true;
        }
        if (ActiveControl == _list && !alt && !ctrl)
        {
            char? c = key switch
            {
                >= Keys.D0 and <= Keys.D9 => (char)('0' + (key - Keys.D0)),
                >= Keys.NumPad0 and <= Keys.NumPad9 => (char)('0' + (key - Keys.NumPad0)),
                Keys.OemPeriod or Keys.Decimal => '.',
                _ => null,
            };
            if (c is char ch)
            {
                if (_squawk != null) TypeSquawk(ch);
                else Press(_entry.Type(ch, _line));
                return true;
            }
            switch (key)
            {
                case Keys.Enter:
                {
                    _squawk = null;
                    int rmp = Rmp, line = _line;
                    string typed = _entry.Typed;
                    string? before = _lines.ElementAtOrDefault(line - 1);
                    Press(_entry.Load(line), () => ReadBackAsync(rmp, line, after => A350RmpEntry.LoadReadBack(typed, before, after)));
                    return true;
                }
                case Keys.Back:
                    if (_squawk is { Length: > 0 }) { _squawk = _squawk[..^1]; _announcer.AnnounceImmediate("Deleted"); }
                    else
                    {
                        if (_entry.DeleteLast() is { } clr) Press(new[] { clr });
                        _announcer.AnnounceImmediate(_entry.Spoken);
                    }
                    return true;
            }
        }
        if (keyData == Keys.F5) { _ = RefreshAsync(true); return true; }
        if (keyData == Keys.Escape)
        {
            if (_squawk != null) { _squawk = null; _announcer.AnnounceImmediate("Squawk cancelled"); return true; }
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Press(_rmp, _entry.Abandon());   // the pump outlives the window
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
        base.OnFormClosed(e);
        if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer?.Stop();
            _timer?.Dispose();
            _timer = null;
        }
        base.Dispose(disposing);
    }
}
