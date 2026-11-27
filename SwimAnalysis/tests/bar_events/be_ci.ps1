# be_ci.ps1 (2.37.0): the CI check of the right bar beside the video on FICTIONAL races (fm_make.js, no real athlete): the events of
# the race in their order for every event of the program (fm_make.js 'mx': every distance of the metre and yard pools, every style,
# medley, relays), the event the list follows, the box of the distances (typed, the wheel, the arrows, two decimals), the English of
# the new texts, and pictures of the bar in its three tabs (an artifact); (2.39.1) the 📍 beside the marks of the left bar at the start
# and its pictures, (2.39.2) in the tab of the turn before the first wall. Windows PowerShell 5.1 (.NET Framework, as Kinovea).
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
    $next = [int]$nextIndex.Invoke($null, [object[]]@(,$list))
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
# (2.38.0) With Windows in Greek (the point is the thousands separator there): a point too many while typing keeps the number as it
# was (it became 330, then the most of the box, 50.00, in the window that keeps each key), and it is dropped when confirmed.
$culture = [System.Threading.Thread]::CurrentThread.CurrentCulture
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('el-GR')
try {
  # Typed: into the text box of the box itself (its Text set by a program is confirmed at once).
  $edit = @($box.Controls | Where-Object { $_ -is [System.Windows.Forms.TextBox] })[0]
  $box.Number = 3.30
  $edit.Text = '3.30.'
  $typing = $box.Number
  Check ([Math]::Abs($typing - 3.30) -lt 1e-9 -and $box.Text -eq '3.30.') ('Windows in Greek, "3.30." being typed: the number as it was (' + $typing + ', "' + $box.Text + '")')
  $edit.Text = '3.303.30'
  $typing = $box.Number
  $tBox.GetMethod('ValidateEditText', $all).Invoke($box, $null) | Out-Null
  Check ([Math]::Abs($typing - 3.30) -lt 1e-9 -and $box.Text -eq '3.30' -and [Math]::Abs($box.Number - 3.30) -lt 1e-9) ('Windows in Greek, "3.303.30" confirmed: dropped (' + $typing + ', "' + $box.Text + '")')
  $box.Text = '3,45'
  Check ([Math]::Abs($box.Number - 3.45) -lt 1e-9) ('Windows in Greek, typed with a comma: ' + $box.Number)
  $box.Text = '3.45'
  Check ([Math]::Abs($box.Number - 3.45) -lt 1e-9) ('Windows in Greek, typed with a point: ' + $box.Number)
}
finally {
  [System.Threading.Thread]::CurrentThread.CurrentCulture = $culture
}
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

# ---- (2.38.1) The wheel in the edited cell of the distance under the water, in the panel of the analysis (a fictional 100 m, its
# turn): over the cell it turns the distance (the parts of a notch of a fine wheel add up) and the list does not move; elsewhere it
# changes nothing; the value goes into the race at the end of the edit. Then the wheel as Windows sends it, to the editor of the cell
# (the editor gives it to the grid: in 2.37.0 the wheel there did nothing).
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BeWheel
{
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Pt p);
    struct Pt { public int X; public int Y; }
    public static int[] Cursor() { Pt p; return GetCursorPos(out p) ? new int[] { p.X, p.Y } : null; }
    public static void Wheel(IntPtr hWnd, int delta, int x, int y)
    {
        SendMessage(hWnd, 0x020A, new IntPtr((delta & 0xFFFF) << 16), new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF)));
    }
}
'@
# (2.38.3) A field that can be empty (double?) set by reflection: PowerShell 5.1 does not assign one from a [Nullable[double]] value.
function SetField($obj, $name, $value) { $b = $obj.psobject.BaseObject; $v = $null; if ($null -ne $value) { $v = [double]$value }; $b.GetType().GetField($name).SetValue($b, $v) }
$form = $null
try {
  $mgrType = T 'SwimAnalysisManager'
  $mgr = $mgrType.GetProperty('Instance').GetValue($null)
  $mgrType.GetProperty('Settings').SetValue($mgr, [Activator]::CreateInstance((T 'SwimSettings')))
  $p = $picks['100|50|m|fs|False']
  $l = Load $p.n $p.f
  $mgrType.GetProperty('Race').SetValue($mgr, $l.race)
  $mgrType.GetProperty('Computed').SetValue($mgr, $l.computed)
  $mgrType.GetProperty('Timing').SetValue($mgr, $l.timing)
  $tForm = T 'FormSwimAnalysis'
  $form = [Activator]::CreateInstance($tForm, [object[]]@($mgr))
  $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
  $form.ShowInTaskbar = $false
  $form.Location = [System.Drawing.Point]::new(0, 0)
  $form.Size = [System.Drawing.Size]::new(1000, 700)
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  [void]$tForm.GetMethod('RefreshAll').Invoke($form, @())
  $form.PageIndex = 3; [System.Windows.Forms.Application]::DoEvents()
  if ($form.PageIndex -ne 3) { throw ('the page of the turns is not shown: ' + $form.PageIndex) }
  $grid = $tForm.GetField('gridTurns', $all).GetValue($form)
  if ($grid.Rows.Count -lt 1 -or -not ($grid.Rows[0].Tag -is [int])) { throw ('no turn in the grid: ' + $grid.Rows.Count) }
  $wall = [int]$grid.Rows[0].Tag
  $col = $grid.Columns['uwDist'].Index
  $grid.CurrentCell = $grid.Rows[0].Cells[$col]
  [void]$grid.BeginEdit($true)
  $editor = $grid.EditingControl
  if ($null -eq $editor) { throw 'the cell is not edited' }
  $editor.Text = '7.50'
  $cellArea = $grid.RectangleToScreen($grid.GetCellDisplayRectangle($col, 0, $false))
  # (::new, not New-Object: a value of New-Object comes wrapped, and the reflection below would not take it.)
  $over = [System.Drawing.Point]::new($cellArea.X + [int]($cellArea.Width / 2), $cellArea.Y + [int]($cellArea.Height / 2))
  $away = [System.Drawing.Point]::new($cellArea.X + [int]($cellArea.Width / 2), $cellArea.Bottom + 40)
  $turnsWheel = $tForm.GetMethod('TurnsWheel', $all)
  function TurnsWheel($delta, $point, $keys) {
    $h = [System.Windows.Forms.HandledMouseEventArgs]::new([System.Windows.Forms.MouseButtons]::None, 0, 0, 0, [int]$delta)
    $took = $turnsWheel.Invoke($form, [object[]]@($h, [int]$delta, [System.Drawing.Point]$point, [System.Windows.Forms.Keys]$keys))
    return @{ took = $took; handled = $h.Handled; text = $editor.Text }
  }
  $r1 = TurnsWheel 120 $over 'None'
  $r2 = TurnsWheel -120 $over 'Control'
  $r3 = TurnsWheel 60 $over 'None'
  $r4 = TurnsWheel 60 $over 'None'
  $r5 = TurnsWheel 120 $away 'Shift'
  Check ($r1.took -and $r1.handled -and $r1.text -eq '7.51') ('the wheel over the edited cell of the turns: ' + $r1.text)
  Check ($r2.took -and $r2.text -eq '7.41') ('there with Ctrl a tenth: ' + $r2.text)
  Check ($r3.took -and $r3.handled -and $r3.text -eq '7.41' -and $r4.text -eq '7.42') ('the parts of a notch of a fine wheel add up: ' + $r3.text + ' ' + $r4.text)
  Check (-not $r5.took -and -not $r5.handled -and $r5.text -eq '7.42') ('away from the cell the wheel changes nothing: ' + $r5.text)
  $tip = [string]$grid.Columns['uwDist'].ToolTipText
  Check ($tip.Contains('F2') -and $tip.Contains('Esc') -and -not $tip.Contains([string][char]0x25B2)) ('the help of the column of the turns, without the arrows of the boxes: ' + $tip)

  # The wheel as Windows sends it: to the editor of the cell with the mouse over the cell, then with the mouse away from it.
  $want = 7.42
  [void][BeWheel]::SetCursorPos($over.X, $over.Y)
  $at = [BeWheel]::Cursor()
  if ($null -ne $at -and $at[0] -eq $over.X -and $at[1] -eq $over.Y) {
    [BeWheel]::Wheel($editor.Handle, 120, $over.X, $over.Y); [System.Windows.Forms.Application]::DoEvents()
    $s1 = $editor.Text
    [BeWheel]::Wheel($editor.Handle, -240, $over.X, $over.Y); [System.Windows.Forms.Application]::DoEvents()
    $s2 = $editor.Text
    [void][BeWheel]::SetCursorPos($away.X, $away.Y)
    [BeWheel]::Wheel($editor.Handle, 120, $away.X, $away.Y); [System.Windows.Forms.Application]::DoEvents()
    $s3 = $editor.Text
    Check ($s1 -eq '7.43' -and $s2 -eq '7.41' -and $s3 -eq '7.41') ('the wheel sent to the editor of the cell as Windows sends it: ' + $s1 + ' ' + $s2 + ' ' + $s3)
    $want = 7.41
  } else {
    Write-Output ('note: the wheel as Windows sends it not checked (the mouse of this machine did not move: ' + ($at -join ',') + ')')
  }

  # (2.38.3) As if the distance there was the estimate from the times (≈), with a video open: the end of the edit is also an answer
  # for the learning, kept once the distance has settled.
  $estTurn = $l.race.GetTurn($wall, $true); SetField $estTurn 'UnderwaterDist' 8.0; $estTurn.UnderwaterEstimated = $true
  $mgrType.GetField('videoPath', $all).SetValue($mgr, 'C:\ci\race_ci_values.mp4')

  # The value into the race at the end of the edit.
  [void]$grid.EndEdit(); [System.Windows.Forms.Application]::DoEvents()
  $manual = $l.race.GetTurn($wall, $false)
  $got = $null
  if ($null -ne $manual -and $null -ne $manual.UnderwaterDist) { $got = [double]$manual.UnderwaterDist }
  Check ($null -ne $got -and [Math]::Abs($got - $want) -lt 1e-9) ('the distance into the race at the end of the edit: ' + $got)
  $gridWaiting = $mgrType.GetField('pendingValues', $all).GetValue($mgr).Count
  [void]$mgrType.GetMethod('FlushValueAnswers', $all).Invoke($mgr, @())
  $gridAnswer = @($mgr.Learning.Records | Where-Object { $_.Kind -eq 'race.underwater' })
  $gridText = '-'
  if ($gridAnswer.Count -gt 0) { $gridText = [string]$gridAnswer[0].Measures['shown'] + ' > ' + [string]$gridAnswer[0].Measures['kept'] }
  Check ($gridWaiting -eq 1 -and $gridAnswer.Count -eq 1 -and [Math]::Abs($gridAnswer[0].Measures['shown'] - 8.0) -lt 1e-9 -and [Math]::Abs($gridAnswer[0].Measures['kept'] - $want) -lt 1e-9) ('(2.38.3) the distance of the grid written over the estimate: an answer once settled (' + $gridWaiting + ' waiting, then ' + $gridAnswer.Count + ': ' + $gridText + ')')
} catch {
  $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
  Check $false ('the wheel in the edited cell of the turns: ' + $x.GetType().Name + ': ' + $x.Message + ' @ ' + (($x.StackTrace -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' < '))
} finally {
  if ($null -ne $form) { try { $form.Close(); $form.Dispose() } catch { } }
}

# ---- (2.38.3) The answers about the values in the learning (numbers only, a fictional 100 m): the strokes of a segment (the count
# of its marks and the count kept) and the underwater distance written or measured over the estimate (≈), once it has settled; the
# same segment or wall again replaces its answer, a distance taken away takes it back, a distance without an estimate says nothing.
try {
  $mgrType = T 'SwimAnalysisManager'
  $mgr = $mgrType.GetProperty('Instance').GetValue($null)
  $tLearning = T 'SwimLearning'
  $valueAnswer = $mgrType.GetMethod('ValueAnswer', $static)
  $m0 = [System.Collections.Generic.Dictionary[string, double]]::new()
  $m0['auto'] = 13; $m0['bad'] = [double]::NaN
  $r0 = $valueAnswer.Invoke($null, [object[]]@('race.strokes', 'v1', 'abcdefghijklmnop', [int]35, [double]14, [double]15, $m0, [DateTime]::Now))
  Check ($r0.Kind -eq 'race.strokes' -and $r0.Key -eq 'v1|abcdefghijkl|race.strokes|35' -and -not $r0.Right -and [Math]::Abs($r0.Error - 1) -lt 1e-9 -and
    $r0.Measures['shown'] -eq 14 -and $r0.Measures['kept'] -eq 15 -and $r0.Measures['auto'] -eq 13 -and -not $r0.Measures.ContainsKey('bad')) ('an answer about a value: ' + $r0.Key + ', error ' + $r0.Error)
  $r1 = $valueAnswer.Invoke($null, [object[]]@('race.strokes', 'v1', 'r', [int]35, [double]14.5, [double]14.504, $null, [DateTime]::Now))
  $r2 = $valueAnswer.Invoke($null, [object[]]@('race.strokes', 'v1', 'r', [int]35, [double]::NaN, [double]14, $null, [DateTime]::Now))
  Check ($r1.Right -and $null -eq $r2) 'the same value is a right answer; not a number: no answer'

  $mgrType.GetProperty('Settings').SetValue($mgr, [Activator]::CreateInstance((T 'SwimSettings')))
  $p = $picks['100|50|m|fs|False']
  $l = Load $p.n $p.f
  $mgrType.GetProperty('Race').SetValue($mgr, $l.race)
  $mgrType.GetProperty('Computed').SetValue($mgr, $l.computed)
  $mgrType.GetProperty('Timing').SetValue($mgr, $l.timing)
  $video = 'C:\ci\race_ci_values.mp4'
  $mgrType.GetField('videoPath', $all).SetValue($mgr, $video)
  $vc = [string]$tLearning.GetMethod('VideoCode').Invoke($null, @($video))
  $race12 = $l.race.Id.Substring(0, [Math]::Min(12, $l.race.Id.Length))
  $learning = $mgr.Learning
  $pending = $mgrType.GetField('pendingValues', $all).GetValue($mgr)
  $flush = $mgrType.GetMethod('FlushValueAnswers', $all)
  $barDistance = $mgrType.GetMethod('SetBarDistance', $all)
  $tBarDistance = T 'SwimBarDistance'
  function Answer($key) { return @($learning.Records | Where-Object { $_.Key -eq $key }) }
  # (A value given to a function comes wrapped: a plain double, or null, for the reflection.)
  function Bar($which, $wall, $value) { $v = $null; if ($null -ne $value) { $v = [double]$value }; [void]$barDistance.Invoke($mgr, [object[]]@([Enum]::Parse($tBarDistance, $which), [int]$wall, $v)) }

  # The turn in the bar, over the estimate, two steps of the wheel: one answer once settled, with the last distance.
  $wall = [int]$l.race.Pool
  $turn = $l.race.GetTurn($wall, $true); SetField $turn 'UnderwaterDist' 8.0; $turn.UnderwaterEstimated = $true
  $uwKey = $vc + '|' + $race12 + '|race.underwater|' + $wall
  Bar 'TurnBreakout' $wall 8.3
  Bar 'TurnBreakout' $wall 8.4
  $waiting = $pending.Count
  [void]$flush.Invoke($mgr, @())
  $ans = Answer $uwKey
  Check ($waiting -eq 1 -and $ans.Count -eq 1 -and $ans[0].Measures['shown'] -eq 8 -and [Math]::Abs($ans[0].Measures['kept'] - 8.4) -lt 1e-9 -and
    [Math]::Abs($ans[0].Error - 0.4) -lt 1e-9 -and -not $ans[0].Right -and $ans[0].Measures['style'] -eq 1 -and $ans[0].Measures['dive'] -eq 0) ('the turn in the bar over the estimate, the wheel twice: ' + $waiting + ' waiting, ' + $ans.Count + ' answer, error ' + $(if ($ans.Count -gt 0) { $ans[0].Error } else { '-' }))
  # Changed again later (the estimate gone): the same answer, from the same estimate.
  Bar 'TurnBreakout' $wall 9.0
  [void]$flush.Invoke($mgr, @())
  $ans = Answer $uwKey
  Check ($ans.Count -eq 1 -and $ans[0].Measures['shown'] -eq 8 -and $ans[0].Measures['kept'] -eq 9) ('changed again: the same answer from the same estimate (' + $ans.Count + ')')
  # Taken away: the answer goes (for the other Kinovea too).
  Bar 'TurnBreakout' $wall $null
  [void]$flush.Invoke($mgr, @())
  $ans = Answer $uwKey
  $forgot = @($learning.Forgotten | Where-Object { $_.Key -eq $uwKey }).Count
  Check ($ans.Count -eq 0 -and $forgot -ge 1) ('the distance taken away: its answer goes (' + $ans.Count + ' left, ' + $forgot + ' forgotten)')

  # The start: a distance without an estimate says nothing; over the estimate, the breakout distance is the answer.
  $startKey = $vc + '|' + $race12 + '|race.underwater|0'
  SetField $l.race 'UnderwaterDist' $null; $l.race.UnderwaterEstimated = $false; SetField $l.race 'BreakoutDist' $null
  Bar 'StartBreakout' 0 11.0
  $none = $pending.Count
  [void]$flush.Invoke($mgr, @())
  Check ($none -eq 0 -and (Answer $startKey).Count -eq 0) ('a distance of the start without an estimate: no answer (' + $none + ' waiting)')
  SetField $l.race 'BreakoutDist' $null; SetField $l.race 'UnderwaterDist' 11.4; $l.race.UnderwaterEstimated = $true
  Bar 'StartBreakout' 0 12.0
  [void]$flush.Invoke($mgr, @())
  $ans = Answer $startKey
  Check ($ans.Count -eq 1 -and [Math]::Abs($ans[0].Measures['shown'] - 11.4) -lt 1e-9 -and $ans[0].Measures['kept'] -eq 12 -and $ans[0].Measures['dive'] -eq 1) ('the start in the bar over the estimate: ' + $ans.Count + ' answer')

  # Measured (📍) over the estimate of the turn.
  SetField $turn 'UnderwaterDist' 7.0; $turn.UnderwaterEstimated = $true
  [void]$mgrType.GetMethod('StoreMeasurement', $all).Invoke($mgr, [object[]]@([Enum]::Parse((T 'SwimPickTarget'), 'TurnUnderwater'), [int]$wall, [double]7.25))
  [void]$flush.Invoke($mgr, @())
  $ans = Answer $uwKey
  Check ($ans.Count -eq 1 -and $ans[0].Measures['shown'] -eq 7 -and [Math]::Abs($ans[0].Measures['kept'] - 7.25) -lt 1e-9) ('measured over the estimate of the turn: ' + $ans.Count + ' answer')

  # The strokes of a segment with three automatic marks: the count of the marks and the count kept; accepted as shown, the same
  # answer, right. A segment without automatic marks, not read now: no answer; read now: one.
  $rows = $l.computed.Rows
  $seg = $null; $seg2 = $null
  for ($i = 0; $i -lt $rows.Count; $i++) {
    $r = $rows[$i]
    if ($r.PrevD -le 0 -or $null -eq $l.race.GetSplit($r.PrevD) -or $null -eq $l.race.GetSplit($r.D)) { continue }
    if ($null -eq $seg) { $seg = $r } elseif ($null -eq $seg2) { $seg2 = $r }
  }
  if ($null -eq $seg -or $null -eq $seg2) { throw 'no two segments between marked checkpoints' }
  $strokeType = [Enum]::Parse((T 'SwimMarkType'), 'Stroke')
  function MarksIn($row) {
    $t0 = $l.race.GetSplit($row.PrevD).Timestamp; $t1 = $l.race.GetSplit($row.D).Timestamp
    $auto = 0; $hand = 0
    foreach ($m in $l.race.Marks) { if ($m.Type -eq $strokeType -and $m.Timestamp -gt $t0 -and $m.Timestamp -le $t1) { if ($null -ne $m.Batch) { $auto++ } else { $hand++ } } }
    return @{ t0 = $t0; t1 = $t1; auto = $auto; hand = $hand }
  }
  $in = MarksIn $seg
  for ($k = 1; $k -le 3; $k++) {
    $m = [Activator]::CreateInstance((T 'SwimMark')); $m.Type = $strokeType; $m.Timestamp = [long]($in.t0 + ($in.t1 - $in.t0) * $k / 4); $m.Batch = 'auto:ci'
    $l.race.Marks.Add($m)
  }
  $learnStroke = $mgrType.GetMethod('LearnStrokeCount', $all)
  [void]$learnStroke.Invoke($mgr, [object[]]@($seg.psobject.BaseObject, [double]14, [double]15, [double]1.25, $false))
  $sKey = $vc + '|' + $race12 + '|race.strokes|' + $seg.D
  $s = Answer $sKey
  Check ($s.Count -eq 1 -and -not $s[0].Right -and [Math]::Abs($s[0].Error - 1) -lt 1e-9 -and $s[0].Measures['auto'] -eq ($in.auto + 3) -and $s[0].Measures['hand'] -eq $in.hand -and
    [Math]::Abs($s[0].Measures['len'] - ($seg.D - $seg.PrevD)) -lt 1e-9 -and $s[0].Measures['style'] -eq 1 -and [Math]::Abs($s[0].Measures['cycle'] - 1.25) -lt 1e-9 -and
    $s[0].Measures['from'] -eq $seg.PrevD) ('the strokes ' + $seg.PrevD + '-' + $seg.D + ': ' + $s.Count + ' answer, auto ' + $(if ($s.Count -gt 0) { $s[0].Measures['auto'] } else { '-' }) + ' of ' + ($in.auto + 3))
  [void]$learnStroke.Invoke($mgr, [object[]]@($seg.psobject.BaseObject, [double]14, [double]14, [double]0, $false))
  $s = Answer $sKey
  Check ($s.Count -eq 1 -and $s[0].Right -and $s[0].Error -eq 0 -and -not $s[0].Measures.ContainsKey('cycle')) ('accepted as shown: the same answer, right (' + $s.Count + ')')
  $in2 = MarksIn $seg2
  $s2Key = $vc + '|' + $race12 + '|race.strokes|' + $seg2.D
  [void]$learnStroke.Invoke($mgr, [object[]]@($seg2.psobject.BaseObject, [double]12, [double]13, [double]0, $false))
  $notRead = (Answer $s2Key).Count
  [void]$learnStroke.Invoke($mgr, [object[]]@($seg2.psobject.BaseObject, [double]12, [double]13, [double]1.3, $true))
  $read = (Answer $s2Key).Count
  Check ((($in2.auto -eq 0 -and $notRead -eq 0) -or ($in2.auto -gt 0 -and $notRead -eq 1)) -and $read -eq 1) ('a segment without automatic marks: an answer only when read now (' + $notRead + ', ' + $read + ')')

  # In the file of the learning as they are (written a little later, with the next ones), and taken by another Kinovea.
  $dueBefore = [bool]$mgrType.GetField('valueSaveDue', $all).GetValue($mgr)
  [void]$flush.Invoke($mgr, @())
  $dueAfter = [bool]$mgrType.GetField('valueSaveDue', $all).GetValue($mgr)
  $loaded = $tLearning.GetMethod('Load').Invoke($null, [object[]]@($null))
  $back = @($loaded.Records | Where-Object { $_.Key -eq $sKey })
  $other = [Activator]::CreateInstance($tLearning)
  [void]$other.Merge($learning.Records)
  $taken = @($other.Records | Where-Object { $_.Kind -eq 'race.strokes' -or $_.Kind -eq 'race.underwater' }).Count
  Check ($dueBefore -and -not $dueAfter -and $back.Count -eq 1 -and $back[0].Measures['auto'] -eq ($in.auto + 3) -and $back[0].Right -and $taken -ge 4) ('in the file of the learning (written after: ' + $dueBefore + ' ' + $dueAfter + ') and taken by another Kinovea (' + $back.Count + ', ' + $taken + ')')
} catch {
  $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
  Check $false ('the answers about the values: ' + $x.GetType().Name + ': ' + $x.Message + ' @ ' + (($x.StackTrace -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' < '))
} finally {
  try { (T 'SwimAnalysisManager').GetField('videoPath', $all).SetValue((T 'SwimAnalysisManager').GetProperty('Instance').GetValue($null), $null) } catch { }
}

# ---- The English of the new texts.
$map = (T 'SwimLangEnglish').GetField('Map', $static).GetValue($null)
$keys = @('ΓΕΓΟΝΟΤΑ', 'ΑΓΩΝΑΣ', 'ΓΕΓΟΝΟΣ', 'ΤΙΜΗ', 'ΚΟΛΥΜΒΗΣΗ {0}–{1}{2}', 'ΑΛΛΑΓΗ ΣΚΥΤΑΛΗΣ {0}{1}', 'ΑΛΛΑΓΗ ΣΤΥΛ {0}{1}', 'ΣΤΡΟΦΗ {0}{1}',
  'Τελ. χεριά', 'Ξεκίνημα', 'Όλα στον πίνακα  ↗', '✓ επίσημοι χρόνοι', ' χερ.',
  'Κλικ: γράψε την απόσταση ή γύρνα τη ροδέλα (0.01 · Ctrl 0.10 · Shift 1.00), Enter για να μπει, Esc για ακύρωση.',
  'Γράψε την απόσταση (π.χ. 3.05 ή 3,05) ή γύρνα τη ροδέλα: 0.01 ανά βήμα · Ctrl 0.10 · Shift 1.00. ',
  'Γράψε την απόσταση στο κελί (π.χ. 3.05 ή 3,05· F2: αλλάζεις αυτή που υπάρχει) ή, όσο γράφεις, γύρνα τη ροδέλα πάνω στο κελί: ',
  '0.01 ανά βήμα · Ctrl 0.10 · Shift 1.00. Enter: μπαίνει · Esc: μένει όπως ήταν. Κενό: χωρίς τιμή.')
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

    # (2.39.1) The left bar at the start of a fictional 100 m: the 📍 beside the entry (2.41.0: of the head; measured) and beside the breakout
    # (≈ the estimate from the times), or without distances (–); a picture of each. (The 📍 of a turn needs the video at the turn.)
    $p = $picks['100|50|m|fs|False']
    if ($null -ne $p) {
      $markButtons = $tBar.GetMethod('MarkButtons', $all)
      $tButton = T 'SwimBarButton'
      $fId = $tButton.GetField('Id'); $fText = $tButton.GetField('Text'); $fValue = $tButton.GetField('Value'); $fSide = $tButton.GetField('Side')
      $leftCases = @(
        @{ name = 'bar_left_start_100free.png'; entry = 3.3; under = 9.6; est = $true; want = '3.30|≈9.6' },
        @{ name = 'bar_left_start_no_distances.png'; entry = $null; under = $null; est = $false; want = '–|–' }
      )
      foreach ($lc in $leftCases) {
        $l = Load $p.n $p.f
        $l.race.Athlete = 'Αθλητής Δείγμα'; $l.race.Competition = 'Αγώνας δείγμα'; $l.race.Location = 'Πόλη δείγμα'; $l.race.Round = 'Τελικός'; $l.race.Lane = 4
        # (2.41.0) The entry of the head.
        $l.race.EntryHead = $true; $l.race.EntryDistHead = $lc.entry; $l.race.BreakoutDist = $null; $l.race.UnderwaterDist = $lc.under; $l.race.UnderwaterEstimated = $lc.est
        $mgrType.GetProperty('Race').SetValue($mgr, $l.race)
        $mgrType.GetProperty('Computed').SetValue($mgr, $l.computed)
        $mgrType.GetProperty('Timing').SetValue($mgr, $l.timing)
        $leftBar = [Activator]::CreateInstance($tBar, [object[]]@($mgr, $true))
        try {
          $margs = [object[]]@($l.race, $false, [int]0, $null)
          $buttons = $markButtons.Invoke($leftBar, $margs)
          $sides = @{}
          foreach ($b in $buttons) { $side = $fSide.GetValue($b); if ($null -ne $side) { $sides[[string]$fId.GetValue($b)] = $side } }
          $got = $(if ($sides.ContainsKey('entry')) { [string]$fValue.GetValue($sides['entry']) } else { 'none' }) + '|' + $(if ($sides.ContainsKey('breakout')) { [string]$fValue.GetValue($sides['breakout']) } else { 'none' })
          $ids = $(if ($sides.Count -eq 2) { [string]$fId.GetValue($sides['entry']) + ',' + [string]$fId.GetValue($sides['breakout']) + ',' + [string]$fText.GetValue($sides['entry']) } else { '' })
          Check ($got -eq $lc.want -and $sides.Count -eq 2 -and $ids -eq 'measure-entry,measure-breakout,📍') ('the 📍 of the left bar at the start (entry|breakout): ' + $got + ' (' + $lc.want + '), ' + $sides.Count + ' pins ' + $ids)
          $leftBar.Size = New-Object System.Drawing.Size 248, 980
          $bmp = New-Object System.Drawing.Bitmap 248, 980
          $leftBar.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, 248, 980))
          $bmp.Save([System.IO.Path]::Combine($out, $lc.name), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
          Write-Output ('picture: ' + $lc.name)
        } finally { $leftBar.Dispose() }
      }

      # (2.39.2) The tab of the turn with the video before the first wall (here: no video): the 📍 of the first turn (50 m), with its
      # distance (8.40, measured), not none (2.39.1 had none: the analyst looked at the tab from the start); a picture of the tab.
      $l = Load $p.n $p.f
      $l.race.Athlete = 'Αθλητής Δείγμα'; $l.race.Competition = 'Αγώνας δείγμα'; $l.race.Location = 'Πόλη δείγμα'; $l.race.Round = 'Τελικός'; $l.race.Lane = 4
      $turn50 = $tRace.GetMethod('GetTurn').Invoke($l.race, [object[]]@([int]50, $true))
      $turn50.UnderwaterDist = 8.4; $turn50.UnderwaterEstimated = $false
      $mgrType.GetProperty('Race').SetValue($mgr, $l.race)
      $mgrType.GetProperty('Computed').SetValue($mgr, $l.computed)
      $mgrType.GetProperty('Timing').SetValue($mgr, $l.timing)
      $leftBar = [Activator]::CreateInstance($tBar, [object[]]@($mgr, $true))
      try {
        $margs = [object[]]@($l.race, $false, [int]2, $null)
        $buttons = $markButtons.Invoke($leftBar, $margs)
        $side = $null
        foreach ($b in $buttons) { if ([string]$fId.GetValue($b) -eq 'breakout') { $side = $fSide.GetValue($b) } }
        $got = $(if ($null -ne $side) { [string]$fId.GetValue($side) + ' ' + [string]$fValue.GetValue($side) } else { 'none' })
        Check ($got -eq 'measure-turn 8.40') ('the 📍 of the tab of the turn before the first wall: the first turn, ' + $got + ' (measure-turn 8.40)')
        # (2.40.0) The last stroke before the wall first in the tab of the turn, as the turn goes (the analyst's request).
        $order = @($buttons | ForEach-Object { [string]$fId.GetValue($_) }) -join ','
        Check ($order.StartsWith('last-stroke,pushoff,kick,breakout,stroke')) ('the tab of the turn: ' + $order)
        # The tab of the turn shown (as after a click on it), its picture.
        $tBar.GetField('manualFrom', $all).SetValue($leftBar, 0)
        $tBar.GetField('manualTab', $all).SetValue($leftBar, 2)
        $leftBar.Size = New-Object System.Drawing.Size 248, 980
        $bmp = New-Object System.Drawing.Bitmap 248, 980
        $leftBar.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle 0, 0, 248, 980))
        $bmp.Save([System.IO.Path]::Combine($out, 'bar_left_turn_before_the_wall.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        Write-Output 'picture: bar_left_turn_before_the_wall.png'
      } finally { $leftBar.Dispose() }
    }
  } catch {
    $x = $_.Exception; while ($null -ne $x.InnerException) { $x = $x.InnerException }
    Check $false ('pictures of the bar: ' + $x.GetType().Name + ': ' + $x.Message + ' @ ' + (($x.StackTrace -split "`n" | Select-Object -First 3 | ForEach-Object { $_.Trim() }) -join ' < '))
  }
}

try { Remove-Item -Recurse -Force $settingsDir } catch { }
Write-Output ('Bar events: ' + $fails + ' checks failed')
if ($fails -gt 0) { exit 1 }
