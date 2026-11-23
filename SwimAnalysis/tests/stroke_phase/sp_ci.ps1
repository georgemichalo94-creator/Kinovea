# sp_ci.ps1: the CI check of the automatic strokes in one phase of the cycle, of the counts the analyst accepts and of the speed from the
# signal to the breakout (2.34.0), on FICTIONAL strokes and a FICTIONAL 100 m butterfly (50 m pool).
#  1. The beats of the stroke reading follow the strongest motion of each cycle, and a cycle can have two: in the middle of a length they
#     went from the one to the other (a step: every stroke after it a third of a second early) or one beat alone was on the other one.
#     SwimStrokeDetector.RepairBeatPhase brings the strokes of the length to one phase, with the same number of strokes; what is not a
#     step stays as it is (one arm slower than the other in freestyle, a gap, the breakout stroke, the stroke into the wall, a stroke
#     too many, a short length).
#  2. In the analysis: the rate of the 10 m with the step was 57.3 for 53.6 cycles a minute; with the strokes in one phase 53.6.
#  3. A count accepted as shown (Enter) stays the count of the marks; it stands as a typed one only against a whole stroke more or
#     less; a typed one always stands. What the Google Sheet gets says the same.
#  4. The average speed from the signal to the breakout is sent when only the distance of the breakout was measured.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File sp_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
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
$tPresets = T 'SwimPresets'; $tDetector = T 'SwimStrokeDetector'; $tPayload = T 'SwimPayload'; $tTech = T 'SwimTechCalculator'; $tSettings = T 'SwimSettings'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function F2($v) { return ([double]$v).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) }
$tCalc.GetField('LengthFromRate').SetValue($null, $false)

# ---------------------------------------------------------------------------------------------------------------------------------
# 1. The strokes of a length in one phase (frames of the reading).
# ---------------------------------------------------------------------------------------------------------------------------------
$repair = $tDetector.GetMethod('RepairBeatPhase', [System.Reflection.BindingFlags]'NonPublic,Static')
# (The list and the arrays from .NET itself: what New-Object makes is wrapped by PowerShell and a method called by reflection refuses it.)
function Repair($beats, $onset, $first, $last, $stride) {
  $list = [Activator]::CreateInstance([System.Collections.Generic.List[int]])
  foreach ($b in $beats) { $list.Add([int]$b) }
  $call = [Array]::CreateInstance([object], 6)
  $call[0] = $list; $call[1] = $null; $call[2] = [int]$first; $call[3] = [int]$last; $call[4] = [int]$stride; $call[5] = [int]0
  if ($null -ne $onset) { $call[1] = $onset.PSObject.BaseObject }
  $moved = $repair.Invoke($null, $call)
  return @{ Beats = @($list.ToArray()); Moved = [int]$moved; Steps = [int]$call[5] }
}
function Gaps($beats) { $g = @(); for ($i = 1; $i -lt $beats.Count; $i++) { $g += ($beats[$i] - $beats[$i - 1]) }; return $g }
function Same($x, $y) { if ($x.Count -ne $y.Count) { return $false }; for ($i = 0; $i -lt $x.Count; $i++) { if ($x[$i] -ne $y[$i]) { return $false } }; return $true }
function Kept($r, $beats) { return ($r.Moved -eq 0 -and $r.Steps -eq 0 -and (Same $r.Beats $beats)) }
$far = 1000000

# Butterfly at 30 frames a second: a stroke every 32 frames from frame 150. From the tenth stroke on the beats are 11 frames early (the
# arms coming out of the water, not their entry), and the fifteenth alone is back on the entry.
$base = @(); for ($i = 0; $i -lt 20; $i++) { $base += (150 + 32 * $i) }
$mixed = @(); for ($i = 0; $i -lt 20; $i++) { if ($i -le 8 -or $i -eq 14) { $mixed += $base[$i] } else { $mixed += ($base[$i] - 11) } }
$early = @(); foreach ($b in $base) { $early += ($b - 11) }
$g0 = @(Gaps $mixed | Sort-Object -Unique)
Check ((Same $g0 @(21, 32, 43))) ('as read: one interval of 21 frames (the step), then one of 43 and one of 21 around the stroke alone')

$r = Repair $mixed $null 0 $far 1
Check ($r.Moved -eq 1 -and $r.Steps -eq 1 -and $r.Beats.Count -eq 20) ('one stroke alone moved, one step closed, the same 20 strokes')
Check ((Same @(Gaps $r.Beats | Sort-Object -Unique) @(32))) 'every interval 32 frames'
Check ((Same $r.Beats $early)) ('without the stroke signal: at the phase of the most strokes (the first stroke at frame ' + $r.Beats[0] + ')')

$r = Repair $mixed $null 145 $far 1
Check ($r.Steps -eq 1 -and (Same $r.Beats $base)) ('no stroke before the first frame of the search (145): the others come to the first ones (the first stroke at ' + $r.Beats[0] + ')')
$r = Repair $mixed $null 0 750 1
Check ($r.Steps -eq 1 -and (Same $r.Beats $early)) ('no stroke after the last frame of the search (750): the last stroke at ' + $r.Beats[19])
$r = Repair $mixed $null 145 750 1
Check ($r.Moved -eq 1 -and $r.Steps -eq 0 -and $r.Beats[0] -eq 150 -and $r.Beats[19] -eq 747 -and $r.Beats[14] -eq ($base[14] - 11)) 'neither fits the search: only the stroke alone is moved, the step stays'

# With the stroke signal: the phase with the most of it, whichever has the most strokes.
function Signal($strong, $weak) {
  $o = [Array]::CreateInstance([double], 1000)
  for ($i = 0; $i -lt 1000; $i++) { $o[$i] = -0.5 }
  foreach ($b in $base) {
    $o[$b] = $strong; $o[$b - 1] = 0.5 * $strong; $o[$b + 1] = 0.5 * $strong
    $o[$b - 11] = $weak; $o[$b - 12] = 0.5 * $weak; $o[$b - 10] = 0.5 * $weak
  }
  return ,$o
}
$r = Repair $mixed (Signal 4.0 2.5) 0 $far 1
Check ($r.Steps -eq 1 -and (Same $r.Beats $base)) 'the stroke signal stronger at the entry of the arms: every stroke there'
$r = Repair $mixed (Signal 2.5 4.0) 0 $far 1
Check ($r.Steps -eq 1 -and (Same $r.Beats $early)) 'stronger 11 frames before it: every stroke there'

# There and back: seven strokes in the middle 11 frames early.
$back = @(); for ($i = 0; $i -lt 20; $i++) { if ($i -ge 6 -and $i -le 12) { $back += ($base[$i] - 11) } else { $back += $base[$i] } }
$r = Repair $back $null 0 $far 1
Check ($r.Steps -eq 2 -and (Same $r.Beats $base)) 'there and back: two steps closed, every stroke at the phase of the most'

# A tempo that slows down along the length (30 to 34 frames) with a step of 10 frames: the intervals of the swimmer come back.
$drift = @(150); for ($i = 0; $i -lt 19; $i++) { $drift += ($drift[$i] + 30 + [Math]::Floor($i / 4)) }
$stepped = @(); for ($i = 0; $i -lt 20; $i++) { if ($i -le 9) { $stepped += $drift[$i] } else { $stepped += ($drift[$i] - 10) } }
$r = Repair $stepped $null 0 $far 1
$g1 = @(Gaps $r.Beats); $g2 = @(Gaps $drift); $worst = 0
for ($i = 0; $i -lt $g1.Count; $i++) { $worst = [Math]::Max($worst, [Math]::Abs($g1[$i] - $g2[$i])) }
Check ($r.Steps -eq 1 -and $worst -le 1) ('a tempo that slows down, with a step: the intervals of the swimmer within a frame (' + $worst + ')')

# What is not a step stays as it is.
$fs = @(100); for ($i = 0; $i -lt 24; $i++) { if ($i % 2 -eq 0) { $fs += ($fs[$i] + 15) } else { $fs += ($fs[$i] + 19) } }
Check (Kept (Repair $fs $null 0 $far 2) $fs) 'freestyle, one arm slower than the other (15 and 19 frames): as it is'
$fsStep = @(); for ($i = 0; $i -lt 25; $i++) { if ($i -lt 12) { $fsStep += $fs[$i] } else { $fsStep += ($fs[$i] - 6) } }
$r = Repair $fsStep $null 0 $far 2
Check ($r.Steps -eq 1 -and (Same @(Gaps $r.Beats | Sort-Object -Unique) @(15, 19))) 'freestyle with a step of 6 frames: closed, each arm with its own interval again'
$gap = @(); for ($i = 0; $i -lt 20; $i++) { if ($i -lt 9 -or $i -gt 11) { $gap += $base[$i] } }
Check (Kept (Repair $gap $null 0 $far 1) $gap) 'a gap (three strokes not found): as it is'
$edge = @(150); for ($i = 0; $i -lt 18; $i++) { if ($i -eq 0) { $edge += ($edge[$i] + 17) } elseif ($i -eq 17) { $edge += ($edge[$i] + 45) } else { $edge += ($edge[$i] + 32) } }
Check (Kept (Repair $edge $null 0 $far 1) $edge) 'the breakout stroke half an interval before the next, a long last stroke into the wall: as they are'
$few = @(150, 182, 214, 235, 267, 299)
Check (Kept (Repair $few $null 0 $far 1) $few) 'six strokes: as they are'
$double = @(); for ($i = 0; $i -lt 20; $i++) { $double += $base[$i]; if ($i -eq 9) { $double += ($base[$i] + 16) } }
Check (Kept (Repair $double $null 0 $far 1) $double) 'a stroke too many (two half intervals): as it is, the count is the analyst''s'
$two = @(); for ($i = 0; $i -lt 20; $i++) { if ($i -le 5) { $two += $base[$i] } elseif ($i -le 12) { $two += ($base[$i] - 10) } else { $two += ($base[$i] - 20) } }
Check (Kept (Repair $two $null 0 $far 1) $two) 'two steps the same way (20 frames in all, more than a step): as they are'
$steady = @(); $jitter = @(0, 1, -1, 0, 1, 0, -1, 1, 0, 0, -1, 1, 0, -1, 0, 1, 0, -1, 1, 0)
for ($i = 0; $i -lt 20; $i++) { $steady += ($base[$i] + $jitter[$i]) }
Check (Kept (Repair $steady $null 0 $far 1) $steady) 'strokes read a frame before or after their own: as they are'

# ---------------------------------------------------------------------------------------------------------------------------------
# 2. In the analysis: a fictional 100 m butterfly at 25 frames a second, a stroke every 28 frames (1.12 s).
# ---------------------------------------------------------------------------------------------------------------------------------
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 40; $timing.FrameIntervalMilliseconds = 40; $timing.HighSpeedFactor = 1
function Mark($race, $type, $seconds, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
$splits = @{ 15 = 6.00; 25 = 11.60; 35 = 17.48; 45 = 23.40; 50 = 26.20; 65 = 33.60; 75 = 39.52; 85 = 45.60; 95 = 51.80; 100 = 55.00 }
$true1 = @(); for ($f = 124; $f -lt 656; $f += 28) { $true1 += $f }
$true2 = @(); for ($f = 770; $f -lt 1375; $f += 28) { $true2 += $f }
function NewRace($frames1) {
  $race = [Activator]::CreateInstance($tRace)
  $race.Style = 'fly'; $race.Distance = 100; $race.Pool = 50
  $race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(100, 50, $false))
  $race.StartTimestamp = [long]1000
  foreach ($d in $splits.Keys) { Mark $race 'Split' $splits[$d] $d }
  Mark $race 'Breakout' 4.6 $null
  Mark $race 'Breakout' 30.0 $null
  foreach ($f in $frames1) { Mark $race 'Stroke' ($f / 25.0) $null }
  foreach ($f in $true2) { Mark $race 'Stroke' ($f / 25.0) $null }
  return $race
}
function Compute($race) { return $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5)) }
function Row($computed, $d) { foreach ($row in $computed.Rows) { if ($row.D -eq $d) { return $row } }; return $null }
function Total($computed) { $s = 0; foreach ($row in $computed.Rows) { if ($row.Strokes) { $s += $row.Strokes } }; return $s }

$entries = Compute (NewRace $true1)
$rate = (Row $entries 35).Sr
Check ((Row $entries 35).SrFromTimes -and [Math]::Abs($rate - 60 / 1.12) -lt 1e-6) ('the strokes at the entry of the arms: 25-35 m at ' + (F2 $rate) + ' cycles a minute')

# The automatic strokes as they were read: from the ninth one (13.92 s, in 25-35 m) 9 frames early, the fourteenth alone at its entry.
$auto1 = @(); for ($i = 0; $i -lt $true1.Count; $i++) { if ($i -lt 8 -or $i -eq 13) { $auto1 += $true1[$i] } else { $auto1 += ($true1[$i] - 9) } }
$read = Compute (NewRace $auto1)
$off = (Row $read 35).Sr
Check ([Math]::Abs($off - $rate) -gt 3) ('as read, with the step in it: 25-35 m at ' + (F2 $off) + ' cycles a minute (' + (F2 (100 * ($off - $rate) / $rate)) + '%)')

$r = Repair $auto1 $null 0 $far 1
$fixed = Compute (NewRace $r.Beats)
$now = (Row $fixed 35).Sr
Check ($r.Moved -eq 1 -and $r.Steps -eq 1 -and (Row $fixed 35).SrFromTimes -and [Math]::Abs($now - $rate) -lt 0.01) ('in one phase: 25-35 m at ' + (F2 $now) + ' cycles a minute, from the times of the strokes')
Check ((Total $fixed) -eq (Total $read) -and (Total $fixed) -eq (Total $entries)) ('the strokes of the race do not change: ' + (Total $fixed))
$bad = 0; foreach ($d in @(25, 35, 45, 75, 85, 95)) { if ([Math]::Abs((Row $fixed $d).Sr - (Row $entries $d).Sr) -gt 0.01) { $bad++ } }
Check ($bad -eq 0) ('every segment of clean swimming at the rate of the swimmer (' + $bad + ' apart)')
$r = Repair $auto1 $null 120 $far 1
Check ((Same $r.Beats $true1)) 'with the reading started after the breakout (frame 120) the strokes come to the entries themselves'

# ---------------------------------------------------------------------------------------------------------------------------------
# 3. The counts the analyst accepts (Enter) and those he types.
# ---------------------------------------------------------------------------------------------------------------------------------
$marks35 = (Row $entries 35).Strokes
function WithCounts($typed, $accepted) {
  $race = NewRace $true1
  if ($null -ne $typed) { foreach ($d in $typed.Keys) { $race.StrokeCounts[[int]$d] = [double]$typed[$d] } }
  if ($null -ne $accepted) { foreach ($d in $accepted.Keys) { $race.StrokeConfirmed[[int]$d] = [double]$accepted[$d] } }
  return $race
}
function Warned($computed) { foreach ($w in $computed.Warnings) { if ("$w".Contains('δέχτηκες')) { return $true } }; return $false }

$c = Compute (WithCounts $null @{ 35 = $marks35 })
$row = Row $c 35
Check ($row.StrokesConfirmed -and -not $row.StrokesTyped -and -not $row.StrokesChanged -and $row.Strokes -eq $marks35 -and $row.SrFromTimes -and -not (Warned $c)) ('accepted as the marks give it (' + $marks35 + '): the count of the marks, the rate from the times, ✓')
$c = Compute (WithCounts $null @{ 35 = ($marks35 - 0.5) })
$row = Row $c 35
Check (-not $row.StrokesConfirmed -and -not $row.StrokesTyped -and $row.Strokes -eq $marks35 -and $row.SrFromTimes -and [Math]::Abs($row.Sr - $rate) -lt 1e-6 -and -not (Warned $c)) ('accepted ' + ($marks35 - 0.5) + ' and the marks now give ' + $marks35 + ' (the length was read again): the marks stand, ' + (F2 $row.Sr) + ' cycles a minute (until 2.33.0 ' + (F2 (($marks35 - 0.5) * 60 / $row.SegTime)) + ')')
$c = Compute (WithCounts $null @{ 35 = ($marks35 + 1) })
$row = Row $c 35
Check ($row.StrokesTyped -and $row.StrokesChanged -and $row.Strokes -eq ($marks35 + 1) -and -not $row.SrFromTimes -and (Warned $c)) ('accepted ' + ($marks35 + 1) + ' and the marks now give a whole stroke less: his count stands and the data check says it')
$c = Compute (WithCounts @{ 35 = ($marks35 - 0.5) } $null)
$row = Row $c 35
Check ($row.StrokesTyped -and -not $row.StrokesChanged -and $row.Strokes -eq ($marks35 - 0.5) -and -not $row.SrFromTimes -and -not (Warned $c)) ('typed ' + ($marks35 - 0.5) + ': it stands (strokes over the time)')
$c = Compute (WithCounts @{ 35 = 7 } @{ 35 = $marks35 })
$row = Row $c 35
Check ($row.StrokesTyped -and -not $row.StrokesChanged -and $row.Strokes -eq 7) 'typed and accepted before: the typed one'

# What the Google Sheet gets: the typed counts and the accepted ones that stand; not the accepted ones the marks still give.
$settings = [Activator]::CreateInstance($tSettings)
function Body($race) {
  $computed = Compute $race
  $phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
  $text = [string]$tPayload.GetMethod('Build').Invoke($null, @($race, $computed, $phases, $timing, [long]0, $settings, 'fictional.mp4', 'update', $null, $false, $null))
  return ($text | ConvertFrom-Json)
}
$sent = $null
try { $sent = (Body (WithCounts @{ 45 = 4 } @{ 25 = (Row $entries 25).Strokes; 35 = ($marks35 + 1) })).race.segmentStrokes } catch { Write-Output ('FAIL what the sheet gets: ' + $_.Exception.Message); $fails++ }
$names = @(); if ($null -ne $sent) { $names = @($sent.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object) }
Check ($names.Count -eq 2 -and $names[0] -eq '35' -and $names[1] -eq '45' -and $sent.'35' -eq ($marks35 + 1) -and $sent.'45' -eq 4) ('the sheet gets the typed count (45 m) and the accepted one that stands (35 m), not the one the marks still give (25 m): ' + ($names -join ', '))
$none = $null
try { $none = (Body (WithCounts $null @{ 25 = (Row $entries 25).Strokes; 35 = $marks35 })).race.segmentStrokes } catch { Write-Output ('FAIL what the sheet gets: ' + $_.Exception.Message); $fails++ }
Check ($null -eq $none) 'only counts accepted as the marks give them: none is sent (the script counts the marks as the program)'

# ---------------------------------------------------------------------------------------------------------------------------------
# 4. The average speed from the signal to the breakout with the distance of the breakout alone (📍).
# ---------------------------------------------------------------------------------------------------------------------------------
function Manual($breakout, $underwater, $estimated) {
  $race = NewRace $true1
  if ($null -ne $breakout) { $race.BreakoutDist = [double]$breakout }
  if ($null -ne $underwater) { $race.UnderwaterDist = [double]$underwater }
  $race.UnderwaterEstimated = [bool]$estimated
  return (Body $race).race.manual
}
try {
  $m = Manual 12.5 $null $false
  Check ([Math]::Abs($m.signalBreakoutVel - 12.5 / 4.6) -lt 0.001) ('only the distance of the breakout (12.5 m at 4.60 s): ' + (F2 $m.signalBreakoutVel) + ' m/s is sent')
  $m = Manual 12.5 11.9 $true
  Check ([Math]::Abs($m.signalBreakoutVel - 12.5 / 4.6) -lt 0.001) 'with the distance under the water an estimate of the times: sent too'
  $m = Manual 12.5 11.8 $false
  Check ($null -eq $m.signalBreakoutVel) 'with the distance under the water measured: the script computes it as before (nothing sent)'
  $m = Manual $null $null $false
  Check ($null -eq $m.signalBreakoutVel) 'without a distance: nothing'
} catch { Write-Output ('FAIL the speed from the signal to the breakout: ' + $_.Exception.Message); $fails++ }

if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'Strokes in one phase, accepted counts, signal to breakout: all checks passed.'
