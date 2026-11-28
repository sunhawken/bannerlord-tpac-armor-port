<#
  run_engine_sim.ps1 - bare-minimal REAL Bannerlord engine test.
  Installs the CirithSim module, launches the game straight into a Custom Battle with one test unit, waits until the engine reports the agent,
  screenshots the game window, collects the log and closes the game.

  usage:  .\run_engine_sim.ps1 [-Char cirith_test] [-Scene battle_terrain_a] [-ShotDelay 8] [-Timeout 150]
  output: enginesim\out\engine_<char>.png, enginesim\out\engine_<char>.log
#>
param([string]$Char = 'cirith_test', [string]$Scene = 'battle_terrain_a', [int]$ShotDelay = 8, [int]$Timeout = 150, [switch]$KeepOpen, [switch]$Visible, [string]$Views = '', [string]$ExtraModules = '', [string]$Pre = '', [string]$Modules = '', [switch]$Persist)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$game = 'T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord'
$mod = Join-Path $game 'Modules\CirithSim'
$bin = Join-Path $game 'bin\Win64_Shipping_Client'
$out = Join-Path $here 'out'; New-Item -ItemType Directory -Force $out | Out-Null
if (Get-Process Bannerlord -ErrorAction SilentlyContinue | Where-Object { -not $_.HasExited }) { throw 'Bannerlord is already running - close it first (this script will not kill a game it did not start).' }

# --- install (idempotent): module xml + data + dll
New-Item -ItemType Directory -Force (Join-Path $mod 'bin\Win64_Shipping_Client'), (Join-Path $mod 'ModuleData') | Out-Null
Copy-Item (Join-Path $here 'CirithSim\module\SubModule.xml') $mod -Force
Copy-Item (Join-Path $here 'CirithSim\module\ModuleData\*') (Join-Path $mod 'ModuleData') -Recurse -Force
Copy-Item (Join-Path $here 'CirithSim\bin\CirithSim.dll') (Join-Path $mod 'bin\Win64_Shipping_Client') -Force
if (Test-Path (Join-Path $here 'CirithSim\module\AssetPackages')) { New-Item -ItemType Directory -Force (Join-Path $mod 'AssetPackages') | Out-Null; Copy-Item (Join-Path $here 'CirithSim\module\AssetPackages\*') (Join-Path $mod 'AssetPackages') -Force }
Remove-Item (Join-Path $mod 'shot.png') -Force -ErrorAction SilentlyContinue; $log = Join-Path $mod 'cirithsim.log'; if (Test-Path $log) { Remove-Item $log -Force }

# --- never show the 'Safe Mode' prompt: the sim kills the game, so mark the last exit as clean before and after
$ecfg = Join-Path $env:USERPROFILE 'Documents\Mount and Blade II Bannerlord\Configs\engine_config.txt'
function Set-CleanExit { if (Test-Path $ecfg) { (Get-Content $ecfg) -replace '^safely_exited\s*=\s*0','safely_exited = 1' | Set-Content $ecfg } }
Set-CleanExit
# sim runs: no motion blur / depth of field in screenshots (the user's own engine_config.txt is restored afterwards)
$origCfg = $null
if (-not $Visible -and (Test-Path $ecfg)) { $origCfg = Get-Content $ecfg -Raw; (Get-Content $ecfg) -replace '^postfx_motion_blur\s*=\s*\d+','postfx_motion_blur = 0' -replace '^postfx_dof\s*=\s*\d+','postfx_dof = 0' -replace '^disable_sound\s*=\s*\d+','disable_sound = 1' | Set-Content $ecfg }

Add-Type @'
using System; using System.Runtime.InteropServices;
public class Dsk {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct STARTUPINFO { public int cb; public string lpReserved; public string lpDesktop; public string lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError; }
  [StructLayout(LayoutKind.Sequential)] public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr CreateDesktopW(string name, IntPtr dev, IntPtr devmode, uint flags, uint access, IntPtr sa);
  [DllImport("user32.dll")] public static extern bool CloseDesktop(IntPtr h);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
}
'@
# the game shows a modal 'Mod change detected' box (OK = delete the shader cache) whenever the active mod list differs from the last launch; on a hidden desktop nobody sees it and the boot just sits there.
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class Dlg {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr d, EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static int Dismiss(IntPtr desk) {
    int n = 0; EnumProc top = (h, l) => {
      var c = new StringBuilder(64); GetClassName(h, c, 64); if (c.ToString() != "#32770") return true;
      var t = new StringBuilder(128); GetWindowText(h, t, 128); string ts = t.ToString();
      if (ts.IndexOf("Mod change", StringComparison.OrdinalIgnoreCase) < 0 && ts.IndexOf("Safe Mode", StringComparison.OrdinalIgnoreCase) < 0) return true;
      EnumChildWindows(h, (ch, l2) => { var cc = new StringBuilder(32); GetClassName(ch, cc, 32); if (cc.ToString() == "Button") { var tt = new StringBuilder(32); GetWindowText(ch, tt, 32); if (tt.ToString().Replace("&", "") == "OK" || tt.ToString().Replace("&", "") == "Yes") { SendMessage(ch, 0x00F5, IntPtr.Zero, IntPtr.Zero); n++; return false; } } return true; }, IntPtr.Zero);
      return true; };
    if (desk == IntPtr.Zero) EnumWindows(top, IntPtr.Zero); else EnumDesktopWindows(desk, top, IntPtr.Zero);
    return n;
  }
}
'@
if ($Views) { $env:CIRITH_VIEWS = (Resolve-Path $Views).Path } else { $env:CIRITH_VIEWS = '' }
Remove-Item (Join-Path $mod 'shots') -Recurse -Force -ErrorAction SilentlyContinue
# --- launch
$env:CIRITH_CHAR = $Char; $env:CIRITH_SCENE = $Scene
# live play (-Visible): StuffedHole's Enhanced Third Person camera + its requirements (Harmony, ButterLib, UIExtenderEx, MCM) are loaded too when installed in Modules
$camMod = Join-Path $game 'Modules\StuffedHoleEnhancedThirdPerson'
$pre = $Pre
if ($Visible -and -not $ExtraModules -and (Test-Path $camMod)) { $pre = 'Bannerlord.Harmony*Bannerlord.ButterLib*Bannerlord.UIExtenderEx*Bannerlord.MBOptionScreen*'; $ExtraModules = 'StuffedHoleEnhancedThirdPerson' }
$mods = if ($ExtraModules) { $pre + 'Native*SandBoxCore*Sandbox*StoryMode*CustomBattle*' + $ExtraModules + '*CirithSim' } else { 'Native*SandBoxCore*CustomBattle*CirithSim' }
if ($Modules) { $mods = $Modules }
$args = "/singleplayer _MODULES_*$mods*_MODULES_"
if ($Visible) {
  $env:CIRITH_CHAR = $Char; $env:CIRITH_SCENE = $Scene; $env:CIRITH_NOCAM = '1'; $env:CIRITH_RUNTIME = '1'; $env:CIRITH_LIVE = '1'
  if ($Persist) {
    # persistent quiet sim: boots ONCE on the normal desktop, the window is pushed off-screen as soon as it exists and focus goes back to whatever you were using; talk to it with simctl.ps1
    $env:CIRITH_SIMCMD = '1'; Remove-Item (Join-Path $mod 'simready.txt') -Force -ErrorAction SilentlyContinue
    Add-Type @'
using System; using System.Runtime.InteropServices;
public class WP {
  [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int i); [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int i, int v); [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(IntPtr h, uint k, byte a, uint f);
  // invisible but 'on screen' (the engine keeps rendering and ticking): layered + click-through + tool window (no taskbar button), alpha 1/255
  public static void Ghost(IntPtr h) { int ex = GetWindowLong(h, -20); SetWindowLong(h, -20, ex | 0x80000 | 0x20 | 0x80 | 0x08000000); SetLayeredWindowAttributes(h, 0, 1, 2); }
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
'@
    $fg = [WP]::GetForegroundWindow()
    $p = Start-Process -FilePath (Join-Path $bin 'Bannerlord.exe') -ArgumentList $args -WorkingDirectory $bin -PassThru
    $sw = [Diagnostics.Stopwatch]::StartNew(); $moved = $false
    while ($sw.Elapsed.TotalSeconds -lt 900 -and -not $p.HasExited) {
      Start-Sleep -Milliseconds 400; $p.Refresh(); if ([Dlg]::Dismiss([IntPtr]::Zero) -gt 0) { Write-Host 'dismissed the Mod change detected box' }
      if ($p.MainWindowHandle -ne [IntPtr]::Zero -and $p.MainWindowTitle -like 'Mount and Blade*') { [WP]::Ghost($p.MainWindowHandle); if (-not $moved) { [void][WP]::SetForegroundWindow($fg); $moved = $true } }
      if (Test-Path (Join-Path $mod 'simready.txt')) { Write-Host ("persistent sim ready after {0:0} s (pid {1}). Use: .\simctl.ps1 'state'" -f $sw.Elapsed.TotalSeconds, $p.Id); return }
    }
    Write-Host 'persistent sim did NOT become ready (see Modules\CirithSim\cirithsim.log)'; return
  }
  $p = Start-Process -FilePath (Join-Path $bin 'Bannerlord.exe') -ArgumentList $args -WorkingDirectory $bin -PassThru
  Write-Host "launched VISIBLE Bannerlord pid $($p.Id) - it will boot straight into the battle with $Char (free camera); close the game window when done"
  return
}
if ($Persist -and -not $Visible) { $env:CIRITH_SIMCMD = '1'; $env:CIRITH_RUNTIME = '1'; $env:CIRITH_LIVE = '1'; Remove-Item (Join-Path $mod 'simready.txt') -Force -ErrorAction SilentlyContinue }
# run the game on its OWN hidden desktop: no window on the user's desktop, and it cannot grab the user's mouse/keyboard
$deskName = "CirithSimDesk_" + [guid]::NewGuid().ToString('N').Substring(0,6)
$desk = [Dsk]::CreateDesktopW($deskName, [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x000F01FF, [IntPtr]::Zero)
if ($desk -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$si = New-Object Dsk+STARTUPINFO; $si.cb = [Runtime.InteropServices.Marshal]::SizeOf($si); $si.lpDesktop = $deskName
$pi = New-Object Dsk+PROCESS_INFORMATION
$exe = Join-Path $bin 'Bannerlord.exe'
if (-not [Dsk]::CreateProcess($exe, "`"$exe`" $args", [IntPtr]::Zero, [IntPtr]::Zero, $false, 0, [IntPtr]::Zero, $bin, [ref]$si, [ref]$pi)) { throw "CreateProcess failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$p = Get-Process -Id $pi.dwProcessId
Write-Host "launched Bannerlord pid $($p.Id)"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public class W { [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; } }
'@
$hidden = $false; if ($Views) { $shot = $true }; $sw = [Diagnostics.Stopwatch]::StartNew(); if (-not $Views) { $shot = $false } ; $found = $null
while ($sw.Elapsed.TotalSeconds -lt $Timeout -and -not $p.HasExited) {
  Start-Sleep -Milliseconds 300
  if ([Dlg]::Dismiss($desk) -gt 0) { Write-Host 'dismissed the Mod change detected box' }
  if ($Persist -and (Test-Path (Join-Path $mod 'simready.txt'))) { if ($origCfg) { Set-Content $ecfg -Value $origCfg -NoNewline }; Set-CleanExit; Write-Host ('persistent sim ready after {0:0} s (pid {1}, hidden desktop). Use: .\simctl.ps1 state' -f $sw.Elapsed.TotalSeconds, $p.Id); return }
  Get-Process opera_crashreporter -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue      # auto-dismiss the game's crash dialog (it would otherwise pop up on the user's screen)
  # headless-style: the moment the game window exists, push it off-screen and hide it from the taskbar (rendering + in-engine screenshots keep working)
  if (-not $hidden) { $p.Refresh(); if ($p.MainWindowHandle -ne [IntPtr]::Zero) { [void][W]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, -32000, -32000, 0, 0, 0x0001 -bor 0x0004 -bor 0x0010); [void][W]::ShowWindow($p.MainWindowHandle, 0); $hidden = $true } }
  $txt = if (Test-Path $log) { Get-Content $log -Raw -ErrorAction SilentlyContinue } else { '' }
  if (-not $found -and $txt -match 'agent found') { $found = [Diagnostics.Stopwatch]::StartNew(); Write-Host 'engine: agent spawned' }
  if ($found -and -not $shot -and $found.Elapsed.TotalSeconds -gt $ShotDelay -and (Test-Path (Join-Path $mod 'shot.png'))) { $shot = $true; if (-not $KeepOpen) { Start-Sleep -Seconds 1; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue } }
  if ($false) {
    $p.Refresh(); $h = $p.MainWindowHandle
    if ($h -ne [IntPtr]::Zero) {
      [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 600
      $r = New-Object W+RECT; [void][W]::GetWindowRect($h, [ref]$r)
      $w = $r.R - $r.L; $hh = $r.B - $r.T
      $bmp = New-Object Drawing.Bitmap $w, $hh; $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
      $png = Join-Path $out "engine_$Char.png"; $bmp.Save($png, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
      Write-Host "screenshot: $png ($w x $hh)"; $shot = $true
      if (-not $KeepOpen) { Start-Sleep -Seconds 1; Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
  }
}
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue; Write-Host "timeout after $Timeout s - killed" }
Start-Sleep -Milliseconds 500; [void][Dsk]::CloseDesktop($desk); if ($origCfg) { Set-Content $ecfg -Value $origCfg -NoNewline }; Set-CleanExit
if (Test-Path (Join-Path $mod 'shots')) { New-Item -ItemType Directory -Force (Join-Path $out 'shots') | Out-Null; Get-ChildItem (Join-Path $mod 'shots\*.bmp') | ForEach-Object { python -c "from PIL import Image; Image.open(r'$($_.FullName)').convert('RGB').save(r'$out\shots\$($_.BaseName).png')" }; Write-Host "shots -> $out\shots" }
if (Test-Path (Join-Path $mod 'shot.png')) { Copy-Item (Join-Path $mod 'shot.png') (Join-Path $out "engine_$Char.bmp") -Force; python -c "from PIL import Image; Image.open(r'$out\engine_$Char.bmp').save(r'$out\engine_$Char.png')" }
if (Test-Path $log) { Copy-Item $log (Join-Path $out "engine_$Char.log") -Force; Get-Content $log | Select-Object -Last 25 } else { Write-Host 'NO LOG WRITTEN - module did not load' }
Get-Process opera_crashreporter -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
