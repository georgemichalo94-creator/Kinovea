# sh_ci.ps1: the CI check of the steady stroke rate of the video graphics (2.33.0) on a FICTIONAL 100 m butterfly (50 m pool). The
# strokes are the automatic ones, their count confirmed or corrected by the analyst at each checkpoint (S); a stroke read 0.30 s before
# its frame made one cycle 0.86 s and the next 1.46 s around 1.16 (70 and 41 cycles a minute on the card of the video around 52): the
# card and the chart show for each cycle the median of it and the cycles beside it in the length. In a segment of clean swimming whose
# count the analyst corrected (an automatic stroke missed), the rate and the cycle length of the segment, as the analysis and the PDF.
# What the analysis counts does not change. In freestyle (two presses a cycle) the same.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File sh_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
$a = [System.Reflection.Assembly]::LoadFrom($sm)
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$tRace = T 'SwimRace'; $tMark = T 'SwimMark'; $tType = T 'SwimMarkType'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'
$tPresets = T 'SwimPresets'; $tReel = T 'SwimReel'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function F2($v) { return ([double]$v).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) }

# 50 fps, a timestamp = 1 ms; the start signal at 1 s of the video. Every time below is on a frame (a multiple of 0.02 s).
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
function Mark($race, $type, $seconds, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
$splits = @{ 15 = 6.0; 25 = 11.6; 35 = 17.5; 45 = 23.4; 50 = 26.2; 65 = 33.6; 75 = 39.5; 85 = 45.6; 95 = 51.8; 100 = 55.0 }
# Strokes from each breakout to the wall every $cycle s ($presses a cycle); the stroke at $shiftAt moved by $shift; the one at $dropAt left out.
function NewRace($style, $cycle, $presses, $shiftAt, $shift, $dropAt) {
  $race = [Activator]::CreateInstance($tRace)
  $race.Style = $style; $race.Distance = 100; $race.Pool = 50
  $race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(100, 50, $false))
  $race.StartTimestamp = [long]1000
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  Mark $race 'Breakout' 4.6 $null
  Mark $race 'Breakout' 30.0 $null
  $step = $cycle / $presses
  $times = @()
  for ($t = 4.9; $t -lt 26.0; $t += $step) { $times += $t }
  for ($t = 30.3; $t -lt 54.8; $t += $step) { $times += $t }
  foreach ($t in $times) {
    if ($null -ne $dropAt -and [Math]::Abs($t - $dropAt) -lt 1e-6) { continue }
    $x = $t
    if ($null -ne $shiftAt -and [Math]::Abs($t - $shiftAt) -lt 1e-6) { $x = $t + $shift }
    Mark $race 'Stroke' $x $null
  }
  return $race
}
function Compute($race) { return $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5)) }
function Reel($race, $computed) { return $tReel.GetMethod('Build').Invoke($null, @($race, $computed)) }
function Row($computed, $d) { foreach ($row in $computed.Rows) { if ($row.D -eq $d) { return $row } }; return $null }

# --- Butterfly, 1.16 s a cycle (58 frames), the stroke of 37.26 s read at 36.96 (0.30 s before).
$step = 1.16
$grid = @(); for ($t = 30.3; $t -lt 54.8; $t += $step) { $grid += $t }
$at = $grid[6]
$race = NewRace 'fly' $step 1 $at -0.30 $null
$c = Compute $race
$clean = Compute (NewRace 'fly' $step 1 $null 0 $null)
$sum = 0; foreach ($row in $c.Rows) { if ($row.Strokes) { $sum += $row.Strokes } }
$sum0 = 0; foreach ($row in $clean.Rows) { if ($row.Strokes) { $sum0 += $row.Strokes } }
Check ($sum -eq $sum0 -and $sum -gt 30) ('the strokes counted do not change: ' + $sum + ' / ' + $sum0)
$reel = Reel $race $c
$short = $null; $long = $null; $before = $null
foreach ($cy in $reel.Cycles) {
  if ([Math]::Abs($cy.Time - ($at - 0.30)) -lt 0.002) { $short = $cy }
  if ([Math]::Abs($cy.Start - ($at - 0.30)) -lt 0.002) { $long = $cy }
}
foreach ($cy in $reel.Cycles) { if ($null -ne $short -and [Math]::Abs($cy.Time - $short.Start) -lt 0.002) { $before = $cy } }
Check ($null -ne $short -and [Math]::Abs($short.Tempo - 0.86) -lt 0.002 -and [Math]::Abs($short.ShownTempo - 1.16) -lt 0.002) ('the short cycle: ' + (F2 (60 / $short.Tempo)) + ' cycles a minute from the strokes, ' + (F2 (60 / $short.ShownTempo)) + ' shown')
Check ($null -ne $long -and [Math]::Abs($long.Tempo - 1.46) -lt 0.002 -and [Math]::Abs($long.ShownTempo - 1.16) -lt 0.002) ('the long cycle: ' + (F2 (60 / $long.Tempo)) + ' from the strokes, ' + (F2 (60 / $long.ShownTempo)) + ' shown')
Check ($null -ne $before -and [Math]::Abs($short.ShownDps - $before.Dps) -lt 0.08 -and [Math]::Abs($long.ShownDps - $before.Dps) -lt 0.08) ('their distance shown: ' + (F2 $short.ShownDps) + ' / ' + (F2 $long.ShownDps) + ' m (from the strokes ' + (F2 $short.Dps) + ' / ' + (F2 $long.Dps) + ', the cycle before ' + (F2 $before.Dps) + ')')
$rates = @($reel.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.ShownTempo })
$lo = ($rates | Measure-Object -Minimum).Minimum; $hi = ($rates | Measure-Object -Maximum).Maximum
Check ($rates.Count -gt 10 -and $hi - $lo -lt 0.5) ('the second length on the card from ' + (F2 $lo) + ' to ' + (F2 $hi) + ' cycles a minute (from the strokes 41.1 to 69.8)')
$reel0 = Reel (NewRace 'fly' $step 1 $null 0 $null) $clean
# (The distance of a cycle on the curve changes a little from one to the next, most after a breakout: within 0.15 m of its own.)
$same = @($reel0.Cycles | Where-Object { [Math]::Abs($_.ShownTempo - $_.Tempo) -gt 1e-9 -or [Math]::Abs($_.ShownDps - $_.Dps) -gt 0.15 })
Check ($reel0.Cycles.Count -gt 20 -and $same.Count -eq 0) ('steady strokes are shown as they are (' + $same.Count + ' apart)')

# --- An automatic stroke missed in 65-75 m and the count corrected at 75 m (S): its cycles as the segment in the analysis and the PDF.
$miss = $grid[5]
$race2 = NewRace 'fly' $step 1 $null 0 $miss
$c2a = Compute $race2
$row2a = Row $c2a 75
$race2.StrokeCounts[75] = [double]($row2a.Strokes + 1)
$c2 = Compute $race2
$row2 = Row $c2 75
Check ($row2.StrokesTyped -and -not $row2.SrFromTimes -and $row2.Sr -gt 0) ('the corrected segment: ' + $row2.Strokes + ' strokes (' + $row2a.Strokes + ' from the times), ' + (F2 $row2.Sr) + ' cycles a minute')
$reel2 = Reel $race2 $c2
$in = @($reel2.Cycles | Where-Object { $_.Time -gt 33.6 + 1e-6 -and $_.Time -le 39.5 + 1e-6 })
$off = @($in | Where-Object { [Math]::Abs(60 / $_.ShownTempo - $row2.Sr) -gt 1e-6 -or [Math]::Abs($_.ShownDps - $row2.Sl) -gt 1e-6 })
Check ($in.Count -ge 3 -and $off.Count -eq 0) ('its cycles on the card at ' + (F2 $row2.Sr) + ' cycles a minute and ' + (F2 $row2.Sl) + ' m (' + $in.Count + ' cycles, ' + $off.Count + ' apart)')
$gap = @($reel2.Cycles | Where-Object { [Math]::Abs($_.Tempo - 2 * $step) -lt 0.002 })
Check ($gap.Count -eq 1 -and [Math]::Abs(60 / $gap[0].ShownTempo - $row2.Sr) -lt 1e-6) ('the double cycle of the missed stroke shown at the rate of the segment, not ' + (F2 (60 / (2 * $step))))
$out = @($reel2.Cycles | Where-Object { $_.Time -gt 39.5 + 0.5 -and $_.Lap -eq 1 -and [Math]::Abs($_.ShownTempo - $step) -gt 0.002 })
Check ($out.Count -eq 0) ('the other segments as their strokes (' + $out.Count + ' apart)')

# --- Freestyle, 1.32 s a cycle (two presses 0.66 s apart), one stroke read 0.26 s after its frame.
$step2 = 0.66
$grid2 = @(); for ($t = 30.3; $t -lt 54.8; $t += $step2) { $grid2 += $t }
$at2 = $grid2[12]
$race3 = NewRace 'fs' 1.32 2 $at2 0.26 $null
$c3 = Compute $race3
$reel3 = Reel $race3 $c3
$rates3 = @($reel3.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.ShownTempo })
$lo3 = ($rates3 | Measure-Object -Minimum).Minimum; $hi3 = ($rates3 | Measure-Object -Maximum).Maximum
$raw3 = @($reel3.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.Tempo })
$rlo = ($raw3 | Measure-Object -Minimum).Minimum; $rhi = ($raw3 | Measure-Object -Maximum).Maximum
Check ($hi3 - $lo3 -lt 0.5 -and $rhi - $rlo -gt 8) ('freestyle on the card from ' + (F2 $lo3) + ' to ' + (F2 $hi3) + ' cycles a minute (from the strokes ' + (F2 $rlo) + ' to ' + (F2 $rhi) + ')')

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'Steady stroke rate of the video graphics: all checks passed.'
