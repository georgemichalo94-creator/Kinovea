# rc_ci.ps1 (2.39.1): the CI check of the lanes by the colours of the ropes (SwimRopeColours), on DRAWN pools (no video, no athlete):
# the yellow pixels; the three yellow ropes of lanes 4 and 5 among the ropes of a pool of 8 lanes seen from the side (green, blue, the
# three yellow, as World Aquatics has them), level, tilted, converging as a camera sees them and in the exact perspective of a camera,
# with splashes, noise and a yellow banner over the water; the place of each lane against them; the other ropes by the perspective;
# no three when four ropes are yellow or none; the horizon of the water; the lane numbers both ways. Also the wait of a package for its
# slides, the defaults of the label of the athlete, and the English of the new texts (also of the 📍 beside the marks of the left bar).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea).
#   powershell -File rc_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> [-out <folder for the pictures>]
param([string]$sm, [string]$bin, [string]$out = '')
$ErrorActionPreference = 'Stop'
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
Add-Type -AssemblyName System.Drawing
$a = [System.Reflection.Assembly]::LoadFrom($sm)
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$static = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
$tRopes = T 'SwimRopeColours'
$tImage = T 'SwimStrokeImage'
$mYellow = $tRopes.GetMethod('Yellow'); $mTriple = $tRopes.GetMethod('Triple'); $mPlace = $tRopes.GetMethod('Place'); $mRope = $tRopes.GetMethod('Rope')
$mIsYellow = $tRopes.GetMethod('IsYellow'); $mLaneAt = $tRopes.GetMethod('LaneAt'); $mPlaceOf = $tRopes.GetMethod('PlaceOf'); $mMiddle = $tRopes.GetMethod('Middle')
function R1($v) { return [Math]::Round([double]$v, 1) }

# ---- The yellow pixels.
function IsY($b, $g, $r) { return [bool]$mIsYellow.Invoke($null, [object[]]@([byte]$b, [byte]$g, [byte]$r)) }
Check ((IsY 20 210 240) -and (IsY 30 200 215) -and (IsY 20 140 240) -and (IsY 10 160 200)) 'yellow: the ropes of lanes 4 and 5, their floats, their orange-yellow'
Check (-not (IsY 200 170 60) -and -not (IsY 230 230 230) -and -not (IsY 60 150 40) -and -not (IsY 160 60 30) -and -not (IsY 30 120 230) -and -not (IsY 40 90 100)) 'not yellow: the water, the white, the green and the blue ropes, a cap, the dark'

# ---- A drawn pool: 9 ropes from the far side (lane 1 at the top), each from (0, left) to (960, right); the stands over the water.
function Pool([string[]]$colours, [double[]]$left, [double[]]$right, [bool]$banner, [bool]$splash, [int]$seed) {
  $top = $left[0]
  $bmp = New-Object System.Drawing.Bitmap 960, 540, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $water = New-Object System.Drawing.Drawing2D.LinearGradientBrush ((New-Object System.Drawing.Point 0, -1), (New-Object System.Drawing.Point 0, 541), [System.Drawing.Color]::FromArgb(40, 110, 140), [System.Drawing.Color]::FromArgb(80, 170, 190))
  $g.FillRectangle($water, 0, 0, 960, 540)
  $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 60, 90))), 0, 0, 960, [int]($top - 12))
  if ($banner) { $g.FillRectangle((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 205, 30))), 0, [int]($top - 34), 960, 9) }
  $paint = @{ green = [System.Drawing.Color]::FromArgb(40, 150, 60); blue = [System.Drawing.Color]::FromArgb(30, 60, 160); yellow = [System.Drawing.Color]::FromArgb(240, 210, 25); dark = [System.Drawing.Color]::FromArgb(200, 160, 10) }
  for ($i = 0; $i -lt $colours.Count; $i++) {
    $w = [float](3 + 2 * $i / $colours.Count)
    $g.DrawLine((New-Object System.Drawing.Pen $paint[$colours[$i]], $w), [float]0, [float]$left[$i], [float]960, [float]$right[$i])
    # The floats of a yellow rope: darker every 18 px.
    if ($colours[$i] -eq 'yellow') {
      $pen = New-Object System.Drawing.Pen $paint['dark'], $w
      for ($x = 0; $x -lt 960; $x += 18) {
        $y0 = $left[$i] + ($right[$i] - $left[$i]) * $x / 960; $y1 = $left[$i] + ($right[$i] - $left[$i]) * ($x + 5) / 960
        $g.DrawLine($pen, [float]$x, [float]$y0, [float]($x + 5), [float]$y1)
      }
    }
  }
  $rnd = New-Object System.Random $seed
  if ($splash) {
    $foam = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 245, 248, 250)); $cap = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(230, 120, 30))
    for ($k = 0; $k -lt 8; $k++) {
      $lane = $rnd.Next(0, $colours.Count - 1); $x = $rnd.Next(80, 860)
      $yy = 0.5 * (($left[$lane] + ($right[$lane] - $left[$lane]) * $x / 960) + ($left[$lane + 1] + ($right[$lane + 1] - $left[$lane + 1]) * $x / 960))
      $g.FillEllipse($foam, [float]($x - 40), [float]($yy - 8), [float]80, [float]14)
      $g.FillEllipse($cap, [float]($x - 46), [float]($yy - 5), [float]10, [float]10)
    }
  }
  $g.Dispose()
  # The rows of the working image (24-bit BGR without padding), with noise.
  $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, 960, 540), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $bytes = [byte[]]::new(960 * 540 * 3)
  for ($row = 0; $row -lt 540; $row++) { [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]($data.Scan0.ToInt64() + $row * $data.Stride), $bytes, $row * 2880, 2880) }
  $bmp.UnlockBits($data)
  for ($k = 0; $k -lt $bytes.Length; $k += 7) { $v = [int]$bytes[$k] + $rnd.Next(-12, 13); if ($v -lt 0) { $v = 0 } elseif ($v -gt 255) { $v = 255 }; $bytes[$k] = [byte]$v }
  $image = [Activator]::CreateInstance($tImage, [object[]]@([int]960, [int]540, $bytes))
  return @{ Image = $image; Bitmap = $bmp }
}

# The ropes of a pool: with lanes wider by a step each (top, the far lane, the step), or by the perspective of a camera,
# y = (A d + B) / (C d + 1) for the ropes d = −3 … 5 (the yellow ones 0, 1, 2).
function Steps([double]$top, [double]$w0, [double]$grow) { $ys = [double[]]::new(9); $y = $top; for ($i = 0; $i -lt 9; $i++) { $ys[$i] = $y; $y += $w0 + $grow * $i }; return ,$ys }
function Camera([double]$A, [double]$B, [double]$C) { $ys = [double[]]::new(9); for ($i = 0; $i -lt 9; $i++) { $d = $i - 3; $ys[$i] = ($A * $d + $B) / ($C * $d + 1) }; return ,$ys }
function Shift([double[]]$ys, [double]$by) { $r = [double[]]::new($ys.Count); for ($i = 0; $i -lt $ys.Count; $i++) { $r[$i] = $ys[$i] + $by }; return ,$r }

$fina = [string[]]@('green', 'blue', 'blue', 'yellow', 'yellow', 'yellow', 'blue', 'blue', 'green')
$cases = @(
  @{ name = 'level, a banner, splashes'; left = (Steps 120 34 4); right = (Steps 120 34 4); banner = $true; splash = $true; seed = 1; exact = $false },
  @{ name = 'tilted down'; left = (Steps 90 30 5); right = (Shift (Steps 90 30 5) 57.6); banner = $false; splash = $true; seed = 2; exact = $false },
  @{ name = 'tilted up, wide lanes'; left = (Steps 140 40 3); right = (Shift (Steps 140 40 3) -48); banner = $true; splash = $false; seed = 3; exact = $false },
  @{ name = 'converging, as a camera sees them'; left = (Camera 40 230 -0.03); right = (Camera 36 210 -0.03); banner = $true; splash = $true; seed = 6; exact = $false },
  @{ name = 'the perspective of a camera, exactly'; left = (Camera 40 225 -0.03); right = (Camera 40 225 -0.03); banner = $false; splash = $true; seed = 7; exact = $true }
)
$x = 480.0
foreach ($c in $cases) {
  $p = Pool $fina $c.left $c.right $c.banner $c.splash $c.seed
  if ($out) { $p.Bitmap.Save([System.IO.Path]::Combine($out, 'pool_' + $c.seed + '.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
  $p.Bitmap.Dispose()
  $at = [double[]]::new(9); for ($i = 0; $i -lt 9; $i++) { $at[$i] = 0.5 * ($c.left[$i] + $c.right[$i]) }
  $lines = $mYellow.Invoke($null, [object[]]@($p.Image))
  $lane45 = $at[5] - $at[4]
  $triple = $mTriple.Invoke($null, [object[]]@($lines, $x, [double]$lane45))
  $okTriple = $null -ne $triple -and [Math]::Abs($triple[0] - $at[3]) -le 3 -and [Math]::Abs($triple[1] - $at[4]) -le 3 -and [Math]::Abs($triple[2] - $at[5]) -le 3
  Check $okTriple ('the three yellow ropes (' + $c.name + '): ' + $lines.Count + ' yellow lines, ' + $(if ($triple) { ($triple | ForEach-Object { R1 $_ }) -join ' / ' } else { 'none' }) + ' (drawn ' + ((R1 $at[3]), (R1 $at[4]), (R1 $at[5]) -join ' / ') + ')')
  if ($null -eq $triple) { continue }
  # Each lane by its middle: lane n between ropes n−1 and n; places −4 … 3 for lanes 1 … 8 (lane 1 at the far side).
  $places = @(); $errors = @(); $okMid = $true; $worst = 0.0
  for ($n = 1; $n -le 8; $n++) {
    $drawn = 0.5 * ($at[$n - 1] + $at[$n]); $width = $at[$n] - $at[$n - 1]
    $q = $mPlace.Invoke($null, [object[]]@($triple, [double]$drawn))
    $places += $(if ($null -eq $q) { 'null' } else { [string]$q })
    $m = [double]$mMiddle.Invoke($null, [object[]]@($triple, [int]($n - 5)))
    $errors += (R1 ($m - $drawn))
    # The middles of lanes 3 to 6 (next to the yellow ropes: where a proposal is moved to) within a tenth of a lane in every pool; in
    # the perspective of a camera also lanes 1, 2, 7 and 8, three or four lanes from the yellow ropes, within 30% of a lane: the
    # perspective comes from three ropes found to a pixel or so (CI 26.11: 20% at lanes 1 and 8, from 0.8 px on the middle rope).
    $limit = $(if ($n -ge 3 -and $n -le 6) { 0.10 } elseif ($c.exact) { 0.30 } else { 10.0 })
    if ([double]::IsNaN($m) -or [Math]::Abs($m - $drawn) -gt $limit * $width) { $okMid = $false }
  }
  Check (($places -join ',') -eq '-4,-3,-2,-1,0,1,2,3') ('the place of lanes 1 to 8 against them (' + $c.name + '): ' + ($places -join ','))
  Check $okMid ('the middles of the lanes from the ropes (' + $c.name + '), lanes 1-8 off by: ' + ($errors -join ' '))
  if ($c.exact) {
    $okRope = $true; $ropeErrors = @()
    for ($i = 0; $i -lt 9; $i++) {
      if ($at[$i] -gt 520) { continue }
      $y = [double]$mRope.Invoke($null, [object[]]@($triple, [double]($i - 3)))
      $width = $(if ($i -gt 0) { $at[$i] - $at[$i - 1] } else { $at[1] - $at[0] })
      # The yellow ropes and the next ones within 10% of a lane, the farthest (three, four lanes away) within 35%.
      $far = [Math]::Abs(($i - 3) - 1) -ge 3
      $ropeErrors += (R1 ($y - $at[$i])); if ([double]::IsNaN($y) -or [Math]::Abs($y - $at[$i]) -gt $(if ($far) { 0.35 } else { 0.10 }) * $width) { $okRope = $false }
    }
    Check $okRope ('the other ropes by the perspective of the three yellow ones (' + $c.name + '), off by: ' + ($ropeErrors -join ' '))
  }
}

# ---- Not the scheme: four yellow ropes in a row, or none (a yellow banner only).
$four = Pool ([string[]]@('green', 'blue', 'yellow', 'yellow', 'yellow', 'yellow', 'blue', 'blue', 'green')) (Steps 120 34 4) (Steps 120 34 4) $false $true 4
$lines4 = $mYellow.Invoke($null, [object[]]@($four.Image)); $four.Bitmap.Dispose()
$t4 = $mTriple.Invoke($null, [object[]]@($lines4, $x, 40.0))
Check ($null -eq $t4) ('four yellow ropes in a row: not lanes 4 and 5 (' + $lines4.Count + ' yellow lines)')
$none = Pool ([string[]]@('green', 'blue', 'blue', 'blue', 'blue', 'blue', 'blue', 'blue', 'green')) (Steps 120 34 4) (Steps 120 34 4) $true $true 5
$lines0 = $mYellow.Invoke($null, [object[]]@($none.Image)); $none.Bitmap.Dispose()
$t0 = $mTriple.Invoke($null, [object[]]@($lines0, $x, 40.0))
Check ($null -eq $t0) ('no yellow ropes (a yellow banner only): not lanes 4 and 5 (' + $lines0.Count + ' yellow lines)')

# ---- The horizon of the water: ropes 300, 330, 370 (lanes much wider nearer) have it at y = 90; nothing above it, no rope past the camera.
$h = [double[]]@(300.0, 330.0, 370.0)
$above = $mPlace.Invoke($null, [object[]]@($h, 50.0)); $far = $mPlace.Invoke($null, [object[]]@($h, 100.0)); $onRope = $mPlace.Invoke($null, [object[]]@($h, 330.5))
$past = [double]$mRope.Invoke($null, [object[]]@($h, 9.0)); $beyond = [double]$mMiddle.Invoke($null, [object[]]@($h, 7))
$inLane = $mPlace.Invoke($null, [object[]]@($h, 350.0))
Check ($null -eq $above -and $null -eq $far -and $null -eq $onRope -and [double]::IsNaN($past) -and [double]::IsNaN($beyond) -and $inLane -eq 0) 'the horizon of the water: no lane above it or far beyond the pool, none on a rope, no rope past the camera; lane 5 below the middle rope'

# ---- The lane numbers both ways (lanes 0 to 9).
$round = $true
for ($n = 0; $n -le 9; $n++) {
  foreach ($side in $true, $false) {
    $q = [int]$mPlaceOf.Invoke($null, [object[]]@([int]$n, [bool]$side))
    if ([int]$mLaneAt.Invoke($null, [object[]]@([int]$q, [bool]$side)) -ne $n) { $round = $false }
  }
}
$f4 = [int]$mLaneAt.Invoke($null, [object[]]@(-1, $true)); $f5 = [int]$mLaneAt.Invoke($null, [object[]]@(0, $true))
$n4 = [int]$mLaneAt.Invoke($null, [object[]]@(0, $false)); $n5 = [int]$mLaneAt.Invoke($null, [object[]]@(-1, $false))
Check ($round -and $f4 -eq 4 -and $f5 -eq 5 -and $n4 -eq 4 -and $n5 -eq 5) 'the lane numbers: lane 1 far: 4 above the middle yellow rope, 5 below; lane 1 near: 4 below, 5 above; lanes 0-9 both ways'

# ---- The package that waits for its slides: the wait as the window says it (in Greek).
(T 'SwimLang').GetProperty('English').SetValue($null, $false)
$waited = (T 'SwimAnalysisManager').GetMethod('SlidesWaited', $static)
$w45 = $waited.Invoke($null, [object[]]@([TimeSpan]::FromSeconds(45))); $w125 = $waited.Invoke($null, [object[]]@([TimeSpan]::FromSeconds(125)))
Check ($w45 -eq '45 δλ.' -and $w125 -eq '2:05') ('the wait of the slides: "' + $w45 + '", "' + $w125 + '"')
$waitField = (T 'SwimAnalysisManager').GetField('SlidesWaitSeconds', $static)
Check ($null -ne $waitField -and [double]$waitField.GetValue($null) -ge 600) ('the video of a package waits for its slides up to ' + $(if ($waitField) { $waitField.GetValue($null) } else { '?' }) + ' s')

# ---- The label of the athlete: on by default, kept in the settings, in the scenes of the video.
$settings = [Activator]::CreateInstance((T 'SwimSettings'))
$reelScene = [Activator]::CreateInstance((T 'SwimReelScene')); $hudScene = [Activator]::CreateInstance((T 'SwimHudScene'))
Check ($settings.ReelLabel -eq $true -and $reelScene.Label -eq $true -and $hudScene.Marker -eq $true) 'the label of the athlete on by default (the settings, the technical view, the HUD)'
Check ($null -ne $settings.LaneColourSide -and $settings.LaneColourSide.Count -eq 0) 'the side of lane 1 per folder in the settings, empty at first'

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $static).GetValue($null)
$keys = @(' δλ.', 'Φτιάχνονται οι διαφάνειες του αγώνα… {0}', 'Οι διαφάνειες έρχονται με το PDF του φύλλου… {0}', 'Ταμπέλα αθλητή', 'ταμπέλα αθλητή',
  'Η ταμπέλα του αθλητή φαίνεται στο βίντεο (και στις εξαγωγές).', 'Η ταμπέλα του αθλητή δεν φαίνεται στο βίντεο ούτε στις εξαγωγές· η ακολούθηση μένει.',
  "Κρύβει την ταμπέλα με το όνομα και τη διαδρομή του αθλητή και τον κύκλο γύρω του, από το βίντεο και τις εξαγωγές.`n",
  'Η ακολούθηση δεν σβήνεται (τη χρειάζεται η σύγκριση με την αναφορά): με ένα κλικ ξαναφαίνεται.', 'Ξαναδείχνει την ταμπέλα του αθλητή στο βίντεο και στις εξαγωγές.',
  "Η ταμπέλα με το όνομα και τη διαδρομή του αθλητή και ο κύκλος γύρω του, στο βίντεο και στις εξαγωγές (και στο βίντεο της αποστολής).`n",
  'Χωρίς αυτήν η ακολούθηση μένει (τη χρειάζεται η σύγκριση με την αναφορά).',
  '🤖 Στη μέση της εικόνας ήταν ο αθλητής της διαδρομής {0} (από τα κίτρινα σχοινιά των διαδρόμων 4 και 5)· ',
  'ο αγώνας είναι στη διαδρομή {1}: σημειώθηκε ο αθλητής της. Enter αν είναι αυτός και η ανάγνωση όλου του αγώνα ξεκινά · ',
  '🤖 ⚠ Ο αθλητής στη μέση της εικόνας φαίνεται να είναι στη διαδρομή {0} (από τα κίτρινα σχοινιά), όχι στη {1} του αγώνα: ',
  '↑/↓ για τη σωστή διαδρομή και Enter, ή κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).',
  '🤖 Ο αθλητής που ακολουθεί η κάμερα σημειώθηκε στο βίντεο (διαδρομή {0}, από τα κίτρινα σχοινιά): ',
  'για άλλον αθλητή: κλικ στο κεφάλι του και μετά στα πόδια του (Esc: ακύρωση).',
  '🤖 Η πρόταση άλλαξε διαδρομή (διαδρομή {0}, από τα κίτρινα σχοινιά): Enter και η ανάγνωση όλου του αγώνα ',
  'ξεκινά με αυτόν τον αθλητή · ↑/↓ για άλλη διαδρομή · ή κλικ στο κεφάλι του αθλητή και μετά στα πόδια του (Esc: ακύρωση).',
  '📍 Απόσταση εισόδου των χεριών από τον τοίχο εκκίνησης: κλικ εδώ και μετά κλικ στα χέρια στο βίντεο (στο καρέ της εισόδου).',
  '📍 Απόσταση εξόδου (κεφάλι) από τον τοίχο εκκίνησης: κλικ εδώ και μετά κλικ στο κεφάλι στο βίντεο (στο καρέ της εξόδου).',
  '📍 Απόσταση εξόδου (κεφάλι) από τον τοίχο της στροφής {0}{1}: κλικ εδώ και μετά κλικ στο κεφάλι στο βίντεο (στο καρέ της εξόδου).',
  ' Χωρίς βαθμονόμηση, ζητείται πρώτα. Δεύτερο πάτημα ή Esc: ακύρωση.', "`nΤώρα: ", ' (εκτίμηση από τους χρόνους, όχι μέτρηση).', "`nΔεν έχει μετρηθεί ακόμα.", 'κλικ…')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts (' + $keys.Count + ', missing ' + $missing.Count + ': ' + ($missing -join ' | ') + ')')

Write-Output ('rc_ci: ' + $fails + ' failed')
exit $fails
