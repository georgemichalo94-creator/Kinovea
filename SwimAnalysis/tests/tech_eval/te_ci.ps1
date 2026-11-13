# te_ci.ps1: the CI check of the technique evaluation (catalogue, levels, measurements from the marks and from a synthetic skeleton,
# proposals, default sheets, automatic error lists, learning, side views) on a fictional training turn and start, with the FICTIONAL
# catalogue mini_catalog.json (invented sheets and level tables; the coach's real catalogue stays on the analysts' computers).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File te_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -catalog mini_catalog.json
param([string]$sm, [string]$bin, [string]$catalog)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path; $catalog = (Resolve-Path $catalog).Path
# The other DLLs of the program from its folder. Plain string work only: in Windows PowerShell a handler that loads anything else
# (New-Object, cmdlets) can be asked again for the resources of what it loads, and so on until the stack ends.
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
# (Its own name: PowerShell names are not case sensitive, and the checks of the shared learning have a Kinovea "$A".)
$smAssembly = [System.Reflection.Assembly]::LoadFrom($sm)
function T($n) { return $smAssembly.GetType('Kinovea.ScreenManager.' + $n, $true) }
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
# ---------------------------------------------------------------------------------------------------------------------------------
# (2.16.0) The single lists (a fictional catalogue made from the one above): one list for the start and one for the turn of a style,
# one for a change of stroke of a medley; the older sheets hidden, their evaluations moved into the single list.
$singlePath = [System.IO.Path]::Combine([System.IO.Path]::GetDirectoryName($catalog), 'mini_single.json')
$one = $tCat.GetMethod('Parse').Invoke($null, @([System.IO.File]::ReadAllText($singlePath)))
Check ($one.SingleLists -and $one.HasTransitions -and -not $cat.SingleLists -and -not $cat.HasTransitions) 'single lists and medley changes known'
$oneFor = @($one.SheetsFor($turnKind, 'fs')) | ForEach-Object { $_.Id }
Check (($oneFor -join ',') -eq 'kv-turn-fs') ('a freestyle turn: only its single list (' + ($oneFor -join ',') + ')')
$oneStart = @($one.SheetsFor($startKind, 'fs')) | ForEach-Object { $_.Id }
Check (($oneStart -join ',') -eq 'kv-start-fs') ('a freestyle start: only its single list (' + ($oneStart -join ',') + ')')
$imChange = @($one.SheetsFor($turnKind, 'im-fly-bk')) | ForEach-Object { $_.Id }
Check (($imChange -join ',') -eq 'kv-turn-im-fly-bk') ('the medley turn butterfly to backstroke: its list (' + ($imChange -join ',') + ')')
Check (@($one.SheetsFor($turnKind, 'im-bk-br')).Count -eq 0 -and @($one.SheetsFor($startKind, 'im-fly-bk')).Count -eq 0) 'no list for a change without one, nor for a start'
Check ((@($one.SheetsFor($turnKind, 'im')) | ForEach-Object { $_.Id }) -contains 'kv-turn-im-fly-bk') 'a medley training turn: the lists of the changes'
Check (($def.Invoke($one, @($turnKind, 'fs', $true, 'koe-demo-turn-fs'))).Id -eq 'kv-turn-fs') 'an older last choice is not kept'
Check ($one.Sheet('koe-demo-turn-fs').Hidden -and $null -ne $one.Sheet('kv-auto-turn-fs')) 'the older sheets still read (hidden)'
$tf = $one.Sheet('kv-turn-fs')
$rq = @($tf.Items) | Where-Object { $_.Key -eq 'ok.rotation_quick' }
Check ($null -ne $rq -and $rq.StandsFor('ka.rotation_slow') -and $rq.StandsFor('ok.rotation_quick') -and -not $rq.StandsFor('ka.push_slow')) 'a point of the single list stands for the error of the older list'

# Older evaluations of a turn move into its single list.
$tEval = T 'SwimTechEval'; $tScore = T 'SwimTechScore'
function Score($v, $auto, $snap) { $s = [Activator]::CreateInstance($tScore); $s.Value = $v; $s.Auto = $auto; $s.SnapshotId = $snap; return $s }
$evals = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tEval))
$dropped = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tScore))
$target = [Activator]::CreateInstance($tEval); $target.PhaseId = 'turn-x'; $target.SheetId = 'kv-turn-fs'
$target.Scores.Add('push.short', (Score 0 $false 'snapT'))
$oldA = [Activator]::CreateInstance($tEval); $oldA.PhaseId = 'turn-x'; $oldA.SheetId = 'kv-auto-turn-fs'
$oldA.Scores.Add('ka.rotation_slow', (Score 1 $false 'snapA')); $oldA.Scores.Add('ka.push_slow', (Score 0 $true $null)); $oldA.Scores.Add('ka.line', (Score 1 $true $null))
$oldB = [Activator]::CreateInstance($tEval); $oldB.PhaseId = 'turn-x'; $oldB.SheetId = 'koe-demo-turn-fs'; $oldB.Notes = 'Σημείωση Β'
$oldB.Scores.Add('t.streamline', (Score 1 $false $null)); $oldB.Scores.Add('t.kicks', (Score 0 $false 'snapB'))
$other = [Activator]::CreateInstance($tEval); $other.PhaseId = 'turn-y'; $other.SheetId = 'kv-auto-turn-fs'; $other.Scores.Add('ka.rotation_slow', (Score 0 $true $null))
$evals.Add($target); $evals.Add($oldA); $evals.Add($oldB); $evals.Add($other)
$moved = $one.MergeOld($evals, 'turn-x', $tf, $dropped, '1/11/2026 08:00')
$sc = $target.Scores
Write-Output ('merge: moved ' + $moved + ', evaluations ' + $evals.Count + ', single list ' + (($sc.Keys | Sort-Object) -join ','))
Check ($sc['ok.rotation_quick'].Value -eq 0 -and -not $sc['ok.rotation_quick'].Auto -and $sc['ok.rotation_quick'].SnapshotId -eq 'snapA') 'an error of the older list: ✗ in the single list, with its frame'
Check ($sc['push.short'].Value -eq 0 -and $sc['push.short'].SnapshotId -eq 'snapT') 'the coach''s own score of a point stays'
Check ($sc['t.streamline'].Value -eq 1 -and -not $sc.ContainsKey('ka.line') -and -not $sc.ContainsKey('t.kicks')) 'the same point moved; a point the list has not did not'
Check (-not $evals.Contains($oldA) -and $evals.Contains($oldB) -and $oldB.Scores.Count -eq 1 -and $oldB.Scores.ContainsKey('t.kicks')) 'the automatic older list went; the one with a score of the coach not moved stays with it'
Check ($target.Notes -eq 'Σημείωση Β' -and $target.Date -eq '1/11/2026 08:00') 'the notes moved'
Check ($evals.Contains($other) -and $other.Scores.Count -eq 1) 'another phase as it was'
Check ($dropped.Count -eq 0) 'no frame to drop'
Check ($moved -ge 3) ('changes counted: ' + $moved)
$again = $one.MergeOld($evals, 'turn-x', $tf, $dropped, '1/11/2026 09:00')
Check ($again -eq 0 -and $target.Date -eq '1/11/2026 08:00') ('nothing moves twice: ' + $again)
# A single list's point overrides an automatic score with the coach's older answer (and the older frame goes with the new one).
$e2 = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tEval))
$t2 = [Activator]::CreateInstance($tEval); $t2.PhaseId = 'p'; $t2.SheetId = 'kv-turn-fs'; $t2.Scores.Add('ok.rotation_quick', (Score 1 $true 'snapAuto'))
$o2 = [Activator]::CreateInstance($tEval); $o2.PhaseId = 'p'; $o2.SheetId = 'kv-auto-turn-fs'; $o2.Scores.Add('ka.rotation_slow', (Score 1 $false 'snapCoach'))
$e2.Add($t2); $e2.Add($o2)
$d2 = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tScore))
$null = $one.MergeOld($e2, 'p', $tf, $d2, 'x')
Check ($t2.Scores['ok.rotation_quick'].Value -eq 0 -and -not $t2.Scores['ok.rotation_quick'].Auto -and $t2.Scores['ok.rotation_quick'].SnapshotId -eq 'snapCoach' -and $d2.Count -eq 1 -and $d2[0].SnapshotId -eq 'snapAuto' -and $e2.Count -eq 1) 'the coach''s older answer over an automatic one'

# (2.16.1) A point of a single list whose words changed (a new key, the old one in its "same"): its scores under the old key move to it.
$moveRenamed = $tCat.GetMethod('MoveRenamed')
$t3 = [Activator]::CreateInstance($tEval); $t3.PhaseId = 'q'; $t3.SheetId = 'kv-turn-fs'
$t3.Scores.Add('ka.rotation_slow', (Score 0 $false 'snapOld')); $t3.Scores.Add('push.short', (Score 1 $false $null)); $t3.Scores.Add('zz.unknown', (Score 1 $false $null))
$d3 = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tScore))
$m3 = $moveRenamed.Invoke($null, [object[]]@($t3.psobject.BaseObject, $tf.psobject.BaseObject, $d3.psobject.BaseObject))
Check ($m3 -eq 1 -and $t3.Scores['ok.rotation_quick'].Value -eq 0 -and $t3.Scores['ok.rotation_quick'].SnapshotId -eq 'snapOld' -and -not $t3.Scores.ContainsKey('ka.rotation_slow') -and $t3.Scores.ContainsKey('push.short') -and $t3.Scores.ContainsKey('zz.unknown')) ('a renamed point keeps its score (' + $m3 + ')')
$t3.Scores.Add('ka.rotation_slow', (Score 1 $false 'snapOlder'))
$m4 = $moveRenamed.Invoke($null, [object[]]@($t3.psobject.BaseObject, $tf.psobject.BaseObject, $d3.psobject.BaseObject))
Check ($m4 -eq 1 -and $t3.Scores['ok.rotation_quick'].SnapshotId -eq 'snapOld' -and -not $t3.Scores.ContainsKey('ka.rotation_slow') -and $d3.Count -eq 1 -and $d3[0].SnapshotId -eq 'snapOlder') 'the coach''s score of the new point stays; the older one goes with its frame'
$m5 = $moveRenamed.Invoke($null, [object[]]@($t3.psobject.BaseObject, $tf.psobject.BaseObject, $d3.psobject.BaseObject))
Check ($m5 -eq 0) 'nothing moves twice'
# (2.16.7) A point that left its list keeps its older score, but its 🤖 waits for nothing: the count of the 🤖 that wait (and the
# confirm button) and the errors there walk the points of the list, as the confirmation and the totals do.
$pendingAuto = $tEv.GetMethod('PendingAuto'); $errorsPresent = $tEv.GetMethod('ErrorsPresent')
$t5 = [Activator]::CreateInstance($tEval); $t5.PhaseId = 'r'; $t5.SheetId = 'kv-turn-fs'
$t5.Scores.Add('push.short', (Score 1 $true $null)); $t5.Scores.Add('zz.left', (Score 1 $true $null)); $t5.Scores.Add('ok.rotation_quick', (Score 0 $false $null))
$p5 = $pendingAuto.Invoke($null, [object[]]@($tf.psobject.BaseObject, $t5.psobject.BaseObject))
$e5 = $errorsPresent.Invoke($null, [object[]]@($tf.psobject.BaseObject, $t5.psobject.BaseObject))
$n5 = $pendingAuto.Invoke($null, [object[]]@($tf.psobject.BaseObject, $null))
Check ($p5 -eq 1 -and $e5 -eq 1 -and $n5 -eq 0 -and $t5.Scores.Count -eq 3) ('a point that left the list: its 🤖 does not wait (' + $p5 + ', ' + $e5 + ', ' + $n5 + ')')

# The learning of a point of a single list counts the answers on the older points it stands for.
$learn2 = [Activator]::CreateInstance($tLearn)
$i = 0
foreach ($v in @(0.5, 0.55, 0.6, 1.2, 1.3)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'r' + $i; $i++; $x.Check = 'rotation_flip'; $x.Criterion = 'ka.rotation_slow'; $x.Measured = 100 * $v / 0.6; $x.Good = ($v -lt 1); $learn2.Put($x) }
$x = [Activator]::CreateInstance($tSample); $x.Key = 'r9'; $x.Check = 'rotation_flip'; $x.Criterion = 'ok.rotation_quick'; $x.Measured = 90; $x.Good = $true; $learn2.Put($x)
$keys = [string[]]@('ok.rotation_quick', 'ka.rotation_slow')
$both = $learn2.AgreementOf([System.Collections.Generic.IEnumerable[string]]$keys, $one.Checks['rotation_flip'])
$alone = $learn2.AgreementOf('ok.rotation_quick', $one.Checks['rotation_flip'])
Check ($both.Samples -eq 6 -and $alone.Samples -eq 1) ('answers of the older point count: ' + $both.Samples + ' with, ' + $alone.Samples + ' without')

# ---------------------------------------------------------------------------------------------------------------------------------
# (2.16.5) The shared learning: the answers of one Kinovea (A) go through the rows of the sheet (as JSON text, as the Apps Script keeps
# them) into another (B), which then learns the same limit; answers taken away, newer and older answers of the same key.
$tShare = T 'SwimAnalysisManager'; $tLearnRec = T 'SwimLearning'; $tForget = T 'SwimTechForget'
$rowOfSample = $tShare.GetMethod('TechShareRow', [type[]]@($tSample))
$rowOfForget = $tShare.GetMethod('TechShareRow', [type[]]@($tForget, [string]))
$readShare = $tShare.GetMethod('ReadTechShare')
$here = $tLearnRec.GetField('Here')
# (The rows stay .NET objects: PowerShell reads the members of a JObject as its JSON properties.)
$tJArray = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll')).GetType('Newtonsoft.Json.Linq.JArray')
$parseArray = $tJArray.GetMethod('Parse', [type[]]@([string]))
function Rows($learning, $since) {
  # The rows a pull of the sheet gives: the answers of this Kinovea from 'since' (an index), then its answers taken away.
  $arr = [Activator]::CreateInstance($tJArray)
  for ($k = $since; $k -lt $learning.Samples.Count; $k++) { $arr.Add($rowOfSample.Invoke($null, [object[]]@($learning.Samples[$k].psobject.BaseObject))) }
  foreach ($f in $learning.Forgotten) { if (-not $f.Shared) { $arr.Add($rowOfForget.Invoke($null, [object[]]@($f.psobject.BaseObject, 'kvA'))) } }
  # Through text, as the sheet keeps them.
  $outArgs = New-Object 'object[]' 3
  $outArgs[0] = $parseArray.Invoke($null, [object[]]@($arr.ToString()))
  $null = $readShare.Invoke($null, $outArgs)
  $pair = New-Object 'object[]' 2
  $pair[0] = $outArgs[1]; $pair[1] = $outArgs[2]
  return ,$pair
}
function Merge($learning, $rows) { return $learning.Merge($rows[0], $rows[1]) }
$here.SetValue($null, 'kvA')
$A = [Activator]::CreateInstance($tLearn)
$i = 0
foreach ($v in @(0.25, 0.27, 0.29, 0.31, 0.32, 0.33, 0.34)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'race1|turn-1|kv-turn-fs|p' + $i; $i++; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $v; $x.Good = $true; $x.Date = '2026-11-04'; $A.Put($x) }
foreach ($v in @(0.37, 0.38, 0.40, 0.42, 0.45, 0.50, 0.55)) { $x = [Activator]::CreateInstance($tSample); $x.Key = 'race2|turn-1|kv-turn-fs|p' + $i; $i++; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $v; $x.Good = $false; $x.Date = '2026-11-04'; $A.Put($x) }
$s0 = $A.Samples[0]
# (PowerShell gives a DateTime? as the DateTime or $null.)
Check ($s0.Id.Length -eq 32 -and $s0.Origin -eq 'kvA' -and $null -ne $s0.At -and $A.Samples[13].At -gt $s0.At) 'an answer of A: its id, its Kinovea, its moment (in order)'
$here.SetValue($null, 'kvB')
$B = [Activator]::CreateInstance($tLearn)
Check ($null -eq $B.LimitOf($pushCheck)) 'B has learned nothing yet'
$rows = Rows $A 0
Check ($rows[0].Count -eq 14 -and $rows[1].Count -eq 0) ('the rows of A read back: ' + $rows[0].Count + ' answers')
$n = Merge $B $rows
$limB = $B.LimitOf($pushCheck); $limA = $A.LimitOf($pushCheck)
Check ($n -eq 14 -and $B.Samples.Count -eq 14 -and $B.SharedCount -eq 14) ('B took the 14 answers of A (' + $n + ', from the others ' + $B.SharedCount + ')')
Check ($null -ne $limB -and $limB.Max -eq $limA.Max -and $limB.Agree -eq $limA.Agree) ('B learned the limit of A: ' + $limB.Max)
$agrB = $B.AgreementOf('push.short', $pushCheck)
Check ($agrB.Shared -eq $agrB.Samples -and $agrB.Samples -gt 0) ('the agreement says the answers came from the others: ' + $agrB.Shared + '/' + $agrB.Samples)
Check ((Merge $B (Rows $A 0)) -eq 0 -and $B.Samples.Count -eq 14) 'the same rows again: nothing'
$b0 = $B.Samples | Where-Object { $_.Key -eq $s0.Key }
Check ($b0.Id -eq $s0.Id -and $b0.Origin -eq 'kvA' -and [Math]::Abs(($b0.At - $s0.At).TotalMilliseconds) -lt 1 -and $b0.Measured -eq 0.25 -and $b0.Good) 'an answer as A gave it (id, Kinovea, moment, measurement, answer)'
# A changes an answer: B follows; an older answer of the same key that comes later does not replace it.
$here.SetValue($null, 'kvA')
$old = $A.Samples[1]
$x = [Activator]::CreateInstance($tSample); $x.Key = $old.Key; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = $old.Measured; $x.Good = $false; $x.Date = '2026-11-04'; $A.Put($x)
Check ($A.Samples.Count -eq 14 -and $A.Samples[13].Key -eq $old.Key -and $A.Samples[13].Id -ne $old.Id) 'the new answer of A replaces its old one (at the end)'
$here.SetValue($null, 'kvB')
$n = Merge $B (Rows $A 13)
$b1 = $B.Samples | Where-Object { $_.Key -eq $old.Key }
Check ($n -eq 1 -and $B.Samples.Count -eq 14 -and -not $b1.Good) ('B follows the new answer (' + $n + ')')
$stale = [Activator]::CreateInstance($tSample); $stale.Key = $old.Key; $stale.Check = 'push_time'; $stale.Criterion = 'push.short'; $stale.Measured = 0.9; $stale.Good = $true
$stale.Id = 'older-one'; $stale.Origin = 'kvC'; $stale.At = $old.At.AddMinutes(-1)
$lst = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tSample)); $lst.Add($stale)
$fl = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tForget))
Check (($B.Merge($lst, $fl)) -eq 0 -and -not ($B.Samples | Where-Object { $_.Key -eq $old.Key }).Good) 'an older answer of the same key does not replace the newer'
# A takes an answer away, then an evaluation (the start of its keys): B too; an answer given after it comes back.
$here.SetValue($null, 'kvA')
$gone = $A.Samples[2].Key
$A.Remove($gone, $false)
Check ($A.Forgotten.Count -eq 1 -and $A.Forgotten[0].Key -eq $gone -and -not $A.Forgotten[0].Prefix) 'A keeps the answer taken away for the others'
$here.SetValue($null, 'kvB')
$n = Merge $B (Rows $A 13)
Check ($n -eq 1 -and $B.Samples.Count -eq 13 -and -not ($B.Samples | Where-Object { $_.Key -eq $gone }) -and $B.Forgotten.Count -eq 1 -and $B.Forgotten[0].Shared) ('B takes it away too (' + $n + ')')
Check ((Merge $B (Rows $A 0)) -eq 0 -and $B.Samples.Count -eq 13) 'the answers before it do not come back'
$here.SetValue($null, 'kvA')
$x = [Activator]::CreateInstance($tSample); $x.Key = $gone; $x.Check = 'push_time'; $x.Criterion = 'push.short'; $x.Measured = 0.30; $x.Good = $true; $x.Date = '2026-11-04'; $A.Put($x)
$A.Remove('race2|', $true)
Check ($A.Samples.Count -eq 7 -and $A.Forgotten.Count -eq 2 -and $A.Forgotten[1].Prefix) 'A deletes an evaluation (7 answers go)'
$here.SetValue($null, 'kvB')
$n = Merge $B (Rows $A 6)
Check ($B.Samples.Count -eq 7 -and -not ($B.Samples | Where-Object { $_.Key.StartsWith('race2|') }) -and ($B.Samples | Where-Object { $_.Key -eq $gone })) ('B: the evaluation went, the answer given again came back (' + $B.Samples.Count + ')')
$whenOf = $tLearn.GetMethod('When')
$order = @(); foreach ($bs in $B.Samples) { $order += $whenOf.Invoke($null, [object[]]@($bs.psobject.BaseObject)) }
$sorted = $true; for ($k = 1; $k -lt $order.Count; $k++) { if ($order[$k] -lt $order[$k - 1]) { $sorted = $false } }
Check $sorted 'the answers in the order they were given'
# An answer older than 2.16.5 (no id, no moment): the same id on every computer, its day as its moment, it is this Kinovea's.
$legacy = [Activator]::CreateInstance($tSample); $legacy.Key = 'race9|start|kv-start-fs|x'; $legacy.Check = 'push_time'; $legacy.Measured = 0.3; $legacy.Good = $true; $legacy.Date = '2026-10-20'
$sid = $tLearn.GetMethod('StableId')
$lb = $legacy.psobject.BaseObject
$id1 = $sid.Invoke($null, [object[]]@($lb)); $id2 = $sid.Invoke($null, [object[]]@($lb))
$when = $whenOf.Invoke($null, [object[]]@($lb))
Check ($id1 -eq $id2 -and $id1.StartsWith('t') -and $id1.Length -eq 21 -and $when -eq [DateTime]::new(2026, 10, 20, 0, 0, 0, [DateTimeKind]::Utc) -and $tLearn.GetMethod('Own').Invoke($null, [object[]]@($lb))) ('an older answer: id ' + $id1 + ', moment ' + $when.ToString('o'))
$here.SetValue($null, $null)

# ---------------------------------------------------------------------------------------------------------------------------------
# (2.16.6) The time of a PDF in its message: with the parts the Apps Script 2.16.6 measured, else the time of the request.
$tRes = T 'SwimSheetResult'
$pdfTime = $tShare.GetMethod('PdfTimeText')
$res = [Activator]::CreateInstance($tRes); $res.Seconds = 18.4
$res.Raw = $tJArray.Assembly.GetType('Newtonsoft.Json.Linq.JObject').GetMethod('Parse', [type[]]@([string])).Invoke($null, [object[]]@('{"timings":{"lock":0,"report":9100,"pdf":6200,"archive":2900,"total":18300}}'))
$txt = $pdfTime.Invoke($null, [object[]]@($res))
Check ($txt -eq 'σε 18″ (report 9″ · PDF 6″ · KOE App 3″)') ('the time of a PDF: ' + $txt)
$res.Raw = $tJArray.Assembly.GetType('Newtonsoft.Json.Linq.JObject').GetMethod('Parse', [type[]]@([string])).Invoke($null, [object[]]@('{"timings":{"lock":12500,"report":9100,"pdf":6200,"archive":2900,"total":30800}}'))
$res.Seconds = 31.2
$txt = $pdfTime.Invoke($null, [object[]]@($res))
Check ($txt -eq 'σε 31″ (report 9″ · PDF 6″ · KOE App 3″ · αναμονή 13″)') ('with the wait for another request: ' + $txt)
$res.Raw = $null; $res.Seconds = 42.0
$txt = $pdfTime.Invoke($null, [object[]]@($res))
Check ($txt -eq 'σε 42″') ('an older script: the time of the request only (' + $txt + ')')
# A straight joint (no angle to draw) and joints off any picture do not stop the drawing of the skeleton (GDI+ says "out of memory").
# (GDI+ only on Windows: the CI runs this in Windows PowerShell.)
if (($PSVersionTable.PSEdition -eq 'Desktop') -or $IsWindows) {
$tPaint = T 'SwimSkeletonPainter'
$paint = $tPaint.GetMethod('Paint', [type[]]@([System.Drawing.Graphics], [System.Drawing.PointF[]], [float[]], [float], [bool]))
$bmp = New-Object System.Drawing.Bitmap 400, 300
$gr = [System.Drawing.Graphics]::FromImage($bmp)
$okDraw = $true
try {
  foreach ($case in @('straight', 'nan', 'far')) {
    $pts = New-Object 'System.Drawing.PointF[]' 17; $vis = New-Object 'float[]' 17
    for ($j = 0; $j -lt 17; $j++) { $pts[$j] = New-Object System.Drawing.PointF (100 + 10 * $j), 150; $vis[$j] = 0.95 }
    if ($case -eq 'nan') { $pts[9] = New-Object System.Drawing.PointF ([float]::NaN), ([float]::NaN) }
    if ($case -eq 'far') { $pts[9] = New-Object System.Drawing.PointF 3e9, 3e9 }
    # (The arrays of New-Object come wrapped for PowerShell: reflection needs the arrays themselves.)
    $null = $paint.Invoke($null, [object[]]@($gr, $pts.psobject.BaseObject, $vis.psobject.BaseObject, [float]1, $true))
  }
} catch { $okDraw = $false; Write-Output ('  ' + $_.Exception.InnerException) }
$gr.Dispose(); $bmp.Dispose()
Check $okDraw 'a skeleton with a straight joint, a NaN and a joint far away is drawn without an error'
} else { Write-Output 'skip the drawing of a skeleton (GDI+ only on Windows)' }

# (2.22.0) The check of the proposals of the technical pilot on a fictional turn: from the contact, then from the first proposal of
# the reading in the order of the video, each one once (until 2.22.0 it went on after the contact and came back to the approach at
# the end: its strokes, named «Χεριά 1, 2…» as those after the turn, looked asked again); the strokes named by their wall.
$tMan = T 'SwimAnalysisManager'; $tProp = T 'SwimProposal'
$bfs = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
function KindOf($n) { return $tMan.GetField($n, $bfs).GetValue($null) }
$batch = 'b1'
$plan = @(@('KindTechStroke', 1.0, 1), @('KindTechStroke', 2.2, 2), @('KindTechStroke', 3.4, 3), @('KindTechStroke', 4.6, 4), @('KindTechLastStroke', 5.8, 0),
  @('KindTechRotation', 6.3, 0), @('KindTechWall', 6.9, 0), @('KindTechPushoff', 7.6, 0), @('KindTechKick', 8.2, 1), @('KindTechKick', 8.7, 2),
  @('KindTechKick', 9.2, 3), @('KindTechBreakout', 10.0, 0), @('KindTechFirstStroke', 10.4, 0), @('KindTechStroke', 11.6, 2), @('KindTechStroke', 12.8, 3))
$typeOf = @{ KindTechStroke = 'Stroke'; KindTechLastStroke = 'Stroke'; KindTechFirstStroke = 'Stroke'; KindTechRotation = 'RotationStart'; KindTechWall = 'WallContact';
  KindTechPushoff = 'FeetOff'; KindTechKick = 'Kick'; KindTechBreakout = 'Breakout' }
$turnRace = [Activator]::CreateInstance($tRace)
foreach ($row in $plan) {
  $p = [Activator]::CreateInstance($tProp)
  $p.Kind = KindOf $row[0]; $p.Type = [Enum]::Parse($tType, $typeOf[$row[0]]); $p.Timestamp = [long]([double]$row[1] * 1000); $p.Index = [int]$row[2]
  $p.Batch = $batch; $p.Confidence = 0.6
  $turnRace.Proposals.Add($p)
}
$labelIn = $tMan.GetMethod('ProposalLabel', [type[]]@($tProp, $tRace))
$at = { param($s) $turnRace.Proposals | Where-Object { $_.Timestamp -eq [long]($s * 1000) } | Select-Object -First 1 }
# (What comes out of a pipeline is wrapped for PowerShell: reflection needs the objects themselves.)
$names = @((& $at 1.0), (& $at 4.6), (& $at 11.6), (& $at 12.8)) | ForEach-Object { $labelIn.Invoke($null, [object[]]@($_.psobject.BaseObject, $turnRace.psobject.BaseObject)) }
Write-Output ('     the strokes of the turn: ' + ($names -join ' | '))
Check ($names[0].StartsWith('5η χεριά πριν τον τοίχο') -and $names[1].StartsWith('2η χεριά πριν τον τοίχο') -and $names[2].StartsWith('2η χεριά μετά την έξοδο') -and
  $names[3].StartsWith('3η χεριά μετά την έξοδο')) 'the strokes of the technical pilot named by their wall: counted back before it, after the breakout after it'
$nextIn = $tMan.GetMethod('NextInCheck', $bfs)
$pending = [Activator]::CreateInstance(([type]'System.Collections.Generic.List`1').MakeGenericType($tProp))
foreach ($p in ($turnRace.Proposals | Sort-Object Timestamp)) { $pending.Add($p) }
$current = & $at 6.9
$seen = New-Object 'System.Collections.Generic.List[double]'
for ($guard = 0; $current -ne $null -and $guard -lt 40; $guard++) {
  $seen.Add($current.Timestamp / 1000.0)
  $null = $pending.Remove($current)
  $current = $nextIn.Invoke($null, [object[]]@($pending.psobject.BaseObject, [long]$current.Timestamp, $current.psobject.BaseObject))
}
$expected = @(6.9, 1.0, 2.2, 3.4, 4.6, 5.8, 6.3, 7.6, 8.2, 8.7, 9.2, 10.0, 10.4, 11.6, 12.8)
Write-Output ('     the check of the turn: ' + (($seen | ForEach-Object { $_.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) }) -join ' '))
Check ((($seen | ForEach-Object { $_.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) }) -join ' ') -eq (($expected | ForEach-Object { $_.ToString('0.0', [Globalization.CultureInfo]::InvariantCulture) }) -join ' ')) 'the check of the turn: the contact, then each proposal once in the order of the video'

Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
