# gcam_ci.ps1: the CI check of the camera of the moment for the snapshots of a group of screens (2.23.0), on FICTIONAL cameras and
# names: which camera shows each event of a start and a turn (out of the water until the entry, then and in the turns in the water),
# the order the screens are asked for their picture (the camera wanted, the screen the analyst works on, the one of the analysis; a
# camera asked for only on its screen; nothing while a camera has no role), the camera of a video (the recordings of the GoPro
# cameras, the name of the file, the file itself), what a name proposes, the settings and the snapshot in their files, the window of
# the cameras (a picture of it goes to -out, an artifact of the run).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File gcam_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
if ($out) { New-Item -ItemType Directory -Force -Path $out | Out-Null; $out = (Resolve-Path $out).Path }
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
$all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

$pick = T 'SwimCameraPick'; $tMark = T 'SwimMarkType'; $tKind = T 'SwimPhaseKind'; $tPhase = T 'SwimTechPhase'; $tCam = T 'SwimGroupCamera'
$tTake = T 'SwimTake'; $tTakeCam = T 'SwimTakeCamera'; $tSettings = T 'SwimSettings'; $tSnap = T 'SwimSnapshot'; $tForm = T 'FormSwimGroupCameras'
$None = [int]$pick.GetField('None').GetValue($null); $Above = [int]$pick.GetField('Above').GetValue($null)
$Water = [int]$pick.GetField('Water').GetValue($null); $Extra = [int]$pick.GetField('Extra').GetValue($null)
Check ($None -eq 0 -and $Above -eq 1 -and $Water -eq 2 -and $Extra -eq 3) 'the roles of a camera as the settings keep them (0 not said, 1 out of the water, 2 in the water, 3 only on its screen)'

# ---- The camera that shows the event of a mark.
$roleOfMark = $pick.GetMethod('RoleOfMark')
function MarkRole([string]$type, [string]$kind) {
  $k = $null
  if ($kind) { $k = [Enum]::Parse($tKind, $kind) }
  return [int]$roleOfMark.Invoke($null, [object[]]@([Enum]::Parse($tMark, $type), $k))
}
$bad = @('Signal', 'Reaction', 'HandsOff', 'BackFoot', 'Entry') | Where-Object { (MarkRole $_ 'Start') -ne $Above -or (MarkRole $_ '') -ne $Above }
Check (@($bad).Count -eq 0) ('a start from its signal to the entry: the camera out of the water ' + (@($bad) -join ','))
$bad = @('Kick', 'Breakout', 'Stroke') | Where-Object { (MarkRole $_ 'Start') -ne $Water }
Check (@($bad).Count -eq 0) ('a start after the entry (kicks, breakout, first stroke): the camera in the water ' + (@($bad) -join ','))
$bad = @('Stroke', 'RotationStart', 'WallContact', 'Split', 'FeetOff', 'Kick', 'Breakout', 'TechSplit') | Where-Object { (MarkRole $_ 'Turn') -ne $Water }
Check (@($bad).Count -eq 0) ('every event of a turn: the camera in the water ' + (@($bad) -join ','))
Check ((MarkRole 'FeetOff' 'Start') -eq $Above -and (MarkRole 'FeetOff' 'Turn') -eq $Water -and (MarkRole 'FeetOff' '') -eq $None) 'the feet leave the block out of the water, the wall in the water; without a phase nothing is said'
Check ((MarkRole 'Stroke' '') -eq $Water -and (MarkRole 'RotationStart' '') -eq $Water -and (MarkRole 'Kick' '') -eq $Water) 'a stroke or a rotation kept before its wall is marked: the camera in the water'

# ---- The camera that shows a moment of a phase.
$roleAt = $pick.GetMethod('RoleAt')
function Phase([string]$kind, $block, $entry) {
  $p = [Activator]::CreateInstance($tPhase); $p.Kind = [Enum]::Parse($tKind, $kind)
  if ($null -ne $block) { $p.Block = [double]$block }
  if ($null -ne $entry) { $p.Entry = [double]$entry }
  return $p
}
function At($p, $t) { return [int]$roleAt.Invoke($null, [object[]]@($p, [double]$t)) }
$start = Phase 'Start' 0.70 1.05
Check ((At $start (-0.4)) -eq $Above -and (At $start 0.3) -eq $Above -and (At $start 1.05) -eq $Above -and (At $start 1.06) -eq $Above) 'a start until its entry: out of the water'
Check ((At $start 1.10) -eq $Water -and (At $start 3.5) -eq $Water) 'a start after its entry: in the water'
$noEntry = Phase 'Start' 0.70 $null
Check ((At $noEntry 1.05) -eq $Above -and (At $noEntry 1.15) -eq $Water) 'the entry not marked: a flight after the release'
$bare = Phase 'Start' $null $null
Check ((At $bare 1.10) -eq $Above -and (At $bare 1.20) -eq $Water) 'neither the release nor the entry marked: a little over a second after the signal'
$turn = Phase 'Turn' $null $null
Check ((At $turn (-1.5)) -eq $Water -and (At $turn 0) -eq $Water -and (At $turn 2.5) -eq $Water) 'a turn: in the water'
Check ([int]$roleAt.Invoke($null, [object[]]@($null, [double]1)) -eq $None) 'outside a phase the moment says nothing'

# ---- The stage of the snapshot of an event: the stage of the event itself (the events after it are not marked yet).
$ofMark = (T 'SwimStages').GetMethod('OfMark')
function StageOf([string]$type, [string]$kind) { return $ofMark.Invoke($null, [object[]]@([Enum]::Parse($tMark, $type), [Enum]::Parse($tKind, $kind))) }
$stagesStart = @((T 'FormSwimAnalysis').GetField('stagesStart', $all).GetValue($null))
$stagesTurn = @((T 'FormSwimAnalysis').GetField('stagesTurn', $all).GetValue($null))
$expect = [ordered]@{ 'Signal' = 'Θέση ετοιμότητας'; 'Reaction' = 'Φάση βατήρα'; 'HandsOff' = 'Φάση βατήρα'; 'BackFoot' = 'Φάση βατήρα'; 'FeetOff' = 'Αποδέσμευση'; 'Entry' = 'Είσοδος στο νερό'; 'Kick' = 'Υποβρύχια φάση'; 'Breakout' = 'Έξοδος από το νερό' }
$bad = @($expect.Keys | Where-Object { (StageOf $_ 'Start') -ne $expect[$_] -or $stagesStart -notcontains $expect[$_] })
Check ($bad.Count -eq 0) ('the events of a start: the reaction, the hands and the back foot on the block, then release, entry, kicks, breakout ' + ($bad -join ','))
$expect = [ordered]@{ 'RotationStart' = 'Περιστροφή'; 'WallContact' = 'Επαφή στον τοίχο'; 'FeetOff' = 'Αποκόλληση'; 'Kick' = 'Υποβρύχια φάση'; 'Breakout' = 'Έξοδος από το νερό' }
$bad = @($expect.Keys | Where-Object { (StageOf $_ 'Turn') -ne $expect[$_] -or $stagesTurn -notcontains $expect[$_] })
Check ($bad.Count -eq 0) ('the events of a turn: rotation, wall, push-off, kicks, breakout ' + ($bad -join ','))
$bad = @('Stroke', 'Split', 'TechSplit') | Where-Object { $null -ne (StageOf $_ 'Start') -or $null -ne (StageOf $_ 'Turn') }
Check (@($bad).Count -eq 0) ('a stroke or a distance: its moment says the stage ' + (@($bad) -join ','))

# ---- The order the screens are asked for their picture.
$order = $pick.GetMethod('Order')
function Ord([int[]]$roles, [int]$wanted, [int]$pressed, [int]$lead) {
  $list = $order.Invoke($null, [object[]]@($roles, $wanted, $pressed, $lead))
  return (@($list) -join ',')
}
Check ((Ord @($Above, $Water) $Above 1 0) -eq '0,1') 'two cameras, an event out of the water, pressed on the camera in the water: the camera out of the water'
Check ((Ord @($Above, $Water) $Water 0 0) -eq '1,0') 'two cameras, an event in the water, pressed on the camera of the analysis: the camera in the water'
Check ((Ord @($Water, $Above) $Above (-1) 0) -eq '1,0') 'a command of the program itself (no screen): the camera wanted, then the analysis'
Check ((Ord @($Above, $Water) $None 1 0) -eq '1,0') 'a moment that says nothing: the screen the analyst works on, then the analysis'
Check ((Ord @($Above, $Water) $None (-1) 0) -eq '0') 'a moment that says nothing and no screen: the analysis'
Check ((Ord @($Above, $Water, $Extra) $Water 2 0) -eq '2,1,0') 'pressed on a camera asked for only on its screen: that camera first'
Check ((Ord @($Above, $Water, $Extra) $Water 0 0) -eq '1,0') 'the camera asked for only on its screen is never taken by itself'
Check ((Ord @($Above, $Water, $Extra) $Above (-1) 1) -eq '0,1') 'three cameras, the analysis on the camera in the water, an event out of the water'
Check ((Ord @($Above, $None) $Above 0 0) -eq '') 'a camera without a role: no camera is chosen (all the screens together)'
Check ((Ord @($None, $None) $Water 1 0) -eq '') 'no camera has a role: no camera is chosen'
Check ((Ord @($Water, $Water) $Above 1 0) -eq '1,0') 'no camera of the kind wanted: the screen the analyst works on, then the analysis'
Check ((Ord @($Above, $Above, $Water) $Above 1 0) -eq '1,0') 'two cameras of the kind wanted: the one the analyst works on first'
Check ((Ord @($Above, $Water, $Water) $Water (-1) 0) -eq '1,2,0') 'two cameras in the water and none pressed: in the order of the screens'
Check ((Ord @($Above, $Water) $Water 7 0) -eq '1,0' -and (Ord @($Above, $Water) $Water 1 9) -eq '1') 'a screen that is not of the group is left out'
Check ((Ord @() $Water 0 0) -eq '') 'no screens'

# ---- The camera in the name of a video of the recordings of the GoPro cameras (fictional names).
$alias = $pick.GetMethod('AliasOfFileName')
function Alias([string]$name) { return [string]$alias.Invoke($null, [object[]]@($name)) }
Check ((Alias 'ΑΘΛΗΤΡΙΑ_ΔΟΚΙΜΗΣ_Στροφή_20261112_GoPro1.MP4') -eq 'GoPro1') 'the camera after the day of the name'
Check ((Alias '990001_ΑΘΛΗΤΡΙΑ_ΔΟΚΙΜΗΣ_Εκκίνηση_20261112_GoPro_2.MP4') -eq 'GoPro_2') 'a camera whose name has a space'
Check ((Alias 'Στροφή_20261112_101500_Υποβρύχια.MP4') -eq 'Υποβρύχια') 'a recording without an athlete: the time of the name is not the camera'
Check ((Alias 'ΑΘΛΗΤΡΙΑ_ΔΟΚΙΜΗΣ_Στροφή_20261112_GoPro1_μέρος2.MP4') -eq 'GoPro1') 'a part the camera cut'
Check ((Alias 'C:\Fictional\ΧΩΡΙΣ ΣΩΜΑΤΕΙΟ\A_Στροφή_20261112_Κάμερα 1.MP4') -eq 'Κάμερα 1') 'a whole path'
Check ((Alias 'IMG_1234.MOV') -eq '' -and (Alias 'clip_20261112.mp4') -eq '' -and (Alias '20261112_Αγώνας_GoPro1.MP4') -eq '' -and (Alias '') -eq '') 'other names say nothing'

# ---- The camera of a video: the recordings, the name of the file, the file itself.
$identify = $pick.GetMethod('Identify')
$takes = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($tTake))
$take = [Activator]::CreateInstance($tTake)
$camA = [Activator]::CreateInstance($tTakeCam); $camA.Alias = 'Κάμερα Α'; $camA.Targets.Add('D:\Fictional\ΧΩΡΙΣ ΣΩΜΑΤΕΙΟ\X\renamed by hand.MP4')
$camB = [Activator]::CreateInstance($tTakeCam); $camB.Alias = 'GoPro1'; $camB.Targets.Add('D:\Fictional\X\X_Στροφή_20261112_GoPro1.MP4')
$take.Cameras.Add($camA); $take.Cameras.Add($camB); $takes.Add($take)
$id = $identify.Invoke($null, [object[]]@('E:\elsewhere\RENAMED BY HAND.mp4', $takes))
Check ($id.Key -eq 'cam:κάμερα_α' -and $id.Name -eq 'Κάμερα Α') ('a file of the recordings, wherever it was moved: ' + $id.Key)
$id1 = $identify.Invoke($null, [object[]]@('E:\elsewhere\X_Στροφή_20261112_GoPro1.MP4', $takes))
$id2 = $identify.Invoke($null, [object[]]@('E:\other day\Y_Εκκίνηση_20261201_GoPro1.MP4', $null))
Check ($id1.Key -eq 'cam:gopro1' -and $id2.Key -eq 'cam:gopro1' -and $id2.Name -eq 'GoPro1') 'the same camera from the recordings and from the name of a file'
$id3 = $identify.Invoke($null, [object[]]@('C:\Fictional\Fictional Clip.mp4', $takes))
Check ($id3.Key -eq 'file:fictional clip.mp4' -and $id3.Name -eq '') ('a video of an unknown camera: its file ' + $id3.Key)

# ---- What the name of a camera proposes in the window, and the second of two cameras.
$suggest = $pick.GetMethod('SuggestRole')
function Sug([string]$name) { return [int]$suggest.Invoke($null, [object[]]@($name)) }
Check ((Sug 'Υποβρύχια') -eq $Water -and (Sug 'GoPro UW') -eq $Water -and (Sug 'Underwater 1') -eq $Water -and (Sug 'Μέσα στο νερό') -eq $Water) 'names of a camera in the water'
Check ((Sug 'Έξω') -eq $Above -and (Sug 'ΕΞΩ ΑΠΟ ΤΟ ΝΕΡΟ') -eq $Above -and (Sug 'Πάνω') -eq $Above -and (Sug 'deck cam') -eq $Above) 'names of a camera out of the water'
Check ((Sug 'GoPro1') -eq $None -and (Sug '') -eq $None -and (Sug 'έξω υποβρύχια') -eq $None -and (Sug 'Μεσαία') -eq $None) 'a name that says nothing, or both'
$complete = $pick.GetMethod('Complete')
function Comp([int[]]$r) { [void]$complete.Invoke($null, [object[]]@(, $r)); return ($r -join ',') }
Check ((Comp @($None, $Water)) -eq '1,2' -and (Comp @($Above, $None)) -eq '1,2' -and (Comp @($None, $Above)) -eq '2,1') 'two cameras, one known: the other from the other side of the surface'
Check ((Comp @($None, $None)) -eq '0,0' -and (Comp @($Extra, $None)) -eq '3,0' -and (Comp @($None, $Water, $None)) -eq '0,2,0') 'nothing proposed for two unknown cameras, a camera only on its screen, or three cameras'

# ---- The settings and the snapshot in their files (Newtonsoft as the program).
$jc = $json.GetType('Newtonsoft.Json.JsonConvert', $true)
$ser = $jc.GetMethod('SerializeObject', [Type[]]@([object]))
$des = $jc.GetMethod('DeserializeObject', [Type[]]@([string], [Type]))
$settings = [Activator]::CreateInstance($tSettings)
Check ($settings.GroupSnapshots -eq 0 -and $null -ne $settings.GroupCameras -and $settings.GroupCameras.Count -eq 0) 'new settings: the camera of the moment, no camera said'
$mgrType = T 'SwimAnalysisManager'
Check ([int]$mgrType.GetField('GroupSnapshotsCamera').GetValue($null) -eq 0 -and [int]$mgrType.GetField('GroupSnapshotsAll').GetValue($null) -eq 1) 'the choices of the snapshots of a group'
$c = [Activator]::CreateInstance($tCam); $c.Key = 'cam:gopro1'; $c.Name = 'GoPro1'; $c.Role = $Water
$settings.GroupCameras.Add($c); $settings.GroupSnapshots = 1
$back = $des.Invoke($null, [object[]]@($ser.Invoke($null, [object[]]@($settings)), $tSettings))
Check ($back.GroupSnapshots -eq 1 -and $back.GroupCameras.Count -eq 1 -and $back.GroupCameras[0].Key -eq 'cam:gopro1' -and $back.GroupCameras[0].Role -eq $Water) 'the cameras in the file of the settings'
$older = $des.Invoke($null, [object[]]@('{"GroupGraphics":2,"AutoTechSnapshots":true}', $tSettings))
Check ($older.GroupSnapshots -eq 0 -and $null -ne $older.GroupCameras -and $older.GroupCameras.Count -eq 0 -and $older.GroupGraphics -eq 2) 'older settings: the camera of the moment, no camera said'
$settings.GroupCameraRoles.Add($Above); $settings.GroupCameraRoles.Add($Water)
$back = $des.Invoke($null, [object[]]@($ser.Invoke($null, [object[]]@($settings)), $tSettings))
Check ((@($back.GroupCameraRoles) -join ',') -eq '1,2' -and $null -ne $older.GroupCameraRoles -and $older.GroupCameraRoles.Count -eq 0) 'the roles of the last time by screen, in the file of the settings'
$snap = [Activator]::CreateInstance($tSnap)
$text = [string]$ser.Invoke($null, [object[]]@($snap))
Check ($text -notmatch '"Camera"') 'a snapshot of one video or of all the screens: no camera in its file'
$snap.Camera = 'cam:gopro1'
$text = [string]$ser.Invoke($null, [object[]]@($snap))
$snapBack = $des.Invoke($null, [object[]]@($text, $tSnap))
Check ($text -match '"Camera":"cam:gopro1"' -and $snapBack.Camera -eq 'cam:gopro1') 'the camera of a snapshot in its file'
$oldSnap = $des.Invoke($null, [object[]]@('{"Id":"abc","Timestamp":120,"Event":"Αντίδραση"}', $tSnap))
Check ($null -eq $oldSnap.Camera -and $oldSnap.Id -eq 'abc') 'an older snapshot: no camera'

# ---- The window of the cameras.
$form = $null
try {
  $titles = [string[]]@('Οθόνη 1  ·  Κάμερα Α', 'Οθόνη 2  ·  Κάμερα Β', 'Οθόνη 3  ·  Κάμερα τοίχου')
  $form = [Activator]::CreateInstance($tForm, [object[]]@($titles, [int[]]@($Above, $None, $Extra)))
  $okButton = $tForm.GetField('ok', $all).GetValue($form)
  $combos = $tForm.GetField('combos', $all).GetValue($form)
  $hint = $tForm.GetField('hint', $all).GetValue($form)
  Check ((@($form.Roles) -join ',') -eq '1,0,3' -and -not $okButton.Enabled) ('a camera not chosen: no saving (' + (@($form.Roles) -join ',') + ')')
  $before = $hint.Text
  $combos[1].SelectedIndex = 1
  Check ((@($form.Roles) -join ',') -eq '1,2,3' -and $okButton.Enabled -and $hint.Text -ne $before -and $hint.Text.Length -gt 20) 'every camera chosen: saving, and the rule in words'
  Check ($combos[0].Items.Count -eq 3 -and $combos.Count -eq 3) 'three choices for each of the three cameras'
  $asked = [Activator]::CreateInstance($tForm, [object[]]@([string[]]@('Οθόνη 1  ·  Κάμερα Α', 'Οθόνη 2  ·  Κάμερα Β'), [int[]]@($Above, $Water), $true))
  try {
    $labels = @($asked.Controls[0].Controls | Where-Object { $_ -is [System.Windows.Forms.Label] })
    $plain = @($form.Controls[0].Controls | Where-Object { $_ -is [System.Windows.Forms.Label] })
    # The title, the words under it, one name for each camera, the rule; with proposals one more line asks to check them.
    Check ((@($asked.Roles) -join ',') -eq '1,2' -and $labels.Count -eq 6 -and $plain.Count -eq 6) ('proposals: the roles are chosen and the window asks to check them (' + $labels.Count + ' / ' + $plain.Count + ' lines)')
  } finally { $asked.Dispose() }
  if ($out) {
    try {
      $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000); $form.ShowInTaskbar = $false
      $form.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
      $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
      $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
      $b.Save([System.IO.Path]::Combine($out, 'group_cameras_window.png'), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
      Write-Output 'picture: group_cameras_window.png'
    } catch { Write-Output ('picture of the window not drawn: ' + $_.Exception.Message) }
  }
} catch {
  Check $false ('the window of the cameras: ' + $_.Exception.Message)
} finally {
  if ($null -ne $form) { try { $form.Close(); $form.Dispose() } catch { } }
}

if ($fails -gt 0) { Write-Output ('FAILED: ' + $fails); exit 1 }
Write-Output 'group cameras ok'
