param (
    [string]$DestinationDir = "NativeLibs/x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Setting up directories ==="
New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null

$vcpkgRoot = if ($env:VCPKG_INSTALLATION_ROOT) { $env:VCPKG_INSTALLATION_ROOT } else { "C:\vcpkg" }
$binCacheDir = if ($env:VCPKG_DEFAULT_BINARY_CACHE) { $env:VCPKG_DEFAULT_BINARY_CACHE } else { "$PWD/.vcpkg-bincache" }
New-Item -ItemType Directory -Path $binCacheDir -Force | Out-Null

$repoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$tripletsDir = "$repoRoot/triplets"

Write-Host "Using vcpkg at: $vcpkgRoot"
Write-Host "Using vcpkg binary cache at: $binCacheDir"
Write-Host "Using overlay triplets at: $tripletsDir"

Write-Host "=== Compiling / Installing SDL2 & FFmpeg for x64-uwp-dynamic via vcpkg ==="
& "$vcpkgRoot/vcpkg.exe" install `
  sdl2:x64-uwp-dynamic `
  ffmpeg[core,avcodec,swscale,swresample]:x64-uwp-dynamic `
  --overlay-triplets="$tripletsDir" `
  --recurse

if ($LASTEXITCODE -ne 0) {
    throw "vcpkg install failed with exit code $LASTEXITCODE"
}

Write-Host "=== Copying runtime DLLs to $DestinationDir ==="
$vcpkgBin = "$vcpkgRoot/installed/x64-uwp-dynamic/bin"
if (-not (Test-Path $vcpkgBin)) {
    throw "Directory $vcpkgBin not found. vcpkg build output missing."
}

# Copy all runtime DLLs (SDL2, FFmpeg, and any dependencies like zlib)
$dlls = Get-ChildItem -Path $vcpkgBin -Filter "*.dll"
foreach ($f in $dlls) {
    Copy-Item -Path $f.FullName -Destination $DestinationDir -Force
    Write-Host "Copied $($f.Name) to $DestinationDir"
}

# FFmpeg.AutoGen expects FFmpeg 5 names (avcodec-59.dll, avutil-57.dll, swscale-6.dll, swresample-4.dll).
# If modern vcpkg generated newer versioned DLLs (e.g. avcodec-61.dll), create version 5 aliases
# so that LoadPackagedLibrary resolves them under both names.
$aliasMap = @{
    "avcodec*.dll"    = "avcodec-59.dll"
    "avutil*.dll"     = "avutil-57.dll"
    "swscale*.dll"    = "swscale-6.dll"
    "swresample*.dll" = "swresample-4.dll"
}

foreach ($pattern in $aliasMap.Keys) {
    $targetName = $aliasMap[$pattern]
    $match = Get-ChildItem -Path $DestinationDir -Filter $pattern | Where-Object { $_.Name -ne $targetName } | Select-Object -First 1
    if ($match -and (-not (Test-Path "$DestinationDir/$targetName"))) {
        Copy-Item -Path $match.FullName -Destination "$DestinationDir/$targetName" -Force
        Write-Host "Created compatibility alias: $targetName from $($match.Name)"
    }
}

Write-Host "=== Verifying required native DLLs ==="
$required = @("SDL2.dll", "avcodec*.dll", "avutil*.dll", "swscale*.dll", "swresample*.dll")
foreach ($r in $required) {
    $found = Get-ChildItem -Path $DestinationDir -Filter $r -ErrorAction SilentlyContinue
    if (-not $found) {
        throw "Verification failed: $r was not found in $DestinationDir"
    }
}

Write-Host "=== Native dependencies installation complete ==="
Get-ChildItem -Path $DestinationDir
