# sr_probe.ps1: what the ffmpeg of the build (n2.7.4) gives for the sound read of SwimStartSound with a few seek variants,
# on fictional sounds made here (a 5 ms click of 1 kHz at 40 s in 60 s of silence): count, peak, first sample above 0, and the
# time stamps of the first decoded frames. Notice annotations, no checks.
param([string]$ff)
$ErrorActionPreference = 'Stop'
$ff = (Resolve-Path $ff).Path
$rate = 16000
$ci = [Globalization.CultureInfo]::InvariantCulture
$tmp = Join-Path ([IO.Path]::GetTempPath()) ('swim_probe_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
function Native($argList) { $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; try { return (& $ff @argList 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $old } }
$click = "aevalsrc=exprs='if(between(t,40,40.005),0.9*sin(2*PI*1000*t),0)':s=48000:d=60"
$mp4 = Join-Path $tmp 'click.mp4'
$ts = Join-Path $tmp 'click.ts'
Native @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', $click, '-c:a', 'aac', '-strict', '-2', '-b:a', '128k', '-y', $mp4) | ForEach-Object { Write-Output ('ffmpeg: ' + $_) }
Native @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', $click, '-c:a', 'mp2', '-b:a', '128k', '-f', 'mpegts', '-output_ts_offset', '100', '-y', $ts) | ForEach-Object { Write-Output ('ffmpeg: ' + $_) }
function Run($argString) {
  $psi = New-Object System.Diagnostics.ProcessStartInfo($ff, $argString)
  $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
  $p = [System.Diagnostics.Process]::Start($psi)
  $errTask = $p.StandardError.ReadToEndAsync()
  $ms = New-Object System.IO.MemoryStream
  $p.StandardOutput.BaseStream.CopyTo($ms)
  $p.WaitForExit()
  return @{ Bytes = $ms.ToArray(); Err = $errTask.Result }
}
foreach ($file in @(@{ Path = $mp4; Name = 'MP4' }, @{ Path = $ts; Name = 'TS' })) {
  $info = ((Native @('-nostdin', '-i', $file.Path)) | Where-Object { $_ -match 'Duration|Stream' }) -join ' / '
  Write-Output ('::notice title=probe ' + $file.Name + ' info::' + $info)
  $m = [regex]::Match($info, 'start:\s*(-?[0-9.]+)')
  $start = if ($m.Success) { [double]::Parse($m.Groups[1].Value, $ci) } else { 0.0 }
  foreach ($pre in @('', '-seek_timestamp 1 -noaccurate_seek -ss {0:0.000} ', '-noaccurate_seek -ss {0:0.000} ', '-ss {0:0.000} ')) {
    $frames = Run ([string]::Format($ci, '-nostdin -v info -copyts ' + $pre + '-i "{1}" -map 0:a:0 -vn -af ashowinfo -frames:a 3 -f null -', $start + 32, $file.Path))
    $pts = (($frames.Err -split "`n") | Where-Object { $_ -match 'pts_time' } | ForEach-Object { if ($_ -match 'n:(\d+).*pts_time:(-?[0-9.]+).*nb_samples:(\d+)') { $Matches[1] + '@' + $Matches[2] + '(' + $Matches[3] + ')' } }) -join ' '
    Write-Output ('::notice title=probe ' + $file.Name + ' frames [' + $pre.Trim() + ']::' + $pts)
    foreach ($zone in @(@{ From = 35.0; Seconds = 10.0 }, @{ From = 0.0; Seconds = 50.0 })) {
      if ($zone.From -eq 0 -and $pre -ne '') { continue }
      $from = $start + $zone.From
      $fp = [long][Math]::Round($from * $rate)
      $a = [string]::Format($ci, '-nostdin -v error -copyts ' + $pre + '-i "{1}" -map 0:a:0 -vn -af "aresample={5},aresample=async=1:min_hard_comp=0.001:first_pts={2},atrim=end={3:0.000000}" -ac 1 -ar {5} -f f32le -', $from - 3, $file.Path, $fp, $from + $zone.Seconds, 0, $rate)
      $r = Run $a
      $n = [int]($r.Bytes.Length / 4)
      $s = New-Object 'float[]' $n
      [Buffer]::BlockCopy($r.Bytes, 0, $s, 0, $n * 4)
      $best = -1.0; $at = -1; $first = -1
      for ($i = 0; $i -lt $n; $i++) { $v = [Math]::Abs($s[$i]); if ($v -gt $best) { $best = $v; $at = $i }; if ($first -lt 0 -and $v -gt 1e-6) { $first = $i } }
      $line = [string]::Format($ci, '{0} from {1} for {2} [{3}]: {4} samples of {5}, peak {6:0.000} at {7:0.00000} s (clock) = {8:0.00000} s of the sound, first above 0 at {9:0.00000} s; err {10}',
        $file.Name, $zone.From, $zone.Seconds, $pre.Trim(), $n, [Math]::Floor($zone.Seconds * $rate), $best, $from + $at / $rate, $from + $at / $rate - $start, $from + $first / $rate, ($r.Err -replace "`r?`n", ' ').Trim())
      Write-Output ('::notice title=probe read::' + $line)
    }
  }
}
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
