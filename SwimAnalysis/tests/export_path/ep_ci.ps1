# ep_ci.ps1: the CI check of Kinovea's video writer (Kinovea.Video.FFMpeg.VideoFileWriter: "Export video", the side by side export)
# with output files whose names are outside the code page of the system (Kinovea Swim 2.25.0): a Greek name in a Greek folder (the code
# page of the Windows of the CI is not Greek) and a name with a star (outside every ANSI code page), next to a plain name. Each video is
# written whole: the writer opens its file, the file is there, and ffmpeg reads its 12 images.
# Before 2.25.0 the name went to ffmpeg in ANSI: such files were never created ("File not opened, Error:Invalid argument" in the log).
# Fictional images drawn here. Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File ep_ci.ps1 -bin <folder of the dlls and ffmpeg.exe>
param([string]$bin)
$ErrorActionPreference = 'Stop'
$bin = (Resolve-Path $bin).Path
# The native ffmpeg libraries of Kinovea.Video.FFMpeg are found by the program next to it, not by this host.
$env:PATH = $bin + ';' + $env:PATH
Set-Location $bin
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
Add-Type -AssemblyName System.Drawing
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Kinovea.Video.dll'))
[void][System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Kinovea.Video.FFMpeg.dll'))
$ff = Join-Path $bin 'ffmpeg.exe'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
function Text($codes) { return -join ($codes | ForEach-Object { [char]$_ }) }

Write-Output ('code page of the system: ' + [System.Text.Encoding]::Default.CodePage)
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('swim_ep_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
# "Δοκιμή εξαγωγής" in a folder "Φάκελος", and "star ★ test": written with their codes (this file stays plain text).
$greekFolder = Join-Path $tmp (Text @(0x03A6, 0x03AC, 0x03BA, 0x03B5, 0x03BB, 0x03BF, 0x03C2))
New-Item -ItemType Directory -Path $greekFolder | Out-Null
$greekName = (Text @(0x0394, 0x03BF, 0x03BA, 0x03B9, 0x03BC, 0x03AE, 0x20, 0x03B5, 0x03BE, 0x03B1, 0x03B3, 0x03C9, 0x03B3, 0x03AE, 0x03C2)) + '.mp4'
$cases = @(
  @{ What = 'a plain name'; Path = (Join-Path $tmp 'plain export.mp4') },
  @{ What = 'a Greek name in a Greek folder'; Path = (Join-Path $greekFolder $greekName) },
  @{ What = 'a name with a star'; Path = (Join-Path $tmp ('star ' + (Text @(0x2605)) + ' test.mp4')) }
)

# ffmpeg writes its messages on stderr: not errors of this script.
function Native($argList) {
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { return (& $ff @argList 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $old }
}

foreach ($c in $cases) {
  $path = [string]$c.Path
  $writer = New-Object Kinovea.Video.FFMpeg.VideoFileWriter
  $info = New-Object Kinovea.Video.VideoInfo
  $info.ReferenceSize = New-Object System.Drawing.Size(320, 240)
  $opened = $writer.OpenSavingContext($path, $info, 'mp4', 40.0)
  Check ("$opened" -eq 'Success') ($c.What + ': the writer opens the file (' + $opened + ')')
  if ("$opened" -ne 'Success') { continue }
  $saved = 0
  for ($i = 0; $i -lt 12; $i++) {
    $bmp = New-Object System.Drawing.Bitmap(320, 240, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::FromArgb(20, 60, 110))
    $g.FillRectangle([System.Drawing.Brushes]::Orange, 20 + $i * 20, 100, 40, 40)
    $g.Dispose()
    $r = $writer.SaveFrame($bmp)
    if ("$r" -eq 'Success') { $saved++ }
    $bmp.Dispose()
  }
  [void]$writer.CloseSavingContext($true)
  Check ($saved -eq 12) ($c.What + ': 12 images given to the writer (' + $saved + ')')
  $exists = [System.IO.File]::Exists($path)
  $size = if ($exists) { (New-Object System.IO.FileInfo($path)).Length } else { 0 }
  Check ($exists -and $size -gt 1000) ($c.What + ': the file is there (' + $size + ' bytes)')
  if ($exists) {
    # ffmpeg reads it by a copy with a plain name (its command line may not take every letter).
    $copy = Join-Path $tmp ('read_' + [Guid]::NewGuid().ToString('N') + '.mp4')
    [System.IO.File]::Copy($path, $copy)
    $out = (Native @('-nostdin', '-i', $copy, '-map', '0:v', '-f', 'null', '-')) -join "`n"
    $m = [regex]::Matches($out, 'frame=\s*([0-9]+)')
    $frames = if ($m.Count -gt 0) { [int]$m[$m.Count - 1].Groups[1].Value } else { -1 }
    Check ($frames -eq 12) ($c.What + ': ffmpeg reads 12 images (' + $frames + ')')
  }
}

try { Remove-Item -Recurse -Force $tmp } catch { }
if ($fails -gt 0) { Write-Output ('FAIL ' + $fails + ' check(s) of the export with names outside the code page'); exit 1 }
Write-Output 'all checks of the export with names outside the code page passed'
