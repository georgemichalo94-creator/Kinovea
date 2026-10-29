# sr_ci.ps1: the CI check of the sound read with ffmpeg (SwimStartSound.Read, 2.15.0) with the ffmpeg.exe of the build: a zone after
# the first seconds of an MP4 and of an MPEG-TS whose clock starts at 100 s comes back whole, with its first sample at 'from' on the
# stream clock: a click at 40 s of the sound is at the same time of the stream clock in the zone read after a seek and in the sound read
# from the beginning (within 1 ms), near 40 s of the clock (an MP4 keeps the sound at 0, the TS at its start; the codecs add a few ms).
# Fictional sounds made here by ffmpeg (a 5 ms click of 1 kHz in 60 s of silence).
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
# ffmpeg writes its messages on stderr: not errors of this script.
function Native($argList) {
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { return (& $ff @argList 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $old }
}
function Make($name, $codecArgs) {
  $path = Join-Path $tmp $name
  $argList = @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', $click) + $codecArgs + @('-y', $path)
  Native $argList | ForEach-Object { Write-Host ('  ffmpeg: ' + $_) }
  return $path
}
function StartOf($path) {
  $info = (Native @('-nostdin', '-i', $path)) -join "`n"
  $m = [regex]::Match($info, 'start:\s*([0-9.]+)')
  if ($m.Success) { return [double]::Parse($m.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture) }
  return 0.0
}
function ReadZone($path, $from, $seconds) {
  $err = $null
  # Plain .NET strings: the paths from Join-Path come wrapped (PSObject), which Invoke does not convert.
  $args2 = [object[]]@([string]$ff, [string]$path, [double]$from, [double]$seconds, [System.Threading.CancellationToken]::None, $err)
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
  $clicks = @()
  foreach ($zone in @(@{ From = 35.0; Seconds = 10.0 }, @{ From = 0.0; Seconds = 50.0 })) {
    $from = $start + $zone.From
    $r = ReadZone $case.Path $from $zone.Seconds
    $n = if ($r.Samples) { $r.Samples.Length } else { 0 }
    $expected = [int][Math]::Floor($zone.Seconds * $rate)
    Check ($n -ge $expected - $rate / 50 -and $n -le $expected + 1) ($case.Name + ' from ' + $zone.From + ' s for ' + $zone.Seconds + ' s: ' + $n + ' samples of ' + $expected + ' (start ' + $start + ')' + $(if ($r.Error) { ' error: ' + $r.Error } else { '' }))
    if ($n -gt 0) { $clicks += ($from + (PeakAt $r.Samples) / $rate) }
  }
  if ($clicks.Count -eq 2) {
    $ms = 1000.0 * [Math]::Abs($clicks[0] - $clicks[1])
    Check ($ms -le 1.0) ($case.Name + ': the click at ' + $clicks[0].ToString('0.0000') + ' s of the clock after the seek, ' + $clicks[1].ToString('0.0000') + ' s from the beginning (' + $ms.ToString('0.00') + ' ms)')
    $off = [Math]::Min([Math]::Abs($clicks[0] - 40.0), [Math]::Abs($clicks[0] - 40.0 - $start))
    Check ($off -le 0.02) ($case.Name + ': the click near 40 s of the sound (' + (1000.0 * $off).ToString('0.0') + ' ms)')
  }
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
if ($fails -gt 0) { Write-Output ("$fails check(s) failed"); exit 1 }
Write-Output 'sound read: all checks passed'
