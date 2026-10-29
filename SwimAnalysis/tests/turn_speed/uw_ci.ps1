# uw_ci.ps1: the CI check of the underwater speed of the turns (2.15.0) on a FICTIONAL 100 m freestyle race: the speed is from where the
# head is at the push-off (the toes still on the wall, the body straight: one body length from the wall) to the breakout,
# (breakout distance − body length) / (breakout − push-off); without the body length, with the distance estimated from the times, or a
# breakout too close to the body length, no speed (a note says why); the Sheet gets it per turn with the body length.
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
function Fill($race, $pushoff) {
  $splits = @{ 15 = 6.2; 25 = 11.8; 35 = 17.6; 45 = 23.4; 50 = 26.3; 65 = 33.5; 75 = 39.4; 85 = 45.3; 95 = 51.2; 100 = 54.1 }
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  if ($pushoff) { Mark $race 'FeetOff' 26.6 $null }
  Mark $race 'Breakout' 30.6 $null
}
function TurnPhase($race) {
  $computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
  $phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
  $turn = $null
  foreach ($p in $phases) { if ($p.Kind.ToString() -eq 'Turn' -and $p.Wall -eq 50) { $turn = $p } }
  return @{ Computed = $computed; Phases = $phases; Turn = $turn }
}

# The breakout measured at 10.0 m (📍), the body 1.90 m: (10.0 − 1.9) / (30.6 − 26.6) = 2.025 m/s (from the wall: 2.50, 23% faster).
$race = NewRace; Fill $race $true
$turn = $race.GetTurn(50, $true); $turn.UnderwaterDist = 10.0
$race.BodyLength = 1.9
$r = TurnPhase $race
Check ($null -ne $r.Turn) 'the turn at 50 m'
$v = $r.Turn.Value('uwVel')
Check ($null -ne $v -and [Math]::Abs($v - 2.025) -lt 0.0005) ('underwater speed from the head at the push-off: ' + $v)
Check ($null -ne $v -and [Math]::Abs($v - 2.5) -gt 0.4) 'not the distance from the wall over the time (2.50)'
Check ([Math]::Abs($r.Turn.HeadAtPushoff - 1.9) -lt 1e-9) 'the head at the push-off is the body length'

# The Sheet: the speed of the program per turn and the body length.
$settings = [Activator]::CreateInstance($tSettings)
$json = $tClient.GetMethod('Build').Invoke($null, @($race, $r.Computed, $r.Phases, $timing, [long]1000, $settings, 'demo.mp4', 'update', $null, $false, $null))
$body = $json | ConvertFrom-Json
Check ([Math]::Abs($body.race.manual.turns.'50'.uwSpeed - 2.025) -lt 0.0005) ('uwSpeed sent: ' + $body.race.manual.turns.'50'.uwSpeed)
Check ([Math]::Abs($body.race.manual.bodyLength - 1.9) -lt 1e-9) 'bodyLength sent'
Check ($null -eq $body.race.manual.turns.'50'.underwaterEstimated) 'a measured distance is not said to be an estimate'

# Without the body length: no speed, the note says what is missing.
$race.BodyLength = $null
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed without the body length'
Check ("$($r.Turn.SpeedNote)" -like '*μήκος του σώματος*') ('the note asks for the body length: ' + $r.Turn.SpeedNote)
$json = $tClient.GetMethod('Build').Invoke($null, @($race, $r.Computed, $r.Phases, $timing, [long]1000, $settings, 'demo.mp4', 'update', $null, $false, $null))
$body = $json | ConvertFrom-Json
Check ($null -eq $body.race.manual.turns.'50'.uwSpeed) 'nothing sent without the body length'

# The distance estimated from the times: no speed from it.
$race.BodyLength = 1.9; $turn.UnderwaterEstimated = $true
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed from an estimated distance'
$json = $tClient.GetMethod('Build').Invoke($null, @($race, $r.Computed, $r.Phases, $timing, [long]1000, $settings, 'demo.mp4', 'update', $null, $false, $null))
$body = $json | ConvertFrom-Json
Check ($body.race.manual.turns.'50'.underwaterEstimated -eq $true) 'the estimate is said to the Sheet'

# A breakout too close to the body length (less than 1 m of way): no speed, a note.
$turn.UnderwaterEstimated = $false; $turn.UnderwaterDist = 2.5
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed for 0.6 m of way'
Check ("$($r.Turn.SpeedNote)" -like '*πολύ κοντά στο μήκος του σώματος*') ('the note: ' + $r.Turn.SpeedNote)

# Without the push-off: no speed (the time is from the push-off, not from the wall).
$turn.UnderwaterDist = 10.0
$race2 = NewRace; Fill $race2 $false
$t2 = $race2.GetTurn(50, $true); $t2.UnderwaterDist = 10.0; $race2.BodyLength = 1.9
$r = TurnPhase $race2
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed without the push-off'

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'underwater speed of the turns: all checks passed'
