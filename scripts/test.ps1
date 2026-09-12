param([string]$FfmpegDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $FfmpegDirectory) {
    $FfmpegDirectory = (Get-ChildItem (Join-Path $repo 'tools/ffmpeg') -Filter ffmpeg.exe -Recurse | Select-Object -First 1).DirectoryName
}
$env:QANIMATOR_FFMPEG_DIR = $FfmpegDirectory
# Allows a machine with a newer SDK/runtime to run the net8 test host.
$env:DOTNET_ROLL_FORWARD = 'Major'
Push-Location $repo
try {
    dotnet run --project Tests/QAnimator.CodecSmoke -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Codec tests failed' }
    foreach ($pair in @(@('treasure_chest_open.mp4', 'treasure_chest_open.bytes'), @('treasure_chest_open_alpha.webm', 'treasure_chest_open_alpha.bytes'))) {
        dotnet run --project Tests/QAnimator.CodecSmoke -c Release -- --integration $pair[0] $pair[1]
        if ($LASTEXITCODE -ne 0) { throw 'Pixel integration test failed' }
    }
    dotnet run --project Tests/QAnimator.DesktopSmoke -c Release -- treasure_chest_open.mp4 artifacts/desktop-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Desktop tests failed' }
}
finally { Pop-Location }
