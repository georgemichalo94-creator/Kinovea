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
    # Before the first times only the title and the clock; then the values and the chart.
    $least = 20; if ($at -lt 11000) { $least = 5 }
    Check ($drawn -gt $least) ('panel at ' + $at + ' ms drawn (' + $drawn + ' bright samples)')
  } catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    Check $false ('panel at ' + $at + ' ms: ' + $e.GetType().Name + ' ' + $e.Message + ' ' + $e.StackTrace)
  } finally { $g.Dispose(); $b.Dispose() }
}
# A wide panel (in the place of a screen of another shape) and a small one: fitted, nothing outside. (2.16.0) In the shape of a 4:3
# video and of a narrow cell: the panel takes the height of its place (its values and chart below each other), not a 16:9 strip.
foreach ($sz in @(@(1920, 600), @(480, 270), @(960, 720), @(420, 560))) {
  $b = New-Object System.Drawing.Bitmap $sz[0], $sz[1]
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    $paint.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new([int]$sz[0], [int]$sz[1]), [long]13500))
    $b.Save([System.IO.Path]::Combine($out, ('panel-' + $sz[0] + 'x' + $sz[1] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz[0] -eq 960) {
      # The lowest fifth of a 4:3 panel has the chart (the 16:9 drawing left it empty).
      $low = 0
      for ($y = 560; $y -lt 690; $y += 9) { for ($xx = 40; $xx -lt 920; $xx += 17) { $c = $b.GetPixel($xx, $y); if ($c.R -gt 80 -or $c.G -gt 120) { $low++ } } }
      Check ($low -gt 10) ('panel 960x720 uses its height (' + $low + ' bright samples in its lowest part)')
    }
    Check $true ('panel ' + $sz[0] + 'x' + $sz[1])
  } catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    Check $false ('panel ' + $sz[0] + 'x' + $sz[1] + ': ' + $e.Message)
  } finally { $g.Dispose(); $b.Dispose() }
}
# ---- (2.16.1) The wheel on a screen of a group: the screens one frame forward or back together (SwimFrameStep.Plan, pure).
$plan = (T 'SwimFrameStep').GetMethod('Plan')
function Step($now, $frame, $last, $offset, $scale, $dir) {
  $inv = [object[]]@([long[]]$now, [long[]]$frame, [long[]]$last, [long[]]$offset, [double[]]$scale, [int]$dir, [long]0)
  $aims = $plan.Invoke($null, $inv)
  return @{ aims = $aims; moment = $inv[6] }
}
function Same($a, $b) { if ($null -eq $a -or $a.Count -ne $b.Count) { return $false }; for ($i = 0; $i -lt $b.Count; $i++) { if ($a[$i] -ne $b[$i]) { return $false } }; return $true }
$r = Step @(1000000, 1000000) @(20000, 20000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(1010000, 1010000)) -and $r.moment -eq 1020000) ('two 50 fps forward: ' + ($r.aims -join ',') + ' at ' + $r.moment)
$r = Step @(1000000, 1000000) @(20000, 20000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) -1
Check ((Same $r.aims @(970000, 970000)) -and $r.moment -eq 980000) ('two 50 fps back: ' + ($r.aims -join ',') + ' at ' + $r.moment)
$r = Step @(1000000, 1000000) @(20000, 40000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(1010000, -1)) -and $r.moment -eq 1020000) ('50 and 25 fps forward: the 25 fps keeps its frame (' + ($r.aims -join ',') + ')')
$r = Step @(1020000, 1000000) @(20000, 40000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(1030000, 1020000)) -and $r.moment -eq 1040000) ('50 and 25 fps forward again: both to 1.04 s (' + ($r.aims -join ',') + ')')
$r = Step @(1020000, 1000000) @(20000, 40000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) -1
Check ((Same $r.aims @(990000, -1)) -and $r.moment -eq 1000000) ('50 and 25 fps back: to 1.00 s (' + ($r.aims -join ',') + ')')
$r = Step @(1000000, 1200000) @(20000, 20000) @(10000000, 10000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(1210000, 1210000)) -and $r.moment -eq 1220000) ('screens apart: the one behind comes to the other (' + ($r.aims -join ',') + ')')
$r = Step @(1000000, 500000) @(20000, 20000) @(10000000, 10000000) @(0, 500000) @(1.0, 1.0) 1
Check ((Same $r.aims @(1010000, 510000)) -and $r.moment -eq 1020000) ('synchronised with an offset (' + ($r.aims -join ',') + ')')
$r = Step @(2000000, 2000000) @(20000, 20000) @(2000000, 5000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(-1, 2010000)) -and $r.moment -eq 2020000) ('one video at its end stays, the other goes on (' + ($r.aims -join ',') + ')')
$r = Step @(2000000, 5000000) @(20000, 20000) @(2000000, 5000000) @(0, 0) @(1.0, 1.0) 1
Check ($null -eq $r.aims) 'all at their end: nothing moves'
$r = Step @(0, 0) @(20000, 20000) @(2000000, 5000000) @(0, 0) @(1.0, 1.0) -1
Check ($null -eq $r.aims) 'all at their start: nothing moves back'
$r = Step @(19999, 20000) @(20000, 20000) @(2000000, 2000000) @(0, 0) @(1.0, 1.0) 1
Check ((Same $r.aims @(29999, 30000)) -and [Math]::Abs($r.moment - 40000) -le 1) ('a microsecond apart is the same frame (' + ($r.aims -join ',') + ' at ' + $r.moment + ')')

# ---- (2.16.1) The playback bar of the full screen: its parts in their places, drawn for a look.
$bar = T 'SwimPlaybackBar'
$places = $bar.GetMethod('Places')
foreach ($case in @(@(1920, 1.0), @(1280, 1.0), @(800, 1.0), @(600, 1.0), @(1920, 1.5), @(1280, 1.5))) {
  $s = [single]$case[1]; $h = [int][Math]::Round(46 * $s)
  $p = $places.Invoke($null, [object[]]@([System.Drawing.Size]::new([int]$case[0], $h), $s))
  $ok = $true; $why = ''
  for ($i = 0; $i -lt $p.Count; $i++) {
    if ($p[$i].IsEmpty) { continue }
    if ($p[$i].Left -lt -0.01 -or $p[$i].Right -gt $case[0] + 0.01 -or $p[$i].Top -lt -0.01 -or $p[$i].Bottom -gt $h + 0.01) { $ok = $false; $why += ' out:' + $i }
    for ($j = $i + 1; $j -lt $p.Count; $j++) {
      if ($p[$j].IsEmpty) { continue }
      if ($p[$i].Right -gt $p[$j].Left + 0.01 -and $p[$j].Right -gt $p[$i].Left + 0.01) { $ok = $false; $why += ' ' + $i + '/' + $j }
    }
  }
  $line = $p[6].Width
  $enough = ($case[0] -lt 800) -or ($line -ge 120 * $s - 0.01)
  Check ($ok -and $enough -and -not $p[8].IsEmpty) ('bar ' + $case[0] + ' px at ' + $case[1] + 'x: the parts apart, the timeline ' + [int]$line + ' px' + $why)
}
$speedText = $bar.GetMethod('SpeedText')
Check ($speedText.Invoke($null, @([double]50)) -eq '50%' -and $speedText.Invoke($null, @([double]100)) -eq '1x' -and $speedText.Invoke($null, @([double]200)) -eq '2x') 'the speed written as the speed of a screen (50%, 1x, 2x)'
$draw = $bar.GetMethod('Draw')
foreach ($case in @(@(1920, 1.0, $true, 37.5, 2, 'playing'), @(1280, 1.0, $false, 100, 6, 'paused'), @(800, 1.0, $false, 12.5, 8, 'slow'), @(1920, 1.5, $true, 50, -1, 'hidpi'))) {
  $s = [single]$case[1]; $h = [int][Math]::Round(46 * $s)
  $b = New-Object System.Drawing.Bitmap ([int]$case[0]), $h
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    $draw.Invoke($null, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new([int]$case[0], $h), $s, [bool]$case[2], [long]62000000, [long]23400000, [long]10000000, '00:00:13.40', [double]$case[3], [double]200, [int]$case[4]))
    $b.Save([System.IO.Path]::Combine($out, ('bar-' + $case[5] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $lit = 0
    for ($xx = 2; $xx -lt $case[0]; $xx += 3) { $c = $b.GetPixel($xx, [int]($h / 2)); if ($c.R -gt 150 -and $c.G -gt 150) { $lit++ } }
    Check ($lit -gt 20) ('bar ' + $case[5] + ' drawn (' + $lit + ' bright samples across its middle)')
  } catch {
    $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
    Check $false ('bar ' + $case[5] + ': ' + $e.Message)
  } finally { $g.Dispose(); $b.Dispose() }
}
# ---- (2.16.4) The package with a group of screens: the bitrate of its video, the sound of the analysis, the pictures of its preview, the message.
try {
  $enc = T 'SwimVideoEncoder'
  $a7 = $enc.GetMethod('Arguments', [type[]]@([int], [int], [double], [string], [double], [double], [string]))
  $argsOne = $a7.Invoke($null, [object[]]@([int]1280, [int]720, [double]50, $null, [double]0, [double]0, 'x.mp4'))
  Check ($argsOne -like '*-maxrate 2500k -bufsize 5000k*' -and $argsOne -notlike '*-map 1:a:0*') 'the video of one screen: 2500 kbit/s, as before'
  $a8 = $enc.GetMethod('Arguments', [type[]]@([int], [int], [double], [string], [double], [double], [string], [int]))
  $argsGroup = $a8.Invoke($null, [object[]]@([int]1920, [int]1080, [double]50, 'a.mp4', [double]1.5, [double]30, 'x.mp4', [int]5000))
  Check ($argsGroup -like '*-maxrate 5000k -bufsize 10000k*' -and $argsGroup -like '*-ss 1.500 -t 30.000 -i "a.mp4"*' -and $argsGroup -like '*-map 1:a:0*') 'the video of a group: 5000 kbit/s and the sound of the analysis'
  $rate = $mt.GetMethod('GroupVideoRate', $all)
  $r1 = $rate.Invoke($null, [object[]]@([int]1920, [int]1080)); $r2 = $rate.Invoke($null, [object[]]@([int]1280, [int]720)); $r3 = $rate.Invoke($null, [object[]]@([int]1920, [int]540))
  Check ($r1 -eq 5000 -and $r2 -eq 2500 -and $r3 -gt 2500 -and $r3 -lt 5000) ('the bitrate of a group video by its size: ' + $r1 + ' / ' + $r2 + ' / ' + $r3)
  $thumbOf = $mt.GetMethod('ThumbOf', $all)
  $wide = New-Object System.Drawing.Bitmap 1920, 540
  $tb = $thumbOf.Invoke($null, [object[]]@($wide.psobject.BaseObject))
  Check ($tb.Width -eq 480 -and $tb.Height -eq 135) ('the picture of the preview of a joined video: ' + $tb.Width + 'x' + $tb.Height)
  $tb.Dispose(); $wide.Dispose()
  $pk = T 'SwimPackage'; $tComputed = T 'SwimComputed'
  $body3 = $pk.GetMethod('BodyFor', [type[]]@($tRace, $tComputed, [bool], [string], [int])).Invoke($null, [object[]]@($race.psobject.BaseObject, $null, $false, '', [int]3))
  Check ($body3 -like '*όλες οι οθόνες μαζί (MP4)*') 'the message of a group: the video of all the screens together'
  $body1 = $pk.GetMethod('BodyFor', [type[]]@($tRace, $tComputed, [bool], [string])).Invoke($null, [object[]]@($race.psobject.BaseObject, $null, $false, ''))
  Check ($body1 -like '*Το βίντεο με τα γραφικά της ανάλυσης (MP4)*' -and $body1 -notlike '*όλες οι οθόνες*') 'the message of one screen, as before'
} catch {
  $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
  Check $false ('the package of a group: ' + $e.GetType().Name + ' ' + $e.Message)
}
Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
