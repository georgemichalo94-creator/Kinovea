# pp_ci.ps1 (2.38.0): the CI check of the PDF of the report of the 2nd version on FICTIONAL races (rv2_build.js, no real athlete): the
# pages that the Apps Script makes are printed by the DLL (SwimPdfPrinter) with the Edge of the runner, through its DevTools (and a few
# from its command line too): a PDF, A4, as many pages as the report, with the fonts of the report embedded (Inter only: a letter
# missing from them would bring another font), in time; the fonts in the DLL; the English of the new texts; the PDFs for a look.
# With -server (pp_server.js: the script of the KOE App on the mock of Apps Script, over HTTP) and -races (fm_make.js): from end to
# end, a fictional race of Kinovea asks for its PDF, the DLL prints the pages that come and sends the PDF to be kept; and the queue of
# the requests with a PDF to keep. Windows PowerShell 5.1 (.NET Framework, as Kinovea).
#   powershell -File pp_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -dir <folder of rv2_build.js> -out <folder>
#     [-server http://127.0.0.1:<port>/exec -races <folder of fm_make.js>]
param([string]$sm, [string]$bin, [string]$dir, [string]$out, [string]$server, [string]$races)
$ErrorActionPreference = 'Stop'
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
$static = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$latin = [System.Text.Encoding]::GetEncoding(28591)

$tPrinter = T 'SwimPdfPrinter'
$print = $tPrinter.GetMethod('Print')
$withFonts = $tPrinter.GetMethod('WithFonts')
$countPages = $tPrinter.GetMethod('CountPages')

# ---- The browser of the runner and the fonts of the report in the DLL.
$browser = $tPrinter.GetProperty('BrowserPath').GetValue($null)
Check ($null -ne $browser) ('the browser that prints: ' + $browser)
if ($browser) { Write-Output ('     version ' + (Get-Item $browser).VersionInfo.ProductVersion) }
$fonts = @($a.GetManifestResourceNames() | Where-Object { $_ -like 'SwimFonts.*.woff' })
Check ($fonts.Count -eq 8) ('the fonts of the report in the DLL: ' + $fonts.Count + ' (' + ($fonts -join ', ') + ')')
$sample = $withFonts.Invoke($null, @('<!doctype html><html><head><meta charset="utf-8"><style>p{}</style></head><body></body></html>'))
$at = $sample.IndexOf('kinovea-swim-fonts'); $own = $sample.IndexOf('<style>p{}')
Check ($at -gt 0 -and $at -lt $own -and $sample.Contains("font-family:'Inter Display';font-style:normal;font-weight:800")) 'the fonts go before the styles of the pages'

# ---- Every page of the fictional reports printed through the DevTools of the browser; a few from its command line too.
$byCommandLine = @('lc-100-fs-full', 'sc-4x50-im-reftimes', 'lc-1500-fs-short')
$files = @(Get-ChildItem $dir -Filter *.html | Sort-Object Name)
Check ($files.Count -gt 0) ('fictional reports to print: ' + $files.Count)
$times = @()
foreach ($f in $files) {
  $html = [System.IO.File]::ReadAllText($f.FullName, $utf8)
  $expected = ([regex]::Matches($html, 'class="page[ "]')).Count
  $methods = @('devtools')
  if ($byCommandLine -contains $f.BaseName) { $methods += 'commandline' }
  foreach ($m in $methods) {
    $r = $print.Invoke($null, @([string]$html, [string]$m, [int]90))
    $made = $r.Ok -and $null -ne $r.Pdf
    $text = if ($made) { $latin.GetString($r.Pdf) } else { '' }
    $boxes = @([regex]::Matches($text, '/MediaBox \[0 0 ([0-9.]+) ([0-9.]+)\]') | ForEach-Object { $_.Groups[1].Value + 'x' + $_.Groups[2].Value } | Sort-Object -Unique)
    $a4 = $boxes.Count -eq 1 -and $boxes[0] -match '^59[45]\.\d+x84[12]\.\d+$'
    $faces = @([regex]::Matches($text, '/BaseFont /(?:[A-Z]{6}\+)?([A-Za-z0-9-]+)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $others = @($faces | Where-Object { $_ -notlike 'Inter-*' -and $_ -notlike 'InterDisplay-*' })
    $pages = if ($made) { [int]$countPages.Invoke($null, @(,[byte[]]$r.Pdf)) } else { 0 }
    if ($m -eq 'devtools' -and $made) { $times += $r.Seconds }
    $what = '{0} ({1}): {2} pages of {3}, {4}, fonts {5}, {6:0.0} s, {7:0} KB{8}' -f $f.BaseName, $m, $pages, $expected, ($boxes -join ' '),
      ($faces -join ','), $r.Seconds, ($(if ($made) { $r.Pdf.Length } else { 0 }) / 1024), $(if ($r.Error) { ' · ' + $r.Error } else { '' })
    Check ($made -and $pages -eq $expected -and $r.Pages -eq $expected -and $a4 -and $faces.Count -gt 0 -and $others.Count -eq 0 -and $r.Seconds -lt 60) $what
    if ($made -and $out) { [System.IO.File]::WriteAllBytes([System.IO.Path]::Combine($out, $f.BaseName + '-' + $m + '.pdf'), $r.Pdf) }
  }
}
if ($times.Count -gt 0) {
  $sorted = @($times | Sort-Object)
  Write-Output ('     print times: median {0:0.0} s, longest {1:0.0} s ({2} reports)' -f $sorted[[int][math]::Floor($sorted.Count / 2)], $sorted[-1], $sorted.Count)
}

# ---- No pages and an empty page: not a PDF, said (the PDF of the sheet takes its place in Kinovea).
$none = $print.Invoke($null, @([string]'', [string]'devtools', [int]60))
Check (-not $none.Ok -and $none.Error) ('no pages: ' + $none.Error)

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $static).GetValue($null)
$keys = @('δεν βρέθηκε ο Microsoft Edge ούτε το Google Chrome', 'χωρίς σελίδες', 'δεν βγήκε PDF', 'ο browser δεν άνοιξε', 'ο browser έκλεισε αμέσως',
  'η σελίδα δεν άνοιξε: ', 'η σελίδα δεν φόρτωσε', 'ο browser δεν έδωσε PDF', 'ο browser δεν έχει σελίδα', 'ο browser δεν τελείωσε', 'ο browser δεν έγραψε PDF',
  'χωρίς απάντηση', 'ο browser έκλεισε τη σύνδεση', '📄 Το PDF ανεβαίνει στο Drive…', '🖨️ Το PDF τυπώνεται…', '🖨️ Η αναφορά ({0} σελίδες) τυπώνεται σε PDF…',
  'Δεν βρέθηκε ο Microsoft Edge (ούτε το Google Chrome) για να τυπωθεί η νέα αναφορά: το PDF γίνεται από το φύλλο, όπως πριν.',
  'Η νέα αναφορά δεν έγινε στο Apps Script ({0}): το PDF του φύλλου, όπως πριν.',
  'Το Apps Script του KOE App δεν έχει το αρχείο SwimReportV2: το PDF του φύλλου, όπως πριν. Πρόσθεσέ το (δες τον οδηγό, «Ενημέρωση του script»).',
  'Το PDF το έφτιαξε το Apps Script {0} από το φύλλο: για τη νέα αναφορά βάλε την έκδοση {1} με το αρχείο SwimReportV2 (δες τον οδηγό, «Ενημέρωση του script»).',
  'Το PDF της νέας αναφοράς δεν τυπώθηκε ({0}): γίνεται το PDF του φύλλου, όπως πριν.',
  'Το PDF της νέας αναφοράς δεν αποθηκεύτηκε ({0}): γίνεται το PDF του φύλλου, όπως πριν.')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts (' + $missing.Count + ' missing: ' + ($missing -join ' | ') + ')')

# ---- From end to end with the script of the KOE App, and the queue of the requests with a PDF to keep.
if ($server -and $races) {
  $tRace = T 'SwimRace'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'
  $tPayload = T 'SwimPayload'; $tClient = T 'SwimSheetClient'; $tSettings = T 'SwimSettings'
  $all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
  $deserialize = $json.GetType('Newtonsoft.Json.JsonConvert', $true).GetMethod('DeserializeObject', [Type[]]@([string], [Type]))
  $parse = $json.GetType('Newtonsoft.Json.Linq.JObject', $true).GetMethod('Parse', [Type[]]@([string]))
  $tCalc.GetField('LengthFromRate').SetValue($null, $true)
  $inv = [System.Globalization.CultureInfo]::InvariantCulture
  $build = $tPayload.GetMethod('Build')
  $cases = [System.IO.File]::ReadAllLines([System.IO.Path]::Combine($races, 'cases.txt'), $utf8)
  $line = @($cases | Where-Object { $_ -like '*;mx' } | Where-Object {
      $t = [System.IO.File]::ReadAllText([System.IO.Path]::Combine($races, 'race_' + $_.Split(';')[0] + '.json'), $utf8)
      $t.Contains('"Distance":100,') -and $t.Contains('"Style":"fs"') -and $t.Contains('"Relay":false') -and $t.Contains('"Pool":50,') })[0]
  Check ($null -ne $line) 'a fictional 100 m free in a 50 m pool'
  $f = $line.Split(';')
  $text = [System.IO.File]::ReadAllText([System.IO.Path]::Combine($races, 'race_' + $f[0] + '.json'), $utf8)
  $timing = [Activator]::CreateInstance($tTiming)
  $timing.AverageTimestampsPerFrame = [double]::Parse($f[1], $inv); $timing.FrameIntervalMilliseconds = [double]::Parse($f[2], $inv); $timing.HighSpeedFactor = [double]::Parse($f[3], $inv)
  $settings = [Activator]::CreateInstance($tSettings)
  $settings.WebAppUrl = $server; $settings.Token = 'tok'
  function Race($id) {
    $r = $deserialize.Invoke($null, @([string]$text, [Type]$tRace))
    [void]$tRace.GetMethod('EnsureLists', $all).Invoke($r, @())
    $r.Id = $id; $r.Athlete = 'Αθλητής Δείγμα'; $r.Competition = 'Αγώνας δείγμα'; $r.Location = 'Πόλη δείγμα'; $r.Round = 'Τελικός'
    return $r
  }
  function Body($r, $action, $extra) {
    $c = $tCalc.GetMethod('Compute').Invoke($null, @($r, $timing, [double]0.5))
    $tech = $tTech.GetMethod('Compute').Invoke($null, @($r, $c, $timing))
    # (Assigned, not through the pipeline: PowerShell would unroll a JObject into its properties.)
    $x = $null
    if ($extra) { $x = $parse.Invoke($null, @([string]$extra)) }
    return [string]$build.Invoke($null, @($r, $c, $tech, $timing, [long]0, $settings, 'video.mp4', [string]$action, $null, $false, $x))
  }
  $client = [Activator]::CreateInstance($tClient)
  Register-ObjectEvent -InputObject $client -EventName StateChanged -SourceIdentifier 'swimsync' | Out-Null
  # The answers of the client (not "Sending…"), until one of a PDF to keep or of a state as asked, or the time.
  function Answer([bool]$store, [int]$seconds) {
    $until = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $until) {
      $ev = Wait-Event -SourceIdentifier 'swimsync' -Timeout 2
      if ($null -eq $ev) { continue }
      Remove-Event -EventIdentifier $ev.EventIdentifier
      $e = $ev.SourceEventArgs
      if ($e.State.ToString() -eq 'Sending') { continue }
      if ([bool]$e.Store -eq $store) { return $e }
    }
    return $null
  }
  # What came to the script, in its order (the array itself: Windows PowerShell gives a JSON array as one object).
  function ServerLog() {
    $parsed = ConvertFrom-Json (Invoke-WebRequest -Uri ($server -replace '/exec$', '/log') -UseBasicParsing).Content
    $items = New-Object System.Collections.ArrayList
    foreach ($it in $parsed) { [void]$items.Add($it) }
    return ,$items.ToArray()
  }

  # 1. The PDF asked for with v2: the pages of the report come in place of the PDF of the sheet.
  $race = Race 'e2e-ci-1'
  $client.Enqueue($server, (Body $race 'pdf' '{"v2":true}'), $race.Id, $true)
  $e = Answer $false 120
  $v2 = $null
  if ($e -and $e.Result.Raw) { $v2 = $e.Result.Raw['v2'] }
  $pages = if ($v2) { [int]$v2['pages'].ToString() } else { 0 }
  Check ($e -and $e.Result.Ok -and $v2 -and $pages -ge 5 -and -not $e.Result.PdfUrl) ('the pages of the report from the script: ' + $pages + ' pages' + $(if ($e) { ' · ' + $e.Message } else { ' · no answer' }))
  if ($v2) {
    # 2. Printed by the DLL and sent to be kept: the answer of a PDF, the PDF of the 2nd version in the folder of the reports.
    $printed = $print.Invoke($null, @([string]$v2['html'].ToString(), [string]$null, [int]90))
    Check ($printed.Ok -and $printed.Pages -eq $pages) ('printed by ' + $printed.Browser + ' (' + $printed.Method + '): ' + $printed.Pages + ' pages of ' + $pages + ', ' + [math]::Round($printed.Seconds, 1) + ' s' + $(if ($printed.Error) { ' · ' + $printed.Error } else { '' }))
    if ($printed.Ok) {
      $extra = '{"raceId":"' + $race.Id + '","pdf":"' + [Convert]::ToBase64String($printed.Pdf) + '"}'
      $client.EnqueueStore($server, (Body $race 'pdfStore' $extra), $race.Id)
      $s = Answer $true 120
      $pdf = $null
      if ($s -and $s.Result.Raw) { $pdf = $s.Result.Raw['pdf'] }
      Check ($s -and $s.Result.Ok -and $s.Result.PdfUrl -and $pdf -and $pdf['v2'].ToString() -eq 'True' -and $pdf['name'].ToString().EndsWith('.pdf')) ('the PDF kept by the script: ' + $(if ($s) { $s.Result.PdfUrl + ' ' + $s.Message } else { 'no answer' }))
      $log = ServerLog
      $kept = @($log | Where-Object { $_.action -eq 'pdfStore' -and $_.raceId -eq $race.Id })
      Check ($kept.Count -eq 1 -and $kept[0].pdfPages -eq $pages -and $kept[0].ok) ('the script got the PDF whole: ' + $(if ($kept.Count) { '' + $kept[0].pdfPages + ' pages, ' + $kept[0].bytes + ' bytes' } else { 'nothing' }))

      # 3. The queue: a state in flight, then a PDF to keep and two newer states of the same race: the PDF is kept, the newest state
      # goes after it (the older one replaced), nothing lost.
      $slow = Race 'e2e-slow-2'
      $client.Enqueue($server, (Body $slow 'update' $null), $slow.Id, $false)
      [void](Answer $false 60)
      $log = ServerLog
      $before = $log.Count
      $client.Enqueue($server, (Body $slow 'update' $null), $slow.Id, $false)
      Start-Sleep -Milliseconds 300
      $client.EnqueueStore($server, (Body $slow 'pdfStore' ('{"raceId":"' + $slow.Id + '","pdf":"' + [Convert]::ToBase64String($printed.Pdf) + '"}')), $slow.Id)
      $slow.Lane = 3
      $client.Enqueue($server, (Body $slow 'update' $null), $slow.Id, $false)
      $slow.Lane = 5
      $client.Enqueue($server, (Body $slow 'update' $null), $slow.Id, $false)
      $got = @(); $until = (Get-Date).AddSeconds(90)
      while ($got.Count -lt 3 -and (Get-Date) -lt $until) { $x = Answer ($got.Count -eq 1) 30; if ($x) { $got += $x } else { break } }
      Start-Sleep -Milliseconds 300
      $log = ServerLog
      $after = @($log | Select-Object -Skip $before | Where-Object { $_.raceId -eq $slow.Id })
      $order = ($after | ForEach-Object { $_.action }) -join ', '
      Check ($order -eq 'update, pdfStore, update' -and @($after | Where-Object { -not $_.ok }).Count -eq 0) ('the queue with a PDF to keep: ' + $order)
      Check ($client.HasWaiting($slow.Id) -eq $false) 'nothing of the race waits after them'

      # 4. The analysis of Kinovea with the answers of the web app (SwimAnalysisManager.V2Answer, as the answer of a PDF comes): the
      # pages printed and the PDF sent to be kept by its own client; why the PDF of the sheet came, said in the status.
      $settingsDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-pp-ci-' + [Guid]::NewGuid().ToString('N')) + [System.IO.Path]::DirectorySeparatorChar
      New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
      $software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
      $software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)
      $mgrType = T 'SwimAnalysisManager'
      $mgr = $mgrType.GetProperty('Instance').GetValue($null)
      $mgrType.GetProperty('Settings').SetValue($mgr, $settings)
      $mgrType.GetProperty('Timing').SetValue($mgr, $timing)
      $answer = $mgrType.GetMethod('V2Answer', $all)
      $warned = $mgrType.GetField('v2BackendWarned', $all)
      [void]$answer.Invoke($mgr, @($race, $e.Result))
      $stored = $null; $until = (Get-Date).AddSeconds(90)
      while ($null -eq $stored -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 500
        $log = ServerLog
        $stored = @($log | Where-Object { $_.action -eq 'pdfStore' -and $_.raceId -eq $race.Id })[1]
      }
      Check ($stored -and $stored.ok -and $stored.pdfPages -eq $pages) ('the analysis printed the pages of the answer and its client sent the PDF: ' + $(if ($stored) { '' + $stored.pdfPages + ' pages, ' + $stored.bytes + ' bytes' } else { 'nothing' }) + ' · ' + $mgrType.GetProperty('LastStatus').GetValue($mgr))
      $tResult = T 'SwimSheetResult'
      function Said($json, $pdfUrl) {
        $warned.SetValue($mgr, $false)
        $r = [Activator]::CreateInstance($tResult)
        $r.Ok = $true; $r.Raw = $parse.Invoke($null, @([string]$json)); $r.PdfUrl = $pdfUrl
        $mgrType.GetProperty('LastStatus').SetValue($mgr, '')
        [void]$answer.Invoke($mgr, @($race, $r))
        return [string]$mgrType.GetProperty('LastStatus').GetValue($mgr)
      }
      $said = Said '{"ok":true,"v2Error":"δοκιμή"}' $null
      Check ($said.Contains('δοκιμή')) ('the pages failed in the script: ' + $said)
      $said = Said '{"ok":true,"v2Missing":true,"pdf":{"url":"https://x"}}' 'https://x'
      Check ($said.Contains('SwimReportV2')) ('the file of the pages missing in the project: ' + $said)
      $said = Said '{"ok":true,"version":"2.21.0","pdf":{"url":"https://x"}}' 'https://x'
      Check ($said.Contains('2.22.0')) ('an older script: ' + $said)
      $said = Said '{"ok":true,"version":"2.22.0","pdf":{"url":"https://x"}}' 'https://x'
      Check ($said -eq '') ('the PDF of the 2nd version kept: nothing to say (' + $said + ')')
    }
  }
  Unregister-Event -SourceIdentifier 'swimsync'
}

# ---- What the printing left: no browser of its own still running, no folder of a print.
Start-Sleep -Milliseconds 500
$left = @(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe' OR Name = 'chrome.exe'" | Where-Object { $_.CommandLine -like '*KinoveaSwimPdf*' })
Check ($left.Count -eq 0) ('browsers of the prints still running: ' + $left.Count)
$root = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'KinoveaSwimPdf')
$dirs = if (Test-Path $root) { @(Get-ChildItem $root -Directory) } else { @() }
Check ($dirs.Count -eq 0) ('folders of the prints left: ' + $dirs.Count)

if ($fails -gt 0) { Write-Output ('pdf print: ' + $fails + ' failed'); exit 1 }
Write-Output ('pdf print: all ok (' + $files.Count + ' reports)')
