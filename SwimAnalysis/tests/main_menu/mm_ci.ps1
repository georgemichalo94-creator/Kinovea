# mm_ci.ps1: the CI check of the main menu of Kinovea Swim (2.18.0) on a FICTIONAL menu laid out as Kinovea's (its items named as the
# fields of the program, with an unknown item in some menus): after the new menu nothing is missing, every item is in its new place,
# the unknown ones are at the end of the new menu of their old group, the names are Greek (and come back when the program sets its own),
# in English with the analysis, and a second layout changes nothing. The bar, the toolbar and every menu are drawn to -out (an
# artifact of the run). Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File mm_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
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

# ---- A fictional main window with the menus and the toolbar of Kinovea (its names, its order, its separators). (Not «MI»: an alias of Move-Item.)
function Mnu([string]$name, [string]$text, $children) {
  $m = New-Object System.Windows.Forms.ToolStripMenuItem
  $m.Name = $name; $m.Text = $text
  if ($children) { $m.DropDownItems.AddRange([System.Windows.Forms.ToolStripItem[]]@($children)) }
  return $m
}
function Sep() { return (New-Object System.Windows.Forms.ToolStripSeparator) }
$keys = [System.Windows.Forms.Keys]
$save = Mnu 'mnuSave' 'Save annotations'; $save.ShortcutKeys = $keys::Control -bor $keys::S
$undo = Mnu 'mnuUndo' 'Undo'; $undo.ShortcutKeys = $keys::Control -bor $keys::Z; $undo.Enabled = $false
$redo = Mnu 'mnuRedo' 'Redo'; $redo.ShortcutKeys = $keys::Control -bor $keys::Y; $redo.Enabled = $false
$recent = Mnu '' 'fictional_race_50fr.mp4'
$file = Mnu 'mnuFile' 'File' @(
  (Mnu 'mnuOpenFile' 'Open video'), (Mnu 'mnuOpenReplayWatcher' 'Open replay folder observer' @((Mnu 'mnuOpenReplayWatcherFolder' 'Open folder'))),
  (Mnu 'mnuHistory' 'Recent' @($recent, (Sep), (Mnu 'mnuHistoryReset' 'Reset history'))), (Sep),
  (Mnu 'mnuLoadAnnotations' 'Load annotations'), $save, (Mnu 'mnuSaveAs' 'Save annotations as'), (Mnu 'mnuUnloadAnnotations' 'Unload annotations'), (Sep),
  (Mnu 'mnuExportVideo' 'Export video' @((Mnu 'mnuExportVideoVideo' 'Video'), (Mnu 'mnuExportVideoSlideshow' 'Slideshow'), (Mnu 'mnuExportVideoWithPauses' 'With pauses'), (Mnu 'mnuExportVideoSideBySide' 'Side by side'))),
  (Mnu 'mnuExportImage' 'Export image' @((Mnu 'mnuExportImageImage' 'Image'), (Mnu 'mnuExportImageSequence' 'Sequence'), (Mnu 'mnuExportImageKeys' 'Key images'), (Mnu 'mnuExportImageSideBySide' 'Side by side'))),
  (Mnu 'mnuExportDocument' 'Export document' @((Mnu 'mnuExportODT' 'Writer'), (Mnu 'mnuExportDOCX' 'Word'), (Sep), (Mnu 'mnuExportMarkdown' 'Markdown'))),
  (Mnu 'mnuExportSpreadsheet' 'Export spreadsheet' @((Mnu 'mnuExportODS' 'Calc'), (Mnu 'mnuExportXLSX' 'Excel'), (Mnu 'mnuExportCSVTrajectory' 'Trajectory CSV'), (Mnu 'mnuExportTXTTrajectory' 'Trajectory text'), (Mnu 'mnuExportCSVChronometer' 'Chronometer CSV'), (Sep), (Mnu 'mnuExportJSON' 'JSON'))),
  (Sep), (Mnu 'mnuCloseFile' 'Close'), (Mnu 'mnuCloseFile2' 'Close'), (Sep), (Mnu 'mnuQuit' 'Quit'))
$edit = Mnu 'mnuEdit' 'Edit' @($undo, $redo, (Sep), (Mnu 'mnuCutDrawing' 'Cut'), (Mnu 'mnuCopyDrawing' 'Copy'), (Mnu 'mnuPasteDrawing' 'Paste'))
$view = Mnu 'mnuView' 'View' @((Mnu 'mnuToggleNavigationPane' 'Navigation pane'), (Mnu 'mnuFullScreen' 'Full screen'), (Sep),
  (Mnu 'mnuOnePlayer' 'One player'), (Mnu 'mnuTwoPlayers' 'Two players'), (Mnu 'mnuThreePlayers' 'Three'), (Mnu 'mnuFourPlayers' 'Four'), (Mnu 'mnuFivePlayers' 'Five'),
  (Mnu 'mnuOneCapture' 'One capture'), (Mnu 'mnuTwoCaptures' 'Two captures'), (Mnu 'mnuTwoMixed' 'Mixed'), (Sep),
  (Mnu 'mnuToggleCommonCtrls' 'Common controls'), (Mnu 'mnuVerticalLayout' 'Vertical layout'), (Mnu 'mnuSwapScreens' 'Swap screens'))
$mysteryImage = Mnu 'mnuMysteryImage' 'A tool of the image'
$image = Mnu 'mnuImage' 'Image' @((Mnu 'mnuAspectRatio' 'Aspect ratio' @((Mnu 'mnuAspectRatioAuto' 'Auto'), (Sep), (Mnu 'mnuAspectRatioForce43' '4:3'))), (Mnu 'mnuRotation' 'Rotation'),
  (Mnu 'mnuMirror' 'Mirror'), (Mnu 'mnuDeinterlace' 'Deinterlace'), (Mnu 'mnuDemosaic' 'Demosaicing' @((Mnu 'mnuDemosaicNone' 'None'))), (Mnu 'mnuStabilization' 'Stabilization'), $mysteryImage)
$filterType = $a.GetType('Kinovea.ScreenManager.VideoFilterType', $true)
$fNone = Mnu '' 'Analysis'; $fNone.Tag = [Enum]::Parse($filterType, 'None')
$fKino = Mnu '' 'Kinogram'; $fKino.Tag = [Enum]::Parse($filterType, 'Kinogram')
$fCam = Mnu '' 'Camera motion estimation'; $fCam.Tag = [Enum]::Parse($filterType, 'CameraMotion')
$video = Mnu 'mnuVideo' 'Video' @($fNone, (Sep), $fKino, $fCam)
$mysteryTool = Mnu 'mnuMysteryTool' 'A tool of a plugin'
$swimItem = Mnu 'mnuSwimAnalysis' 'Swim race analysis'
$tools = Mnu 'mnuTools' 'Tools' @((Mnu 'mnuImportImage' 'Import image'), (Mnu 'mnuBackground' 'Background'), (Mnu 'mnuTestGrid' 'Test grid'), (Sep),
  (Mnu 'mnuTimeCalibration' 'Time calibration'), (Mnu 'mnuCoordinateSystem' 'Coordinate system'), (Mnu 'mnuLensCalibration' 'Lens calibration' @((Mnu 'mnuLensCalibrationOpen' 'Import'), (Mnu 'mnuLensCalibrationNone' 'None'))),
  (Mnu 'mnuCalibrationValidation' 'Calibration validation'), (Sep), (Mnu 'mnuScatterDiagram' 'Scatter'), (Mnu 'mnuLinearKinematics' 'Linear'), (Mnu 'mnuAngularKinematics' 'Angular'),
  (Mnu 'mnuAngleAngleDiagram' 'Angle-angle'), (Sep), $swimItem, $mysteryTool)
$window = Mnu 'mnuWindow' 'Window' @((Mnu 'mnuWindowProperties' 'Window properties'), (Sep), (Mnu 'mnuOpenNewWindow' 'Open new window'), (Mnu 'mnuReopenWindow' 'Reopen window'),
  (Mnu 'mnuManageWindows' 'Manage windows'), (Sep), (Mnu 'mnuManageWorkspaces' 'Manage workspaces'))
$options = Mnu 'mnuOptions' 'Options' @((Mnu 'mnuLanguages' 'Language' @((Mnu '' 'English'), (Mnu '' 'Ελληνικά'), (Sep), (Mnu 'mnuTranslate1' 'Translate'))),
  (Mnu 'mnuTimecode' 'Time' @((Mnu 'mnuTimecodeClassic' 'Classic'), (Mnu 'mnuTimecodeFrames' 'Frames'))), (Mnu 'mnuPointer' 'Pointer'), (Sep),
  (Mnu 'mnuVariables' 'Context' @((Mnu 'mnuImportVariables' 'Import'))), (Sep), (Mnu 'mnuPreferences' 'Preferences'))
$updates = Mnu '' 'Check for updates'
$debug = Mnu 'mnuEnableDebugLogs' 'Enable debug logs'
$helpContents = Mnu 'mnuHelpContents' 'Help'; $helpContents.ShortcutKeys = $keys::F1
$help = Mnu 'mnuHelp' 'Help' @($helpContents, $updates, (Mnu 'mnuTranslate2' 'Translate Kinovea'), (Sep), (Mnu 'mnuApplicationFolder' 'Open application data folder'),
  $debug, (Sep), (Mnu 'mnuWebsite' 'www.kinovea.org'), (Mnu 'mnuAbout' 'About'))
$plugin = Mnu 'mnuPlugin' 'A menu of a plugin' @((Mnu '' 'Its command'))

$form = New-Object System.Windows.Forms.Form
$form.Font = New-Object System.Drawing.Font('Segoe UI', 9)
$strip = New-Object System.Windows.Forms.MenuStrip
$strip.BackColor = [System.Drawing.Color]::FromArgb(188, 199, 216)
$strip.Items.AddRange([System.Windows.Forms.ToolStripItem[]]@($file, $edit, $view, $image, $video, $tools, $window, $options, $help, $plugin))
$tool = New-Object System.Windows.Forms.ToolStrip
$tool.GripStyle = 'Hidden'
function TB([string]$name, [string]$tip) { $b = New-Object System.Windows.Forms.ToolStripButton; $b.Name = $name; $b.ToolTipText = $tip; $b.DisplayStyle = 'Image'
  $bmp = New-Object System.Drawing.Bitmap(16, 16); $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::FromArgb(120, 200, 255)); $g.Dispose(); $b.Image = $bmp; return $b }
$more = New-Object System.Windows.Forms.ToolStripDropDownButton; $more.Name = 'toolMorePlayers'; $more.DisplayStyle = 'Image'
$more.Image = (TB 'toolMoreIcon' '').Image
$more.DropDownItems.AddRange([System.Windows.Forms.ToolStripItem[]]@((Mnu 'toolThreePlayers' '3'), (Mnu 'toolFourPlayers' '4'), (Mnu 'toolFivePlayers' '5')))
$tool.Items.AddRange([System.Windows.Forms.ToolStripItem[]]@((TB 'toolOpenFile' 'Open'), (TB 'toolSave' 'Save'), (Sep), (TB 'toolToggleNavigationPane' 'Pane'), (Sep),
  (TB 'toolExplorer' 'Browser'), (TB 'toolOnePlayer' '1'), (TB 'toolTwoPlayers' '2'), $more, (TB 'toolOneCapture' 'C1'), (TB 'toolTwoCaptures' 'C2'), (TB 'toolTwoMixed' 'M'),
  (Sep), (TB 'toolFullScreen' 'Full')))
$form.Controls.Add($tool); $form.Controls.Add($strip); $form.MainMenuStrip = $strip

function Walk($items) { foreach ($i in $items) { if (-not ($i -is [System.Windows.Forms.ToolStripSeparator])) { $i; if ($i -is [System.Windows.Forms.ToolStripDropDownItem]) { Walk $i.DropDownItems } } } }
$before = @(Walk $strip.Items)
$beforeTool = @($tool.Items | ForEach-Object { $_ })
Write-Output ('fictional menu: ' + $before.Count + ' items, toolbar ' + $beforeTool.Count)

# ---- The menu of Kinovea Swim (a host that says what it was asked).
$tHost = T 'SwimMenuHost'; $tMenu = T 'SwimMainMenu'
$h = [Activator]::CreateInstance($tHost)
$script:asked = @()
$open = [Func[Nullable[bool], string, bool]] { param($technical, $page) $script:asked += ('' + $technical + '|' + $page); return $true }
$tHost.GetField('OpenAnalysis').SetValue($h, $open)
# (2.26.1) The analysis of the video of the active screen, for the buttons of the bars of the assistant: no video here.
$script:tookUp = 0
$tHost.GetField('TakeUpAnalysis').SetValue($h, [Func[bool]] { $script:tookUp++; return $false })
$tHost.GetField('BigScreensOn').SetValue($h, [Func[bool]] { return $false })
$tHost.GetField('CanBigScreens').SetValue($h, [Func[bool]] { return $true })
# Reflection gets the objects themselves, not their PowerShell wrappers (see gp_ci.ps1).
$menu = $tMenu.GetConstructors($all)[0].Invoke([object[]]@($strip.psobject.BaseObject, $h.psobject.BaseObject))
$tMenu.GetMethod('SetToolbar').Invoke($menu, [object[]]@($tool.psobject.BaseObject)) | Out-Null
$tMenu.GetMethod('Apply').Invoke($menu, $null) | Out-Null
$active = $tMenu.GetProperty('Active').GetValue($menu)
$lost = $tMenu.GetProperty('Lost', $all).GetValue($menu)
Check ($active -and $lost -eq 0) ('the new menu is on, nothing lost (' + $lost + ')')

$after = @(Walk $strip.Items)
$missing = @($before | Where-Object { $after -notcontains $_ })
Check ($missing.Count -eq 0) ('every item of the old menu is in the new one (' + $missing.Count + ' missing)')
$toolMissing = @($beforeTool | Where-Object { -not ($_ -is [System.Windows.Forms.ToolStripSeparator]) -and -not $tool.Items.Contains($_) })
Check ($toolMissing.Count -eq 0) 'every button of the toolbar stays'

function Top($item) { $t = $item; while ($t.OwnerItem -ne $null) { $t = $t.OwnerItem }; return $t }
function TextOf($item) { return $item.Text }
$visibleTop = @($strip.Items | Where-Object { $_.Available -and $_.Alignment -eq 'Left' } | ForEach-Object { $_.Text })
Check (($visibleTop -join ',') -eq 'Αρχείο,Επεξεργασία,Προβολή,Αγώνας,Τεχνική,Αναφορά,Εργαλεία,Βοήθεια,A menu of a plugin') ('the menus: ' + ($visibleTop -join ', '))
$right = @($strip.Items | Where-Object { $_.Alignment -eq 'Right' } | ForEach-Object { $_.Text })
Check ($right.Count -eq 5 -and $right[0] -eq '⚙ Ρυθμίσεις' -and $right[1] -eq 'EN' -and $right[2] -eq 'ΕΛ' -and $right[3].Contains('Google Sheet') -and $right[4] -eq 'ΒΟΗΘΟΣ') ('on the right: ' + ($right -join ' | '))
Check (-not $image.Available -and -not $video.Available -and -not $window.Available) 'Image, Video and Window are empty and hidden'

$race = $strip.Items | Where-Object { $_.Text -eq 'Αγώνας' }
$report = $strip.Items | Where-Object { $_.Text -eq 'Αναφορά' }
function Named($n) { return @($after | Where-Object { $_.Name -eq $n })[0] }
Check ((Named 'mnuSave').OwnerItem -eq $file -and (Named 'mnuSave').ShortcutKeys -eq ($keys::Control -bor $keys::S)) 'Save in Αρχείο, with its key'
Check ((Named 'mnuExportVideo').OwnerItem -eq $report -and (Named 'mnuExportSpreadsheet').OwnerItem -eq $report) 'the exports in Αναφορά'
Check ((Named 'mnuExportJSON').OwnerItem -eq (Named 'mnuExportSpreadsheet')) 'an export keeps its own items'
Check ((Named 'mnuAspectRatio').OwnerItem.OwnerItem -eq $view -and $mysteryImage.OwnerItem -eq (Named 'mnuAspectRatio').OwnerItem) 'the image items (the unknown one too) in Προβολή › Εικόνα'
Check ((Named 'mnuOnePlayer').OwnerItem.OwnerItem -eq $view) 'the screens in Προβολή › Οθόνες'
Check ($recent.OwnerItem -eq (Named 'mnuHistory') -and (Named 'mnuHistory').OwnerItem -eq $file) 'a recent file stays in Πρόσφατα'
Check ($file.DropDownItems[$file.DropDownItems.Count - 1] -eq (Named 'mnuQuit')) 'Έξοδος is last in Αρχείο'
Check ($mysteryTool.OwnerItem -eq $tools -and $tools.DropDownItems[$tools.DropDownItems.Count - 1] -eq $mysteryTool) 'an unknown tool at the end of Εργαλεία'
Check ($swimItem.OwnerItem -eq $tools) 'the swim analysis stays in Εργαλεία'
Check ($updates.OwnerItem -eq $help -and $debug.OwnerItem -eq $help -and @('Enable debug logs', 'Ενεργοποίηση καταγραφής (debug)') -contains $debug.Text) ('Βοήθεια keeps its items (the debug logs: ' + $debug.Text + ')')
Check ((Named 'mnuWindowProperties').OwnerItem.OwnerItem -eq $options) 'the windows of Kinovea in ⚙ Ρυθμίσεις › Παράθυρα'
Check ($plugin.Available -and $strip.Items.Contains($plugin) -and $plugin.DropDownItems.Count -eq 1) 'an unknown menu stays as it is'
Check ($undo.Text -eq 'Αναίρεση') ('undo: our word (' + $undo.Text + ')')
$close = Named 'mnuCloseFile'
$close.Text = 'Close (fictional_race.mp4)'
Check ($close.Text -eq 'Κλείσιμο (fictional_race.mp4)') ('closing keeps its file: ' + $close.Text)
Check ($fNone.Text -eq 'Κανονικό' -and $fKino.Text -eq 'Κινόγραμμα' -and $fCam.Text -eq 'Κίνηση κάμερας') ('the filters by their kind: ' + $fNone.Text + ', ' + $fKino.Text + ', ' + $fCam.Text)

# Names: Greek, again after the program sets its own, in English with the analysis.
Check ($file.Text -eq 'Αρχείο' -and (Named 'mnuSaveAs').Text -eq 'Αποθήκευση ως…') ('names: ' + $file.Text + ', ' + (Named 'mnuSaveAs').Text)
$file.Text = 'File'
Check ($file.Text -eq 'Αρχείο') 'the name comes back when Kinovea sets its own'
$lang = T 'SwimLang'
$lang.GetProperty('English').SetValue($null, $true)
Check ($file.Text -eq 'File' -and $report.Text -eq 'Report' -and (Named 'mnuQuit').Text -eq 'Exit' -and $race.Text -eq 'Race' -and $close.Text -eq 'Close (fictional_race.mp4)') ('in English: ' + $file.Text + ', ' + $report.Text + ', ' + (Named 'mnuQuit').Text + ', ' + $race.Text + ', ' + $close.Text)
$lang.GetProperty('English').SetValue($null, $false)
Check ($file.Text -eq 'Αρχείο' -and $report.Text -eq 'Αναφορά' -and $close.Text -eq 'Κλείσιμο (fictional_race.mp4)') 'back in Greek'

# A second layout (a change of language of Kinovea) changes nothing.
$count = $strip.Items.Count; $raceCount = $race.DropDownItems.Count; $toolCount = $tool.Items.Count
$tMenu.GetMethod('Apply').Invoke($menu, $null) | Out-Null
Check ($strip.Items.Count -eq $count -and $race.DropDownItems.Count -eq $raceCount -and $tool.Items.Count -eq $toolCount) 'a second layout adds nothing'

# The toolbar.
$names = @($tool.Items | ForEach-Object { if ($_ -is [System.Windows.Forms.ToolStripSeparator]) { '|' } else { $_.Text } })
Write-Output ('toolbar: ' + ($names -join ' '))
Check ($tool.Items[0].Text -eq 'Άνοιγμα' -and $tool.Items[1].Text -eq 'Αποθήκευση' -and $tool.Items[2].Text -eq '↶' -and $tool.Items[3].Text -eq '↷') 'Άνοιγμα, Αποθήκευση, ↶ ↷'
Check (($names -join ' ').Contains('ΟΘΟΝΕΣ') -and ($names -join ' ').Contains('ΑΝΑΛΥΣΗ Αγώνας Τεχνική') -and $names[$names.Count - 1] -eq '📦 Δημιουργία αποστολής') 'the parts of the toolbar'
Check (-not $tool.Items[2].Enabled) '↶ waits for something to undo'
$undo.Enabled = $true; $undo.Text = 'Undo : a fictional drawing'
Check ($tool.Items[2].Enabled -and $undo.Text -eq 'Αναίρεση : a fictional drawing' -and $tool.Items[2].ToolTipText -eq 'Αναίρεση : a fictional drawing (Ctrl+Z)') ('↶ follows the menu: ' + $tool.Items[2].ToolTipText)

# (2.26.2, the test copy: the tabs of the list on the left were not in sight) «Κάμερες» after the buttons of the screens, and
# Προβολή › Λίστα καμερών after the file list: the list on the left at its tab of the cameras in one click.
$camBar = @($tool.Items | Where-Object { $_.Name -eq 'swimCamerasBar' })[0]
$camMenu = Named 'swimCameras'
$camAt = $tool.Items.IndexOf($camBar)
Check ($null -ne $camBar -and $camBar.Text -eq 'Κάμερες' -and $null -ne $camBar.Image -and $camBar.DisplayStyle -eq 'ImageAndText' -and $camAt -gt $tool.Items.IndexOf(@($tool.Items | Where-Object { $_.Name -eq 'toolFullScreen' })[0]) -and $camAt -lt [array]::IndexOf($names, 'ΑΝΑΛΥΣΗ') -and $camBar.ToolTipText.Contains('διπλό κλικ σε μια κάμερα')) ('«Κάμερες» on the toolbar, after the screens (' + $camAt + ')')
Check ($null -ne $camMenu -and $camMenu.OwnerItem -eq $view -and $camMenu.Text -eq 'Λίστα καμερών' -and $view.DropDownItems.IndexOf($camMenu) -eq $view.DropDownItems.IndexOf((Named 'mnuToggleNavigationPane')) + 1) 'Προβολή › Λίστα καμερών after Λίστα αρχείων'
$svc = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll'))
$tabField = $svc.GetType('Kinovea.Services.NotificationCenter', $true).GetField('BrowserContentTypeChanged')
$script:tabAsked = @()
$tabHandler = [System.Management.Automation.LanguagePrimitives]::ConvertTo([scriptblock]{ param($s, $e) $script:tabAsked += [string]$e.Value }, $tabField.FieldType)
$tabField.SetValue($null, [System.Delegate]::Combine($tabField.GetValue($null), $tabHandler))
$camBar.PerformClick()
$camMenu.PerformClick()
$tabField.SetValue($null, [System.Delegate]::Remove($tabField.GetValue($null), $tabHandler))
Check (($script:tabAsked -join ',') -eq 'Cameras,Cameras') ('the list asked at its cameras: ' + ($script:tabAsked -join ','))

# The pages of the window from the menus.
$infoItem = @($race.DropDownItems | Where-Object { $_.Text -eq 'Στοιχεία και αποτελέσματα' })[0]
$infoItem.PerformClick()
$settings = $strip.Items | Where-Object { $_.Text -eq '⚙ Ρυθμίσεις' }
$connection = @($settings.DropDownItems | Where-Object { $_.Text -eq 'Σύνδεση με Google Sheet…' })[0]
$connection.PerformClick()
$tech = $strip.Items | Where-Object { $_.Text -eq 'Τεχνική' }
@($tech.DropDownItems | Where-Object { $_.Text -eq 'Αξιολόγηση' })[0].PerformClick()
Check (($script:asked -join ' ; ') -eq 'False|race ; |connection ; True|tech-eval') ('the window asked at its pages: ' + ($script:asked -join ' ; '))

# ---- Pictures of the bar and of every menu (for a look; not a check).
try {
  $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(0, 0); $form.Size = New-Object System.Drawing.Size(1600, 500)
  $form.ShowInTaskbar = $false
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  $w = $strip.Width; $hh = $strip.Height + $tool.Height
  $bmp = New-Object System.Drawing.Bitmap($w, $hh)
  $strip.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $strip.Width, $strip.Height)))
  $tb = New-Object System.Drawing.Bitmap($tool.Width, $tool.Height)
  $tool.DrawToBitmap($tb, (New-Object System.Drawing.Rectangle(0, 0, $tool.Width, $tool.Height)))
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.DrawImage($tb, 0, $strip.Height); $g.Dispose()
  $bmp.Save([System.IO.Path]::Combine($out, '0_bar.png'), [System.Drawing.Imaging.ImageFormat]::Png)
  $k = 1
  foreach ($m in @($strip.Items | Where-Object { $_.Available -and $_ -is [System.Windows.Forms.ToolStripMenuItem] -and $_.HasDropDownItems })) {
    $m.ShowDropDown(); [System.Windows.Forms.Application]::DoEvents()
    $dd = $m.DropDown
    $b = New-Object System.Drawing.Bitmap($dd.Width, $dd.Height)
    $dd.DrawToBitmap($b, (New-Object System.Drawing.Rectangle(0, 0, $dd.Width, $dd.Height)))
    $b.Save([System.IO.Path]::Combine($out, ('{0}_{1}.png' -f $k, $m.Name)), [System.Drawing.Imaging.ImageFormat]::Png)
    # Its groups too.
    foreach ($s in @($dd.Items | Where-Object { $_ -is [System.Windows.Forms.ToolStripMenuItem] -and $_.HasDropDownItems -and $_.Available })) {
      $s.ShowDropDown(); [System.Windows.Forms.Application]::DoEvents()
      $sd = $s.DropDown
      $sb = New-Object System.Drawing.Bitmap($sd.Width, $sd.Height)
      $sd.DrawToBitmap($sb, (New-Object System.Drawing.Rectangle(0, 0, $sd.Width, $sd.Height)))
      $safe = ($s.Name, ('g' + $dd.Items.IndexOf($s)))[[string]::IsNullOrEmpty($s.Name)]
      $sb.Save([System.IO.Path]::Combine($out, ('{0}_{1}_{2}.png' -f $k, $m.Name, $safe)), [System.Drawing.Imaging.ImageFormat]::Png)
      $s.HideDropDown()
    }
    $m.HideDropDown(); $k++
  }
  Write-Output ('pictures: ' + @(Get-ChildItem $out -Filter *.png).Count)
} catch { Write-Output ('note: the pictures were not drawn: ' + $_.Exception.Message) }

# ---- (2.26.0) The digital assistant on the fictional menu: its place on the bar and first in Βοήθεια (F1, taken from the guide of
# Kinovea), its panel on the right of the window between the bars, the topics of questions (Greek with and without accents, capitals,
# a wrong letter, English), an item of the menu found by its name, nothing found, «Δείξε μου» (the menus opened, the item lit with ΕΔΩ,
# the light gone with its menu), «Άνοιξέ το», F1, the full screen, English, and what Claude would get (the menu without names of files).
# Pictures of the window with the panel to -out.
$tPanel = T 'SwimAssistantPanel'; $tAi = T 'SwimAssistantAi'; $tHostI = T 'ISwimAssistantHost'; $tIcon = T 'SwimAssistantIcon'
# (The values as they are: a list is not unrolled into the pipeline.)
function F($o, $n) { if ($null -eq $o) { return $null }; $b = $o.psobject.BaseObject; return ,($b.GetType().GetField($n, $all).GetValue($b)) }
function Pr($o, $n) { if ($null -eq $o) { return $null }; $b = $o.psobject.BaseObject; return ,($b.GetType().GetProperty($n, $all).GetValue($b)) }
function Call($o, $n, $params) { $b = $o.psobject.BaseObject; return ,($b.GetType().GetMethod($n, $all).Invoke($b, $params)) }
function Pump() { for ($i = 0; $i -lt 4; $i++) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 30 } }
function Shot([string]$name, $drops) {
  try {
    Pump
    $bmp = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
    $form.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, $form.Width, $form.Height)))
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    foreach ($dd in @($drops)) {
      if ($null -eq $dd -or -not $dd.Visible) { continue }
      $b = New-Object System.Drawing.Bitmap($dd.Width, $dd.Height)
      $dd.DrawToBitmap($b, (New-Object System.Drawing.Rectangle(0, 0, $dd.Width, $dd.Height)))
      $g.DrawImage($b, $dd.Left - $form.Left, $dd.Top - $form.Top); $b.Dispose()
    }
    $g.Dispose()
    $bmp.Save([System.IO.Path]::Combine($out, $name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  } catch { Write-Output ('note: no picture ' + $name + ': ' + $_.Exception.Message) }
}

$bar = @($strip.Items | Where-Object { $_.Name -eq 'swimAssistantBar' })[0]
$entry = @($help.DropDownItems | Where-Object { $_.Name -eq 'swimAssistant' })[0]
Check ($null -ne $bar -and $bar.Alignment -eq 'Right' -and $bar.Text -eq 'ΒΟΗΘΟΣ' -and [bool](F $bar 'Fresh')) ('ΒΟΗΘΟΣ on the bar with its new dot: ' + $bar.Text)
Check ($null -ne $entry -and $help.DropDownItems.IndexOf($entry) -eq 0 -and $entry.ShortcutKeys -eq $keys::F1 -and $entry.Text -eq 'Ψηφιακός βοηθός') 'Βοήθεια › Ψηφιακός βοηθός first, with F1'
Check ($helpContents.ShortcutKeys -eq $keys::None -and $helpContents.OwnerItem -eq $help) 'the guide of Kinovea stays in Βοήθεια, its F1 given to the assistant'
# (Found in the test copy of Kinovea: F1 did nothing, taken out of the keys of Βοήθεια when the guide was given no key after the entry.)
$keyTable = [System.Windows.Forms.ToolStrip].GetProperty('Shortcuts', $all).GetValue($help.DropDown.psobject.BaseObject)
Check ($null -ne $keyTable -and $keyTable[$keys::F1] -eq $entry) 'F1 in the keys of Βοήθεια is the assistant''s'

# The window as Kinovea's: a main view that fills what the bars leave.
$form.FormBorderStyle = 'None'
$form.Size = New-Object System.Drawing.Size(1440, 860)
$mainView = New-Object System.Windows.Forms.Panel; $mainView.Dock = 'Fill'; $mainView.BackColor = [System.Drawing.Color]::FromArgb(8, 40, 56)
$form.Controls.Add($mainView); $mainView.BringToFront()
if (-not $form.Visible) { $form.Show() }
Pump
[void]$tMenu.GetMethod('OpenAssistant', $all).Invoke($menu, $null)
Pump
$panel = $tMenu.GetProperty('Assistant', $all).GetValue($menu)
$pc = [System.Windows.Forms.Control]$panel
Check ($null -ne $panel -and $pc.Visible -and $pc.Dock -eq 'Right' -and $form.Controls.GetChildIndex($pc) -eq 1) ('the panel on the right, docked after the bars (index ' + $form.Controls.GetChildIndex($pc) + ')')
Check ($pc.Top -ge $tool.Bottom - 1 -and $pc.Right -eq $form.ClientSize.Width -and $mainView.Right -le $pc.Left + 1) ('under the bars, the main view makes room: panel ' + $pc.Bounds + ', view ' + $mainView.Bounds)
# (2.26.2, the test copy) A bar laid out after the main view covers its top (the tabs of the list on the left, «Κάμερες»): the view
# put first again, the panel next to it just after it, the window laid out again; then nothing more to do.
$tool.BringToFront()
$coveredBefore = $mainView.Top -lt $tool.Bottom
$keep = $tMenu.GetMethod('KeepWorkSpaceUnderBars', $all)
$done = [string]$keep.Invoke($menu, [object[]]@('test'))
Pump
Check ($coveredBefore -and $done -eq 'order+layout' -and $form.Controls.GetChildIndex($mainView) -eq 0 -and $form.Controls.GetChildIndex($pc) -eq 1 -and $mainView.Top -ge $tool.Bottom -and $pc.Top -ge $tool.Bottom - 1) ('the work space under the bars again (' + $done + '): view ' + $mainView.Bounds + ', bar ' + $tool.Bounds)
$again = $keep.Invoke($menu, [object[]]@('test'))
Check ($null -eq $again) ('nothing more to do (' + $again + ')')
Check ([bool](F $bar 'Open') -and -not [bool](F $bar 'Fresh')) 'ΒΟΗΘΟΣ open, its new dot gone'
Shot 'as_1_welcome.png' @()

function Ask([string]$q) { [void](Call $panel 'Ask' ([object[]]@($q))); Pump; return (Pr $panel 'Result') }
function TopicId($r) { $t = F $r 'Topic'; if ($null -eq $t) { return '' }; return [string](F $t 'Id') }
$expect = [ordered]@{
  'πού είναι η εξαγωγή βίντεο;' = 'exportvideo'; 'πως στελνω το βιντεο στον προπονητη' = 'package'; 'συγχρονισμός καμερών' = 'sync'
  'ΣΥΓΧΡΟΝΙΣΜΟΣ ΚΑΜΕΡΩΝ' = 'sync'; 'google sheet σύνδεση' = 'connection'; 'πλήκτρα' = 'keys'; 'export video' = 'exportvideo'
  'full screen' = 'fullscreen'; 'εξαγογη βιντεο' = 'exportvideo'; 'PDF' = 'pdf'; 'νέος αθλητής στο ίδιο βίντεο' = 'newrace'
  'γλώσσα αγγλικά' = 'language'; 'πώς ανοίγω ένα βίντεο' = 'open'; 'αποθήκευση' = 'save'; 'κινόγραμμα' = 'filters'; 'καθρέφτης' = 'image'
  'στροφές' = 'turns'; 'βίντεο δίπλα δίπλα' = 'sidebyside'; 'πού βρίσκω το κλασικό μενού' = 'classic'; 'how do I send to the coaches' = 'package'
  'λίστα καμερών' = 'cameralist'; 'κάμερα baumer δικτύου' = 'cameralist'
}
$wrong = @()
foreach ($q in $expect.Keys) { $id = TopicId (Ask $q); if ($id -ne $expect[$q]) { $wrong += ($q + ' -> ' + $id + ' (not ' + $expect[$q] + ')') } }
Check ($wrong.Count -eq 0) ('the topics of ' + $expect.Count + ' questions' + $(if ($wrong.Count -gt 0) { ': ' + ($wrong -join ' | ') } else { '' }))

$r = Ask 'σειρά εικόνων'
$entries = F $r 'Entries'
$first = $null; if ($null -ne $entries -and $entries.Count -gt 0) { $first = $entries[0] }
Check ($null -ne $first -and (F $first 'Name') -eq 'mnuExportImageSequence' -and (F $first 'Path') -eq 'Αναφορά › Εικόνες › Σειρά εικόνων…') ('an item of the menu found by its name: ' + (F $first 'Path'))
Shot 'as_7_menu_items.png' @()
$r = Ask 'ζουμ στο νερό'
Check ((TopicId $r) -eq '' -and (F $r 'Entries').Count -eq 0 -and (F $r 'Guesses').Count -eq 3) ('nothing for «ζουμ στο νερό»: ' + (F $r 'Guesses').Count + ' guesses')
Shot 'as_4_nothing.png' @()
[void](Ask 'πώς στέλνω το βίντεο στον προπονητή;')
Shot 'as_2_package.png' @()
[void](Ask 'πλήκτρα')
Shot 'as_5_keys.png' @()

# (2.26.1, the recording of the user) Scrolled, the texts move with their boxes: 2.26.0 drew the boxes through a transform of the
# Graphics that the texts of TextRenderer do not follow (the keys and the buttons left behind, black boxes over the chips).
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
namespace SwimCi {
  public static class Images {
    // The points of b that differ from a moved up by shift (one in two, the right edge left out: the scroll bar): {differ, compared}.
    public static int[] Diff(System.Drawing.Bitmap a, System.Drawing.Bitmap b, int shift, int right) {
      int diff = 0, total = 0;
      for (int y = 0; y + shift < a.Height && y < b.Height; y += 2)
        for (int x = 0; x < a.Width - right && x < b.Width; x += 2) { total++; if (a.GetPixel(x, y + shift).ToArgb() != b.GetPixel(x, y).ToArgb()) diff++; }
      return new int[] { diff, total };
    }
  }
}
'@
$view = [System.Windows.Forms.Control](F $panel 'view')
$vw = $view.Width; $vh = $view.Height
$b0 = New-Object System.Drawing.Bitmap($vw, $vh); $view.DrawToBitmap($b0, (New-Object System.Drawing.Rectangle(0, 0, $vw, $vh)))
[void](Call $view 'ScrollBy' ([object[]]@([int]120))); Pump
$sc1 = [int](F $view 'scroll')
$b1 = New-Object System.Drawing.Bitmap($vw, $vh); $view.DrawToBitmap($b1, (New-Object System.Drawing.Rectangle(0, 0, $vw, $vh)))
$d = [SwimCi.Images]::Diff($b0, $b1, $sc1, 16)
Check ($sc1 -gt 40 -and $d[1] -gt 1000 -and $d[0] -le $d[1] / 500) ('scrolled by ' + $sc1 + ' px: the texts move with their boxes (' + $d[0] + ' of ' + $d[1] + ' points differ)')
Shot 'as_10_keys_scrolled.png' @()
$b0.Dispose(); $b1.Dispose()

# (2.26.1) The automatic readings, buttons of the bars next to the video (not in the menu): found with «αυτόματη ανάλυση»; between
# the race and the turn of a training video the video open decides.
$expect2 = [ordered]@{
  'πως κάνω αυτόματη ανάλυση τεχνικής' = 'techauto'; 'αυτόματη ανάλυση αγώνα' = 'autopilot'; 'αυτόματος πιλότος' = 'autopilot'
  'όλος ο αγώνας' = 'wholerace'; 'εκκίνηση από το φως' = 'startlight'; 'σκελετός' = 'skeleton'; 'automatic technique analysis' = 'techauto'
}
$wrong = @()
foreach ($q in $expect2.Keys) { $id = TopicId (Ask $q); if ($id -ne $expect2[$q]) { $wrong += ($q + ' -> ' + $id + ' (not ' + $expect2[$q] + ')') } }
Check ($wrong.Count -eq 0) ('the automatic readings: ' + $expect2.Count + ' questions' + $(if ($wrong.Count -gt 0) { ': ' + ($wrong -join ' | ') } else { '' }))
$find5 = (T 'SwimAssistantSearch').GetMethods($all) | Where-Object { $_.Name -eq 'Find' -and $_.GetParameters().Count -eq 5 } | Select-Object -First 1
$topicsNow = Call $panel 'Topics' $null
$mapNow = $tHostI.GetMethod('MenuMap').Invoke($menu, $null)
function FindIn([string]$q, [string]$video) { return TopicId ($find5.Invoke($null, [object[]]@($q, $topicsNow, $mapNow, $video, $false))) }
$inTraining = FindIn 'πως κάνω αυτόματη ανάλυση' 'training'; $inRace = FindIn 'πως κάνω αυτόματη ανάλυση' 'race'
$plainRace = FindIn 'ανάλυση αγώνα' 'race'; $plainTech = FindIn 'τεχνική ανάλυση' 'training'
Check ($inTraining -eq 'techauto' -and $inRace -eq 'autopilot' -and $plainRace -eq 'racepanel' -and $plainTech -eq 'techpanel') ('«αυτόματη ανάλυση» in a training video ' + $inTraining + ', in a race ' + $inRace + '; «ανάλυση αγώνα» ' + $plainRace + ', «τεχνική ανάλυση» ' + $plainTech)
$r = Ask 'πως κάνω αυτόματη ανάλυση τεχνικής'
$auto = F $r 'Topic'
$place = [string](Call $panel 'X' ([object[]]@([string](Pr $auto 'PlaceText'), $auto)))
$hotIds = @((F $view 'hots') | ForEach-Object { [string](F $_ 'Id') })
Check ($place -eq 'Μπάρα αριστερά του βίντεο › 🤖 Αυτόματα' -and $hotIds -contains 'show' -and $hotIds -contains 'open' -and [string](Pr $auto 'RunText') -eq 'Ξεκίνα το') ('its card: where it is (' + $place + '), «Δείξε μου» and «Ξεκίνα το»')
Shot 'as_9_auto.png' @()
[void](Call $panel 'ShowTopic' $null); Pump
$warn1 = [string](Pr $panel 'Warning')
[void](Call $panel 'OpenTopic' $null); Pump
$warn2 = [string](Pr $panel 'Warning')
Check ($warn1.Contains('άνοιξε πρώτα ένα βίντεο') -and $warn1.Contains('Αρχείο') -and $warn2 -eq $warn1 -and $script:tookUp -eq 2 -and $null -eq $tMenu.GetProperty('Spotted', $all).GetValue($menu)) ('without a video, «Δείξε μου» and «Ξεκίνα το» look for the analysis of the active screen (' + $script:tookUp + ' times) and say what it needs: ' + $warn1)
Shot 'as_11_auto_needs.png' @()
# (2.26.1, the test copy) Under the buttons of a long card the warning was below the bottom of the panel, unseen: now in sight.
Pump
$noteBottom = [int](F $view 'noteBottom'); $noteScroll = [int](F $view 'scroll')
Check ($noteBottom -gt 0 -and $noteBottom -le $view.Height -and $noteScroll -gt 0) ('the warning in sight: the card scrolled by ' + $noteScroll + ' px, the warning ends at ' + $noteBottom + ' of ' + $view.Height + ' px')

# (2.26.1) The bars do not fit next to the video (two or more screens, the assistant open): «Δείξε μου» lights the button in the
# window of the analysis, a light that breathes in the button until a click or for a few seconds.
$tBtn = T 'SwimButton'
$lone = New-Object System.Windows.Forms.Form
$lone.ShowInTaskbar = $false; $lone.StartPosition = 'Manual'; $lone.Location = New-Object System.Drawing.Point(-2000, -2000)
$btn = [System.Windows.Forms.Button]([Activator]::CreateInstance((T 'SwimNoFocusButton')))
$btn.Text = '✈ Αυτόματος πιλότος'; $btn.Size = New-Object System.Drawing.Size(180, 30)
$lone.Controls.Add($btn); $lone.Show(); Pump
function ButtonShot($b) { $bm = New-Object System.Drawing.Bitmap($b.Width, $b.Height); $b.DrawToBitmap($bm, (New-Object System.Drawing.Rectangle(0, 0, $b.Width, $b.Height))); return $bm }
$p0 = ButtonShot $btn
[void]$tBtn.GetMethod('Spot').Invoke($btn, [object[]]@([int]10))
$lit = [bool]$tBtn.GetProperty('Spotted').GetValue($btn)
$p1 = ButtonShot $btn
$p1.Save([System.IO.Path]::Combine($out, 'as_12_window_button_lit.png'), [System.Drawing.Imaging.ImageFormat]::Png)
[void]$tBtn.GetMethod('EndSpot').Invoke($btn, $null)
$p2 = ButtonShot $btn
$d01 = [SwimCi.Images]::Diff($p0, $p1, 0, 0); $d02 = [SwimCi.Images]::Diff($p0, $p2, 0, 0)
Check ($lit -and $d01[0] -gt $d01[1] / 4 -and $d02[0] -eq 0 -and -not [bool]$tBtn.GetProperty('Spotted').GetValue($btn)) ('a button of the window lit by «Δείξε μου» (' + $d01[0] + ' of ' + $d01[1] + ' points), then as before (' + $d02[0] + ')')
$p0.Dispose(); $p1.Dispose(); $p2.Dispose(); $lone.Close(); $lone.Dispose()
$formSpot = (T 'FormSwimAnalysis').GetMethod('SpotButton', $all)
Check ($null -ne $formSpot -and $formSpot.GetParameters().Count -eq 2) 'the window of the analysis lights its buttons of the automatic readings (SpotButton)'

# «Δείξε μου»: the menus of the item opened from the bar, the item lit with ΕΔΩ; the light goes with its menu.
[void](Ask 'πού είναι η εξαγωγή βίντεο;')
[void](Call $panel 'ShowTopic' $null)
Pump
$videoGroup = Named 'mnuExportVideo'; $videoItem = Named 'mnuExportVideoVideo'
$spotted = $tMenu.GetProperty('Spotted', $all).GetValue($menu)
Check ($spotted -eq $videoItem -and $report.DropDown.Visible -and $videoGroup.DropDown.Visible -and $videoItem.ShortcutKeyDisplayString -eq 'ΕΔΩ') ('Δείξε μου: Αναφορά › Βίντεο opened, Βίντεο… lit (' + $videoItem.ShortcutKeyDisplayString + ')')
Shot 'as_3_show_export.png' @($report.DropDown, $videoGroup.DropDown)
$report.HideDropDown()
Pump
Check ($null -eq $tMenu.GetProperty('Spotted', $all).GetValue($menu) -and [string]::IsNullOrEmpty($videoItem.ShortcutKeyDisplayString)) 'the light goes when its menu closes (its keys as before)'

[void](Ask 'σύνδεση με google sheet')
[void](Call $panel 'ShowTopic' $null)
Pump
$conn = Named 'swimConnection'
Check ($tMenu.GetProperty('Spotted', $all).GetValue($menu) -eq $conn -and $settings.DropDown.Visible) 'Δείξε μου in ⚙ Ρυθμίσεις, on the right of the bar'
Shot 'as_8_show_settings.png' @($settings.DropDown)
$settings.HideDropDown()
Pump
$asked0 = $script:asked.Count
[void](Call $panel 'OpenTopic' $null)
Pump
$note = [string](Pr $panel 'Note')
Check ($script:asked.Count -eq $asked0 + 1 -and $script:asked[$script:asked.Count - 1] -eq '|connection' -and $note.Contains('Σύνδεση με Google Sheet')) ('Άνοιξέ το: the page of the connection asked (' + $note + ')')

# F1 and the full screen.
$entry.PerformClick(); Pump
$closed = -not $pc.Visible -and -not [bool](F $bar 'Open')
$entry.PerformClick(); Pump
Check ($closed -and $pc.Visible) 'F1 (Βοήθεια › Ψηφιακός βοηθός) closes and opens it'
# F1 pressed as in Kinovea (the key into the queue of the window, through the keys of the menus): in the main view, and in the box of
# the question; then ΒΟΗΘΟΣ on the bar.
Add-Type -Namespace SwimCi -Name Keyboard -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr hWnd, int msg, System.IntPtr wParam, System.IntPtr lParam);'
function PressF1($c) { [void][SwimCi.Keyboard]::PostMessage($c.Handle, 0x100, [IntPtr]0x70, [IntPtr]0x3B0001); Pump }
$box = [System.Windows.Forms.Control](F (F $panel 'query') 'Box')
PressF1 $mainView; $k1 = -not $pc.Visible
PressF1 $mainView; $k2 = $pc.Visible
PressF1 $box; $k3 = -not $pc.Visible
Check ($k1 -and $k2 -and $k3) ('the key F1 closes and opens it, in the box of the question too (' + $k1 + ', ' + $k2 + ', ' + $k3 + ')')
$bar.PerformClick(); Pump; $b1 = $pc.Visible -and [bool](F $bar 'Open')
$bar.PerformClick(); Pump; $b2 = -not $pc.Visible -and -not [bool](F $bar 'Open')
$bar.PerformClick(); Pump
Check ($b1 -and $b2 -and $pc.Visible) ('ΒΟΗΘΟΣ on the bar opens and closes it (' + $b1 + ', ' + $b2 + ')')
[void]$tMenu.GetMethod('SetFullScreen').Invoke($menu, [object[]]@($true)); $hidden = -not $pc.Visible
[void]$tMenu.GetMethod('SetFullScreen').Invoke($menu, [object[]]@($false))
Check ($hidden -and $pc.Visible) 'the full screen hides the panel, it comes back after it'

# What Claude would get with «Ρώτα το AI»: the menu (no recent file, no name of a file of a screen, no drawing), the topics, the keys.
$map = $tHostI.GetMethod('MenuMap').Invoke($menu, $null)
$topics = Call $panel 'Topics' $null
$pathOf = [Delegate]::CreateDelegate([Func[string, string]], $menu.psobject.BaseObject, $tHostI.GetMethod('PathOf'))
$keyOf = [Delegate]::CreateDelegate([Func[string, string]], $menu.psobject.BaseObject, $tHostI.GetMethod('ShortcutOf'))
$system = [string]$tAi.GetMethod('SystemPrompt', $all).Invoke($null, [object[]]@($map, $topics, $pathOf, $keyOf))
Check ($system.Contains('Αναφορά › Βίντεο › Βίντεο…') -and $system.Contains('Αρχείο › Κλείσιμο') -and -not $system.Contains('fictional_race') -and -not $system.Contains('a fictional drawing')) ('what Claude would get: the menu without names of files (' + $system.Length + ' characters)')
try { [System.IO.File]::WriteAllText([System.IO.Path]::Combine($out, 'as_claude_system.txt'), $system, (New-Object System.Text.UTF8Encoding($false))) } catch { }

# In English.
$lang.GetProperty('English').SetValue($null, $true); Pump
$idEn = TopicId (Ask 'export a video')
$pathEn = [string]$tHostI.GetMethod('PathOf').Invoke($menu, [object[]]@('mnuExportVideoVideo'))
Check ($bar.Text -eq 'ASSISTANT' -and $idEn -eq 'exportvideo' -and $pathEn -eq 'Report › Video › Video…') ('in English: ' + $bar.Text + ', ' + $idEn + ', ' + $pathEn)
Shot 'as_6_english.png' @()
$lang.GetProperty('English').SetValue($null, $false); Pump

# The bar with the assistant open, closed and new (the bar wider than this screen, so that all of its right side is in the picture),
# and the sign at its sizes.
function Bar([string]$name) {
  try {
    $strip.Dock = 'None'; $strip.AutoSize = $false; $strip.Width = 1600; $strip.PerformLayout(); Pump
    $bb = New-Object System.Drawing.Bitmap($strip.Width, $strip.Height)
    $strip.DrawToBitmap($bb, (New-Object System.Drawing.Rectangle(0, 0, $strip.Width, $strip.Height)))
    $bb.Save([System.IO.Path]::Combine($out, $name), [System.Drawing.Imaging.ImageFormat]::Png); $bb.Dispose()
  } catch { Write-Output ('note: no picture ' + $name + ': ' + $_.Exception.Message) }
  finally { $strip.AutoSize = $true; $strip.Dock = 'Top' }
}
Bar 'as_0_bar_open.png'
$entry.PerformClick(); Pump
Bar 'as_0_bar_closed.png'
$tA = T 'SwimMenuAssistant'
$tA.GetField('Fresh').SetValue($bar.psobject.BaseObject, $true); $bar.Invalidate()
Bar 'as_0_bar_new.png'
$tA.GetField('Fresh').SetValue($bar.psobject.BaseObject, $false)
try {
  $sheet = New-Object System.Drawing.Bitmap(420, 120)
  $g = [System.Drawing.Graphics]::FromImage($sheet); $g.Clear([System.Drawing.Color]::FromArgb(3, 14, 23))
  $draw = $tIcon.GetMethod('Draw', $all); $x = 10
  foreach ($s in @(96, 64, 40, 32, 22, 16)) {
    $rect = (New-Object System.Drawing.RectangleF([single]$x, [single]10, [single]$s, [single]$s)).psobject.BaseObject
    $dot = ([System.Drawing.Color]::FromArgb(201, 251, 255)).psobject.BaseObject
    [void]$draw.Invoke($null, [object[]]@($g.psobject.BaseObject, $rect, [single]0, $dot, [bool]($s -ge 40), [single]0.6))
    $x += $s + 12
  }
  $g.Dispose()
  $sheet.Save([System.IO.Path]::Combine($out, 'as_0_sign.png'), [System.Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
} catch { Write-Output ('note: no picture of the sign: ' + $_.Exception.Message) }
try { $form.Close() } catch { }

Write-Output ('main menu checks: ' + $fails + ' failed')
if ($fails -gt 0) { exit 1 }
exit 0
