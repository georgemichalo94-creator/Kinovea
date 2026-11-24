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
# (2.34.0) As with the Apps Script of the repository (2.19.0 on): the stroke length from the speed and the rate.
$tCalc.GetField('LengthFromRate').SetValue($null, $true)
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

# (2.36.0) The events in the order of the race (fm_make.js 'mx': every distance of the metre and yard pools, every style, medley,
# relays): the program keeps the checkpoints, the walls, the phases and the events of the start and of each turn in the order of the
# race, and «Πέρασμα» (S) marks the right checkpoint: pressed at each one in order (video paused or playing), and passing some by,
# then coming back to them.
$tPresets = T 'SwimPresets'; $tSplits = T 'SwimSplits'
$defaultCps = $tPresets.GetMethod('DefaultCheckpoints'); $styleAt = $tPresets.GetMethod('StyleAt'); $targetAt = $tSplits.GetMethod('TargetAt')
$orderFails = 0; $ordered = 0; $presses = 0; $passedBy = 0
function OrderFail($n, $what) { $script:orderFails++; if ($script:orderFails -le 24) { Write-Output ('FAIL order ' + $n + ': ' + $what) } }
function Ascending($list) { for ($k = 1; $k -lt $list.Count; $k++) { if (-not ($list[$k] -gt $list[$k - 1])) { return $false } }; return $true }
# «Πέρασμα» at the frame of a checkpoint: the checkpoint it marks.
function Press($race, $timing, [long]$ts, [bool]$playing) {
  $callArgs = [object[]]@($race, $timing, $ts, -1, $playing, $false)
  $script:presses++
  return [int]$targetAt.Invoke($null, $callArgs)
}
function CheckOrder($n, $race, $timing, $computed, $phases) {
  $pool = [int]$race.Pool; $dist = [int]$race.Distance; $relay = [bool]$race.Relay; $leg = $dist / 4
  $what = $race.Style + ' ' + $dist + '/' + $pool + $race.PoolUnit + $(if ($relay) { ' relay' } else { '' })
  # The checkpoints: the program's own, ascending, every wall, the finish last.
  $cps = @($race.Checkpoints | ForEach-Object { [int]$_ })
  $def = @($defaultCps.Invoke($null, @($dist, $pool, $relay)) | ForEach-Object { [int]$_ })
  if (($cps -join ',') -ne ($def -join ',')) { OrderFail $n ($what + ': checkpoints ' + ($cps -join ',') + ' / program ' + ($def -join ',')) }
  if (-not (Ascending $def) -or $def[$def.Count - 1] -ne $dist) { OrderFail $n ($what + ': checkpoints not in order or not to the finish: ' + ($def -join ',')) }
  $walls = @(); for ($w = $pool; $w -lt $dist; $w += $pool) { $walls += $w }
  foreach ($w in $walls) { if ($def -notcontains $w) { OrderFail $n ($what + ': no checkpoint at the wall ' + $w) } }
  # The rows: one per checkpoint, in its order, the times going up.
  $rows = @($computed.Rows)
  if ((($rows | ForEach-Object { $_.D }) -join ',') -ne ($def -join ',')) { OrderFail $n ($what + ': rows ' + (($rows | ForEach-Object { $_.D }) -join ',')) }
  $times = @($rows | ForEach-Object { if ($null -eq $_.T) { -1 } else { [double]$_.T } })
  if (-not (Ascending $times)) { OrderFail $n ($what + ': times of the rows not going up: ' + ($times -join ',')) }
  # The turns: every wall in order, the changes of a relay where its legs end, the styles of the medley.
  $turns = @($computed.Turns)
  if ((($turns | ForEach-Object { $_.Wall }) -join ',') -ne ($walls -join ',')) { OrderFail $n ($what + ': turns at ' + (($turns | ForEach-Object { $_.Wall }) -join ',')) }
  foreach ($tc in $turns) {
    $exchange = $relay -and ($tc.Wall % $leg -eq 0)
    if ([bool]$tc.Exchange -ne $exchange) { OrderFail $n ($what + ': the wall ' + $tc.Wall + ' change of a relay ' + $tc.Exchange) }
    $sin = [string]$styleAt.Invoke($null, @($race, [double]($tc.Wall - 0.5))); $sout = [string]$styleAt.Invoke($null, @($race, [double]($tc.Wall + 0.5)))
    if ($race.Style -eq 'im' -and ($tc.StyleIn -ne $sin -or $tc.StyleOut -ne $sout)) { OrderFail $n ($what + ': the wall ' + $tc.Wall + ' ' + $tc.StyleIn + ' → ' + $tc.StyleOut + ' (' + $sin + ' → ' + $sout + ')') }
  }
  # The phases: the start, then a turn at each wall that is not a change, in the order of the race; the events of each in order.
  $ph = @($phases)
  $turnWalls = @($walls | Where-Object { -not ($relay -and ($_ % $leg -eq 0)) })
  if ($ph.Count -lt 1 -or $ph[0].Kind.ToString() -ne 'Start') { OrderFail $n ($what + ': the first phase is not the start') }
  $pt = @($ph | Where-Object { $_.Kind.ToString() -eq 'Turn' })
  if ((($pt | ForEach-Object { $_.Wall }) -join ',') -ne ($turnWalls -join ',')) { OrderFail $n ($what + ': turn phases at ' + (($pt | ForEach-Object { $_.Wall }) -join ',')) }
  if (-not (Ascending @($ph | ForEach-Object { [double]$_.Origin }))) { OrderFail $n ($what + ': phases not in the order of the race') }
  foreach ($p in $ph) {
    $s15 = $null; if ($p.Splits.ContainsKey(15)) { $s15 = $p.Splits[15] }
    if ($p.Kind.ToString() -eq 'Start') {
      # (Lists built item by item: a subexpression of one item is that item, and a number + a list fails.)
      $seq = @(0, $p.Reaction, $p.Block, $p.Entry, $p.Breakout); if ($null -ne $s15) { $seq += $s15 }
      if (@($seq | Where-Object { $null -eq $_ }).Count -gt 0 -or -not (Ascending $seq)) { OrderFail $n ($what + ': start signal, first movement, release, entry, breakout, 15: ' + ($seq -join ' / ')) }
      $from = $p.Entry
    } else {
      $m5 = $null; if ($p.Splits.ContainsKey(-5)) { $m5 = $p.Splits[-5] }
      $seq = @(); if ($null -ne $m5) { $seq += $m5 }
      $seq += @($p.LastStroke, 0, $p.Pushoff, $p.Breakout); if ($null -ne $s15) { $seq += $s15 }
      if (@($seq | Where-Object { $null -eq $_ }).Count -gt 0 -or -not (Ascending $seq)) { OrderFail $n ($what + ': turn ' + $p.Wall + ' -5, last stroke, wall, push-off, breakout, +15: ' + ($seq -join ' / ')) }
      $from = $p.Pushoff
    }
    foreach ($k in $p.Kicks) { if ($null -ne $from -and $null -ne $p.Breakout -and ($k -le $from -or $k -ge $p.Breakout)) { OrderFail $n ($what + ': a kick of ' + $p.Id + ' at ' + $k + ' outside ' + $from + '–' + $p.Breakout) } }
  }

  # «Πέρασμα» at each checkpoint in order, the video paused and playing: the checkpoint of that frame (2.9.5b–2.35.1 an 800 in a
  # 50 m pool: S at the 45 marked the 50).
  $splits = @($race.Marks | Where-Object { $_.Type.ToString() -eq 'Split' } | Sort-Object Timestamp)
  foreach ($m in $splits) { [void]$race.Marks.Remove($m) }
  $wrong = 0
  foreach ($m in $splits) {
    $paused = Press $race $timing $m.Timestamp $false; $playing = Press $race $timing $m.Timestamp $true
    if ($paused -ne $m.Distance -or $playing -ne $m.Distance) { $wrong++; if ($wrong -le 2) { OrderFail $n ($what + ': S at ' + $m.Distance + ' marks ' + $paused + ' (paused) / ' + $playing + ' (playing)') } }
    $race.Marks.Add($m)
  }
  # Passing by the ones that can be told apart (half the way or less from the last one marked to the next one; not two in a row): S at
  # the next one's frame marks the next one; then back to each one passed by, between two marked ones.
  foreach ($m in $splits) { [void]$race.Marks.Remove($m) }
  $skipped = @(); $last = -1; $justSkipped = $false
  for ($k = 0; $k -lt $splits.Count; $k++) {
    $m = $splits[$k]
    if ($k + 1 -lt $splits.Count -and $last -ge 0 -and -not $justSkipped -and ($m.Distance - $last) / ($splits[$k + 1].Distance - $last) -le 0.5) {
      $nx = $splits[$k + 1]; $got = Press $race $timing $nx.Timestamp $false
      if ($got -ne $nx.Distance) { $wrong++; if ($wrong -le 2) { OrderFail $n ($what + ': S at ' + $nx.Distance + ' with ' + $m.Distance + ' passed by marks ' + $got) } }
      $race.Marks.Add($nx); $skipped += $m; $last = $nx.Distance; $justSkipped = $true; $k++
      continue
    }
    $got = Press $race $timing $m.Timestamp $false
    if ($got -ne $m.Distance) { $wrong++; if ($wrong -le 2) { OrderFail $n ($what + ': S at ' + $m.Distance + ' (with some passed by) marks ' + $got) } }
    $race.Marks.Add($m); $last = $m.Distance; $justSkipped = $false
  }
  foreach ($m in $skipped) {
    $got = Press $race $timing $m.Timestamp $false
    if ($got -ne $m.Distance) { $wrong++; if ($wrong -le 2) { OrderFail $n ($what + ': S back at ' + $m.Distance + ' marks ' + $got) } }
    $race.Marks.Add($m)
  }
  if ((Press $race $timing ($splits[$splits.Count - 1].Timestamp + 1000) $false) -ne -1) { OrderFail $n ($what + ': S after all the checkpoints marks one') }
  if ($skipped.Count -gt 0) { $script:passedBy += $skipped.Count }
  $script:ordered++
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
    if ($f.Count -gt 5 -and $f[5] -eq 'mx') { CheckOrder $n $race $timing $computed $phases }
  }
  catch {
    $fails++
    $cause = Cause $_.Exception
    if ($fails -le 12) { Write-Output ('FAIL analysis ' + $n + ' (mutation ' + $f[4] + '): ' + $cause) }
    [System.IO.File]::WriteAllText($outPath, ('{"error":' + [string]$serialize.Invoke($null, (,[string]$cause)) + '}'), $utf8)
  }
}

if ($orderFails -gt 0) { Write-Output ('FAILED: ' + $orderFails + ' checks of the order of the events') }
if ($fails -gt 0 -or $orderFails -gt 0) { Write-Output ('FAILED: ' + $fails + ' of ' + $lines.Count + ' analyses, ' + $orderFails + ' of the order'); exit 1 }
if ($ordered -lt 60 -or $passedBy -lt 100) { Write-Output ('FAILED: only ' + $ordered + ' events in the order of the race, ' + $passedBy + ' checkpoints passed by'); exit 1 }
Write-Output ('fuzz marks ok: ' + $done + ' fictional analyses computed by the program; ' + $ordered + ' events in the order of the race, ' + $presses + ' presses of S, ' + $passedBy + ' checkpoints passed by and marked after')
