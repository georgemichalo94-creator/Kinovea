# pp_ci.ps1 (2.38.0): the CI check of the PDF of the report of the 2nd version on FICTIONAL races (rv2_build.js, no real athlete): the
# pages that the Apps Script makes are printed by the DLL (SwimPdfPrinter) with the Edge of the runner, through its DevTools (and a few
# from its command line too): a PDF, A4, as many pages as the report, with the fonts of the report embedded (Inter only: a letter
# missing from them would bring another font), in time; the fonts in the DLL; the English of the new texts; the PDFs for a look.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea).
#   powershell -File pp_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -dir <folder of rv2_build.js> -out <folder>
param([string]$sm, [string]$bin, [string]$dir, [string]$out)
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
[void][System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Newtonsoft.Json.dll'))
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

# ---- What the printing left: no browser of its own still running, no folder of a print.
Start-Sleep -Milliseconds 500
$left = @(Get-CimInstance Win32_Process -Filter "Name = 'msedge.exe' OR Name = 'chrome.exe'" | Where-Object { $_.CommandLine -like '*KinoveaSwimPdf*' })
Check ($left.Count -eq 0) ('browsers of the prints still running: ' + $left.Count)
$root = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'KinoveaSwimPdf')
$dirs = if (Test-Path $root) { @(Get-ChildItem $root -Directory) } else { @() }
Check ($dirs.Count -eq 0) ('folders of the prints left: ' + $dirs.Count)

if ($fails -gt 0) { Write-Output ('pdf print: ' + $fails + ' failed'); exit 1 }
Write-Output ('pdf print: all ok (' + $files.Count + ' reports)')
