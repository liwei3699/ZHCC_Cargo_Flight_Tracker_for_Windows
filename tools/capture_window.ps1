param(
    [string]$OutFile = 'D:\AI\vibe\ZHCC\out\win1.png',
    [string]$ProcessName = 'ZhccCargoBoard.App',
    [string]$TitleMatch = ''
)

$src = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinCap
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    public static List<IntPtr> Find(uint pid, string titlePart)
    {
        var found = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowText(h, sb, sb.Capacity);
            var title = sb.ToString();
            if (title.Length == 0) return true;
            if (titlePart.Length > 0 && title.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) < 0) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
if (-not ('WinCap' -as [type])) { Add-Type -TypeDefinition $src }
Add-Type -AssemblyName System.Drawing
[WinCap]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$proc = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
$handles = [WinCap]::Find([uint32]$proc.Id, $TitleMatch)
if ($handles.Count -eq 0) { throw "未找到匹配窗口：$TitleMatch" }
$h = $handles[$handles.Count - 1]

$r = New-Object WinCap+RECT
[WinCap]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left
$ht = $r.Bottom - $r.Top
if ($w -le 0 -or $ht -le 0) { throw "窗口尺寸无效：${w}x${ht}" }

$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [WinCap]::PrintWindow($h, $hdc, 2)
$g.ReleaseHdc($hdc)
$g.Dispose()
$bmp.Save($OutFile, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
"hwnd=$h size=${w}x${ht} ok=$ok out=$OutFile"
