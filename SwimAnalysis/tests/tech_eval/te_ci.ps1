# te_ci.ps1: the CI check of the technique evaluation (catalogue, levels, measurements from the marks and from a synthetic skeleton,
# proposals, default sheets, automatic error lists, learning, side views) on a fictional training turn and start, with the FICTIONAL
# catalogue mini_catalog.json (invented sheets and level tables; the coach's real catalogue stays on the analysts' computers).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File te_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -catalog mini_catalog.json
param([string]$sm, [string]$bin, [string]$catalog)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path; $catalog = (Resolve-Path $catalog).Path
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = (New-Object System.Reflection.AssemblyName($e.Name)).Name
  $p = Join-Path $bin ($n + '.dll')
  if (Test-Path $p) { return [System.Reflection.Assembly]::LoadFrom($p) }
  return $null })
$a = [System.Reflection.Assembly]::LoadFrom($sm)
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$tCat = T 'SwimTechSheetCatalog'; $tEv = T 'SwimTechEvaluator'; $tRace = T 'SwimRace'; $tMark = T 'SwimMark'; $tType = T 'SwimMarkType'
$tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'; $tSkel = T 'SwimSkeleton'; $tKind = T 'SwimPhaseKind'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

$cat = $tCat.GetMethod('Parse').Invoke($null, @([System.IO.File]::ReadAllText($catalog)))
Write-Output ('catalogue: ' + $cat.Sheets.Count + ' sheets, ' + $cat.Criteria.Count + ' criteria, ' + $cat.Checks.Count + ' checks, ' + $cat.Levels.Count + ' level tables')
Check ($cat.Sheets.Count -eq 12) 'all sheets read'
$levelOf = $tEv.GetMethod('LevelOf')
Check (($levelOf.Invoke($null, @($cat, 'turn_5_10', 'br', 'Ανδρών', 9.5))) -eq 5) 'turn 5+10 breaststroke men 9.5 s = level 5'
Check (($levelOf.Invoke($null, @($cat, 'turn_5_10', 'fly', 'Γυναικών', 8.1))) -eq 3) 'fly women 8.1 s = level 3'
Check (($levelOf.Invoke($null, @($cat, 'start_15', 'fs', 'M', 5.6))) -eq 4) 'start 15 m free men 5.6 = level 4'
Check (($levelOf.Invoke($null, @($cat, 'start_15', 'bk', 'F', 9.9))) -eq 9) 'slower than level 8 = 9'
Check ($null -eq ($levelOf.Invoke($null, @($cat, 'turn_5_10', 'fs', 'M', 7.0)))) 'no table for a freestyle turn'
Check ($null -eq ($levelOf.Invoke($null, @($cat, 'start_15', 'fs', '', 5.6)))) 'no level without the gender'
$gc = $tEv.GetMethod('GenderCode')
Check (($gc.Invoke($null, @('Κορασίδες'))) -eq $null -or $true) 'gender code runs'
Check (($gc.Invoke($null, @('Γυναικών'))) -eq 'F' -and ($gc.Invoke($null, @('Ανδρών'))) -eq 'M' -and ($gc.Invoke($null, @('Κορίτσια'))) -eq 'F' -and ($gc.Invoke($null, @('Αγόρια'))) -eq 'M') 'gender codes'

$turnKind = [Enum]::Parse($tKind, 'Turn'); $startKind = [Enum]::Parse($tKind, 'Start')
$def = $cat.GetType().GetMethod('DefaultFor')
Check (($def.Invoke($cat, @($turnKind, 'fs', $true, $null))).Id -eq 'koe-demo-turn-fs') 'default for a freestyle turn: the koe- sheet'
Check (($def.Invoke($cat, @($turnKind, 'br', $false, $null))).Id -eq 'koe-demo-turn-br') 'default for a breaststroke turn: the koe- sheet'
Check (($def.Invoke($cat, @($startKind, 'bk', $false, $null))).Id -eq 'demo-start-bk') 'default for a backstroke start'
Check (($def.Invoke($cat, @($turnKind, 'fs', $true, 'demo-uw'))).Id -eq 'demo-uw') 'the last choice when it fits'
Check (($def.Invoke($cat, @($turnKind, 'fs', $true, 'koe-demo-turn-br'))).Id -eq 'koe-demo-turn-fs') 'a last choice that does not fit is not kept'
$for = $cat.GetType().GetMethod('SheetsFor')
$fsTurn = @($for.Invoke($cat, @($turnKind, 'fs'))) | ForEach-Object { $_.Id }
Check (($fsTurn -contains 'koe-demo-turn-fs') -and ($fsTurn -contains 'demo-uw') -and ($fsTurn -contains 'demo-rules') -and -not ($fsTurn -contains 'koe-demo-turn-br') -and -not ($fsTurn -contains 'demo-uw-br')) ('sheets of a freestyle turn: ' + ($fsTurn -join ','))
$brStart = @($for.Invoke($cat, @($startKind, 'br'))) | ForEach-Object { $_.Id }
Check (($brStart -contains 'demo-start-fwd') -and ($brStart -contains 'demo-uw-br') -and -not ($brStart -contains 'demo-uw')) ('sheets of a breaststroke start: ' + ($brStart -join ','))

# A training video at 50 fps (a timestamp = 1 ms): a flip turn with its approach strokes, rotation, wall, push-off, kicks, splits.
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
$race = [Activator]::CreateInstance($tRace)
$race.Kind = 'training'; $race.Style = 'fs'; $race.Pool = 50; $race.AthleteGender = 'Ανδρών'
function Mark($type, $ms, $dist) {
  $m = [Activator]::CreateInstance($tMark); $m.Type = [Enum]::Parse($tType, $type); $m.Timestamp = [long]$ms; $m.Wall = -1
  if ($null -ne $dist) { $m.Distance = [int]$dist }
  $race.Marks.Add($m)
}
foreach ($t in @(-4.0, -3.4, -2.8, -2.2, -1.6, -1.0)) { Mark 'Stroke' ([Math]::Round((10 + $t) * 1000)) $null }
Mark 'TechSplit' 7400 -5
Mark 'RotationStart' 9400 $null
Mark 'WallContact' 10000 $null
Mark 'FeetOff' 10300 $null
foreach ($k in @(11000, 11460, 11900, 12360)) { Mark 'Kick' $k $null }
Mark 'Breakout' 13500 $null
Mark 'TechSplit' 15000 10

# A synthetic skeleton around the wall: knees at 90° at the plant, a streamline at the push-off, the hips moving forward and level.
$skel = [Activator]::CreateInstance($tSkel); $skel.Start = 9800; $skel.Step = 20
$data = $skel.Data
for ($i = 0; $i -le 60; $i++) {
  $ts = 9800 + 20 * $i
  # x grows away from the wall after the push-off (the swimmer goes right), before it towards the wall (left: the wall at x = 50).
  if ($ts -le 10300) { $ox = 100 } else { $ox = 100 + ($ts - 10300) * 0.6 }
  $pts = @{}
  $pts[0] = @(($ox + 70), 100)            # nose: ahead, between the arms
  $pts[1] = @(($ox + 50), 100); $pts[2] = @(($ox + 50), 100)   # shoulders
  $pts[3] = @(($ox + 85), 100); $pts[4] = @(($ox + 85), 101)   # elbows (straight)
  $pts[5] = @(($ox + 120), 100); $pts[6] = @(($ox + 120), 101) # wrists ahead
  $pts[7] = @($ox, 100); $pts[8] = @($ox, 101)                 # hips
  if ($ts -le 10100) {
    $pts[9] = @(($ox - 25), 75); $pts[10] = @(($ox - 25), 76)   # knees up towards the wall
    $pts[11] = @(($ox - 50), 100); $pts[12] = @(($ox - 50), 101) # feet on the wall at the depth of the head: knee 90°
  } else {
    $pts[9] = @(($ox - 50), 100); $pts[10] = @(($ox - 50), 101)
    $pts[11] = @(($ox - 100), 100); $pts[12] = @(($ox - 100), 101) # legs straight
  }
  $pts[13] = $pts[11]; $pts[14] = $pts[12]; $pts[15] = $pts[11]; $pts[16] = $pts[12]
  for ($j = 0; $j -lt 17; $j++) { $data.Add([int][Math]::Round($pts[$j][0])); $data.Add([int][Math]::Round($pts[$j][1])); $data.Add(95) }
}
$race.Skeletons.Add($skel)

$computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
$phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
Check ($phases.Count -eq 1) ('one phase: ' + $phases.Count)
$p = $phases[0]
Write-Output ('phase ' + $p.Id + ' flip ' + $p.Flip + ' last stroke ' + $p.LastStroke + ' pushoff ' + $p.Pushoff + ' kicks ' + $p.Kicks.Count + ' inOut10 ' + $p.Value('inOut10'))
$measure = $tEv.GetMethod('Measure')
$m = $measure.Invoke($null, @($race, $p, $timing, $cat))
foreach ($k in ($m.Keys | Sort-Object)) { $x = $m[$k]; Write-Output ('  ' + $k + ' = ' + [Math]::Round($x.Value, 3) + ' | ' + $x.Text + ' | good ' + $x.Good + ' | conf ' + [Math]::Round($x.Confidence, 2)) }
Check ($m.ContainsKey('push_time') -and [Math]::Abs($m['push_time'].Value - 0.3) -lt 1e-6 -and $null -eq $m['push_time'].Good) 'push 0.30 s, at the limit: no proposal'
Check ($m.ContainsKey('rotation_flip') -and [Math]::Abs($m['rotation_flip'].Value - 166.67) -lt 0.1 -and $m['rotation_flip'].Good -eq $false) 'rotation 1.0 s against a half cycle of 0.6 s: 167 % (slow)'
Check ($m.ContainsKey('turn_5_10') -and [Math]::Abs($m['turn_5_10'].Value - 7.6) -lt 1e-6) '5 m in + 10 m out = 7.6 s'
Check ($m.ContainsKey('kick_rate') -and $m['kick_rate'].Good -eq $true) 'kick rate above 2 /s'
Check ($m.ContainsKey('rate_speed_5m') -and [Math]::Abs($m['rate_speed_5m'].Value - 100) -lt 0.1 -and $m['rate_speed_5m'].Good -eq $true) 'same stroke rate in the last 5 m'
Check ($m.ContainsKey('knee_plant') -and [Math]::Abs($m['knee_plant'].Value - 90) -lt 2 -and $m['knee_plant'].Good -eq $true) 'knees 90 at the plant'
Check ($m.ContainsKey('head_feet_depth') -and $m['head_feet_depth'].Value -lt 3 -and $m['head_feet_depth'].Good -eq $true) 'head and feet at the same depth'
Check (-not $m.ContainsKey('entry_streamline')) 'no entry streamline in a turn'
Check ($m.ContainsKey('streamline') -and $m['streamline'].Value -eq 1) 'streamline at the push-off'
Check ($m.ContainsKey('body_line') -and $m['body_line'].Value -lt 3) 'straight line at the push-off'
Check ($m.ContainsKey('push_dir') -and [Math]::Abs($m['push_dir'].Value) -lt 2 -and $m['push_dir'].Good -eq $true) 'horizontal push'
Check (-not $m.ContainsKey('rule_15m')) 'no 15 m rule without a breakout distance'
Check ($m.ContainsKey('streamline_plant') -and $m['streamline_plant'].Value -eq 1 -and $m['streamline_plant'].Good -eq $true) 'streamline at the plant'
Check ($m.ContainsKey('body_line_plant') -and $m['body_line_plant'].Value -lt 3) 'arm and trunk in line at the plant'
Check ($m.ContainsKey('body_line_glide') -and $m['body_line_glide'].Value -lt 3 -and $m['body_line_glide'].Good -eq $true) 'straight in the glide'
$mc = $tEv.GetField('MeasuredChecks').GetValue($null)
$unknown = @($m.Keys | Where-Object { -not $mc.Contains($_) })
Check ($unknown.Count -eq 0) ('every measurement is a listed check ' + ($unknown -join ','))

$crit = $cat.Criteria['plant.knee90']
$prop = $tEv.GetMethod('Proposal')
Check (($prop.Invoke($null, @($crit, $m['knee_plant'], $false, $null))) -eq $null) 'the knees at the plant (under the water): no proposal before the coach agrees'
Check (($prop.Invoke($null, @($cat.Criteria['t.wall'], $m['knee_plant'], $false, $null))) -eq $null) 'no proposal for a point the coach judges'
Check (($prop.Invoke($null, @($cat.Criteria['e.front_flexed'], $m['knee_plant'], $true, $null))) -eq 0) 'on a list of errors a good value means no error'
Check (($prop.Invoke($null, @($cat.Criteria['ka.rotation_slow'], $m['rotation_flip'], $true, $null))) -eq 1) 'automatic list: slow rotation is an error'
Check (($prop.Invoke($null, @($cat.Criteria['push.rigid'], $m['body_line'], $false, $null))) -eq $null) 'a partly measured point: no proposal before the coach agrees'
$tAg = T 'SwimTechAgreement'
$ag = [Activator]::CreateInstance($tAg); $ag.Samples = 12; $ag.Agree = 11
Check (($prop.Invoke($null, @($cat.Criteria['push.rigid'], $m['body_line'], $false, $ag))) -eq 1) 'a partly measured point: proposed once 11 of 12 answers agree'
Check (($prop.Invoke($null, @($crit, $m['knee_plant'], $false, $ag))) -eq 1) 'the knees at the plant: proposed once 11 of 12 answers agree'
$bad = [Activator]::CreateInstance($tAg); $bad.Samples = 10; $bad.Agree = 6
Check (($prop.Invoke($null, @($cat.Criteria['plant.knee90'], $m['knee_plant'], $false, $bad))) -eq $null) 'a measured point the coach disagrees with (6 of 10): no proposal'

# The automatic error lists.
$autoFor = $cat.GetType().GetMethod('AutoFor')
Check (($autoFor.Invoke($cat, @($turnKind, 'fs', $true))).Id -eq 'kv-auto-turn-fs') 'automatic list of a freestyle turn'
Check (($autoFor.Invoke($cat, @($turnKind, 'br', $false))).Id -eq 'kv-auto-turn-br') 'automatic list of a breaststroke turn'
Check (($autoFor.Invoke($cat, @($startKind, 'fly', $false))).Id -eq 'kv-auto-start') 'automatic list of a butterfly start'
Check (($autoFor.Invoke($cat, @($startKind, 'bk', $false))).Id -eq 'kv-auto-start-bk') 'automatic list of a backstroke start'
Check (($def.Invoke($cat, @($turnKind, 'fs', $true, $null))).Id -eq 'koe-demo-turn-fs') 'the automatic list is not the default sheet'
$autoTurn = $autoFor.Invoke($cat, @($turnKind, 'fs', $true))
$found = 0; $errs = @()
foreach ($it in $autoTurn.Items) {
  $x = $null
  if ($it.Criterion.Check -ne $null -and $m.ContainsKey($it.Criterion.Check)) { $x = $m[$it.Criterion.Check] }
  $pv = $prop.Invoke($null, @($it.Criterion, $x, $true, $null))
  if ($pv -ne $null) { $found++ }
  if ($pv -eq 1) { $errs += $it.Key }
}
Check ($found -eq 3 -and ($errs -join ',') -eq 'ka.rotation_slow') ('automatic list of the turn (the marks only, the skeleton under the water waits for the coach): ' + $found + ' answered, errors ' + ($errs -join ','))

# Anchors of the turn.
$anchor = $tEv.GetMethod('AnchorTimestamp')
Check (($anchor.Invoke($null, @($p, 'plant', $timing))) -eq 10000) 'plant of a flip turn = the wall contact'
Check (($anchor.Invoke($null, @($p, 'pushoff', $timing))) -eq 10300) 'push-off'
Check (($anchor.Invoke($null, @($p, 'in5', $timing))) -eq 7400) 'head at 5 m'
Check (($anchor.Invoke($null, @($p, 'rotation', $timing))) -eq 9400) 'rotation start'

# ---------------------------------------------------------------------------------------------------------------------------------
# A synthetic dive (to the right, 50 fps, 1 timestamp = 1 ms): signal 10000, take-off 10700, hands in 11050.
function Rot($ux, $uy, $deg) { $r = $deg * [Math]::PI / 180; return @(($ux * [Math]::Cos($r) - $uy * [Math]::Sin($r)), ($ux * [Math]::Sin($r) + $uy * [Math]::Cos($r))) }
function Rigid($hx, $hy, $theta, $headDeg, $legsUpDeg) {
  # The body as a straight line: the hips at (hx, hy), the trunk at theta (clockwise on the screen, 0 = to the right).
  $t = $theta * [Math]::PI / 180; $ux = [Math]::Cos($t); $uy = [Math]::Sin($t)
  $pts = @{}
  $sx = $hx + 50 * $ux; $sy = $hy + 50 * $uy
  $pts[1] = @($sx, $sy); $pts[2] = @($sx, $sy)
  $pts[3] = @(($sx + 25 * $ux), ($sy + 25 * $uy)); $pts[4] = $pts[3]
  $pts[5] = @(($sx + 50 * $ux), ($sy + 50 * $uy)); $pts[6] = $pts[5]
  # the nose beside the shoulder, towards the chest (neutral about 75-80°: the arms in front lift the shoulders beside the head)
  $n = Rot $ux $uy $headDeg
  $pts[0] = @(($sx + 22 * $n[0]), ($sy + 22 * $n[1]))
  $pts[7] = @($hx, $hy); $pts[8] = @($hx, $hy)
  $l = Rot (-$ux) (-$uy) ($legsUpDeg)   # from the line pointing back, clockwise on the screen is up
  $pts[9] = @(($hx + 40 * $l[0]), ($hy + 40 * $l[1])); $pts[10] = $pts[9]
  $pts[11] = @(($hx + 80 * $l[0]), ($hy + 80 * $l[1])); $pts[12] = $pts[11]
  return $pts
}
function Lerp($a, $b, $f) { return @(($a[0] + ($b[0] - $a[0]) * $f), ($a[1] + ($b[1] - $a[1]) * $f)) }
function DivePose($ts, $lifted) {
  $set = @{}
  $set[0] = @(135, 175); $set[1] = @(120, 165); $set[2] = @(120, 165); $set[3] = @(118, 183); $set[4] = @(118, 183); $set[5] = @(115, 200); $set[6] = @(115, 200)
  $set[7] = @(85, 145); $set[8] = @(85, 145); $set[9] = @(105, 170); $set[10] = @(70, 170); $set[11] = @(110, 200); $set[12] = @(60, 195)
  $off = @{}
  $off[0] = @(235, 143); $off[1] = @(210, 150); $off[2] = @(210, 150); $off[3] = @(245, 145); $off[4] = @(245, 145); $off[5] = @(280, 140); $off[6] = @(280, 140)
  $off[7] = @(160, 160); $off[8] = @(160, 160); $off[9] = @(140, 180); $off[10] = @(135, 155); $off[11] = @(115, 198); $off[12] = @(110, 150)
  if ($ts -le 10000) { $pts = $set }
  elseif ($ts -lt 10700) {
    $f = ($ts - 10000) / 700.0; $pts = @{}
    for ($j = 0; $j -le 12; $j++) { $pts[$j] = Lerp $set[$j] $off[$j] $f }
    if ($ts -ge 10200 -and $ts -le 10500) { $pts[3] = @(($pts[1][0] + 5), ($pts[1][1] + 25)); $pts[4] = $pts[3]; $pts[5] = @(($pts[1][0] + 30), ($pts[1][1] + 25)); $pts[6] = $pts[5] }
  }
  elseif ($ts -le 11050) {
    $s = ($ts - 10700) / 350.0
    $hx = 160 + 175 * $s; $hy = 160 - 40 * $s + 140 * $s * $s
    if ($ts -le 10875) { $th = -10 + 15 * ($ts - 10700) / 175.0 } else { $th = 5 + 25 * ($ts - 10875) / 175.0 }
    $pts = Rigid $hx $hy $th 78 0
    if ($ts -lt 10820) {
      $f = ($ts - 10700) / 120.0
      $pts[11] = Lerp $off[11] $pts[11] $f; $pts[12] = Lerp $off[12] $pts[12] $f
      $pts[9] = Lerp $off[9] $pts[9] $f; $pts[10] = Lerp $off[10] $pts[10] $f
    }
  }
  else {
    $s = [Math]::Min(1.0, ($ts - 11050) / 400.0)
    $hx = 335 + 500 * ($ts - 11050) / 1000.0; $hy = 260 + 70 * $s - 20 * $s * $s
    $th = 30 - 30 * $s
    $head = 78; $legs = 0
    if ($lifted -and $ts -ge 11080 -and $ts -le 11500) { $head = 25 }   # lifted through most of the entry
    if ($lifted -and $ts -ge 11100 -and $ts -le 11300) { $legs = 30 }
    $pts = Rigid $hx $hy $th $head $legs
  }
  $pts[13] = $pts[11]; $pts[14] = $pts[12]; $pts[15] = $pts[11]; $pts[16] = $pts[12]
  return $pts
}
function DiveRace($lifted) {
  $r = [Activator]::CreateInstance($tRace)
  $r.Kind = 'training'; $r.Style = 'fs'; $r.Pool = 50; $r.AthleteGender = 'Ανδρών'
  foreach ($mk in @(@('Signal', 10000, $null), @('FeetOff', 10700, $null), @('Entry', 11050, $null), @('TechSplit', 16000, 15))) {
    $x = [Activator]::CreateInstance($tMark); $x.Type = [Enum]::Parse($tType, $mk[0]); $x.Timestamp = [long]$mk[1]; $x.Wall = -1
    if ($null -ne $mk[2]) { $x.Distance = [int]$mk[2] }
    $r.Marks.Add($x)
  }
  $sk = [Activator]::CreateInstance($tSkel); $sk.Start = 9700; $sk.Step = 20
  for ($i = 0; $i -le 100; $i++) {
    $pts = DivePose (9700 + 20 * $i) $lifted
    for ($j = 0; $j -lt 17; $j++) { $sk.Data.Add([int][Math]::Round($pts[$j][0])); $sk.Data.Add([int][Math]::Round($pts[$j][1])); $sk.Data.Add(95) }
  }
  $r.Skeletons.Add($sk)
  return $r
}
foreach ($lifted in @($false, $true)) {
  $dr = DiveRace $lifted
  $dc = $tCalc.GetMethod('Compute').Invoke($null, @($dr, $timing, 0.5))
  $dp = $tTech.GetMethod('Compute').Invoke($null, @($dr, $dc, $timing))
  Check ($dp.Count -eq 1 -and $dp[0].Kind -eq $startKind) ('one start (lifted ' + $lifted + ')')
  $sp = $dp[0]
  $dm = $measure.Invoke($null, @($dr, $sp, $timing, $cat))
  Write-Output ('dive, head lifted ' + $lifted + ': block ' + $sp.Block + ' entry ' + $sp.Entry)
  foreach ($k in ($dm.Keys | Sort-Object)) { $x = $dm[$k]; Write-Output ('  ' + $k + ' = ' + [Math]::Round($x.Value, 3) + ' | ' + $x.Text + ' | good ' + $x.Good + ' | conf ' + [Math]::Round($x.Confidence, 2)) }
  $unknown = @($dm.Keys | Where-Object { -not $mc.Contains($_) })
  Check ($unknown.Count -eq 0) ('every measurement of the dive is a listed check ' + ($unknown -join ','))
  if (-not $lifted) {
    Check ($dm['set_hips_front'].Value -eq 1 -and $dm['set_hips_front'].Good -eq $true) 'set: hips in front of the back foot'
    Check ($dm['set_elbow'].Value -gt 170 -and $dm['set_elbow'].Good -eq $true) 'set: arms long'
    Check ($dm['front_knee_set'].Good -eq $true -and $dm['hips_vs_shoulders'].Good -eq $true) 'set: front knee and hips high'
    Check ($dm['rear_leg_height'].Good -eq $true -and $dm['shoulders_takeoff'].Good -eq $true) 'take-off: rear leg and shoulders'
    Check ($dm['arms_flex_takeoff'].Value -lt 120 -and $dm['arms_flex_takeoff'].Good -eq $true) 'the arms bend before the take-off'
    Check ($dm.ContainsKey('lead_leg_hip') -and $dm['lead_leg_hip'].Value -le 0.14 -and $dm['lead_leg_hip'].Good -eq $true) 'lead leg at the hip line in time'
    Check ($dm['legs_split'].Value -lt 2 -and $dm['legs_split'].Good -eq $true) 'legs together in flight'
    Check ([Math]::Abs($dm['head_spine_flight'].Value - 78) -lt 3 -and $dm['head_spine_flight'].Good -eq $true -and $dm['head_drop_flight'].Good -eq $true) 'head neutral in flight (about 78)'
    Check ($dm['flight_line'].Value -lt 5 -and $dm['arms_flight'].Value -eq 1) 'straight body and arms ahead in flight'
    Check ($dm['entry_streamline'].Value -eq 1 -and $dm['entry_streamline'].Good -eq $true) 'streamline before the entry'
    Check ($dm['legs_split_entry'].Value -lt 2) 'legs together as the hands enter'
    Check ($dm['head_lift_entry'].Value -gt 70 -and $dm['head_lift_entry'].Good -eq $true) 'head kept down through the entry'
    Check ([Math]::Abs($dm['entry_arch'].Value) -lt 3 -and $dm['entry_arch'].Good -eq $true) 'no banana'
    Check ($dm.ContainsKey('hips_peak') -and $dm['hips_peak'].Value -lt -0.2) 'the hips at their highest early in the flight'
    Check ($dm.ContainsKey('start_15') -and [Math]::Abs($dm['start_15'].Value - 6.0) -lt 1e-6 -and $dm['start_15'].Text -like '*6*') ('start to 15 m from the signal: ' + $dm['start_15'].Text)
    Check (($levelOf.Invoke($null, @($cat, 'start_15', 'fs', 'Ανδρών', $dm['start_15'].Value))) -eq 6) 'free men 6.0 s from the signal = level 6'
  } else {
    Check ($dm['head_lift_entry'].Value -lt 35 -and $dm['head_lift_entry'].Good -eq $false) 'head lifted at the entry: found'
    Check ($dm['entry_arch'].Value -gt 25 -and $dm['entry_arch'].Good -eq $false) 'banana at the entry: found'
    $autoStart = $autoFor.Invoke($cat, @($startKind, 'fs', $false))
    $errs = @()
    foreach ($it in $autoStart.Items) {
      $x = $null
      if ($it.Criterion.Check -ne $null -and $dm.ContainsKey($it.Criterion.Check)) { $x = $dm[$it.Criterion.Check] }
      if (($prop.Invoke($null, @($it.Criterion, $x, $true, $null))) -eq 1) { $errs += $it.Key }
    }
    Check (($errs -join ',') -eq 'ka.head_lift,ka.banana') ('automatic list of the dive: errors ' + ($errs -join ','))
  }
}

# ---------------------------------------------------------------------------------------------------------------------------------
# Learning from the coach's answers: the push of a turn judged good up to 0.34 s by the coach (the catalogue says 0.30 s).
$tLearn = T 'SwimTechLearning'; $tSample = T 'SwimTechSample'
$learn = [Activator]::CreateInstance($tLearn)
$i = 0
foreach ($v in @(0.25, 0.27, 0.29, 0.31, 0.32, 0.33, 0.34)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'k' + $i; $i++; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $v; $x.Good = $true; $learn.Put($x) }
foreach ($v in @(0.37, 0.38, 0.40, 0.42, 0.45, 0.50, 0.55)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'k' + $i; $i++; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $v; $x.Good = $false; $learn.Put($x) }
$pushCheck = $cat.Checks['push_time']
$lim = $learn.LimitOf($pushCheck)
Check ($null -ne $lim -and [Math]::Abs($lim.Max - 0.355) -lt 1e-6 -and $lim.Agree -eq 14 -and $lim.StartAgree -eq 10) ('learned limit of the push: ' + $lim.Max + ' (' + $lim.Agree + '/' + $lim.Samples + ', at first ' + $lim.StartAgree + ')')
$judge = $tEv.GetMethod('Judge')
Check (($judge.Invoke($null, @($pushCheck, 0.33))) -eq $false) 'without the learning 0.33 s is slow'
$tLearn.GetField('Override').SetValue($null, $learn)
try {
  Check (($judge.Invoke($null, @($pushCheck, 0.33))) -eq $true) 'with the learning 0.33 s is good'
  $agr = $learn.AgreementOf('push.short', $pushCheck)
  Check ($agr.Samples -eq 12 -and $agr.Agree -eq 12 -and $agr.Reliable) ('agreement with the coach ' + $agr.Text)
  $few = [Activator]::CreateInstance($tLearn)
  foreach ($v in @(0.25, 0.4)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'f' + $v; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $v; $x.Good = ($v -lt 0.3); $few.Put($x) }
  Check ($null -eq $few.LimitOf($pushCheck)) 'nothing learned from 2 answers'
  $learn.Remove('k0', $false)
  Check ($learn.Samples.Count -eq 13) 'an answer taken away'
  $learn.Remove('k1', $true)
  Check ($learn.Samples.Count -eq 8) 'answers taken away by their start (k1, k10…k13)'
  $eqLearn = [Activator]::CreateInstance($tLearn)
  Check ($null -eq $eqLearn.LimitOf($cat.Checks['streamline'])) 'nothing learned for a yes/no check'
} finally {
  $tLearn.GetField('Override').SetValue($null, $null)
}
# The side view of a skeleton: a swimmer read as standing (trunk folded, shoulders apart) is not used by the evaluation.
$front = [Activator]::CreateInstance($tSkel); $front.Start = 0; $front.Step = 20
for ($i = 0; $i -lt 5; $i++) {
  $pts = @{}
  $pts[0] = @(100, 100); $pts[1] = @(80, 110); $pts[2] = @(120, 110); $pts[3] = @(70, 130); $pts[4] = @(130, 130); $pts[5] = @(65, 150); $pts[6] = @(135, 150)
  $pts[7] = @(90, 114); $pts[8] = @(110, 114); $pts[9] = @(90, 160); $pts[10] = @(110, 160); $pts[11] = @(90, 200); $pts[12] = @(110, 200)
  $pts[13] = $pts[11]; $pts[14] = $pts[12]; $pts[15] = $pts[11]; $pts[16] = $pts[12]
  for ($j = 0; $j -lt 17; $j++) { $front.Data.Add($pts[$j][0]); $front.Data.Add($pts[$j][1]); $front.Data.Add(95) }
}
$pp = New-Object 'System.Drawing.PointF[]' 17; $vv = New-Object 'float[]' 17
Check ($front.TryGet(40, 2, $pp, $vv, $false)) 'a skeleton read from the front: read as before'
Check (-not $front.TryGet(40, 2, $pp, $vv, $true)) 'a skeleton read from the front: not a side view'
Check ($skel.TryGet(10300, 2, $pp, $vv, $true)) 'the synthetic turn: a side view'
Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
