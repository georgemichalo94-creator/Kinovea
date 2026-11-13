# fm_ci.ps1: the CI check that Kinovea computes any analysis without an error (2.22.0): the fictional analyses of fm_make.js (races,
# relays, medley, yards, several frame rates, training videos; two fifths with wrong or missing marks) are read as a .swim.json reads
# them, computed (SwimCalculator, SwimTechCalculator) and made into what the Google Sheet gets (SwimPayload): no exception, no number
# that is not a number in what is sent. What the program computed is written beside each analysis for fm_check.js (the same numbers
# in the report of the script). Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File fm_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -dir <folder of fm_make.js>
param([string]$sm, [string]$bin, [string]$dir)
$ErrorActionPreference = 'Stop'
try { Add-Type -AssemblyName System.Drawing } catch { }
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path; $dir = (Resolve-Path $dir).Path
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
$tRace = T 'SwimRace'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tTech = T 'SwimTechCalculator'; $tClient = T 'SwimPayload'; $tSettings = T 'SwimSettings'
$tConvert = $json.GetType('Newtonsoft.Json.JsonConvert', $true)
$deserialize = $tConvert.GetMethod('DeserializeObject', [Type[]]@([string], [Type]))
$serialize = $tConvert.GetMethod('SerializeObject', [Type[]]@([object]))
$ensure = $tRace.GetMethod('EnsureLists', [System.Reflection.BindingFlags]'Public,NonPublic,Instance')
$compute = $tCalc.GetMethod('Compute'); $techCompute = $tTech.GetMethod('Compute'); $build = $tClient.GetMethod('Build')
$utf8 = New-Object System.Text.UTF8Encoding($false)
$inv = [System.Globalization.CultureInfo]::InvariantCulture
$settings = [Activator]::CreateInstance($tSettings)
$rounding = [double]$settings.StrokeRounding
$fails = 0; $done = 0

# The deepest cause of an error of the program, with where it was thrown.
function Cause($e) {
  $x = $e
  while ($null -ne $x.InnerException) { $x = $x.InnerException }
  $where = ''
  if ($null -ne $x.StackTrace) { $where = (($x.StackTrace -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' < ') }
  return ($x.GetType().Name + ': ' + $x.Message + ' @ ' + $where)
}

$lines = [System.IO.File]::ReadAllLines([System.IO.Path]::Combine($dir, 'cases.txt'), $utf8)
foreach ($line in $lines) {
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  $f = $line.Split(';')
  $n = $f[0]
  $outPath = [System.IO.Path]::Combine($dir, 'out_' + $n + '.json')
  try {
    $text = [System.IO.File]::ReadAllText([System.IO.Path]::Combine($dir, 'race_' + $n + '.json'), $utf8)
    $race = $deserialize.Invoke($null, @([string]$text, [Type]$tRace))
    [void]$ensure.Invoke($race, @())
    $timing = [Activator]::CreateInstance($tTiming)
    $timing.AverageTimestampsPerFrame = [double]::Parse($f[1], $inv); $timing.FrameIntervalMilliseconds = [double]::Parse($f[2], $inv); $timing.HighSpeedFactor = [double]::Parse($f[3], $inv)
    $computed = $compute.Invoke($null, @($race, $timing, $rounding))
    $phases = $techCompute.Invoke($null, @($race, $computed, $timing))
    $body = [string]$build.Invoke($null, @($race, $computed, $phases, $timing, [long]0, $settings, 'fictional.mp4', 'update', $null, $false, $null))
    if ($body -cmatch 'NaN|Infinity') {
      $at = $body.IndexOf('NaN'); if ($at -lt 0) { $at = $body.IndexOf('Infinity') }
      throw ('a number that is not a number is sent: …' + $body.Substring([Math]::Max(0, $at - 80), [Math]::Min(120, $body.Length - [Math]::Max(0, $at - 80))))
    }
    # (The unary comma: one argument, also when it is a list.)
    $cj = [string]$serialize.Invoke($null, (,$computed))
    [System.IO.File]::WriteAllText($outPath, ('{"body":' + $body + ',"computed":' + $cj + ',"phases":' + $phases.Count + '}'), $utf8)
    $done++
  }
  catch {
    $fails++
    $cause = Cause $_.Exception
    if ($fails -le 12) { Write-Output ('FAIL analysis ' + $n + ' (mutation ' + $f[4] + '): ' + $cause) }
    [System.IO.File]::WriteAllText($outPath, ('{"error":' + [string]$serialize.Invoke($null, (,[string]$cause)) + '}'), $utf8)
  }
}

if ($fails -gt 0) { Write-Output ('FAILED: ' + $fails + ' of ' + $lines.Count + ' analyses'); exit 1 }
Write-Output ('fuzz marks ok: ' + $done + ' fictional analyses computed by the program')
