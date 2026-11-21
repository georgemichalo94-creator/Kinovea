# cw_ci.ps1: the CI check of the wall of cameras (2.27.0) on FICTIONAL cameras: the places of the images and of the live panel for
# two to five cameras (whole images, joined, the rows that make them biggest, the panel in the free place of the last row, else below
# or beside), one camera alone on the wall, images of another shape, the time of a recording, the buttons over an image, the English
# of the new texts; pictures of the wall (ready, recording, empty screens, alone, English) go to -out, an artifact of the run.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File cw_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
if ($out) { New-Item -ItemType Directory -Force -Path $out | Out-Null; $out = (Resolve-Path $out).Path }
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

$tWall = T 'SwimCaptureWall'; $tCam = T 'SwimWallCamera'; $tPanel = T 'SwimLivePanel'; $tBar = T 'SwimCaptureBar'; $tStatus = T 'RecordingStatus'
$tTheme = T 'SwimTheme'; $tLang = T 'SwimLang'
$layoutOf = $tWall.GetMethod('Layout')
$clock = $tWall.GetMethod('Clock')
$hit = $tWall.GetMethod('HitButton')
$buttons = $tWall.GetMethod('Buttons')
$paint = $tWall.GetMethod('PaintOverlay')
$drawPanel = $tPanel.GetMethod('Draw')
$back = [System.Drawing.Color]$tTheme.GetField('Back').GetValue($null)
$wide = 16.0 / 9

function Aspects([double[]]$values) {
  $list = New-Object 'System.Collections.Generic.List[double]'
  foreach ($v in $values) { $list.Add($v) }
  return ,$list
}
function L([int]$w, [int]$h, [double[]]$aspects, [bool]$info, [int]$solo) {
  return $layoutOf.Invoke($null, [object[]]@((New-Object System.Drawing.Rectangle 0, 0, $w, $h), (Aspects $aspects), $info, $solo, [single]1))
}
function Rows($layout) { return (@($layout.Rows) -join ',') }
function Same([double]$x, [double]$y, [double]$tolerance) { return [Math]::Abs($x - $y) -le $tolerance }

# The images of a layout: none over another, all inside the area, joined in each row, all of the same height.
function Joined($layout, [int]$w, [int]$h) {
  $cells = @($layout.Cells)
  $area = New-Object System.Drawing.Rectangle 0, 0, $w, $h
  for ($i = 0; $i -lt $cells.Count; $i++) {
    if (-not $area.Contains($cells[$i])) { return 'cell ' + $i + ' outside ' + $cells[$i] }
    if ($cells[$i].Height -ne $cells[0].Height) { return 'cell ' + $i + ' of another height' }
    for ($j = $i + 1; $j -lt $cells.Count; $j++) { if ($cells[$i].IntersectsWith($cells[$j])) { return 'cells ' + $i + ' and ' + $j + ' over each other' } }
  }
  $index = 0
  foreach ($n in @($layout.Rows)) {
    for ($c = 1; $c -lt $n; $c++) { if ($cells[$index + $c].Left -ne $cells[$index + $c - 1].Right -or $cells[$index + $c].Top -ne $cells[$index].Top) { return 'row with a gap at cell ' + ($index + $c) } }
    $index += $n
  }
  if (-not $layout.Info.IsEmpty) {
    if (-not $area.Contains($layout.Info)) { return 'the live panel outside' }
    foreach ($c in $cells) { if ($c.IntersectsWith($layout.Info)) { return 'the live panel over an image' } }
  }
  return ''
}

# ---- Five cameras (the plan of the user: 3 above, 2 and the live panel below).
$l5 = L 1880 900 @($wide, $wide, $wide, $wide, $wide) $true (-1)
Check ((Rows $l5) -eq '3,2' -and $l5.InfoPlace -eq 'slot') ('five cameras: three above, two and the live panel below (' + (Rows $l5) + ' ' + $l5.InfoPlace + ')')
$c5 = @($l5.Cells)
Check ((Joined $l5 1880 900) -eq '') ('five cameras joined without gaps ' + (Joined $l5 1880 900))
Check ($l5.Info.Left -eq $c5[4].Right -and $l5.Info.Top -eq $c5[3].Top -and $l5.Info.Height -eq $c5[3].Height -and (Same $l5.Info.Width $c5[2].Width 2)) ('the live panel in the sixth place, of the size of an image ' + $l5.Info)
Check ((Same ($c5[0].Width / $c5[0].Height) $wide 0.02) -and $c5[0].Height -eq 352) ('whole 16:9 images, 352 px high in 1880 x 900 (' + $c5[0].Width + ' x ' + $c5[0].Height + ')')
Check ($c5[0].Left -eq 1 -and $c5[2].Right -eq 1878 -and $c5[0].Top -eq 98 -and $c5[3].Top -eq 450) ('the block in the middle of the area ' + $c5[0] + ' ' + $c5[2])
$full = L 1920 1030 @($wide, $wide, $wide, $wide, $wide) $true (-1)
Check ((Rows $full) -eq '3,2' -and @($full.Cells)[0].Width -eq 640 -and @($full.Cells)[0].Height -eq 360) ('full screen 1920 x 1030: 640 x 360 each (' + @($full.Cells)[0] + ')')
$narrow = L 1600 930 @($wide, $wide, $wide, $wide, $wide) $true (-1)
Check ((Rows $narrow) -eq '3,2' -and $narrow.InfoPlace -eq 'slot' -and (Joined $narrow 1600 930) -eq '') ('a narrower window (the list of the files open): the same plan (' + (Rows $narrow) + ')')
$no = L 1880 900 @($wide, $wide, $wide, $wide, $wide) $false (-1)
Check ($no.Info.IsEmpty -and $no.InfoPlace -eq '') 'without a live panel nothing is kept for it'

# ---- Four, three, two cameras.
$l4 = L 1880 900 @($wide, $wide, $wide, $wide) $true (-1)
Check ((Rows $l4) -eq '2,2' -and $l4.InfoPlace -eq 'side' -and (Joined $l4 1880 900) -eq '' -and $l4.Info.Height -eq 900 -and $l4.Info.Width -ge 220) ('four cameras: 2 x 2, the live panel beside (' + (Rows $l4) + ' ' + $l4.InfoPlace + ' ' + $l4.Info + ')')
$l3 = L 1880 900 @($wide, $wide, $wide) $true (-1)
Check ((Rows $l3) -eq '2,1' -and $l3.InfoPlace -eq 'slot' -and (Joined $l3 1880 900) -eq '') ('three cameras: two above, one and the live panel below (' + (Rows $l3) + ' ' + $l3.InfoPlace + ')')
$l2 = L 1880 900 @($wide, $wide) $true (-1)
$c2 = @($l2.Cells)
Check ((Rows $l2) -eq '2' -and $l2.InfoPlace -eq 'below' -and (Joined $l2 1880 900) -eq '' -and $l2.Info.Top -eq $c2[0].Bottom -and $l2.Info.Width -eq ($c2[1].Right - $c2[0].Left)) ('two cameras side by side, the live panel below (' + (Rows $l2) + ' ' + $l2.InfoPlace + ' ' + $l2.Info + ')')
Check ($l2.Info.Height -ge 110 -and $l2.Info.Height -le [int]($c2[0].Height * 0.6) + 1) ('the panel below at most 0.6 of an image high (' + $l2.Info.Height + ')')
$tight = L 1600 900 @($wide, $wide, $wide, $wide) $true (-1)
Check ((Rows $tight) -eq '2,2' -and $tight.Info.IsEmpty) ('no room for the live panel: the images stay as big (' + (Rows $tight) + ' ' + $tight.InfoPlace + ')')

# ---- Images of another shape, unknown, one alone.
$baumer = 2048.0 / 1088
$lb = L 1880 900 @($baumer, $baumer, $baumer, $baumer, $baumer) $true (-1)
$cb = @($lb.Cells)
Check ((Same ($cb[0].Width / $cb[0].Height) $baumer 0.02) -and (Joined $lb 1880 900) -eq '' -and (Rows $lb) -eq '3,2') ('images of 2048 x 1088 keep their shape (' + $cb[0] + ')')
$lu = L 1880 900 @(0, 0, 0) $true (-1)
Check ((Same (@($lu.Cells)[0].Width / @($lu.Cells)[0].Height) $wide 0.02)) 'a screen without an image yet takes the place of a 16:9 image'
$ls = L 1880 900 @($wide, $wide, $wide, $wide, $wide) $true 2
$cs = @($ls.Cells)
$shown = @($cs | Where-Object { $_.Width -gt 0 }).Count
Check ($ls.Solo -eq 2 -and $shown -eq 1 -and $cs[2].Height -eq 900 -and (Same ($cs[2].Width / $cs[2].Height) $wide 0.02) -and $cs[2].Left -eq [int]((1880 - $cs[2].Width) / 2) -and $ls.Info.IsEmpty) ('one camera alone on the wall: as big as it fits, in the middle (' + $cs[2] + ')')
$lx = L 1880 900 @($wide, $wide) $true 7
Check ($lx.Solo -eq -1 -and @($lx.Cells | Where-Object { $_.Width -gt 0 }).Count -eq 2) 'a screen that is not there is never alone'
$lt = L 10 10 @($wide, $wide) $true (-1)
Check (@($lt.Cells | Where-Object { $_.Width -gt 0 }).Count -eq 0) 'an area too small: nothing placed, no error'

# ---- The time of a recording, the buttons over an image.
Check (($clock.Invoke($null, [object[]]@([double]12.44, $true))) -eq '00:12.4' -and ($clock.Invoke($null, [object[]]@([double]75.9, $false))) -eq '01:15' -and ($clock.Invoke($null, [object[]]@([double](-3), $true))) -eq '00:00.0') 'the time of a recording (00:12.4, 01:15)'
$img = New-Object System.Drawing.Rectangle 100, 50, 640, 360
$places = @($buttons.Invoke($null, [object[]]@($img, [single]1)))
$inside = @($places | Where-Object { -not $img.Contains($_) }).Count -eq 0
$hits = @(0, 1, 2 | ForEach-Object { $p = $places[$_]; [int]$hit.Invoke($null, [object[]]@($img, [single]1, (New-Object System.Drawing.Point ($p.Left + $p.Width / 2), ($p.Top + $p.Height / 2)))) })
$miss = [int]$hit.Invoke($null, [object[]]@($img, [single]1, (New-Object System.Drawing.Point 120, 70)))
Check ($inside -and ($hits -join ',') -eq '0,1,2' -and $miss -eq -1 -and $places[2].Right -le $img.Right -and $places[0].Left -lt $places[1].Left) ('three buttons at the bottom right of the image: alone, snapshot, settings (' + ($hits -join ',') + ')')

# ---- Pictures of the wall on fictional cameras.
function Cam([int]$n, [string]$name, [bool]$loaded, [bool]$connected, [bool]$grabbing, [bool]$recording, [double]$fps, [long]$drops, [double]$seconds) {
  $c = [Activator]::CreateInstance($tCam)
  $c.Number = $n; $c.Name = $name; $c.Loaded = $loaded; $c.Connected = $connected; $c.Grabbing = $grabbing; $c.Recording = $recording
  $c.Fps = $fps; $c.Drops = $drops; $c.Load = [single]35; $c.RecordingSeconds = $seconds
  return $c
}
function Frame($g, $r, [int]$kind) {
  # A fictional pool: the water (above it for the first camera), the lane ropes, a few light bands.
  if ($kind -eq 0) { $c1 = [System.Drawing.Color]::FromArgb(150, 170, 182); $c2 = [System.Drawing.Color]::FromArgb(24, 96, 128) }
  else { $c1 = [System.Drawing.Color]::FromArgb(18, 118, 152); $c2 = [System.Drawing.Color]::FromArgb(3, 42, 66) }
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush($r, $c1, $c2, [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
  $g.FillRectangle($br, $r); $br.Dispose()
  $rnd = New-Object System.Random ($kind * 7 + 3)
  for ($i = 0; $i -lt 14; $i++) {
    $x = $r.Left + $rnd.NextDouble() * $r.Width; $y = $r.Top + $rnd.NextDouble() * $r.Height; $w = 40 + $rnd.NextDouble() * $r.Width * 0.3
    $lb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(18, 220, 250, 255))
    $g.FillEllipse($lb, [single]$x, [single]$y, [single]$w, [single]($w * 0.18)); $lb.Dispose()
  }
  for ($k = 1; $k -le 2; $k++) {
    $y = $r.Top + $r.Height * (0.35 + 0.4 * ($k - 1))
    for ($x = $r.Left; $x -lt $r.Right; $x += 18) {
      $col = if ((($x - $r.Left) / 18) % 2 -eq 0) { [System.Drawing.Color]::FromArgb(200, 230, 60, 60) } else { [System.Drawing.Color]::FromArgb(200, 240, 240, 240) }
      $pb = New-Object System.Drawing.SolidBrush $col
      $g.FillEllipse($pb, [single]$x, [single]($y - 4), [single]14, [single]8); $pb.Dispose()
    }
  }
}
$statusOf = @{}
foreach ($n in @('Disconnected', 'Paused', 'Disarmed', 'Recording')) { $statusOf[$n] = [Enum]::Parse($tStatus, $n) }
function Picture([string]$file, [int]$w, [int]$h, $cams, [double[]]$aspects, [int]$solo, [int]$hover, [int]$hoverButton, [bool]$recording) {
  $bar = 50
  $b = New-Object System.Drawing.Bitmap $w, ($h + $bar)
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    $g.Clear($back)
    $layout = $layoutOf.Invoke($null, [object[]]@((New-Object System.Drawing.Rectangle 0, 0, $w, $h), (Aspects $aspects), $true, $solo, [single]1))
    $cells = @($layout.Cells)
    for ($i = 0; $i -lt $cells.Count; $i++) {
      $r = $cells[$i]
      if ($r.Width -le 0) { continue }
      $cam = $cams[$i]
      $hasImage = $cam.Loaded -and $cam.Connected
      $status = if (-not $cam.Loaded) { $statusOf['Disconnected'] } elseif ($cam.Recording) { $statusOf['Recording'] } elseif (-not $cam.Grabbing) { $statusOf['Paused'] } else { $statusOf['Disarmed'] }
      if ($hasImage) { Frame $g $r ([Math]::Min(1, $i)) }
      $g.SetClip($r)
      [void]$paint.Invoke($null, [object[]]@($g, $r, $cam, $status, [single]1, $hasImage, ($i -eq $hover), $(if ($i -eq $hover) { $hoverButton } else { -1 }), ($i -eq $solo), [single]1))
      $g.ResetClip()
    }
  } catch {
    Check $false ($file + ': ' + $_.Exception.ToString())
  } finally { $g.Dispose() }
  return @{ Bitmap = $b; Layout = $layout; Bar = $bar }
}
function Finish($pic, [string]$file, $cams, [bool]$recording, [bool]$swap) {
  $b = $pic.Bitmap; $layout = $pic.Layout
  $g = [System.Drawing.Graphics]::FromImage($b)
  try {
    # The live panel with the cameras of the picture.
    if (-not $layout.Info.IsEmpty) {
      $list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tCam))
      foreach ($c in $cams) { $list.Add($c) }
      [void]$drawPanel.Invoke($null, [object[]]@($g, $layout.Info, $list, [single]1))
    }
    $bar = [Activator]::CreateInstance($tBar)
    try {
      $bar.WallAvailable = $true; $bar.WallOn = $true; $bar.SwapAvailable = $swap
      if ($recording) { $bar.SetRecording($true); Start-Sleep -Milliseconds 1300 }
      $bar.Draw($g, (New-Object System.Drawing.Rectangle 0, ($b.Height - $pic.Bar), $b.Width, $pic.Bar))
    } finally { $bar.Dispose() }
  } catch {
    Check $false ($file + ': ' + $_.Exception.ToString())
  } finally { $g.Dispose() }
  if ($out) { $b.Save([System.IO.Path]::Combine($out, $file), [System.Drawing.Imaging.ImageFormat]::Png) }
  $b.Dispose()
  Check $true ('picture ' + $file)
}

$names = @('Πάνω από το νερό', 'Υποβρύχια 1', 'Υποβρύχια 2', 'Υποβρύχια 3', 'Υποβρύχια 4')
$five = @($wide, $wide, $wide, $wide, $wide)
$ready = @(0..4 | ForEach-Object { Cam ($_ + 1) $names[$_] $true $true $true $false 50 0 0 })
$ready[2].Drops = 3
$p = Picture 'wall-5-ready.png' 1880 900 $ready $five (-1) 3 1 $false
Finish $p 'wall-5-ready.png' $ready $false $false

$rec = @(0..4 | ForEach-Object { Cam ($_ + 1) $names[$_] $true $true $true $true 50 0 12.4 })
$rec[4].Drops = 12
$p = Picture 'wall-5-recording.png' 1880 900 $rec $five (-1) 1 2 $true
Finish $p 'wall-5-recording.png' $rec $true $false

$mixed = @((Cam 1 $names[0] $true $true $true $false 50 0 0), (Cam 2 $names[1] $true $false $true $false 0 0 0), (Cam 3 '' $false $false $false $false 0 0 0), (Cam 4 $names[3] $true $true $false $false 0 0 0), (Cam 5 '' $false $false $false $false 0 0 0))
$p = Picture 'wall-5-empty-screens.png' 1880 900 $mixed $five (-1) (-1) (-1) $false
Finish $p 'wall-5-empty-screens.png' $mixed $false $false

$p = Picture 'wall-5-alone.png' 1880 900 $ready $five 2 2 0 $false
Finish $p 'wall-5-alone.png' $ready $false $false

$two = @($ready[0], $ready[1])
$p = Picture 'wall-2.png' 1880 900 $two @($wide, $wide) (-1) (-1) (-1) $false
Finish $p 'wall-2.png' $two $false $true
$three = @($ready[0], $ready[1], $ready[2])
$p = Picture 'wall-3.png' 1880 900 $three @($wide, $wide, $wide) (-1) (-1) (-1) $false
Finish $p 'wall-3.png' $three $false $false
$four = @($ready[0], $ready[1], $ready[2], $ready[3])
$p = Picture 'wall-4.png' 1880 900 $four @($wide, $wide, $wide, $wide) (-1) (-1) (-1) $false
Finish $p 'wall-4.png' $four $false $false

# English (the button of the language of Kinovea Swim).
$english = $tLang.GetProperty('English')
$english.SetValue($null, $true)
try {
  $p = Picture 'wall-5-english.png' 1880 900 $rec $five (-1) 0 0 $true
  Finish $p 'wall-5-english.png' $rec $true $false
} finally { $english.SetValue($null, $false) }

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $all).GetValue($null)
$keys = @('Ζωντανή εικόνα', 'Έτοιμο για εγγραφή', 'Εγγραφή', 'Στοπ', 'Παύση', 'Έναρξη', 'Εναλλαγή', 'Τοίχος καμερών', 'Όλες οι κάμερες', 'Χωρίς κάμερα',
  'Σύνδεση…', 'Σε παύση', 'Μόνη της σε όλη την οθόνη', 'Πίσω σε όλες τις κάμερες', 'Ρυθμίσεις κάμερας', 'Ρυθμίσεις κάμερας (όχι στην εγγραφή)',
  'Διπλό κλικ σε μια κάμερα: μόνη της σε όλη την οθόνη', '{0} από {1} κάμερες με εικόνα', '{0} κάμερες', 'χαμένα καρέ', 'χαμένο καρέ', 'Κενή οθόνη')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts' + ($(if ($missing.Count -gt 0) { ': missing ' + ($missing -join ' | ') } else { '' })))

if ($fails -gt 0) { Write-Output ('FAILED ' + $fails); exit 1 }
Write-Output 'all capture wall checks passed'
