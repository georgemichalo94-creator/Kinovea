# sr_ci.ps1: the CI check of the sound read with ffmpeg (SwimStartSound.Read, 2.15.0) with the ffmpeg.exe of the build: a zone after
# the first seconds of an MP4 and of an MPEG-TS whose clock starts at 100 s comes back whole, with its first sample at 'from' (a click
# at 40 s at its place). Fictional sounds made here by ffmpeg (a 5 ms click of 1 kHz in 60 s of silence).
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File sr_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls and ffmpeg.exe>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
$a = [System.Reflection.Assembly]::LoadFrom($sm)
$tSound = $a.GetType('Kinovea.ScreenManager.SwimStartSound', $true)
$read = $tSound.GetMethod('Read')
$rate = [int]$tSound.GetField('Rate').GetValue($null)
$ff = Join-Path $bin 'ffmpeg.exe'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('swim_sr_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
$click = "aevalsrc=exprs='if(between(t,40,40.005),0.9*sin(2*PI*1000*t),0)':s=48000:d=60"
function Make($name, $codecArgs) {
  $path = Join-Path $tmp $name
  $argList = @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', $click) + $codecArgs + @('-y', $path)
  & $ff @argList 2>&1 | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) }
  return $path
}
function StartOf($path) {
  $info = (& $ff -nostdin -i $path 2>&1 | Out-String)
  $m = [regex]::Match($info, 'start:\s*([0-9.]+)')
  if ($m.Success) { return [double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) }
  return 0.0
}
function ReadZone($path, $from, $seconds) {
  $err = $null
  $args2 = [object[]]@($ff, $path, [double]$from, [double]$seconds, [System.Threading.CancellationToken]::None, $err)
  $samples = $read.Invoke($null, $args2)
  return @{ Samples = $samples; Error = $args2[5] }
}
function PeakAt($samples) {
  $best = -1.0; $at = -1
  for ($i = 0; $i -lt $samples.Length; $i++) { $v = [Math]::Abs($samples[$i]); if ($v -gt $best) { $best = $v; $at = $i } }
  return $at
}

$mp4 = Make 'click.mp4' @('-c:a', 'aac', '-strict', '-2', '-b:a', '128k')
$ts = Make 'click.ts' @('-c:a', 'mp2', '-b:a', '128k', '-f', 'mpegts', '-output_ts_offset', '100')
Check (Test-Path $mp4) 'MP4 made'
Check (Test-Path $ts) 'MPEG-TS made'
foreach ($case in @(@{ Path = $mp4; Name = 'MP4' }, @{ Path = $ts; Name = 'TS (clock from 100 s)' })) {
  if (-not (Test-Path $case.Path)) { continue }
  $start = StartOf $case.Path
  foreach ($zone in @(@{ From = 35.0; Seconds = 10.0 }, @{ From = 0.0; Seconds = 50.0 })) {
    $from = $start + $zone.From
    $r = ReadZone $case.Path $from $zone.Seconds
    $n = if ($r.Samples) { $r.Samples.Length } else { 0 }
    $expected = [int][Math]::Floor($zone.Seconds * $rate)
    Check ($n -ge $expected - $rate / 50 -and $n -le $expected + 1) ($case.Name + ' from ' + $zone.From + ' s for ' + $zone.Seconds + ' s: ' + $n + ' samples of ' + $expected + ' (start ' + $start + ')' + $(if ($r.Error) { ' error: ' + $r.Error } else { '' }))
    if ($n -gt 0) {
      $at = PeakAt $r.Samples
      $want = (40.0 - $zone.From) * $rate
      Check ([Math]::Abs($at - $want) -le $rate * 0.003) ($case.Name + ': the click at sample ' + $at + ' (' + [int]$want + ' ± 3 ms)')
    }
  }
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'sound read: all checks passed'
