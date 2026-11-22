# rs_ci.ps1: the CI check of the speeds of the video graphics (SwimReel, 2.31.0) on a FICTIONAL 100 m backstroke (50 m pool): the
# curve through the checkpoints is on the times of the race (the official final time where typed, as the sheet and the PDF), the
# swimmer on the frames of the marks. A finish marked 0.75 s after its official time made the last 5 m 1.32 m/s on the video
# (1.64 in the PDF) and pulled the 85-95 m down; now the speeds are those of the PDF and the swimmer still reaches the wall on the
# frame of the mark. Without an official time nothing changes.
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
# Fictional splits; the finish mark at 53.60.
$splits = [ordered]@{ 15 = 6.30; 25 = 11.50; 35 = 16.60; 45 = 22.40; 50 = 25.40; 65 = 32.60; 75 = 38.10; 85 = 43.70; 95 = 49.80; 100 = 53.60 }
function NewRace($official) {
  $race = [Activator]::CreateInstance($tRace)
  $race.Style = 'bk'; $race.Distance = 100; $race.Pool = 50
  $race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(100, 50, $false))
  $race.StartTimestamp = [long]1000
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  Mark $race 'Breakout' 9.6 $null
  Mark $race 'Breakout' 29.4 $null
  # Arm strokes every 0.66 s after each breakout up to the wall (backstroke: two presses a cycle).
  for ($t = 9.9; $t -lt 25.2; $t += 0.66) { Mark $race 'Stroke' $t $null }
  for ($t = 29.7; $t -lt 53.4; $t += 0.66) { Mark $race 'Stroke' $t $null }
  if ($null -ne $official) { $race.Official = New-Object 'System.Collections.Generic.List[double]'; foreach ($o in $official) { $race.Official.Add([double]$o) } }
  return $race
}
function Reel($race) {
  $computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
  return @{ Computed = $computed; Reel = $tReel.GetMethod('Build').Invoke($null, @($race, $computed)) }
}

# --- The official final time 52.85, the finish marked at 53.60 (0.75 s later).
$r = Reel (NewRace @(52.85))
$reel = $r.Reel
$row100 = $null; foreach ($row in $r.Computed.Rows) { if ($row.D -eq 100) { $row100 = $row } }
Check ($row100.Official -and [Math]::Abs($row100.T - 52.85) -lt 1e-9 -and [Math]::Abs($row100.MarkT - 53.60) -lt 1e-9) ('the analysis keeps the official time, the mark apart: ' + (F2 $row100.T) + ' / ' + (F2 $row100.MarkT))
Check ($row100.MarkApart) 'the mark is shown apart (the analysis window warns)'
$pdf95 = 5 / (52.85 - 49.80); $mark95 = 5 / (53.60 - 49.80)
$v = $reel.SpeedAt(51.7)
Check ([Math]::Abs($v - $pdf95) -lt 0.06) ('95-100 m on the video as in the PDF: ' + (F2 $v) + ' m/s (PDF ' + (F2 $pdf95) + ', from the mark ' + (F2 $mark95) + ')')
$v2 = $reel.SpeedAt(53.4)
Check ([Math]::Abs($v2 - $pdf95) -lt 0.08) ('just before the wall: ' + (F2 $v2) + ' m/s, no drop')
$pdf85 = 10 / (49.80 - 43.70)
$lowest = 9.0; for ($t = 44.0; $t -le 49.6; $t += 0.2) { $s = $reel.SpeedAt($t); if ($s -lt $lowest) { $lowest = $s } }
Check ($lowest -gt $pdf85 - 0.08) ('85-95 m not pulled down by the finish: lowest ' + (F2 $lowest) + ' m/s (segment ' + (F2 $pdf85) + ')')
Check ([Math]::Abs($reel.DistanceAt(49.80) - 95) -lt 1e-6) ('95 m on the frame of its mark: ' + (F2 $reel.DistanceAt(49.80)))
Check ([Math]::Abs($reel.DistanceAt(53.60) - 100) -lt 1e-6) ('the wall on the frame of the finish mark: ' + (F2 $reel.DistanceAt(53.60)))
Check ($reel.DistanceAt(53.20) -lt 99.9) ('before the mark the swimmer is not at the wall yet: ' + (F2 $reel.DistanceAt(53.20)))
Check ([Math]::Abs($reel.Finish - 53.60) -lt 1e-9 -and [Math]::Abs($reel.FinalTime - 52.85) -lt 1e-9) 'the final time shown on the frame of the finish mark'
Check ([Math]::Abs($reel.RaceTime(53.60) - 52.85) -lt 1e-9 -and [Math]::Abs($reel.VideoTime(52.85) - 53.60) -lt 1e-9) 'race time and video time at the finish'
Check ([Math]::Abs($reel.RaceTime(43.70) - 43.70) -lt 1e-9) 'where the mark is on its time they are the same'
$late = @(); foreach ($c in $reel.Cycles) { if ($c.Time -gt 50.2) { $late += $c } }
$bad = @($late | Where-Object { [Math]::Abs($_.Speed - $pdf95) -gt 0.12 })
Check ($late.Count -ge 2 -and $bad.Count -eq 0) ('the cycles of the last 5 m at the speed of the PDF: ' + (($late | ForEach-Object { F2 $_.Speed }) -join ', '))
$part = $null; foreach ($p in $reel.Parts) { if ($p.From -eq 50 -and $p.To -eq 100) { $part = $p } }
Check ($null -ne $part -and [Math]::Abs($part.Time - (52.85 - 25.40)) -lt 1e-6) ('the second length in the summary: ' + (F2 $part.Time) + ' s as the analysis')
Check ($null -ne $part -and $part.Strokes -gt 30) ('its strokes between the frames of its checkpoints: ' + $part.Strokes)
$wall = $reel.Walls[0]
Check ([Math]::Abs($wall - 25.40) -lt 1e-9) ('the wall at 50 m: ' + (F2 $wall))

# --- Without an official time: the curve on the marks, as before (the last 5 m at the speed of the marks).
$r0 = Reel (NewRace $null)
$v0 = $r0.Reel.SpeedAt(51.7)
Check ([Math]::Abs($v0 - $mark95) -lt 0.08) ('without an official time the marks: ' + (F2 $v0) + ' m/s (marks ' + (F2 $mark95) + ')')
Check ([Math]::Abs($r0.Reel.RaceTime(51.7) - 51.7) -lt 1e-9) 'without an official time the times of the video'

# --- The official time on the frame of the mark (a hundredth apart): the same as without.
$r1 = Reel (NewRace @(53.61))
$v1 = $r1.Reel.SpeedAt(51.7)
Check ([Math]::Abs($v1 - $v0) -lt 0.01) ('an official time a hundredth off changes nothing: ' + (F2 $v1) + ' / ' + (F2 $v0))

# --- The official 50 m split too (25.20, its mark 25.40) and the final: the first length ends on the official split.
$r2 = Reel (NewRace @(25.20, 52.85))
$p2 = $null; foreach ($p in $r2.Reel.Parts) { if ($p.From -eq 0 -and $p.To -eq 50) { $p2 = $p } }
Check ($null -ne $p2 -and [Math]::Abs($p2.Time - 25.20) -lt 1e-6) ('the first length on the official split: ' + (F2 $p2.Time))
Check ([Math]::Abs($r2.Reel.DistanceAt(25.40) - 50) -lt 1e-6) ('the wall on the frame of its mark: ' + (F2 $r2.Reel.DistanceAt(25.40)))
$v3 = $r2.Reel.SpeedAt(24.6)
$seg45 = 5 / (25.20 - 22.40)
Check ([Math]::Abs($v3 - $seg45) -lt 0.15) ('45-50 m at the speed of the official split: ' + (F2 $v3) + ' (segment ' + (F2 $seg45) + ')')

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'All checks of the speeds of the video graphics passed.'
