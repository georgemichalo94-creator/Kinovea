# be_ci.ps1 (2.37.0): the CI check of the right bar beside the video on FICTIONAL races (fm_make.js, no real athlete): the events of
# the race in their order for every event of the program (fm_make.js 'mx': every distance of the metre and yard pools, every style,
# medley, relays), the event the list follows, the box of the distances (typed, the wheel, the arrows, two decimals), the English of
# the new texts, and pictures of the bar in its three tabs (an artifact). Windows PowerShell 5.1 (.NET Framework, as Kinovea).
#   powershell -File be_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -dir <folder of fm_make.js> -out <folder>
param([string]$sm, [string]$bin, [string]$dir, [string]$out)
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
$static = [System.Reflection.BindingFlags]'Public,NonPublic,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

# A settings folder of its own (the manager of the pictures reads its files there).
$settingsDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-be-ci-' + [Guid]::NewGuid().ToString('N')) + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
$software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)

$tRace = T 'SwimRace'; $tTiming = T 'SwimTiming'; $tCalc = T 'SwimCalculator'; $tEvents = T 'SwimBarEvents'; $tPresets = T 'SwimPresets'
$tConvert = $json.GetType('Newtonsoft.Json.JsonConvert', $true)
$deserialize = $tConvert.GetMethod('DeserializeObject', [Type[]]@([string], [Type]))
$ensure = $tRace.GetMethod('EnsureLists', $all)
$compute = $tCalc.GetMethod('Compute'); $build = $tEvents.GetMethod('Build')
$currentIndex = $tEvents.GetMethod('CurrentIndex'); $nextIndex = $tEvents.GetMethod('NextIndex')
$styleAt = $tPresets.GetMethod('StyleAt')
$tCalc.GetField('LengthFromRate').SetValue($null, $true)
$utf8 = New-Object System.Text.UTF8Encoding($false)
$inv = [System.Globalization.CultureInfo]::InvariantCulture

function Load($n, $f) {
  $text = [System.IO.File]::ReadAllText([System.IO.Path]::Combine($dir, 'race_' + $n + '.json'), $utf8)
  $race = $deserialize.Invoke($null, @([string]$text, [Type]$tRace))
  [void]$ensure.Invoke($race, @())
  $timing = [Activator]::CreateInstance($tTiming)
  $timing.AverageTimestampsPerFrame = [double]::Parse($f[1], $inv); $timing.FrameIntervalMilliseconds = [double]::Parse($f[2], $inv); $timing.HighSpeedFactor = [double]::Parse($f[3], $inv)
  $c = $compute.Invoke($null, @($race, $timing, [double]0.5))
  return @{ race = $race; timing = $timing; computed = $c }
}
# The list itself (the unary comma: PowerShell would unroll it), for the methods that take it.
function EventList($race, $c) { return ,($build.Invoke($null, @($race, $c))) }
function Time($e) { if ($null -ne $e.SeekTime) { return $e.SeekTime } else { return $e.Time } }

# ---- The events of every event of the program, in their order.
$orderFails = 0; $checkedRaces = 0; $kinds = @{}
function Fail($n, $what) { $script:orderFails++; if ($script:orderFails -le 30) { Write-Output ('FAIL events ' + $n + ': ' + $what) } }
$cases = [System.IO.File]::ReadAllLines([System.IO.Path]::Combine($dir, 'cases.txt'), $utf8)
$picks = @{}
foreach ($line in $cases) {
  if ([string]::IsNullOrWhiteSpace($line)) { continue }
  $f = $line.Split(';')
  if ($f.Count -lt 6 -or $f[5] -ne 'mx') { continue }
  $n = $f[0]
  try {
    $l = Load $n $f
    $race = $l.race; $c = $l.computed
    $list = EventList $race $c
    $ev = @($list)
    $what = '' + $race.DistanceLabel + $race.PoolUnit + ' ' + $race.Style + ' pool ' + $race.Pool
    $kinds[$what] = $true
    if ($ev.Count -eq 0 -or $ev[0].Id -ne 'start') { Fail $n ($what + ': no start first'); continue }
    $ids = @($ev | ForEach-Object { $_.Id })
    $dup = @($ids | Group-Object | Where-Object { $_.Count -gt 1 })
    if ($dup.Count -gt 0) { Fail $n ($what + ': an id twice ' + $dup[0].Name) }

    # Every checkpoint once, in the order of the distance; the finish last.
    $cps = @($ev | Where-Object { $_.Checkpoint -gt 0 } | ForEach-Object { [int]$_.Checkpoint })
    $want = @($race.Checkpoints | ForEach-Object { [int]$_ } | Sort-Object)
    if (($cps -join ',') -ne ($want -join ',')) { Fail $n ($what + ': checkpoints ' + ($cps -join ',') + ' instead of ' + ($want -join ',')) }
    if ($ev[$ev.Count - 1].Id -ne ('cp' + $race.Distance)) { Fail $n ($what + ': the last event is ' + $ev[$ev.Count - 1].Id) }

    # Each wall: the touch, the push off (or the next swimmer off the block), the kicks and the breakout in one group, named for what it is.
    for ($w = $race.Pool; $w -lt $race.Distance; $w += $race.Pool) {
      $group = @($ev | Where-Object { $_.Id -eq ('pushoff' + $w) -or $_.Id -eq ('kicks' + $w) -or $_.Id -eq ('breakout' + $w) })
      if ($group.Count -ne 3) { Fail $n ($what + ': the turn at ' + $w + ' has ' + $group.Count + ' events'); continue }
      $touch = @($ev | Where-Object { $_.Id -eq ('cp' + $w) })
      if ($touch.Count -eq 1 -and $touch[0].Group -ne $group[0].Group) { Fail $n ($what + ': the touch at ' + $w + ' is not in the group of its turn') }
      $exchange = $race.Relay -and ($race.Distance % 4 -eq 0) -and ($w % ($race.Distance / 4) -eq 0)
      $change = ($styleAt.Invoke($null, @($race, [double]$w)) -ne $styleAt.Invoke($null, @($race, [double]($w + $race.Pool))))
      $title = $group[0].Group
      if ($exchange -and $title -notlike 'ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ*') { Fail $n ($what + ': the exchange at ' + $w + ' is "' + $title + '"') }
      elseif (-not $exchange -and $change -and $title -notlike 'ΑΛΛΑΓΗ ΣΤΥΛ*') { Fail $n ($what + ': the change of style at ' + $w + ' is "' + $title + '"') }
      elseif (-not $exchange -and -not $change -and $title -notlike 'ΣΤΡΟΦΗ*') { Fail $n ($what + ': the turn at ' + $w + ' is "' + $title + '"') }
      $b = @($group | Where-Object { $_.Id -eq ('breakout' + $w) })[0]
      if ([string]$b.Distance -ne 'TurnBreakout' -or $b.Wall -ne $w) { Fail $n ($what + ': the breakout of ' + $w + ' does not change its distance') }
    }

    # The checkpoints with a time in the order of the race; the events of the start in theirs; each push off after its touch.
    $last = -1.0; $lastId = ''
    foreach ($e in $ev) {
      $t = Time $e
      if ($null -eq $t -or $e.Checkpoint -le 0) { continue }
      if ($t -lt $last - 1e-6) { Fail $n ($what + ': ' + $e.Id + ' at ' + $t + ' before ' + $lastId + ' at ' + $last); break }
      $last = $t; $lastId = $e.Id
    }
    $startTimes = @($ev | Where-Object { @('start', 'reaction', 'block', 'entry') -contains $_.Id -and $null -ne $_.Time } | ForEach-Object { [double]$_.Time })
    for ($k = 1; $k -lt $startTimes.Count; $k++) { if ($startTimes[$k] -lt $startTimes[$k - 1] - 1e-6) { Fail $n ($what + ': the events of the start out of order'); break } }
    for ($w = $race.Pool; $w -lt $race.Distance; $w += $race.Pool) {
      $touch = @($ev | Where-Object { $_.Id -eq ('cp' + $w) })
      $push = @($ev | Where-Object { $_.Id -eq ('pushoff' + $w) })
      $isExchange = $race.Relay -and ($race.Distance % 4 -eq 0) -and ($w % ($race.Distance / 4) -eq 0)
      if (-not $isExchange -and $touch.Count -eq 1 -and $push.Count -eq 1 -and $null -ne $touch[0].Time -and $null -ne $push[0].Time -and $push[0].Time -lt $touch[0].Time - 1e-6) {
        Fail $n ($what + ': the push off at ' + $w + ' before its touch'); break
      }
    }
    $lastStroke = @($ev | Where-Object { $_.Id -eq 'last' })[0]
    $finish = $ev[$ev.Count - 1]
    if ($null -ne $lastStroke.Time -and $null -ne $finish.Time -and $lastStroke.Time -gt $finish.Time + 1e-6) { Fail $n ($what + ': the last stroke after the finish') }
    $marked = @($ev | Where-Object { $_.Checkpoint -gt 0 -and $_.Marked -and $null -eq $_.Time })
    if ($marked.Count -gt 0) { Fail $n ($what + ': a marked checkpoint without a time (' + $marked[0].Id + ')') }
    $distances = @($ev | Where-Object { [string]$_.Distance -ne 'None' })
    $turns = [Math]::Max(0, [int][Math]::Ceiling($race.Distance / $race.Pool) - 1)
    if ($distances.Count -ne 3 + $turns) { Fail $n ($what + ': ' + $distances.Count + ' distances to change instead of ' + (3 + $turns)) }

    # The event the list follows: at the time of each event, that event (or a later one at the same time); before the start none.
    if ([int]$currentIndex.Invoke($null, [object[]]@($list, [System.Nullable[double]](-1.0))) -ne -1) { Fail $n ($what + ': an event before the start') }
    for ($i = 0; $i -lt $ev.Count; $i++) {
      $t = Time $ev[$i]
      if ($null -eq $t -or $ev[$i].Checkpoint -le 0) { continue }
      $at = [int]$currentIndex.Invoke($null, [object[]]@($list, [System.Nullable[double]]$t))
      if ($at -lt $i -or [Math]::Abs((Time $ev[$at]) - $t) -gt 0.006) { Fail $n ($what + ': at ' + $t + ' the list follows ' + $at + ' instead of ' + $i); break }
    }
    $next = [int]$nextIndex.Invoke($null, [object[]]@($list))
    $firstOpen = -1
    for ($i = 0; $i -lt $ev.Count; $i++) { if ($ev[$i].Checkpoint -gt 0 -and -not $ev[$i].Marked) { $firstOpen = $i; break } }
    if ($next -ne $firstOpen) { Fail $n ($what + ': the next checkpoint ' + $next + ' instead of ' + $firstOpen) }

    $checkedRaces++
    $key = '' + $race.Distance + '|' + $race.Pool + '|' + $race.PoolUnit + '|' + $race.Style + '|' + $race.Relay
    if (-not $picks.ContainsKey($key)) { $picks[$key] = @{ n = $n; f = $f } }
  }
  catch {
    $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
    Fail $n ('error: ' + $x.GetType().Name + ': ' + $x.Message)
  }
}
Check ($checkedRaces -ge 60 -and $orderFails -eq 0) ('the events in their order: ' + $checkedRaces + ' races of ' + $kinds.Count + ' events, ' + $orderFails + ' problems')

# ---- The box of the distances.
$tBox = T 'SwimDecimalBox'
$box = [Activator]::CreateInstance($tBox)
$changed = 0
$box.add_NumberChanged({ $script:changed++ })
$box.Seed = [decimal]3
Check ($null -eq $box.Number -and $box.Text -eq '') ('a new box is empty: "' + $box.Text + '"')
$turn = $tBox.GetMethod('Turn', $all)
function Wheel($b, $notches, $keys) { [void]$turn.Invoke($b, [object[]]@([int]$notches, [System.Windows.Forms.Keys]$keys)) }
Wheel $box 1 'None'
Check ($box.Number -eq 3 -and $box.Text -eq '3.00' -and $changed -eq 1) ('from empty the wheel goes to the usual value: ' + $box.Text)
Wheel $box 3 'None'
Check ([Math]::Abs($box.Number - 3.03) -lt 1e-9 -and $box.Text -eq '3.03') ('three notches: 0.03 more (' + $box.Text + ')')
Wheel $box 1 'Control'
Check ([Math]::Abs($box.Number - 3.13) -lt 1e-9) ('Ctrl: 0.10 a notch (' + $box.Text + ')')
Wheel $box -1 'Shift'
Check ([Math]::Abs($box.Number - 2.13) -lt 1e-9 -and $changed -eq 4) ('Shift: 1.00 a notch (' + $box.Text + ')')
$box.Number = 3.456
Check ([Math]::Abs($box.Number - 3.46) -lt 1e-9 -and $box.Text -eq '3.46' -and $changed -eq 4) ('set by the program: two decimals, no change of the user (' + $box.Text + ')')
$box.Number = 49.99
Wheel $box 1 'Shift'
Check ($box.Number -eq 50 -and $box.Text -eq '50.00') ('not past the most (' + $box.Text + ')')
$box.Number = 0.01
Wheel $box -3 'None'
Check ($box.Number -eq 0 -and $box.Text -eq '0.00') ('not under 0 (' + $box.Text + ')')
$box.Number = $null
Check ($null -eq $box.Number -and $box.Text -eq '') 'emptied by the program'
# Typed with a comma, read while typed, written with two decimals when confirmed.
$box.Text = '2,5'
Check ([Math]::Abs($box.Number - 2.5) -lt 1e-9) ('typed "2,5": ' + $box.Number)
$tBox.GetMethod('ValidateEditText', $all).Invoke($box, $null) | Out-Null
Check ($box.Text -eq '2.50' -and [Math]::Abs($box.Number - 2.5) -lt 1e-9) ('confirmed: ' + $box.Text)
$box.Text = 'abc'
$tBox.GetMethod('ValidateEditText', $all).Invoke($box, $null) | Out-Null
Check ($box.Text -eq '2.50') ('not a number: the value stays (' + $box.Text + ')')
$box.Text = ''
$tBox.GetMethod('ValidateEditText', $all).Invoke($box, $null) | Out-Null
Check ($null -eq $box.Number) 'emptied by the user: no value'
$box.UpButton()
Check ($box.Number -eq 3) ('the arrow from empty: the usual value (' + $box.Text + ')')
$box.UpButton()
Check ([Math]::Abs($box.Number - 3.01) -lt 1e-9) ('the arrow: 0.01 (' + $box.Text + ')')
$box.Dispose()
$lane = [Activator]::CreateInstance($tBox)
$lane.DecimalPlaces = 0; $lane.Increment = [decimal]1; $lane.Maximum = [decimal]10; $lane.Accelerations.Clear()
$lane.Number = 4
Wheel $lane 1 'Control'
Check ($lane.Number -eq 5 -and $lane.Text -eq '5') ('a whole number (a lane): steps of 1 (' + $lane.Text + ')')
$lane.Dispose()
$read = $tBox.GetMethod('Read', $static); $format = $tBox.GetMethod('Format', $static)
Check ([Math]::Abs($read.Invoke($null, @(' 3,05 ')) - 3.05) -lt 1e-9 -and [Math]::Abs($read.Invoke($null, @('7.5')) - 7.5) -lt 1e-9 -and $null -eq $read.Invoke($null, @('x'))) 'read with a point or a comma'
Check ($format.Invoke($null, @([System.Nullable[double]]3)) -eq '3.00' -and $format.Invoke($null, @([System.Nullable[double]]$null)) -eq '') 'written with two decimals'
$wheelDistance = (T 'FormSwimAnalysis').GetMethod('WheelDistance', $static)
$w1 = $wheelDistance.Invoke($null, [object[]]@('3.05', 2, [System.Windows.Forms.Keys]::None, [decimal]8))
$w2 = $wheelDistance.Invoke($null, [object[]]@('', 1, [System.Windows.Forms.Keys]::None, [decimal]8))
$w3 = $wheelDistance.Invoke($null, [object[]]@('49,5', 1, [System.Windows.Forms.Keys]::Shift, [decimal]8))
Check ($w1 -eq '3.07' -and $w2 -eq '8.00' -and $w3 -eq '50.00') ('the wheel in the cells of the turns: ' + $w1 + ' ' + $w2 + ' ' + $w3)

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $static).GetValue($null)
$keys = @('ΓΕΓΟΝΟΤΑ', 'ΑΓΩΝΑΣ', 'ΓΕΓΟΝΟΣ', 'ΤΙΜΗ', 'ΚΟΛΥΜΒΗΣΗ {0}–{1}{2}', 'ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ {0}{1}', 'ΑΛΛΑΓΗ ΣΤΥΛ {0}{1}', 'ΣΤΡΟΦΗ {0}{1}',
  'Τελ. χεριά → τοίχος', 'Ξεκίνημα επόμενου', 'Όλα στον πίνακα  ↗', '✓ επίσημοι χρόνοι', ' χερ.',
  'Κλικ: γράψε την απόσταση ή γύρνα τη ροδέλα (0.01 · Ctrl 0.10 · Shift 1.00), Enter για να μπει, Esc για ακύρωση.',
  'Γράψε την απόσταση (π.χ. 3.05 ή 3,05) ή γύρνα τη ροδέλα: 0.01 ανά βήμα · Ctrl 0.10 · Shift 1.00. ')
$missing = @($keys | Where-Object { -not $map.ContainsKey($_) })
Check ($missing.Count -eq 0) ('the English of the new texts (' + $missing.Count + ' missing: ' + ($missing -join ' | ') + ')')

# ---- Pictures of the right bar in its three tabs (a fictional 100 m and a medley relay, at a moment of the race).
if ($out) {
  try {
    $mgrType = T 'SwimAnalysisManager'
    $mgr = $mgrType.GetProperty('Instance').GetValue($null)
    $settings = [Activator]::CreateInstance((T 'SwimSettings'))
    $mgrType.GetProperty('Settings').SetValue($mgr, $settings)
    $tBar = T 'SwimVideoBar'
    $shots = @(
      @{ key = '100|50|m|fs|False'; name = 'bar_events_100free.png'; tab = 0; at = 0.55 },
      @{ key = '100|50|m|fs|False'; name = 'bar_splits_100free.png'; tab = 1; at = 0.55 },
      @{ key = '100|50|m|fs|False'; name = 'bar_race_100free.png'; tab = 2; at = 0.55 },
      @{ key = '400|50|m|im|True'; name = 'bar_events_4x100medley.png'; tab = 0; at = 0.30 },
      @{ key = '200|25|m|im|False'; name = 'bar_events_200im_25m.png'; tab = 0; at = 0.45 }
    )
    foreach ($s in $shots) {
      if (-not $picks.ContainsKey($s.key)) { Write-Output ('picture ' + $s.name + ': no such race'); continue }
      $p = $picks[$s.key]
      $l = Load $p.n $p.f
      $l.race.Athlete = 'Αθλητής Δείγμα'; $l.race.Competition = 'Αγώνας δείγμα'; $l.race.Location = 'Πόλη δείγμα'; $l.race.Round = 'Τελικός'; $l.race.Lane = 4
      $mgrType.GetProperty('Race').SetValue($mgr, $l.race)
      $mgrType.GetProperty('Computed').SetValue($mgr, $l.computed)
      $mgrType.GetProperty('Timing').SetValue($mgr, $l.timing)
      $settings.BarListTab = $s.tab
      $bar = [Activator]::CreateInstance($tBar, [object[]]@($mgr, $false))
      try {
        $final = $l.computed.FinalTime
        if ($null -ne $final) { $tBar.GetField('PreviewTime', $all).SetValue($bar, [System.Nullable[double]]($final * $s.at)) }
        $bar.Size = New-Object System.Drawing.Size 248, 980
        $bmp = New-Object System.Drawing.Bitmap 248, 980
        $bar.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, 248, 980))
        $bmp.Save([System.IO.Path]::Combine($out, $s.name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        Write-Output ('picture: ' + $s.name)
      } finally { $bar.Dispose() }
    }
  } catch {
    $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
    Check $false ('pictures of the bar: ' + $x.GetType().Name + ': ' + $x.Message + ' @ ' + (($x.StackTrace -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' < '))
  }
}

try { Remove-Item -Recurse -Force $settingsDir } catch { }
Write-Output ('Bar events: ' + $fails + ' checks failed')
if ($fails -gt 0) { exit 1 }
