# gt_ci.ps1: the CI check of the recordings of the GoPro cameras and their videos (2.21.0) on FICTIONAL cards, athletes and clubs:
# the recordings kept with the details of their moment, the videos of a card found by the serial number written in them and by
# their time (also with the clock of a camera in universal time), their folders and names (technique, race with the swimmer of the
# lane, no athlete, the parts of a long recording, a name already taken), the copy (whole, the card untouched, the CSV of the day),
# a copy that stopped and goes on, the list of a card by Wi-Fi, the names in Greek also with the English interface. The two windows
# are drawn to -out (an artifact of the run). Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File gt_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls> -out <folder>
param([string]$sm, [string]$bin, [string]$out)
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
function T($n) { return $a.GetType('Kinovea.ScreenManager.' + $n, $true) }
$all = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$none = [System.Threading.CancellationToken]::None

# A folder of its own: the settings (the list of the recordings), the cards, the folder of the videos.
$work = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-gt-ci-' + [Guid]::NewGuid().ToString('N'))
$settingsDir = [System.IO.Path]::Combine($work, 'settings') + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
$software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)
$root = [System.IO.Path]::Combine($work, 'videos')

# ---- Fictional GoPro files: an MP4 with the start of the recording (mvhd, seconds since 1904) and the serial number and model of the
# camera in the GPMF of its udta, as a GoPro writes them; the picture is a pattern of bytes (to compare the copies).
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
public static class FakeGoPro
{
    static byte[] U32(uint v) { return new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v }; }
    static byte[] Join(params byte[][] parts) { MemoryStream m = new MemoryStream(); foreach (byte[] p in parts) m.Write(p, 0, p.Length); return m.ToArray(); }
    static byte[] Box(string type, params byte[][] content) { byte[] c = Join(content); return Join(U32((uint)(8 + c.Length)), Encoding.ASCII.GetBytes(type), c); }
    static byte[] Klv(string key, byte type, int structSize, byte[] data)
    {
        int repeat = structSize == 0 ? 0 : data.Length / structSize;
        int padded = (data.Length + 3) & ~3;
        byte[] d = new byte[padded];
        Array.Copy(data, d, data.Length);
        return Join(Encoding.ASCII.GetBytes(key), new byte[] { type, (byte)structSize, (byte)(repeat >> 8), (byte)repeat }, d);
    }
    public static void Write(string path, DateTime created, string serial, string model, int payload, int seed)
    {
        Write(path, created, serial, model, payload, seed, int.MinValue);
    }
    // zone: the time zone in minutes written in TZON (as a HERO12: then `created` is universal time); int.MinValue: none.
    public static void Write(string path, DateTime created, string serial, string model, int payload, int seed, int zone)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        uint seconds = (uint)(created - new DateTime(1904, 1, 1)).TotalSeconds;
        byte[] mvhd = Join(new byte[4], U32(seconds), U32(seconds), U32(1000), U32(60000), U32(0x00010000), new byte[] { 1, 0 }, new byte[10],
            U32(0x00010000), U32(0), U32(0), U32(0), U32(0x00010000), U32(0), U32(0), U32(0), U32(0x40000000), new byte[24], U32(2));
        List<byte[]> keys = new List<byte[]>();
        keys.Add(Klv("FMWR", (byte)'c', 1, Encoding.ASCII.GetBytes("H00.00.00.00.00")));
        if (serial != null) keys.Add(Klv("CASN", (byte)'c', 1, Encoding.ASCII.GetBytes(serial)));
        if (model != null) keys.Add(Klv("MINF", (byte)'c', 1, Encoding.ASCII.GetBytes(model)));
        if (zone != int.MinValue) keys.Add(Klv("TZON", (byte)'s', 2, new byte[] { (byte)((short)zone >> 8), (byte)(short)zone }));
        byte[] inner = Join(keys.ToArray());
        byte[] devc = Join(Encoding.ASCII.GetBytes("DEVC"), new byte[] { 0, 1, (byte)(inner.Length >> 8), (byte)inner.Length }, inner);
        byte[] picture = new byte[payload];
        for (int i = 0; i < payload; i++) picture[i] = (byte)((i * 7 + seed) & 0xFF);
        byte[] file = Join(Box("ftyp", Encoding.ASCII.GetBytes("mp41"), U32(0), Encoding.ASCII.GetBytes("mp41")), Box("mdat", picture),
            Box("moov", Box("mvhd", mvhd), Box("udta", Box("GPMF", devc))));
        File.WriteAllBytes(path, file);
        File.SetLastWriteTime(path, created.AddMinutes(9));
    }
}
'@

$t0 = New-Object DateTime 2026, 11, 8, 10, 0, 0
$s1 = 'C3441300000001'; $s2 = 'C3501300000002'
$card1 = [System.IO.Path]::Combine($work, 'card1'); $card2 = [System.IO.Path]::Combine($work, 'card2')
$dcim1 = [System.IO.Path]::Combine($card1, 'DCIM\100GOPRO'); $dcim2 = [System.IO.Path]::Combine($card2, 'DCIM\100GOPRO')
[FakeGoPro]::Write("$dcim1\GX010001.MP4", $t0.AddSeconds(1.0), $s1, 'HERO9 Black', 40000, 1)
[FakeGoPro]::Write("$dcim1\GX010002.MP4", $t0.AddMinutes(5).AddSeconds(1.3), $s1, 'HERO9 Black', 30000, 2)
[FakeGoPro]::Write("$dcim1\GX010003.MP4", $t0.AddMinutes(10).AddSeconds(0.9), $s1, 'HERO9 Black', 50000, 3)
[FakeGoPro]::Write("$dcim1\GX020003.MP4", $t0.AddMinutes(18).AddSeconds(49), $s1, 'HERO9 Black', 20000, 4)
[FakeGoPro]::Write("$dcim1\GX010004.MP4", $t0.AddMinutes(20).AddSeconds(1.1), $s1, 'HERO9 Black', 10000, 5)
[FakeGoPro]::Write("$dcim1\GX010000.MP4", $t0.AddDays(-1), $s1, 'HERO9 Black', 10000, 6)
[FakeGoPro]::Write("$card1\DCIM\100GOPRO\IMG_TEST.MP4", $t0.AddSeconds(1), $s1, 'HERO9 Black', 1000, 7)
# The second camera writes its clock in universal time (two hours behind the computer).
[FakeGoPro]::Write("$dcim2\GX010011.MP4", $t0.AddHours(-2).AddSeconds(1.6), $s2, 'HERO12 Black', 40000, 8)
[FakeGoPro]::Write("$dcim2\GX010012.MP4", $t0.AddHours(-2).AddMinutes(5).AddSeconds(1.4), $s2, 'HERO12 Black', 30000, 9)

$filesT = T 'SwimGoProFiles'
$takesT = T 'SwimGoProTakes'
$detT = T 'SwimTakeDetails'
$swimmerT = T 'SwimTakeSwimmer'

# ---- The names of the files of a card.
$n = 0; $p = 0
Check ($filesT::ParseName('GX020123.MP4', [ref]$n, [ref]$p) -and $n -eq 123 -and $p -eq 2) 'GX020123.MP4: file 123, part 2'
Check ($filesT::ParseName('gh010007.mp4', [ref]$n, [ref]$p) -and $n -eq 7 -and $p -eq 1) 'gh010007.mp4: file 7, part 1'
Check ($filesT::ParseName('GOPR0042.MP4', [ref]$n, [ref]$p) -and $n -eq 42 -and $p -eq 1) 'GOPR0042.MP4: file 42 (older cameras)'
Check (-not $filesT::ParseName('IMG_TEST.MP4', [ref]$n, [ref]$p)) 'IMG_TEST.MP4 is not a GoPro file'
Check (-not $filesT::ParseName('GL010001.LRV', [ref]$n, [ref]$p)) 'the low resolution copy (LRV) is left'

# ---- What the file says.
$f1 = $filesT::Read("$dcim1\GX010001.MP4")
Check ($f1.Serial -eq $s1) ('the serial number of the camera from the file: ' + $f1.Serial)
Check ($f1.Model -eq 'HERO9 Black') ('the model from the file: ' + $f1.Model)
Check ([Math]::Abs(($f1.Created - $t0.AddSeconds(1)).TotalSeconds) -lt 1.01) ('the start of the recording from the file: ' + $f1.Created.ToString('HH:mm:ss'))
$scan1 = $filesT::Scan($card1, $none)
Check ($scan1.Count -eq 6) ('the card of K1: 6 GoPro videos (not the other file): ' + $scan1.Count)

# ---- The recordings, kept with the details of their moment (fictional athletes and clubs).
function Details([string]$kind, [string]$id, [string]$athlete, [string]$club) {
  $d = [Activator]::CreateInstance($detT)
  $d.Kind = $kind; $d.AthleteId = $id; $d.Athlete = $athlete; $d.Club = $club
  $d.Competition = 'Αγώνες Δοκιμής'; $d.Location = 'Πόλη Δοκιμής'; $d.Date = '8.11.2026'
  return $d
}
$takesT::SetNext((Details 'turn' '900001' 'Παράδειγμα Αθλητής' 'Ν.Ο. Δοκιμής'))
$takesT::Started('K1', $s1, 'HERO9 Black', $t0)
$takesT::Started('K2', $s2, 'HERO12 Black', $t0)
$takesT::Stopped('K1', $t0.AddSeconds(40)); $takesT::Stopped('K2', $t0.AddSeconds(40))
$takesT::SetNext((Details 'start' '900002' 'ΔΕΥΤΕΡΟ ΠΑΡΑΔΕΙΓΜΑ' 'Α.Ο. Δοκιμής'))
$takesT::Started('K1', $s1, 'HERO9 Black', $t0.AddMinutes(5)); $takesT::Started('K2', $s2, 'HERO12 Black', $t0.AddMinutes(5))
$race = Details 'race' '' '' ''
$race.Distance = 100; $race.Style = 'fly'; $race.Round = 'Τελικός'; $race.Heat = 2; $race.Lane = 4
foreach ($sw in @(@(3, 'ΠΡΩΤΟ ΠΑΡΑΔΕΙΓΜΑ', 'Κ.Ο. Δοκιμής'), @(4, 'ΤΡΙΤΟ ΠΑΡΑΔΕΙΓΜΑ', 'Ο.Φ. Δοκιμής'))) {
  $x = [Activator]::CreateInstance($swimmerT); $x.Lane = $sw[0]; $x.Name = $sw[1]; $x.Club = $sw[2]; $race.Swimmers.Add($x)
}
$takesT::SetNext($race)
Check ($takesT::Next.Summary -eq '100 Πεταλούδα · Τελικός · ΤΡΙΤΟ ΠΑΡΑΔΕΙΓΜΑ') ('the button of a race says: ' + $takesT::Next.Summary)
$takesT::Started('K1', $s1, 'HERO9 Black', $t0.AddMinutes(10))
$takesT::SetNext((Details 'turn' '' '' ''))
$takesT::Started('K1', $s1, 'HERO9 Black', $t0.AddMinutes(20))
$takes = $takesT::Takes
Check ($takes.Count -eq 4) ('four recordings kept: ' + $takes.Count)
Check ($takes[0].Cameras.Count -eq 2 -and $takes[1].Cameras.Count -eq 2 -and $takes[2].Cameras.Count -eq 1) 'the cameras started together are one recording'
Check ($takes[0].Details.Athlete -eq 'Παράδειγμα Αθλητής' -and $takes[1].Details.Kind -eq 'start' -and $takes[2].Details.Kind -eq 'race') 'each recording keeps the details of its moment'
Check ($takes[0].Stop -eq $t0.AddSeconds(40)) 'the end of a recording is kept'
Check ([System.IO.File]::Exists([System.IO.Path]::Combine($settingsDir, 'SwimGoProTakes.json'))) 'the list is kept in the settings folder'

# ---- The card of K1: its camera by the serial number, the recordings by their time.
Check ($filesT::CameraOf($takes, $scan1) -eq 'K1') 'the card is of K1 (serial number in its files)'
$scan2 = $filesT::Scan($card2, $none)
Check ($filesT::CameraOf($takes, $scan2) -eq 'K2') 'the card is of K2'
$found1 = $filesT::Match($takes, 'K1', $scan1)
Check ($found1.Count -eq 4) ('the 4 recordings of K1 found on its card: ' + $found1.Count)
$race1 = @($found1 | Where-Object { $_.Take.Details.Kind -eq 'race' })[0]
Check ($race1 -and $race1.Files.Count -eq 2 -and $race1.Files[1].Name -eq 'GX020003.MP4') 'the race has its two parts'
Check (@($found1 | Where-Object { $_.Files[0].Name -eq 'GX010000.MP4' }).Count -eq 0) 'a video of another day is left'
$filesT::PlanTargets($root, $found1)
$day = [System.IO.Path]::Combine($root, '8.11.2026ΠόληΔοκιμήςvideo')
$want = @(
  [System.IO.Path]::Combine($day, 'Ν.Ο. ΔΟΚΙΜΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ_Στροφή_20261108_K1.MP4'),
  [System.IO.Path]::Combine($day, 'Α.Ο. ΔΟΚΙΜΗΣ\900002_ΔΕΥΤΕΡΟ_ΠΑΡΑΔΕΙΓΜΑ\900002_ΔΕΥΤΕΡΟ_ΠΑΡΑΔΕΙΓΜΑ_Εκκίνηση_20261108_K1.MP4'),
  [System.IO.Path]::Combine($day, '100Πεταλούδα\20261108_Αγώνες_Δοκιμής_100Πεταλούδα_Τελικός_Σειρά2_ΤΡΙΤΟ_ΠΑΡΑΔΕΙΓΜΑ_K1.MP4'),
  [System.IO.Path]::Combine($day, '100Πεταλούδα\20261108_Αγώνες_Δοκιμής_100Πεταλούδα_Τελικός_Σειρά2_ΤΡΙΤΟ_ΠΑΡΑΔΕΙΓΜΑ_K1_μέρος2.MP4'),
  [System.IO.Path]::Combine($day, 'ΧΩΡΙΣ ΑΘΛΗΤΗ\Στροφή_20261108_102000_K1.MP4'))
$got = @(); foreach ($m in $found1) { foreach ($x in $m.Targets) { $got += $x } }
for ($i = 0; $i -lt $want.Count; $i++) { Check ($got -contains $want[$i]) ('the path ' + $want[$i].Substring($root.Length)) }
foreach ($x in $got) { if ($want -notcontains $x) { Write-Output ('     (planned: ' + $x.Substring($root.Length) + ')') } }

# ---- The copy: whole, the card untouched, done in the list, the CSV of the day.
$err = $null
$copied = $filesT::Copy($found1, [Action[string, long, long]]{ param($name, $done, $total) }, $none, [ref]$err)
Check ($copied -eq 5 -and $err -eq $null) ('5 files copied: ' + $copied + ' ' + $err)
$same = $true
foreach ($m in $found1) { for ($i = 0; $i -lt $m.Files.Count; $i++) {
  $x = [System.IO.File]::ReadAllBytes($m.Files[$i].Path); $y = [System.IO.File]::ReadAllBytes($m.Targets[$i])
  if ($x.Length -ne $y.Length -or [Convert]::ToBase64String($x) -ne [Convert]::ToBase64String($y)) { $same = $false } } }
Check $same 'every copy is the same as its file on the card'
Check ([System.IO.File]::Exists("$dcim1\GX010001.MP4")) 'the files of the card stay'
Check (@(Get-ChildItem -Path $root -Recurse -Filter '*.part').Count -eq 0) 'no temporary file is left'
$csv = @(Get-ChildItem -Path $day -Filter '_Kinovea_βίντεο_ιστορικό_*.csv')
Check ($csv.Count -eq 1) 'the CSV of the day'
if ($csv.Count -eq 1) {
  $lines = [System.IO.File]::ReadAllLines($csv[0].FullName, [System.Text.Encoding]::UTF8)
  Check ($lines[0] -eq 'Ενέργεια;Από;Προς' -and $lines.Count -eq 6 -and $lines[1].StartsWith('Αντιγραφή;')) ('its lines: ' + $lines.Count)
}
$takes = $takesT::Takes
Check (@($takes | ForEach-Object { $_.Cameras } | Where-Object { $_.Alias -eq 'K1' -and $_.Done }).Count -eq 4) 'the 4 recordings of K1 are done'
Check (-not $takes[0].AllDone) 'the first recording waits for K2'
Check ($filesT::Match($takes, 'K1', $scan1).Count -eq 0) 'the same card again: nothing to copy'

# ---- A camera that writes another serial number in its files than by Bluetooth: learned from a download, its card is found by it.
$card3 = [System.IO.Path]::Combine($work, 'card3')
[FakeGoPro]::Write("$card3\DCIM\100GOPRO\GX010031.MP4", $t0.AddMinutes(5).AddSeconds(1), 'C3501399999999', 'HERO12 Black', 1000, 12)
$scan3 = $filesT::Scan($card3, $none)
Check ($filesT::CameraOf($takes, $scan3) -eq 'K2') 'an unknown serial number, the only HERO12 Black of the recordings: K2 by its model'
$takesT::Started('K3', 'C3501300000003', 'HERO12 Black', $t0.AddMinutes(60))
$takes = $takesT::Takes
Check ($filesT::CameraOf($takes, $scan3) -eq $null) 'two cameras of that model: the files do not say which (the window asks)'
$takesT::LearnFileSerial('C3501300000003', 'C3501399999999')
Check ($filesT::CameraOf($takes, $scan3) -eq 'K3') 'the serial number learned from a download: K3'
$takesT::Remove([string[]]@($takes[$takes.Count - 1].Id))
$takes = $takesT::Takes

# ---- The card of K2: its clock two hours behind.
$found2 = $filesT::Match($takes, 'K2', $scan2)
Check ($found2.Count -eq 2) ('the 2 recordings of K2 found with its clock in universal time: ' + $found2.Count)
$filesT::PlanTargets($root, $found2)
Check ($found2[0].Targets[0] -eq [System.IO.Path]::Combine($day, 'Ν.Ο. ΔΟΚΙΜΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ_Στροφή_20261108_K2.MP4')) 'K2 beside K1 in the folder of the athlete'
$copied = $filesT::Copy($found2, [Action[string, long, long]]{ param($name, $done, $total) }, $none, [ref]$err)
$takes = $takesT::Takes
Check ($copied -eq 2 -and $takes[0].AllDone -and $takes[1].AllDone) 'both recordings of the two cameras are done'

# ---- A copy that stopped goes on: the part already whole stays at its name, only the rest comes; a different file keeps its name.
$takesT::SetNext((Details 'turn' '900001' 'Παράδειγμα Αθλητής' 'Ν.Ο. Δοκιμής'))
$takesT::Started('K1', $s1, 'HERO9 Black', $t0.AddMinutes(30))
[FakeGoPro]::Write("$dcim1\GX010005.MP4", $t0.AddMinutes(30).AddSeconds(1), $s1, 'HERO9 Black', 30000, 10)
[FakeGoPro]::Write("$dcim1\GX020005.MP4", $t0.AddMinutes(38).AddSeconds(49), $s1, 'HERO9 Black', 20000, 11)
$takes = $takesT::Takes; $scan1 = $filesT::Scan($card1, $none)
$found5 = $filesT::Match($takes, 'K1', $scan1)
$filesT::PlanTargets($root, $found5)
$first = $found5[0].Targets[0]
Check ($first -eq [System.IO.Path]::Combine($day, 'Ν.Ο. ΔΟΚΙΜΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ\900001_ΠΑΡΑΔΕΙΓΜΑ_ΑΘΛΗΤΗΣ_Στροφή_20261108_K1_2.MP4')) 'a second turn of the same day: «_2»'
[System.IO.File]::Copy($found5[0].Files[0].Path, $first)
$filesT::PlanTargets($root, $found5)
Check ($found5[0].Targets[0] -eq $first -and $found5[0].Present[0] -and -not $found5[0].Present[1]) 'the part already whole keeps its name and is not copied again'
$copied = $filesT::Copy($found5, [Action[string, long, long]]{ param($name, $done, $total) }, $none, [ref]$err)
Check ($copied -eq 1 -and [System.IO.File]::Exists($found5[0].Targets[1])) 'only the second part is copied'
Check ($found5[0].Targets[1].EndsWith('_K1_2_μέρος2.MP4')) ('the second part follows the name of the first: ' + [System.IO.Path]::GetFileName($found5[0].Targets[1]))

# ---- The list of a card by Wi-Fi (Open GoPro /gopro/media/list) and its match.
$goproAsm = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Camera.GoPro.dll'))
$media = $goproAsm.GetType('Kinovea.Camera.GoPro.GoProMedia', $true)
# The list by Wi-Fi gives the clock of the camera (a HERO12 too, as tested).
$cre = [long]($t0.AddMinutes(50).AddSeconds(2) - (New-Object DateTime 1970, 1, 1)).TotalSeconds
$json = '{"id":"1","media":[{"d":"100GOPRO","fs":[{"n":"GX010021.MP4","cre":"' + $cre + '","mod":"' + ($cre + 60) + '","s":"123456"},{"n":"GX010021.THM","cre":"' + $cre + '","s":"10"}]}]}'
$list = $media.GetMethod('ParseList', $all).Invoke($null, @($json))
Check ($list.Count -eq 1 -and $list[0].Name -eq 'GX010021.MP4' -and $list[0].Size -eq 123456 -and $list[0].Folder -eq '100GOPRO') 'the list of the card: the videos, their folder and size'
$takesT::SetNext((Details 'start' '900002' 'ΔΕΥΤΕΡΟ ΠΑΡΑΔΕΙΓΜΑ' 'Α.Ο. Δοκιμής'))
$takesT::Started('K2', $s2, 'HERO12 Black', $t0.AddMinutes(50))
$remote = $filesT::FromRemote($list)
$foundW = $filesT::Match($takesT::Takes, 'K2', $remote)
Check ($foundW.Count -eq 1 -and $foundW[0].Files[0].Name -eq 'GX010021.MP4') 'the video of the card found by Wi-Fi for its recording'

# ---- A HERO12 writes universal time and its time zone: the clock of the camera comes back.
[FakeGoPro]::Write("$work\zone\DCIM\100GOPRO\GX010041.MP4", $t0.AddHours(-3).AddSeconds(1), $s2, 'HERO12 Black', 1000, 13, 180)
$fz = $filesT::Read("$work\zone\DCIM\100GOPRO\GX010041.MP4")
Check ($fz.ZoneMinutes -eq 180 -and [Math]::Abs(($fz.Created - $t0.AddSeconds(1)).TotalSeconds) -lt 1.01) ('universal time + the zone of the camera (TZON 180): ' + $fz.Created.ToString('HH:mm:ss'))
[FakeGoPro]::Write("$work\zone2\DCIM\100GOPRO\GX010042.MP4", $t0.AddHours(2).AddSeconds(1), $s2, 'HERO12 Black', 1000, 14, -300)
$fz2 = $filesT::Read("$work\zone2\DCIM\100GOPRO\GX010042.MP4")
Check ($fz2.ZoneMinutes -eq -300 -and [Math]::Abs(($fz2.Created - $t0.AddHours(-3).AddSeconds(1)).TotalSeconds) -lt 1.01) 'a zone west of Greenwich (TZON -300)'

# ---- A camera in universal time without its zone, recordings three hours apart: one clock for all its files, no file of another hour.
$takesT::SetNext((Details 'turn' '900003' 'ΤΕΤΑΡΤΟ ΠΑΡΑΔΕΙΓΜΑ' 'Ν.Ο. Δοκιμής'))
$tA = $t0.AddHours(2); $tB = $t0.AddHours(5)
$takesT::Started('K4', 'C3441300000004', 'HERO9 Black', $tA); $takesT::Started('K4', 'C3441300000004', 'HERO9 Black', $tB)
$card4 = [System.IO.Path]::Combine($work, 'card4')
[FakeGoPro]::Write("$card4\DCIM\100GOPRO\GX010051.MP4", $tA.AddHours(-3).AddSeconds(1), 'C3441300000004', 'HERO9 Black', 1000, 15)
[FakeGoPro]::Write("$card4\DCIM\100GOPRO\GX010052.MP4", $tB.AddHours(-3).AddSeconds(1), 'C3441300000004', 'HERO9 Black', 1000, 16)
$found4 = $filesT::Match($takesT::Takes, 'K4', $filesT::Scan($card4, $none))
$okA = @($found4 | Where-Object { $_.Take.Start -eq $tA -and $_.Files[0].Name -eq 'GX010051.MP4' }).Count -eq 1
$okB = @($found4 | Where-Object { $_.Take.Start -eq $tB -and $_.Files[0].Name -eq 'GX010052.MP4' }).Count -eq 1
Check ($found4.Count -eq 2 -and $okA -and $okB) 'the clock three hours off for all the files: each recording its own file (not the one of the other hour)'
$takesT::SetNext((Details 'start' '900002' 'ΔΕΥΤΕΡΟ ΠΑΡΑΔΕΙΓΜΑ' 'Α.Ο. Δοκιμής'))

# ---- A recording without its file never takes a file of another hour (one file of another hour proves nothing).
$t9 = $t0.AddMinutes(90)
$takesT::Started('K1', $s1, 'HERO9 Black', $t9)
$card5 = [System.IO.Path]::Combine($work, 'card5')
[FakeGoPro]::Write("$card5\DCIM\100GOPRO\GX010061.MP4", $t9.AddHours(-5).AddSeconds(3), $s1, 'HERO9 Black', 1000, 17)
Check ($filesT::Match($takesT::Takes, 'K1', $filesT::Scan($card5, $none)).Count -eq 0) 'a recording without its file: the file of another hour is not taken'
$takesT::Remove([string[]]@(@($takesT::Takes | Where-Object { $_.Start -eq $t9 })[0].Id))

# ---- A camera that did not start leaves the recording; a recording without cameras goes.
$t7 = $t0.AddMinutes(70)
$takesT::Started('K1', $s1, 'HERO9 Black', $t7); $takesT::Started('K2', $s2, 'HERO12 Black', $t7)
$takesT::Failed('K2', $t7)
$take7 = @($takesT::Takes | Where-Object { $_.Start -eq $t7 })
Check ($take7.Count -eq 1 -and $take7[0].Cameras.Count -eq 1 -and $take7[0].Cameras[0].Alias -eq 'K1') 'the camera that did not start is out of the recording'
$takesT::Failed('K1', $t7)
Check (@($takesT::Takes | Where-Object { $_.Start -eq $t7 }).Count -eq 0) 'no camera left: the recording goes'

# ---- The names stay in Greek with the English interface.
$lang = T 'SwimLang'
$lang.GetProperty('English').SetValue($null, $true)
$filesT::PlanTargets($root, $foundW)
Check ([System.IO.Path]::GetFileName($foundW[0].Targets[0]) -eq '900002_ΔΕΥΤΕΡΟ_ΠΑΡΑΔΕΙΓΜΑ_Εκκίνηση_20261108_K2_2.MP4') ('the name in Greek with the English interface (a second start of the day): ' + [System.IO.Path]::GetFileName($foundW[0].Targets[0]))
Check ($takesT::Next.Summary -eq '900002 ΔΕΥΤΕΡΟ ΠΑΡΑΔΕΙΓΜΑ · Start') ('the button in English: ' + $takesT::Next.Summary)

# ---- The two windows, drawn (fictional details), in English then in Greek.
function Draw($form, [string]$name) {
  if (-not $out) { $form.Dispose(); return }
  $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000)
  $form.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
  $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
  $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
  $b.Save([System.IO.Path]::Combine($out, $name), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
  $form.Close(); $form.Dispose()
}
$transferT = T 'FormGoProTransfer'
$ctor = $transferT.GetConstructor([System.Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@(), $null)
Draw ($ctor.Invoke(@())) 'gt_transfer_en.png'
$lang.GetProperty('English').SetValue($null, $false)
$takesT::Root = $root
Draw ($ctor.Invoke(@())) 'gt_transfer.png'
$detailsForm = (T 'FormGoProTakeDetails').GetConstructor([Type[]]@($detT, [string], [string])).Invoke(@($race, $root, 'K1'))
Draw $detailsForm 'gt_details_race.png'
$detailsForm = (T 'FormGoProTakeDetails').GetConstructor([Type[]]@($detT, [string], [string])).Invoke(@((Details 'turn' '900001' 'Παράδειγμα Αθλητής' 'Ν.Ο. Δοκιμής'), $root, 'K1'))
Draw $detailsForm 'gt_details_turn.png'

try { [System.IO.Directory]::Delete($work, $true) } catch { }
if ($fails -gt 0) { Write-Output ("$fails checks failed"); exit 1 }
Write-Output 'GoPro recordings and videos: all checks passed'
