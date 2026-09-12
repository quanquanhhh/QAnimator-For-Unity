param([string]$FfmpegDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$bundle = Join-Path $repo 'dist/QAnimatorEncoder'
if (-not $FfmpegDirectory) {
    $candidate = Get-ChildItem (Join-Path $repo 'tools/ffmpeg') -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($candidate) { $FfmpegDirectory = $candidate.DirectoryName }
}
if (-not $FfmpegDirectory -or -not (Test-Path (Join-Path $FfmpegDirectory 'ffprobe.exe'))) {
    throw 'Provide -FfmpegDirectory containing ffmpeg.exe and ffprobe.exe (static Windows build).'
}
dotnet publish (Join-Path $repo 'Encoder/QAnimator.Encoder.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $bundle
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $FfmpegDirectory 'ffmpeg.exe'), (Join-Path $FfmpegDirectory 'ffprobe.exe') -Destination $bundle -Force
$ffmpegRoot = Split-Path $FfmpegDirectory -Parent
foreach ($name in @('LICENSE', 'README.txt')) {
    $file = Join-Path $ffmpegRoot $name
    if (Test-Path $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $bundle "FFmpeg-$name") -Force }
}
Write-Host "Ready: $bundle/QAnimatorEncoder.exe"
