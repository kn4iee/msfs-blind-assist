param([string]$Out, [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Cap {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
}
"@
$p = Get-Process | Where-Object { $_.ProcessName -like "FlightSimulator*" -and $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output "no sim window"; exit 1 }
$r = New-Object Cap+RECT
[Cap]::GetClientRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bw = $r.R - $r.L; $bh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $bw, $bh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Cap]::PrintWindow($p.MainWindowHandle, $hdc, 3) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
if ($W -gt 0 -and $H -gt 0) {
  $crop = $bmp.Clone((New-Object System.Drawing.Rectangle $X, $Y, $W, $H), $bmp.PixelFormat)
  $bmp.Dispose(); $bmp = $crop
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Output "$($p.ProcessName) ${bw}x${bh} -> $Out"
