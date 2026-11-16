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
      # (2.24.0) A 4:3 panel uses its height: the chart of the speed goes down to its bottom (its axis and its labels in the lowest
      # part; the 16:9 drawing left it empty).
      $low = 0
      for ($y = 600; $y -lt 712; $y += 1) { for ($xx = 500; $xx -lt 940; $xx += 11) { $c = $b.GetPixel($xx, $y); if ($c.G -gt 60 -and $c.B -gt 80) { $low++ } } }
      Check ($low -gt 20) ('panel 960x720 uses its height: its chart down to the bottom (' + $low + ' samples of its axis and labels)')
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
# ---- (2.24.0) The panel of a start and of a turn: its values big, the speed as a line; the picture of its end (the report, the end of
# the video of the group) and the video that closes with it. A FICTIONAL training turn, two attempts (the second the faster).
try {
  $paintFinal = $mt.GetMethod('PaintFinalPanel', $all)
  $b = New-Object System.Drawing.Bitmap 1280, 720
  $g = [System.Drawing.Graphics]::FromImage($b)
  $paintFinal.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new(1280, 720), $ph.psobject.BaseObject))
  $g.Dispose()
  $b.Save([System.IO.Path]::Combine($out, 'panel-final-start.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $lit = 0
  for ($y = 230; $y -lt 700; $y += 11) { for ($xx = 40; $xx -lt 620; $xx += 13) { $c = $b.GetPixel($xx, $y); if ($c.R -gt 160 -and $c.G -gt 160 -and $c.B -gt 160) { $lit++ } } }
  Check ($lit -gt 40) ('the end of the start: its values drawn big (' + $lit + ' white samples)')
  $b.Dispose()
  # (2.24.1) The speed of the start stops at the 15: the chart ends there and the line levels off (it does not go on falling).
  $dS = $mt.GetMethod('BuildPanel', $all).Invoke($m, [object[]]@($ph.psobject.BaseObject))
  $fS = $dS.GetType()
  $sToS = $fS.GetField('SpeedTo', $all).GetValue($dS); $cToS = $fS.GetField('ChartTo', $all).GetValue($dS); $spS = $fS.GetField('Speed', $all).GetValue($dS)
  Check ([Math]::Abs($sToS - 5.78) -lt 0.05 -and [Math]::Abs($cToS - $sToS) -lt 1e-6) ('the chart of the start ends at the 15: ' + [Math]::Round($cToS, 3) + ' (15 m at ' + [Math]::Round($sToS, 3) + ' s)')
  $v1S = $spS.Invoke($sToS - 0.1); $v2S = $spS.Invoke($sToS); $v3S = $spS.Invoke($sToS + 1.0)
  Check ([Math]::Abs($v2S - $v1S) -lt 0.003 -and [Math]::Abs($v3S - $v2S) -lt 1e-9) ('the speed of the start levels off at the 15 and stays: ' + [Math]::Round($v1S, 4) + ' / ' + [Math]::Round($v2S, 4) + ' / ' + [Math]::Round($v3S, 4))

  $turnRace = [Activator]::CreateInstance($tRace)
  $turnRace.Kind = 'training'; $turnRace.Style = 'fs'; $turnRace.Pool = 25; $turnRace.AthleteGender = 'Ανδρών'
  $contacts = @(40000, 80000)
  foreach ($c0 in $contacts) {
    $last15 = 7850; if ($c0 -eq 80000) { $last15 = 7650 }
    foreach ($mk in @(@('TechSplit', -3120, -5), @('Stroke', -1280, $null), @('RotationStart', -910, $null), @('WallContact', 0, $null), @('FeetOff', 240, $null),
        @('Kick', 620, $null), @('Kick', 1100, $null), @('Kick', 1580, $null), @('Kick', 2060, $null), @('Kick', 2550, $null), @('Kick', 3030, $null),
        @('TechSplit', 2180, 5), @('Breakout', 3950, $null), @('Stroke', 4300, $null), @('TechSplit', 4950, 10), @('TechSplit', $last15, 15))) {
      $x = [Activator]::CreateInstance($tMark); $x.Type = [Enum]::Parse($tType, $mk[0]); $x.Timestamp = [long]($c0 + $mk[1]); $x.Wall = -1
      if ($null -ne $mk[2]) { $x.Distance = [int]$mk[2] }
      $turnRace.Marks.Add($x)
      if ($mk[0] -eq 'WallContact') { $turnRace.GetPhaseValues($x.Id, $true).BreakoutDist = 8.4 }
    }
  }
  $tComputedT = $tCalc.GetMethod('Compute').Invoke($null, @($turnRace, $timing, 0.5))
  $turnPhases = $tTech.GetMethod('Compute').Invoke($null, @($turnRace, $tComputedT, $timing))
  Check ($turnPhases.Count -eq 2) ('two turns: ' + $turnPhases.Count)
  $t1 = $turnPhases[0]
  Write-Output ('turn: in5 ' + $t1.Value('in5') + ' turnTime ' + $t1.Value('turnTime') + ' out5 ' + $t1.Value('out5') + ' v0_5 ' + $t1.Value('v0_5') + ' kicks ' + $t1.Value('kicks') + ' breakoutDist ' + $t1.Value('breakoutDist'))
  $mt.GetField('currentRace', $all).SetValue($m, $turnRace.psobject.BaseObject)
  $mt.GetProperty('TechPhases', $all).GetSetMethod($true).Invoke($m, [object[]]@(,$turnPhases.psobject.BaseObject))
  # The data of the panel: all the values, the speed line through the measured points, the mean of each segment as measured.
  $build = $mt.GetMethod('BuildPanel', $all)
  $d = $build.Invoke($m, [object[]]@($t1.psobject.BaseObject))
  # (The data of the panel is a private class: its fields by reflection.)
  function F($o, $name) { return $o.GetType().GetField($name, $all).GetValue($o) }
  $groups = F $d 'Groups'; $segments = F $d 'Segments'; $speed = F $d 'Speed'
  $nValues = 0; foreach ($gr in $groups) { $nValues += (F $gr 'Values').Count }
  Check ($groups.Count -eq 4 -and $nValues -eq 14) ('the turn in four groups, 14 values: ' + $groups.Count + ' / ' + $nValues)
  Check ($null -ne $speed -and $segments.Count -eq 4) ('a line of the speed and four segments (' + $segments.Count + ')')
  # The line goes through the distances: its mean over each segment is the one measured (within 1%).
  $okMeans = $true; $why = ''
  foreach ($sg in $segments) {
    $from = F $sg 'From'; $to = F $sg 'To'; $sv = F $sg 'Speed'
    $n = 400; $sum = 0.0
    for ($i = 0; $i -lt $n; $i++) { $t = $from + ($to - $from) * ($i + 0.5) / $n; $sum += $speed.Invoke($t) }
    $mean = $sum / $n
    if ([Math]::Abs($mean - $sv) / $sv -gt 0.01) { $okMeans = $false }
    $why += (' ' + [Math]::Round($mean, 3) + '/' + [Math]::Round($sv, 3))
  }
  Check $okMeans ('the line keeps the mean of each segment:' + $why)
  $neg = $false
  $sFrom = F $d 'SpeedFrom'; $sTo = F $d 'SpeedTo'; $sTop = F $d 'SpeedTop'
  for ($i = 0; $i -le 400; $i++) { $t = $sFrom + ($sTo - $sFrom) * $i / 400; $v = $speed.Invoke($t); if ($v -lt -0.01 -or $v -gt 4.5) { $neg = $true } }
  Check (-not $neg -and $sTop -gt 2 -and $sTop -lt 4) ('the speed plausible all along (top ' + [Math]::Round($sTop, 2) + ' m/s)')
  # (2.24.1) The speed stops at the +15: the chart ends there (nothing after it) and the line levels off (it does not go on falling).
  $cTo = F $d 'ChartTo'
  Check ([Math]::Abs($sTo - 7.85) -lt 0.05 -and [Math]::Abs($cTo - $sTo) -lt 1e-6) ('the chart of the turn ends at the +15: ' + [Math]::Round($cTo, 3) + ' (+15 at ' + [Math]::Round($sTo, 3) + ' s)')
  $v1 = $speed.Invoke($sTo - 0.1); $v2 = $speed.Invoke($sTo); $v3 = $speed.Invoke($sTo + 1.0)
  Check ([Math]::Abs($v2 - $v1) -lt 0.003 -and [Math]::Abs($v3 - $v2) -lt 1e-9) ('the speed of the turn levels off at the +15 and stays: ' + [Math]::Round($v1, 4) + ' / ' + [Math]::Round($v2, 4) + ' / ' + [Math]::Round($v3, 4))
  foreach ($rel in @(-2000, 120, 1600, 5000, 8300)) {
    $b = New-Object System.Drawing.Bitmap 1280, 720
    $g = [System.Drawing.Graphics]::FromImage($b)
    $paint.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new(1280, 720), [long](40000 + $rel)))
    $g.Dispose()
    $b.Save([System.IO.Path]::Combine($out, ('panel-turn-' + $rel + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
  }
  foreach ($sz in @(@(960, 540), @(960, 720), @(640, 900))) {
    $b = New-Object System.Drawing.Bitmap $sz[0], $sz[1]
    $g = [System.Drawing.Graphics]::FromImage($b)
    $paint.Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new([int]$sz[0], [int]$sz[1]), [long]45000))
    $g.Dispose()
    $b.Save([System.IO.Path]::Combine($out, ('panel-turn-' + $sz[0] + 'x' + $sz[1] + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
    $b.Dispose()
    Check $true ('the turn in a place of ' + $sz[0] + 'x' + $sz[1])
  }
  # The report: the panel of the best turn (the second), at the size of the graphics of the report.
  $best = $mt.GetMethod('BestPhase', $all).Invoke($m, [object[]]@([Enum]::Parse((T 'SwimPhaseKind'), 'Turn')))
  Check ($best.Attempt -eq 2) ('the best turn for the report: ' + $best.Attempt)
  $pics = $mt.GetMethod('RenderPanelGraphics', $all).Invoke($m, $null)
  Check ($pics.ContainsKey('panel-turn') -and -not $pics.ContainsKey('panel-start')) ('the pictures of the report: ' + (@($pics.Keys) -join ', '))
  [System.IO.File]::WriteAllBytes([System.IO.Path]::Combine($out, 'report-panel-turn.png'), $pics['panel-turn'])
  $rp = [System.Drawing.Image]::FromFile([System.IO.Path]::Combine($out, 'report-panel-turn.png'))
  Check ($rp.Width -eq 1280 -and $rp.Height -eq 720) ('the picture of the report: ' + $rp.Width + 'x' + $rp.Height)
  $rp.Dispose()
  # The closing phase of a part of the video: the last turn in it.
  $closing = $mt.GetMethod('ClosingPhase', $all)
  Check ($closing.Invoke($m, [object[]]@([long]30000, [long]100000)).Attempt -eq 2 -and $closing.Invoke($m, [object[]]@([long]30000, [long]50000)).Attempt -eq 1) 'the panel that closes the video: the last turn of the part'

  # The end of the video of a group: the panel grows from its place (the fourth screen) to the whole picture, stays and fades out.
  $ff = Join-Path $bin 'ffmpeg.exe'
  $encT = T 'SwimVideoEncoder'
  $encoder = [Activator]::CreateInstance($encT)
  $video = [System.IO.Path]::Combine($out, 'closing.mp4')
  $startArgs = [object[]]@([string]$ff, [string]$video, [int]1280, [int]720, [double]25, $null, [double]0, [double]0, $null)
  $encoder = $encoder.psobject.BaseObject
  $started = $encT.GetMethod('Start').Invoke($encoder, $startArgs)
  Check $started ('ffmpeg for the closing: ' + $startArgs[8])
  $joined = New-Object System.Drawing.Bitmap 1280, 720, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $gj = [System.Drawing.Graphics]::FromImage($joined); $gj.DrawImage($j4, 0, 0, 1280, 720); $gj.Dispose()
  $buffer = New-Object byte[] (1280 * 720 * 3)
  $jobT = $mt.GetNestedType('GroupVideoJob', $all)
  $job = [Activator]::CreateInstance($jobT, $true)
  $write = $mt.GetMethod('WriteClosingPanel', $all)
  $place = [Nullable[System.Drawing.Rectangle]]([System.Drawing.Rectangle]::new(640, 360, 640, 360))
  $wargs = [object[]]@($encoder, $joined.psobject.BaseObject, $buffer.psobject.BaseObject, $place, $best.psobject.BaseObject, [double]25, $job.psobject.BaseObject, $null, $false)
  $frames = $write.Invoke($m, $wargs)
  $err = ''
  $finished = $encT.GetMethod('Finish').Invoke($encoder, [object[]]@([int]60000, [string]$err))
  $encoder.Dispose()
  Check ($wargs[8] -and $frames -eq 170) ('the closing: 1 s + 5 s + 0.8 s at 25 fps = ' + $frames + ' frames')
  Check ((Test-Path $video) -and (Get-Item $video).Length -gt 10000) ('the closing video written (' + (Get-Item $video).Length + ' bytes)')
  foreach ($at in @('0.48', '0.96', '3.00', '6.60')) {
    $png = [System.IO.Path]::Combine($out, ('closing-' + $at + '.png'))
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    & $ff -nostdin -v error -y -ss $at -i $video -frames:v 1 -update 1 $png 2>&1 | Out-Null
    $ErrorActionPreference = $old
  }
  $mid = [System.Drawing.Bitmap]::FromFile([System.IO.Path]::Combine($out, 'closing-3.00.png'))
  $c = $mid.GetPixel(60, 60)
  Check ($c.R -lt 60 -and $c.G -lt 80) ('the panel over the whole picture while it stays (' + $c + ')')
  $mid.Dispose()
} catch {
  $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
  Check $false ('the panel of 2.24.0: ' + $e.GetType().Name + ' ' + $e.Message + ' ' + $e.StackTrace)
}
# ---- (2.24.1) A FICTIONAL turn whose speed falls at the end (the breakout late, near the +10; the means 2.38, 2.04, 1.72 m/s): the
# line levels off at the +15 (a three-point end brought it to about 1.37) and the chart ends there. Its panel after the +15 and its
# picture of the end.
try {
  $fall = [Activator]::CreateInstance($tRace)
  $fall.Kind = 'training'; $fall.Style = 'fs'; $fall.Pool = 25; $fall.AthleteGender = 'Ανδρών'
  $f0 = 200000
  foreach ($mk in @(@('TechSplit', -3150, -5), @('Stroke', -1300, $null), @('RotationStart', -950, $null), @('WallContact', 0, $null), @('FeetOff', 230, $null),
      @('Kick', 600, $null), @('Kick', 1080, $null), @('Kick', 1560, $null), @('Kick', 2040, $null), @('Kick', 2520, $null), @('Kick', 3000, $null),
      @('TechSplit', 2100, 5), @('Breakout', 3900, $null), @('Stroke', 4250, $null), @('TechSplit', 4550, 10), @('TechSplit', 7450, 15))) {
    $x = [Activator]::CreateInstance($tMark); $x.Type = [Enum]::Parse($tType, $mk[0]); $x.Timestamp = [long]($f0 + $mk[1]); $x.Wall = -1
    if ($null -ne $mk[2]) { $x.Distance = [int]$mk[2] }
    $fall.Marks.Add($x)
    if ($mk[0] -eq 'WallContact') { $fall.GetPhaseValues($x.Id, $true).BreakoutDist = 8.6 }
  }
  $fComputed = $tCalc.GetMethod('Compute').Invoke($null, @($fall, $timing, 0.5))
  $fPhases = $tTech.GetMethod('Compute').Invoke($null, @($fall, $fComputed, $timing))
  Check ($fPhases.Count -eq 1) ('the falling turn: ' + $fPhases.Count)
  $mt.GetField('currentRace', $all).SetValue($m, $fall.psobject.BaseObject)
  $mt.GetProperty('TechPhases', $all).GetSetMethod($true).Invoke($m, [object[]]@(,$fPhases.psobject.BaseObject))
  $fd = $mt.GetMethod('BuildPanel', $all).Invoke($m, [object[]]@($fPhases[0].psobject.BaseObject))
  $ftype = $fd.GetType()
  $fSpeed = $ftype.GetField('Speed', $all).GetValue($fd); $fTo = $ftype.GetField('SpeedTo', $all).GetValue($fd); $fChart = $ftype.GetField('ChartTo', $all).GetValue($fd)
  $fv10 = $fSpeed.Invoke(4.55); $fvLate = $fSpeed.Invoke($fTo - 0.3); $fv15 = $fSpeed.Invoke($fTo)
  Write-Output ('falling turn: v(+10) ' + [Math]::Round($fv10, 3) + ', v(+15 - 0.3 s) ' + [Math]::Round($fvLate, 3) + ', v(+15) ' + [Math]::Round($fv15, 3) + ' (mean 10-15: ' + [Math]::Round(5 / 2.9, 3) + ')')
  Check ($fv15 -gt 1.55 -and $fv15 -lt 1.724 -and [Math]::Abs($fv15 - $fvLate) -lt 0.02 -and [Math]::Abs($fChart - $fTo) -lt 1e-6 -and [Math]::Abs($fTo - 7.45) -lt 0.05) ('the falling turn levels off at the +15: ' + [Math]::Round($fv15, 3) + ' m/s, the chart ends at ' + [Math]::Round($fChart, 3) + ' s')
  $b = New-Object System.Drawing.Bitmap 1280, 720
  $g = [System.Drawing.Graphics]::FromImage($b)
  $mt.GetMethod('PaintGroupDashboard', $all).Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new(1280, 720), [long]($f0 + 8070)))
  $g.Dispose()
  $b.Save([System.IO.Path]::Combine($out, 'panel-turn-falling.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $b.Dispose()
  $b = New-Object System.Drawing.Bitmap 1280, 720
  $g = [System.Drawing.Graphics]::FromImage($b)
  $mt.GetMethod('PaintFinalPanel', $all).Invoke($m, [object[]]@($g.psobject.BaseObject, [System.Drawing.Size]::new(1280, 720), $fPhases[0].psobject.BaseObject))
  $g.Dispose()
  $b.Save([System.IO.Path]::Combine($out, 'panel-turn-falling-final.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $b.Dispose()
  Check $true 'the falling turn drawn after the +15 and at its end'
} catch {
  $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
  Check $false ('the falling turn of 2.24.1: ' + $e.GetType().Name + ' ' + $e.Message + ' ' + $e.StackTrace)
}
Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
