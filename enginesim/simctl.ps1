<#
  simctl.ps1 - talk to the persistent sim (run_engine_sim.ps1 -Visible -Persist ...).
  usage:  .\simctl.ps1 'state'
          .\simctl.ps1 'script on; wield; bring 1.4; freeze; attack right 1.2; wait 1.5; hp; state'
  commands: ping | state | script [on|off] | cam az el dist h | camoff | shot name [az el dist h] | move forward|back|left|right sec | attack right|left|up|down sec | block sec | stop | jump |
            sprint [off] | wield [off] | give | voice <Type> | bring dist | freeze [n] | heal | hp | reload | log text | quit      (client-side: wait <seconds>)
  screenshots land in enginesim\out\shots\<name>.png
#>
param([Parameter(Mandatory = $true, Position = 0)][string]$Cmd, [int]$Timeout = 20)
$game = 'T:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\CirithSim'
$cf = Join-Path $game 'simcmd.txt'; $of = Join-Path $game 'simout.txt'; $here = Split-Path -Parent $MyInvocation.MyCommand.Path; $out = Join-Path $here 'out\shots'; New-Item -ItemType Directory -Force $out | Out-Null
if (-not (Get-Process Bannerlord -ErrorAction SilentlyContinue)) { Write-Host 'Bannerlord is not running - start it with: .\run_engine_sim.ps1 -Visible -Persist -Char cirith_w_main'; exit 2 }
$n = 0
foreach ($c in ($Cmd -split ';')) {
  $c = $c.Trim(); if (-not $c) { continue }
  $w = $c -split '\s+'
  if ($w[0] -eq 'wait') { Start-Sleep -Milliseconds ([int]([double]$w[1] * 1000)); continue }
  $id = 'c' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + '_' + $n++
  $t0 = Get-Date; while ((Test-Path $cf) -and ((Get-Date) - $t0).TotalSeconds -lt 5) { Start-Sleep -Milliseconds 50 }
  Set-Content -Path $cf -Value "$id $c" -Encoding ASCII
  $res = $null; $t0 = Get-Date
  while (((Get-Date) - $t0).TotalSeconds -lt $Timeout) {
    Start-Sleep -Milliseconds 100
    if (Test-Path $of) { $m = Select-String -Path $of -Pattern ('^' + $id + ' ') -ErrorAction SilentlyContinue | Select-Object -Last 1; if ($m) { $res = $m.Line; break } }
  }
  if (-not $res) { Write-Host "[$c] TIMEOUT (no answer in $Timeout s)"; continue }
  $txt = $res.Substring($id.Length + 1); Write-Host "[$c] $txt"
  if ($w[0] -eq 'shot') {      # the engine writes the bmp a frame later: wait for it and convert
    $bmp = Join-Path $game ('shots\' + $w[1] + '.bmp'); $t1 = Get-Date; while (-not (Test-Path $bmp) -and ((Get-Date) - $t1).TotalSeconds -lt 10) { Start-Sleep -Milliseconds 100 }
    Start-Sleep -Milliseconds 300; if (Test-Path $bmp) { python -c "from PIL import Image; Image.open(r'$bmp').convert('RGB').save(r'$out\$($w[1]).png')"; Write-Host "   -> $out\$($w[1]).png" }
  }
}
