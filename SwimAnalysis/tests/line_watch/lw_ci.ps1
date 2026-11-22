# lw_ci.ps1: the CI check of the line watch of the wall of cameras (2.28.0) on SYNTHETIC underwater side views: a swimmer of known
# shape (thin arms in front of the head, the shoulders the thickest) passes the line of a camera at a known time, with the noise of a
# camera, the light on the bottom, the bubbles behind him (and a cloud of them in front of him); the time the watch gives for his
# head on the line, the way he goes, his places for the speed, and the small grey image of 24 and 32 bit pictures.
# Windows PowerShell 5.1 (.NET Framework, as Kinovea). Exit code 1 when a check fails.
#   powershell -File lw_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
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
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }

$scene = @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Kinovea.ScreenManager;

public static class LineScene
{
    public const int W = 640, H = 360;
    public const double Cross = 1.30;
    static readonly double[,] Profile = { { 0.00, 0.15, 0.06 }, { 0.15, 0.45, 0.085 }, { 0.45, 0.70, 0.22 }, { 0.70, 1.10, 0.29 },
        { 1.10, 1.40, 0.24 }, { 1.40, 1.80, 0.17 }, { 1.80, 2.20, 0.11 }, { 2.20, 2.45, 0.08 } };
    const double HeadBack = 0.45;

    // Gaussian noise from three uniforms.
    static double Noise(Random r) { return (r.NextDouble() + r.NextDouble() + r.NextDouble() - 1.5) * 2.0; }

    public static float[] Render(double t, double v, int way, double ppm, double swimmer, double lineFrac, double[,] bubbles, double noise, Random r)
    {
        float[] img = new float[W * H];
        double top = 150, bottom = 95, linePx = lineFrac * W;
        for (int y = 0; y < H; y++)
        {
            double water = top + (bottom - top) * y / (H - 1);
            for (int x = 0; x < W; x++)
            {
                double g = water;
                if (y >= 18 && y < 24) g += ((x / 24) % 2 == 0) ? 60 : -30;
                if (y >= (int)(H * 0.86) && y < (int)(H * 0.89)) g -= 35;
                img[y * W + x] = (float)g;
            }
        }

        for (int k = 0; k < 3; k++)
        {
            int yc = (int)(H * (0.70 + 0.06 * k) + 6 * Math.Sin(2 * Math.PI * (0.7 * t + k * 0.3)));
            for (int y = yc; y < yc + 3 && y < H; y++)
                for (int x = 0; x < W; x++)
                    img[y * W + x] += (float)((Math.Sin(x / 37.0 + t * 5 + k) * 0.5 + 0.5) * 28);
        }

        double headPx = linePx + way * (t - Cross) * v * ppm;
        double tipsPx = headPx + way * HeadBack * ppm;
        double axis = H * 0.42;
        for (int p = 0; p < Profile.GetLength(0); p++)
        {
            for (double s = Profile[p, 0]; s < Profile[p, 1]; s += 1.0 / ppm)
            {
                int x = (int)Math.Round(tipsPx - way * s * ppm);
                if (x < 0 || x >= W) continue;
                double off = s > 1.2 ? 0.16 * ((s - 1.2) / 1.25) * Math.Sin(2 * Math.PI * 2.2 * t - s * 2) : 0;
                double yc = axis + off * ppm, half = Profile[p, 2] * ppm / 2;
                for (int y = Math.Max(0, (int)(yc - half)); y < Math.Min(H, (int)(yc + half)); y++)
                    img[y * W + x] = (float)swimmer;
            }
        }

        if (bubbles != null)
        {
            for (int b = 0; b < bubbles.GetLength(0); b++)
            {
                if (t < bubbles[b, 3]) continue;
                int bx = (int)bubbles[b, 0], by = (int)(bubbles[b, 1] - (t - bubbles[b, 3]) * 40), rr = (int)bubbles[b, 2];
                if (bx < 0 || bx >= W || by < 0 || by >= H) continue;
                for (int y = Math.Max(0, by - rr); y < Math.Min(H, by + rr); y++)
                    for (int x = Math.Max(0, bx - rr); x < Math.Min(W, bx + rr); x++)
                        img[y * W + x] = 235;
            }
        }

        for (int i = 0; i < img.Length; i++)
            img[i] = (float)Math.Max(0, Math.Min(255, img[i] + noise * Noise(r)));

        return img;
    }

    public static Bitmap ToBitmap(float[] img, PixelFormat format)
    {
        Bitmap bmp = new Bitmap(W, H, format);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, format);
        int step = format == PixelFormat.Format24bppRgb ? 3 : 4;
        byte[] row = new byte[data.Stride];
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                byte g = (byte)Math.Round(img[y * W + x]);
                row[x * step] = g; row[x * step + 1] = g; row[x * step + 2] = g;
                if (step == 4) row[x * step + 3] = 255;
            }
            Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), data.Stride);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    // One pass of a swimmer before a camera: the error of the time of his head on the line (NaN: not found).
    public static double Run(int way, double v, double swimmer, double lineFrac, double fps, int seed, bool trail, bool cloud, double noise, int direction, out SwimLineWatch watch)
    {
        Random r = new Random(seed);
        double ppm = 200, linePx = lineFrac * W;
        int count = (trail ? 60 : 0) + (cloud ? 80 : 0);
        double[,] bubbles = new double[count, 4];
        int n = 0;
        if (trail)
        {
            for (int k = 0; k < 60; k++, n++)
            {
                double tb = Cross - 0.9 + k * 0.03;
                double hx = linePx + way * (tb - Cross) * v * ppm - way * 1.1 * ppm;
                bubbles[n, 0] = hx + Noise(r) * 15; bubbles[n, 1] = H * 0.42 - 15 + Noise(r) * 12; bubbles[n, 2] = 2 + r.Next(4); bubbles[n, 3] = tb;
            }
        }
        if (cloud)
        {
            for (int k = 0; k < 80; k++, n++)
            {
                bubbles[n, 0] = linePx + way * (40 + Noise(r) * 60); bubbles[n, 1] = H * 0.30 + Noise(r) * 25; bubbles[n, 2] = 2 + r.Next(5); bubbles[n, 3] = Cross - 0.6 + r.NextDouble() * 0.3;
            }
        }

        SwimLineWatch.SharedDirection = 0;
        watch = new SwimLineWatch();
        watch.Line = (float)lineFrac;
        watch.Direction = direction;
        Random jitter = new Random(seed + 7);
        for (double t = -1.0; t < Cross + 1.6; t += 1.0 / fps + (jitter.NextDouble() - 0.5) * 0.006)
        {
            using (Bitmap bmp = ToBitmap(Render(t, v, way, ppm, swimmer, lineFrac, bubbles, noise, r), PixelFormat.Format24bppRgb))
                watch.Feed(bmp, t);
        }

        return watch.Crossed - Cross;
    }

    // The speed of the head from the places of the watch near the line (widths of the image per second).
    public static double Speed(SwimLineWatch watch)
    {
        double sumT = 0, sumX = 0, sumTT = 0, sumTX = 0; int n = 0;
        foreach (SwimLineSample s in watch.Samples)
        {
            if (Math.Abs(s.Seconds - Cross) > 0.25) continue;
            sumT += s.Seconds; sumX += s.Head; sumTT += s.Seconds * s.Seconds; sumTX += s.Seconds * s.Head; n++;
        }
        if (n < 3) return double.NaN;
        return (n * sumTX - sumT * sumX) / (n * sumTT - sumT * sumT);
    }

    // (2.30.0) The images of a recorded file of a camera as PNG files for ffmpeg: a swimmer crossing the line Cross s after the horn,
    // the horn at the frame 'hornFrame' of the file.
    public static int WriteFrames(string folder, double fps, double hornFrame, double seconds, double v, int way, double noise, int seed)
    {
        Random r = new Random(seed);
        int n = (int)Math.Round(seconds * fps);
        for (int k = 0; k < n; k++)
        {
            double t = (k - hornFrame) / fps;
            using (Bitmap bmp = ToBitmap(Render(t, v, way, 200, 40, 0.5, null, noise, r), PixelFormat.Format24bppRgb))
                bmp.Save(System.IO.Path.Combine(folder, string.Format("f{0:00000}.png", k)), ImageFormat.Png);
        }
        return n;
    }

    // The small grey images of the same picture in 24 and 32 bits: the largest difference.
    public static double SmallSame()
    {
        Random r = new Random(3);
        float[] img = Render(1.2, 2.2, 1, 200, 40, 0.5, null, 3, r);
        int h = (int)Math.Round(160.0 * H / W);
        float[] a = new float[160 * h], b = new float[160 * h];
        using (Bitmap b24 = ToBitmap(img, PixelFormat.Format24bppRgb))
        using (Bitmap b32 = ToBitmap(img, PixelFormat.Format32bppArgb))
        {
            if (!SwimLineWatch.Small(b24, a, 160, h) || !SwimLineWatch.Small(b32, b, 160, h)) return double.NaN;
        }
        double worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return worst;
    }
}
'@
Add-Type -TypeDefinition $scene -ReferencedAssemblies @($sm, 'System.Drawing') -Language CSharp

function Pass([string]$what, [int]$way, [double]$v, [double]$swimmer, [double]$line, [double]$fps, [int]$seed, [bool]$trail, [bool]$cloud, [double]$noise, [int]$direction, [double]$tolerance) {
  $w = $null
  $e = [LineScene]::Run($way, $v, $swimmer, $line, $fps, $seed, $trail, $cloud, $noise, $direction, [ref]$w)
  $ok = -not [double]::IsNaN($e) -and [Math]::Abs($e) -le $tolerance -and $w.Way -eq $way
  Check $ok ($what + ': head on the line ' + $(if ([double]::IsNaN($e)) { 'not found' } else { '{0:+0.000;-0.000} s' -f $e }) + ', way ' + $w.Way)
  $script:watch = $w
}

Pass 'swimmer to the right, 2.2 m/s, 30 images/s' 1 2.2 40 0.5 30 1 $true $false 3 0 0.02
$wp = $script:watch
$speed = [LineScene]::Speed($wp)
Check ([Math]::Abs($speed - 2.2 * 200 / 640) -le 0.06) ('his speed from the places of the head: {0:0.000} widths/s (truth {1:0.000})' -f $speed, (2.2 * 200 / 640))
Check ($wp.Samples.Count -ge 20) ('places of the head kept after the horn: ' + $wp.Samples.Count)
Check ([LineScene]::Run(1, 2.2, 40, 0.5, 30, 1, $true, $false, 3, 0, [ref]$null) -eq [LineScene]::Run(1, 2.2, 40, 0.5, 30, 1, $true, $false, 3, 0, [ref]$null)) 'the same images give the same time'
Pass 'swimmer to the left' (-1) 2.2 40 0.5 30 1 $true $false 3 0 0.02
Pass 'slow, 1.6 m/s' 1 1.6 40 0.5 30 2 $true $false 3 0 0.02
Pass 'fast, 3.0 m/s' 1 3.0 40 0.5 30 3 $true $false 3 0 0.02
Pass 'lighter than the water' 1 2.2 215 0.5 30 4 $true $false 3 0 0.02
Pass 'line left of the middle' 1 2.2 40 0.42 30 5 $true $false 3 0 0.02
Pass '25 images/s' 1 2.2 40 0.5 25 6 $true $false 3 0 0.02
Pass 'noisy camera' 1 2.2 40 0.5 30 7 $true $false 8 0 0.02
Pass 'low contrast' 1 2.2 110 0.5 30 8 $true $false 3 0 0.03
Pass 'bubbles in front of him' 1 2.2 40 0.5 30 9 $true $true 3 0 0.04
Pass 'the way given' (-1) 2.2 40 0.5 30 10 $true $false 3 (-1) 0.02

# Before the horn nothing is kept; without a swimmer nothing is found.
$w0 = $null
$e0 = [LineScene]::Run(1, 2.2, 40, 0.5, 30, 11, $false, $false, 3, 0, [ref]$w0)
Check (-not [double]::IsNaN($e0)) 'without bubbles'
$rnd = New-Object System.Random 5
$still = New-Object Kinovea.ScreenManager.SwimLineWatch
for ($i = 0; $i -lt 60; $i++) {
  $bmp = [LineScene]::ToBitmap([LineScene]::Render(-5.0 + $i / 30, 2.2, 1, 200, 40, 0.5, $null, 3, $rnd), [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
  $still.Feed($bmp, $i / 30)
  $bmp.Dispose()
}
Check ([double]::IsNaN($still.Crossed) -and -not $still.Swimmer) 'only water: no swimmer, no time'

$same = [LineScene]::SmallSame()
Check ($same -le 1.0) ('the small grey image of 24 and 32 bit pictures: largest difference {0:0.00}' -f $same)

$reset = $wp
$reset.Reset()
Check ([double]::IsNaN($reset.Crossed) -and $reset.Samples.Count -eq 0) 'a new attempt forgets the time and the places'

# ---- (2.30.0) The line read again in a recorded file: a fictional camera of 50 images/s made by the ffmpeg of the build, the horn at
# its frame 40.5, a swimmer at 2.2 m/s on the line 1.30 s after the horn; read as Kinovea reads a video.
$ff = Join-Path $bin 'ffmpeg.exe'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('swim_lw_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
try {
  $fps = 50.0; $hornFrame = 40.5
  $n = [LineScene]::WriteFrames($tmp, $fps, $hornFrame, 3.6, 2.2, 1, 3, 11)
  $video = Join-Path $tmp 'camera.avi'
  $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
  try { & $ff -nostdin -v error -framerate 50 -i (Join-Path $tmp 'f%05d.png') -c:v mjpeg -q:v 2 -y $video 2>&1 | ForEach-Object { Write-Output ('info ffmpeg: ' + $_) } } finally { $ErrorActionPreference = $old }
  if (-not (Test-Path $video)) { Check $false 'the fictional recorded file made by ffmpeg' } else {
    $env:PATH = $bin + ';' + $env:PATH
    $va = [System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Kinovea.Video.dll'))
    $fa = [System.Reflection.Assembly]::LoadFrom((Join-Path $bin 'Kinovea.Video.FFMpeg.dll'))
    $readers = New-Object 'System.Collections.Generic.List[Type]'
    $readers.Add($fa.GetType('Kinovea.Video.FFMpeg.VideoReaderFFMpeg', $true))
    # (The objects of New-Object come wrapped for PowerShell: Invoke wants them as they are.)
    $list = $readers.psobject.BaseObject
    [void]$va.GetType('Kinovea.Video.VideoTypeManager', $true).GetMethod('LoadVideoReaders', [Type[]]@($list.GetType())).Invoke($null, [object[]]@(,$list))
    $read = $a.GetType('Kinovea.ScreenManager.SwimLineFile', $true).GetMethod('Read')
    # As after a live attempt: the line seen at about its time, the way known.
    $w = $read.Invoke($null, [object[]]@([string]$video, [double]$hornFrame, [double]$fps, [single]0.5, [int]1, [double]1.32))
    $e = $(if ($w -eq $null) { [double]::NaN } else { $w.Crossed - [LineScene]::Cross })
    Check ($w -ne $null -and -not [double]::IsNaN($e) -and [Math]::Abs($e) -le 0.02) ('the line read again in the file: head on the line ' + $(if ([double]::IsNaN($e)) { 'not found' } else { '{0:+0.000;-0.000} s' -f $e }) + ' (' + $n + ' images)')
    $places = $(if ($w -ne $null) { $w.Samples.Count } else { 0 })
    Check ($places -ge 40) ('every image of the file: ' + $places + ' places of the head')
    # The line not seen live: read from the horn.
    $w2 = $read.Invoke($null, [object[]]@([string]$video, [double]$hornFrame, [double]$fps, [single]0.5, [int]0, [double]::NaN))
    $e2 = $(if ($w2 -eq $null) { [double]::NaN } else { $w2.Crossed - [LineScene]::Cross })
    Check ($w2 -ne $null -and -not [double]::IsNaN($e2) -and [Math]::Abs($e2) -le 0.02) ('the line not seen live, read from the horn: ' + $(if ([double]::IsNaN($e2)) { 'not found' } else { '{0:+0.000;-0.000} s' -f $e2 }))
    $none = $read.Invoke($null, [object[]]@([string](Join-Path $tmp 'missing.avi'), [double]$hornFrame, [double]$fps, [single]0.5, [int]1, [double]1.32))
    Check ($none -eq $null) 'a file that is not there: nothing read'
  }
} catch { Check $false ('the line read again in a file: ' + $_.Exception.ToString()) } finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }

if ($fails -gt 0) { Write-Output ('FAILED ' + $fails + ' line watch checks'); exit 1 }
Write-Output 'all line watch checks passed'
