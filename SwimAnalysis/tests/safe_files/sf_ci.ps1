# sf_ci.ps1: the CI check of the files of the swim analysis written whole or not at all (2.22.0): the text through a temporary file,
# the file of before kept as .bak and read when the file cannot be read (not when it was deleted), the copy a day of an analysis (once
# a day, the newest kept, the oldest gone), the copy of a file that cannot be read, a write that cannot be done (the file of before
# stays) and the analyses of a save that stopped in the middle (its whole temporary file). Fictional files in a temporary folder.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File sf_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
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
$tFiles = $a.GetType('Kinovea.ScreenManager.SwimFiles', $true)
$tObject = $json.GetType('Newtonsoft.Json.Linq.JObject', $true)
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$utf8 = [System.Text.Encoding]::UTF8
$write = $tFiles.GetMethod('WriteText')
$read = $tFiles.GetMethod('ReadJson').MakeGenericMethod($tObject)
$backup = $tFiles.GetMethod('BackupDaily'); $backups = $tFiles.GetMethod('Backups'); $unreadable = $tFiles.GetMethod('KeepUnreadable')

# (Join-Path gives a string wrapped by PowerShell, which reflection does not take for a System.String: the paths with Path.Combine.)
$dir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'swim_safe_files_' + [Guid]::NewGuid().ToString('N'))
[void][System.IO.Directory]::CreateDirectory($dir)
try {
  # Whole or not at all, with the file of before as .bak when asked.
  $f = [System.IO.Path]::Combine($dir, 'fictional video.mp4.swim.json')
  [void]$write.Invoke($null, @($f, '{"a":1,"name":"δοκιμή"}', $utf8, $true))
  Check ([System.IO.File]::ReadAllText($f, $utf8) -eq '{"a":1,"name":"δοκιμή"}') 'a new file is written (Greek text kept)'
  Check (-not (Test-Path ($f + '.tmp')) -and -not (Test-Path ($f + '.bak'))) 'no temporary file left, no .bak of a new file'
  [void]$write.Invoke($null, @($f, '{"a":2}', $utf8, $true))
  Check ([System.IO.File]::ReadAllText($f, $utf8) -eq '{"a":2}' -and [System.IO.File]::ReadAllText($f + '.bak', $utf8) -eq '{"a":1,"name":"δοκιμή"}') 'the new text in the file, the one of before in .bak'
  Check (-not (Test-Path ($f + '.tmp'))) 'no temporary file left after a replace'
  [void]$write.Invoke($null, @($f, '{"a":3}', $utf8, $false))
  Check ([System.IO.File]::ReadAllText($f, $utf8) -eq '{"a":3}' -and [System.IO.File]::ReadAllText($f + '.bak', $utf8) -eq '{"a":1,"name":"δοκιμή"}') 'without keepBackup the .bak is left as it was'

  # Read: the file; when it cannot be read, the one of before; when it was deleted, nothing.
  $o = $read.Invoke($null, @($f, $null))
  Check ($null -ne $o -and $o.GetValue('a').ToString() -eq '3') 'a file that reads'
  [System.IO.File]::WriteAllText($f, '{"a":', $utf8)
  $o = $read.Invoke($null, @($f, $null))
  Check ($null -ne $o -and $o.GetValue('a').ToString() -eq '1') 'half a file: the one of before (.bak) is read'
  [System.IO.File]::WriteAllText($f, '', $utf8)
  $o = $read.Invoke($null, @($f, $null))
  Check ($null -ne $o -and $o.GetValue('a').ToString() -eq '1') 'an empty file: the one of before (.bak) is read'
  [System.IO.File]::Delete($f)
  Check ($null -eq $read.Invoke($null, @($f, $null))) 'a deleted file is not brought back from its .bak'
  [System.IO.File]::WriteAllText($f, '{"a":', $utf8); [System.IO.File]::Delete($f + '.bak')
  Check ($null -eq $read.Invoke($null, @($f, $null))) 'half a file without a .bak: nothing (no exception)'

  # The copy a day of an analysis.
  $folder = [System.IO.Path]::Combine($dir, 'backup'); $prefix = 'fictional video.mp4.0a1b2c3d'; $ext = '.swim.json'
  [System.IO.File]::WriteAllText($f, '{"races":1}', $utf8)
  [void]$backup.Invoke($null, @($f, $folder, $prefix, $ext, 3, [long]1000000))
  $day = [DateTime]::Now.ToString('yyyyMMdd')
  $today = [System.IO.Path]::Combine($folder, $prefix + '.' + $day + $ext)
  Check ((Test-Path $today) -and [System.IO.File]::ReadAllText($today, $utf8) -eq '{"races":1}') 'the copy of the day, as the file was'
  [System.IO.File]::WriteAllText($f, '{"races":2}', $utf8)
  [void]$backup.Invoke($null, @($f, $folder, $prefix, $ext, 3, [long]1000000))
  Check ([System.IO.File]::ReadAllText($today, $utf8) -eq '{"races":1}') 'once a day: the copy keeps the state before the first save'
  # Older days of this video and of another one: the three newest of each stay.
  [System.IO.File]::Delete($today)
  foreach ($d in @('20260101', '20260102', '20260103', '20260104')) { [System.IO.File]::WriteAllText([System.IO.Path]::Combine($folder, $prefix + '.' + $d + $ext), '{"old":' + $d + '}', $utf8) }
  $other = 'another video.mp4.11223344'
  [System.IO.File]::WriteAllText([System.IO.Path]::Combine($folder, $other + '.20260101' + $ext), '{"other":1}', $utf8)
  [void]$backup.Invoke($null, @($f, $folder, $prefix, $ext, 3, [long]1000000))
  $mine = @($backups.Invoke($null, @($folder, $prefix, $ext)))
  Check ($mine.Count -eq 3 -and $mine[0].Name -eq [System.IO.Path]::GetFileName($today) -and $mine[2].Name -eq ($prefix + '.20260103' + $ext)) ('the three newest copies of the video, newest first: ' + (($mine | ForEach-Object { $_.Name }) -join ', '))
  Check ([System.IO.File]::Exists([System.IO.Path]::Combine($folder, $other + '.20260101' + $ext))) 'the copies of another video are not touched'
  # An empty file is not copied.
  $empty = [System.IO.Path]::Combine($dir, 'empty.mp4.swim.json'); [System.IO.File]::WriteAllText($empty, '')
  [void]$backup.Invoke($null, @($empty, $folder, 'empty.mp4.00000000', $ext, 3, [long]1000000))
  Check (@($backups.Invoke($null, @($folder, 'empty.mp4.00000000', $ext))).Count -eq 0) 'an empty file is not copied'
  # The folder over its size: the oldest go, today's stays.
  [void]$backup.Invoke($null, @($f, $folder, 'third.mp4.55667788', $ext, 3, [long]20))
  $left = @([System.IO.Directory]::GetFiles($folder, '*' + $ext))
  Check ([System.IO.File]::Exists([System.IO.Path]::Combine($folder, 'third.mp4.55667788.' + $day + $ext)) -and $left.Count -lt 6) ('over the size of the folder the oldest go, the new copy stays (' + $left.Count + ' left)')

  # A file that cannot be read is kept before anything is written over it.
  [System.IO.File]::WriteAllText($f, '{"half":', $utf8)
  $kept = $unreadable.Invoke($null, @($f, $folder, $prefix, $ext))
  Check ($null -ne $kept -and (Test-Path $kept) -and [System.IO.File]::ReadAllText($kept, $utf8) -eq '{"half":' -and [System.IO.Path]::GetFileName($kept) -like ($prefix + '.unreadable-*' + $ext)) 'the unreadable file is kept as it is'
  Check (@($backups.Invoke($null, @($folder, $prefix, $ext)) | Where-Object { $_.Name -like '*unreadable*' }).Count -eq 0) 'it is not one of the daily copies'
  # Half a file is no copy of a day (it would take the place of a good one).
  [void]$backup.Invoke($null, @($f, $folder, 'half.mp4.99aabbcc', $ext, 3, [long]1000000))
  Check (@($backups.Invoke($null, @($folder, 'half.mp4.99aabbcc', $ext))).Count -eq 0) 'half a file is not copied as the copy of the day'
  [System.IO.File]::WriteAllBytes($f, (New-Object byte[] 64))
  [void]$backup.Invoke($null, @($f, $folder, 'half.mp4.99aabbcc', $ext, 3, [long]1000000))
  Check (@($backups.Invoke($null, @($folder, 'half.mp4.99aabbcc', $ext))).Count -eq 0) 'nor a file of zeros'

  # A write that cannot be done (its temporary file cannot be made): it says so, and the file of before is as it was.
  $g = [System.IO.Path]::Combine($dir, 'blocked.mp4.swim.json')
  [System.IO.File]::WriteAllText($g, '{"before":1}', $utf8)
  $tempPath = $tFiles.GetMethod('TempPath').Invoke($null, @($g))
  Check ($tempPath -eq ($g + '.tmp')) 'the temporary file of a path'
  [void][System.IO.Directory]::CreateDirectory($tempPath)
  $threw = $false
  try { [void]$write.Invoke($null, @($g, '{"after":2}', $utf8, $false)) } catch { $threw = $true }
  Check ($threw -and [System.IO.File]::ReadAllText($g, $utf8) -eq '{"before":1}') 'a write that cannot be done throws, the file of before stays'
  [System.IO.Directory]::Delete($tempPath)
  [void]$write.Invoke($null, @($g, '{"after":2}', $utf8, $false))
  Check ([System.IO.File]::ReadAllText($g, $utf8) -eq '{"after":2}' -and -not (Test-Path $tempPath)) 'and the next write goes'

  # The analyses of a save that stopped in the middle: from its whole temporary file, never from half a one.
  $flags = [System.Reflection.BindingFlags]'NonPublic,Static'
  $tMan = $a.GetType('Kinovea.ScreenManager.SwimAnalysisManager', $true)
  $interrupted = $tMan.GetMethod('ReadInterruptedSave', $flags)
  $video = [System.IO.Path]::Combine($dir, 'fictional turn.mp4'); $h = $video + '.swim.json'
  [System.IO.File]::WriteAllText($h, '{"Races":[{"Id":"r1","Athl', $utf8)
  Check ($null -eq $interrupted.Invoke($null, @($h, $video))) 'half an analysis file without a temporary file: nothing'
  [System.IO.File]::WriteAllText($h + '.tmp', '{"Races":[{"Id":"r1","Athlete":"FICTIONAL ATHLETE"},{"Id":"r2"}]}', $utf8)
  $file = $interrupted.Invoke($null, @($h, $video))
  Check ($null -ne $file -and $file.Races.Count -eq 2 -and $file.Races[0].Id -eq 'r1' -and $file.VideoPath -eq $video) 'the whole temporary file of the save that stopped: its analyses'
  [System.IO.File]::WriteAllText($h + '.tmp', '{"Races":[{"Id":"r1"},{"Id', $utf8)
  Check ($null -eq $interrupted.Invoke($null, @($h, $video))) 'half a temporary file: nothing (no exception)'
  [System.IO.File]::WriteAllText($h + '.tmp', 'null', $utf8)
  Check ($null -eq $interrupted.Invoke($null, @($h, $video))) 'an empty temporary file: nothing'
}
finally {
  try { [System.IO.Directory]::Delete($dir, $true) } catch { }
}

if ($fails -gt 0) { Write-Output ('FAILED: ' + $fails); exit 1 }
Write-Output 'safe files ok'
