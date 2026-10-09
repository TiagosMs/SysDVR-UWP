param (
    [string]$DestinationDir = "NativeLibs/x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Setting up directories ==="
New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null

$vcpkgRoot = if ($env:VCPKG_INSTALLATION_ROOT) { $env:VCPKG_INSTALLATION_ROOT } else { "C:\vcpkg" }
$binCacheDir = if ($env:VCPKG_DEFAULT_BINARY_CACHE) { $env:VCPKG_DEFAULT_BINARY_CACHE } else { "$PWD/.vcpkg-bincache" }
New-Item -ItemType Directory -Path $binCacheDir -Force | Out-Null

Write-Host "Using vcpkg at: $vcpkgRoot"
Write-Host "Using vcpkg binary cache at: $binCacheDir"

Write-Host "=== Compiling / Installing SDL2 & FFmpeg for x64-uwp via vcpkg ==="
& "$vcpkgRoot/vcpkg.exe" install `
  sdl2:x64-uwp `
  ffmpeg[core,avcodec,swscale,swresample]:x64-uwp `
  --recurse

if ($LASTEXITCODE -ne 0) {
    throw "vcpkg install failed with exit code $LASTEXITCODE"
}

Write-Host "=== Copying runtime DLLs to $DestinationDir ==="
$vcpkgBin = "$vcpkgRoot/installed/x64-uwp/bin"
$patterns = @("SDL2.dll", "avcodec*.dll", "avutil*.dll", "swscale*.dll", "swresample*.dll")

foreach ($pattern in $patterns) {
    $files = Get-ChildItem -Path $vcpkgBin -Filter $pattern -ErrorAction SilentlyContinue
    foreach ($f in $files) {
        Copy-Item -Path $f.FullName -Destination $DestinationDir -Force
        Write-Host "Copied $($f.Name) to $DestinationDir"
    }
}

# Verification
$sdl = Get-ChildItem -Path $DestinationDir -Filter "SDL2.dll" -ErrorAction SilentlyContinue
if (-not $sdl) {
    throw "Verification failed: SDL2.dll was not found in $DestinationDir"
}

Write-Host "=== Native dependencies installation complete ==="
Get-ChildItem -Path $DestinationDir
