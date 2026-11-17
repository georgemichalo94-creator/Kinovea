# pb_ci.ps1: the CI check of the package finished in the background (2.25.0), on FICTIONAL data. What the analysis of a package in the
# background changed goes to the analysis of the screens value by value, a value the analyst changed meanwhile staying his (the changes
# of two JSON, applied on a third, then set on the objects); the window of the package: «Συνέχισε με άλλον αθλητή» leaves it while its
# video goes on (ShowDialog returns Ignore), shown again it only hides while the video is made, the end said once; one video at a time
# (the second waits for the first, then starts by itself); the box of the packages (a package being made, one waiting, one ready with its
# notice) as a picture in -out; the English of the new texts. Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a
# check fails.
#   powershell -File pb_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
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
$nj = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll'))
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function B($o) { if ($null -eq $o) { return $null }; return $o.psobject.BaseObject }
function F($o, $n) { return ,($o.GetType().GetField($n, $all).GetValue((B $o))) }
function SetF($o, $n, $v) { $o.GetType().GetField($n, $all).SetValue((B $o), (B $v)) }
function Pump($seconds, $until) {
  $end = (Get-Date).AddSeconds($seconds)
  while ((Get-Date) -lt $end) {
    [System.Windows.Forms.Application]::DoEvents()
    if ($until -and (& $until)) { break }
    Start-Sleep -Milliseconds 30
  }
}
function Shot($form, $name) {
  $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
  $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
  $b.Save([System.IO.Path]::Combine($out, $name), [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

$tMgr = T 'SwimAnalysisManager'
$jObject = $nj.GetType('Newtonsoft.Json.Linq.JObject', $true)
# The comma: the JSON object as it is (PowerShell would unroll its values).
function J($text) { return ,($jObject.GetMethod('Parse', [type[]]@([string])).Invoke($null, [object[]]@($text))) }

# ---- What the package changed, value by value (fictional analyses of a video).
$baseline = '{"Version":2,"VideoPath":"C:\\Fictional\\race.mp4","ActiveRaceId":"r1","Races":[' +
  '{"Id":"r1","Athlete":"Αθλητής Δοκιμής","LastPdfUrl":null,"SheetPending":true,"PackageMade":"17/11/2026 22:00","Marks":[{"Timestamp":100}]},' +
  '{"Id":"r2","Athlete":"Αθλήτρια Παράδειγμα","LastPdfUrl":null,"SheetPending":false}],"Calibrations":[]}'
# The package: its PDF, its ready package, the Sheet up to date; a race that went in the meantime is not touched.
$now = '{"Version":2,"VideoPath":"C:\\Fictional\\race.mp4","ActiveRaceId":"r1","Races":[' +
  '{"Id":"r1","Athlete":"Αθλητής Δοκιμής","LastPdfUrl":"https://example.com/fictional.pdf","SheetPending":false,"PackageMade":"17/11/2026 22:00","Marks":[{"Timestamp":100}],' +
  '"PackageInfo":{"Seconds":62.5,"Width":1280,"Height":720,"Screens":3,"Archived":true}},' +
  '{"Id":"r2","Athlete":"Αθλήτρια Παράδειγμα","LastPdfUrl":null,"SheetPending":false}],"Calibrations":[]}'
$changes = $tMgr.GetMethod('BackgroundChanges', $all).Invoke($null, [object[]]@($baseline, $now))
$names = @($changes | ForEach-Object { (F $_ 'RaceId') + '.' + (F $_ 'Name') }) | Sort-Object
Check (($names -join ',') -eq 'r1.LastPdfUrl,r1.PackageInfo,r1.SheetPending') ('the changes of the package: ' + ($names -join ', '))

# The analysis as it is now (the analyst opened the same video again meanwhile): a mark more, the Sheet waiting again; the second race gone.
$current = J ('{"Version":2,"VideoPath":"C:\\Fictional\\race.mp4","ActiveRaceId":"r1","Races":[' +
  '{"Id":"r1","Athlete":"Αθλητής Δοκιμής","LastPdfUrl":null,"SheetPending":"changed by the analyst","PackageMade":"17/11/2026 22:00","Marks":[{"Timestamp":100},{"Timestamp":250}]}],"Calibrations":[]}')
# (Not through B: PowerShell would give the values of the JSON object one by one.)
$applied = $tMgr.GetMethod('ApplyChanges', $all).Invoke($null, [object[]]@($current, $changes))
$appliedNames = @($applied | ForEach-Object { F $_ 'Name' }) | Sort-Object
Check (($appliedNames -join ',') -eq 'LastPdfUrl,PackageInfo') ('applied: ' + ($appliedNames -join ', ') + ' (the value the analyst changed meanwhile stays his)')
$r1 = $current['Races'][0]
Check ([string]$r1['LastPdfUrl'] -eq 'https://example.com/fictional.pdf' -and [double]$r1['PackageInfo']['Seconds'] -eq 62.5) 'the PDF and the ready package in the analysis'
Check ([string]$r1['SheetPending'] -eq 'changed by the analyst' -and $r1['Marks'].Count -eq 2) 'the marks and the value of the analyst as they were'

# Set on the objects of the program: a race of the program gets the values of its file.
$race = B ([Activator]::CreateInstance((T 'SwimRace')))
SetF $race 'Id' 'r1'
SetF $race 'Athlete' 'Αθλητής Δοκιμής'
$setMember = $tMgr.GetMethod('SetJsonMember', $all)
foreach ($c in $applied) { $setMember.Invoke($null, [object[]]@($race, (F $c 'Name'), (F $c 'After'))) }
$info = F $race 'PackageInfo'
Check ((F $race 'LastPdfUrl') -eq 'https://example.com/fictional.pdf' -and $null -ne $info -and (F $info 'Screens') -eq 3 -and (F $info 'Archived') -eq $true) 'set on the race of the program (its PDF, its ready package)'
$setMember.Invoke($null, [object[]]@($race, 'LastPdfUrl', $null))
Check ($null -eq (F $race 'LastPdfUrl')) 'a value taken away'
$none = $tMgr.GetMethod('BackgroundChanges', $all).Invoke($null, [object[]]@($now, $now))
Check (@($none).Count -eq 0) 'nothing changed: nothing to give'

# ---- The window of the package and the background.
Add-Type -ReferencedAssemblies System.dll -TypeDefinition @"
using System;
using System.ComponentModel;
using System.Threading;
public sealed class FictionalRenderGate
{
    public volatile bool Go;
    public volatile int Started;
}
public static class FictionalRender
{
    // The video of a fictional package: 0 to 100 %, stopped at 40 % until its gate opens; made (VideoReady) at the end.
    public static Func<BackgroundWorker, string> For(object package, FictionalRenderGate gate)
    {
        return w =>
        {
            gate.Started++;
            for (int i = 0; i <= 100; i++)
            {
                if (w.CancellationPending)
                    return null;
                w.ReportProgress(i, "Το βίντεο γίνεται με τα γραφικά: καρέ " + i + "/100");
                while (!gate.Go && i == 40)
                {
                    if (w.CancellationPending)
                        return null;
                    Thread.Sleep(20);
                }

                Thread.Sleep(4);
            }

            package.GetType().GetField("VideoReady").SetValue(package, true);
            package.GetType().GetField("VideoSeconds").SetValue(package, 62.5);
            package.GetType().GetField("VideoScreens").SetValue(package, 3);
            package.GetType().GetField("VideoHasSound").SetValue(package, true);
            return null;
        };
    }
}
"@

$tPkg = T 'SwimPackage'
$folder = [System.IO.Path]::Combine($out, 'Αποστολές προπονητών')
[System.IO.Directory]::CreateDirectory($folder) | Out-Null
function NewPackage($name) {
  $r = B ([Activator]::CreateInstance((T 'SwimRace')))
  SetF $r 'Athlete' $name
  SetF $r 'Kind' 'training'
  SetF $r 'Style' 'fly'
  $base = [string]$tPkg.GetMethod('BaseNameFor').Invoke($null, [object[]]@($r))
  return @{ race = $r; pkg = (B ($tPkg.GetMethod('At').Invoke($null, [object[]]@($r, [string]$folder, $base)))) }
}
$tForm = T 'FormSwimPackage'
$ctor = $tForm.GetConstructors()[0]
function NewForm($p, $title, $gate) {
  $render = [FictionalRender]::For($p.pkg, $gate)
  $f = $ctor.Invoke([object[]]@($p.pkg, $title, 'Τμήμα ανάλυσης', $render, $null, $null, $null, $null))
  $f.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
  $f.Location = New-Object System.Drawing.Point 0, 0
  return $f
}

$pa = NewPackage 'Αθλητής Δοκιμής'
$gateA = New-Object FictionalRenderGate
$formA = NewForm $pa 'Αθλητής Δοκιμής · Τεχνική πεταλούδα' $gateA
$formA.AllowBackground = $true
$formA.QueueTitle = 'Αθλητής Δοκιμής · Τεχνική πεταλούδα'
$script:endsA = 0
$formA.add_VideoFinished({ $script:endsA++ })
$script:enabledBefore = $false
$tick = New-Object System.Windows.Forms.Timer
$tick.Interval = 1200
$tick.add_Tick({
  $tick.Stop()
  $btn = F $formA 'btnBackground'
  $script:enabledBefore = $btn.Enabled
  Shot $formA 'package-making.png'
  $btn.PerformClick()
})
$tick.Start()
$result = $formA.ShowDialog()
Check ($script:enabledBefore) '«Συνέχισε με άλλον αθλητή» while the video is made'
Check ($result -eq [System.Windows.Forms.DialogResult]::Ignore -and $formA.Background -and $formA.VideoRunning -and -not $formA.IsDisposed) ('the window left, the video goes on (ShowDialog: ' + $result + ')')

# Shown again from the box, then closed: it only hides while the video is made.
$formA.ShowAgain($null)
Pump 1 $null
Check ($formA.Visible) 'shown again (not modal)'
$formA.Close()
Pump 1 $null
Check (-not $formA.IsDisposed -and -not $formA.Visible -and $formA.VideoRunning) 'closed while its video is made: hidden, the video goes on'

# ---- One video at a time: a second package waits, then starts by itself.
$pb = NewPackage 'Αθλήτρια Παράδειγμα'
$gateB = New-Object FictionalRenderGate
$gateB.Go = $true
$formB = NewForm $pb 'Αθλήτρια Παράδειγμα · Τεχνική ελεύθερο' $gateB
$formB.AllowBackground = $true
$formB.QueueTitle = 'Αθλήτρια Παράδειγμα · Τεχνική ελεύθερο'
$formB.Show()
Pump 2 $null
Check ($formB.VideoWaiting -and -not $formB.VideoRunning -and $gateB.Started -eq 0) 'the second video waits for the first'
$waitLine = (F $formB 'videoStatus').Text
Check ($waitLine -like '*Αθλητής Δοκιμής*%*') ('its line: ' + $waitLine)
Check ((F $formB 'btnBackground').Enabled) '«Συνέχισε με άλλον αθλητή» also while it waits'
Shot $formB 'package-waiting.png'

# ---- The box of the packages: the first being made, the second waiting, a third ready (its notice).
$pc = NewPackage 'Αθλητής Τρίτος'
$gateC = New-Object FictionalRenderGate
$gateC.Go = $true
$formC = NewForm $pc 'Αθλητής Τρίτος · Τεχνική ύπτιο' $gateC
SetF $formC 'shownOnce' $true
SetF $formC 'videoDone' $true
SetF $pc.pkg 'VideoReady' $true
SetF $pc.pkg 'VideoSeconds' ([double]62)
SetF $pc.pkg 'VideoScreens' ([int]3)
SetF $pc.pkg 'VideoHasSound' $true
SetF $pc.pkg 'PdfReady' $true
SetF $pc.pkg 'Archived' $true
$owner = New-Object System.Windows.Forms.Form
$owner.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$owner.Location = New-Object System.Drawing.Point 0, 0
$owner.Size = New-Object System.Drawing.Size 1400, 860
$owner.BackColor = [System.Drawing.Color]::FromArgb(27, 35, 41)
$owner.Show()
$tBp = $a.GetType('Kinovea.ScreenManager.SwimAnalysisManager+BackgroundPackage', $true)
$list = $tMgr.GetField('background', $all).GetValue($null)
function AddBackground($form, $p, $title) {
  $bp = [Activator]::CreateInstance($tBp)
  SetF $bp 'Form' $form
  SetF $bp 'Package' $p.pkg
  SetF $bp 'Race' $p.race
  SetF $bp 'Title' $title
  SetF $bp 'Owner' $owner
  $list.GetType().GetMethod('Add').Invoke($list, [object[]]@($bp))
  return $bp
}
$bpA = AddBackground $formA $pa 'Αθλητής Δοκιμής · Τεχνική πεταλούδα'
$bpB = AddBackground $formB $pb 'Αθλήτρια Παράδειγμα · Τεχνική ελεύθερο'
$bpC = AddBackground $formC $pc 'Αθλητής Τρίτος · Τεχνική ύπτιο'
$rows = $tMgr.GetMethod('BackgroundRows', $all).Invoke($null, $null)
$states = @($rows | ForEach-Object { [string](F $_ 'State') })
Check (($states -join ',') -eq 'Making,Waiting,Ready') ('the rows of the box: ' + ($states -join ', '))
Check ((F $rows[2] 'Line') -like '*1:02*3*PDF ✓*KOE App ✓*') ('the ready one: ' + (F $rows[2] 'Line'))
Check ((F $rows[0] 'Line') -like '*40%*') ('the one being made: ' + (F $rows[0] 'Line'))
$tTray = T 'FormSwimPackageTray'
$tTray.GetMethod('UpdateFor', $all).Invoke($null, [object[]]@($owner))
Pump 1 $null
$tray = $tTray.GetField('tray', $all).GetValue($null)
Check ($null -ne $tray -and $tray.Visible -and $tray.Width -eq 416 -and $tray.Height -eq (132 + 8 + 3 * 58 + 2 * 8)) ('the box with the notice: ' + $tray.Width + 'x' + $tray.Height)
$inside = $owner.RectangleToScreen($owner.ClientRectangle)
Check ($tray.Right -le $inside.Right -and $tray.Bottom -le $inside.Bottom -and $tray.Right -ge $inside.Right - 40) ('at the bottom right of Kinovea: ' + $tray.Location)
Shot $tray 'package-box-notice.png'
$tMgr.GetMethod('NoticedBackground', $all).Invoke($null, [object[]]@($bpC))
$tTray.GetMethod('UpdateFor', $all).Invoke($null, [object[]]@($owner))
Pump 1 $null
Check ($tray.Width -eq 392 -and $tray.Height -eq (3 * 58 + 2 * 8)) ('«Αργότερα»: the rows only (' + $tray.Width + 'x' + $tray.Height + ')')
Shot $tray 'package-box.png'

# ---- The first video ends: said once; the second starts by itself and ends.
$gateA.Go = $true
Pump 20 { $formA.VideoDone -and $formB.VideoRunning }
Check ($formA.VideoDone -and $script:endsA -eq 1) ('the end of the first said once: ' + $script:endsA)
Pump 20 { $formB.VideoDone }
Check ($gateB.Started -eq 1 -and $formB.VideoDone) 'the second started by itself after the first, and was made'

# Closed for good from the box («×»): the window goes.
$formA.CloseForGood()
Pump 1 $null
Check ($formA.IsDisposed) 'the first closed for good'
$list.GetType().GetMethod('Clear').Invoke($list, $null)
$tTray.GetMethod('UpdateFor', $all).Invoke($null, [object[]]@($owner))
Pump 1 $null
Check (-not $tray.Visible) 'no package: no box'
$formB.Close(); $formC.Dispose(); $owner.Close()
Pump 1 $null

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $all).GetValue($null)
$keys = @('⏩ Συνέχισε με άλλον αθλητή', '✓ Η αποστολή είναι έτοιμη', '📦 Άνοιγμα αποστολής', 'Αργότερα', 'Έτοιμη αποστολή', 'αποστολή στο παρασκήνιο',
  '⏳ Το βίντεο περιμένει να τελειώσει το βίντεο της αποστολής «{0}» ({1}%).', '📦 Η αποστολή «{0}» τελειώνει στο παρασκήνιο: οι οθόνες είναι ελεύθερες για τον επόμενο αθλητή.')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts' + ($(if ($missing.Count -gt 0) { ': missing ' + ($missing -join ' | ') } else { '' })))

if ($fails -gt 0) { Write-Output ('FAILED ' + $fails); exit 1 }
Write-Output 'all background package checks passed'
