using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A350;
using MSFSBlindAssist.Aircraft.A350.Display;

namespace MSFSBlindAssist.Forms.A350;

/// <summary>
/// The A350 Radio Management Panels as a window (Ctrl+Shift+R, input mode; the A380 window's keys). The list is the
/// chosen RMP's screen (A350GroundDisplays.ReadRmp), refreshed twice a second; after a key, the lines that changed
/// are spoken. Digits and the point are keyed into the cockpit RMP's keypad as you type (the leading 1 and the point
/// are optional: 2 1 9 is 121.900); Ctrl+1/2/3 picks the radio line Enter loads it on (the line select key: standby);
/// Alt+1/2/3 swaps that line's standby in (the activation key); Backspace is CLR. Alt+V/H/L/N/U press the VHF, HF,
/// TEL, NAV and MENU page keys. Alt+T starts a squawk: four digits 0-7, set on the fourth through the stock XPNDR_SET
/// (the A350 keeps the transponder and its RMPs in step). Keys go one at a time, 150 ms apart: two in one frame are
/// read in the aircraft's own order (live: 4 3 2 1 typed in a burst became 3412).
/// </summary>
public sealed class A350RmpWindow : Form
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int KeyGapMs = 150;

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
    private readonly SemaphoreSlim _keys = new(1, 1);
    private IReadOnlyList<string> _lines = Array.Empty<string>();
    private System.Windows.Forms.Timer? _timer;
    private bool _busy, _announceChanges;
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
        _side.SelectedIndexChanged += (s, e) => _ = RefreshAsync(false);

        _list = new DisplayListBox
        {
            Dock = DockStyle.Fill, Font = new Font("Consolas", 11), TabIndex = 1, AccessibleName = "RMP screen",
            AccessibleDescription = "Type a frequency, Control plus 1 to 3 picks the radio, Enter loads it as standby, Alt plus 1 to 3 "
                + "swaps it in, Backspace clears. Alt plus V, H, L, N, U: VHF, HF, TEL, NAV, MENU pages. Alt plus T: squawk.",
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
            if (cap is not { Ok: true }) cap = _latest(Rmp);
            if (IsDisposed) return;
            if (cap == null) { _list.SetText("The A350 displays are not available yet."); return; }
            var lines = A350GroundDisplays.ReadRmp(cap);
            if (lines.Count == 0) lines = new[] { "The RMP screen is off." };
            if (_announceChanges)
            {
                var changed = lines.Where(l => !_lines.Contains(l)).Take(3).ToList();
                if (changed.Count > 0)
                {
                    _announcer.Announce(string.Join(". ", changed));
                    _announceChanges = false;
                }
            }
            _lines = lines;
            _list.SetLines(lines);
        }
        catch { /* best-effort; keep the last good screen */ }
        finally { _busy = false; }
    }

    /// <summary>Presses keys on the chosen RMP one at a time, a frame or more apart, then speaks what changed.</summary>
    private async Task PressAsync(params string[] keys)
    {
        await _keys.WaitAsync();
        try
        {
            foreach (var key in keys)
            {
                _send(A350Rmp.Press(Rmp, key));
                await Task.Delay(KeyGapMs);
            }
        }
        finally { _keys.Release(); }
        _announceChanges = true;
        await Task.Delay(300);
        await RefreshAsync(true);
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
            _announceChanges = true;
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
            _ = PressAsync(page);
            return true;
        }
        if ((alt || ctrl) && key is >= Keys.D1 and <= Keys.D3)
        {
            int line = key - Keys.D0;
            if (ctrl)
            {
                _line = line;
                _announcer.AnnounceImmediate(_lines.ElementAtOrDefault(line - 1) is { } l ? $"Line {line}: {l}" : $"Line {line}");
            }
            else _ = PressAsync(A350Rmp.ActivationKey(line));
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
                else if (A350Rmp.KeyFor(ch) is { } k) _ = PressAsync(k);
                return true;
            }
            switch (key)
            {
                case Keys.Enter: _squawk = null; _ = PressAsync(A350Rmp.LineSelectKey(_line)); return true;
                case Keys.Back:
                    if (_squawk is { Length: > 0 }) { _squawk = _squawk[..^1]; _announcer.AnnounceImmediate("Deleted"); }
                    else _ = PressAsync("CLR");
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
            _keys.Dispose();
        }
        base.Dispose(disposing);
    }
}
