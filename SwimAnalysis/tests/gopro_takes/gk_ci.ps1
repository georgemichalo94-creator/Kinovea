# gk_ci.ps1: the CI check of the continuous recordings (2.22.0) on FICTIONAL videos made here by the ffmpeg of the build: an order of
# four fictional athletes, «⏭ Επόμενος» pressed three times (a press too soon is the same press, a press undone), the parts of the
# recording (one per athlete, the last one to the end); two cameras of one scene (a picture that changes in every image, a pool heard):
# K1 started 1.0 s after the command, its recording in two files of its card (GX010101, GX020101: a long recording), K2 started 1.5 s
# after it; their files found on their cards, cut without coding for each athlete (a part over the two files of K1 joined), each video in
# the folder of its athlete with his name, the length of each part, the image of the scene at a second of a part (the one the starts of
# the cameras and the key images say), the start of each part's video kept; the video of all the cameras of a part lined up by the sound,
# and by the kept starts when there is no sound; the window of the videos shows the parts, not the recording; the details with the order;
# a pasted list read. Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File gk_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls and ffmpeg.exe> -out <folder>
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

# A folder of its own: the settings (the list of the recordings), the cards and the videos.
$work = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'kv-gk-ci-' + [Guid]::NewGuid().ToString('N'))
$settingsDir = [System.IO.Path]::Combine($work, 'settings') + [System.IO.Path]::DirectorySeparatorChar
New-Item -ItemType Directory -Force -Path $settingsDir | Out-Null
$software = [System.Reflection.Assembly]::LoadFrom([System.IO.Path]::Combine($bin, 'Kinovea.Services.dll')).GetType('Kinovea.Services.Software', $true)
$software.GetProperty('SettingsDirectory').SetValue($null, $settingsDir)
$root = [System.IO.Path]::Combine($work, 'videos')
$card1 = [System.IO.Path]::Combine($work, 'cards', 'K1', 'DCIM', '100GOPRO')
$card2 = [System.IO.Path]::Combine($work, 'cards', 'K2', 'DCIM', '100GOPRO')
foreach ($d in @($root, $card1, $card2)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
public static class FictionalPool2
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

    // The mean brightness of the blocks (16 × 16) of a place of an image.
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

    // The samples of a WAV (16 bits, one channel) at another rate (linear), as the sound read from a video.
    public static float[] ToRate(byte[] wav, int from, int to)
    {
        int n = (wav.Length - 44) / 2;
        float[] x = new float[n];
        for (int i = 0; i < n; i++)
            x[i] = BitConverter.ToInt16(wav, 44 + 2 * i) / 32768f;
        int m = (int)((long)n * to / from);
        float[] y = new float[m];
        double ratio = from / (double)to;
        int half = ratio > 1.5 ? (int)Math.Floor(ratio / 2) : 0;
        for (int j = 0; j < m; j++)
        {
            double p = j * ratio;
            int i = (int)p;
            if (half > 0)
            {
                // From a higher rate: the mean of the samples around (a low pass: less of what is above the new half rate folds back).
                double sum = 0;
                int count = 0;
                for (int k = Math.Max(0, i - half); k <= Math.Min(n - 1, i + half); k++)
                {
                    sum += x[k];
                    count++;
                }
                y[j] = (float)(sum / Math.Max(1, count));
                continue;
            }
            double f = p - i;
            y[j] = (float)(i + 1 < n ? x[i] * (1 - f) + x[i + 1] * f : x[Math.Min(i, n - 1)]);
        }
        return y;
    }

    // A fictional WAV of two channels (16 bits) for the own sounds of the start signal.
    public static void WriteStereo(string path, double[] left, double[] right, int rate)
    {
        int n = left.Length;
        using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + 4 * n);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16); w.Write((short)1); w.Write((short)2);
            w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(4 * n);
            for (int i = 0; i < n; i++)
            {
                w.Write((short)Math.Round(32767 * Math.Max(-1, Math.Min(1, left[i]))));
                w.Write((short)Math.Round(32767 * Math.Max(-1, Math.Min(1, right[i]))));
            }
        }
    }

    // A fictional horn: two square tones together (low and high), silence before and after.
    public static double[] Horn2(int rate, double before, double length, double after, double low, double high)
    {
        int n = (int)((before + length + after) * rate);
        double[] x = new double[n];
        int a = (int)(before * rate), b = (int)((before + length) * rate);
        for (int i = a; i < b; i++)
        {
            double t = (i - a) / (double)rate;
            x[i] = 0.4 * Math.Sign(Math.Sin(2 * Math.PI * low * t)) + 0.4 * Math.Sign(Math.Sin(2 * Math.PI * high * t));
        }
        return x;
    }

    // Fictional words: bursts of a voice of 125 Hz (its 7th and 8th harmonics 875 and 1000 Hz, 8/7 apart as the tones of a horn)
    // and a low rumble after them (40 dB under: not words).
    public static double[] Voice2(int rate, double[][] bursts, double seconds)
    {
        int n = (int)(seconds * rate);
        double[] x = new double[n];
        foreach (double[] burst in bursts)
        {
            int a = (int)(burst[0] * rate), b = Math.Min(n, (int)(burst[1] * rate));
            for (int i = a; i < b; i++)
            {
                double t = i / (double)rate, s = 0;
                for (int k = 1; k <= 20; k++)
                    s += Math.Sin(2 * Math.PI * k * 125 * t) / k;
                double edge = Math.Min(1, Math.Min((i - a) / (0.02 * rate), (b - 1 - i) / (0.02 * rate)));
                x[i] = 0.35 * edge * s;
            }
        }
        double end = bursts[bursts.Length - 1][1];
        for (int i = (int)(end * rate); i < n; i++)
            x[i] += 0.006 * Math.Sin(2 * Math.PI * 30 * i / (double)rate);
        return x;
    }

    // The signal as a camera at the pool hears it from the speaker: lower, with the noise of its microphone (never digital silence).
    public static float[] Heard(float[] x, int seed)
    {
        Random r = new Random(seed);
        float[] y = new float[x.Length];
        for (int i = 0; i < x.Length; i++)
        {
            double g = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
            y[i] = (float)(0.5 * x[i] + 0.01 * g);
        }
        return y;
    }

    // An image (a whole frame) against five images in a row of another video: the nearest one, as the shift from the middle (-2 … 2).
    public static int Nearest(string target, string[] frames, Rectangle area, out string details)
    {
        double[] t;
        using (Bitmap b = new Bitmap(target))
            t = Blocks(b, area);
        int best = 0;
        double bestValue = double.MaxValue;
        details = "";
        for (int i = 0; i < frames.Length; i++)
        {
            double v;
            using (Bitmap b = new Bitmap(frames[i]))
                v = Difference(t, Blocks(b, area));
            details += (i > 0 ? " " : "") + v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            if (v < bestValue) { bestValue = v; best = i; }
        }
        return best - frames.Length / 2;
    }

    // In five images of a video of all the cameras: the image of another camera (at x, y) against the first camera's (at 0, 0).
    public static int Shift(string[] frames, int x, int y, out string details)
    {
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

function Native($argList) {
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { return (& $ff @argList 2>&1 | ForEach-Object { "$_" }) } finally { $ErrorActionPreference = $old }
}
function Say($lines) { $lines | ForEach-Object { Write-Output ('  ffmpeg: ' + $_) } }

# ---- The scene and the files of the two cards (key images every second, no B images: as a GoPro).
Write-Output 'step: fictional cards'
$t0 = [DateTime]::Today.AddHours(10)
$wav = [System.IO.Path]::Combine($work, 'pool.wav')
[FictionalPool2]::WriteWav($wav, 33, 47.0)
$scene = [System.IO.Path]::Combine($work, 'scene.mp4')
Say (Native @('-nostdin', '-v', 'error', '-f', 'lavfi', '-i', 'mandelbrot=size=640x360:rate=30', '-i', $wav, '-t', '47', '-c:v', 'libx264', '-preset', 'ultrafast', '-g', '30', '-bf', '0', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-strict', '-2', '-b:a', '128k', '-y', $scene))
function Camera([string]$path, [double]$from, [double]$length, [DateTime]$created) {
  Say (Native @('-nostdin', '-v', 'error', '-i', $scene, '-ss', $from.ToString('0.000', $ci), '-t', $length.ToString('0.000', $ci), '-c:v', 'libx264', '-preset', 'ultrafast', '-g', '30', '-keyint_min', '30', '-sc_threshold', '0', '-bf', '0', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-strict', '-2', '-b:a', '128k', '-metadata', ('creation_time=' + $created.ToString('yyyy-MM-dd HH:mm:ss', $ci)), '-y', $path))
}
# K1: the scene from 0 s, 44 s, in two files of 24 s and 20 s (as a GoPro cuts a long recording); K2: the scene from 0.5 s, 43 s.
$k1a = [System.IO.Path]::Combine($card1, 'GX010101.MP4')
$k1b = [System.IO.Path]::Combine($card1, 'GX020101.MP4')
Camera $k1a 0.0 24.0 $t0.AddSeconds(1)
Camera $k1b 24.0 20.0 $t0.AddSeconds(25)
$k2 = [System.IO.Path]::Combine($card2, 'GX010202.MP4')
Camera $k2 0.5 43.0 $t0.AddSeconds(2)
Check ((@($k1a, $k1b, $k2) | Where-Object { [System.IO.File]::Exists($_) }).Count -eq 3) 'the files of the two cards made'

$filesT = T 'SwimGoProFiles'
$keysA = $filesT::KeyframeTimes($k1a)
$video = 0.0
$keysB = $filesT::KeyframeTimes($k1b, [ref]$video)
Write-Output ('     key images of GX010101: ' + (($keysA | Select-Object -First 4 | ForEach-Object { $_.ToString('0.000', $ci) }) -join ' ') + ' … (' + $keysA.Length + ')')
Check ($keysA.Length -eq 24 -and [Math]::Abs($keysA[0]) -lt 1e-6 -and [Math]::Abs($keysA[23] - 23) -lt 1e-6) 'the key images of a file read: every second'
Check ([Math]::Abs($video - 20) -lt 0.05) ('the length of the picture of the second file: ' + $video.ToString('0.000', $ci) + ' s')

# ---- The order, the recording, the presses.
Write-Output 'step: the order and «⏭ Επόμενος»'
$takesT = T 'SwimGoProTakes'
$detT = T 'SwimTakeDetails'
$athT = T 'SwimTakeAthlete'
$d = [Activator]::CreateInstance($detT)
$d.Kind = 'turn'; $d.Competition = 'Προπόνηση Δοκιμής'; $d.Location = 'Πόλη Δοκιμής'; $d.Date = $t0.ToString('d.M.yyyy', $ci)
$people = @(@('900101', 'Παράδειγμα Πρώτος', 'Ν.Ο. Δοκιμής'), @('900102', 'Παράδειγμα Δεύτερη', 'Ο.Α. Δοκιμής'), @('900103', 'Παράδειγμα Τρίτος', 'Ν.Ο. Δοκιμής'), @('900104', 'Παράδειγμα Τέταρτη', 'Κ.Ο. Δοκιμής'))
foreach ($p in $people) { $x = [Activator]::CreateInstance($athT); $x.AthleteId = $p[0]; $x.Athlete = $p[1]; $x.Club = $p[2]; $d.Queue.Add($x) }
$takesT::Root = $root
$takesT::SetNext($d)
Check ($takesT::Next.IsContinuous -and $takesT::Next.Summary.Contains('4')) ('the next recordings: ' + $takesT::Next.Summary)
$takesT::Started('K1', 'C3441300000101', 'HERO9 Black', $t0)
$takesT::Started('K2', 'C3501300000202', 'HERO12 Black', $t0)
$takesT::SetStartOffset('K1', $t0, 1.0)
$takesT::SetStartOffset('K2', $t0, 1.5)
$open = $null; $count = 0
$rec = $takesT::Continuous([ref]$open, [ref]$count)
Check ($rec -ne $null -and $open -ne $null -and $open.Index -eq 1 -and $open.Details.Athlete -eq 'Παράδειγμα Πρώτος' -and $open.Cameras.Count -eq 2) 'recording: the first athlete of the order in the water, with both cameras'
$msg = ''
# The ends of the attempts (on the clock of the computer): the scene at 10 s, 21 s and 30 s (K1 started 1 s after the command).
Check ($takesT::NextAthlete($t0.AddSeconds(11), [ref]$msg)) ('press 1: ' + $msg)
Check (-not $takesT::NextAthlete($t0.AddSeconds(12), [ref]$msg)) ('a press 1 s later is the same press: ' + $msg)
Check ($takesT::NextAthlete($t0.AddSeconds(22), [ref]$msg)) ('press 2: ' + $msg)
Check ($takesT::NextAthlete($t0.AddSeconds(26), [ref]$msg)) ('a press too early: ' + $msg)
Check ($takesT::UndoNextAthlete([ref]$msg) -and ($takesT::Parts($rec.Id)).Count -eq 3) ('undone: ' + $msg)
Check ($takesT::NextAthlete($t0.AddSeconds(31), [ref]$msg)) ('press 3: ' + $msg)
$rec = $takesT::Continuous([ref]$open, [ref]$count)
Check ($open -ne $null -and $open.Index -eq 4 -and $open.Details.AthleteId -eq '900104' -and $count -eq 4) 'the fourth athlete in the water'
$takesT::Stopped('K1', $t0.AddSeconds(45.5))
$takesT::Stopped('K2', $t0.AddSeconds(46))
$rec = $takesT::Continuous([ref]$open, [ref]$count)
Check ($rec -eq $null) 'stopped: no continuous recording goes on'
$recording = @($takesT::Takes | Where-Object { $_.Start -eq $t0 -and -not $_.IsPart })[0]
$parts = @($takesT::Parts($recording.Id))
Write-Output ('     parts: ' + (($parts | ForEach-Object { '#' + $_.Index + ' ' + $_.Start.ToString('HH:mm:ss', $ci) + '–' + $_.Stop.ToString('HH:mm:ss', $ci) + ' ' + $_.Details.AthleteId + ($(if ($_.EndedByButton) { ' (button)' } else { ' (end)' })) }) -join ' | '))
Check ($parts.Count -eq 4 -and $parts[0].Start -eq $t0 -and $parts[1].Start -eq $t0.AddSeconds(11) -and $parts[2].Stop -eq $t0.AddSeconds(31) -and -not $parts[3].EndedByButton -and $parts[2].EndedByButton) 'four parts: from press to press, the last to the end'
Check ($recording.Marks.Count -eq 3) 'three presses kept'

# ---- The cards: found, cut for each athlete.
Write-Output 'step: the cards cut'
$cutT = T 'SwimGoProCut'
$takes = $takesT::Takes
$jobs = New-Object System.Collections.ArrayList
$matches = New-Object System.Collections.ArrayList
$taken = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($c in @(@('K1', ([System.IO.Path]::Combine($work, 'cards', 'K1'))), @('K2', ([System.IO.Path]::Combine($work, 'cards', 'K2'))))) {
  $files = $filesT::Scan($c[1], $none)
  $found = $filesT::Match($takes, $c[0], $files)
  Write-Output ('     ' + $c[0] + ': ' + $files.Count + ' files, ' + $found.Count + ' recording, ' + $(if ($found.Count -gt 0) { $found[0].Files.Count } else { 0 }) + ' files of it')
  foreach ($m in $found) {
    [void]$matches.Add($m)
    foreach ($j in $cutT::Plan($root, $m, $takesT::Parts($m.Take.Id), $taken)) { [void]$jobs.Add($j) }
  }
}
Check ($matches.Count -eq 2 -and $matches[0].Take.Id -eq $recording.Id -and $matches[0].Files.Count -eq 2) 'the continuous recording found on both cards (K1: its two files)'
$jobList = [Activator]::CreateInstance([System.Collections.Generic.List``1].MakeGenericType((T 'SwimCutJob')))
foreach ($j in $jobs) { $jobList.Add($j) }
Check ($jobList.Count -eq 8) ('a video for each athlete and camera: ' + $jobList.Count)
# The first image of each part's video after the command (its key image), its length, its pieces: K1 started 1.0 s after the command,
# K2 1.5 s; key images every second of each file.
$expected = @{ 'K1#1' = @(1.0, 11.0, 1); 'K1#2' = @(10.0, 13.0, 1); 'K1#3' = @(21.0, 11.0, 2); 'K1#4' = @(30.0, 15.0, 1); 'K2#1' = @(1.5, 10.5, 1); 'K2#2' = @(9.5, 13.5, 1); 'K2#3' = @(20.5, 11.5, 1); 'K2#4' = @(29.5, 15.0, 1) }
$planOk = $true
foreach ($j in $jobList) {
  $key = $j.Alias + '#' + $j.Part.Index
  $e = $expected[$key]
  Write-Output ('     ' + $key + ': from ' + $j.StartFromCommand.ToString('0.000', $ci) + ' s after the command, ' + $j.Length.ToString('0.000', $ci) + ' s, ' + $j.Pieces.Count + ' piece(s) -> ' + [System.IO.Path]::GetFileName($j.Target))
  if ([Math]::Abs($j.StartFromCommand - $e[0]) -gt 0.01 -or [Math]::Abs($j.Length - $e[1]) -gt 0.02 -or $j.Pieces.Count -ne $e[2]) { $planOk = $false }
}
Check $planOk 'each part from the key image at or before its start (a second before the press), to a second after the next one'
$err = $null
$cut = $cutT::Run($ff, $jobList, $null, $none, [ref]$err)
Write-Output ('     cut: ' + $cut + ' ' + $err)
Check ($cut -eq 8 -and $err -eq $null) 'the eight videos cut without coding'
foreach ($m in $matches) { $cutT::Finish($m) }
$recording = @($takesT::Takes | Where-Object { $_.Id -eq $recording.Id })[0]
Check ($recording.AllDone) 'the recording came: all its cameras'
$parts = @($takesT::Parts($recording.Id))
Check (@($parts | Where-Object { $_.AllDone }).Count -eq 4) 'each part came, with both cameras'
$k1p3 = ($parts[2].Cameras | Where-Object { $_.Alias -eq 'K1' })[0]
$k2p3 = ($parts[2].Cameras | Where-Object { $_.Alias -eq 'K2' })[0]
Check ([Math]::Abs($k1p3.StartOffset - 21.0) -lt 0.01 -and [Math]::Abs($k2p3.StartOffset - 20.5) -lt 0.01) ('the starts of the videos of part 3 kept: ' + $k1p3.StartOffset + ' and ' + $k2p3.StartOffset)
$path = $k1p3.Targets[0]
$rel = $path.Substring($root.Length)
Write-Output ('     ' + $rel)
Check ($rel -match '\\Ν\.Ο\. ΔΟΚΙΜΗΣ\\900103_ΠΑΡΑΔΕΙΓΜΑ_ΤΡΙΤΟΣ\\900103_ΠΑΡΑΔΕΙΓΜΑ_ΤΡΙΤΟΣ_Στροφή_\d{8}_K1\.MP4$') 'in the folder of the athlete, with his name'
$lengthsOk = $true
foreach ($p in $parts) {
  foreach ($c in $p.Cameras) {
    $f = $filesT::Read($c.Targets[0])
    $e = $expected[$c.Alias + '#' + $p.Index]
    Write-Output ('     ' + $c.Alias + '#' + $p.Index + ': ' + $f.Duration.ToString('0.000', $ci) + ' s (' + $e[1] + ')')
    if ([Math]::Abs($f.Duration - $e[1]) -gt 0.2) { $lengthsOk = $false }
  }
}
Check $lengthsOk 'the length of each video (to 0.2 s)'
Check (-not (Get-ChildItem -Path $root -Recurse -Filter '*.part' | Select-Object -First 1)) 'no temporary file is left'

# The image of the scene at a second of a part: K1 part 3 at 5 s is the scene at 25 s (over its two files), K2 part 3 at 5.5 s too.
function Frame([string]$video, [double]$at, [string]$file) { Say (Native @('-nostdin', '-v', 'error', '-ss', $at.ToString('0.0000', $ci), '-i', $video, '-frames:v', '1', '-y', $file)) }
$dir = [System.IO.Path]::Combine($work, 'frames')
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Say (Native @('-nostdin', '-v', 'error', '-ss', (25.0 - 2.0 / 30).ToString('0.0000', $ci), '-i', $scene, '-frames:v', '5', '-y', [System.IO.Path]::Combine($dir, 's%d.png')))
$sceneFrames = [string[]](1..5 | ForEach-Object { [System.IO.Path]::Combine($dir, 's' + $_ + '.png') })
$area = New-Object System.Drawing.Rectangle 0, 0, 640, 352
foreach ($c in @(@($k1p3, 5.0), @($k2p3, 5.5))) {
  $target = [System.IO.Path]::Combine($dir, $c[0].Alias + '.png')
  Frame $c[0].Targets[0] $c[1] $target
  $det = ''
  $shift = if ([System.IO.File]::Exists($target)) { [FictionalPool2]::Nearest($target, $sceneFrames, $area, [ref]$det) } else { 99 }
  Write-Output ('     ' + $c[0].Alias + ' part 3 at ' + $c[1] + ' s against the scene at 25 s (images -2 … +2): ' + $det + ' -> ' + $shift)
  Check ($shift -eq 0) ($c[0].Alias + ': the image the start of the part says')
}

# ---- The video of all the cameras of a part.
Write-Output 'step: the video of all the cameras of a part'
$compT = T 'SwimTakeComposite'
$part2 = $parts[1]
$output = $compT::OutputPath($part2)
$made = $null
$err = $compT::Make($ff, $part2, $output, $null, $none, [ref]$made)
Check ($err -eq $null -and [System.IO.File]::Exists($output)) ('the video of the two cameras of part 2 made ' + $err)
if ($made) {
  Write-Output ('     K2: ' + $made[1].Offset.ToString('0.0000', $ci) + ' s (' + $made[1].Way + ')')
  Check ($made[1].Way -eq 'sound' -and [Math]::Abs($made[1].Offset - 0.5) -le 0.02) 'K2 lined up by the sound: 0.5 s ahead in its part'
}
if ([System.IO.File]::Exists($output)) {
  $fdir = [System.IO.Path]::Combine($work, 'frames2')
  New-Item -ItemType Directory -Force -Path $fdir | Out-Null
  Say (Native @('-nostdin', '-v', 'error', '-ss', (6.0 - 2.0 / 30).ToString('0.0000', $ci), '-i', $output, '-frames:v', '5', '-y', [System.IO.Path]::Combine($fdir, 'f%d.png')))
  $frames = [string[]](1..5 | ForEach-Object { [System.IO.Path]::Combine($fdir, 'f' + $_ + '.png') })
  if (@($frames | Where-Object { [System.IO.File]::Exists($_) }).Count -eq 5) {
    $det = ''
    $shift = [FictionalPool2]::Shift($frames, 0, 540, [ref]$det)
    Write-Output ('     K2 against K1 (images -2 … +2): ' + $det + ' -> ' + $shift)
    Check ($shift -eq 0) 'the two cameras of the part show the same image of the scene'
    if ($out) { [System.IO.File]::Copy($frames[2], [System.IO.Path]::Combine($out, 'gk_composite_part2.png'), $true) }
  } else { Check $false 'the images of the video of the part read' }
}
# Without sound (under the water): by the starts of the videos of the part kept at the cut.
$views = $compT::Views($parts[2])
foreach ($v in $views) { $e2 = $null; [void]$compT::Probe($ff, $v, [ref]$e2); $v.Sound = $false }
$compT::LineUp($ff, $views, $none)
Check ($views[1].Way -eq 'start' -and [Math]::Abs($views[1].Offset - 0.5) -lt 1e-6) ('without sound, by the kept starts: ' + $views[1].Offset.ToString('0.000', $ci) + ' (' + $views[1].Way + ')')

# The sound of one part lines up the others of the same recording (the test of 12.11.2026: a camera that did not hear the same as
# the others in a part without the horn was lined up by its start measured by Bluetooth, 0.09 s off). K2's measured start 0.08 s
# late in parts 2 and 3: part 2 made again (its sound: 0.5 s) keeps what it says of the starts; part 3 without sound by it: 0.5 s.
foreach ($p in @($parts[1], $parts[2])) {
  $c = @($p.Cameras | Where-Object { $_.Alias -eq 'K2' })[0]
  $takesT::SetPartStart($p.Id, 'K2', $c.StartOffset + 0.08)
}
$parts = @($takesT::Parts($recording.Id))
$made = $null
$err = $compT::Make($ff, $parts[1], $compT::OutputPath($parts[1]), $null, $none, [ref]$made)
Check ($err -eq $null -and $made -ne $null -and $made[1].Way -eq 'sound' -and [Math]::Abs($made[1].Offset - 0.5) -le 0.02) ('part 2 again, by its sound: ' + $(if ($made) { $made[1].Offset.ToString('0.000', $ci) + ' (' + $made[1].Way + ')' } else { $err }))
$parts = @($takesT::Parts($recording.Id))
$clock = $parts[1].Clock
Write-Output ('     what the sound of part 2 says of the starts: ' + $(if ($clock) { (($clock.GetEnumerator() | Sort-Object Key | ForEach-Object { $_.Key + ' ' + $_.Value.ToString('+0.000;-0.000', $ci) }) -join ', ') } else { 'nothing' }))
Check ($clock -ne $null -and $clock.ContainsKey('K1') -and $clock.ContainsKey('K2') -and [Math]::Abs($clock['K2'] - $clock['K1'] + 0.08) -le 0.02) 'kept with part 2: K2 started 0.08 s before its measured start'
$views = $compT::Views($parts[2])
foreach ($v in $views) { $e2 = $null; [void]$compT::Probe($ff, $v, [ref]$e2); $v.Sound = $false }
$compT::LineUp($ff, $views, $none, $parts[2])
Check ($views[1].Way -eq 'clock' -and [Math]::Abs($views[1].Offset - 0.5) -le 0.02) ('part 3 without sound, by the sound of part 2: ' + $views[1].Offset.ToString('0.000', $ci) + ' (' + $views[1].Way + '; by its measured start alone: 0.420)')

# ---- The window of the videos: the parts, not the recording; the details with the order; a pasted list.
$transferT = T 'FormGoProTransfer'
$ctor = $transferT.GetConstructor([System.Reflection.BindingFlags]'NonPublic,Instance', $null, [Type[]]@(), $null)
$form = $ctor.Invoke(@())
$grid = $transferT.GetField('grid', [System.Reflection.BindingFlags]'NonPublic,Instance').GetValue($form)
$rows = @($grid.Rows | ForEach-Object { $_.Tag })
Check ($rows.Count -eq 4 -and @($rows | Where-Object { $_.IsPart }).Count -eq 4 -and @($rows | Where-Object { $_.Id -eq $recording.Id }).Count -eq 0) ('the window shows the four parts, not the recording: ' + $rows.Count)
Check ([string]$grid.Rows[0].Cells[2].Value -like '#4*') ('the last part first: ' + $grid.Rows[0].Cells[2].Value)
if ($out) {
  $form.StartPosition = 'Manual'; $form.Location = New-Object System.Drawing.Point(-4000, -4000)
  $form.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
  $b = New-Object System.Drawing.Bitmap $form.Width, $form.Height
  $form.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $form.Width, $form.Height))
  $b.Save([System.IO.Path]::Combine($out, 'gk_transfer.png'), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
  $form.Close()
}
$form.Dispose()

$detailsT = T 'FormGoProTakeDetails'
$df = [Activator]::CreateInstance($detailsT, @($takesT::Next, $root, 'K1', $true))
if ($out) {
  $df.StartPosition = 'Manual'; $df.Location = New-Object System.Drawing.Point(-4000, -4000)
  $df.Show(); ('System.Windows.Forms.Application' -as [type])::DoEvents()
  $b = New-Object System.Drawing.Bitmap $df.Width, $df.Height
  $df.DrawToBitmap($b, (New-Object System.Drawing.Rectangle 0, 0, $df.Width, $df.Height))
  $b.Save([System.IO.Path]::Combine($out, 'gk_details.png'), ('System.Drawing.Imaging.ImageFormat' -as [type])::Png); $b.Dispose()
  $df.Close()
}
$df.Dispose()
$lines = "900201`tΠαράδειγμα Πέμπτος`tΝ.Ο. Δοκιμής`r`n900202 Παράδειγμα Έκτη`r`n`r`nΠαράδειγμα Έβδομος;Ο.Α. Δοκιμής"
$read = $detailsT::ReadAthleteLines($lines, $null)
Write-Output ('     pasted: ' + (($read | ForEach-Object { $_.ToString() }) -join ' | '))
Check ($read.Count -eq 3 -and $read[0].AthleteId -eq '900201' -and $read[0].Club -eq 'Ν.Ο. Δοκιμής' -and $read[1].AthleteId -eq '900202' -and $read[1].Athlete -eq 'Παράδειγμα Έκτη' -and $read[2].Athlete -eq 'Παράδειγμα Έβδομος' -and $read[2].Club -eq 'Ο.Α. Δοκιμής') 'a pasted list read: number, name, club'

# ---- The start signal: the horn heard as a start signal by the start analysis, at its place; the words when Windows has a voice.
Write-Output 'step: the start signal'
$sigT = T 'SwimStartSignal'
$hornAt = 0.0
$wave = $sigT::Build([int16[]]@(), $sigT::Pause, [ref]$hornAt)
$samples = [FictionalPool2]::Heard([FictionalPool2]::ToRate($wave, $sigT::Rate, 16000), 5)
$beeps = (T 'SwimStartSound')::Find($samples)
$b0 = if ($beeps.Count -gt 0) { $beeps[0] } else { $null }
if ($b0) { Write-Output ('     heard: ' + $b0.Onset.ToString('0.000', $ci) + ' s (the horn at ' + $hornAt.ToString('0.000', $ci) + ' s), ' + $b0.Low.ToString('0', $ci) + '/' + $b0.High.ToString('0', $ci) + ' Hz, score ' + $b0.Score.ToString('0.0', $ci)) }
Check ($b0 -ne $null -and [Math]::Abs($b0.Onset - $hornAt) -le 0.06) 'the horn found by the start analysis at its moment'
Check ($b0 -ne $null -and [Math]::Abs($b0.Low - 912) -le 30 -and [Math]::Abs($b0.High - 1043) -le 30) 'its two tones (912 and 1043 Hz, as the start systems and TEMPLO)'
# The horn short as at the meets, made as that of TEMPLO (asked: 11-12.11.2026): its length and level (RMS) of the scale, 1 s after the words.
$horn = $sigT::Horn()
$sq = 0.0
foreach ($s in $horn) { $sq += [double]$s * [double]$s }
$rms = [Math]::Sqrt($sq / $horn.Length) / 32767.0
Write-Output ('     the horn: ' + ($horn.Length / [double]$sigT::Rate).ToString('0.00', $ci) + ' s, level ' + $rms.ToString('0.000', $ci) + ' of the scale (that of TEMPLO: 0.33), pause ' + $sigT::Pause + ' s')
Check ($rms -ge 0.30 -and $horn.Length -le [int](0.26 * $sigT::Rate) -and $sigT::Pause -eq 1.0) 'the horn short as at the meets (0.25 s, its level at least 0.30 of the scale), 1 s after the words'
$words = $sigT::Voice()
Write-Output ('     the words of Windows: ' + ($words.Length / [double]$sigT::Rate).ToString('0.00', $ci) + ' s')
$full = $sigT::Build($words, $sigT::Pause, [ref]$hornAt)
Check ($full.Length -gt $wave.Length -or $words.Length -eq 0) 'the words before the pause (when Windows has a voice)'
# The words without the band of the horn: the start analysis hears only the horn as a start signal, not the voice before it.
$beeps = (T 'SwimStartSound')::Find([FictionalPool2]::Heard([FictionalPool2]::ToRate($full, $sigT::Rate, 16000), 6))
Write-Output ('     with the words, heard: ' + (($beeps | Select-Object -First 3 | ForEach-Object { $_.Onset.ToString('0.000', $ci) + ' s ' + $_.Low.ToString('0', $ci) + '/' + $_.High.ToString('0', $ci) + ' Hz score ' + $_.Score.ToString('0.0', $ci) }) -join ' | ') + ' (the horn at ' + $hornAt.ToString('0.000', $ci) + ' s)')
Check ($beeps.Count -gt 0 -and [Math]::Abs($beeps[0].Onset - $hornAt) -le 0.06) 'with the words before it: the horn the clearest start signal, at its moment'
if ($out) { [System.IO.File]::WriteAllBytes([System.IO.Path]::Combine($out, 'gk_signal.wav'), $full) }

# The own sounds of the analyst (12.11.2026: those of TEMPLO, at 44.1 kHz, two channels): fictional ones in the folder of the settings,
# read where they are. The words from their first sound to their last (the rumble after them out), without the band of the horn; the
# horn from its first sound; the signal with them: the horn heard at its moment with its tones, the clearest with the words before it.
$r = $sigT::Rate
Check ($sigT::Sounds() -eq 'words: voice of Windows; horn: made') ('no own sounds: ' + $sigT::Sounds())
$bursts = [double[][]]@([double[]]@(0.20, 0.60), [double[]]@(0.80, 1.20), [double[]]@(1.40, 1.70))
$v = [FictionalPool2]::Voice2($r, $bursts, 2.4)
[FictionalPool2]::WriteStereo([System.IO.Path]::Combine($settingsDir, 'SwimStartWords.wav'), $v, $v, $r)
$h = [FictionalPool2]::Horn2($r, 0.08, 0.25, 0.4, 915, 1046)
[FictionalPool2]::WriteStereo([System.IO.Path]::Combine($settingsDir, 'SwimStartHorn.wav'), $h, $h, $r)
Write-Output ('     own sounds: ' + $sigT::Sounds())
$ownWords = $sigT::WordsSound()
$ownHorn = $sigT::HornSound()
$wordsLength = $ownWords.Length / [double]$r
$hornLength = $ownHorn.Length / [double]$r
Write-Output ('     own words ' + $wordsLength.ToString('0.000', $ci) + ' s (their sounds: 1.500 s), own horn ' + $hornLength.ToString('0.000', $ci) + ' s (0.250 s)')
Check ($sigT::Sounds() -eq 'words: SwimStartWords.wav; horn: SwimStartHorn.wav') 'the own sounds found in the folder of the settings'
Check ([Math]::Abs($wordsLength - 1.50) -le 0.04) 'the own words from their first sound to their last (the rumble after them out)'
Check ($hornLength -ge 0.25 -and $hornLength -le 0.26) 'the own horn from its first sound'
$ownAt = 0.0
$ownWave = $sigT::Build($ownWords, $ownHorn, $sigT::Pause, [ref]$ownAt)
$beeps = (T 'SwimStartSound')::Find([FictionalPool2]::Heard([FictionalPool2]::ToRate($ownWave, $r, 16000), 7))
Write-Output ('     with the own sounds, heard: ' + (($beeps | Select-Object -First 3 | ForEach-Object { $_.Onset.ToString('0.000', $ci) + ' s ' + $_.Low.ToString('0', $ci) + '/' + $_.High.ToString('0', $ci) + ' Hz score ' + $_.Score.ToString('0.0', $ci) }) -join ' | ') + ' (the horn at ' + $ownAt.ToString('0.000', $ci) + ' s)')
Check ($beeps.Count -gt 0 -and [Math]::Abs($beeps[0].Onset - $ownAt) -le 0.06 -and [Math]::Abs($beeps[0].Low - 915) -le 30 -and [Math]::Abs($beeps[0].High - 1046) -le 30) 'the own horn heard at its moment with its tones, the clearest with the own words before it'
[System.IO.File]::Delete([System.IO.Path]::Combine($settingsDir, 'SwimStartWords.wav'))
[System.IO.File]::Delete([System.IO.Path]::Combine($settingsDir, 'SwimStartHorn.wav'))
Check ($sigT::Sounds() -eq 'words: voice of Windows; horn: made') 'without them again: the voice of Windows and the made horn'

# ---- The end of the session; the parts out of the list, the recording too.
$takesT::EndSession()
Check (-not $takesT::Next.IsContinuous) 'the end of the session: no order for the next recordings'
$takesT::Remove([string[]]@($parts | ForEach-Object { $_.Id }))
Check (@($takesT::Takes | Where-Object { $_.Id -eq $recording.Id -or $_.ParentId -eq $recording.Id }).Count -eq 0) 'its parts out of the list: the recording too'

try { [System.IO.Directory]::Delete($work, $true) } catch { }
if ($fails -gt 0) { Write-Output ("$fails checks failed"); exit 1 }
Write-Output 'Continuous recordings: all checks passed'
