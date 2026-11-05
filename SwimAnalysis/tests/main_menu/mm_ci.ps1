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
$help = Mnu 'mnuHelp' 'Help' @((Mnu 'mnuHelpContents' 'Help'), $updates, (Mnu 'mnuTranslate2' 'Translate Kinovea'), (Sep), (Mnu 'mnuApplicationFolder' 'Open application data folder'),
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
Check ($right.Count -eq 4 -and $right[0] -eq '⚙ Ρυθμίσεις' -and $right[1] -eq 'EN' -and $right[2] -eq 'ΕΛ' -and $right[3].Contains('Google Sheet')) ('on the right: ' + ($right -join ' | '))
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
  $form.Close()
  Write-Output ('pictures: ' + @(Get-ChildItem $out -Filter *.png).Count)
} catch { Write-Output ('note: the pictures were not drawn: ' + $_.Exception.Message) }

Write-Output ('main menu checks: ' + $fails + ' failed')
if ($fails -gt 0) { exit 1 }
exit 0
