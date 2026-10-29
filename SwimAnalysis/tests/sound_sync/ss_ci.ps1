# ss_ci.ps1: the CI check of the sync of cameras by their sound (SwimSoundSync, 2.14.1) on FICTIONAL sounds: a pool heard by two
# cameras (the second later by 1.2345 s, muffled, with its own noise and sounds of its own) must line up to the millisecond; a camera
# that heard something else must not. Windows PowerShell 5.1 (.NET Framework, as Kinovea) or PowerShell 7. Exit code 1 when a check fails.
#   powershell -File ss_ci.ps1 -sm <Kinovea.ScreenManager.dll> -bin <folder of the other dlls>
param([string]$sm, [string]$bin)
$ErrorActionPreference = 'Stop'
$sm = (Resolve-Path $sm).Path; $bin = (Resolve-Path $bin).Path
Add-Type -TypeDefinition @'
using System;
public static class FictionalPool
{
    // A pool heard for 'seconds' at 8 kHz: claps, whistles and voices at random moments (seeded), in quiet noise.
    public static float[] Hear(int seed, double seconds, double delay, double noise, double muffle, int ownSeed, int ownEvents)
    {
        int rate = 8000, n = (int)(seconds * rate);
        double[] x = new double[n];
        Random events = new Random(seed);
        AddEvents(x, events, 60, delay, rate);
        if (ownEvents > 0)
            AddEvents(x, new Random(ownSeed), ownEvents, 0, rate);
        Random r = new Random(seed * 7919 + 17 + ownSeed);
        float[] y = new float[n];
        double lp = 0;
        for (int i = 0; i < n; i++)
        {
            lp = muffle * lp + (1 - muffle) * x[i];
            double g = Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());
            y[i] = (float)(lp + noise * g);
        }
        return y;
    }

    private static void AddEvents(double[] x, Random e, int count, double delay, int rate)
    {
        int n = x.Length;
        for (int k = 0; k < count; k++)
        {
            double t = e.NextDouble() * (n / (double)rate) + delay;
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
    }
}
'@
# The other DLLs of the program from its folder. Plain string work only: in Windows PowerShell a handler that loads anything else
# (New-Object, cmdlets) can be asked again for the resources of what it loads, and so on until the stack ends.
$script:resolving = $false
[System.AppDomain]::CurrentDomain.add_AssemblyResolve({ param($s, $e)
  $n = $e.Name.Split(',')[0]
  if ($script:resolving -or $n.EndsWith('.resources')) { return $null }
  $p = [System.IO.Path]::Combine($bin, $n + '.dll')
  if (-not [System.IO.File]::Exists($p)) { return $null }
  $script:resolving = $true
  try { return [System.Reflection.Assembly]::LoadFrom($p) } finally { $script:resolving = $false } })
$a = [System.Reflection.Assembly]::LoadFrom($sm)
$sync = $a.GetType('Kinovea.ScreenManager.SwimSoundSync', $true)
$fails = 0
function Check($ok, $what) { if ($ok) { Write-Output ('ok   ' + $what) } else { Write-Output ('FAIL ' + $what); $script:fails++ } }
$match = $sync.GetMethod('Match')
Write-Output 'step: fictional sounds'
$first = [FictionalPool]::Hear(11, 60, 0, 0.01, 0.0, 0, 0)
# The same pool 1.2345 s later on the second camera, muffled (as under the water), noisier, with 20 sounds of its own.
$second = [FictionalPool]::Hear(11, 60, 1.2345, 0.03, 0.85, 5, 20)
Write-Output 'step: match'
$m = $match.Invoke($null, @($first, $second, 30.0))
Write-Output ('second camera: lag ' + [Math]::Round($m.LagSeconds, 4) + ' s, reliable ' + $m.Reliable + ', clarity ' + [Math]::Round($m.Clarity, 1) + '/' + [Math]::Round($m.ClarityHigh, 1) + ', spread ' + [Math]::Round($m.Spread, 4) + ', check ' + [Math]::Round($m.Check, 4))
Check ($m.Reliable) 'the second camera matches'
Check ([Math]::Abs($m.LagSeconds - 1.2345) -le 0.002) 'its clock 1.2345 s later, to 2 ms'
# The other way: the first camera is earlier.
$back = $match.Invoke($null, @($second, $first, 30.0))
Check ($back.Reliable -and [Math]::Abs($back.LagSeconds + 1.2345) -le 0.002) ('the other way round: ' + [Math]::Round($back.LagSeconds, 4))
# A camera that heard another pool.
Write-Output 'step: other pool'
$other = [FictionalPool]::Hear(99, 60, 0, 0.01, 0.0, 0, 0)
$o = $match.Invoke($null, @($first, $other, 30.0))
Write-Output ('other pool: lag ' + [Math]::Round($o.LagSeconds, 3) + ' s, reliable ' + $o.Reliable + ', clarity ' + [Math]::Round($o.Clarity, 1) + '/' + [Math]::Round($o.ClarityHigh, 1) + ', spread ' + [Math]::Round($o.Spread, 3))
Check (-not $o.Reliable) 'a camera that heard something else does not match'
# Only noise.
Write-Output 'step: quiet'
$quiet = [FictionalPool]::Hear(3, 60, 0, 0.02, 0.0, 0, 0)
for ($i = 0; $i -lt $quiet.Length; $i++) { $quiet[$i] = [float](0.02 * [Math]::Sin($i * 0.37)) }
$q = $match.Invoke($null, @($first, $quiet, 30.0))
Check (-not $q.Reliable) 'a camera without sounds does not match'
$tiny = [float[]]::new(100)
Check ($null -eq ($match.Invoke($null, @($first, $tiny, 30.0)))) 'too little sound: nothing'
Write-Output ('fails ' + $fails)
if ($fails -gt 0) { exit 1 }
exit 0
