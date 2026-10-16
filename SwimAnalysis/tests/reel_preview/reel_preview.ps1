# Preview images of the technical view (reel) for the CI, with the fictional races of ReelPreview.cs.
# Windows PowerShell 5.1 (the program is built for the .NET Framework).
#   -Mode render  compiles ReelPreview.cs against the program in -Bin and draws the images in -Out.
#   -Mode emit    writes the images of the matrix part -Part (of -Parts) as notice annotations: base64 in chunks of
#                 -Chunk characters, chunks -First to -First + -Count - 1 (a step writes at most 10 annotations).
#                 Titles: "reel|<image>|<chunk>|<chunks>|<base64 length>", "reel-model|<i>|<n>", "reel-pose|<i>|<n>",
#                 "reel-errors|<i>|<n>".
param(
    [ValidateSet('render', 'emit')] [string]$Mode = 'render',
    [string]$Bin = 'portable',
    [string]$Out = 'reel-preview',
    [int]$Part = 0,
    [int]$Parts = 1,
    [int]$First = 0,
    [int]$Count = 10,
    [int]$Chunk = 3800
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

if ($Mode -eq 'render') {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    $outFull = (Resolve-Path $Out).Path
    try {
        $binFull = (Resolve-Path $Bin).Path
        # The native OpenCV library of the program (OpenCvSharpExtern) is found by the program next to it, not by this host.
        $native = Get-ChildItem -Path $binFull -Recurse -Filter 'OpenCvSharpExtern.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($native) {
            Write-Output ('OpenCvSharpExtern: ' + $native.FullName.Substring($binFull.Length))
            $env:PATH = $native.DirectoryName + ';' + $binFull + ';' + $env:PATH
        }
        $dll = Join-Path $binFull 'ReelPreview.dll'
        $refs = @((Join-Path $binFull 'Kinovea.ScreenManager.dll'))
        foreach ($name in @('Kinovea.Services.dll', 'Newtonsoft.Json.dll', 'OpenCvSharp.dll')) {
            if (Test-Path (Join-Path $binFull $name)) { $refs += (Join-Path $binFull $name) }
        }
        $refs += @('System.Drawing', 'System.Core')
        Add-Type -Path (Join-Path $here 'ReelPreview.cs') -ReferencedAssemblies $refs -OutputAssembly $dll -OutputType Library
        $asm = [System.Reflection.Assembly]::LoadFrom($dll)
        # The charts of the PDF report first (they do not need the races).
        try {
            $sample = [string](Join-Path $here 'charts_sample.json')
            $charts = $asm.GetType('ReelPreview').GetMethod('RenderCharts').Invoke($null, [object[]]@($sample, [string]$outFull))
            Write-Output $charts
        }
        catch {
            $text = $_.Exception.ToString()
            if ($_.Exception.InnerException) { $text = $_.Exception.InnerException.ToString() + "`n" + $text }
            [System.IO.File]::AppendAllText((Join-Path $outFull 'errors.txt'), "charts: " + $text + "`n", [System.Text.Encoding]::UTF8)
            Write-Output $text
        }
        $result = $asm.GetType('ReelPreview').GetMethod('Render').Invoke($null, [object[]]@($outFull))
        Write-Output $result
    }
    catch {
        $text = $_.Exception.ToString()
        if ($_.Exception.InnerException) { $text = $_.Exception.InnerException.ToString() + "`n" + $text }
        [System.IO.File]::AppendAllText((Join-Path $outFull 'errors.txt'), "render: " + $text + "`n", [System.Text.Encoding]::UTF8)
        Write-Output $text
        exit 1
    }

    Get-ChildItem $outFull | Format-Table Name, Length | Out-String | Write-Output
    exit 0
}

# Emit.
if (-not (Test-Path $Out)) {
    Write-Output ('::notice title=reel-errors|1|1::no images (the drawing step did not run)')
    exit 0
}

$outFull = (Resolve-Path $Out).Path
$items = New-Object System.Collections.Generic.List[object]

function Add-Text([string]$kind, [string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return }
    $n = [int][Math]::Ceiling($text.Length / [double]$Chunk)
    for ($i = 0; $i -lt $n; $i++) {
        $start = $i * $Chunk
        $piece = $text.Substring($start, [Math]::Min($Chunk, $text.Length - $start))
        $items.Add(@(($kind + '|' + ($i + 1) + '|' + $n), $piece))
    }
}

if ($Part -eq 0) {
    foreach ($name in @('errors', 'model', 'pose')) {
        $path = Join-Path $outFull ($name + '.txt')
        if (Test-Path $path) { Add-Text ('reel-' + $name) ([System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)) }
    }
}

$framesPath = Join-Path $outFull 'frames.txt'
$frames = @()
if (Test-Path $framesPath) { $frames = @(Get-Content $framesPath | Where-Object { $_.Trim().Length -gt 0 }) }
for ($k = 0; $k -lt $frames.Count; $k++) {
    if (($k % $Parts) -ne $Part) { continue }
    $image = Join-Path $outFull ($frames[$k] + '.jpg')
    if (-not (Test-Path $image)) { continue }
    $b64 = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($image))
    $n = [int][Math]::Ceiling($b64.Length / [double]$Chunk)
    for ($i = 0; $i -lt $n; $i++) {
        $start = $i * $Chunk
        $items.Add(@(('reel|' + $frames[$k] + '|' + ($i + 1) + '|' + $n + '|' + $b64.Length), $b64.Substring($start, [Math]::Min($Chunk, $b64.Length - $start))))
    }
}

Write-Output ('part ' + $Part + ': ' + $items.Count + ' annotations in all, this step ' + $First + ' to ' + ($First + $Count - 1))
$last = [Math]::Min($items.Count, $First + $Count)
for ($i = $First; $i -lt $last; $i++) {
    $title = ([string]$items[$i][0]).Replace('%', '%25').Replace(',', '%2C').Replace(':', '%3A')
    $message = ([string]$items[$i][1]).Replace('%', '%25').Replace("`r", '').Replace("`n", '%0A')
    Write-Output ('::notice title=' + $title + '::' + $message)
}
