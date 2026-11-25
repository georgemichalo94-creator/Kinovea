# sl_ci.ps1 (2.39.0): the CI check of the slides of a race in its package, on FICTIONAL races (sl_build.js, no real athlete): the
# slides that the Apps Script 2.26.0 sends are made pictures by the DLL (SwimPdfPrinter.Shoot) with the Edge of the runner, with a
# logo and a frame of the video put in (SwimSlides.WithImages): one picture per slide, 1920 x 1080 (the video) and 1080 x 1350 (the
# posts), the logo where it goes, the frame in the cover, in time; the slide fitted to a video of 16:9 and of 4:3 (the dark around
# it), its fades, its frames; and a fictional video with the slides at its end (SwimSlides.Write with the encoder and the ffmpeg of
# the build): 3 s each. The pictures for a look. Windows PowerShell 5.1 (.NET Framework, as Kinovea).
#   powershell -File sl_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls and ffmpeg.exe> -dir <folder of sl_build.js> -out <folder>
param([string]$sm, [string]$bin, [string]$dir, [string]$out)
$ErrorActionPreference = 'Stop'
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
New-Item -ItemType Directory -Force -Path $out | Out-Null; $out = (Resolve-Path $out).Path
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
Add-Type -AssemblyName System.Drawing
$a = [System.Reflection.Assembly]::LoadFrom($sm)
[System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll')) | Out-Null
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$tSlides = T 'SwimSlides'
$tPrinter = T 'SwimPdfPrinter'
$tEnc = T 'SwimVideoEncoder'

function PngOf($bmp) { $m = New-Object System.IO.MemoryStream; $bmp.Save($m, [System.Drawing.Imaging.ImageFormat]::Png); $b = $m.ToArray(); $m.Dispose(); return ,$b }
function JpegOf($bmp) { $m = New-Object System.IO.MemoryStream; $bmp.Save($m, [System.Drawing.Imaging.ImageFormat]::Jpeg); $b = $m.ToArray(); $m.Dispose(); return ,$b }
function BitmapOf([byte[]]$png) { $m = New-Object System.IO.MemoryStream (,$png); return New-Object System.Drawing.Bitmap $m }
# The brightest and the mean of a region of a picture (the mean of R, G, B).
function Region($bmp, $x0, $y0, $x1, $y1) {
  $max = 0; $sumR = 0; $sumB = 0; $n = 0
  for ($y = $y0; $y -lt $y1; $y += 4) { for ($x = $x0; $x -lt $x1; $x += 4) {
    $c = $bmp.GetPixel($x, $y); $v = ($c.R + $c.G + $c.B) / 3
    if ($v -gt $max) { $max = $v }; $sumR += $c.R; $sumB += $c.B; $n++ } }
  return @{ Max = $max; R = $sumR / [Math]::Max(1, $n); B = $sumB / [Math]::Max(1, $n) }
}

# ---- A logo (white on transparent, as SwimAnalysis\logo.png) and a frame of the video (orange, as nothing of the slides is).
$lb = New-Object System.Drawing.Bitmap 600, 240, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($lb); $g.Clear([System.Drawing.Color]::Transparent)
$g.FillEllipse([System.Drawing.Brushes]::White, 10, 20, 200, 200)
$g.DrawString('LOGO', (New-Object System.Drawing.Font 'Arial', 90, ([System.Drawing.FontStyle]::Bold)), [System.Drawing.Brushes]::White, 220, 40); $g.Dispose()
$logo = PngOf $lb; $lb.Dispose()
$cb = New-Object System.Drawing.Bitmap 1280, 720, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($cb); $g.Clear([System.Drawing.Color]::FromArgb(230, 120, 30)); $g.Dispose()
$cover = JpegOf $cb; $cb.Dispose()

$with = $tSlides.GetMethod('WithImages')
$shoot = $tPrinter.GetMethod('Shoot')
$browser = $tPrinter.GetProperty('BrowserPath').GetValue($null)
Check ($null -ne $browser) ('the browser of the pictures: ' + $browser)

# ---- The slides of the fictional races as pictures.
$files = @(Get-ChildItem $dir -Filter *.html | Sort-Object Name)
Check ($files.Count -gt 0) ('fictional slides: ' + $files.Count)
$times = @()
$firstVideo = $null
foreach ($f in $files) {
  $html = [System.IO.File]::ReadAllText($f.FullName, $utf8)
  $expect = (Get-Content ([System.IO.Path]::ChangeExtension($f.FullName, '.json')) -Raw) | ConvertFrom-Json
  $page = [string]$with.Invoke($null, [object[]]@([string]$html, [byte[]]$logo, [byte[]]$cover))
  Check ((-not $page.Contains('{{LOGO}}')) -and (-not $page.Contains('{{FRAME}}')) -and $page.Contains('data:image/png;base64,') -and $page.Contains('data:image/jpeg;base64,')) ($f.BaseName + ': the logo and the frame put in')
  $r = $shoot.Invoke($null, [object[]]@([string]$page, [int]120))
  $v = @($r.Shots | Where-Object { $_.Kind -eq 'v' }); $s = @($r.Shots | Where-Object { $_.Kind -eq 's' })
  $vSize = @($v | Where-Object { $_.Width -eq 1920 -and $_.Height -eq 1080 }).Count
  $sSize = @($s | Where-Object { $_.Width -eq 1080 -and $_.Height -eq 1350 }).Count
  if ($r.Ok) { $times += $r.Seconds }
  Check ($r.Ok -and $v.Count -eq $expect.video -and $s.Count -eq $expect.social -and $vSize -eq $v.Count -and $sSize -eq $s.Count -and $r.Seconds -lt 90) (
    '{0}: {1} slides of {2} (1920x1080: {3}), {4} posts of {5} (1080x1350: {6}), {7:0.0} s{8}' -f $f.BaseName, $v.Count, $expect.video, $vSize, $s.Count, $expect.social, $sSize, $r.Seconds, $(if ($r.Error) { ' · ' + $r.Error } else { '' }))
  $i = 0
  foreach ($shot in $r.Shots) { $i++; [System.IO.File]::WriteAllBytes([System.IO.Path]::Combine($out, ('{0}-{1:00}-{2}.png' -f $f.BaseName, $i, $shot.Kind)), $shot.Png) }
  if ($r.Ok -and $v.Count -gt 1 -and $s.Count -gt 0) {
    # The logo top right on the slides (white), the frame of the video in the cover (orange).
    $b = BitmapOf $v[1].Png; $lr = Region $b 1650 40 1830 140; $b.Dispose()
    Check ($lr.Max -gt 200) ($f.BaseName + ': the logo on the 2nd slide (brightest ' + [int]$lr.Max + ')')
    $b = BitmapOf $s[0].Png; $fr = Region $b 120 260 960 700; $b.Dispose()
    Check ($fr.R -gt $fr.B + 60) ($f.BaseName + ': the frame of the video in the cover (red {0:0}, blue {1:0})' -f $fr.R, $fr.B)
    if ($null -eq $firstVideo) { $firstVideo = [System.Collections.Generic.List[byte[]]]::new(); foreach ($x in @($v | Select-Object -First 3)) { $firstVideo.Add([byte[]]$x.Png) } }
  }
}
if ($times.Count -gt 0) { $sorted = @($times | Sort-Object); Write-Output ('     slide times: median {0:0.0} s, longest {1:0.0} s ({2} races)' -f $sorted[[int][Math]::Floor($sorted.Count / 2)], $sorted[-1], $sorted.Count) }

# Without a logo: its place stays dark (the slides hide a logo they do not have); without a frame the cover has its lanes.
if ($files.Count -gt 0) {
  $html = [System.IO.File]::ReadAllText($files[0].FullName, $utf8)
  $r = $shoot.Invoke($null, [object[]]@([string]$html, [int]120))
  $v = @($r.Shots | Where-Object { $_.Kind -eq 'v' })
  if ($r.Ok -and $v.Count -gt 1) { $b = BitmapOf $v[1].Png; $lr = Region $b 1650 40 1830 140; $b.Dispose() } else { $lr = @{ Max = 999 } }
  Check ($r.Ok -and $lr.Max -lt 120) ('without the logo: its place dark (brightest ' + [int]$lr.Max + ')')
}

# ---- A slide in a video: as large as it fits, the dark around it; its fades; its frames.
$fit = $tSlides.GetMethod('Fit')
$sb = New-Object System.Drawing.Bitmap 1920, 1080, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g = [System.Drawing.Graphics]::FromImage($sb)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 40, 40))), 0, 0, 960, 1080)
$g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 40, 200))), 960, 0, 960, 1080); $g.Dispose()
$slide = PngOf $sb; $sb.Dispose()
function Px([byte[]]$bgr, $w, $x, $y) { $i = ($y * $w + $x) * 3; return @([int]$bgr[$i + 2], [int]$bgr[$i + 1], [int]$bgr[$i]) }
$wide = [byte[]]$fit.Invoke($null, [object[]]@([byte[]]$slide, [System.Drawing.Size]::new(1280, 720)))
$p1 = Px $wide 1280 20 360; $p2 = Px $wide 1280 1260 360
Check ($wide.Length -eq 1280 * 720 * 3 -and $p1[0] -gt 150 -and $p1[2] -lt 90 -and $p2[2] -gt 150 -and $p2[0] -lt 90) ('a slide in a 16:9 video: the whole of it (left {0}, right {1})' -f ($p1 -join ','), ($p2 -join ','))
$tall = [byte[]]$fit.Invoke($null, [object[]]@([byte[]]$slide, [System.Drawing.Size]::new(960, 720)))
$top = Px $tall 960 480 20; $mid = Px $tall 960 200 360
Check ($tall.Length -eq 960 * 720 * 3 -and $top[0] -eq 10 -and $top[1] -eq 19 -and $top[2] -eq 34 -and $mid[0] -gt 150) ('a slide in a 4:3 video: the dark above it ({0}), the slide in the middle ({1})' -f ($top -join ','), ($mid -join ','))
$fade = $tSlides.GetMethod('Fade')
$into = (New-Object byte[] ($wide.Length)).psobject.BaseObject
$fade.Invoke($null, [object[]]@([byte[]]$wide, $into, [double]0)) | Out-Null
$d0 = Px $into 1280 20 360
$fade.Invoke($null, [object[]]@([byte[]]$wide, $into, [double]1)) | Out-Null
$d1 = Px $into 1280 20 360
$fade.Invoke($null, [object[]]@([byte[]]$wide, $into, [double]0.5)) | Out-Null
$dh = Px $into 1280 20 360
Check (($d0 -join ',') -eq '10,19,34' -and ($d1 -join ',') -eq ($p1 -join ',') -and $dh[0] -gt 60 -and $dh[0] -lt $p1[0]) ('the fades: 0 the dark ({0}), 1 the slide ({1}), half between ({2})' -f ($d0 -join ','), ($d1 -join ','), ($dh -join ','))
$framesOf = $tSlides.GetMethod('FramesOf'); $op = $tSlides.GetMethod('Opacity')
$n30 = [int]$framesOf.Invoke($null, @([double]30)); $n50 = [int]$framesOf.Invoke($null, @([double]50)); $n2997 = [int]$framesOf.Invoke($null, @([double]29.97))
$o0 = [double]$op.Invoke($null, @([int]0, [int]90, [double]30)); $oMid = [double]$op.Invoke($null, @([int]45, [int]90, [double]30)); $oEnd = [double]$op.Invoke($null, @([int]89, [int]90, [double]30))
Check ($n30 -eq 90 -and $n50 -eq 150 -and $n2997 -eq 90 -and $o0 -gt 0 -and $o0 -lt 0.2 -and $oMid -eq 1 -and $oEnd -lt 0.2) ('3 s a slide: {0} frames at 30, {1} at 50, {2} at 29.97; seen {3:0.00} → {4} → {5:0.00}' -f $n30, $n50, $n2997, $o0, $oMid, $oEnd)

# ---- A fictional video (2 s, 640 x 360 at 25 fps) with the slides of a race at its end, by the encoder and the ffmpeg of the build.
$ff = [System.IO.Path]::Combine($bin, 'ffmpeg.exe')
Check ([System.IO.File]::Exists($ff)) 'the ffmpeg.exe of the build'
if ($null -ne $firstVideo -and [System.IO.File]::Exists($ff)) {
  $video = [System.IO.Path]::Combine($out, 'video-with-slides.mp4')
  $enc = ([Activator]::CreateInstance($tEnc)).psobject.BaseObject
  $startArgs = [object[]]@([string]$ff, [string]$video, [int]640, [int]360, [double]25, $null, [double]0, [double]0, $null)
  Check ($tEnc.GetMethod('Start').Invoke($enc, $startArgs)) ('ffmpeg for the fictional video: ' + $startArgs[8])
  $buf = New-Object byte[] (640 * 360 * 3)
  for ($i = 0; $i -lt $buf.Length; $i += 3) { $buf[$i] = 120; $buf[$i + 1] = 90; $buf[$i + 2] = 20 }
  for ($i = 0; $i -lt 50; $i++) { [void]$enc.Write($buf) }
  $write = $tSlides.GetMethod('Write')
  $wArgs = [object[]]@($enc, $firstVideo.psobject.BaseObject, [System.Drawing.Size]::new(640, 360), [double]25, $null, $null, $null, $null)
  $frames = [int]$write.Invoke($null, $wArgs)
  $finishArgs = [object[]]@([int]60000, $null)
  $finished = $tEnc.GetMethod('Finish').Invoke($enc, $finishArgs)
  $enc.Dispose()
  Check ($firstVideo.Count -eq 3 -and $frames -eq 3 * 75 -and $wArgs[6] -eq $true -and $wArgs[7] -eq $false -and $finished) ('the slides at the end of the video: {0} frames (taken {1}, stopped {2}) {3}' -f $frames, $wArgs[6], $wArgs[7], $finishArgs[1])
  $probe = $tEnc.GetMethod('Probe')
  $pArgs = [object[]]@([string]$ff, [string]$video, $null, $null, $null)
  $read = $probe.Invoke($null, $pArgs)
  Check ($read -and [Math]::Abs([double]$pArgs[2] - 11.0) -lt 0.3) ('the video with its slides: {0:0.00} s (2 s and 3 x 3 s)' -f [double]$pArgs[2])
}

Write-Output ('slides: ' + $(if ($fails -eq 0) { 'all checks ok' } else { "$fails checks failed" }))
if ($fails -gt 0) { exit 1 }
