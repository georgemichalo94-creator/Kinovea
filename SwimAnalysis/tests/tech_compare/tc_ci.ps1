# tc_ci.ps1: the CI check of the comparison of a technique analysis (2.22.0) on FICTIONAL finals: the elite of a final as the mean of its
# swimmers and as its winner (the turns: -5, wall -> +15, -5 -> +15, the breakout; the start: release, 15 m, breakout), the labels
# without the names of the finalists, the finals of the style and gender of the analysis, what the Sheet gets (race.compare.techPrev and
# techElite, only for a technique analysis), the choice kept in the file, and "auto" / "best" as the Apps Script picks them.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File tc_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
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
$json = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll'))
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$tRace = T 'SwimRace'; $tFinals = T 'SwimFinals'; $tElite = T 'SwimTechElite'; $tEF = T 'SwimTechEliteFinals'; $tItem = T 'SwimTechAnalysisItem'
$tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'; $tClient = T 'SwimPayload'; $tSettings = T 'SwimSettings'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function Near($x, $y) { return $null -ne $x -and [Math]::Abs([double]$x - [double]$y) -lt 1e-9 }

# Fictional finals (no real swimmer): 100 m freestyle women (3), men (2), backstroke women (2).
$text = @'
{ "format": "kinovea-swim-finals", "source": "Fictional Games 2030", "short": "FG2030", "pool": 50, "finals": [
  { "gender": "F", "style": "fs", "distance": 100, "swimmers": [
    { "rank": 1, "name": "Anna ALPHATEST", "final": 52.10, "blockTime": 0.66, "start15": 6.40, "startBreakoutTime": 4.0, "startBreakoutDist": 11.0,
      "turns": [ { "wall": 50, "in5": 2.50, "out15": 6.80, "breakoutTime": 3.0, "breakoutDist": 8.5 } ] },
    { "rank": 2, "name": "Bea BETATEST", "final": 52.50, "blockTime": 0.70, "start15": 6.50, "startBreakoutTime": 4.2, "startBreakoutDist": 10.0,
      "turns": [ { "wall": 50, "in5": 2.60, "out15": 7.00, "breakoutTime": 3.2, "breakoutDist": 8.0 } ] },
    { "rank": 3, "name": "Cleo GAMMATEST", "final": 52.90, "blockTime": 0.68, "start15": 6.60,
      "turns": [ { "wall": 50, "in5": 2.70, "out15": 7.20, "breakoutTime": 3.1, "breakoutDist": 7.5 } ] } ] },
  { "gender": "M", "style": "fs", "distance": 100, "swimmers": [
    { "rank": 1, "name": "Dan DELTATEST", "final": 47.10, "blockTime": 0.62, "start15": 5.60, "turns": [ { "wall": 50, "in5": 2.20, "out15": 6.00 } ] },
    { "rank": 2, "name": "Eli EPSILONTEST", "final": 47.40, "blockTime": 0.64, "start15": 5.70, "turns": [ { "wall": 50, "in5": 2.30, "out15": 6.10 } ] } ] },
  { "gender": "F", "style": "bk", "distance": 100, "swimmers": [
    { "rank": 1, "name": "Fay ZETATEST", "final": 58.10, "start15": 7.10 }, { "rank": 2, "name": "Gia ETATEST", "final": 58.40, "start15": 7.20 } ] } ] }
'@
$err = $null
$finals = $tFinals.GetMethod('Parse').Invoke($null, @($text, $err))
Check ($null -ne $finals -and $finals.Finals.Count -eq 3) 'the fictional finals'

$race = [Activator]::CreateInstance($tRace)
$race.Kind = 'training'; $race.Style = 'fs'; $race.Pool = 25; $race.AthleteGender = 'Γυναικών'; $race.Date = '12.11.2026'
$for = $tEF.GetMethod('For')
$list = @($for.Invoke($null, @($finals, $race)))
Check ($list.Count -eq 2) ('the women''s freestyle final only, as its mean and its winner: ' + $list.Count)
$mean = $list[0]; $first = $list[1]
Check ($mean.Type -eq 'final' -and $mean.Id.EndsWith('|mean') -and $first.Id.EndsWith('|first')) ('ids: ' + $mean.Id + ' / ' + $first.Id)
Check ($mean.Label -match 'FG2030' -and $mean.Label -match '3' -and $first.Label -match 'FG2030') ('labels: ' + $mean.Label + ' / ' + $first.Label)
$names = 'ALPHATEST|BETATEST|GAMMATEST|Anna|Bea|Cleo'
Check (-not ($mean.Label -match $names) -and -not ($first.Label -match $names)) 'no names of the finalists in the labels'
# The mean over the swimmers and their walls; -5 -> +15 the mean of each turn's sum.
Check ((Near $mean.Turn['in5'] 2.6) -and (Near $mean.Turn['out15'] 7.0) -and (Near $mean.Turn['inOut15'] 9.6)) ('turn means: ' + $mean.Turn['in5'] + ' ' + $mean.Turn['out15'] + ' ' + $mean.Turn['inOut15'])
Check ((Near $mean.Turn['breakoutTime'] 3.1) -and (Near $mean.Turn['breakoutDist'] 8.0)) 'turn breakout means'
Check ((Near $mean.Start['blockTime'] 0.68) -and (Near $mean.Start['t15'] 6.5) -and (Near $mean.Start['breakoutTime'] 4.1) -and (Near $mean.Start['breakoutDist'] 10.5)) 'start means (the breakout of the two that have it)'
Check ((Near $first.Turn['in5'] 2.5) -and (Near $first.Turn['inOut15'] 9.3) -and (Near $first.Start['t15'] 6.4) -and (Near $first.Start['breakoutDist'] 11.0)) 'the winner alone'
Check ($mean.Distance -eq 100 -and $mean.Pool -eq 50 -and $mean.Gender -eq 'F' -and $mean.Style -eq 'fs') 'the event of the final (for its levels in the report)'
# Gender not known: the freestyle finals of both.
$race.AthleteGender = ''
Check (@($for.Invoke($null, @($finals, $race))).Count -eq 4) 'no gender: both freestyle finals'
$race.AthleteGender = 'Γυναικών'

# What the Sheet gets: only for a technique analysis.
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
$settings = [Activator]::CreateInstance($tSettings)
function Payload($r) {
  $computed = $tCalc.GetMethod('Compute').Invoke($null, @($r, $timing, 0.5))
  $phases = $tTech.GetMethod('Compute').Invoke($null, @($r, $computed, $timing))
  $body = $tClient.GetMethod('Build').Invoke($null, @($r, $computed, $phases, $timing, [long]0, $settings, 'fictional.mp4', 'update', $null, $false, $null))
  return ($body | ConvertFrom-Json)
}
$race.TechPrev = 'auto'; $race.TechElite = $mean
$b = Payload $race
Check ($b.race.compare.techPrev -eq 'auto') 'techPrev sent'
$e = $b.race.compare.techElite
Check ($e.type -eq 'final' -and $e.distance -eq 100 -and $e.pool -eq 50 -and $e.gender -eq 'F' -and $e.label -eq $mean.Label) 'techElite of the final sent with its event'
Check ((Near $e.values.turn.in5 2.6) -and (Near $e.values.turn.inOut15 9.6) -and (Near $e.values.start.t15 6.5)) 'its values sent'
$ref = [Activator]::CreateInstance($tElite); $ref.Type = 'ref'; $ref.Id = 'tc-elite-1'; $ref.Label = 'Fictional elite turn'
$race.TechElite = $ref; $race.TechPrev = 'tc-prev-1'
$b = Payload $race
Check ($b.race.compare.techPrev -eq 'tc-prev-1' -and $b.race.compare.techElite.type -eq 'ref' -and $b.race.compare.techElite.id -eq 'tc-elite-1' -and $null -eq $b.race.compare.techElite.values) 'an elite technique analysis: its id only'
$race.TechPrev = ''; $race.TechElite = $null
$b = Payload $race
Check ($null -eq $b.race.compare.techPrev -and $null -eq $b.race.compare.techElite) 'none: nothing sent'
$race2 = [Activator]::CreateInstance($tRace); $race2.Style = 'fs'; $race2.Distance = 100; $race2.Pool = 50; $race2.TechPrev = 'auto'; $race2.TechElite = $mean
$b = Payload $race2
Check ($null -eq $b.race.compare.techPrev -and $null -eq $b.race.compare.techElite -and $b.race.compare.pb -eq 'auto') 'a race: its own comparison only'

# The choice kept in the file of the analyses (Newtonsoft as the program).
$jc = $json.GetType('Newtonsoft.Json.JsonConvert', $true)
$race.TechPrev = 'best'; $race.TechElite = $first
$text2 = $jc.GetMethod('SerializeObject', [Type[]]@([object])).Invoke($null, @($race))
$back = $jc.GetMethod('DeserializeObject', [Type[]]@([string], [Type])).Invoke($null, @($text2, $tRace))
$back.EnsureLists()
Check ($back.TechPrev -eq 'best' -and $back.TechElite.Id -eq $first.Id -and (Near $back.TechElite.Turn['in5'] 2.5)) 'the choice in the file'
$old = $jc.GetMethod('DeserializeObject', [Type[]]@([string], [Type])).Invoke($null, @('{"Kind":"training","Style":"fs"}', $tRace))
$old.EnsureLists()
Check ($old.TechPrev -eq '' -and $null -eq $old.TechElite) 'an older file: no comparison'

# "auto" and "best" as the Apps Script picks them: the newest not after this one, the fastest; turns, else starts.
$items = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tItem))
function Item($id, $key, $turns, $starts, $t15, $s15) {
  $i = [Activator]::CreateInstance($tItem); $i.Id = $id; $i.Key = $key; $i.Turns = $turns; $i.Starts = $starts
  if ($null -ne $t15) { $i.Turn15 = [double]$t15 }; if ($null -ne $s15) { $i.Start15 = [double]$s15 }
  $items.Add($i)
}
Item 'after' 20261201 2 0 11.0 $null; Item 'oct' 20261010 2 0 11.9 $null; Item 'sep' 20260912 3 0 11.5 $null; Item 'starts' 20261101 0 2 $null 8.1
$pick = $tEF.GetMethod('Pick')
Check ($pick.Invoke($null, @($items, $race, $true, $false)).Id -eq 'oct') 'auto: the newest turns before this date'
Check ($pick.Invoke($null, @($items, $race, $true, $true)).Id -eq 'sep') 'best: the fastest -5 -> +15 before this date'
Check ($pick.Invoke($null, @($items, $race, $false, $false)).Id -eq 'starts') 'auto of the starts'
$key = $tEF.GetMethod('DateKey')
Check ($key.Invoke($null, @('12.11.2026')) -eq 20261112 -and $key.Invoke($null, @('2026-11-12')) -eq 20261112 -and $key.Invoke($null, @('12/11/2026 10:15')) -eq 20261112 -and $key.Invoke($null, @('')) -eq 0) 'dates'

if ($fails -gt 0) { Write-Output ('FAILED: ' + $fails); exit 1 }
Write-Output 'tech compare ok'
