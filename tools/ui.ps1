# Drives the built player with REAL mouse input and screenshots its client area.
#   ui.ps1 launch [width height]   start the game windowed
#   ui.ps1 click X Y               left click at client coordinates
#   ui.ps1 drag X1 Y1 X2 Y2        press, move, release
#   ui.ps1 key NAME                send a key (e.g. Escape, H, Z)
#   ui.ps1 shot NAME               save client area to <proj>\..\shots2\NAME.png
#   ui.ps1 kill
param([string]$cmd, [string]$a1, [string]$a2, [string]$a3, [string]$a4)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32 {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@
[void][W32]::SetProcessDPIAware()

$proj = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $proj "Builds\Windows\Mahjong.exe"
$out = Join-Path $proj "build\shots2"
New-Item -ItemType Directory -Force $out | Out-Null

function Get-Game {
    $p = Get-Process Mahjong -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    return $p
}

function Client-Origin($h) {
    $pt = New-Object W32+POINT
    [void][W32]::ClientToScreen($h, [ref]$pt)
    $r = New-Object W32+RECT
    [void][W32]::GetClientRect($h, [ref]$r)
    return @{ X = $pt.X; Y = $pt.Y; W = $r.Right; H = $r.Bottom }
}

switch ($cmd) {
    "launch" {
        $w = if ($a1) { $a1 } else { "1280" }; $h = if ($a2) { $a2 } else { "800" }
        $save = Join-Path $proj "build\realsave"
        Remove-Item $save -Recurse -Force -ErrorAction SilentlyContinue
        Start-Process -FilePath $exe -ArgumentList '-screen-width',$w,'-screen-height',$h,'-screen-fullscreen','0','-savedir',$save,'-logFile',(Join-Path $out "real.log")
        for ($i = 0; $i -lt 40; $i++) { Start-Sleep -Milliseconds 500; if (Get-Game) { break } }
        Start-Sleep -Seconds 3
        $g = Get-Game
        [void][W32]::SetForegroundWindow($g.MainWindowHandle)
        Start-Sleep -Milliseconds 500
        $o = Client-Origin $g.MainWindowHandle
        "client origin $($o.X),$($o.Y) size $($o.W)x$($o.H)"
    }
    "click" {
        $g = Get-Game; $o = Client-Origin $g.MainWindowHandle
        [void][W32]::SetForegroundWindow($g.MainWindowHandle)
        [W32]::SetCursorPos([int]($o.X + [int]$a1), [int]($o.Y + [int]$a2)) | Out-Null
        Start-Sleep -Milliseconds 120
        [W32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80
        [W32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
        "clicked $a1,$a2"
    }
    "drag" {
        $g = Get-Game; $o = Client-Origin $g.MainWindowHandle
        [void][W32]::SetForegroundWindow($g.MainWindowHandle)
        [W32]::SetCursorPos([int]($o.X + [int]$a1), [int]($o.Y + [int]$a2)) | Out-Null
        Start-Sleep -Milliseconds 100
        [W32]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
        for ($i = 1; $i -le 12; $i++) {
            $x = [int]$a1 + ([int]$a3 - [int]$a1) * $i / 12; $y = [int]$a2 + ([int]$a4 - [int]$a2) * $i / 12
            [W32]::SetCursorPos([int]($o.X + $x), [int]($o.Y + $y)) | Out-Null; Start-Sleep -Milliseconds 30
        }
        [W32]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 300
        "dragged"
    }
    "key" {
        $g = Get-Game
        [void][W32]::SetForegroundWindow($g.MainWindowHandle)
        $map = @{ Escape = "{ESC}"; H = "h"; Z = "z"; S = "s"; R = "r" }
        [System.Windows.Forms.SendKeys]::SendWait($map[$a1]); Start-Sleep -Milliseconds 300
        "key $a1"
    }
    "shot" {
        $g = Get-Game; $o = Client-Origin $g.MainWindowHandle
        $bmp = New-Object System.Drawing.Bitmap $o.W, $o.H
        $gr = [System.Drawing.Graphics]::FromImage($bmp)
        $gr.CopyFromScreen($o.X, $o.Y, 0, 0, $bmp.Size)
        $path = Join-Path $out "$a1.png"
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $gr.Dispose(); $bmp.Dispose()
        $path
    }
    "kill" { Get-Process Mahjong -ErrorAction SilentlyContinue | Stop-Process -Force; "killed" }
}
