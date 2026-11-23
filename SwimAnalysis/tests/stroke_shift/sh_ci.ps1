# sh_ci.ps1: the CI check that the video graphics show the numbers of the analysis (2.34.0; the steady stroke rate of 2.33.0 before it)
# on a FICTIONAL 100 m butterfly (50 m pool). In the clean swimming the card, the bars of the chart and the summary show the rate, the
# cycle length and the speed of each segment as the analysis and the PDF have them (speed = rate × length ÷ 60, the length from the
# speed and the rate as the Apps Script from 2.19.0 on; with the script before it the metres over the strokes, also the same number on
# the video and in the PDF). A stroke read 0.30 s before its frame made one cycle 0.86 s and the next 1.46 s around 1.16 (70 and 41
# cycles a minute on the card around 52): the card shows the rate of the segment. Outside the clean swimming (the first 15 m, after the
# turn, the last 5 m) the analysis has no rate: the cycles there show the median of them and the cycles beside them. The counter of the
# video ends where the analysis does: a count typed by the analyst (an automatic stroke missed, or one too many) puts a stroke on the
# video or leaves one out. In freestyle (two presses a cycle) the same.
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
$fromRate = $tCalc.GetField('LengthFromRate')
function LengthFromRate($on) { $fromRate.SetValue($null, [bool]$on) }

# 50 fps, a timestamp = 1 ms; the start signal at 1 s of the video. Every time below is on a frame (a multiple of 0.02 s).
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
function Mark($race, $type, $seconds, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
$splits = @{ 15 = 6.0; 25 = 11.6; 35 = 17.5; 45 = 23.4; 50 = 26.2; 65 = 33.6; 75 = 39.5; 85 = 45.6; 95 = 51.8; 100 = 55.0 }
# Strokes from each breakout to the wall every $cycle s ($presses a cycle); the stroke at $shiftAt moved by $shift; the one at $dropAt left
# out; one more at $extraAt.
function NewRace($style, $cycle, $presses, $shiftAt, $shift, $dropAt, $extraAt) {
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
  if ($null -ne $extraAt) { Mark $race 'Stroke' $extraAt $null }
  return $race
}
function Compute($race) { return $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5)) }
function Reel($race, $computed) { return $tReel.GetMethod('Build').Invoke($null, @($race, $computed)) }
function Row($computed, $d) { foreach ($row in $computed.Rows) { if ($row.D -eq $d) { return $row } }; return $null }
# The segment of clean swimming a cycle ends in (as the reel puts it), $null outside the clean swimming.
function CleanOf($reel, $cycle) { foreach ($c in $reel.Clean) { if ($cycle.Time -gt $c.Start + 1e-6 -and $cycle.Time -le $c.End + 1e-6) { return $c } }; return $null }

# ================= With the Apps Script 2.19.0: the length from the speed and the rate.
LengthFromRate $true

# --- Butterfly, 1.16 s a cycle (58 frames), steady strokes: every segment of clean swimming as the analysis has it.
$step = 1.16
$grid = @(); for ($t = 30.3; $t -lt 54.8; $t += $step) { $grid += $t }
$race0 = NewRace 'fly' $step 1 $null 0 $null $null
$clean = Compute $race0
$reel0 = Reel $race0 $clean
Check ($reel0.Clean.Count -eq 6) ('six segments of clean swimming on the video: ' + $reel0.Clean.Count)
$apart = 0; $inClean = 0; $identity = 0
foreach ($cy in $reel0.Cycles) {
  $seg = CleanOf $reel0 $cy
  if ($null -eq $seg) {
    if ($cy.FromAnalysis -or [Math]::Abs($cy.ShownTempo - $cy.Tempo) -gt 1e-9) { $apart++ }
    continue
  }
  $inClean++
  $row = Row $clean $seg.To
  if (-not $cy.FromAnalysis -or [Math]::Abs(60 / $cy.ShownTempo - $row.Sr) -gt 1e-9 -or [Math]::Abs($cy.ShownDps - $row.Sl) -gt 1e-9) { $apart++ }
  if ([Math]::Abs($seg.Rate - $row.Sr) -gt 1e-9 -or [Math]::Abs($seg.Length - $row.Sl) -gt 1e-9 -or [Math]::Abs($seg.Speed - $row.Vel) -gt 1e-9) { $apart++ }
  if ([Math]::Abs($row.Vel - $row.Sr * $row.Sl / 60) -gt 1e-9) { $identity++ }
}
Check ($inClean -ge 24 -and $apart -eq 0) ('the cycles of the clean swimming show the rate and the length of their segment as the analysis (' + $inClean + ' cycles, ' + $apart + ' apart)')
Check ($identity -eq 0) ('speed = rate × length ÷ 60 in every one of them (' + $identity + ' apart)')

# --- At a moment of the video: the numbers of the card.
$row35 = Row $clean 35
$at = $reel0.CleanAt([double]14.0)
Check ($null -ne $at -and $at.From -eq 25 -and $at.To -eq 35 -and [Math]::Abs($at.Rate - $row35.Sr) -lt 1e-9 -and [Math]::Abs($at.Length - $row35.Sl) -lt 1e-9) ('at 14.0 s the card shows 25-35 m: ' + (F2 $at.Rate) + ' cycles a minute, ' + (F2 $at.Length) + ' m')
$measured = $false
$speed = $reel0.ShownSpeed([double]14.0, [ref]$measured)
Check ($measured -and [Math]::Abs($speed - $row35.Vel) -lt 1e-9 -and [Math]::Abs($speed - $at.Rate * $at.Length / 60) -lt 1e-9) ('and the speed of the segment: ' + (F2 $speed) + ' m/s = rate × length ÷ 60')
$next = $reel0.CleanAt([double]17.5)
Check ($null -ne $next -and $next.From -eq 35) ('from the frame of the 35 m the next segment')
Check ($null -eq $reel0.CleanAt([double]24.0) -and $null -eq $reel0.CleanAt([double]3.0) -and $null -eq $reel0.CleanAt([double]31.0)) 'no numbers of the analysis in the last 5 m, in the first 15 m and after the turn'
$measured = $true
$curve = $reel0.ShownSpeed([double]24.0, [ref]$measured)
Check (-not $measured -and [Math]::Abs($curve - $reel0.SpeedAt([double]24.0)) -lt 1e-9) ('there the speed of the curve (an estimate): ' + (F2 $curve))

# --- The summary: each length as the «Ανά 50» of the report.
$okParts = 0
foreach ($p in $reel0.Parts) {
  $block = $null; foreach ($b in $clean.Blocks) { if ($b.From -eq $p.From -and $b.To -eq $p.To) { $block = $b } }
  if ($null -ne $block -and $p.FromAnalysis -and [Math]::Abs(60 / $p.Tempo - $block.Sr) -lt 1e-9 -and [Math]::Abs($p.Dps - $block.Sl) -lt 1e-9) { $okParts++ }
}
Check ($reel0.Parts.Count -eq 2 -and $okParts -eq 2) ('the two lengths of the summary with the rate and the cycle length of their 50 in the analysis (' + $okParts + ' of ' + $reel0.Parts.Count + ')')

# --- The stroke of 37.26 s read at 36.96 (0.30 s before).
$at = $grid[6]
$race = NewRace 'fly' $step 1 $at -0.30 $null $null
$c = Compute $race
$sum = 0; foreach ($row in $c.Rows) { if ($row.Strokes) { $sum += $row.Strokes } }
$sum0 = 0; foreach ($row in $clean.Rows) { if ($row.Strokes) { $sum0 += $row.Strokes } }
Check ($sum -eq $sum0 -and $sum -gt 30) ('the strokes counted do not change: ' + $sum + ' / ' + $sum0)
$reel = Reel $race $c
$row75 = Row $c 75
$short = $null; $long = $null
foreach ($cy in $reel.Cycles) {
  if ([Math]::Abs($cy.Time - ($at - 0.30)) -lt 0.002) { $short = $cy }
  if ([Math]::Abs($cy.Start - ($at - 0.30)) -lt 0.002) { $long = $cy }
}
Check ($null -ne $short -and [Math]::Abs($short.Tempo - 0.86) -lt 0.002 -and [Math]::Abs(60 / $short.ShownTempo - $row75.Sr) -lt 1e-9 -and [Math]::Abs($short.ShownTempo - 1.16) -lt 0.002) ('the short cycle: ' + (F2 (60 / $short.Tempo)) + ' cycles a minute from the strokes, ' + (F2 (60 / $short.ShownTempo)) + ' shown, the rate of 65-75 m in the analysis')
Check ($null -ne $long -and [Math]::Abs($long.Tempo - 1.46) -lt 0.002 -and [Math]::Abs(60 / $long.ShownTempo - $row75.Sr) -lt 1e-9) ('the long cycle: ' + (F2 (60 / $long.Tempo)) + ' from the strokes, ' + (F2 (60 / $long.ShownTempo)) + ' shown')
Check ([Math]::Abs($short.ShownDps - $row75.Sl) -lt 1e-9 -and [Math]::Abs($long.ShownDps - $row75.Sl) -lt 1e-9) ('their distance shown: ' + (F2 $short.ShownDps) + ' m, the cycle length of the segment')
$rates = @($reel.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.ShownTempo })
$lo = ($rates | Measure-Object -Minimum).Minimum; $hi = ($rates | Measure-Object -Maximum).Maximum
Check ($rates.Count -gt 10 -and $hi - $lo -lt 0.5) ('the second length on the card from ' + (F2 $lo) + ' to ' + (F2 $hi) + ' cycles a minute (from the strokes 41.1 to 69.8)')

# --- An automatic stroke missed in 65-75 m and the count corrected at 75 m (S): its cycles as the segment in the analysis and the PDF,
# and a stroke on the video where it is missing (the counter ends where the analysis does).
$miss = $grid[5]
$race2 = NewRace 'fly' $step 1 $null 0 $miss $null
$c2a = Compute $race2
$row2a = Row $c2a 75
$race2.StrokeCounts[75] = [double]($row2a.Strokes + 1)
$c2 = Compute $race2
$row2 = Row $c2 75
Check ($row2.StrokesTyped -and -not $row2.SrFromTimes -and $row2.Sr -gt 0) ('the corrected segment: ' + $row2.Strokes + ' strokes (' + $row2a.Strokes + ' from the times), ' + (F2 $row2.Sr) + ' cycles a minute')
$reel2 = Reel $race2 $c2
$in = @($reel2.Cycles | Where-Object { $_.Time -gt 33.6 + 1e-6 -and $_.Time -le 39.5 + 1e-6 })
$off = @($in | Where-Object { [Math]::Abs(60 / $_.ShownTempo - $row2.Sr) -gt 1e-6 -or [Math]::Abs($_.ShownDps - $row2.Sl) -gt 1e-6 })
Check ($in.Count -ge 4 -and $off.Count -eq 0) ('its cycles on the card at ' + (F2 $row2.Sr) + ' cycles a minute and ' + (F2 $row2.Sl) + ' m (' + $in.Count + ' cycles, ' + $off.Count + ' apart)')
$gap = @($reel2.Cycles | Where-Object { [Math]::Abs($_.Tempo - 2 * $step) -lt 0.002 })
Check ($gap.Count -eq 0) ('no double cycle on the video where the stroke was missed (' + $gap.Count + ')')
$lap2 = 0; foreach ($d in @(65, 75, 85, 95, 100)) { $lap2 += (Row $c2 $d).Strokes }
$counter2 = $reel2.StrokesInLength([double]54.99)
Check ($counter2 -eq $lap2 -and $counter2 -eq $reel0.StrokesInLength([double]54.99)) ('the counter of the video at the finish: ' + $counter2 + ', the strokes of the length in the analysis: ' + $lap2)
Check ($reel2.StrokesInLength([double]34.0) -eq $reel0.StrokesInLength([double]34.0)) 'before the missed stroke the counter as it was'

# --- An automatic stroke too many in 65-75 m (half a cycle after the stroke of 36.10 s) and the count corrected: one left out on the video.
$race3 = NewRace 'fly' $step 1 $null 0 $null ($grid[5] + 0.58)
$c3a = Compute $race3
$row3a = Row $c3a 75
$race3.StrokeCounts[75] = [double]($row3a.Strokes - 1)
$c3 = Compute $race3
$reel3 = Reel $race3 $c3
$lap3 = 0; foreach ($d in @(65, 75, 85, 95, 100)) { $lap3 += (Row $c3 $d).Strokes }
$counter3 = $reel3.StrokesInLength([double]54.99)
Check ((Row $c3 75).StrokesTyped -and $counter3 -eq $lap3 -and $counter3 -eq $reel0.StrokesInLength([double]54.99)) ('a stroke too many, the count corrected: the counter ends at ' + $counter3 + ' (the analysis ' + $lap3 + ', the marks one more)')

# --- Backstroke: the first pull after the dive is not a stroke of the analysis, nor of the counter of the video.
$race4 = NewRace 'bk' 1.32 2 $null 0 $null $null
$c4 = Compute $race4
$reel4 = Reel $race4 $c4
$lap4 = 0; foreach ($d in @(15, 25, 35, 45, 50)) { $lap4 += (Row $c4 $d).Strokes }
$counter4 = $reel4.StrokesInLength([double]26.19)
Check ($counter4 -eq $lap4) ('backstroke: the counter of the first length ends at ' + $counter4 + ', the analysis counts ' + $lap4 + ' (the first pull is not one)')

# --- Freestyle, 1.32 s a cycle (two presses 0.66 s apart), one stroke read 0.26 s after its frame: the card shows the rates of the
# analysis (the segment with that stroke a little off the others, as in the PDF), not the cycles of 38 and 57 a minute.
$step2 = 0.66
$grid2 = @(); for ($t = 30.3; $t -lt 54.8; $t += $step2) { $grid2 += $t }
$at2 = $grid2[12]
$race5 = NewRace 'fs' 1.32 2 $at2 0.26 $null $null
$c5 = Compute $race5
$reel5 = Reel $race5 $c5
$rates5 = @($reel5.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.ShownTempo })
$lo5 = ($rates5 | Measure-Object -Minimum).Minimum; $hi5 = ($rates5 | Measure-Object -Maximum).Maximum
$raw5 = @($reel5.Cycles | Where-Object { $_.Lap -eq 1 } | ForEach-Object { 60 / $_.Tempo })
$rlo = ($raw5 | Measure-Object -Minimum).Minimum; $rhi = ($raw5 | Measure-Object -Maximum).Maximum
Check ($hi5 - $lo5 -lt 2.0 -and $rhi - $rlo -gt 8) ('freestyle on the card from ' + (F2 $lo5) + ' to ' + (F2 $hi5) + ' cycles a minute (from the strokes ' + (F2 $rlo) + ' to ' + (F2 $rhi) + ')')
$apart5 = 0
foreach ($cy in $reel5.Cycles) {
  $seg = CleanOf $reel5 $cy
  if ($null -eq $seg) { continue }
  $row = Row $c5 $seg.To
  if ([Math]::Abs(60 / $cy.ShownTempo - $row.Sr) -gt 1e-9 -or [Math]::Abs($cy.ShownDps - $row.Sl) -gt 1e-9) { $apart5++ }
}
Check ($apart5 -eq 0) ('every one of them the rate and the length of its segment in the analysis (' + $apart5 + ' apart)')

# ================= With the Apps Script before 2.19.0: the metres over the strokes, on the video as in its PDF.
LengthFromRate $false
$old = Compute $race0
$reelOld = Reel $race0 $old
$row85 = Row $old 95
Check ([Math]::Abs($row85.Sl - 10 / $row85.Strokes) -lt 1e-9 -and [Math]::Abs($row85.Dps - $row85.Sl) -lt 1e-9) ('with the script before: the cycle length of 85-95 m is the 10 m over its ' + $row85.Strokes + ' strokes (' + (F2 $row85.Sl) + ' m)')
$apartOld = 0
foreach ($cy in $reelOld.Cycles) {
  $seg = CleanOf $reelOld $cy
  if ($null -eq $seg) { continue }
  $row = Row $old $seg.To
  if ([Math]::Abs(60 / $cy.ShownTempo - $row.Sr) -gt 1e-9 -or [Math]::Abs($cy.ShownDps - $row.Sl) -gt 1e-9) { $apartOld++ }
}
Check ($apartOld -eq 0) ('and the video shows that one (' + $apartOld + ' apart)')
$newRow = Row $clean 95
Check ([Math]::Abs($newRow.Sr - $row85.Sr) -lt 1e-9 -and [Math]::Abs($newRow.Strokes - $row85.Strokes) -lt 1e-9 -and [Math]::Abs($newRow.Sl - $newRow.Vel * 60 / $newRow.Sr) -lt 1e-9 -and [Math]::Abs($newRow.Sl - $row85.Sl) -gt 0.01) ('the rate and the strokes are the same with both scripts; the length ' + (F2 $row85.Sl) + ' m before, ' + (F2 $newRow.Sl) + ' m from the speed and the rate')
LengthFromRate $false

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'The video graphics show the numbers of the analysis: all checks passed.'
