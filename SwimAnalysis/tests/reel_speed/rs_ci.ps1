# rs_ci.ps1: the CI check of the speeds of the video graphics (SwimReel) on a FICTIONAL 100 m backstroke (50 m pool). 2.31.0: the curve
# through the checkpoints is on the times of the race (the official final time where typed, as the sheet and the PDF). 2.32.0: the
# official time is the time of the checkpoint on the video too, also when the mark is a little before or after it (the analyst's
# request; a finish marked 0.75 s after its official time made the last 5 m 1.32 m/s on the video, 1.64 in the PDF); the start and the
# turns on the curve from their own marks: on the block up to the release, the entry at its distance, the breakout at its distance, the
# swimmer at the wall up to the push off it (estimated here, 0.3 s after the touch of a tumble turn), so that the speed after the push
# is high as after the start (until 2.31.0 a flat 1.89 m/s under the water after a turn). The speed in the water also under it, the
# samples of the chart of the whole race.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File rs_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
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
function F2($v) { return ([double]$v).ToString('0.000', [Globalization.CultureInfo]::InvariantCulture) }

# 50 fps, a timestamp = 1 ms; the start signal at 1 s of the video.
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
function Mark($race, $type, $seconds, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
# Fictional splits; the finish mark at 53.60. (An [ordered] table with int keys: $splits[$d] would be the d-th entry, not the key d.)
$splits = [ordered]@{ 15 = 6.30; 25 = 11.50; 35 = 16.60; 45 = 22.40; 50 = 25.40; 65 = 32.60; 75 = 38.10; 85 = 43.70; 95 = 49.80; 100 = 53.60 }
function NewRace($official, $startBreakout = 9.6) {
  $race = [Activator]::CreateInstance($tRace)
  $race.Style = 'bk'; $race.Distance = 100; $race.Pool = 50
  $race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(100, 50, $false))
  $race.StartTimestamp = [long]1000
  foreach ($e in $splits.GetEnumerator()) { Mark $race 'Split' $e.Value $e.Key }
  Mark $race 'Breakout' $startBreakout $null
  Mark $race 'Breakout' 29.4 $null
  # Arm strokes every 0.66 s after each breakout up to the wall (backstroke: two presses a cycle).
  for ($t = $startBreakout + 0.3; $t -lt 25.2; $t += 0.66) { Mark $race 'Stroke' $t $null }
  for ($t = 29.7; $t -lt 53.4; $t += 0.66) { Mark $race 'Stroke' $t $null }
  if ($null -ne $official) { $race.Official = New-Object 'System.Collections.Generic.List[double]'; foreach ($o in $official) { $race.Official.Add([double]$o) } }
  return $race
}
function Reel($race) {
  $computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
  return @{ Computed = $computed; Reel = $tReel.GetMethod('Build').Invoke($null, @($race, $computed)) }
}

# --- The official final time 52.85, the finish marked at 53.60 (0.75 s later): the official time stands, also on the video.
$r = Reel (NewRace @(52.85))
$reel = $r.Reel
$row100 = $null; foreach ($row in $r.Computed.Rows) { if ($row.D -eq 100) { $row100 = $row } }
Check ($row100.Official -and [Math]::Abs($row100.T - 52.85) -lt 1e-9 -and [Math]::Abs($row100.MarkT - 53.60) -lt 1e-9) ('the analysis keeps the official time, the mark apart: ' + (F2 $row100.T) + ' / ' + (F2 $row100.MarkT))
Check ($row100.MarkApart) 'the mark is shown apart (the analysis window warns)'
Check ([Math]::Abs($row100.OnVideo - 52.85) -lt 1e-9 -and $row100.PassedAt(52.85) -and -not $row100.PassedAt(52.70)) ('on the video at the official time: ' + (F2 $row100.OnVideo))
$pdf95 = 5 / (52.85 - 49.80); $mark95 = 5 / (53.60 - 49.80)
$v = $reel.SpeedAt(51.7)
Check ([Math]::Abs($v - $pdf95) -lt 0.06) ('95-100 m on the video as in the PDF: ' + (F2 $v) + ' m/s (PDF ' + (F2 $pdf95) + ', from the mark ' + (F2 $mark95) + ')')
$v2 = $reel.SpeedAt(52.7)
Check ([Math]::Abs($v2 - $pdf95) -lt 0.08) ('just before the wall: ' + (F2 $v2) + ' m/s, no drop')
$pdf85 = 10 / (49.80 - 43.70)
$lowest = 9.0; for ($t = 44.0; $t -le 49.6; $t += 0.2) { $s = $reel.SpeedAt($t); if ($s -lt $lowest) { $lowest = $s } }
Check ($lowest -gt $pdf85 - 0.08) ('85-95 m not pulled down by the finish: lowest ' + (F2 $lowest) + ' m/s (segment ' + (F2 $pdf85) + ')')
Check ([Math]::Abs($reel.DistanceAt(49.80) - 95) -lt 1e-6) ('95 m at its time: ' + (F2 $reel.DistanceAt(49.80)))
Check ([Math]::Abs($reel.DistanceAt(52.85) - 100) -lt 1e-6) ('the wall at the official time: ' + (F2 $reel.DistanceAt(52.85)))
Check ($reel.DistanceAt(52.5) -lt 99.9) ('before it the swimmer is not at the wall yet: ' + (F2 $reel.DistanceAt(52.5)))
Check ([Math]::Abs($reel.Finish - 52.85) -lt 1e-9 -and [Math]::Abs($reel.FinalTime - 52.85) -lt 1e-9) 'the finish on the video at the official time'
Check ([Math]::Abs($reel.RaceTime(53.60) - 53.60) -lt 1e-9 -and [Math]::Abs($reel.RaceTime(43.70) - 43.70) -lt 1e-9) 'the times of the race are those of the video'
$late = @(); foreach ($c in $reel.Cycles) { if ($c.Time -gt 50.2) { $late += $c } }
$bad = @($late | Where-Object { [Math]::Abs($_.Speed - $pdf95) -gt 0.12 })
Check ($late.Count -ge 2 -and $bad.Count -eq 0) ('the cycles of the last 5 m at the speed of the PDF: ' + (($late | ForEach-Object { F2 $_.Speed }) -join ', '))
$part = $null; foreach ($p in $reel.Parts) { if ($p.From -eq 50 -and $p.To -eq 100) { $part = $p } }
Check ($null -ne $part -and [Math]::Abs($part.Time - (52.85 - 25.40)) -lt 1e-6) ('the second length in the summary: ' + (F2 $part.Time) + ' s as the analysis')
Check ($null -ne $part -and $part.Strokes -gt 30) ('its strokes up to its official time: ' + $part.Strokes)
$wall = $reel.Walls[0]
Check ([Math]::Abs($wall - 25.40) -lt 1e-9) ('the wall at 50 m: ' + (F2 $wall))

# --- (2.32.0) The turn on the curve: at the wall up to the push off it (not marked: 0.3 s after the touch of a tumble turn), then fast
# and slower down to the breakout at its distance (estimated from the times: about 9 m), as after the start.
Check ([Math]::Abs($reel.DistanceAt(25.55) - 50) -lt 1e-6 -and [Math]::Abs($reel.DistanceAt(25.69) - 50) -lt 1e-6) ('at the wall while turning: ' + (F2 $reel.DistanceAt(25.55)) + ' m')
$bo = $reel.BreakoutDistances[1]
Check ([Math]::Abs($reel.DistanceAt(29.4) - (50 + $bo)) -lt 0.01) ('the breakout of the turn at its distance: ' + (F2 $reel.DistanceAt(29.4)) + ' m (' + (F2 $bo) + ' from the wall)')
$s1 = $reel.SpeedAt(25.75); $s2 = $reel.SpeedAt(27.0); $s3 = $reel.SpeedAt(28.0); $s4 = $reel.SpeedAt(29.3)
Check ($s1 -gt 2.5 -and $s1 -gt $s2 -and $s2 -gt $s3 -and $s3 -gt $s4) ('fast after the push, slower to the breakout: ' + (F2 $s1) + ' > ' + (F2 $s2) + ' > ' + (F2 $s3) + ' > ' + (F2 $s4))
$seg = 6 / 3.2; $avg = $bo / (29.4 - 25.70)
Check ([Math]::Abs($s4 - $seg) -lt 0.15) ('at the breakout the speed of the swimming after it: ' + (F2 $s4) + ' (to 65 m ' + (F2 $seg) + ')')
Check ($avg -gt 2.2 -and $avg -lt 2.7) ('under the water ' + (F2 $avg) + ' m/s on average (the distance of the breakout over the time from the push)')

# --- In the water: from about the entry of the start (1.5 m in backstroke, not marked here) and from the push off the wall, the speed
# with ≈ under the water up to each breakout.
$entry = $reel.TimeAtDistance(1.5)
Check ($entry -gt 0.3 -and $entry -lt 0.9) ('the entry of the start estimated at ' + (F2 $entry) + ' s (1.5 m on the curve)')
Check (-not $reel.InWater(0.2) -and $reel.InWater($entry + 0.05) -and $reel.Underwater($entry + 0.05)) 'no speed before the entry, under the water after it'
Check ($reel.PhaseAt(0.2) -eq 'ΕΚΚΙΝΗΣΗ' -and $reel.PhaseAt(3.0) -eq 'ΥΠΟΒΡΥΧΙΑ') ('the phase: ' + $reel.PhaseAt(0.2) + ' then ' + $reel.PhaseAt(3.0))
Check ($reel.InWater(10.0) -and -not $reel.Underwater(10.0)) 'after the breakout in the water, not under it'
Check (-not $reel.InWater(25.55) -and $reel.PhaseAt(25.55) -eq 'ΣΤΡΟΦΗ') 'the turn up to the push off the wall: no speed'
Check (-not $reel.InWater(25.70) -and $reel.InWater(25.75) -and $reel.Underwater(25.75) -and $reel.PhaseAt(26.0) -eq 'ΥΠΟΒΡΥΧΙΑ') 'after the push under the water (not on its knot)'
Check ($reel.InWater(29.5) -and -not $reel.Underwater(29.5)) 'after the breakout of the turn'
Check (-not $reel.InWater(52.85)) 'no speed after the finish'
$uw = $reel.SpeedAt(3.0)
Check ($uw -gt 2.0 -and $uw -lt 3.0) ('the speed under the water after the start: ' + (F2 $uw) + ' m/s')
Check ([Math]::Abs($reel.SpeedAt(25.40) - $reel.SpeedAt(25.38)) -lt 0.02) ('on the frame of the wall the speed of its length: ' + (F2 $reel.SpeedAt(25.40)) + ' (0.02 s before ' + (F2 $reel.SpeedAt(25.38)) + ')')
$spans = @($reel.UnderwaterSpans)
Check ($spans.Count -eq 2) ('two underwater parts: ' + $spans.Count)
if ($spans.Count -eq 2) {
  Check ([Math]::Abs($spans[0].From - $entry) -lt 0.002 -and [Math]::Abs($spans[0].To - 9.6) -lt 1e-6 -and [Math]::Abs($spans[0].FromDistance - 1.5) -lt 0.01) ('after the start: ' + (F2 $spans[0].From) + '-' + (F2 $spans[0].To) + ' s from ' + (F2 $spans[0].FromDistance) + ' m')
  Check ([Math]::Abs($spans[1].From - 25.70) -lt 0.002 -and [Math]::Abs($spans[1].To - 29.4) -lt 1e-6 -and $spans[1].FromDistance -eq 50 -and $spans[1].ToDistance -gt 50 -and $spans[1].ToDistance -lt 65) ('after the turn: ' + (F2 $spans[1].From) + '-' + (F2 $spans[1].To) + ' s, ' + (F2 $spans[1].FromDistance) + '-' + (F2 $spans[1].ToDistance) + ' m')
}
$samples = @($reel.Samples)
$inTurn = @($samples | Where-Object { $_.Time -gt 25.40 + 1e-6 -and $_.Time -lt 25.70 - 1e-6 })
$early = @($samples | Where-Object { $_.Time -lt $entry - 1e-6 })
$bad = @($samples | Where-Object { $_.Speed -lt 1.5 -or $_.Speed -gt 3.0 })
Check ($samples.Count -gt 500 -and $inTurn.Count -eq 0 -and $early.Count -eq 0 -and $bad.Count -eq 0) ('the samples of the chart: ' + $samples.Count + ', none in the turn (' + $inTurn.Count + '), before the entry (' + $early.Count + ') or out of 1.5-3.0 m/s (' + $bad.Count + ')')
Check ($samples.Count -gt 0 -and [Math]::Abs($samples[$samples.Count - 1].Time - 52.85) -lt 1e-6 -and [Math]::Abs($samples[$samples.Count - 1].Distance - 100) -lt 1e-6) 'the last sample at the official finish, at the wall'
$lateS = @($samples | Where-Object { $_.Time -ge 50.5 -and $_.Time -le 52.8 -and [Math]::Abs($_.Speed - $pdf95) -gt 0.06 })
Check ($lateS.Count -eq 0) ('the last 5 m of the chart at the speed of the PDF (' + $lateS.Count + ' apart)')
$uwS = @($samples | Where-Object { $_.Underwater }); $uwBad = @($uwS | Where-Object { -not (($_.Time -lt 9.6) -or ($_.Time -ge 25.70 -and $_.Time -lt 29.4)) })
Check ($uwS.Count -gt 50 -and $uwBad.Count -eq 0) ('the samples under the water: ' + $uwS.Count + ' (' + $uwBad.Count + ' outside the parts)')

# --- (2.32.0) The start from its marks: on the wall up to the release (0.62), the entry at 2.0 m (0.95), the breakout at 11.5 m (4.6).
$rs = NewRace @(52.85) 4.6
Mark $rs 'FeetOff' 0.62 $null
Mark $rs 'Entry' 0.95 $null
$rs.EntryDistHands = 2.0
$rs.BreakoutDist = 11.5
$st = (Reel $rs).Reel
Check ([Math]::Abs($st.DistanceAt(0.5)) -lt 1e-6) ('on the wall up to the release: ' + (F2 $st.DistanceAt(0.5)) + ' m')
Check ([Math]::Abs($st.DistanceAt(0.95) - 2.0) -lt 0.01 -and [Math]::Abs($st.DistanceAt(4.6) - 11.5) -lt 0.01) ('the entry and the breakout at their distances: ' + (F2 $st.DistanceAt(0.95)) + ' / ' + (F2 $st.DistanceAt(4.6)) + ' m')
$e1 = $st.SpeedAt(1.0); $e2 = $st.SpeedAt(3.0); $e3 = $st.SpeedAt(4.5)
Check ($e1 -gt $e2 -and $e2 -gt $e3 -and $e1 -lt 3.5 -and $e3 -gt 1.8) ('fast after the entry, slower to the breakout: ' + (F2 $e1) + ' > ' + (F2 $e2) + ' > ' + (F2 $e3))
Check ($st.InWater(0.952) -and $st.SpeedAt(0.952) -lt 3.5) ('in the water just after the entry, not at the speed of the flight: ' + (F2 $st.SpeedAt(0.952)))
$uwAvg = (11.5 - 2.0) / (4.6 - 0.95)
Check ([Math]::Abs(($st.DistanceAt(4.6) - $st.DistanceAt(0.95)) / (4.6 - 0.95) - $uwAvg) -lt 0.01) ('under the water ' + (F2 $uwAvg) + ' m/s on average, as the PDF')

# --- Without an official time: the curve on the marks (the last 5 m at the speed of the marks).
$r0 = Reel (NewRace $null)
$v0 = $r0.Reel.SpeedAt(51.7)
Check ([Math]::Abs($v0 - $mark95) -lt 0.08) ('without an official time the marks: ' + (F2 $v0) + ' m/s (marks ' + (F2 $mark95) + ')')
Check ([Math]::Abs($r0.Reel.RaceTime(51.7) - 51.7) -lt 1e-9) 'without an official time the times of the video'

# --- The official time a hundredth off its mark: the same as without.
$r1 = Reel (NewRace @(53.61))
$v1 = $r1.Reel.SpeedAt(51.7)
Check ([Math]::Abs($v1 - $v0) -lt 0.01) ('an official time a hundredth off changes nothing: ' + (F2 $v1) + ' / ' + (F2 $v0))

# --- The official 50 m split too (25.20, its mark 25.40) and the final: the first length ends at the official split, also on the video.
$r2 = Reel (NewRace @(25.20, 52.85))
$p2 = $null; foreach ($p in $r2.Reel.Parts) { if ($p.From -eq 0 -and $p.To -eq 50) { $p2 = $p } }
Check ($null -ne $p2 -and [Math]::Abs($p2.Time - 25.20) -lt 1e-6) ('the first length on the official split: ' + (F2 $p2.Time))
Check ([Math]::Abs($r2.Reel.DistanceAt(25.20) - 50) -lt 1e-6 -and $r2.Reel.DistanceAt(25.10) -lt 50) ('the wall at its official time: ' + (F2 $r2.Reel.DistanceAt(25.20)))
Check ([Math]::Abs($r2.Reel.Walls[0] - 25.20) -lt 1e-9) ('the turn from the official touch: ' + (F2 $r2.Reel.Walls[0]))
$v3 = $r2.Reel.SpeedAt(24.6)
$seg45 = 5 / (25.20 - 22.40)
Check ([Math]::Abs($v3 - $seg45) -lt 0.15) ('45-50 m at the speed of the official split: ' + (F2 $v3) + ' (segment ' + (F2 $seg45) + ')')

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'All checks of the speeds of the video graphics passed.'
