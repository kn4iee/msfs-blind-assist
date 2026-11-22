// Plan 07 owns this file: the EFB.

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

public partial class IniA330Definition
{
    private SimConnect.CoherentPmdgEfbClient? _efbClient;

    private void ShowEfb(ScreenReaderAnnouncer announcer)
    {
        ShowTrackedWindow(() =>
        {
            _efbClient?.Dispose();
            _efbClient = SimConnect.CoherentPmdgEfbClient.ForIniA330();
            _efbClient.Start();
            var client = _efbClient;
            var f = new Forms.FBWA380.FbwEfbForm(client, announcer, $"{ShortName} EFB", "EFB", "EFB");
            f.VisibleChanged += (_, _) => client.SetActive(!f.IsDisposed && f.Visible);
            f.Disposed += (_, _) => { client.Dispose(); if (ReferenceEquals(_efbClient, client)) _efbClient = null; };
            return f;
        }, f => { _efbClient?.SetActive(true); f.ShowForm(); });
    }

    /// <summary>The aircraft was switched away from: the EFB connection is closed (its window closes with
    /// the tracked windows).</summary>
    partial void CloseEfb()
    {
        _efbClient?.Dispose();
        _efbClient = null;
    }

    partial void HandleEfbHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeys, ref bool handled)
    {
        switch (action)
        {
            case HotkeyAction.ShowPMDGEFB:
                hotkeys.ExitInputHotkeyMode();
                ShowEfb(announcer);
                handled = true; return;
        }
    }
}
