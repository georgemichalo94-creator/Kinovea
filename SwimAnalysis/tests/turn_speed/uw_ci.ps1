# uw_ci.ps1: the CI check of the speeds of the start and of the turns on a FICTIONAL 100 m freestyle race (50 m pool). 2.35.0, the
# analyst's way: the start in phases (signal → entry of the hands, entry → breakout of the head, breakout → 15) and the turn from the
# push-off to the breakout of the head and from there to +15, each from where the head was at its two ends. The analyst marks only the
# hands at the entry and the push-off: the head behind the hands (0.27 × height) and the head from the wall at the push-off (height +
# 0.10 m) come from the typical height of the swimmer for the gender and age (2.36.0: a body length of before is not used). The
# distance per kick from the head too. None from a distance estimated from the times or from a distance no swimmer has (a breakout
# measured at 39.6 m with the calibration of another frame: the Sheet gets the estimate of the video graphics instead, said as an
# estimate). The official time of the wall is the time of the turn (also when its mark is a little before it): the contact, the −5 and
# the +15 from it, as the splits of the race. 2.35.1: the speed from the breakout to 15 for every breakout before it (a late turn, a
# late start), none past it or with its mark after the head at 15.
# 2.38.6: the previous model again, the analyst's request: under the water from where the hands entered (the start) and from the wall
# (a turn), the distance per kick as measured, with the analyst's two rules: (1) from the breakout to the 15 not slower than the
# swimming after it (the breakout nearer for the speeds, the distance sent as measured; 2.39.4: however far, not nearer than 2 m from
# a wall or than the entry + 1 m; 2.39.3 up to 0.5 m, before also up to 5%); (2) a turn
# not faster under the water than the start, on the mean or at the highest speed of the video (the speed of the turn starts a little
# out from the wall; the warning only past the body length).
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
function Fill($race, $pushoff, $wall = 26.3, $breakout = 30.6) {
  $splits = @{ 15 = 6.2; 25 = 11.8; 35 = 17.6; 45 = 23.4; 50 = $wall; 65 = 33.5; 75 = 39.4; 85 = 45.3; 95 = 51.2; 100 = 54.1 }
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  if ($pushoff) { Mark $race 'FeetOff' 26.6 $null }
  # Six dolphin kicks between the push-off and the breakout.
  foreach ($k in @(27.0, 27.5, 28.0, 28.5, 29.0, 29.5)) { Mark $race 'Kick' $k $null }
  Mark $race 'Breakout' $breakout $null
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

# The typical heights (m) by gender and age, and the head at the two moments (SwimHead).
$tHead = T 'SwimHead'
$typ = $tHead.GetMethod('Typical')
Check ([Math]::Abs($typ.Invoke($null, @('M', 0)) - 1.85) -lt 1e-9 -and [Math]::Abs($typ.Invoke($null, @('F', 0)) - 1.72) -lt 1e-9) 'adults: men 1.85, women 1.72'
Check ([Math]::Abs($typ.Invoke($null, @('F', 14)) - 1.66) -lt 1e-9 -and [Math]::Abs($typ.Invoke($null, @('M', 12)) - 1.56) -lt 1e-9) 'girls of 14: 1.66, boys of 12: 1.56'
Check ([Math]::Abs($typ.Invoke($null, @('', 0)) - 1.785) -lt 1e-9) 'gender unknown: the middle, 1.785'
Check ($tHead.GetMethod('AgeOf').Invoke($null, @('2012', '23.11.2026')) -eq 14) 'the age in the year of the race'
Check ($tHead.GetMethod('AgeOf').Invoke($null, @('', '23.11.2026')) -eq 0) 'no year of birth: 0 (the adults)'

# The breakout measured at 10.0 m (📍), push-off 26.6, breakout 30.6. (2.38.6) From the wall, the previous model: 10.0 / 4.0 = 2.50
# m/s (2.35.0–2.38.5 from the head at the push-off, 1.885 m out: 2.029); the distance per kick from the wall. From the breakout to the
# +15 5 / 2.9 = 1.724, faster than the swimming after it (65–75: 10 / 5.9 = 1.695): as measured.
$race = NewRace; Fill $race $true
$turn = $race.GetTurn(50, $true); $turn.UnderwaterDist = 10.0
$r = TurnPhase $race
Check ($null -ne $r.Turn) 'the turn at 50 m'
Check ([Math]::Abs($r.Turn.HeadAtPushoff - 1.885) -lt 1e-9) ('the head at the push-off (the bound of the rule of the start): ' + $r.Turn.HeadAtPushoff)
$v = $r.Turn.Value('uwVel')
Check (Near $v 2.5 0.0006) ('underwater speed from the wall: ' + $v)
$kd = $r.Turn.Value('kickDist')
Check (Near $kd (10.0 / 6) 0.0006) ('distance per kick from the wall: ' + $kd)
$b15 = $r.Turn.Value('bo15Vel')
Check (Near $b15 (5 / (33.5 - 30.6)) 0.0005) ('from the breakout to the 15 m: ' + $b15)
Check ($null -eq $r.Turn.SpeedNote -and $null -eq $r.Turn.RuleNote -and $r.Turn.PushAway -eq 0) ('no note, no rule: ' + $r.Turn.SpeedNote + ' / ' + $r.Turn.RuleNote)
$body = Sheet $race $r
Check (Near $body.race.manual.turns.'50'.uwSpeed 2.5 0.0006) ('uwSpeed sent: ' + $body.race.manual.turns.'50'.uwSpeed)
Check (Near $body.race.manual.turns.'50'.bo15Speed (5 / (33.5 - 30.6)) 0.0006) ('bo15Speed sent: ' + $body.race.manual.turns.'50'.bo15Speed)
Check (Near $body.race.manual.turns.'50'.underwaterDist 10.0 1e-9) 'the distance measured sent'
Check ($null -eq $body.race.manual.turns.'50'.underwaterEstimated) 'a measured distance is not said to be an estimate'
$json = $tClient.GetMethod('Build').Invoke($null, @($race, $r.Computed, $r.Phases, $timing, [long]1000, $settings, 'demo.mp4', 'update', $null, $false, $null))
Check ($json -notmatch 'τυπικό|typical|BehindHands|HeadAt|κανόνας|PushAway|OutForSpeeds|RuleNote') 'how the speeds are found is not sent'

# (2.38.6) The height of the swimmer no longer in the speed: a man of 26, a girl of 14, a body length of before: 2.50 all.
$race.AthleteGender = 'Ανδρών'; $race.AthleteBirthYear = '2000'; $race.Date = '23.11.2026'
$r = TurnPhase $race
Check ((Near $r.Turn.Value('uwVel') 2.5 0.0006) -and [Math]::Abs($r.Turn.HeadAtPushoff - 1.95) -lt 1e-9) ('a man: ' + $r.Turn.Value('uwVel'))
$race.AthleteGender = 'Γυναικών'; $race.AthleteBirthYear = '2012'
$r = TurnPhase $race
Check (Near $r.Turn.Value('uwVel') 2.5 0.0006) ('a girl of 14: ' + $r.Turn.Value('uwVel'))
$race.BodyLength = 12
$r = TurnPhase $race
Check (Near $r.Turn.Value('uwVel') 2.5 0.0006) ('a body length of 12 m of before: ' + $r.Turn.Value('uwVel'))
$body = Sheet $race $r
Check ($null -eq $body.race.manual.bodyLength -and $null -eq $body.race.manual.entryDistHips -and $null -eq $body.race.manual.entryVelHips -and $null -eq $body.race.manual.entryVelFeet) 'no body length, hips or feet sent'
$race.BodyLength = $null; $race.AthleteGender = ''; $race.AthleteBirthYear = ''

# (2.38.6) The rule of the breakout: out at 10.19 m, to the +15 4.81 / 2.9 = 1.659, 2.1% below the swimming after it (1.695): the speeds
# from 10.0847 (15 − 1.695 × 2.9), the speed to the +15 as the swimming's; the distance sent as measured. (2.39.3) 8% below (10.478,
# 0.39 m too far): the same. (2.39.4) 10.9% below (10.62, 0.54 m too far): the same too (2.39.3: as measured, more than 0.5 m). The rule
# itself: a breakout it would take nearer than the least it is given (2 m from a wall): as measured, the marks are off.
$turn.UnderwaterDist = 10.19
$r = TurnPhase $race
$next = 10 / (39.4 - 33.5)
Check ([Math]::Abs($r.Turn.NextSpeed - $next) -lt 1e-9) ('the swimming after the +15: ' + $r.Turn.NextSpeed)
Check ([Math]::Abs($r.Turn.OutForSpeeds - (15 - $next * 2.9)) -lt 1e-6) ('the breakout for the speeds: ' + $r.Turn.OutForSpeeds)
Check ((Near $r.Turn.Value('bo15Vel') $next 0.0006) -and (Near $r.Turn.Value('uwVel') ((15 - $next * 2.9) / 4.0) 0.0006)) ('to the +15 as the swimming ' + $r.Turn.Value('bo15Vel') + ', under the water ' + $r.Turn.Value('uwVel'))
Check ("$($r.Turn.RuleNote)" -like '*1.66 από τα σημάδια, γράφεται 1.69*') ('the note of the panel: ' + $r.Turn.RuleNote)
Check (Near $r.Turn.Value('kickDist') (10.19 / 6) 0.0006) ('the distance per kick as measured: ' + $r.Turn.Value('kickDist'))
$body = Sheet $race $r
Check ((Near $body.race.manual.turns.'50'.underwaterDist 10.19 1e-9) -and (Near $body.race.manual.turns.'50'.bo15Speed $next 0.0006)) ('sent: the distance measured ' + $body.race.manual.turns.'50'.underwaterDist + ', the speed ' + $body.race.manual.turns.'50'.bo15Speed)
$turn.UnderwaterDist = 10.478
$r = TurnPhase $race
Check ((Near $r.Turn.Value('bo15Vel') $next 0.0006) -and "$($r.Turn.RuleNote)" -like '*1.56 από τα σημάδια, γράφεται 1.69*' -and [Math]::Abs($r.Turn.OutForSpeeds - (15 - $next * 2.9)) -lt 1e-6) ('8% slower, 0.39 m: as the swimming (' + $r.Turn.Value('bo15Vel') + ', ' + $r.Turn.RuleNote + ')')
$body = Sheet $race $r
Check (Near $body.race.manual.turns.'50'.underwaterDist 10.478 1e-9) ('sent: the distance measured ' + $body.race.manual.turns.'50'.underwaterDist)
$turn.UnderwaterDist = 10.62
$r = TurnPhase $race
Check ((Near $r.Turn.Value('bo15Vel') $next 0.0006) -and "$($r.Turn.RuleNote)" -like '*1.51 από τα σημάδια, γράφεται 1.69*' -and [Math]::Abs($r.Turn.OutForSpeeds - (15 - $next * 2.9)) -lt 1e-6) ('10.9% slower, 0.54 m: as the swimming (' + $r.Turn.Value('bo15Vel') + ', ' + $r.Turn.RuleNote + ')')
$body = Sheet $race $r
Check (Near $body.race.manual.turns.'50'.underwaterDist 10.62 1e-9) ('sent: the distance measured ' + $body.race.manual.turns.'50'.underwaterDist)
$ruleOf = (T 'SwimSpeedRules').GetMethod('Breakout')
$near = [double]$ruleOf.Invoke($null, [object[]]@([double]10.62, [double]2.9, [double]$next, [double]1.0, [double]2.0))
$far = [double]$ruleOf.Invoke($null, [object[]]@([double]6.0, [double]8.0, [double]1.7, [double]1.0, [double]2.0))
$fast = [double]$ruleOf.Invoke($null, [object[]]@([double]10.0, [double]2.9, [double]$next, [double]1.0, [double]2.0))
Check ([Math]::Abs($near - (15 - $next * 2.9)) -lt 1e-9 -and $far -eq 6.0 -and $fast -eq 10.0) ('the rule: 10.62 -> ' + $near + '; one that would be 1.4 m from the wall: as measured (' + $far + '); faster: as measured (' + $fast + ')')
$turn.UnderwaterDist = 10.0

# The distance estimated from the times: no speed from it.
$turn.UnderwaterEstimated = $true
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed from an estimated distance'
Check ($null -eq $r.Turn.Value('kickDist')) 'no distance per kick from an estimated distance'
Check ("$($r.Turn.SpeedNote)" -like '*εκτίμηση*') ('the note: ' + $r.Turn.SpeedNote)
$body = Sheet $race $r
Check ($body.race.manual.turns.'50'.underwaterEstimated -eq $true) 'the estimate is said to the Sheet'

# A breakout typed at 0.8 m: under the metre from the wall: no speed, a note. (2.38.6: from the wall; 2.35.0–2.38.5 2.5 m was 0.6
# from the head.) At 2.5 m: 0.625 m/s, not a speed a swimmer has: none, the note says so.
$turn.UnderwaterEstimated = $false; $turn.UnderwaterDist = 0.8
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel')) 'no speed for 0.8 m'
Check ("$($r.Turn.SpeedNote)" -like '*κοντά στον τοίχο*') ('the note: ' + $r.Turn.SpeedNote)
$turn.UnderwaterDist = 2.5
$r = TurnPhase $race
Check ($null -eq $r.Turn.Value('uwVel') -and "$($r.Turn.SpeedNote)" -like '*αποκόλληση → έξοδος: 0.6? m/s δεν γίνεται*') ('2.5 m in 4 s: ' + $r.Turn.SpeedNote)

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
Check (Near $r.Turn.Value('uwVel') 2.5 0.0006) ('the underwater speed from its own marks: ' + $r.Turn.Value('uwVel'))

# The start in phases: a man of 26; entry of the hands at 1.04 s, 3.30 m; three kicks; breakout at 5.30 s, 13.0 m; 15 m at 6.20 s.
# (2.38.6) Under the water from where the hands entered, the previous model (2.35.0–2.38.5 from the head 0.4995 m behind them).
$race4 = NewRace; Fill $race4 $true
$race4.AthleteGender = 'Ανδρών'; $race4.AthleteBirthYear = '2000'; $race4.Date = '23.11.2026'
Mark $race4 'Entry' 1.04 $null
foreach ($k in @(2.0, 2.6, 3.2)) { Mark $race4 'Kick' $k $null }
Mark $race4 'Breakout' 5.30 $null
$race4.EntryDistHands = 3.30; $race4.BreakoutDist = 13.0
$r = TurnPhase $race4
$start = $null
foreach ($p in $r.Phases) { if ($p.Kind.ToString() -eq 'Start') { $start = $p } }
Check ($null -ne $start) 'the start'
Check (Near $start.Value('sigEntryVel') (3.30 / 1.04) 0.0006) ('signal → entry of the hands: ' + $start.Value('sigEntryVel'))
$way = 13.0 - 3.30
Check (Near $start.Value('uwVel') ($way / (5.30 - 1.04)) 0.0006) ('entry of the hands → breakout: ' + $start.Value('uwVel'))
Check (Near $start.Value('bo15Vel') (2.0 / 0.9) 0.0006) ('breakout → 15: ' + $start.Value('bo15Vel'))
Check (Near $start.Value('kickDist') ($way / 3) 0.0006) ('distance per kick from where the hands entered: ' + $start.Value('kickDist'))
$body = Sheet $race4 $r
$m = $body.race.manual
Check ((Near $m.sigEntryVel (3.30 / 1.04) 0.0006) -and (Near $m.entryBreakoutVel ($way / 4.26) 0.0006) -and (Near $m.breakout15Vel (2.0 / 0.9) 0.0006)) ('the three phases sent: ' + $m.sigEntryVel + ' / ' + $m.entryBreakoutVel + ' / ' + $m.breakout15Vel)
# Without the entry distance: no speed from the signal or from the entry; a note.
$race4.EntryDistHands = $null
$r = TurnPhase $race4
foreach ($p in $r.Phases) { if ($p.Kind.ToString() -eq 'Start') { $start = $p } }
Check ($null -eq $start.Value('sigEntryVel') -and $null -eq $start.Value('uwVel') -and $null -ne $start.Value('bo15Vel')) 'without the entry distance: only breakout → 15'
Check ("$($start.SpeedNote)" -like '*απόσταση εισόδου*') ('the note: ' + $start.SpeedNote)
$body = Sheet $race4 $r
Check ($null -eq $body.race.manual.sigEntryVel -and $null -eq $body.race.manual.entryBreakoutVel) 'nothing sent for them'

# (2.35.1) Late breakouts, the analyst's request: the speed to 15 for every breakout before it (2.9.8–2.35.0 only 2 m and more before).
# A turn out at 13.5 m: breakout at 32.8 s, +15 (65 m) at 33.5 s: 1.5 / 0.7 = 2.142857; push-off → breakout 13.5 / 6.2 (from the wall).
$race5 = NewRace; Fill $race5 $true 26.3 32.8
$t5 = $race5.GetTurn(50, $true); $t5.UnderwaterDist = 13.5
$r = TurnPhase $race5
Check (Near $r.Turn.Value('bo15Vel') (1.5 / 0.7) 0.0006) ('a turn out at 13.5 m, breakout → +15: ' + $r.Turn.Value('bo15Vel'))
Check (Near $r.Turn.Value('uwVel') (13.5 / 6.2) 0.0006) ('and push-off → breakout: ' + $r.Turn.Value('uwVel'))
Check ($null -eq $r.Turn.SpeedNote) ('no note: ' + $r.Turn.SpeedNote)
$body = Sheet $race5 $r
Check (Near $body.race.manual.turns.'50'.bo15Speed (1.5 / 0.7) 0.0006) ('bo15Speed sent: ' + $body.race.manual.turns.'50'.bo15Speed)
# A start out at 14.2 m (0.8 m before the 15): breakout at 5.84 s, 15 m at 6.20 s: 0.8 / 0.36; entry → breakout (14.2 − 3.30) / 4.80.
$race6 = NewRace; Fill $race6 $true
$race6.AthleteGender = 'Ανδρών'; $race6.AthleteBirthYear = '2000'; $race6.Date = '23.11.2026'
Mark $race6 'Entry' 1.04 $null
foreach ($k in @(2.0, 2.6, 3.2)) { Mark $race6 'Kick' $k $null }
Mark $race6 'Breakout' 5.84 $null
$race6.EntryDistHands = 3.30; $race6.BreakoutDist = 14.2
$r = TurnPhase $race6
foreach ($p in $r.Phases) { if ($p.Kind.ToString() -eq 'Start') { $start = $p } }
Check (Near $start.Value('bo15Vel') (0.8 / 0.36) 0.0006) ('a start out at 14.2 m, breakout → 15: ' + $start.Value('bo15Vel'))
Check (Near $start.Value('uwVel') ((14.2 - 3.30) / 4.80) 0.0006) ('and entry → breakout: ' + $start.Value('uwVel'))
Check ($null -eq $start.SpeedNote) ('no note: ' + $start.SpeedNote)
$body = Sheet $race6 $r
Check (Near $body.race.manual.breakout15Vel (0.8 / 0.36) 0.0006) ('breakout15Vel sent: ' + $body.race.manual.breakout15Vel)
# Out at 15.2 m (past the 15, inside the margin of a measure): no speed to 15, the note says why.
$race6.BreakoutDist = 15.2
$r = TurnPhase $race6
foreach ($p in $r.Phases) { if ($p.Kind.ToString() -eq 'Start') { $start = $p } }
Check ($null -eq $start.Value('bo15Vel') -and $null -ne $start.Value('uwVel')) 'out at 15.2 m: no speed to 15, entry → breakout still'
Check ("$($start.SpeedNote)" -like '*15.2*δεν είναι πριν*') ('the note: ' + $start.SpeedNote)
# The breakout marked after the head at 15 (6.30 s, the 15 m at 6.20 s): no speed, the note says to check the marks.
$race6.BreakoutDist = 14.2
foreach ($mk in $race6.Marks) { if ($mk.Type.ToString() -eq 'Breakout' -and $mk.Timestamp -eq [long]6840) { $mk.Timestamp = [long]7300 } }
$r = TurnPhase $race6
foreach ($p in $r.Phases) { if ($p.Kind.ToString() -eq 'Start') { $start = $p } }
Check ($null -eq $start.Value('bo15Vel')) 'the breakout marked after the 15: no speed to 15'
Check ("$($start.SpeedNote)" -like '*μετά το κεφάλι στα 15*') ('the note: ' + $start.SpeedNote)

# (2.38.5, 2.38.6) The analyst's rule of the start: under the water after the push off a wall not faster than after the start (the dive
# gives more), neither on the mean nor at the highest speed the video shows (SwimSpeedRules.Peak). The start out at 14.2 m (entry →
# breakout 10.9 / 4.80 = 2.2708 m/s, to the 15 2.2222, the highest on the video 2.3161). A turn out at 10.0 m: from the wall 2.50 m/s:
# its speed starts 1.60 m out (within the body length of a man, 1.95): 2.0988, its highest as the start's; no warning, a note. Out at
# 12.0 m (2.39.4: with the +15 earlier, below): past the body length and the warning of the panel. Not in a relay.
$race7 = NewRace; Fill $race7 $true
$race7.AthleteGender = 'Ανδρών'; $race7.AthleteBirthYear = '2000'; $race7.Date = '23.11.2026'
Mark $race7 'Entry' 1.04 $null
Mark $race7 'Breakout' 5.84 $null
$race7.EntryDistHands = 3.30; $race7.BreakoutDist = 14.2
$t7 = $race7.GetTurn(50, $true); $t7.UnderwaterDist = 10.0
$fasterOf = $tTech.GetMethod('FasterThanStart')
$tRules = T 'SwimSpeedRules'
$peakOf = $tRules.GetMethod('Peak')
function Phases7() {
  $r7 = TurnPhase $race7
  $s7 = $null; foreach ($p in $r7.Phases) { if ($p.Kind.ToString() -eq 'Start') { $s7 = $p } }
  return @{ Turn = $r7.Turn; Start = $s7 }
}
function Faster($q) { return $fasterOf.Invoke($null, [object[]]@($race7.psobject.BaseObject, $q.Turn.psobject.BaseObject, $q.Start.psobject.BaseObject)) }
function PeakOf($under, $after, $ut, $at) { return [double]$peakOf.Invoke($null, [object[]]@([double]$under, [double]$after, [double]$ut, [double]$at)) }
$q = Phases7
$startUw = (14.2 - 3.30) / 4.80; $startPeak = PeakOf $startUw (0.8 / 0.36) 4.80 0.36
Check ((Near $q.Start.Value('uwVel') $startUw 0.0006) -and [Math]::Abs($q.Start.UnderwaterMean - $startUw) -lt 1e-6 -and [Math]::Abs($q.Start.UnderwaterPeak - $startPeak) -lt 1e-6) ('the start: ' + $q.Start.Value('uwVel') + ', the highest on the video ' + $q.Start.UnderwaterPeak)
$turnUw = [double]$q.Turn.Value('uwVel')
$turnPeak = PeakOf ((10.0 - $q.Turn.PushAway) / 4.0) (5 / 2.9) 4.0 2.9
Check ((Near $turnUw 2.0988 0.0006) -and $turnUw -le $startUw + 1e-9) ('a turn out at 10.0 m: ' + $turnUw + ' (from the wall 2.50), not above the start ' + $startUw)
Check ($q.Turn.PushAway -gt 1.55 -and $q.Turn.PushAway -lt 1.95 -and -not $q.Turn.PastHead) ('its speed from ' + $q.Turn.PushAway + ' m out, within the body length (1.95)')
Check ([Math]::Abs($turnPeak - $startPeak) -lt 1e-6) ('its highest on the video as the start: ' + $turnPeak + ' / ' + $startPeak)
Check ($null -eq (Faster $q)) 'within the body length: no warning'
Check ("$($q.Turn.RuleNote)" -like '*2.50 από τα σημάδια, γράφεται 2.10: όχι πιο γρήγορα από την εκκίνηση*') ('the note of the panel: ' + $q.Turn.RuleNote)
Check (Near $q.Turn.Value('kickDist') (10.0 / 6) 0.0006) ('the distance per kick as measured: ' + $q.Turn.Value('kickDist'))
$body = Sheet $race7 (TurnPhase $race7)
Check ((Near $body.race.manual.turns.'50'.uwSpeed 2.0988 0.0006) -and (Near $body.race.manual.turns.'50'.underwaterDist 10.0 1e-9)) ('sent: ' + $body.race.manual.turns.'50'.uwSpeed + ' from ' + $body.race.manual.turns.'50'.underwaterDist + ' m')
# (2.39.4) Out at 12.0 m with the +15 at 32.3 s (not 33.5): from the breakout to the +15 3 / 1.7 = 1.76, not below the swimming
# after it (10 / 7.1 = 1.41), so the rule of the breakout (now however far) leaves it as measured; under the water 12.0 / 4.0 = 3.0
# m/s, so far above the start that its speed would start past the body length (at least 12 − 2.2708 × 4 = 2.92 m out): the warning.
foreach ($mk in $race7.Marks) { if ($mk.Type.ToString() -eq 'Split' -and $mk.Distance -eq 65) { $mk.Timestamp = [long](1000 + 32300) } }
$t7.UnderwaterDist = 12.0
$q = Phases7
$w = Faster $q
# (The speed of the start as the panel shows it, to the hundredth.)
$startShown = ([double]$q.Start.Value('uwVel')).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture)
$uw12 = $q.Turn.Value('uwVel')
Check ($null -ne $uw12 -and [double]$uw12 -le $startUw + 1e-6 -and [double]$uw12 -gt 1.0 -and $q.Turn.PushAway -gt 2.9 -and $q.Turn.PastHead -and [Math]::Abs($q.Turn.OutForSpeeds - 12.0) -lt 1e-9 -and "$w" -like ('*πολύ πιο γρήγορη από την εκκίνηση (' + $startShown + ' m/s)*')) ('a turn out at 12.0 m (' + $uw12 + ', from ' + $q.Turn.PushAway + ' m): ' + $w)
$race7.Relay = $true
Check ($null -eq (Faster $q)) 'not in a relay'
$race7.Relay = $false
foreach ($mk in $race7.Marks) { if ($mk.Type.ToString() -eq 'Split' -and $mk.Distance -eq 65) { $mk.Timestamp = [long](1000 + 33500) } }

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'speeds of the start and of the turns: all checks passed'
