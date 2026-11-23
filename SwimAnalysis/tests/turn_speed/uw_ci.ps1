# uw_ci.ps1: the CI check of the underwater speed of the turns on a FICTIONAL 100 m freestyle race (50 m pool). 2.32.0, the analyst's
# request: as at the start, the distance of the breakout from the wall over the time from the push-off to the breakout (2.15.0–2.31.0
# from where the head was at the push-off, the body length from the wall: no speed without it), the distance per kick from the wall too;
# none from a distance estimated from the times or from a distance no swimmer has (a breakout measured at 39.6 m with the calibration of
# another frame: the Sheet gets the estimate of the video graphics instead, said as an estimate). The official time of the wall is the
# time of the turn (also when its mark is a little before it): the contact, the −5 and the +15 from it, as the splits of the race.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File uw_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
# The other DLLs of the program from its folder (plain string work only in the handler, see te_ci.ps1).
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
$tTech = T 'SwimTechCalculator'; $tPresets = T 'SwimPresets'; $tClient = T 'SwimPayload'; $tSettings = T 'SwimSettings'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

# 50 fps, a timestamp = 1 ms; the start signal at 1 s of the video.
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
function NewRace() {
  $race = [Activator]::CreateInstance($tRace)
  $race.Style = 'fs'; $race.Distance = 100; $race.Pool = 50
  $race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(100, 50, $false))
  $race.StartTimestamp = [long]1000
  return $race
}
function Mark($race, $type, $seconds, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
function Fill($race, $pushoff, $wall = 26.3) {
  $splits = @{ 15 = 6.2; 25 = 11.8; 35 = 17.6; 45 = 23.4; 50 = $wall; 65 = 33.5; 75 = 39.4; 85 = 45.3; 95 = 51.2; 100 = 54.1 }
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  if ($pushoff) { Mark $race 'FeetOff' 26.6 $null }
  # Six dolphin kicks between the push-off and the breakout.
  foreach ($k in @(27.0, 27.5, 28.0, 28.5, 29.0, 29.5)) { Mark $race 'Kick' $k $null }
  Mark $race 'Breakout' 30.6 $null
}
function TurnPhase($race) {
  $computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
  $phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
  $turn = $null
  foreach ($p in $phases) { if ($p.Kind.ToString() -eq 'Turn' -and $p.Wall -eq 50) { $turn = $p } }
  return @{ Computed = $computed; Phases = $phases; Turn = $turn }
}
$settings = [Activator]::CreateInstance($tSettings)
function Sheet($race, $r) {
  $json = $tClient.GetMethod('Build').Invoke($null, @($race, $r.Computed, $r.Phases, $timing, [long]1000, $settings, 'demo.mp4', 'update', $null, $false, $null))
  return ($json | ConvertFrom-Json)
}
function Near($v, $x, $tol) { return ($null -ne $v -and [Math]::Abs([double]$v - $x) -lt $tol) }

# The breakout measured at 10.0 m (📍): 10.0 / (30.6 − 26.6) = 2.50 m/s, as the start (no body length needed).
$race = NewRace; Fill $race $true
$turn = $race.GetTurn(50, $true); $turn.UnderwaterDist = 10.0
$r = TurnPhase $race
Check ($null -ne $r.Turn) 'the turn at 50 m'
$v = $r.Turn.Value('uwVel')
Check (Near $v 2.5 0.0005) ('underwater speed from the wall over the time from the push-off: ' + $v)
$kd = $r.Turn.Value('kickDist')
Check (Near $kd (10.0 / 6) 0.0005) ('distance per kick from the wall: ' + $kd + ' (the values to the thousandth)')
$b15 = $r.Turn.Value('bo15Vel')
Check (Near $b15 (5 / (33.5 - 30.6)) 0.0005) ('from the breakout to the 15 m: ' + $b15)
Check ($null -eq $r.Turn.SpeedNote) ('no note: ' + $r.Turn.SpeedNote)
$body = Sheet $race $r
Check (Near $body.race.manual.turns.'50'.uwSpeed 2.5 0.0005) ('uwSpeed sent: ' + $body.race.manual.turns.'50'.uwSpeed)
Check (Near $body.race.manual.turns.'50'.underwaterDist 10.0 1e-9) 'the distance measured sent'
Check ($null -eq $body.race.manual.turns.'50'.underwaterEstimated) 'a measured distance is not said to be an estimate'

# A body length typed changes nothing (2.15.0–2.31.0: (10.0 − 1.9) / 4.0 = 2.025).
$race.BodyLength = 1.9
$r = TurnPhase $race
Check (Near $r.Turn.Value('uwVel') 2.5 0.0005) ('with a body length the same speed: ' + $r.Turn.Value('uwVel'))
Check (Near $r.Turn.Value('kickDist') (10.0 / 6) 0.0005) 'and the same distance per kick'
$race.BodyLength = $null

# The distance estimated from the times: no speed from it.
$turn.UnderwaterEstimated = $true
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed from an estimated distance'
Check ($null -eq $r.Turn.Value('kickDist')) 'no distance per kick from an estimated distance'
Check ("$($r.Turn.SpeedNote)" -like '*εκτίμηση*') ('the note: ' + $r.Turn.SpeedNote)
$body = Sheet $race $r
Check ($body.race.manual.turns.'50'.underwaterEstimated -eq $true) 'the estimate is said to the Sheet'

# A breakout typed at 2.5 m: 0.62 m/s, slower than a swimmer under the water: no speed, a note.
$turn.UnderwaterEstimated = $false; $turn.UnderwaterDist = 2.5
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed for 2.5 m in 4 s'
Check ("$($r.Turn.SpeedNote)" -like '*δεν γίνεται*') ('the note: ' + $r.Turn.SpeedNote)

# A breakout measured at 39.6 m (the calibration of another frame): no speed, no distance per kick, a note; the Sheet gets the estimate.
$turn.UnderwaterDist = 39.6
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel') -and $null -eq $r.Turn.Value('kickDist') -and $null -eq $r.Turn.Value('bo15Vel')) 'nothing from 39.6 m'
Check ("$($r.Turn.SpeedNote)" -like '*39.60*δεν γίνεται*') ('the note: ' + $r.Turn.SpeedNote)
$body = Sheet $race $r
$sent = $body.race.manual.turns.'50'.underwaterDist
Check ($null -ne $sent -and [double]$sent -gt 5 -and [double]$sent -le 15.5) ('the Sheet gets the estimate, not 39.6: ' + $sent)
Check ($body.race.manual.turns.'50'.underwaterEstimated -eq $true -and $null -eq $body.race.manual.turns.'50'.uwSpeed) 'said as an estimate, no speed from it'

# Without the push-off: no speed (the time is from the push-off, not from the wall).
$race2 = NewRace; Fill $race2 $false
$t2 = $race2.GetTurn(50, $true); $t2.UnderwaterDist = 10.0
$r = TurnPhase $race2
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed without the push-off'

# The official time of the wall (26.30) with its mark 0.12 s before (26.18): the turn from the official time, as the splits.
$race3 = NewRace; Fill $race3 $true 26.18
$t3 = $race3.GetTurn(50, $true); $t3.UnderwaterDist = 10.0
$race3.Official = New-Object 'System.Collections.Generic.List[double]'; $race3.Official.Add(26.30); $race3.Official.Add(54.10)
$r = TurnPhase $race3
Check (Near $r.Turn.Value('contact') 0.30 0.0015) ('the contact from the official touch: ' + $r.Turn.Value('contact') + ' (from the mark 0.42)')
Check (Near $r.Turn.Value('in5') 2.90 0.0015) ('45 m → the wall as the splits: ' + $r.Turn.Value('in5') + ' (from the mark 2.78)')
Check (Near $r.Turn.Value('out15') 7.20 0.0015) ('the wall → 65 m as the splits: ' + $r.Turn.Value('out15') + ' (from the mark 7.32)')
Check (Near $r.Turn.Value('uwVel') 2.5 0.0005) ('the underwater speed from its own marks: ' + $r.Turn.Value('uwVel'))

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'underwater speed of the turns: all checks passed'
