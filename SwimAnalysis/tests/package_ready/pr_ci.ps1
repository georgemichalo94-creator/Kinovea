# pr_ci.ps1: the CI check of the ready package (2.24.3) on a FICTIONAL training. A package made before (its video, its PDF and its
# message file in its folder) opens as it is: SwimPackage.Ready (the video and what is kept about it, the PDF, the recipients and the
# text of its message file), the window (the line on top with when it was made, OK at once, the pictures of the video by the ffmpeg of
# the build, «Νέα δημιουργία»), the signature of the analysis (the same without what the program keeps about the sending, another after
# a change: the amber line), a package of an earlier version (what ffmpeg reads in its video), a video gone (made again), the message
# file kept on «Κλείσιμο». The pictures of the window go to -out. Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1
# when a check fails.
#   powershell -File pr_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls and ffmpeg.exe> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
New-Item -ItemType Directory -Force -Path $out | Out-Null
$out = (Resolve-Path $out).Path
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
# Reflection gets the objects themselves, not their PowerShell wrappers.
function B($o) { if ($null -eq $o) { return $null }; return $o.psobject.BaseObject }
# The comma: a list (even an empty one) comes out of the function as it is, not unrolled.
function F($o, $n) { return ,($o.GetType().GetField($n, $all).GetValue((B $o))) }
function SetF($o, $n, $v) { $o.GetType().GetField($n, $all).SetValue((B $o), (B $v)) }
function Pump($seconds, $until) {
  $end = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $end) {
    [System.Windows.Forms.Application]::DoEvents()
    if ($until -and (& $until)) { break }
    Start-Sleep -Milliseconds 40
  }
}
function Shot($form, $name) {
  $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
  $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
  $b.Save([System.IO.Path]::Combine($out, $name), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

$ff = [System.IO.Path]::Combine($bin, 'ffmpeg.exe')
Check ([System.IO.File]::Exists($ff)) 'the ffmpeg.exe of the build'
$tPkg = T 'SwimPackage'

# ---- A fictional training and the folder of its package.
$race = B ([Activator]::CreateInstance((T 'SwimRace')))
SetF $race 'Athlete' 'Αθλητής Δοκιμής'
SetF $race 'Kind' 'training'
SetF $race 'Style' 'fly'
SetF $race 'Date' '16.11.2026'
SetF $race 'AthleteClub' 'Όμιλος Δοκιμής'
$folder = [System.IO.Path]::Combine($out, 'Αποστολές προπονητών', '2026-11-16 Τεχνική ανάλυση', 'Όμιλος Δοκιμής')
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
$base = [string]$tPkg.GetMethod('BaseNameFor').Invoke($null, [object[]]@($race))
$pk = B ($tPkg.GetMethod('At').Invoke($null, [object[]]@($race, [string]$folder, $base)))
$video = F $pk 'VideoPath'
Check ($video -like '*Όμιλος Δοκιμής*.mp4' -and (F $pk 'PdfPath') -like '*.pdf') ('the files of the package: ' + [System.IO.Path]::GetFileName($video))

# The video: 6 s at 25 fps by the encoder of the program (a colour and the second on each image).
$encT = T 'SwimVideoEncoder'
$enc = ([Activator]::CreateInstance($encT)).psobject.BaseObject
$startArgs = [object[]]@([string]$ff, [string]$video, [int]640, [int]360, [double]25, $null, [double]0, [double]0, $null)
Check ($encT.GetMethod('Start').Invoke($enc, $startArgs)) ('ffmpeg for the fictional video: ' + $startArgs[8])
$w = 640; $h = 360
$buf = New-Object byte[] ($w * $h * 3)
$bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$font = New-Object System.Drawing.Font 'Segoe UI', 54, ([System.Drawing.FontStyle]::Bold)
$cols = @(@(20, 90, 140), @(30, 120, 80), @(140, 70, 40), @(90, 40, 130), @(60, 60, 60), @(160, 130, 30))
for ($i = 0; $i -lt 150; $i++) {
  $sec = [int][Math]::Floor($i / 25)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $c = $cols[$sec]; $g.Clear([System.Drawing.Color]::FromArgb($c[0], $c[1], $c[2]))
  $g.DrawString(('0:0' + $sec), $font, [System.Drawing.Brushes]::White, 40, 110); $g.Dispose()
  $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $w, $h), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, $bmp.PixelFormat)
  [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
  $bmp.UnlockBits($data)
  if (-not $enc.Write($buf)) { break }
}
$finishArgs = [object[]]@([int]60000, $null)
$finished = $encT.GetMethod('Finish').Invoke($enc, $finishArgs)
$enc.Dispose()
Check ($finished -and [System.IO.File]::Exists($video)) ('the fictional video written: ' + $finishArgs[1])

# A PDF of one page (fictional).
$stream = 'BT /F1 28 Tf 72 760 Td (Fictional report) Tj ET'
$objs = @('<< /Type /Catalog /Pages 2 0 R >>', '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
  '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
  ('<< /Length ' + $stream.Length + " >>`nstream`n" + $stream + "`nendstream"), '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append("%PDF-1.4`n")
$offsets = @()
for ($i = 0; $i -lt $objs.Count; $i++) { $offsets += $sb.Length; [void]$sb.Append(($i + 1).ToString() + " 0 obj`n" + $objs[$i] + "`nendobj`n") }
$xref = $sb.Length
[void]$sb.Append("xref`n0 " + ($objs.Count + 1) + "`n0000000000 65535 f `n")
foreach ($o in $offsets) { [void]$sb.Append($o.ToString('0000000000') + " 00000 n `n") }
[void]$sb.Append("trailer`n<< /Size " + ($objs.Count + 1) + " /Root 1 0 R >>`nstartxref`n" + $xref + "`n%%EOF`n")
[System.IO.File]::WriteAllText((F $pk 'PdfPath'), $sb.ToString(), [System.Text.Encoding]::ASCII)

# The message file, read back.
$rec = F $pk 'Recipients'; $rec.Add('coach.one@example.com'); $rec.Add('coach.two@example.com')
SetF $pk 'Subject' 'Τεχνική ανάλυση: Αθλητής Δοκιμής – Πεταλούδα – 16.11.2026'
SetF $pk 'Body' ("Καλησπέρα σας,`r`n`r`nσας στέλνουμε την τεχνική ανάλυση (φανταστική).`r`n`r`nΤμήμα ανάλυσης`r`n")
$pk.WriteMessage()
$back = B ($tPkg.GetMethod('At').Invoke($null, [object[]]@($race, [string]$folder, $base)))
$read = $back.ReadMessage()
Check ($read -and (F $back 'Recipients').Count -eq 2 -and (F $back 'Subject') -eq (F $pk 'Subject') -and (F $back 'Body').TrimEnd() -eq (F $pk 'Body').TrimEnd()) 'the message file read back: the recipients, the subject, the text'

# ---- What the race keeps about its package, and the signature of the analysis.
SetF $race 'PackageFolder' $folder
SetF $race 'PackageBase' $base
SetF $race 'PackageMade' '16/11/2026 22:40'
$info = B ([Activator]::CreateInstance((T 'SwimPackageInfo')))
SetF $info 'Seconds' ([double]6); SetF $info 'Width' ([int]640); SetF $info 'Height' ([int]360); SetF $info 'Sound' $false; SetF $info 'Screens' ([int]1)
$pics = F $info 'Pictures'
foreach ($m in @(@(0.5, 'Εκκίνηση'), @(2.5, 'Στροφή 1'), @(4.5, 'Στροφή 2'))) {
  $x = B ([Activator]::CreateInstance((T 'SwimPackageMoment'))); SetF $x 'Seconds' ([double]$m[0]); SetF $x 'Label' $m[1]; $pics.Add($x)
}
SetF $info 'Archived' $true
SetF $info 'ArchiveNote' '✓ Καταχωρίστηκε στο KOE App («Αρχεία Αθλητών/τριων»).'
SetF $race 'PackageInfo' $info
$sign = $tPkg.GetMethod('Signature')
$changedSince = $tPkg.GetMethod('ChangedSince')
SetF $info 'Sig' ([string]$sign.Invoke($null, [object[]]@($race)))
$s0 = $sign.Invoke($null, [object[]]@($race))
SetF $race 'LastPdfUrl' 'https://example.com/fictional.pdf'
SetF $race 'SheetPending' $true
(F $race 'Graphics').Add((B ([Activator]::CreateInstance((T 'SwimReportGraphic')))))
SetF $race 'PackageSent' '17/11/2026 09:00'
$s1 = $sign.Invoke($null, [object[]]@($race))
Check ($s0 -eq $s1 -and $s0.Length -eq 20) ('the signature without what is kept about the sending (the PDF, the Sheet, the images, the package): ' + $s0)
Check ($changedSince.Invoke($null, [object[]]@($race)) -eq $false) 'not changed after its package'

# ---- The ready package.
$ready = B ($tPkg.GetMethod('Ready').Invoke($null, [object[]]@($race, [string]$ff)))
Check ($null -ne $ready -and (F $ready 'VideoReady') -and (F $ready 'PdfReady') -and (F $ready 'MessageKept')) 'the ready package: its video, its PDF, its message'
Check ((F $ready 'Moments').Count -eq 3 -and (F $ready 'Recipients').Count -eq 2 -and (F $ready 'VideoSize').Width -eq 640 -and [Math]::Abs((F $ready 'VideoSeconds') - 6) -lt 0.01) 'what is kept: the moments of the pictures, the size and length of the video'
Check ((F $ready 'Archived') -eq $true -and (F $ready 'ArchiveNote') -like '✓*') 'the line of the KOE App kept'

$tForm = T 'FormSwimPackage'
$ctor = $tForm.GetConstructors()[0]
$form = $ctor.Invoke([object[]]@((B $ready), 'Αθλητής Δοκιμής · Τεχνική πεταλούδα 16.11.2026', 'Τμήμα ανάλυσης', $null, $null, $null, $null, $null))
$form.ShowReady('16/11/2026 22:40', $false, $ff)
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Location = New-Object System.Drawing.Point 0, 0
$form.Show()
$thumbs = F $form 'thumbs'
Pump 45 { $thumbs.Controls.Count -ge 3 }
Pump 3 $null
Check ($thumbs.Controls.Count -eq 3) ('the pictures of the video by ffmpeg: ' + $thumbs.Controls.Count)
Check ((F $form 'btnOk').Enabled) 'OK at once (nothing is made)'
Check ((F $form 'txtTo').Text -eq 'coach.one@example.com, coach.two@example.com') ('the recipients of the message file: ' + (F $form 'txtTo').Text)
Check ((F $form 'txtBody').Text -like '*(φανταστική)*') 'the text of the message file'
$line = $form.Controls.Find('readyText', $true)
Check ($line.Count -eq 1 -and $line[0].Text -like '*16/11/2026 22:40*' -and $line[0].Text -notlike '*άλλαξε*') ('the line on top: ' + $line[0].Text)
Check ((F $form 'videoStatus').Text -like '✓*0:06*640x360*') ('the video: ' + (F $form 'videoStatus').Text)
Check ((F $form 'pdfStatus').Text -like '✓*') ('the PDF: ' + ((F $form 'pdfStatus').Text -replace "`n", ' / '))
Check (-not (F $form 'videoProgress').Visible) 'no progress of a video being made'
Shot $form 'package-ready.png'

# «Κλείσιμο» keeps the recipients and the text of the window in the message file.
(F $form 'txtTo').Text = 'coach.three@example.com'
$form.Close()
Pump 1 $null
$kept = B ($tPkg.GetMethod('At').Invoke($null, [object[]]@($race, [string]$folder, $base)))
Check ($kept.ReadMessage() -and (F $kept 'Recipients').Count -eq 1 -and (F $kept 'Recipients')[0] -eq 'coach.three@example.com') 'the message kept on «Κλείσιμο»'
$form.Dispose()

# ---- The analysis changed after the package: the amber line, «Νέα δημιουργία».
SetF $race 'TechNotes' 'Μια σημείωση μετά την αποστολή (φανταστική).'
Check ($changedSince.Invoke($null, [object[]]@($race)) -eq $true) 'changed after its package: another signature'
$ready2 = B ($tPkg.GetMethod('Ready').Invoke($null, [object[]]@($race, [string]$ff)))
$form2 = $ctor.Invoke([object[]]@((B $ready2), 'Αθλητής Δοκιμής · Τεχνική πεταλούδα 16.11.2026', 'Τμήμα ανάλυσης', $null, $null, $null, $null, $null))
$form2.ShowReady('16/11/2026 22:40', $true, $ff)
$form2.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form2.Location = New-Object System.Drawing.Point 0, 0
$form2.Show()
$thumbs2 = F $form2 'thumbs'
Pump 45 { $thumbs2.Controls.Count -ge 3 }
Pump 2 $null
$line2 = $form2.Controls.Find('readyText', $true)
$remake = $form2.Controls.Find('btnRemake', $true)
Check ($line2.Count -eq 1 -and $line2[0].Text -like '*άλλαξε*' -and $remake.Count -eq 1 -and $remake[0].GetType().GetProperty('Highlighted', $all).GetValue($remake[0])) ('the amber line and «Νέα δημιουργία» lit: ' + $line2[0].Text)
Shot $form2 'package-ready-changed.png'
$remake[0].PerformClick()
Pump 1 $null
Check ($form2.Remake -and $form2.DialogResult -eq [System.Windows.Forms.DialogResult]::Retry) '«Νέα δημιουργία»: made again'
$form2.Dispose()

# ---- A package of an earlier version (nothing kept about it): what ffmpeg reads in its video, six pictures over it.
SetF $race 'PackageInfo' $null
Check ($null -eq $changedSince.Invoke($null, [object[]]@($race))) 'an earlier package: a change is not known'
$old = B ($tPkg.GetMethod('Ready').Invoke($null, [object[]]@($race, [string]$ff)))
$size = F $old 'VideoSize'
Check ($null -ne $old -and $size.Width -eq 640 -and $size.Height -eq 360 -and [Math]::Abs((F $old 'VideoSeconds') - 6) -lt 0.2 -and (F $old 'Moments').Count -eq 6) ('an earlier package: ' + $size.Width + 'x' + $size.Height + ', ' + (F $old 'VideoSeconds') + ' s, ' + (F $old 'Moments').Count + ' pictures')
$frameAt = $encT.GetMethod('Frame').Invoke($null, [object[]]@([string]$ff, [string]$video, [double]3.5, [int]320))
Check ($null -ne $frameAt -and $frameAt.Width -eq 320 -and $frameAt.Height -eq 180) 'a picture of the video at 3.5 s, 320 wide'
if ($null -ne $frameAt) { $frameAt.Dispose() }
# The picture of its moment, not of the key image before it (every 2 s): the second by its colour (the nearest of the six).
foreach ($at in @(0.5, 1.5, 2.5, 3.5, 4.5, 5.5)) {
  $pic = $encT.GetMethod('Frame').Invoke($null, [object[]]@([string]$ff, [string]$video, [double]$at, [int]320))
  if ($null -eq $pic) { Check $false ('a picture at ' + $at + ' s'); continue }
  $px = $pic.GetPixel(300, 170); $pic.Dispose()
  $best = -1; $dist = 1e9
  for ($k = 0; $k -lt $cols.Count; $k++) { $d = [Math]::Abs($px.R - $cols[$k][0]) + [Math]::Abs($px.G - $cols[$k][1]) + [Math]::Abs($px.B - $cols[$k][2]); if ($d -lt $dist) { $dist = $d; $best = $k } }
  Check ($best -eq [int][Math]::Floor($at)) ('the picture at ' + $at + ' s shows the second ' + $best + ' (' + $px.R + ',' + $px.G + ',' + $px.B + ')')
}
$none = $encT.GetMethod('Frame').Invoke($null, [object[]]@([string]$ff, [string]$video, [double]30, [int]320))
Check ($null -eq $none) 'no picture after the end of the video'

# ---- The video gone from the folder: no ready package (the package is made again).
$moved = $video + '.moved'
[System.IO.File]::Move($video, $moved)
Check ($null -eq $tPkg.GetMethod('Ready').Invoke($null, [object[]]@($race, [string]$ff))) 'the video not in its folder: made again'
[System.IO.File]::Move($moved, $video)
SetF $race 'PackageFolder' $null
Check ($null -eq $tPkg.GetMethod('Ready').Invoke($null, [object[]]@($race, [string]$ff))) 'no package made: none ready'

Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
