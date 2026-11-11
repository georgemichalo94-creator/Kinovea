# gc_ci.ps1: the CI check of the video of all the cameras of a recording (2.21.0) on FICTIONAL videos made here by the ffmpeg of the
# build: one scene (a picture that changes in every image, a pool heard: claps, whistles, voices) cut for three cameras that started
# 0, 1.1 and 0.6 s into it, lined up by their sound (to 20 ms), the video of all of them made (1920 × 1080, the part all of them have,
# the sound kept) and its cameras in the same image (each camera's image of the video compared with the first camera's images around it:
# the nearest one is the same image); two cameras without sound lined up by the starts the cameras said (from the command they shared);
# where it goes, the mark of the window of the videos, the drawing over the video. Fictional athletes and clubs.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File gc_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls and ffmpeg.exe> -out <folder>
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
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$none = [System.Threading.CancellationToken]::None
$ci = [System.Globalization.CultureInfo]::InvariantCulture
$ff = [System.IO.Path]::Combine($bin, 'ffmpeg.exe')
Check ([System.IO.File]::Exists($ff)) 'the ffmpeg of the program'

# A folder of its own: the settings (the list of the recordings) and the videos.
$work = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-gc-ci-' + [Guid]::NewGuid().ToString('N'))
$settingsDir = [System.IO.Path]::Combine($work, 'settings') + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
$software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)
$videos = [System.IO.Path]::Combine($work, 'videos')
New-Item -ItemType Directory -Force -Path $videos | Out-Null

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
public static class FictionalScene
{
    // A pool heard (48 kHz, 16 bits, one channel): claps, whistles and voices at random moments (seeded), in quiet noise.
    public static void WriteWav(string path, int seed, double seconds)
    {
        int rate = 48000, n = (int)(seconds * rate);
        double[] x = new double[n];
        Random e = new Random(seed);
        int count = (int)(seconds * 1.6);
        for (int k = 0; k < count; k++)
        {
            double t = e.NextDouble() * seconds;
            int kind = e.Next(3);
            double amp = 0.1 + 0.4 * e.NextDouble();
            double f = kind == 1 ? 2000 + 1000 * e.NextDouble() : 300 + 700 * e.NextDouble();
            double len = kind == 0 ? 0.03 : kind == 1 ? 0.3 : 0.5;
            int s0 = (int)(t * rate);
            Random noise = new Random(e.Next());
            for (int i = 0; i < (int)(len * rate); i++)
            {
                int j = s0 + i;
                if (j < 0 || j >= n)
                    continue;
                double u = i / (double)rate;
                double v;
                if (kind == 0)
                    v = (noise.NextDouble() * 2 - 1) * Math.Exp(-u / 0.008);
                else if (kind == 1)
                    v = Math.Sin(2 * Math.PI * f * u) * Math.Min(1, u / 0.01);
                else
                    v = Math.Sin(2 * Math.PI * f * u + 3 * Math.Sin(2 * Math.PI * 5 * u)) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * u));
                x[j] += amp * v;
            }
        }
        Random r = new Random(seed * 7919 + 17);
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); w.Write(36 + 2 * n);
            w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); w.Write(2 * n);
            for (int i = 0; i < n; i++)
            {
                double g = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
                double v = Math.Max(-1, Math.Min(1, 0.6 * x[i] + 0.01 * g));
                w.Write((short)Math.Round(v * 32000));
            }
        }
    }

    // The mean brightness of the blocks (16 × 16) of a place of an image: the picture of a camera without the noise of the coding.
    public static double[] Blocks(Bitmap b, Rectangle area)
    {
        const int k = 16;
        int bw = area.Width / k, bh = area.Height / k;
        double[] m = new double[bw * bh];
        BitmapData d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            byte[] row = new byte[d.Stride];
            for (int y = 0; y < bh * k; y++)
            {
                Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)(area.Y + y) * d.Stride), row, 0, d.Stride);
                for (int x = 0; x < bw * k; x++)
                {
                    int o = 3 * (area.X + x);
                    m[(y / k) * bw + x / k] += 0.114 * row[o] + 0.587 * row[o + 1] + 0.299 * row[o + 2];
                }
            }
        }
        finally
        {
            b.UnlockBits(d);
        }
        for (int i = 0; i < m.Length; i++)
            m[i] /= k * k;
        return m;
    }

    public static double Difference(double[] a, double[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
            s += Math.Abs(a[i] - b[i]);
        return s / a.Length;
    }

    // Five images in a row of the video of all the cameras: the image of the first camera (the cell at 0, 0) nearest to the middle
    // image of another camera (the cell at x, y), as the shift from the middle one (-2 … 2); the differences in 'details'.
    public static int Shift(string[] frames, int x, int y, out string details)
    {
        // The name of each camera is drawn at the top left of its place: below it only.
        Rectangle first = new Rectangle(20, 90, 920, 430), other = new Rectangle(x + 20, y + 90, 920, 430);
        double[][] firsts = new double[frames.Length][];
        double[] target = null;
        for (int i = 0; i < frames.Length; i++)
        {
            using (Bitmap b = new Bitmap(frames[i]))
            {
                firsts[i] = Blocks(b, first);
                if (i == frames.Length / 2)
                    target = Blocks(b, other);
            }
        }
        int best = 0;
        double bestValue = double.MaxValue;
        details = "";
        for (int i = 0; i < frames.Length; i++)
        {
            double v = Difference(target, firsts[i]);
            details += (i > 0 ? " " : "") + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            if (v < bestValue) { bestValue = v; best = i; }
        }
        return best - frames.Length / 2;
    }
}
'@

# ffmpeg writes its messages on stderr: not errors of this script.
function Native($argList) {
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { return (& $ff @argList 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $old }
}
function Info($path) { return (Native @('-hide_banner', '-nostdin', '-i', $path)) -join "`n" }

# ---- The scene and its three cameras (and two without sound).
Write-Output 'step: fictional videos'
$wav = [System.IO.Path]::Combine($work, 'pool.wav')
[FictionalScene]::WriteWav($wav, 21, 18.0)
$master = [System.IO.Path]::Combine($work, 'scene.mp4')
Native @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', 'mandelbrot=size=640x360:rate=30', '-i', $wav, '-t', '18', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-strict', '-2', '-b:a', '128k', '-y', $master) | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) }
Check ([System.IO.File]::Exists($master)) 'the scene made'
function Cut([string]$name, [double]$from, [bool]$sound) {
  $path = [System.IO.Path]::Combine($videos, $name)
  $argList = @('-nostdin', '-v', 'error', '-i', $master, '-ss', $from.ToString('0.000', $ci), '-t', '14', '-c:v', 'libx264', '-preset', 'ultrafast', '-pix_fmt', 'yuv420p')
  if ($sound) { $argList += @('-c:a', 'aac', '-strict', '-2', '-b:a', '128k') } else { $argList += @('-an') }
  Native ($argList + @('-y', $path)) | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) }
  return $path
}
$clipA = Cut 'K1.mp4' 0.0 $true
$clipB = Cut 'K2.mp4' 1.1 $true
$clipC = Cut 'K3.mp4' 0.6 $true
$clipD = Cut 'K4.mp4' 0.3 $false
$clipE = Cut 'K5.mp4' 0.9 $false
Check ((@($clipA, $clipB, $clipC, $clipD, $clipE) | Where-Object { [System.IO.File]::Exists($_) }).Count -eq 5) 'the videos of the five cameras made'

$takesT = T 'SwimGoProTakes'
$detT = T 'SwimTakeDetails'
$compT = T 'SwimTakeComposite'
$viewT = T 'SwimCompositeView'
function Details([string]$kind, [string]$id, [string]$athlete, [string]$club) {
  $d = [Activator]::CreateInstance($detT)
  $d.Kind = $kind; $d.AthleteId = $id; $d.Athlete = $athlete; $d.Club = $club
  $d.Competition = 'Αγώνες Δοκιμής'; $d.Location = 'Πόλη Δοκιμής'
  return $d
}
function Targets([string]$path) { $l = New-Object 'System.Collections.Generic.List[string]'; $l.Add($path); return ,$l }

# ---- A recording of three cameras (today, so that the window shows it).
$t0 = [DateTime]::Today.AddHours(10)
$takesT::SetNext((Details 'start' '900001' 'Παράδειγμα Αθλητής' 'Ν.Ο. Δοκιμής'))
$takesT::Started('K1', 'C3441300000001', 'HERO9 Black', $t0)
$takesT::Started('K2', 'C3501300000002', 'HERO12 Black', $t0)
$takesT::Started('K3', 'C3441300000003', 'HERO9 Black', $t0)
foreach ($k in @('K1', 'K2', 'K3')) { $takesT::Stopped($k, $t0.AddSeconds(14)) }
$take = @($takesT::Takes | Where-Object { $_.Start -eq $t0 })[0]
Check (-not $compT::Ready($take)) 'before its videos came: nothing to make'
$takesT::MarkDone($take.Id, 'K1', 'GX010001.MP4', (Targets $clipA))
$takesT::MarkDone($take.Id, 'K2', 'GX010002.MP4', (Targets $clipB))
$take = @($takesT::Takes | Where-Object { $_.Start -eq $t0 })[0]
Check (-not $compT::Ready($take)) 'two of the three cameras came: it waits for the third'
$takesT::MarkDone($take.Id, 'K3', 'GX010003.MP4', (Targets $clipC))
$take = @($takesT::Takes | Where-Object { $_.Start -eq $t0 })[0]
Check ($compT::Ready($take)) 'all the videos came: ready to be made by itself'

$views = $compT::Views($take)
Check ($views.Count -eq 3 -and $views[0].Camera -eq 'K1' -and $views[2].Camera -eq 'K3') 'the three cameras in the order of the recording'
$probed = $true
foreach ($v in $views) {
  $err = $null
  $ok = $compT::Probe($ff, $v, [ref]$err)
  Write-Output ('     ' + $v.Camera + ': ' + $v.Duration.ToString('0.000', $ci) + ' s, ' + $v.Fps + ' fps, sound ' + $v.Sound + ' ' + $err)
  $probed = $probed -and $ok -and [Math]::Abs($v.Fps - 30) -lt 0.01 -and [Math]::Abs($v.Duration - 14) -lt 0.1 -and $v.Sound
}
Check $probed 'each video read: 14 s, 30 images per second, with sound'

Write-Output 'step: lined up by the sound'
$compT::LineUp($ff, $views, $none)
foreach ($v in $views) { Write-Output ('     ' + $v.Camera + ': ' + $v.Offset.ToString('0.0000', $ci) + ' s (' + $v.Way + ')') }
Check ($views[0].Way -eq 'first' -and $views[0].Offset -eq 0) 'the first camera: the others are lined up with it'
Check ($views[1].Way -eq 'sound' -and [Math]::Abs($views[1].Offset + 1.1) -le 0.02) ('K2 started 1.1 s later, by the sound: ' + $views[1].Offset.ToString('0.0000', $ci))
Check ($views[2].Way -eq 'sound' -and [Math]::Abs($views[2].Offset + 0.6) -le 0.02) ('K3 started 0.6 s later, by the sound: ' + $views[2].Offset.ToString('0.0000', $ci))
$from = 0.0; $to = 0.0
Check ($compT::Common($views, [ref]$from, [ref]$to) -and [Math]::Abs($from - 1.1) -le 0.03 -and [Math]::Abs($to - 14) -le 0.1) ('the part all of them have: ' + $from.ToString('0.00', $ci) + ' – ' + $to.ToString('0.00', $ci) + ' s of K1')
Check ([Math]::Abs($compT::OutputFps($views) - 30) -lt 1e-6) 'the video at 30 images per second'
$fast = [Activator]::CreateInstance($viewT); $fast.Fps = 100
$slow = [Activator]::CreateInstance($viewT); $slow.Fps = 59.94
$list = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType($viewT))
$list.Add($fast); $list.Add($slow)
Check ([Math]::Abs($compT::OutputFps($list) - 50) -lt 1e-6) 'a camera at 100 images per second: the video at 50'

Write-Output 'step: the video of all the cameras'
$output = $compT::OutputPath($take)
Check ($output -ne $null -and [System.IO.Path]::GetDirectoryName($output) -eq $videos -and [System.IO.Path]::GetFileName($output).EndsWith('_Ενοποιημένο.MP4')) ('beside the video of the first camera: ' + [System.IO.Path]::GetFileName($output))
$made = $null
$watch = [System.Diagnostics.Stopwatch]::StartNew()
$err = $compT::Make($ff, $take, $output, $null, $none, [ref]$made)
Write-Output ('     made in ' + $watch.Elapsed.TotalSeconds.ToString('0.0', $ci) + ' s ' + $err)
Check ($err -eq $null -and [System.IO.File]::Exists($output)) 'the video of all the cameras made'
Check (-not [System.IO.File]::Exists($output + '.part')) 'no temporary file is left'
if ([System.IO.File]::Exists($output)) {
  $info = Info $output
  $video = @($info -split "`n" | Where-Object { $_ -match 'Stream #.*Video:' })
  Write-Output ('     ' + ($video -join ' ').Trim())
  Check ($video.Count -eq 1 -and $video[0] -match '1920x1080' -and $video[0] -match 'h264') 'H.264 in 1920 × 1080'
  Check ($info -match 'Stream #.*Audio:') 'with the sound of the first camera'
  $check = [Activator]::CreateInstance($viewT); $check.Path = $output
  $err2 = $null
  [void]$compT::Probe($ff, $check, [ref]$err2)
  Check ([Math]::Abs($check.Duration - ($to - $from)) -le 0.15) ('as long as the part all of them have: ' + $check.Duration.ToString('0.000', $ci) + ' s')
  # The same moment in all the places: around 3 s and 9 s, the image of each camera against the images of the first one.
  $sameAll = $true
  foreach ($at in @(3.0, 9.0)) {
    $dir = [System.IO.Path]::Combine($work, 'frames' + $at)
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Native @('-nostdin', '-v', 'error', '-ss', ($at - 2.0 / 30).ToString('0.0000', $ci), '-i', $output, '-frames:v', '5', '-y', [System.IO.Path]::Combine($dir, 'f%d.png')) | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) }
    $frames = [string[]](1..5 | ForEach-Object { [System.IO.Path]::Combine($dir, 'f' + $_ + '.png') })
    if (@($frames | Where-Object { [System.IO.File]::Exists($_) }).Count -ne 5) { $sameAll = $false; Write-Output '     (the images were not read)'; continue }
    foreach ($cell in @(@('K2', 0, 540), @('K3', 960, 540))) {
      $d = ''
      $shift = [FictionalScene]::Shift($frames, $cell[1], $cell[2], [ref]$d)
      Write-Output ('     ' + $at + ' s, ' + $cell[0] + ' against K1 (images -2 … +2): ' + $d + ' -> ' + $shift)
      if ($shift -ne 0) { $sameAll = $false }
    }
    if ($out -and $at -eq 3.0) { [System.IO.File]::Copy($frames[2], [System.IO.Path]::Combine($out, 'gc_composite_3cams.png'), $true) }
  }
  Check $sameAll 'the three cameras show the same image of the scene (to the image)'
}

# ---- Two cameras without sound: lined up by the starts the cameras said (from the command they shared).
Write-Output 'step: without sound'
$t1 = $t0.AddMinutes(10)
$takesT::SetNext((Details 'turn' '' '' ''))
$takesT::Started('K4', 'C3441300000004', 'HERO9 Black', $t1)
$takesT::Started('K5', 'C3501300000005', 'HERO12 Black', $t1)
# K5 started 0.6 s after K4 (the scene: K4 from 0.3 s, K5 from 0.9 s).
$takesT::SetStartOffset('K4', $t1, 0.8)
$takesT::SetStartOffset('K5', $t1, 1.4)
$takesT::SetStartOffset('K5', $t1, [double]::NaN)
$takesT::Stopped('K4', $t1.AddSeconds(14)); $takesT::Stopped('K5', $t1.AddSeconds(14))
$take2 = @($takesT::Takes | Where-Object { $_.Start -eq $t1 })[0]
$takesT::MarkDone($take2.Id, 'K4', 'GX010004.MP4', (Targets $clipD))
$takesT::MarkDone($take2.Id, 'K5', 'GX010005.MP4', (Targets $clipE))
$take2 = @($takesT::Takes | Where-Object { $_.Start -eq $t1 })[0]
Check ($take2.Cameras[0].StartOffset -eq 0.8 -and $take2.Cameras[1].StartOffset -eq 1.4) 'the starts of the cameras kept with the recording (not a wrong one)'
$output2 = $compT::OutputPath($take2)
$made2 = $null
$err = $compT::Make($ff, $take2, $output2, $null, $none, [ref]$made2)
Check ($err -eq $null -and [System.IO.File]::Exists($output2)) ('the video of the two cameras without sound made ' + $err)
if ($made2) {
  Write-Output ('     ' + $made2[1].Camera + ': ' + $made2[1].Offset.ToString('0.0000', $ci) + ' s (' + $made2[1].Way + ')')
  Check ($made2[1].Way -eq 'start' -and [Math]::Abs($made2[1].Offset + 0.6) -lt 1e-9) 'K5 lined up by the starts of the cameras: 0.6 s later'
}
if ([System.IO.File]::Exists($output2)) {
  Check (-not ((Info $output2) -match 'Stream #.*Audio:')) 'no sound (the cameras had none)'
  $dir = [System.IO.Path]::Combine($work, 'frames2')
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  Native @('-nostdin', '-v', 'error', '-ss', (5.0 - 2.0 / 30).ToString('0.0000', $ci), '-i', $output2, '-frames:v', '5', '-y', [System.IO.Path]::Combine($dir, 'f%d.png')) | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) }
  $frames = [string[]](1..5 | ForEach-Object { [System.IO.Path]::Combine($dir, 'f' + $_ + '.png') })
  if (@($frames | Where-Object { [System.IO.File]::Exists($_) }).Count -eq 5) {
    $d = ''
    $shift = [FictionalScene]::Shift($frames, 0, 540, [ref]$d)
    Write-Output ('     K5 against K4 (images -2 … +2): ' + $d + ' -> ' + $shift)
    Check ($shift -eq 0) 'the two cameras show the same image of the scene'
    if ($out) { [System.IO.File]::Copy($frames[2], [System.IO.Path]::Combine($out, 'gc_composite_2cams.png'), $true) }
  } else { Check $false 'the images of the video of the two cameras read' }
}

# ---- Lined up by the files when the starts were not measured; not at all without either.
$x = [Activator]::CreateInstance($viewT); $y = [Activator]::CreateInstance($viewT)
$x.Created = $t0.AddSeconds(2); $y.Created = $t0.AddSeconds(1)
$way = ''
Check ([Math]::Abs($compT::ByClocks($x, $y, [ref]$way) - 1) -lt 1e-9 -and $way -eq 'file') 'by the starts written in the files (to the second)'
$y.Created = [DateTime]::MinValue
Check ($compT::ByClocks($x, $y, [ref]$way) -eq 0 -and $way -eq 'none') 'without them: not lined up (and told)'

# ---- The window of the videos marks the recording (✓) and the drawing over the video.
$takesT::SetComposite($take.Id, $output)
$take = @($takesT::Takes | Where-Object { $_.Start -eq $t0 })[0]
Check ($compT::HasComposite($take) -and -not $compT::Ready($take)) 'made: kept with the recording (not made again by itself)'
$takesT::Root = $videos
$transferT = T 'FormGoProTransfer'
$ctor = $transferT.GetConstructor([System.Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@(), $null)
$form = $ctor.Invoke(@())
$grid = $transferT.GetField('grid', [System.Reflection.BindingFlags]'NonPublic,Instance').GetValue($form)
$mark = $null
foreach ($row in $grid.Rows) { if ($row.Tag -and $row.Tag.Id -eq $take.Id) { $mark = $row.Cells[4].Value } }
Check ($mark -eq '✓') ('the recording of today in the window, its video of all the cameras made: ' + $mark)
if ($out) {
  $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000)
  $form.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
  $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
  $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
  $b.Save([System.IO.Path]::Combine($out, 'gc_transfer.png'), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
  $form.Close()
  $over = $compT::DrawOverlay($take, $views)
  $sheet = New-Object System.Drawing.Bitmap 1920, 1080
  $g = [System.Drawing.Graphics]::FromImage($sheet)
  $g.Clear([System.Drawing.Color]::FromArgb(70, 90, 110)); $g.DrawImage($over, 0, 0, 1920, 1080); $g.Dispose()
  $sheet.Save([System.IO.Path]::Combine($out, 'gc_overlay.png'), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png)
  $sheet.Dispose(); $over.Dispose()
}
$form.Dispose()

try { [System.IO.Directory]::Delete($work, $true) } catch { }
if ($fails -gt 0) { Write-Output ("$fails checks failed"); exit 1 }
Write-Output 'Video of all the cameras: all checks passed'
