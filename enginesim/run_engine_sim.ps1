<#
  run_engine_sim.ps1 - bare-minimal REAL Bannerlord engine test.
  Installs the CirithSim module, launches the game straight into a Custom Battle with one test unit, waits until the engine reports the agent,
  screenshots the game window, collects the log and closes the game.

  usage:  .\run_engine_sim.ps1 [-Char cirith_test] [-Scene battle_terrain_a] [-ShotDelay 8] [-Timeout 150]
  output: enginesim\out\engine_<char>.png, enginesim\out\engine_<char>.log
#>
param([string]$Char = 'cirith_test', [string]$Scene = 'battle_terrain_a', [int]$ShotDelay = 8, [int]$Timeout = 150, [switch]$KeepOpen, [switch]$Visible, [string]$Views = '')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$game = 'S:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord'
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
if (-not $Visible -and (Test-Path $ecfg)) { $origCfg = Get-Content $ecfg -Raw; (Get-Content $ecfg) -replace '^postfx_motion_blur\s*=\s*\d+','postfx_motion_blur = 0' -replace '^postfx_dof\s*=\s*\d+','postfx_dof = 0' | Set-Content $ecfg }

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
if ($Views) { $env:CIRITH_VIEWS = (Resolve-Path $Views).Path } else { $env:CIRITH_VIEWS = '' }
Remove-Item (Join-Path $mod 'shots') -Recurse -Force -ErrorAction SilentlyContinue
# --- launch
$env:CIRITH_CHAR = $Char; $env:CIRITH_SCENE = $Scene
$args = '/singleplayer _MODULES_*Native*SandBoxCore*CustomBattle*CirithSim*_MODULES_'
if ($Visible) {
  $env:CIRITH_CHAR = $Char; $env:CIRITH_SCENE = $Scene; $env:CIRITH_NOCAM = '1'
  $p = Start-Process -FilePath (Join-Path $bin 'Bannerlord.exe') -ArgumentList $args -WorkingDirectory $bin -PassThru
  Write-Host "launched VISIBLE Bannerlord pid $($p.Id) - it will boot straight into the battle with $Char (free camera); close the game window when done"
  return
}
# run the game on its OWN hidden desktop: no window on the user's desktop, and it cannot grab the user's mouse/keyboard
$desk = [Dsk]::CreateDesktopW('CirithSimDesk', [IntPtr]::Zero, [IntPtr]::Zero, 0, 0x000F01FF, [IntPtr]::Zero)
if ($desk -eq [IntPtr]::Zero) { throw "CreateDesktop failed: $([Runtime.InteropServices.Marshal]::GetLastWin32Error())" }
$si = New-Object Dsk+STARTUPINFO; $si.cb = [Runtime.InteropServices.Marshal]::SizeOf($si); $si.lpDesktop = 'CirithSimDesk'
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
