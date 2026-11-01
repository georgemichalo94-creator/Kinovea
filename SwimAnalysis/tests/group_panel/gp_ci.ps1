# gp_ci.ps1: the CI check of the technique analysis of a group of screens (2.16.0) on a FICTIONAL training start: the joined images
# (SwimTiles: each image in its shape, all the rows the same height, nothing cut, no black between them) and the panel of the group
# (the start at a few moments: its title and clock, its times as they pass, the speeds of its segments). The images go to -out for a
# look (an artifact of the run). Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File gp_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
New-Item -ItemType Directory -Force -Path $out | Out-Null
$out = (Resolve-Path $out).Path
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
$all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

# ---- The joined images.
$tiles = T 'SwimTiles'
$layout = $tiles.GetMethod('Layout')
function Places($aspects, $w, $h) {
  $list = New-Object 'System.Collections.Generic.List[double]'
  foreach ($x in $aspects) { $list.Add([double]$x) }
  # Reflection gets the objects themselves, not their PowerShell wrappers.
  $inv = [object[]]@($list.psobject.BaseObject, [double]$w, [double]$h, [double]0, [double]0)
  $p = $layout.Invoke($null, $inv)
  return @{ places = @($p); width = $inv[3]; height = $inv[4] }
}
$r = Places @(1.7778, 1.7778, 1.7778) 1280 720
Check ($r.places.Count -eq 3 -and [Math]::Abs($r.width - 1280) -lt 1 -and [Math]::Abs($r.height - 720) -lt 1) ('three 16:9 in 1280x720: ' + $r.width + 'x' + $r.height)
$p = $r.places
Check ([Math]::Abs($p[0].Height - $p[2].Height) -lt 0.01 -and [Math]::Abs($p[0].Right - $p[1].Left) -lt 0.01 -and [Math]::Abs($p[2].Top - $p[0].Bottom) -lt 0.01) 'two above and one below, joined, the same height'
Check ([Math]::Abs($p[2].Left - 320) -lt 1) ('the row below in the middle: ' + $p[2].Left)
$r = Places @(1.7778, 1.7778, 1.7778, 1.7778) 1280 720
Check ($r.places.Count -eq 4 -and [Math]::Abs($r.places[3].Right - 1280) -lt 1 -and [Math]::Abs($r.places[3].Bottom - 720) -lt 1) 'four 16:9: a 2x2 grid that fills the image'
$r = Places @(1.7778, 1.7778) 1280 720
Check ($r.places.Count -eq 2 -and [Math]::Abs($r.height - 360) -lt 1) ('two 16:9: one row 1280x360 (' + $r.height + ')')
$r = Places @(1.3333, 1.7778, 1.7778) 1920 1080
$ok = $true
for ($i = 0; $i -lt $r.places.Count; $i++) { for ($j = $i + 1; $j -lt $r.places.Count; $j++) { $o = [System.Drawing.RectangleF]::Intersect($r.places[$i], $r.places[$j]); if ($o.Width -gt 0.01 -and $o.Height -gt 0.01) { $ok = $false } } }
Check $ok 'a 4:3 and two 16:9: nothing over another'
foreach ($q in $r.places) { if ($q.Right -gt $r.width + 0.5 -or $q.Bottom -gt $r.height + 0.5) { $ok = $false } }
Check $ok 'inside the joined image'

# Coloured images joined (their labels drawn on them) for a look.
$colors = @([System.Drawing.Color]::FromArgb(30, 90, 140), [System.Drawing.Color]::FromArgb(40, 120, 80), [System.Drawing.Color]::FromArgb(130, 70, 40), [System.Drawing.Color]::FromArgb(3, 14, 23))
$imgs = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'
for ($i = 0; $i -lt 4; $i++) {
  $b = New-Object System.Drawing.Bitmap 1280, 720
  $g = [System.Drawing.Graphics]::FromImage($b); $g.Clear($colors[$i])
  $f = New-Object System.Drawing.Font 'Segoe UI', 64
  $g.DrawString(('screen ' + ($i + 1)), $f, [System.Drawing.Brushes]::White, 60, 60); $g.DrawRectangle([System.Drawing.Pens]::White, 0, 0, 1279, 719); $g.Dispose()
  $imgs.Add($b)
}
$compose = $tiles.GetMethod('Compose')
$three = New-Object 'System.Collections.Generic.List[System.Drawing.Bitmap]'; $three.Add($imgs[0]); $three.Add($imgs[1]); $three.Add($imgs[2])
$j3 = $compose.Invoke($null, [object[]]@($three.psobject.BaseObject, [int]1280, [int]720, [System.Drawing.Color]::Black))
$j3.Save([System.IO.Path]::Combine($out, 'joined-3.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Check ($j3.Width -eq 1280 -and $j3.Height -eq 720) ('joined image of three: ' + $j3.Width + 'x' + $j3.Height)
$px = $j3.GetPixel(700, 200)
Check ($px.R -eq 40 -and $px.G -eq 120) ('the second screen at the top right: ' + $px)
$px = $j3.GetPixel(640, 540)
Check ($px.R -eq 130 -and $px.G -eq 70) ('the third screen in the middle below: ' + $px)
$px = $j3.GetPixel(100, 540)
Check ($px.R -eq 0 -and $px.G -eq 0 -and $px.B -eq 0) 'black only beside the row below'
$j4 = $compose.Invoke($null, [object[]]@($imgs.psobject.BaseObject, [int]1280, [int]720, [System.Drawing.Color]::Black))
$j4.Save([System.IO.Path]::Combine($out, 'joined-4.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Check ($j4.GetPixel(100, 540).R -eq 130 -and $j4.GetPixel(1200, 700).B -eq 23) 'four joined: the third below left, the panel below right'

# ---- The panel of the group on a fictional training start (50 fps, a timestamp = 1 ms): the signal at 10 s, the feet off, the
# entry, kicks, the head at 5, 10 and 15 m, the breakout.
$tRace = T 'SwimRace'; $tMark = T 'SwimMark'; $tType = T 'SwimMarkType'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'
$timing = [Activator]::CreateInstance($tTiming); $timing.AverageTimestampsPerFrame = 20; $timing.FrameIntervalMilliseconds = 20; $timing.HighSpeedFactor = 1
$race = [Activator]::CreateInstance($tRace)
$race.Kind = 'training'; $race.Style = 'fs'; $race.Pool = 25; $race.AthleteGender = 'Ανδρών'
foreach ($mk in @(@('Signal', 10000, $null), @('FeetOff', 10720, $null), @('Entry', 11060, $null), @('TechSplit', 11650, 5), @('Kick', 12100, $null), @('Kick', 12550, $null),
    @('Kick', 13000, $null), @('TechSplit', 13350, 10), @('Breakout', 14100, $null), @('TechSplit', 15780, 15))) {
  $x = [Activator]::CreateInstance($tMark); $x.Type = [Enum]::Parse($tType, $mk[0]); $x.Timestamp = [long]$mk[1]; $x.Wall = -1
  if ($null -ne $mk[2]) { $x.Distance = [int]$mk[2] }
  $race.Marks.Add($x)
}
$computed = $tCalc.GetMethod('Compute').Invoke($null, @($race, $timing, 0.5))
$phases = $tTech.GetMethod('Compute').Invoke($null, @($race, $computed, $timing))
Check ($phases.Count -eq 1) ('one start: ' + $phases.Count)
$ph = $phases[0]
Write-Output ('start: t5 ' + $ph.Value('t5') + ' t10 ' + $ph.Value('t10') + ' t15 ' + $ph.Value('t15') + ' v0_5 ' + $ph.Value('v0_5') + ' v5_10 ' + $ph.Value('v5_10') + ' v10_15 ' + $ph.Value('v10_15'))
Check ($null -ne $ph.Value('v0_5') -and $null -ne $ph.Value('v10_15')) 'speeds of the segments measured'

$mt = T 'SwimAnalysisManager'
$m = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mt)
[GC]::SuppressFinalize($m)
$mt.GetField('currentRace', $all).SetValue($m, $race.psobject.BaseObject)
$mt.GetField('currentTiming', $all).SetValue($m, $timing.psobject.BaseObject)
$mt.GetProperty('TechPhases', $all).GetSetMethod($true).Invoke($m, [object[]]@(,$phases.psobject.BaseObject))
$paint = $mt.GetMethod('PaintGroupDashboard', $all)
foreach ($at in @(9500, 10900, 13500, 16200, 30000)) {
  $b = New-Object System.Drawing.Bitmap 1280, 720
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    $paint.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new(1280, 720), [long]$at))
    $drawn = 0
    for ($y = 40; $y -lt 700; $y += 23) { for ($xx = 40; $xx -lt 1240; $xx += 31) { $c = $b.GetPixel($xx, $y); if ($c.R -gt 80 -or $c.G -gt 120) { $drawn++ } } }
    $b.Save([System.IO.Path]::Combine($out, ('panel-' + $at + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    Check ($drawn -gt 20) ('panel at ' + $at + ' ms drawn (' + $drawn + ' bright samples)')
  } catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    Check $false ('panel at ' + $at + ' ms: ' + $e.GetType().Name + ' ' + $e.Message + ' ' + $e.StackTrace)
  } finally { $g.Dispose(); $b.Dispose() }
}
# A wide panel (in the place of a screen of another shape) and a small one: fitted, nothing outside.
foreach ($sz in @(@(1920, 600), @(480, 270))) {
  $b = New-Object System.Drawing.Bitmap $sz[0], $sz[1]
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    $paint.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new([int]$sz[0], [int]$sz[1]), [long]13500))
    $b.Save([System.IO.Path]::Combine($out, ('panel-' + $sz[0] + 'x' + $sz[1] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    Check $true ('panel ' + $sz[0] + 'x' + $sz[1])
  } catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    Check $false ('panel ' + $sz[0] + 'x' + $sz[1] + ': ' + $e.Message)
  } finally { $g.Dispose(); $b.Dispose() }
}
Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
