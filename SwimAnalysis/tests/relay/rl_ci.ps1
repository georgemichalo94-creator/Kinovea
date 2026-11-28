# rl_ci.ps1: the CI check of the relays (2.42.0) on FICTIONAL teams and swimmers (no real result, name or number): the swimmers of a
# relay from the KOE live results (their order, their numbers, the time of each leg from SPLIT_TIME or from the team's times), from
# pasted results (their lines under the team's line: the times of the legs, or the team's times at the ends of the legs), found in a
# fictional KOE App (by the number, by the name and year, or two of the same family name to choose from); the exchanges of a race (the
# touch, the feet off the block, their time, the zones, what is written, the time running until F on the bar and on the HUD); the style
# of each leg of a medley relay; the English of the new texts; pictures of the exchange on the HUD.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code: the number of failed checks.
#   powershell -File rl_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> [-out <folder of the pictures>]
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
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
[void][System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll'))
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
function F2($v) { if ($null -eq $v) { return 'null' }; return ([double]$v).ToString('0.00', [Globalization.CultureInfo]::InvariantCulture) }
function Near($x, $y, $d = 0.005) { return $null -ne $x -and [Math]::Abs([double]$x - [double]$y) -le $d }
$static = [System.Reflection.BindingFlags]'NonPublic,Public,Static'
$tRace = T 'SwimRace'; $tMark = T 'SwimMark'; $tType = T 'SwimMarkType'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'
$tPresets = T 'SwimPresets'; $tReel = T 'SwimReel'; $tRelay = T 'SwimRelay'; $tResults = T 'SwimOfficialResults'; $tUnit = T 'SwimOfficialUnit'
$tPaste = T 'SwimResultsPaste'; $tLeg = T 'SwimRelayLeg'; $tManager = T 'SwimAnalysisManager'; $tAthlete = T 'SwimAthlete'; $tList = T 'SwimAthleteList'

# ---- 1. The KOE live results of a relay: a FICTIONAL answer in their format (two teams: the times of the legs given, and not given).
$json = @'
{ "Results": [ { "Results": { "Results": [
 { "StartOrder": 4, "CompetitorCode": 5001, "OrganisationCode": "ΟΜΙΛΟΣ ΔΕΙΓΜΑ", "IRM_ENG": "", "Result": "3:18.40", "NationCode": "GRE",
   "Composition": { "Athlete": [
     { "Order": 2, "CompetitorCode": 990102, "Description": [ { "FamilyName": "ΚΥΡΙΑΖΟΠΟΥΛΟΣ", "GivenName": "Ηλίας", "FamilyName_ENG": "KYRIAZOPOULOS", "GivenName_ENG": "Ilias", "GenderCode_ENG": "M", "BirthDate": "2006-02-01T00:00:00", "OrganisationCode": "ΟΜΙΛΟΣ ΔΕΙΓΜΑ", "IFId": "" } ],
       "EventUnitEntry": [ { "Type": "EUE", "Code": "QUAL_TIME", "Value": "NT" }, { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "50.05" } ] },
     { "Order": 1, "CompetitorCode": 990101, "Description": [ { "FamilyName": "ΑΛΕΞΑΝΔΡΑΚΗΣ", "GivenName": "Μάριος", "FamilyName_ENG": "ALEXANDRAKIS", "GivenName_ENG": "Marios", "GenderCode_ENG": "M", "BirthDate": "2005-03-01T00:00:00", "OrganisationCode": "ΟΜΙΛΟΣ ΔΕΙΓΜΑ", "IFId": "" } ],
       "EventUnitEntry": [ { "Type": "EUE", "Code": "QUAL_TIME", "Value": "NT" }, { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "49.79" } ] },
     { "Order": 3, "CompetitorCode": 880003, "Description": [ { "FamilyName": "ΔΡΑΚΟΝΤΑΕΙΔΗΣ", "GivenName": "Λευτέρης", "FamilyName_ENG": "DRAKONTAEIDIS", "GivenName_ENG": "Lefteris", "GenderCode_ENG": "M", "BirthDate": "2004-05-01T00:00:00", "OrganisationCode": "ΟΜΙΛΟΣ ΔΕΙΓΜΑ", "IFId": "" } ],
       "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "49.86" } ] },
     { "Order": 4, "CompetitorCode": "", "Description": [ { "FamilyName": "ΣΤΑΥΡΙΝΟΥΔΗΣ", "GivenName": "Νίκος", "FamilyName_ENG": "STAVRINOUDIS", "GivenName_ENG": "Nikos", "GenderCode_ENG": "M", "BirthDate": "", "OrganisationCode": "ΟΜΙΛΟΣ ΔΕΙΓΜΑ", "IFId": "" } ],
       "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "48.70" } ] } ] },
   "ExtendedResults": { "ExtendedResult": [
     { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "50.00", "Value": "23.71" }, { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "100.00", "Value": "49.79" },
     { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "150.00", "Value": "1:13.10" }, { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "200.00", "Value": "1:39.84" },
     { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "250.00", "Value": "2:03.48" }, { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "300.00", "Value": "2:29.70" },
     { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "350.00", "Value": "2:53.10" } ] } },
 { "StartOrder": 5, "CompetitorCode": 5002, "OrganisationCode": "Κ.Σ. ΠΑΡΑΔΕΙΓΜΑ", "IRM_ENG": "", "Result": "3:20.95", "NationCode": "GRE",
   "Composition": { "Athlete": [
     { "Order": 1, "CompetitorCode": 990201, "Description": [ { "FamilyName": "ΠΡΩΤΟΣ", "GivenName": "Δείγμα", "GenderCode_ENG": "M", "BirthDate": "2005-01-01T00:00:00" } ], "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "NT" } ] },
     { "Order": 2, "CompetitorCode": 990202, "Description": [ { "FamilyName": "ΔΕΥΤΕΡΟΣ", "GivenName": "Δείγμα", "GenderCode_ENG": "M", "BirthDate": "2005-01-01T00:00:00" } ], "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "NT" } ] },
     { "Order": 3, "CompetitorCode": 990203, "Description": [ { "FamilyName": "ΤΡΙΤΟΣ", "GivenName": "Δείγμα", "GenderCode_ENG": "M", "BirthDate": "2005-01-01T00:00:00" } ], "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "NT" } ] },
     { "Order": 4, "CompetitorCode": 990204, "Description": [ { "FamilyName": "ΤΕΤΑΡΤΟΣ", "GivenName": "Δείγμα", "GenderCode_ENG": "M", "BirthDate": "2005-01-01T00:00:00" } ], "EventUnitEntry": [ { "Type": "EUE", "Code": "SPLIT_TIME", "Value": "NT" } ] } ] },
   "ExtendedResults": { "ExtendedResult": [
     { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "100.00", "Value": "50.31" }, { "Code": "INTERMEDIATE", "Type": "PROGRESS", "Pos": "200.00", "Value": "1:41.02" } ] } }
] } } ] }
'@
$unit = [Activator]::CreateInstance($tUnit); $unit.Source = 'koe'; $unit.Distance = 400; $unit.Style = 'fs'; $unit.Relay = $true; $unit.Heat = 1
$read = $tResults.GetMethod('ReadKoeEntries').Invoke($null, @([Newtonsoft.Json.Linq.JObject]::Parse($json), $unit))
Check ($read -and $unit.Entries.Count -eq 2) ('the KOE live results of a relay read: ' + $unit.Entries.Count + ' teams')
$team = $unit.Entries[0]; $other = $unit.Entries[1]
Check ($team.FamilyName -eq 'ΟΜΙΛΟΣ ΔΕΙΓΜΑ' -and (Near $team.Time 198.40) -and $team.Lane -eq 4) ('the team: its name, its time, its lane (' + $team.FamilyName + ', ' + (F2 $team.Time) + ', ' + $team.Lane + ')')
$m = @($team.Members)
Check ($m.Count -eq 4 -and $m[0].Order -eq 1 -and $m[1].Order -eq 2 -and $m[3].Order -eq 4) ('its four swimmers in their order (' + (($m | ForEach-Object { $_.Order }) -join ',') + ')')
Check ($m.Count -eq 4 -and $m[0].Code -eq '990101' -and $m[0].FamilyName -eq 'ΑΛΕΞΑΝΔΡΑΚΗΣ' -and $m[0].GivenName -eq 'Μάριος' -and $m[0].BirthYear -eq 2005 -and $m[0].Gender -eq 'M') ('a swimmer: number, names, year, gender (' + $m[0].Code + ' ' + $m[0].Name + ' ' + $m[0].BirthYear + ')')
Check ($m.Count -eq 4 -and (Near $m[0].LegTime 49.79) -and (Near $m[1].LegTime 50.05) -and (Near $m[2].LegTime 49.86) -and (Near $m[3].LegTime 48.70)) ('the times of the legs from SPLIT_TIME: ' + (($m | ForEach-Object { F2 $_.LegTime }) -join ' '))
$o = @($other.Members)
Check ($o.Count -eq 4 -and (Near $o[0].LegTime 50.31) -and (Near $o[1].LegTime 50.71) -and $null -eq $o[2].LegTime -and $null -eq $o[3].LegTime) ('no SPLIT_TIME: the legs from the team''s times at their ends, when both are known (' + (($o | ForEach-Object { F2 $_.LegTime }) -join ' ') + ')')
Check ($team.Splits.Count -eq 7) ('the team''s official times every 50 m (' + $team.Splits.Count + ')')

# ---- 2. The swimmers in a FICTIONAL KOE App: by the number, by the name and year, two of the same family name to choose from.
$registry = [Activator]::CreateInstance($tList)
foreach ($x in @(@('990101', 'Αλεξανδράκης Μάριος', '2005'), @('990102', 'Κυριαζόπουλος Ηλίας', '2006'), @('990103', 'Δρακονταειδής Λευτέρης', '2004'),
                 @('990104', 'Σταυρινούδης Πάνος', '2007'), @('990177', 'Σταυρινούδης Παναγιώτης', '2009'))) {
  $ath = [Activator]::CreateInstance($tAthlete); $ath.Id = $x[0]; $ath.Name = $x[1]; $ath.BirthYear = $x[2]; $ath.Gender = 'Ανδρών'; $ath.Club = 'Όμιλος Δείγμα'
  $registry.Athletes.Add($ath)
}
$registry.BuildIndex()
$legsOf = $tManager.GetMethod('RelayLegsOf', $static)
$legs = @($legsOf.Invoke($null, [object[]]@($team, $registry, $null)))
$note = $tManager.GetMethod('LegNote', $static)
Check ($legs.Count -eq 4) ('the four legs of the race (' + $legs.Count + ')')
if ($legs.Count -eq 4) {
  Check ($legs[0].AthleteId -eq '990101' -and $legs[0].Match -eq 'code' -and $legs[0].Athlete -eq 'Αλεξανδράκης Μάριος') ('leg 1 by the number: ' + $legs[0].Athlete + ' (' + $legs[0].Match + ', ' + $note.Invoke($null, @($legs[0])) + ')')
  Check ($legs[2].AthleteId -eq '990103' -and $legs[2].Match -eq 'name') ('leg 3 by the name and year (another number in the results): ' + $legs[2].AthleteId + ' (' + $legs[2].Match + ')')
  $c4 = @($legs[3].Candidates)
  Check ($legs[3].AthleteId -eq '' -and $c4.Count -eq 2 -and $legs[3].Athlete -eq 'ΣΤΑΥΡΙΝΟΥΔΗΣ Νίκος' -and $note.Invoke($null, @($legs[3])) -like '⚠ 2*') ('leg 4 not sure: his name as published, two of his family to choose from (' + $c4.Count + ', ' + $note.Invoke($null, @($legs[3])) + ')')
  Check ((Near $legs[1].OfficialTime 50.05) -and $legs[1].OfficialCode -eq '990102') ('the official time of a leg kept with it (' + (F2 $legs[1].OfficialTime) + ')')
  # The analyst's choice for the same swimmer stays when the results are taken again.
  $legs[3].AthleteId = '990104'; $legs[3].Athlete = 'Σταυρινούδης Πάνος'; $legs[3].Match = 'chosen'
  $before = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tLeg)); foreach ($l in $legs) { $before.Add($l) }
  $again = @($legsOf.Invoke($null, [object[]]@($team, $registry, $before)))
  Check ($again[3].AthleteId -eq '990104' -and $again[3].Match -eq 'chosen') ('the analyst''s choice for the same swimmer stays (' + $again[3].AthleteId + ', ' + $again[3].Match + ')')
}

# ---- 3. Pasted results of a relay (FICTIONAL): the swimmers under each team, the times of the legs.
$text = @"
4x100μ. Ελεύθερο Ανδρών · ΤΕΛΙΚΟΣ
Θέση Δ. Ομάδα Αντ. 50μ. 100μ. Χρόνος
1 4 ΟΜΙΛΟΣ ΔΕΙΓΜΑ 0.66 23.71 49.79 3:18.40
ΑΛΕΞΑΝΔΡΑΚΗΣ Μάριος 2005 0.66 23.71 49.79
ΚΥΡΙΑΖΟΠΟΥΛΟΣ Ηλίας 2006 0.21 1:13.10 1:39.84
ΔΡΑΚΟΝΤΑΕΙΔΗΣ Λευτέρης 2004 0.30 2:03.48 2:29.70
ΣΤΑΥΡΙΝΟΥΔΗΣ Πάνος 2007 0.18 2:53.10 3:18.40
2 5 Κ.Σ. ΠΑΡΑΔΕΙΓΜΑ 0.71 24.02 50.31 3:20.95
ΠΑΠΑΔΕΙΓΜΑΤΟΣ Ορέστης 2005 0.71 24.02 50.31
ΔΕΥΤΕΡΟΣ Δείγμα 2006 0.25 1:15.00 1:41.02
ΤΡΙΤΟΣ Δείγμα 2005 0.28 2:05.00 2:31.50
ΤΕΤΑΡΤΟΣ Δείγμα 2004 0.20 2:55.00 3:20.95
"@
$parse = $tPaste.GetMethod('Parse', [Type[]]@([string], [int], [string], [int], [bool]))
$res = $parse.Invoke($null, @($text, 400, '', 4, $true))
$recs = @($res.Records)
Check ($recs.Count -eq 2) ('pasted: two teams (' + $recs.Count + ')')
if ($recs.Count -ge 1) {
  $r0 = $recs[0]; $pm = @($r0.Members)
  Check ((Near $r0.Time 198.40) -and $r0.Lane -eq 4 -and $pm.Count -eq 4) ('the first team: its time, lane and four swimmers (' + (F2 $r0.Time) + ', ' + $r0.Lane + ', ' + $pm.Count + ')')
  if ($pm.Count -eq 4) {
    Check ($pm[0].FamilyName -eq 'ΑΛΕΞΑΝΔΡΑΚΗΣ' -and $pm[0].GivenName -eq 'Μάριος' -and $pm[0].BirthYear -eq 2005) ('a swimmer pasted: ' + $pm[0].FamilyName + ' ' + $pm[0].GivenName + ' ' + $pm[0].BirthYear)
    Check ((Near $pm[0].Time 49.79) -and (Near $pm[1].Time 50.05) -and (Near $pm[2].Time 49.86) -and (Near $pm[3].Time 48.70)) ('the legs from the team''s times at their ends: ' + (($pm | ForEach-Object { F2 $_.Time }) -join ' '))
    Check ((Near $pm[0].Reaction 0.66) -and (Near $pm[1].Reaction 0.21) -and (Near $pm[3].Reaction 0.18)) ('the official exchanges: ' + (($pm | ForEach-Object { F2 $_.Reaction }) -join ' '))
  }
}
$koeText = "1`t4`tSAMPLE CLUB`tSAMPLE CLUB`t`t7:45.12`t`t`t`tremove_circle`nTEAMDETAILSSAMPLETEXT`n`n`nNAME`t`t`tTIME`nALPHA Andreas`t`t`t1:55.12`nBETA Vasilis`t`t`t1:56.40`nGAMMA Nikos`t`t`t1:57.30`nDELTA Petros`t`t`t1:56.30`n2`t5`tOTHER CLUB`tOTHER CLUB`t`t7:50.00`t+4.88`t`t`tadd_circle"
$res2 = $parse.Invoke($null, @($koeText, 800, '', 4, $true))
$k = @($res2.Records)
$km = if ($k.Count -gt 0) { @($k[0].Members) } else { @() }
Check ($k.Count -eq 2 -and (Near $k[0].Time 465.12) -and $km.Count -eq 4) ('pasted as the results show a team opened: ' + $k.Count + ' teams, ' + $km.Count + ' swimmers')
Check ($km.Count -eq 4 -and (Near $km[0].Time 115.12) -and (Near $km[2].Time 117.30) -and $km[1].FamilyName -eq 'BETA') ('the times of the legs as published: ' + (($km | ForEach-Object { F2 $_.Time }) -join ' '))
$res3 = $parse.Invoke($null, @($text, 400, '', 4, $false))
Check (@($res3.Records | Where-Object { $_.Members.Count -gt 0 }).Count -eq 0) 'not a relay: read as before (no swimmers)'

# ---- 4. The exchanges of a FICTIONAL 4x100 freestyle (50 m pool, 50 fps): the official touches, F at two of them.
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
function Mark($race, $type, $seconds, $dist) {
  $mk = [Activator]::CreateInstance($tMark); $mk.Type = [Enum]::Parse($tType, $type); $mk.Timestamp = [long](1000 + [Math]::Round($seconds * 1000))
  if ($null -ne $dist) { $mk.Distance = [int]$dist }
  $race.Marks.Add($mk)
}
$official = @(23.71, 49.79, 73.10, 99.84, 123.48, 149.70, 173.10, 198.40)
$race = [Activator]::CreateInstance($tRace)
$race.Style = 'fs'; $race.Distance = 400; $race.Pool = 50; $race.Relay = $true; $race.StartTimestamp = [long]1000
$race.Checkpoints = $tPresets.GetMethod('DefaultCheckpoints').Invoke($null, @(400, 50, $true))
$race.Official = New-Object 'System.Collections.Generic.List[double]'; foreach ($v in $official) { $race.Official.Add([double]$v) }
$walls = @{ 0 = 0.0 }; for ($i = 0; $i -lt 8; $i++) { $walls[50 * ($i + 1)] = $official[$i] }
foreach ($d in $race.Checkpoints) {
  $w0 = [Math]::Floor(($d - 1) / 50) * 50; $w1 = $w0 + 50
  $t0 = [double]$walls[[int]$w0]; $t1 = [double]$walls[[int]$w1]
  Mark $race 'Split' ($t0 + ($t1 - $t0) * ($d - $w0) / 50.0) $d
}
Mark $race 'FeetOff' 0.70 $null
Mark $race 'FeetOff' 50.02 $null
Mark $race 'FeetOff' 100.00 $null
foreach ($n in 1..4) {
  $l = $race.RelayLegFor($n); $l.Athlete = @('Αλεξανδράκης Μάριος', 'Κυριαζόπουλος Ηλίας', 'Δρακονταειδής Λευτέρης', 'Σταυρινούδης Πάνος')[$n - 1]
  $l.OfficialExchange = @(0.66, 0.21, 0.30, 0.18)[$n - 1]
}
$computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
$exList = $tRelay.GetMethod('Exchanges').Invoke($null, @($race, $computed)); $ex = @($exList)
Check ($ex.Count -eq 3) ('three exchanges (' + $ex.Count + ')')
if ($ex.Count -eq 3) {
  Check ($ex[0].Wall -eq 100 -and (Near $ex[0].Touch 49.79) -and (Near $ex[0].FeetOff 50.02) -and (Near $ex[0].Time 0.23)) ('exchange 1 → 2: the official touch 49.79, the feet off 50.02: ' + (F2 $ex[0].Time) + ' s')
  Check ((Near $ex[1].Time 0.16) -and $ex[1].From -eq 'Κυριαζόπουλος' -and $ex[1].To -eq 'Δρακονταειδής' -and (Near $ex[1].Official 0.30)) ('exchange 2 → 3: ' + (F2 $ex[1].Time) + ' s, ' + $ex[1].From + ' → ' + $ex[1].To + ', official ' + (F2 $ex[1].Official))
  Check ((Near $ex[2].Touch 149.70) -and $null -eq $ex[2].FeetOff -and $null -eq $ex[2].Time) ('exchange 3 → 4 without F: its touch only (' + (F2 $ex[2].Touch) + ')')
}
$shownAt = $tRelay.GetMethod('ShownAt')
function Shown($t) {
  $args3 = [object[]]@($exList, [double]$t, [double]0, $false)
  $x = $shownAt.Invoke($null, $args3)
  return @{ X = $x; Shown = $args3[2]; Running = $args3[3] }
}
$s0 = Shown 49.79; $s1 = Shown 49.90; $s2 = Shown 50.30; $s3 = Shown 52.10; $s4 = Shown 150.40; $s5 = Shown 151.30; $s6 = Shown 30.0
Check ($null -ne $s0.X -and $s0.Running -and (Near $s0.Shown 0.0)) ('at the touch: the time starts from 0.00 (' + (F2 $s0.Shown) + ', running ' + $s0.Running + ')')
Check ($s1.Running -and (Near $s1.Shown 0.11)) ('frame by frame: it runs (' + (F2 $s1.Shown) + ')')
Check ($null -ne $s2.X -and -not $s2.Running -and (Near $s2.Shown 0.23)) ('after F: its time stays (' + (F2 $s2.Shown) + ')')
Check ($null -eq $s3.X) 'two seconds after F: gone'
Check ($null -ne $s4.X -and $s4.X.Number -eq 3 -and $s4.Running -and (Near $s4.Shown 0.70)) ('without F: it runs from the touch (' + (F2 $s4.Shown) + ')')
Check ($null -eq $s5.X) ('without F: not longer than ' + $tRelay.GetField('RunningSeconds').GetValue($null) + ' s')
Check ($null -eq $s6.X) 'in the middle of a leg: none'

# The zones and what is written.
$zone = $tRelay.GetMethod('Zone'); $verdict = $tRelay.GetMethod('Verdict'); $figure = $tRelay.GetMethod('Figure')
$zones = @(@(-0.05, 0), @(-0.03, 1), @(0.05, 1), @(0.10, 2), @(0.20, 2), @(0.25, 3), @(0.35, 3), @(0.40, 4))
$badZones = @($zones | Where-Object { $zone.Invoke($null, @([double]$_[0])) -ne $_[1] })
Check ($badZones.Count -eq 0) ('the zones: −0.03 the limit, the target 0.10–0.20, slow to 0.35 (' + $badZones.Count + ' wrong)')
$v1 = $verdict.Invoke($null, @([double]0.22)); $v2 = $verdict.Invoke($null, @([double]0.15)); $v3 = $verdict.Invoke($null, @([double]0.04)); $v4 = $verdict.Invoke($null, @([double]-0.05))
Check ($v1 -eq '+0.02 πάνω από τη ζώνη 0.10–0.20' -and $v2 -eq 'στη ζώνη 0.10–0.20' -and $v3 -eq '−0.06 κάτω από τη ζώνη 0.10–0.20 · ρίσκο' -and $v4 -like 'άκυρη*') ('what is written: "' + $v1 + '", "' + $v2 + '", "' + $v3 + '", "' + $v4 + '"')
Check ($figure.Invoke($null, @([double]-0.02)) -eq '−0.02' -and $figure.Invoke($null, @([double]0.0)) -eq '0.00') 'the figures with the minus sign'

# The bar: «Αποκόλληση βατήρα», its time, and the time running from the touch until F.
$events = @((T 'SwimBarEvents').GetMethod('Build').Invoke($null, @($race, $computed)))
$p100 = $events | Where-Object { $_.Id -eq 'pushoff100' } | Select-Object -First 1
$p300 = $events | Where-Object { $_.Id -eq 'pushoff300' } | Select-Object -First 1
Check ($null -ne $p100 -and $p100.Label -eq 'Αποκόλληση βατήρα' -and $p100.Value -eq '0.23 s' -and $null -eq $p100.RunningFrom) ('the bar: «' + $p100.Label + '» ' + $p100.Value)
Check ($null -ne $p300 -and $null -eq $p300.Time -and (Near $p300.RunningFrom 149.70)) ('the bar: F not marked, the time runs from the touch (' + (F2 $p300.RunningFrom) + ')')

# The style of each leg of a medley relay; a relay of four only.
$med = [Activator]::CreateInstance($tRace); $med.Style = 'im'; $med.Distance = 400; $med.Pool = 50; $med.Relay = $true
$styles = (1..4 | ForEach-Object { $med.LegStyle($_) }) -join ','
Check ($styles -eq 'bk,br,fly,fs' -and $med.LegDistance -eq 100) ('the legs of a medley relay: ' + $styles)
$ind = [Activator]::CreateInstance($tRace); $ind.Style = 'fs'; $ind.Distance = 400; $ind.Pool = 50
Check (-not $ind.IsRelayOfFour -and $ind.LegDistance -eq 0 -and @($tRelay.GetMethod('Exchanges').Invoke($null, @($ind, $computed))).Count -eq 0) 'a race that is not a relay: no legs, no exchanges'

# The snapshots of the exchanges: kept for the PDF, not uploaded.
$snap = [Activator]::CreateInstance((T 'SwimSnapshot')); $snap.Event = $tRelay.GetMethod('TouchLabel').Invoke($null, @(2))
$plain = [Activator]::CreateInstance((T 'SwimSnapshot')); $plain.Event = 'Επαφή στον τοίχο'
$isEx = $tRelay.GetMethod('IsExchangeSnapshot')
Check ($snap.Event -eq 'Επαφή · αλλαγή 2 → 3' -and $isEx.Invoke($null, @($snap)) -and -not $isEx.Invoke($null, @($plain))) ('the snapshot of an exchange told apart: "' + $snap.Event + '"')

# ---- 5. The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $static).GetValue($null)
$keys = @('ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ', 'Αποκόλληση βατήρα', '● μετράει από την επαφή…', 'στη ζώνη ', ' πάνω από τη ζώνη ', ' κάτω από τη ζώνη ', ' · ρίσκο', ' · αργή',
  'άκυρη: πριν από το −0.03', 'Κολυμβητές σκυταλοδρομίας', '✔ όνομα + έτος', '⚠ {0} στο KOE App: διάλεξε', '🔍 Αναζήτηση στο KOE App (όνομα ή ΑΜ)…',
  'Κολυμβητές (η ανάλυση του σκέλους του στην καρτέλα του καθενός):', 'με τους αριθμούς μητρώου των κολυμβητών', 'Σκέλος {0}')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts (' + $keys.Count + ', missing ' + $missing.Count + ': ' + ($missing -join ' | ') + ')')

# The pictures of the snapshots in the pages printed by Edge: {{SNAP:id}} → the JPEG of the snapshot (at most 1100 wide), one
# transparent pixel when there is none; each id read once.
try {
  $fill = $tManager.GetMethod('FillSnapshots', $static); $uriOf = $tManager.GetMethod('SnapshotDataUri', $static)
  $png = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'rl_ci_snapshot.png')
  $sb = New-Object System.Drawing.Bitmap 1600, 900; $sg = [System.Drawing.Graphics]::FromImage($sb); $sg.Clear([System.Drawing.Color]::FromArgb(20, 90, 140)); $sg.Dispose()
  $sb.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $sb.Dispose()
  $pic = [string]$uriOf.Invoke($null, [object[]]@($png))
  $script:asked = @()
  $pictureOf = [Func[string, string]]{ param($id) $script:asked += $id; if ($id -eq 'aa1') { return $pic }; return $null }
  $filled = [string]$fill.Invoke($null, [object[]]@('<img src="{{SNAP:aa1}}"><img src="{{SNAP:bb2}}"><img src="{{SNAP:aa1}}">', $pictureOf))
  $jpeg = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream (, [Convert]::FromBase64String($pic.Substring($pic.IndexOf(',') + 1)))))
  $twice = ([regex]::Matches($filled, [regex]::Escape('src="' + $pic + '"'))).Count
  $pixel = ([regex]::Matches($filled, [regex]::Escape('src="data:image/gif;base64,'))).Count
  $left = $filled.Contains('{{SNAP:')
  $heads = @([regex]::Matches($filled, 'src="([^"]{0,22})') | ForEach-Object { $_.Groups[1].Value }) -join ' | '
  Check ($pic.StartsWith('data:image/jpeg;base64,') -and $jpeg.Width -eq 1100 -and $jpeg.Height -eq 619) ('the picture of a snapshot for the pages: a JPEG ' + $jpeg.Width + '×' + $jpeg.Height + ' (from 1600×900)')
  Check ((-not $left) -and $twice -eq 2 -and $pixel -eq 1 -and $script:asked.Count -eq 2) ('the pages filled: the snapshot twice (' + $twice + '), one pixel for the one missing (' + $pixel + '), none left (' + (-not $left) + '), each asked once (' + ($script:asked -join ',') + '): ' + $heads + ' (' + $filled.Length + ' characters, the picture ' + $pic.Length + ')')
  $jpeg.Dispose(); Remove-Item $png -ErrorAction SilentlyContinue
} catch {
  $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
  Check $false ('the pictures of the snapshots in the pages: ' + $x.GetType().Name + ': ' + $x.Message)
}

# ---- 6. Pictures of the exchange on the HUD (1920×1080, no video under it).
if ($out) {
  try {
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $reel = $tReel.GetMethod('Build').Invoke($null, @($race, $computed))
    $tScene = T 'SwimHudScene'; $tHud = T 'SwimHudPainter'
    $shots = @(@(49.79, 'exchange_1_touch.png'), @(49.90, 'exchange_1_running.png'), @(50.30, 'exchange_1_done.png'), @(100.40, 'exchange_2_target.png'), @(150.30, 'exchange_3_running_noF.png'))
    foreach ($s in $shots) {
      $bmp = New-Object System.Drawing.Bitmap 1920, 1080
      $g = [System.Drawing.Graphics]::FromImage($bmp)
      try {
        $g.Clear([System.Drawing.Color]::FromArgb(18, 80, 120))
        $scene = [Activator]::CreateInstance($tScene)
        $ts = [long](1000 + [Math]::Round([double]$s[0] * 50) * 20)
        $scene.Race = $race; $scene.Computed = $computed; $scene.Reel = $reel; $scene.Timing = $timing
        $scene.Timestamp = $ts; $scene.Time = $timing.Seconds($ts, [long]1000); $scene.FrameNumber = [long](($ts - 1000) / 20)
        $scene.VideoSeconds = $scene.FrameNumber / 50.0; $scene.Fps = 50; $scene.VideoSize = New-Object System.Drawing.Size 1920, 1080
        $scene.Calibrated = $true; $scene.Summary = $true; $scene.SpeedTrace = $true; $scene.DeltaInfo = $false
        $tHud.GetMethod('Paint').Invoke($null, [object[]]@($g, [System.Drawing.Size]::new(1920, 1080), $scene))
      } finally { $g.Dispose() }
      $bmp.Save([System.IO.Path]::Combine($out, $s[1]), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
      Write-Output ('picture: ' + $s[1])
    }
  } catch {
    $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
    Check $false ('the pictures of the HUD: ' + $x.GetType().Name + ': ' + $x.Message)
  }
}

Write-Output ('rl_ci: ' + $fails + ' failed')
exit $fails
